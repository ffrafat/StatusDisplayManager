import Darwin
import Foundation
import IOKit

/// One snapshot of everything the System screen shows.
public struct SystemStats: Sendable {
    public struct Fan: Sendable {
        public var rpm: Double
        public var min: Double
        public var max: Double
        /// 0...1 position of `rpm` between min and max.
        public var fraction: Double { max > min ? Swift.min(1, Swift.max(0, (rpm - min) / (max - min))) : 0 }
    }

    public var hostname = ""
    public var modelName = "Mac"
    public var chip = ""
    public var ip = ""
    public var uptime: TimeInterval = 0

    public var cpuPercent: Double = 0
    public var eCorePercents: [Double] = []
    public var pCorePercents: [Double] = []
    public var cpuTemp: Double?

    public var gpuPercent: Double = 0
    public var gpuTemp: Double?

    public var memUsed: Double = 0
    public var memTotal: Double = 0
    public var memPressure: Double = 0
    public var swapUsed: Double = 0
    public var memPercent: Double { memTotal > 0 ? memUsed / memTotal * 100 : 0 }

    public var diskUsed: Double = 0
    public var diskTotal: Double = 0
    public var diskPercent: Double { diskTotal > 0 ? diskUsed / diskTotal * 100 : 0 }

    public var rxPerSec: Double = 0
    public var txPerSec: Double = 0

    /// Whole-system power from the SMC (wall power for a Mac Studio), watts.
    public var systemPower: Double?
    /// CPU power, watts: IOReport when its counters move, else the SMC's CPU rail (PHPC).
    public var cpuPower: Double?
    /// GPU power from IOReport, watts.
    public var gpuPower: Double?
    public var fans: [Fan] = []

    /// Recent history for sparklines, oldest first.
    public var cpuHistory: [Double] = []
    public var powerHistory: [Double] = []

    public init() {}
}

/// Samples the Mac. Call `sample()` about once a second, always from the same queue.
public final class SystemSampler: @unchecked Sendable {
    private let smc = SMC()
    private let power = PowerSampler()
    private var prevTicks: [[UInt32]] = []
    private var prevNet: (rx: UInt64, tx: UInt64, time: Date)?
    private var cpuHistory: [Double] = []
    private var powerHistory: [Double] = []
    private let historyLength = 60

    private let eCores = SystemSampler.sysctlInt("hw.perflevel1.logicalcpu") ?? 0
    private let cpuTempKeys: [String]
    private let gpuTempKeys: [String]
    private let fanCount: Int
    private let staticInfo: (host: String, model: String, chip: String)

    public init() {
        // Use whichever of the known Apple Silicon sensor keys this Mac actually has.
        let cpuCandidates = ["Tp01", "Tp05", "Tp09", "Tp0D", "Tp0H", "Tp0L", "Tp0P", "Tp0T", "Tp0X", "Tp0b",
                             "Te05", "Te0L", "Te0P", "Te0S", "Tf04", "Tf09", "Tf0A", "Tf0B", "Tf0D", "Tf0E",
                             "Tf44", "Tf49", "Tf4A", "Tf4B", "Tf4D", "Tf4E"]
        let gpuCandidates = ["Tg05", "Tg0D", "Tg0L", "Tg0T", "Tg0f", "Tg0j", "Tf14", "Tf18", "Tf19", "Tf1A", "Tf24", "Tf28", "Tf29", "Tf2A"]
        let valid: (String) -> Bool = { [smc] key in
            guard let t = smc?.number(key) else { return false }
            return t > 5 && t < 130
        }
        cpuTempKeys = cpuCandidates.filter(valid)
        gpuTempKeys = gpuCandidates.filter(valid)
        fanCount = Int(smc?.number("FNum") ?? 0)
        staticInfo = (SystemSampler.hostName(), SystemSampler.modelName(), SystemSampler.chipName())
    }

    public func sample() -> SystemStats {
        var s = SystemStats()
        s.hostname = staticInfo.host
        s.modelName = staticInfo.model
        s.chip = staticInfo.chip
        s.ip = SystemSampler.primaryIPv4() ?? "—"
        s.uptime = SystemSampler.uptime()

        sampleCPU(&s)
        sampleMemory(&s)
        sampleDisk(&s)
        sampleNetwork(&s)
        s.gpuPercent = SystemSampler.gpuUtilization() ?? 0

        if let smc {
            s.cpuTemp = average(cpuTempKeys.compactMap(smc.number))
            s.gpuTemp = average(gpuTempKeys.compactMap(smc.number))
            s.systemPower = smc.number("PSTR")
            s.fans = (0..<fanCount).compactMap { i in
                guard let rpm = smc.number("F\(i)Ac") else { return nil }
                return .init(rpm: rpm, min: smc.number("F\(i)Mn") ?? 0, max: smc.number("F\(i)Mx") ?? 0)
            }
        }
        let soc = power.sample()
        s.gpuPower = soc?.gpu
        if let cpu = soc?.cpu, cpu > 0 { s.cpuPower = cpu } else { s.cpuPower = smc?.number("PHPC") }

        push(&cpuHistory, s.cpuPercent)
        push(&powerHistory, s.systemPower ?? s.cpuPower ?? 0)
        s.cpuHistory = cpuHistory
        s.powerHistory = powerHistory
        return s
    }

    private func push(_ history: inout [Double], _ value: Double) {
        history.append(value)
        if history.count > historyLength { history.removeFirst(history.count - historyLength) }
    }

    private func average(_ values: [Double]) -> Double? {
        values.isEmpty ? nil : values.reduce(0, +) / Double(values.count)
    }

    // MARK: CPU

    private func sampleCPU(_ s: inout SystemStats) {
        var count: natural_t = 0
        var info: processor_info_array_t?
        var infoCount: mach_msg_type_number_t = 0
        guard host_processor_info(mach_host_self(), PROCESSOR_CPU_LOAD_INFO, &count, &info, &infoCount) == KERN_SUCCESS,
              let info else { return }
        defer {
            vm_deallocate(mach_task_self_, vm_address_t(bitPattern: info), vm_size_t(Int(infoCount) * MemoryLayout<integer_t>.stride))
        }

        var ticks: [[UInt32]] = []
        for cpu in 0..<Int(count) {
            let base = cpu * Int(CPU_STATE_MAX)
            ticks.append((0..<Int(CPU_STATE_MAX)).map { UInt32(bitPattern: info[base + $0]) })
        }
        defer { prevTicks = ticks }
        guard prevTicks.count == ticks.count else { return }

        var percents: [Double] = []
        var busyAll: UInt64 = 0, totalAll: UInt64 = 0
        for (now, before) in zip(ticks, prevTicks) {
            let d = (0..<Int(CPU_STATE_MAX)).map { UInt64(now[$0] &- before[$0]) }
            let idle = d[Int(CPU_STATE_IDLE)]
            let total = d.reduce(0, +)
            busyAll += total - idle
            totalAll += total
            percents.append(total > 0 ? Double(total - idle) / Double(total) * 100 : 0)
        }
        s.cpuPercent = totalAll > 0 ? Double(busyAll) / Double(totalAll) * 100 : 0
        let e = min(eCores, percents.count)
        s.eCorePercents = Array(percents.prefix(e))
        s.pCorePercents = Array(percents.dropFirst(e))
    }

    // MARK: Memory

    private func sampleMemory(_ s: inout SystemStats) {
        var stats = vm_statistics64()
        var size = mach_msg_type_number_t(MemoryLayout<vm_statistics64>.stride / MemoryLayout<integer_t>.stride)
        let r = withUnsafeMutablePointer(to: &stats) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(size)) { host_statistics64(mach_host_self(), HOST_VM_INFO64, $0, &size) }
        }
        let page = Double(vm_kernel_page_size)
        s.memTotal = Double(ProcessInfo.processInfo.physicalMemory)
        if r == KERN_SUCCESS {
            // Activity Monitor's "Memory Used": app memory + wired + compressed.
            let app = Double(stats.internal_page_count) - Double(stats.purgeable_count)
            s.memUsed = (app + Double(stats.wire_count) + Double(stats.compressor_page_count)) * page
        }
        if let level = SystemSampler.sysctlInt("kern.memorystatus_level") {
            s.memPressure = Double(100 - level)
        }
        var swap = xsw_usage()
        var swapSize = MemoryLayout<xsw_usage>.size
        if sysctlbyname("vm.swapusage", &swap, &swapSize, nil, 0) == 0 {
            s.swapUsed = Double(swap.xsu_used)
        }
    }

    // MARK: Disk

    private func sampleDisk(_ s: inout SystemStats) {
        let url = URL(fileURLWithPath: "/")
        guard let v = try? url.resourceValues(forKeys: [.volumeTotalCapacityKey, .volumeAvailableCapacityForImportantUsageKey]),
              let total = v.volumeTotalCapacity else { return }
        let free = Double(v.volumeAvailableCapacityForImportantUsage ?? 0)
        s.diskTotal = Double(total)
        s.diskUsed = Double(total) - free
    }

    // MARK: Network

    private func sampleNetwork(_ s: inout SystemStats) {
        var rx: UInt64 = 0, tx: UInt64 = 0
        var addrs: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&addrs) == 0, let first = addrs else { return }
        defer { freeifaddrs(addrs) }
        for p in sequence(first: first, next: { $0.pointee.ifa_next }) {
            let ifa = p.pointee
            guard let addr = ifa.ifa_addr, addr.pointee.sa_family == UInt8(AF_LINK),
                  String(cString: ifa.ifa_name).hasPrefix("en"),
                  let data = ifa.ifa_data?.assumingMemoryBound(to: if_data.self) else { continue }
            rx += UInt64(data.pointee.ifi_ibytes)
            tx += UInt64(data.pointee.ifi_obytes)
        }
        let now = Date()
        if let prev = prevNet {
            let dt = max(now.timeIntervalSince(prev.time), 0.001)
            s.rxPerSec = Double(rx >= prev.rx ? rx - prev.rx : 0) / dt
            s.txPerSec = Double(tx >= prev.tx ? tx - prev.tx : 0) / dt
        }
        prevNet = (rx, tx, now)
    }

    static func primaryIPv4() -> String? {
        var addrs: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&addrs) == 0, let first = addrs else { return nil }
        defer { freeifaddrs(addrs) }
        var found: [(String, String)] = []
        for p in sequence(first: first, next: { $0.pointee.ifa_next }) {
            let ifa = p.pointee
            guard let addr = ifa.ifa_addr, addr.pointee.sa_family == UInt8(AF_INET),
                  (ifa.ifa_flags & UInt32(IFF_UP)) != 0, (ifa.ifa_flags & UInt32(IFF_LOOPBACK)) == 0 else { continue }
            var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            if getnameinfo(addr, socklen_t(addr.pointee.sa_len), &host, socklen_t(host.count), nil, 0, NI_NUMERICHOST) == 0 {
                found.append((String(cString: ifa.ifa_name), String(cString: host)))
            }
        }
        return (found.first { $0.0.hasPrefix("en") } ?? found.first)?.1
    }

    // MARK: GPU

    static func gpuUtilization() -> Double? {
        var iterator: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("IOAccelerator"), &iterator) == KERN_SUCCESS else { return nil }
        defer { IOObjectRelease(iterator) }
        var result: Double?
        while case let service = IOIteratorNext(iterator), service != 0 {
            defer { IOObjectRelease(service) }
            if let stats = IORegistryEntryCreateCFProperty(service, "PerformanceStatistics" as CFString, kCFAllocatorDefault, 0)?
                .takeRetainedValue() as? [String: Any],
               let util = stats["Device Utilization %"] as? NSNumber {
                result = max(result ?? 0, util.doubleValue)
            }
        }
        return result
    }

    // MARK: Static info

    /// Wall-clock time since boot (systemUptime leaves out time asleep).
    static func uptime() -> TimeInterval {
        var boot = timeval()
        var size = MemoryLayout<timeval>.size
        guard sysctlbyname("kern.boottime", &boot, &size, nil, 0) == 0 else { return ProcessInfo.processInfo.systemUptime }
        return Date().timeIntervalSince1970 - (Double(boot.tv_sec) + Double(boot.tv_usec) / 1e6)
    }

    static func sysctlInt(_ name: String) -> Int? {
        var value: Int64 = 0
        var size = MemoryLayout<Int64>.size
        guard sysctlbyname(name, &value, &size, nil, 0) == 0 else { return nil }
        return size == 4 ? Int(Int32(truncatingIfNeeded: value)) : Int(value)
    }

    static func sysctlString(_ name: String) -> String? {
        var size = 0
        guard sysctlbyname(name, nil, &size, nil, 0) == 0, size > 0 else { return nil }
        var buf = [CChar](repeating: 0, count: size)
        guard sysctlbyname(name, &buf, &size, nil, 0) == 0 else { return nil }
        return String(cString: buf)
    }

    static func chipName() -> String {
        let brand = sysctlString("machdep.cpu.brand_string") ?? ""
        return brand.replacingOccurrences(of: "Apple ", with: "")
    }

    static func hostName() -> String {
        let name = Host.current().localizedName ?? ProcessInfo.processInfo.hostName
        return name
    }

    /// Marketing name such as "Mac Studio", from the model identifier.
    static func modelName() -> String {
        let id = sysctlString("hw.model") ?? ""
        let known: [String: String] = [
            "Mac13,1": "Mac Studio", "Mac13,2": "Mac Studio", "Mac14,13": "Mac Studio", "Mac14,14": "Mac Studio",
            "Mac15,14": "Mac Studio", "Mac16,9": "Mac Studio",
            "Macmini9,1": "Mac mini", "Mac14,3": "Mac mini", "Mac14,12": "Mac mini", "Mac16,10": "Mac mini", "Mac16,11": "Mac mini",
            "Mac14,8": "Mac Pro",
        ]
        if let name = known[id] { return name }
        if id.hasPrefix("MacBookPro") { return "MacBook Pro" }
        if id.hasPrefix("MacBookAir") { return "MacBook Air" }
        if id.hasPrefix("iMac") { return "iMac" }
        return "Mac"
    }
}
