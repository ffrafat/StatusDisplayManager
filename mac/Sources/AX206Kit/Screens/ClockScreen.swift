import SwiftUI

/// Analog dial (macOS Clock widget style) beside a calendar-style date panel.
public struct ClockScreen: DisplayScreen {
    public let id = "clock"
    public let title = "Clock"
    public let symbol = "clock"

    public init() {}

    public func view(_ context: ScreenContext) -> AnyView {
        AnyView(ClockView(now: context.now, use24Hour: context.use24Hour))
    }
}

struct ClockView: View {
    let now: Date
    let use24Hour: Bool

    private var calendar: Calendar { .current }

    var body: some View {
        HStack(spacing: 10) {
            Card(padding: 0) {
                AnalogDial(now: now, city: Self.cityName)
                    .padding(9)
            }
            .frame(width: 300)

            Card(padding: 14) { datePanel }
        }
        .padding(10)
    }

    private var datePanel: some View {
        let parts = calendar.dateComponents([.hour, .minute, .second, .weekOfYear], from: now)
        let dayOfYear = calendar.ordinality(of: .day, in: .year, for: now) ?? 0
        let startOfDay = calendar.startOfDay(for: now)
        let dayFraction = now.timeIntervalSince(startOfDay) / 86_400

        return VStack(alignment: .leading, spacing: 0) {
            Text(now.formatted(.dateTime.weekday(.wide)).uppercased())
                .font(Theme.text(14, .bold))
                .kerning(0.8)
                .foregroundStyle(Theme.orange)
                .lineLimit(1)
                .minimumScaleFactor(0.7)

            Text(now.formatted(.dateTime.day()))
                .font(Theme.rounded(70, .light))
                .foregroundStyle(Theme.primary)
                .frame(height: 74)
                .padding(.top, 2)

            Text(now.formatted(.dateTime.month(.wide)))
                .font(Theme.text(18, .semibold))
                .foregroundStyle(Theme.primary)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
            Text(now.formatted(.dateTime.year()))
                .font(Theme.text(14, .medium))
                .foregroundStyle(Theme.secondary)

            Spacer(minLength: 6)

            HStack(alignment: .firstTextBaseline, spacing: 3) {
                Text(timeString(parts))
                    .font(Theme.rounded(34, .semibold))
                    .monospacedDigit()
                    .foregroundStyle(Theme.primary)
                if !use24Hour {
                    Text(parts.hour! < 12 ? "AM" : "PM")
                        .font(Theme.text(12, .semibold))
                        .foregroundStyle(Theme.secondary)
                }
            }
            .lineLimit(1)
            .minimumScaleFactor(0.6)

            Text("Week \(parts.weekOfYear ?? 0) · Day \(dayOfYear)")
                .font(Theme.text(11, .medium))
                .foregroundStyle(Theme.tertiary)
                .padding(.top, 2)

            Meter(fraction: dayFraction, tint: Theme.orange, height: 4)
                .padding(.top, 9)
        }
    }

    private func timeString(_ p: DateComponents) -> String {
        let h = p.hour ?? 0, m = p.minute ?? 0
        if use24Hour { return String(format: "%02d:%02d", h, m) }
        return String(format: "%d:%02d", h % 12 == 0 ? 12 : h % 12, m)
    }

    static var cityName: String {
        let id = TimeZone.current.identifier
        return (id.split(separator: "/").last.map(String.init) ?? id).replacingOccurrences(of: "_", with: " ")
    }
}

/// The analog face: minute track, numerals, white hands and an orange sweep hand.
struct AnalogDial: View {
    let now: Date
    let city: String

    var body: some View {
        Canvas { ctx, size in
            let side = min(size.width, size.height)
            let c = CGPoint(x: size.width / 2, y: size.height / 2)
            let r = side / 2

            // Face.
            let face = Path(ellipseIn: CGRect(x: c.x - r, y: c.y - r, width: side, height: side))
            ctx.fill(face, with: .radialGradient(
                Gradient(colors: [Color(white: 0.09), Color(white: 0.035)]),
                center: c, startRadius: 0, endRadius: r))
            ctx.stroke(face, with: .color(.white.opacity(0.07)), lineWidth: 1)

            // Minute and hour ticks.
            for i in 0..<60 {
                let hour = i % 5 == 0
                let a = Double(i) / 60 * 2 * .pi
                let outer = r - 7, inner = r - (hour ? 17 : 12)
                var p = Path()
                p.move(to: point(c, inner, a))
                p.addLine(to: point(c, outer, a))
                ctx.stroke(p, with: .color(hour ? .white : Color(white: 0.4)),
                           style: StrokeStyle(lineWidth: hour ? 2.6 : 1.2, lineCap: .round))
            }

            // Numerals.
            for n in 1...12 {
                let a = Double(n) / 12 * 2 * .pi
                ctx.draw(Text("\(n)").font(Theme.rounded(21, .semibold)).foregroundStyle(Color.white),
                         at: point(c, r - 36, a))
            }

            // Labels inside the dial.
            ctx.draw(Text(city.uppercased()).font(Theme.text(11, .bold)).kerning(1).foregroundStyle(Theme.secondary),
                     at: CGPoint(x: c.x, y: c.y - r * 0.36))
            let comps = Calendar.current.dateComponents([.hour, .minute, .second, .nanosecond], from: now)
            let h = Double(comps.hour ?? 0), m = Double(comps.minute ?? 0), s = Double(comps.second ?? 0)
            ctx.draw(Text(h < 12 ? "AM" : "PM").font(Theme.text(11, .bold)).kerning(1).foregroundStyle(Theme.orange),
                     at: CGPoint(x: c.x, y: c.y + r * 0.36))

            // Hands. Angles in radians clockwise from 12.
            let hourA = (h.truncatingRemainder(dividingBy: 12) + m / 60 + s / 3600) / 12 * 2 * .pi
            let minA = (m + s / 60) / 60 * 2 * .pi
            let secA = s / 60 * 2 * .pi

            var shadowed = ctx
            shadowed.addFilter(.shadow(color: .black.opacity(0.6), radius: 3, x: 0, y: 1.5))
            hand(&shadowed, c, angle: hourA, length: r * 0.52, width: 8)
            hand(&shadowed, c, angle: minA, length: r * 0.80, width: 8)

            // Second hand with counterweight tail.
            var sec = Path()
            sec.move(to: point(c, -r * 0.17, secA))
            sec.addLine(to: point(c, r * 0.90, secA))
            shadowed.stroke(sec, with: .color(Theme.orange), style: StrokeStyle(lineWidth: 2, lineCap: .round))
            ctx.fill(Path(ellipseIn: CGRect(x: c.x - 5, y: c.y - 5, width: 10, height: 10)), with: .color(Theme.orange))
            ctx.fill(Path(ellipseIn: CGRect(x: c.x - 2, y: c.y - 2, width: 4, height: 4)), with: .color(.black))
        }
    }

    /// Apple-style hand: thin stem from the centre, then a wide rounded blade.
    private func hand(_ ctx: inout GraphicsContext, _ c: CGPoint, angle: Double, length: CGFloat, width: CGFloat) {
        var stem = Path()
        stem.move(to: c)
        stem.addLine(to: point(c, 16, angle))
        ctx.stroke(stem, with: .color(.white), style: StrokeStyle(lineWidth: 3, lineCap: .round))
        var blade = Path()
        blade.move(to: point(c, 16, angle))
        blade.addLine(to: point(c, length, angle))
        ctx.stroke(blade, with: .color(.white), style: StrokeStyle(lineWidth: width, lineCap: .round))
        ctx.fill(Path(ellipseIn: CGRect(x: c.x - 4.5, y: c.y - 4.5, width: 9, height: 9)), with: .color(.white))
    }

    private func point(_ c: CGPoint, _ radius: CGFloat, _ angle: Double) -> CGPoint {
        CGPoint(x: c.x + radius * CGFloat(sin(angle)), y: c.y - radius * CGFloat(cos(angle)))
    }
}
