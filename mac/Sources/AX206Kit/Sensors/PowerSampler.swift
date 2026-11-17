import CIOReport
import Foundation

/// Per-component SoC power from IOReport's "Energy Model" group (what powermetrics reports),
/// as watts averaged since the previous sample. On some macOS builds only the GPU counter
/// advances; components whose counters stand still read 0 and callers fall back to the SMC.
public final class PowerSampler {
    public struct Reading: Sendable {
        public var cpu: Double = 0
        public var gpu: Double = 0
        public var ane: Double = 0
        public var dram: Double = 0
        public var package: Double { cpu + gpu + ane + dram }
    }

    private var subscription: IOReportSubscriptionRef?
    private var subscribed: CFMutableDictionary?
    private var previous: CFDictionary?
    private var previousTime = Date()

    public init() {
        guard let channels = IOReportCopyChannelsInGroup("Energy Model" as CFString, nil, 0, 0, 0),
              let desired = CFDictionaryCreateMutableCopy(kCFAllocatorDefault, 0, channels) else { return }
        var subbed: Unmanaged<CFMutableDictionary>?
        guard let sub = IOReportCreateSubscription(nil, desired, &subbed, 0, nil), let subbed else { return }
        subscription = sub
        subscribed = subbed.takeRetainedValue()
        previous = IOReportCreateSamples(sub, subscribed!, nil)
    }

    public var isAvailable: Bool { subscription != nil }

    public func sample() -> Reading? {
        guard let subscription, let subscribed, let previous,
              let current = IOReportCreateSamples(subscription, subscribed, nil) else { return nil }
        let now = Date()
        let seconds = max(now.timeIntervalSince(previousTime), 0.001)
        defer { self.previous = current; previousTime = now }
        guard let delta = IOReportCreateSamplesDelta(previous, current, nil),
              let channels = (delta as NSDictionary)["IOReportChannels"] as? [NSDictionary] else { return nil }

        var r = Reading()
        for channel in channels {
            let ch = channel as CFDictionary
            guard let name = IOReportChannelGetChannelName(ch) as String? else { continue }
            let unit = (IOReportChannelGetUnitLabel(ch) as String?)?.trimmingCharacters(in: .whitespaces) ?? "mJ"
            let joules = Double(IOReportSimpleGetIntegerValue(ch, 0)) * PowerSampler.scale(unit)
            let watts = joules / seconds
            if name.hasSuffix("CPU Energy") { r.cpu += watts }
            else if name == "GPU Energy" || name == "GPU" { r.gpu += watts }
            else if name.hasPrefix("ANE") { r.ane += watts }
            else if name.hasPrefix("DRAM") { r.dram += watts }
        }
        return r
    }

    private static func scale(_ unit: String) -> Double {
        switch unit {
        case "nJ": return 1e-9
        case "uJ", "µJ": return 1e-6
        case "J": return 1
        default: return 1e-3
        }
    }
}
