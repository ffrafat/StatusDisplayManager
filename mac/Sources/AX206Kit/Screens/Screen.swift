import SwiftUI

/// What a screen needs to draw one frame.
public struct ScreenContext {
    public var now: Date
    public var stats: SystemStats?
    public var use24Hour: Bool

    public init(now: Date, stats: SystemStats?, use24Hour: Bool) {
        self.now = now
        self.stats = stats
        self.use24Hour = use24Hour
    }
}

/// One page in the rotation.
public protocol DisplayScreen {
    var id: String { get }
    var title: String { get }
    var symbol: String { get }
    func view(_ context: ScreenContext) -> AnyView
}

public enum ScreenCatalog {
    public static let all: [any DisplayScreen] = [ClockScreen(), SystemScreen()]
}

enum Format {
    static func bytes(_ v: Double, decimals: Int = 1) -> String {
        let units = ["B", "KB", "MB", "GB", "TB"]
        var value = v
        var i = 0
        while value >= 1000 && i < units.count - 1 { value /= 1000; i += 1 }
        return i == 0 ? "\(Int(value)) B" : String(format: "%.\(value >= 100 ? 0 : decimals)f %@", value, units[i])
    }

    static func rate(_ v: Double) -> String { bytes(v) + "/s" }

    static func gb(_ v: Double) -> String {
        let g = v / 1e9
        return g >= 100 ? String(format: "%.0f", g) : String(format: "%.1f", g)
    }

    /// Binary gigabytes, as Activity Monitor shows memory ("32 GB" for 32 GiB).
    static func gib(_ v: Double) -> String {
        let g = v / 1_073_741_824
        return abs(g - g.rounded()) < 0.05 ? String(format: "%.0f", g) : String(format: "%.1f", g)
    }

    static func watts(_ v: Double?) -> String {
        guard let v else { return "—" }
        return v >= 10 ? String(format: "%.0f W", v) : String(format: "%.1f W", v)
    }

    static func temp(_ v: Double?) -> String {
        guard let v else { return "—" }
        return "\(Int(v.rounded()))°"
    }

    static func uptime(_ t: TimeInterval) -> String {
        let m = Int(t) / 60, h = m / 60, d = h / 24
        if d > 0 { return "\(d)d \(h % 24)h" }
        if h > 0 { return "\(h)h \(m % 60)m" }
        return "\(m)m"
    }

    static func rpm(_ v: Double) -> String {
        let f = NumberFormatter()
        f.numberStyle = .decimal
        f.maximumFractionDigits = 0
        return f.string(from: NSNumber(value: v.rounded())) ?? "\(Int(v))"
    }
}
