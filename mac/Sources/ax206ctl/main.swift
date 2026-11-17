import AX206Kit
import Foundation

let args = CommandLine.arguments.dropFirst()
switch args.first {
case "stats":
    let sampler = SystemSampler()
    _ = sampler.sample()
    Thread.sleep(forTimeInterval: 1)
    let s = sampler.sample()
    print(s.modelName, s.chip, s.hostname, s.ip)
    print("cpu", Int(s.cpuPercent), "E", s.eCorePercents.map { Int($0) }, "P", s.pCorePercents.map { Int($0) }, "temp", s.cpuTemp as Any)
    print("gpu", s.gpuPercent, "temp", s.gpuTemp as Any)
    print("mem", s.memUsed / 1e9, s.memTotal / 1e9, "pressure", s.memPressure, "swap", s.swapUsed / 1e9)
    print("disk", s.diskUsed / 1e9, s.diskTotal / 1e9, "net", s.rxPerSec, s.txPerSec)
    print("power system", s.systemPower as Any, "cpu", s.cpuPower as Any, "gpu", s.gpuPower as Any)
    print("fans", s.fans.map { "\(Int($0.rpm)) [\(Int($0.min))-\(Int($0.max))]" })
case "bars":
    // Colour bars, then timing over 10 frames.
    do {
        let dev = try AX206Device.open()
        let colors: [UInt16] = [0xF800, 0x07E0, 0x001F, 0xFFE0, 0x07FF, 0xF81F, 0xFFFF, 0x0000]
        var frame = Data(count: AX206Device.frameBytes)
        frame.withUnsafeMutableBytes { raw in
            let p = raw.bindMemory(to: UInt8.self)
            for y in 0..<AX206Device.height {
                for x in 0..<AX206Device.width {
                    let c = colors[x * colors.count / AX206Device.width]
                    let o = (y * AX206Device.width + x) * 2
                    p[o] = UInt8(c >> 8); p[o + 1] = UInt8(c & 0xFF)
                }
            }
        }
        let start = Date()
        for _ in 0..<10 { try dev.blit(frame) }
        print(String(format: "10 frames in %.2fs (%.1f fps)", Date().timeIntervalSince(start), 10 / Date().timeIntervalSince(start)))
    } catch { print("error:", error); exit(1) }
case "render":
    // Renders every screen to PNG: ax206ctl render [dir]
    let dir = URL(fileURLWithPath: args.dropFirst().first ?? ".")
    let sampler = SystemSampler()
    _ = sampler.sample()
    var stats = sampler.sample()
    for _ in 0..<40 { Thread.sleep(forTimeInterval: 0.05); stats = sampler.sample() }
    MainActor.assumeIsolated {
        let renderer = FrameRenderer()
        let ctx = ScreenContext(now: Date(), stats: stats, use24Hour: true)
        for screen in ScreenCatalog.all {
            guard let frame = renderer.render(screen.view(ctx)) else { print("render failed:", screen.id); continue }
            let url = dir.appendingPathComponent("\(screen.id).png")
            try? FrameRenderer.writePNG(frame.image, to: url)
            print(url.path)
        }
    }
case "show":
    // Pushes one screen to the panel for N seconds: ax206ctl show clock|system [seconds]
    let id = args.dropFirst().first ?? "clock"
    let seconds = Int(args.dropFirst(2).first ?? "10") ?? 10
    guard let screen = ScreenCatalog.all.first(where: { $0.id == id }) else { print("unknown screen", id); exit(1) }
    do {
        let dev = try AX206Device.open()
        let sampler = SystemSampler()
        _ = sampler.sample()
        try MainActor.assumeIsolated {
            let renderer = FrameRenderer()
            let end = Date().addingTimeInterval(TimeInterval(seconds))
            while Date() < end {
                let ctx = ScreenContext(now: Date(), stats: sampler.sample(), use24Hour: true)
                if let frame = renderer.render(screen.view(ctx)) { try dev.blit(frame.rgb565) }
                Thread.sleep(forTimeInterval: 0.5)
            }
        }
    } catch { print("error:", error); exit(1) }
case "smc":
    guard let smc = SMC() else { print("SMC unavailable"); exit(1) }
    let prefix = args.dropFirst().first ?? ""
    for key in smc.allKeys() where key.hasPrefix(prefix) {
        if let v = smc.read(key) {
            print(key, v.type, v.number.map { String(format: "%.2f", $0) } ?? v.bytes.map { String(format: "%02x", $0) }.joined())
        }
    }
default:
    print("usage: ax206ctl stats | smc [prefix] | bars | render [dir] | show clock|system [seconds]")
}
