import sys
import os
import time
import json
import base64
import struct
import threading
import ctypes
from ctypes import wintypes
from datetime import datetime
import urllib.request
import zipfile
import shutil

# Try importing external dependencies
try:
    import psutil
except ImportError:
    print(json.dumps({"type": "log", "msg": "psutil is not installed. Run 'pip install psutil'", "level": "error"}))
    sys.exit(1)

try:
    import usb.core
    import usb.util
except ImportError:
    print(json.dumps({"type": "log", "msg": "pyusb is not installed. Run 'pip install pyusb'", "level": "error"}))
    sys.exit(1)

try:
    from winsdk.windows.media.control import GlobalSystemMediaTransportControlsSessionManager
    from winsdk.windows.storage.streams import DataReader
    from winsdk.windows.security.cryptography import CryptographicBuffer
except ImportError:
    print(json.dumps({"type": "log", "msg": "winsdk is not installed. Run 'pip install winsdk'", "level": "error"}))
    sys.exit(1)

# Win32 setup for active app detection
user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32

# USB constants for AX206
VID = 0x1908
PID = 0x0102
EP_OUT = 0x01
EP_IN = 0x81
DIR_OUT = 0x00
DIR_IN = 0x80

# Determine base directory for file downloads
if getattr(sys, 'frozen', False):
    BASE_DIR = os.path.dirname(sys.executable)
else:
    BASE_DIR = os.path.dirname(os.path.abspath(__file__))

# Global states
display_device = None
display_connected = False
display_lock = threading.Lock()

# Cached telemetry data
cached_stats = {}
cached_active_app = None
cached_media = None
cached_claude_usage = {"ok": False, "error": "Loading..."}
cached_ag_usage = {"available": False, "groups": [], "error": "Loading..."}
cached_bangla_gov = {"ok": False, "tools": [], "error": "Loading..."}

# Helper to pack SCSI CBW
def make_cbw(data_len, direction, cdb):
    cdb_padded = cdb + b'\x00' * (16 - len(cdb))
    return struct.pack("<4sIIBBB16s", b"USBC", 0xefbeadde, data_len, direction, 0, 16, cdb_padded)

# Convert flat RGBA bytes to Big-Endian RGB565 bytes
def to_rgb565_be(rgba):
    pixel_count = len(rgba) // 4
    out = bytearray(pixel_count * 2)
    for i in range(pixel_count):
        idx = i * 4
        r = rgba[idx]
        g = rgba[idx + 1]
        b = rgba[idx + 2]
        rgb = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3)
        out[i * 2] = (rgb >> 8) & 0xFF
        out[i * 2 + 1] = rgb & 0xFF
    return bytes(out)

# AX206 USB Driver implementation
class AX206Driver:
    @staticmethod
    def open_device():
        global display_device, display_connected
        with display_lock:
            if display_connected:
                return True
            try:
                dev = usb.core.find(idVendor=VID, idProduct=PID)
                if dev is None:
                    raise Exception("AX206 display device not found")
                
                # Check backend
                dev.set_configuration()
                
                # claim interface 0
                usb.util.claim_interface(dev, 0)
                
                # Clear halt
                try:
                    dev.clear_halt(EP_OUT)
                    dev.clear_halt(EP_IN)
                except Exception:
                    pass
                
                display_device = dev
                display_connected = True
                print(json.dumps({"type": "status", "connected": True}))
                print(json.dumps({"type": "log", "msg": "AX206 Display opened successfully via Python pyusb.", "level": "success"}))
                return True
            except Exception as e:
                display_device = None
                display_connected = False
                print(json.dumps({"type": "status", "connected": False}))
                print(json.dumps({"type": "log", "msg": f"Failed to open display: {str(e)}", "level": "error"}))
                return False

    @staticmethod
    def close_device():
        global display_device, display_connected
        with display_lock:
            if display_device:
                try:
                    usb.util.release_interface(display_device, 0)
                    display_device.attach_kernel_driver(0)
                except Exception:
                    pass
                display_device = None
            display_connected = False
            print(json.dumps({"type": "status", "connected": False}))
            print(json.dumps({"type": "log", "msg": "AX206 Display closed.", "level": "info"}))

    @staticmethod
    def recover():
        global display_device, display_connected
        with display_lock:
            if not display_device:
                return
            print(json.dumps({"type": "log", "msg": "Executing BOT USB Reset...", "level": "warning"}))
            try:
                # Bulk-Only Mass Storage Reset (class request 0xFF)
                display_device.ctrl_transfer(0x21, 0xFF, 0x0000, 0x0000, None, 4000)
            except Exception as e:
                print(json.dumps({"type": "log", "msg": f"BOT Reset warning: {str(e)}", "level": "warning"}))
            
            try:
                display_device.clear_halt(EP_OUT)
                display_device.clear_halt(EP_IN)
            except Exception:
                pass
            
            # Drain IN endpoint
            for _ in range(3):
                try:
                    display_device.read(EP_IN, 64, 60)
                except Exception:
                    break

    @staticmethod
    def blit(x0, y0, x1, y1, rgb565_bytes):
        global display_device, display_connected
        w = x1 - x0
        h = y1 - y0
        if len(rgb565_bytes) != w * h * 2:
            raise Exception("Blit byte length mismatch")

        cdb = bytearray(16)
        cdb[0] = 0xCD
        cdb[5] = 0x06
        cdb[6] = 0x12  # BLIT command
        struct.pack_into("<HHHH", cdb, 7, x0, y0, x1 - 1, y1 - 1)

        with display_lock:
            if not display_device or not display_connected:
                raise Exception("Device not connected")

            # Write CBW
            cbw = make_cbw(len(rgb565_bytes), DIR_OUT, bytes(cdb))
            display_device.write(EP_OUT, cbw, 4000)
            
            # Write data
            display_device.write(EP_OUT, rgb565_bytes, 4000)
            
            # Read CSW
            csw = display_device.read(EP_IN, 13, 2000)
            if len(csw) < 13 or csw[12] != 0:
                raise Exception(f"CSW status failed or signature mismatch: {list(csw)}")

    @staticmethod
    def draw_rgba(rgba_bytes):
        rgb565 = to_rgb565_be(rgba_bytes)
        AX206Driver.blit(0, 0, 480, 320, rgb565)

# --- SYSTEM STATS POLL ---
prev_net_bytes = None
prev_net_time = time.time()

def get_ip_address():
    try:
        import socket
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except Exception:
        return "127.0.0.1"

def update_stats():
    global prev_net_bytes, prev_net_time, cached_stats
    now = time.time()
    dt = now - prev_net_time
    prev_net_time = now

    try:
        cpu_pct = psutil.cpu_percent(interval=None)
        
        freq = psutil.cpu_freq()
        cpu_freq = (freq.current / 1000.0) if freq else 0.0 # to GHz

        mem = psutil.virtual_memory()
        ram_pct = mem.percent
        ram_used = mem.used
        ram_total = mem.total

        disk = psutil.disk_usage('C:\\')
        disk_pct = disk.percent
        disk_used = disk.used
        disk_total = disk.total

        net = psutil.net_io_counters()
        rx_rate = 0
        tx_rate = 0
        if prev_net_bytes and dt > 0:
            rx_rate = max(0, (net.bytes_recv - prev_net_bytes[0]) / dt)
            tx_rate = max(0, (net.bytes_sent - prev_net_bytes[1]) / dt)
        
        prev_net_bytes = (net.bytes_recv, net.bytes_sent)

        # CPU Temp (not supported directly on Windows without admin/WMI, default to None)
        cpu_temp = None

        cached_stats = {
            "cpu": cpu_pct,
            "cpu_freq": cpu_freq,
            "ram": ram_pct,
            "ram_used": ram_used,
            "ram_total": ram_total,
            "disk_root_pct": disk_pct,
            "disk_used": disk_used,
            "disk_total": disk_total,
            "rx": rx_rate,
            "tx": tx_rate,
            "net_recv_total": net.bytes_recv,
            "net_sent_total": net.bytes_sent,
            "temp": cpu_temp,
            "ip": get_ip_address(),
            "hostname": os.environ.get('COMPUTERNAME', 'LOCALHOST').upper()
        }
    except Exception as e:
        cached_stats = {"error": str(e)}

# --- ACTIVE APP POLL ---
def get_active_app():
    try:
        hwnd = user32.GetForegroundWindow()
        if not hwnd or hwnd == 0:
            return None
        
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        if pid.value == 0:
            return None
        
        PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
        h_process = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid.value)
        
        process_name = None
        if h_process:
            size = wintypes.DWORD(260)
            buf = ctypes.create_unicode_buffer(size.value)
            if kernel32.QueryFullProcessImageNameW(h_process, 0, buf, ctypes.byref(size)):
                process_name = os.path.basename(buf.value)
                if process_name.lower().endswith(".exe"):
                    process_name = process_name[:-4]
            kernel32.CloseHandle(h_process)
            
        title_len = user32.GetWindowTextLengthW(hwnd)
        title = ""
        if title_len > 0:
            title_buf = ctypes.create_unicode_buffer(title_len + 1)
            user32.GetWindowTextW(hwnd, title_buf, title_len + 1)
            title = title_buf.value
            
        if not process_name:
            return None
            
        return {"app": process_name, "title": title}
    except Exception:
        return None

# --- MEDIA SESSION POLL ---
async def get_media_sessions():
    sessions_data = []
    try:
        manager = await GlobalSystemMediaTransportControlsSessionManager.request_async()
        if not manager:
            return sessions_data
            
        sessions = manager.get_sessions()
        if not sessions:
            return sessions_data
            
        for s in sessions:
            try:
                app_id = s.source_app_user_model_id
                
                playback = s.get_playback_info()
                status = "Unknown"
                if playback and playback.playback_status is not None:
                    try:
                        # 1. Try to use the .name attribute if available
                        if hasattr(playback.playback_status, 'name'):
                            status = str(playback.playback_status.name).capitalize()
                        # 2. Try converting the str representation if it has PLAY/PAUSE/etc
                        elif 'PLAY' in str(playback.playback_status).upper():
                            status = 'Playing'
                        elif 'PAUS' in str(playback.playback_status).upper():
                            status = 'Paused'
                        elif 'STOP' in str(playback.playback_status).upper():
                            status = 'Stopped'
                        else:
                            # 3. Fallback to integer map (using correct WinRT enum mappings)
                            val = None
                            if hasattr(playback.playback_status, 'value'):
                                val = playback.playback_status.value
                            else:
                                val = int(playback.playback_status)
                            
                            status_map = {
                                0: "Closed",
                                1: "Opened",
                                2: "Changing",
                                3: "Stopped",
                                4: "Playing",
                                5: "Paused"
                            }
                            status = status_map.get(val, "Unknown")
                    except Exception:
                        pass
                
                props = await s.try_get_media_properties_async()
                if not props:
                    continue
                    
                thumbnail_base64 = None
                if props.thumbnail:
                    try:
                        stream = await props.thumbnail.open_read_async()
                        if stream:
                            reader = DataReader(stream)
                            await reader.load_async(stream.size)
                            buffer = reader.detach_buffer()
                            thumbnail_base64 = CryptographicBuffer.encode_to_base64_string(buffer)
                            reader.close()
                            stream.close()
                    except Exception:
                        pass
                
                sessions_data.append({
                    "sourceAppUserModelId": app_id,
                    "title": props.title or "",
                    "artist": props.artist or "",
                    "albumTitle": props.album_title or "",
                    "playbackStatus": status,
                    "thumbnail": thumbnail_base64
                })
            except Exception:
                pass
    except Exception:
        pass
    return sessions_data

def update_media():
    global cached_media
    try:
        import asyncio
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        sessions = loop.run_until_complete(get_media_sessions())
        loop.close()

        session = None
        if sessions:
            session = next((s for s in sessions if s["playbackStatus"] == "Playing"), sessions[0])

        if session:
            # Clean app name
            player = 'System Media'
            app_id = session.get("sourceAppUserModelId", "")
            if '!' in app_id:
                player = app_id.split('!')[0]
            elif '\\' in app_id:
                player = os.path.basename(app_id)
            elif app_id:
                player = app_id
            
            if player.lower().endswith(".exe"):
                player = player[:-4]

            cached_media = {
                "title": session.get("title") or "Unknown Title",
                "artist": session.get("artist") or "Unknown Artist",
                "album": session.get("albumTitle") or "",
                "player": player,
                "playing": session.get("playbackStatus") == "Playing",
                "status": session.get("playbackStatus") or "Unknown",
                "thumbnail": session.get("thumbnail") or None
            }
        else:
            cached_media = None
    except Exception:
        cached_media = None

# --- API PROVIDERS ---

# Helper for HTTP requests
def fetch_url(url, method="GET", headers=None, data=None, timeout=5):
    try:
        req = urllib.request.Request(url, method=method)
        if headers:
            for k, v in headers.items():
                req.add_header(k, v)
        
        response = urllib.request.urlopen(req, data=data, timeout=timeout)
        return response.status, response.headers, response.read()
    except Exception as e:
        # Check if it has a read method (HTTPError)
        if hasattr(e, 'read'):
            return e.code, e.headers, e.read()
        raise e

def parse_reset_ts(val):
    if not val:
        return 0
    val = str(val).strip()
    try:
        if val.replace('.', '', 1).isdigit():
            ts = float(val)
            return ts if ts > 1000000000 else 0
    except ValueError:
        pass
    try:
        if val.endswith('Z'):
            val = val[:-1] + '+00:00'
        dt = datetime.fromisoformat(val)
        return dt.timestamp()
    except Exception:
        return 0

def fetch_claude_usage_sync():
    global cached_claude_usage
    # Locate token candidate paths
    home = os.path.expanduser("~")
    local_app_data = os.environ.get("LOCALAPPDATA", os.path.join(home, "AppData", "Local"))
    candidates = [
        os.path.join(home, ".claude", ".credentials.json"),
        os.path.join(local_app_data, "Claude", ".credentials.json")
    ]
    
    token = None
    for p in candidates:
        try:
            if os.path.exists(p):
                with open(p, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    token = data.get("accessToken")
                    if not token and "claudeAiOauth" in data:
                        token = data["claudeAiOauth"].get("accessToken")
                    if token:
                        token = token.strip()
                        break
        except Exception:
            pass

    if not token:
        cached_claude_usage = {"ok": False, "error": "No Claude credentials found"}
        return

    payload = json.dumps({
        "model": "claude-haiku-4-5-20251001",
        "max_tokens": 1,
        "messages": [{"role": "user", "content": "hi"}]
    }).encode("utf-8")

    try:
        status, headers, body = fetch_url(
            "https://api.anthropic.com/v1/messages",
            method="POST",
            headers={
                "Authorization": f"Bearer {token}",
                "anthropic-version": "2023-06-01",
                "anthropic-beta": "oauth-2025-04-20",
                "Content-Type": "application/json"
            },
            data=payload
        )
        
        session_util = float(headers.get("anthropic-ratelimit-unified-5h-utilization", "0"))
        weekly_util = float(headers.get("anthropic-ratelimit-unified-7d-utilization", "0"))
        session_rst = headers.get("anthropic-ratelimit-unified-5h-reset", "")
        weekly_rst = headers.get("anthropic-ratelimit-unified-7d-reset", "")

        cached_claude_usage = {
            "ok": True,
            "error": None,
            "session_pct": session_util * 100,
            "weekly_pct": weekly_util * 100,
            "session_reset_ts": parse_reset_ts(session_rst),
            "weekly_reset_ts": parse_reset_ts(weekly_rst)
        }
    except Exception as e:
        cached_claude_usage = {
            "ok": False,
            "error": str(e),
            "session_pct": 0,
            "weekly_pct": 0,
            "session_reset_ts": 0,
            "weekly_reset_ts": 0
        }

def find_ag_server():
    ag_procs = []
    # Find process
    for proc in psutil.process_iter(['pid', 'name', 'cmdline']):
        try:
            cmd = " ".join(proc.info['cmdline'] or [])
            if 'language_server' in proc.info['name'].lower() or 'language_server' in cmd.lower():
                import re
                csrf_match = re.search(r'--csrf_token\s+(\S+)', cmd)
                if csrf_match:
                    csrf = csrf_match.group(1)
                    ag_procs.append({"pid": proc.info['pid'], "csrf": csrf})
        except Exception:
            pass

    if not ag_procs:
        return None

    # Test ports
    for ap in ag_procs:
        try:
            p = psutil.Process(ap["pid"])
            for conn in p.connections(kind='tcp'):
                if conn.status == 'LISTEN':
                    port = conn.laddr.port
                    # Test status call
                    try:
                        url = f"http://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/GetUserStatus"
                        headers = {
                            "X-Codeium-Csrf-Token": ap["csrf"],
                            "Connect-Protocol-Version": "1",
                            "Content-Type": "application/json"
                        }
                        payload = json.dumps({"metadata": {"ideName": "antigravity", "extensionName": "antigravity", "locale": "en"}}).encode("utf-8")
                        status, _, _ = fetch_url(url, method="POST", headers=headers, data=payload, timeout=2)
                        if status == 200:
                            return {"port": port, "csrf": ap["csrf"]}
                    except Exception:
                        pass
        except Exception:
            pass
    return None

cached_ag_server = None

def fetch_ag_usage_sync():
    global cached_ag_usage, cached_ag_server
    server = cached_ag_server or find_ag_server()
    if not server:
        cached_ag_server = None
        cached_ag_usage = {"available": False, "groups": [], "error": "Antigravity not running"}
        return

    url = f"http://127.0.0.1:{server['port']}/exa.language_server_pb.LanguageServerService/GetUserStatus"
    headers = {
        "X-Codeium-Csrf-Token": server["csrf"],
        "Connect-Protocol-Version": "1",
        "Content-Type": "application/json"
    }
    payload = json.dumps({"metadata": {"ideName": "antigravity", "extensionName": "antigravity", "locale": "en"}}).encode("utf-8")

    try:
        status, _, body = fetch_url(url, method="POST", headers=headers, data=payload)
        if status != 200:
            cached_ag_server = None
            cached_ag_usage = {"available": False, "groups": [], "error": f"HTTP {status}"}
            return

        cached_ag_server = server
        data = json.loads(body.decode("utf-8"))
        models = data.get("userStatus", {}).get("cascadeModelConfigData", {}).get("clientModelConfigs", [])

        ag_groups_def = [
            {"label": "Gemini Flash", "color": [64, 196, 255], "keywords": ["gemini", "flash"]},
            {"label": "Gemini Pro",   "color": [105, 240, 174], "keywords": ["gemini", "pro"]},
            {"label": "Claude",       "color": [255, 171, 64], "keywords": ["claude"]}
        ]

        groups = []
        for group in ag_groups_def:
            matches = [m for m in models if all(kw in (m.get("label") or "").lower() for kw in group["keywords"])]
            if not matches:
                continue

            fractions = [m["quotaInfo"]["remainingFraction"] for m in matches if m.get("quotaInfo", {}).get("remainingFraction") is not None]
            if not fractions:
                continue

            remaining = min(fractions) * 100

            # Find worst resetting time
            worst = matches[0]
            for m in matches:
                if m.get("quotaInfo", {}).get("remainingFraction", 1) < worst.get("quotaInfo", {}).get("remainingFraction", 1):
                    worst = m
            
            reset_str = worst.get("quotaInfo", {}).get("resetTime") or ""
            groups.append({
                "label": group["label"],
                "color": group["color"],
                "remaining": remaining,
                "reset_ts": parse_reset_ts(reset_str)
            })

        cached_ag_usage = {
            "available": True,
            "groups": groups,
            "error": None
        }
    except Exception as e:
        cached_ag_server = None
        cached_ag_usage = {"available": False, "groups": [], "error": str(e)}

def download_file(url, dest_path):
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"})
        with urllib.request.urlopen(req, timeout=10) as response:
            with open(dest_path, "wb") as f:
                f.write(response.read())
    except Exception:
        pass

def fetch_bangla_gov_sync():
    global cached_bangla_gov
    try:
        status, _, body = fetch_url("https://bangla.gov.bd/api/bangla-gov-bd/bangla-gov-bd/", timeout=8)
        if status != 200:
            raise Exception(f"HTTP {status}")

        data = json.loads(body.decode("utf-8"))

        # Check and download banglaLogo
        bangla_logo_path = os.path.join(BASE_DIR, "bangla.png")
        if not os.path.exists(bangla_logo_path) or os.path.getsize(bangla_logo_path) == 0:
            download_file("https://bangla.gov.bd/banglaLogo.png", bangla_logo_path)

        products_def = [
            {"id": "02b6b237-2875-44a4-80ea-e720f8d7d488", "localIcon": "product_purno.png"},
            {"id": "904bfa8b-5dd4-4f9b-b0dc-568d381717af", "localIcon": "product_sothik.png"},
            {"id": "602a728d-b718-47ee-906f-d153f05d99fc", "localIcon": "product_banglaword.png"}
        ]

        tools = []
        for prod in products_def:
            match = next((item for item in data if item.get("id") == prod["id"]), None)
            if match:
                # Download product icon if missing
                icon_path = os.path.join(BASE_DIR, prod["localIcon"])
                if not os.path.exists(icon_path) or os.path.getsize(icon_path) == 0:
                    icon_url = f"https://bangla.gov.bd{match.get('icon')}"
                    download_file(icon_url, icon_path)

                tools.append({
                    "id": match["id"],
                    "title": match.get("title") or "",
                    "title_en": match.get("title_en") or "",
                    "version": match.get("version") or "",
                    "type": match.get("type") or "",
                    "type_en": match.get("type_en") or "",
                    "downloadCount": match.get("downloaded_file_count") or 0,
                    "localIcon": prod["localIcon"]
                })

        cached_bangla_gov = {
            "ok": True,
            "tools": tools,
            "error": None
        }
    except Exception as e:
        cached_bangla_gov = {
            "ok": False,
            "tools": [],
            "error": str(e)
        }

# --- BACKGROUND API POLLER ---
def api_poller_loop():
    last_claude_poll = 0
    last_ag_poll = 0
    last_bangla_poll = 0
    
    while True:
        now = time.time()
        
        # Claude usage every 60s
        if now - last_claude_poll > 60:
            last_claude_poll = now
            fetch_claude_usage_sync()
            
        # Antigravity usage every 30s
        if now - last_ag_poll > 30:
            last_ag_poll = now
            fetch_ag_usage_sync()
            
        # Bangla Gov every 60s
        if now - last_bangla_poll > 60:
            last_bangla_poll = now
            fetch_bangla_gov_sync()
            
        time.sleep(1)

# --- PLUGINS SYSTEM CONTROLLERS ---
PLUGINS_DIR = os.path.join(BASE_DIR, "plugins")
if not os.path.exists(PLUGINS_DIR):
    os.makedirs(PLUGINS_DIR)

def get_installed_plugins():
    plugins = []
    if not os.path.exists(PLUGINS_DIR):
        return plugins
    for name in os.listdir(PLUGINS_DIR):
        p_path = os.path.join(PLUGINS_DIR, name)
        if os.path.isdir(p_path) and not name.startswith("_"):
            manifest_path = os.path.join(p_path, "manifest.json")
            if os.path.exists(manifest_path):
                try:
                    with open(manifest_path, "r", encoding="utf-8") as f:
                        manifest = json.load(f)
                    plugins.append({
                        "id": name,
                        "manifest": manifest
                    })
                except Exception as e:
                    print(json.dumps({"type": "log", "msg": f"Failed to parse manifest for plugin {name}: {str(e)}", "level": "warning"}))
    return plugins

def install_plugin(zip_path):
    if not os.path.exists(zip_path):
        raise Exception(f"ZIP file not found at path: {zip_path}")
    
    # Create temporary folder
    temp_dir = os.path.join(PLUGINS_DIR, "_temp_extract")
    if os.path.exists(temp_dir):
        shutil.rmtree(temp_dir)
    os.makedirs(temp_dir)
    
    try:
        # Extract zip
        with zipfile.ZipFile(zip_path, 'r') as zip_ref:
            zip_ref.extractall(temp_dir)
            
        # Find manifest.json (might be nested in a single root folder in the zip)
        manifest_file = None
        search_dir = temp_dir
        
        # Check if there is a single subfolder containing manifest.json
        items = [i for i in os.listdir(temp_dir) if not i.startswith("__MACOSX")]
        if len(items) == 1 and os.path.isdir(os.path.join(temp_dir, items[0])):
            search_dir = os.path.join(temp_dir, items[0])
            
        manifest_path = os.path.join(search_dir, "manifest.json")
        if not os.path.exists(manifest_path):
            raise Exception("No manifest.json found in the plugin ZIP.")
            
        with open(manifest_path, "r", encoding="utf-8") as f:
            manifest = json.load(f)
            
        plugin_id = manifest.get("id")
        if not plugin_id:
            raise Exception("Plugin manifest is missing the 'id' field.")
            
        # Clean destination if exists
        dest_dir = os.path.join(PLUGINS_DIR, plugin_id)
        if os.path.exists(dest_dir):
            shutil.rmtree(dest_dir)
            
        # Move files
        shutil.move(search_dir, dest_dir)
        print(json.dumps({"type": "log", "msg": f"Plugin '{plugin_id}' installed successfully.", "level": "success"}))
        print(json.dumps({"type": "plugin_installed", "id": plugin_id, "manifest": manifest}))
    finally:
        # Cleanup temp
        if os.path.exists(temp_dir):
            shutil.rmtree(temp_dir)

def uninstall_plugin(plugin_id):
    dest_dir = os.path.join(PLUGINS_DIR, plugin_id)
    if os.path.exists(dest_dir):
        shutil.rmtree(dest_dir)
        print(json.dumps({"type": "log", "msg": f"Plugin '{plugin_id}' uninstalled successfully.", "level": "success"}))
        print(json.dumps({"type": "plugin_uninstalled", "id": plugin_id}))
    else:
        raise Exception(f"Plugin '{plugin_id}' not found.")

# --- STDIN READER THREAD ---
def stdin_listener():
    global display_connected
    while True:
        line = sys.stdin.readline()
        if not line:
            # stdin closed (Electron terminated), shut down Python process
            AX206Driver.close_device()
            os._exit(0)
            
        try:
            payload = json.loads(line.strip())
            cmd = payload.get("cmd")
            
            if cmd == "connect":
                AX206Driver.open_device()
            elif cmd == "disconnect":
                AX206Driver.close_device()
            elif cmd == "install_plugin":
                try:
                    zip_path = payload.get("zip_path")
                    install_plugin(zip_path)
                except Exception as e:
                    print(json.dumps({"type": "log", "msg": f"Plugin install error: {str(e)}", "level": "error"}))
            elif cmd == "uninstall_plugin":
                try:
                    plugin_id = payload.get("id")
                    uninstall_plugin(plugin_id)
                except Exception as e:
                    print(json.dumps({"type": "log", "msg": f"Plugin uninstall error: {str(e)}", "level": "error"}))
            elif cmd == "draw":
                try:
                    if display_connected:
                        frame_base64 = payload.get("frame")
                        if frame_base64:
                            frame_bytes = base64.b64decode(frame_base64)
                            try:
                                AX206Driver.draw_rgba(frame_bytes)
                            except Exception as draw_err:
                                # Try to recover once
                                print(json.dumps({"type": "log", "msg": f"USB blit error: {str(draw_err)}. Attempting recovery...", "level": "warning"}))
                                try:
                                    AX206Driver.recover()
                                    AX206Driver.open_device()
                                    # Retry blit
                                    AX206Driver.draw_rgba(frame_bytes)
                                except Exception as rec_err:
                                    print(json.dumps({"type": "log", "msg": f"Recovery failed: {str(rec_err)}", "level": "error"}))
                                    AX206Driver.close_device()
                finally:
                    print(json.dumps({"type": "draw_done"}))
                    sys.stdout.flush()
        except Exception as e:
            print(json.dumps({"type": "log", "msg": f"Stdin command error: {str(e)}", "level": "error"}))

# --- MAIN LOOP ---
def main():
    # Set stdout to UTF-8
    if sys.stdout.encoding != 'utf-8':
        import io
        sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

    # Start API poller thread
    api_thread = threading.Thread(target=api_poller_loop, daemon=True)
    api_thread.start()

    # Start stdin reader thread
    stdin_thread = threading.Thread(target=stdin_listener, daemon=True)
    stdin_thread.start()

    # Scan and announce plugins list to Electron
    try:
        plugins = get_installed_plugins()
        print(json.dumps({"type": "plugins_list", "plugins": plugins}))
        sys.stdout.flush()
    except Exception as e:
        print(json.dumps({"type": "log", "msg": f"Plugin scan error: {str(e)}", "level": "error"}))
        sys.stdout.flush()

    # Try to connect to display on startup
    AX206Driver.open_device()

    # Main 1-second telemetry loop
    while True:
        try:
            update_stats()
            active_app = get_active_app()
            update_media()
            
            # Print combined telemetry payload to Electron
            telemetry = {
                "type": "telemetry",
                "data": {
                    "stats": cached_stats,
                    "activeApp": active_app,
                    "media": cached_media,
                    "claudeUsage": cached_claude_usage,
                    "agUsage": cached_ag_usage,
                    "banglaGovData": cached_bangla_gov,
                    "displayConnected": display_connected
                }
            }
            print(json.dumps(telemetry))
            sys.stdout.flush()
        except Exception as e:
            print(json.dumps({"type": "log", "msg": f"Main loop telemetry error: {str(e)}", "level": "error"}))
            sys.stdout.flush()
            
        time.sleep(1)

if __name__ == "__main__":
    main()
