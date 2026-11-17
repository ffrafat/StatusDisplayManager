import SwiftUI

/// macOS dark-mode widget look, tuned for the AX206's small TN panel.
public enum Theme {
    public static let background = Color.black
    public static let card = Color(white: 0.115)
    public static let cardStroke = Color.white.opacity(0.06)
    public static let track = Color(white: 0.22)

    public static let primary = Color.white
    public static let secondary = Color(white: 0.62)
    public static let tertiary = Color(white: 0.42)

    // Apple system colours (dark appearance).
    public static let blue = Color(red: 0.04, green: 0.52, blue: 1.0)
    public static let green = Color(red: 0.19, green: 0.82, blue: 0.35)
    public static let orange = Color(red: 1.0, green: 0.62, blue: 0.04)
    public static let red = Color(red: 1.0, green: 0.27, blue: 0.23)
    public static let yellow = Color(red: 1.0, green: 0.84, blue: 0.04)
    public static let purple = Color(red: 0.75, green: 0.35, blue: 0.95)
    public static let pink = Color(red: 1.0, green: 0.22, blue: 0.37)
    public static let teal = Color(red: 0.25, green: 0.78, blue: 0.88)
    public static let mint = Color(red: 0.40, green: 0.83, blue: 0.81)
    public static let indigo = Color(red: 0.37, green: 0.36, blue: 0.90)

    public static func rounded(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight, design: .rounded)
    }

    public static func text(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight)
    }

    /// Green → yellow → red for a 0...100 load or temperature-style value.
    public static func level(_ percent: Double, warn: Double = 60, hot: Double = 85) -> Color {
        percent >= hot ? red : percent >= warn ? yellow : green
    }
}

/// A rounded "widget" panel.
public struct Card<Content: View>: View {
    let content: Content
    var padding: CGFloat = 10

    public init(padding: CGFloat = 10, @ViewBuilder content: () -> Content) {
        self.padding = padding
        self.content = content()
    }

    public var body: some View {
        content
            .padding(padding)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .fill(Theme.card)
                    .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).strokeBorder(Theme.cardStroke, lineWidth: 1))
            )
    }
}

/// Small coloured symbol + caps label, as on macOS widgets.
public struct CardHeader<Trailing: View>: View {
    let symbol: String
    let title: String
    let tint: Color
    let trailing: Trailing

    public init(_ symbol: String, _ title: String, tint: Color, @ViewBuilder trailing: () -> Trailing = { EmptyView() }) {
        self.symbol = symbol
        self.title = title
        self.tint = tint
        self.trailing = trailing()
    }

    public var body: some View {
        HStack(spacing: 5) {
            Image(systemName: symbol)
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(tint)
            Text(title)
                .font(Theme.text(11, .semibold))
                .foregroundStyle(Theme.secondary)
                .kerning(0.4)
            Spacer(minLength: 0)
            trailing
        }
        .frame(height: 14)
    }
}

/// Circular progress ring with a value in the middle.
public struct RingGauge: View {
    let fraction: Double
    let tint: Color
    let label: String
    var size: CGFloat = 62
    var line: CGFloat = 7

    public init(fraction: Double, tint: Color, label: String, size: CGFloat = 62, line: CGFloat = 7) {
        self.fraction = fraction
        self.tint = tint
        self.label = label
        self.size = size
        self.line = line
    }

    public var body: some View {
        ZStack {
            Circle().stroke(Theme.track, lineWidth: line)
            Circle()
                .trim(from: 0, to: max(0.001, min(1, fraction)))
                .stroke(tint, style: StrokeStyle(lineWidth: line, lineCap: .round))
                .rotationEffect(.degrees(-90))
            Text(label)
                .font(Theme.rounded(16, .bold))
                .monospacedDigit()
                .foregroundStyle(Theme.primary)
        }
        .frame(width: size, height: size)
    }
}

/// Horizontal capsule bar.
public struct Meter: View {
    let fraction: Double
    let tint: Color
    var height: CGFloat = 6

    public init(fraction: Double, tint: Color, height: CGFloat = 6) {
        self.fraction = fraction
        self.tint = tint
        self.height = height
    }

    public var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(Theme.track)
                Capsule().fill(tint).frame(width: max(height, g.size.width * min(1, max(0, fraction))))
            }
        }
        .frame(height: height)
    }
}

/// Line chart with a soft fill, scaled to `range` (or to the data when nil).
public struct Sparkline: View {
    let values: [Double]
    let tint: Color
    var range: ClosedRange<Double>?
    var capacity = 60

    public init(values: [Double], tint: Color, range: ClosedRange<Double>? = nil, capacity: Int = 60) {
        self.values = values
        self.tint = tint
        self.range = range
        self.capacity = capacity
    }

    public var body: some View {
        Canvas { ctx, size in
            guard values.count > 1 else { return }
            let lo = range?.lowerBound ?? max(0, (values.min() ?? 0) * 0.85)
            var hi = range?.upperBound ?? (values.max() ?? 1) * 1.08
            if hi - lo < 0.001 { hi = lo + 1 }
            let step = size.width / CGFloat(max(1, capacity - 1))
            let x0 = size.width - step * CGFloat(values.count - 1)
            let points = values.enumerated().map { i, v in
                CGPoint(x: x0 + CGFloat(i) * step,
                        y: size.height - CGFloat((min(hi, max(lo, v)) - lo) / (hi - lo)) * (size.height - 2) - 1)
            }
            var line = Path()
            line.addLines(points)
            var fill = line
            fill.addLine(to: CGPoint(x: points.last!.x, y: size.height))
            fill.addLine(to: CGPoint(x: points.first!.x, y: size.height))
            fill.closeSubpath()
            ctx.fill(fill, with: .linearGradient(
                Gradient(colors: [tint.opacity(0.38), tint.opacity(0.02)]),
                startPoint: .zero, endPoint: CGPoint(x: 0, y: size.height)))
            ctx.stroke(line, with: .color(tint), style: StrokeStyle(lineWidth: 1.6, lineCap: .round, lineJoin: .round))
        }
    }
}
