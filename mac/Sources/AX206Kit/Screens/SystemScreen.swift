import SwiftUI

/// Mac Studio dashboard: CPU, GPU, memory, power, fans, network and disk as widgets.
public struct SystemScreen: DisplayScreen {
    public let id = "system"
    public let title = "System"
    public let symbol = "macstudio"

    public init() {}

    public func view(_ context: ScreenContext) -> AnyView {
        AnyView(SystemView(stats: context.stats))
    }
}

struct SystemView: View {
    let stats: SystemStats?

    var body: some View {
        if let s = stats {
            VStack(spacing: 8) {
                header(s)
                HStack(spacing: 8) {
                    cpu(s).frame(width: 228)
                    gpu(s)
                    memory(s)
                }
                HStack(spacing: 8) {
                    power(s).frame(width: 178)
                    fans(s).frame(width: 138)
                    io(s)
                }
            }
            .padding(10)
        } else {
            VStack(spacing: 10) {
                Image(systemName: "macstudio").font(.system(size: 44, weight: .light)).foregroundStyle(Theme.secondary)
                Text("Gathering metrics…").font(Theme.text(16, .medium)).foregroundStyle(Theme.secondary)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    // MARK: Header

    private func header(_ s: SystemStats) -> some View {
        HStack(spacing: 7) {
            Image(systemName: symbolFor(s.modelName))
                .font(.system(size: 15, weight: .medium))
                .foregroundStyle(Theme.primary)
            Text(s.modelName)
                .font(Theme.text(15, .bold))
                .foregroundStyle(Theme.primary)
            Text(s.chip)
                .font(Theme.text(13, .semibold))
                .foregroundStyle(Theme.secondary)
            Spacer(minLength: 4)
            Label(Format.uptime(s.uptime), systemImage: "clock.arrow.circlepath")
                .font(Theme.text(11, .medium))
                .foregroundStyle(Theme.secondary)
            Text(s.ip)
                .font(Theme.text(11, .medium))
                .monospacedDigit()
                .foregroundStyle(Theme.tertiary)
        }
        .padding(.horizontal, 4)
        .frame(height: 20)
    }

    private func symbolFor(_ model: String) -> String {
        switch model {
        case "Mac Studio": return "macstudio"
        case "Mac mini": return "macmini"
        case "Mac Pro": return "macpro.gen3"
        case "iMac": return "desktopcomputer"
        default: return "laptopcomputer"
        }
    }

    // MARK: CPU

    private func cpu(_ s: SystemStats) -> some View {
        Card {
            VStack(alignment: .leading, spacing: 0) {
                CardHeader("cpu", "CPU", tint: Theme.blue) { temperature(s.cpuTemp) }
                HStack(alignment: .bottom, spacing: 8) {
                    percent(s.cpuPercent)
                    Sparkline(values: s.cpuHistory, tint: Theme.blue, range: 0...100)
                        .frame(height: 30)
                        .padding(.bottom, 4)
                }
                .padding(.top, 2)
                Spacer(minLength: 4)
                coreBars(s)
            }
        }
    }

    private func coreBars(_ s: SystemStats) -> some View {
        HStack(alignment: .bottom, spacing: 8) {
            coreGroup("E", s.eCorePercents, Theme.teal)
            coreGroup("P", s.pCorePercents, Theme.blue)
        }
    }

    private func coreGroup(_ label: String, _ values: [Double], _ tint: Color) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(alignment: .bottom, spacing: 3) {
                ForEach(Array(values.enumerated()), id: \.offset) { _, v in
                    ZStack(alignment: .bottom) {
                        RoundedRectangle(cornerRadius: 2.5, style: .continuous).fill(Theme.track)
                        RoundedRectangle(cornerRadius: 2.5, style: .continuous)
                            .fill(tint)
                            .frame(height: max(3, 30 * CGFloat(min(100, v)) / 100))
                    }
                    .frame(width: 13, height: 30)
                }
            }
            Text("\(label)-cores")
                .font(Theme.text(9, .semibold))
                .foregroundStyle(Theme.tertiary)
        }
    }

    // MARK: GPU / Memory

    private func gpu(_ s: SystemStats) -> some View {
        Card {
            VStack(spacing: 0) {
                CardHeader("cube.transparent", "GPU", tint: Theme.purple)
                Spacer(minLength: 2)
                RingGauge(fraction: s.gpuPercent / 100, tint: Theme.purple, label: "\(Int(s.gpuPercent.rounded()))%")
                Spacer(minLength: 2)
                HStack(spacing: 3) {
                    Image(systemName: "thermometer.medium").font(.system(size: 10, weight: .semibold))
                    Text(Format.temp(s.gpuTemp)).font(Theme.rounded(12, .semibold))
                }
                .foregroundStyle(tempColor(s.gpuTemp))
            }
        }
    }

    private func memory(_ s: SystemStats) -> some View {
        let tint = s.memPressure >= 80 ? Theme.red : s.memPressure >= 55 ? Theme.yellow : Theme.green
        return Card {
            VStack(spacing: 0) {
                CardHeader("memorychip", "MEMORY", tint: tint)
                Spacer(minLength: 2)
                RingGauge(fraction: s.memPercent / 100, tint: tint, label: "\(Int(s.memPercent.rounded()))%")
                Spacer(minLength: 2)
                Text("\(Format.gib(s.memUsed)) / \(Format.gib(s.memTotal)) GB")
                    .font(Theme.rounded(12, .semibold))
                    .foregroundStyle(Theme.secondary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
            }
        }
    }

    // MARK: Power

    private func power(_ s: SystemStats) -> some View {
        Card {
            VStack(alignment: .leading, spacing: 0) {
                CardHeader("bolt.fill", "POWER", tint: Theme.yellow)
                HStack(alignment: .firstTextBaseline, spacing: 3) {
                    Text(s.systemPower.map { String(format: $0 >= 100 ? "%.0f" : "%.1f", $0) } ?? "—")
                        .font(Theme.rounded(30, .bold))
                        .monospacedDigit()
                        .foregroundStyle(Theme.primary)
                    Text("W")
                        .font(Theme.rounded(16, .semibold))
                        .foregroundStyle(Theme.secondary)
                    Spacer(minLength: 0)
                    Text("system")
                        .font(Theme.text(10, .medium))
                        .foregroundStyle(Theme.tertiary)
                }
                .padding(.top, 1)
                Sparkline(values: s.powerHistory, tint: Theme.yellow)
                    .frame(height: 22)
                Spacer(minLength: 4)
                HStack(spacing: 8) {
                    powerPart("CPU", s.cpuPower, Theme.blue)
                    powerPart("GPU", s.gpuPower, Theme.purple)
                }
            }
        }
    }

    private func powerPart(_ name: String, _ watts: Double?, _ tint: Color) -> some View {
        HStack(spacing: 4) {
            Circle().fill(tint).frame(width: 6, height: 6)
            Text(name).font(Theme.text(11, .semibold)).foregroundStyle(Theme.secondary)
            Text(Format.watts(watts)).font(Theme.rounded(12, .bold)).monospacedDigit().foregroundStyle(Theme.primary)
        }
        .fixedSize()
    }

    // MARK: Fans

    private func fans(_ s: SystemStats) -> some View {
        Card {
            VStack(alignment: .leading, spacing: 0) {
                CardHeader("fan.fill", "FANS", tint: Theme.teal)
                Spacer(minLength: 4)
                if s.fans.isEmpty {
                    Text("No fans").font(Theme.text(12, .medium)).foregroundStyle(Theme.tertiary)
                    Spacer(minLength: 0)
                } else {
                    VStack(alignment: .leading, spacing: 10) {
                        ForEach(Array(s.fans.enumerated()), id: \.offset) { i, fan in
                            VStack(alignment: .leading, spacing: 4) {
                                HStack(alignment: .firstTextBaseline, spacing: 3) {
                                    Text(s.fans.count > 1 ? "\(i + 1)" : "")
                                        .font(Theme.text(10, .bold))
                                        .foregroundStyle(Theme.tertiary)
                                        .frame(width: s.fans.count > 1 ? 8 : 0, alignment: .leading)
                                    Text(Format.rpm(fan.rpm))
                                        .font(Theme.rounded(17, .bold))
                                        .monospacedDigit()
                                        .foregroundStyle(Theme.primary)
                                    Text("rpm")
                                        .font(Theme.text(10, .semibold))
                                        .foregroundStyle(Theme.secondary)
                                }
                                Meter(fraction: fan.fraction, tint: Theme.teal, height: 5)
                            }
                        }
                    }
                }
            }
        }
    }

    // MARK: Network + disk

    private func io(_ s: SystemStats) -> some View {
        Card {
            VStack(alignment: .leading, spacing: 0) {
                CardHeader("network", "NETWORK", tint: Theme.orange)
                VStack(alignment: .leading, spacing: 2) {
                    rate("arrow.down", s.rxPerSec, Theme.green)
                    rate("arrow.up", s.txPerSec, Theme.orange)
                }
                .padding(.top, 5)
                Spacer(minLength: 4)
                HStack(spacing: 4) {
                    Image(systemName: "internaldrive").font(.system(size: 10, weight: .semibold)).foregroundStyle(Theme.mint)
                    Text("\(Format.gb(s.diskTotal - s.diskUsed)) GB free")
                        .font(Theme.text(10, .semibold))
                        .foregroundStyle(Theme.secondary)
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                }
                Meter(fraction: s.diskPercent / 100, tint: Theme.mint, height: 5)
                    .padding(.top, 4)
            }
        }
    }

    private func rate(_ symbol: String, _ value: Double, _ tint: Color) -> some View {
        HStack(spacing: 4) {
            Image(systemName: symbol).font(.system(size: 10, weight: .heavy)).foregroundStyle(tint)
            Text(Format.rate(value))
                .font(Theme.rounded(13, .semibold))
                .monospacedDigit()
                .foregroundStyle(Theme.primary)
                .lineLimit(1)
                .minimumScaleFactor(0.7)
        }
    }

    // MARK: Bits

    private func percent(_ v: Double) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 1) {
            Text("\(Int(v.rounded()))")
                .font(Theme.rounded(30, .bold))
                .monospacedDigit()
                .foregroundStyle(Theme.primary)
            Text("%")
                .font(Theme.rounded(16, .semibold))
                .foregroundStyle(Theme.secondary)
        }
        .fixedSize()
    }

    private func temperature(_ t: Double?) -> some View {
        HStack(spacing: 2) {
            Image(systemName: "thermometer.medium").font(.system(size: 10, weight: .semibold))
            Text(Format.temp(t)).font(Theme.rounded(12, .semibold))
        }
        .foregroundStyle(tempColor(t))
    }

    private func tempColor(_ t: Double?) -> Color {
        guard let t else { return Theme.secondary }
        return t >= 90 ? Theme.red : t >= 75 ? Theme.orange : Theme.secondary
    }
}
