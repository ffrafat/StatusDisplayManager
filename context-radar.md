# Context: adding a plane-radar screen

Carried over from `D:\FFR Personal Code\geekmagic-generic-companion` (2026-10-28), where the radar mode of
[giovi321/smalltv-mod](https://github.com/giovi321/smalltv-mod) was tested on a SmallTV Ultra. The source is cloned at
`geekmagic-generic-companion\base\giovi321_smalltv-mod\src\features\radar\` (`RadarClient.cpp` = data,
`RadarMode.cpp` = drawing). This file explains how the same feature maps onto this app.

## Why this app is a better home for it than the SmallTV

On the SmallTV (ESP8266) the radar never fetched anything: each HTTPS request needs a 16 KB contiguous heap block and
the device had 5.2 KB. This app runs on the PC, so the fetch is just a normal `urllib` call in `backend.py`, and the
480×320 landscape screen has room for a scope *and* a side list, which the 240×240 square does not.

## Data sources (all free, no API key), tested 2026-10-28

| Feed | URL | Near Dhaka (23.8, 90.4) | Notes |
|---|---|---|---|
| adsb.lol | `https://api.adsb.lol/v2/lat/{lat}/lon/{lon}/dist/{nm}` | 1 aircraft within 100 nm, 34 within 250 nm | Plain JSON, `{"ac":[...]}`. Works with Python's default User-Agent. No CORS header, so fetch from `backend.py`, not from the renderer. Data under ODbL. |
| adsb.fi | `https://opendata.adsb.fi/api/v2/lat/{lat}/lon/{lon}/dist/{nm}` | 0 within 250 nm | Same `{"ac":[...]}` shape. **Returns 403 to `Python-urllib`**; send a custom `User-Agent`. Non-commercial use. |
| airplanes.live | `https://api.airplanes.live/v2/point/{lat}/{lon}/{nm}` | 0 within 100 nm | Same `ac` shape. |
| OpenSky Network | `https://opensky-network.org/api/states/all?lamin=&lomin=&lamax=&lomax=` | **10 in a ~100 km box** | Best coverage here. Different shape: `states` is an array of arrays (index 1 callsign, 5 lon, 6 lat, 7 baro altitude in **metres**, 8 on_ground, 9 velocity in **m/s**, 10 true_track). Daily credit limit (anonymous is low; a free account gets more), so poll every 30–60 s, not 10 s. Check their current limits before shipping. |

**Coverage is the real constraint, not cost.** All of these are volunteer receiver networks, and Bangladesh has very
few feeders, so a 20 km radar will often be empty. Plan for it: default to OpenSky (or adsb.lol + OpenSky merged by
`hex`/icao24), default range ~100 km, and give the empty state a proper design ("No aircraft in range", last-seen
time). A cheap RTL-SDR dongle + antenna feeding adsb.lol would fix coverage locally, and adsb.lol then serves your own
receiver's data back for free.

Fields the radar needs per aircraft (adsb.lol/fi/airplanes.live names): `lat`, `lon`, `track` (deg, 0 = N),
`gs` (knots), `flight` (callsign, space-padded; trim it), `hex` (fallback label), `alt_baro` (feet, or the string
`"ground"`).

## Where it plugs into this codebase

The Weather screen (commit `78a2dda`) is the exact template: one backend fetcher + cache, one telemetry key, one
renderer function, one toggle row.

### `backend.py`

1. **Cache**, next to the others (`backend.py:79-87`):
   `cached_radar = {"ok": False, "error": "Loading..."}`.
2. **Location**: reuse `cached_geo` from `fetch_geo_sync()` (`backend.py:787`). ip-api.com is city-level and can be
   tens of km off, which matters at radar scale, so add a manual lat/lon override sent from the GUI over stdin
   (same channel `stdin_listener()` at `backend.py:965` already uses for the Claude token).
3. **Fetcher** `fetch_radar_sync()`, modelled on `fetch_weather_sync()` (`backend.py:804`) and using the existing
   `fetch_url()` (`backend.py:456`) with `headers={"User-Agent": "AX206-Display-Manager/1.0"}`:
   - km → nm for the URL: `nm = round(km / 1.852) + 1` (the +1 covers the ring edge, as smalltv-mod does).
   - Parse `ac`, drop entries without `lat`/`lon`, treat `alt_baro == "ground"` as 0, optional min-altitude filter.
   - Compute distance/bearing per aircraft (flat-earth is fine at these ranges, from `RadarClient.cpp:78`):
     ```python
     d_lat = (lat - home_lat) * 111.0
     d_lon = (lon - home_lon) * 111.0 * math.cos(math.radians(home_lat))
     dist_km = math.hypot(d_lat, d_lon)
     bearing = (math.degrees(math.atan2(d_lon, d_lat)) + 360) % 360   # 0 = N, 90 = E
     ```
   - Keep the nearest ~25, sorted by distance. Return
     `{"ok": True, "home": {...}, "range_km": 50, "fetched_at": time.time(), "source": "adsb.lol", "aircraft": [...]}`.
4. **Poll it** in `api_poller_loop()` (`backend.py:848`): 10 s for adsb.lol, 30–60 s for OpenSky. That loop is
   sequential, so a slow radar request delays Claude/AG/Bangla/weather polls. Either keep the timeout short (5 s) or
   run the radar fetch in its own thread like the stdin-triggered Claude refresh (`backend.py:991`). Only poll while
   the radar screen is enabled.
5. **Telemetry**: add `"radarData": cached_radar` to the payload in `main()` (`backend.py:~1063`, next to
   `"weatherData"`).

### `renderer.js` + `index.html`

1. Toggle row in `index.html` copied from the Weather block (`index.html:180-195`): `chk-radar`, `dur-radar`, a
   Lucide radar icon.
2. Element refs (`renderer.js:104`, `:114`), `localStorage` load/save (`:158-200`), `getDuration()` (`:518-530`).
3. `currentRadarData = data.radarData;` where weather is stored (`renderer.js:377`).
4. Screen list (`renderer.js:235`): `if (chkRadar.checked && currentRadarData && currentRadarData.ok) list.push('radar');`.
   Consider keeping it in rotation even when the sky is empty, so the empty state is visible.
5. Dispatch in `drawActiveScreen()` (`renderer.js:1815`): `else if (currentScreen === 'radar') renderRadar(offCtx);`.

### `renderRadar(ctx)`, drawn at 960×640 (shown at 480×320)

Layout for the landscape panel: the scope on the left (centre ≈ (320, 320), radius ≈ 290) and a list of the
nearest 4–5 aircraft on the right (callsign, altitude, speed, distance), in the `drawCard()` / `PALETTE` style.

Scope rules, ported from `RadarMode.cpp:64-182` (sizes there are for a ~110 px radius; scale by `R / 110`):

- Background `PALETTE.bg`; range rings at `R` and `R/2` plus a crosshair in `PALETTE.track`; an `N` just inside the
  top of the outer ring.
- Polar → screen: `x = cx + r·sin(bearing)`, `y = cy − r·cos(bearing)`, with `r = dist_km / range_km · R`.
- Aircraft inside the ring: a filled triangle with its nose along `track`. With no track, a dot instead.
- Speed vector: a line along `track` of length `clamp(gs · 0.08, 6, 24)` px (scaled).
- Aircraft beyond the ring: a small dot on the rim at their bearing ("traffic just outside").
- Labels: callsign, plus `FL` + `alt_baro/100` (e.g. `FL350`) under it. Place them nearest-first, to the right of
  the marker, flipped to the left near the edge. Skip any label whose box overlaps one already drawn; the triangle
  still shows.
- Home marker (green dot) drawn last; header shows range at top-left and the aircraft count at top-right; a red
  dot when the last fetch failed.

This app renders at **1 FPS** (`tasks.md`), so a rotating sweep line would only step once a second; skip it or step
it deliberately. What 1 FPS *does* allow is dead-reckoning between polls, which makes planes glide instead of
jumping every 10 s:
`ahead_km = gs · 1.852 / 3600 · seconds_since_fetch`, moved along `track` before projecting.

### Plugin instead of built-in?

There is a plugin system (`plugins/<id>/manifest.json` + `renderer.js` with `onDraw(ctx, 960, 640)` / `onTick`). A
radar plugin would have to do its own network fetch in the renderer, and adsb.lol sends no CORS header. Built-in,
following the Weather pattern, is the cleaner fit.

## Bonus: one fetcher, two screens

`backend.py` already runs on the PC, so it can also serve the SmallTV. smalltv-mod's radar has a "Custom webhook"
source that does a plain-HTTP `GET <url>?lat=&lon=&dist=<km>` and expects `{"ac":[{lat, lon, track, gs, flight, hex,
alt_baro}]}`. A tiny `http.server` thread in `backend.py` returning `cached_radar` in that shape would put planes on the
SmallTV too, with the PC doing the TLS work the ESP8266 can't. It would need a Windows Firewall allow for inbound
LAN traffic on that port.
