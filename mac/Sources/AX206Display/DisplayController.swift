import AX206Kit
import Foundation
import SwiftUI

/// Owns the 1 Hz loop: sample the Mac, pick the screen, render, push to the panel.
@MainActor
final class DisplayController: ObservableObject {
    struct ScreenInfo: Identifiable {
        let id: String
        let title: String
        let symbol: String
    }

    // UI state
    @Published private(set) var preview: CGImage?
    @Published private(set) var connected = false
    @Published private(set) var status = "Looking for the display…"
    @Published private(set) var currentID: String
    @Published private(set) var secondsLeft = 0
    @Published private(set) var nextTitle = ""

    // Settings (persisted)
    @Published var paused: Bool { didSet { defaults.set(paused, forKey: "paused") } }
    @Published var use24Hour: Bool { didSet { defaults.set(use24Hour, forKey: "use24Hour") } }
    @Published var outputEnabled: Bool { didSet { defaults.set(outputEnabled, forKey: "outputEnabled") } }
    @Published private(set) var enabled: [String: Bool]
    @Published private(set) var durations: [String: Int]

    let screens: [ScreenInfo]
    private let catalog = ScreenCatalog.all
    private let defaults = UserDefaults.standard
    private let renderer = FrameRenderer()
    private let sampler = SystemSampler()
    private let sampleQueue = DispatchQueue(label: "ax206.sample", qos: .utility)
    private let link = PanelLink()
    private var latestStats: SystemStats?
    private var screenStart = Date()
    private var pushing = false
    /// Seconds from starting a push until the panel shows it; frames are drawn this far ahead.
    private var latency = 0.45
    private var loop: Task<Void, Never>?

    init() {
        screens = ScreenCatalog.all.map { ScreenInfo(id: $0.id, title: $0.title, symbol: $0.symbol) }
        currentID = screens[0].id
        defaults.register(defaults: ["use24Hour": true, "outputEnabled": true])
        paused = defaults.bool(forKey: "paused")
        use24Hour = defaults.bool(forKey: "use24Hour")
        outputEnabled = defaults.bool(forKey: "outputEnabled")
        enabled = defaults.dictionary(forKey: "enabled") as? [String: Bool] ?? [:]
        durations = defaults.dictionary(forKey: "durations") as? [String: Int] ?? [:]
        if let saved = defaults.string(forKey: "current"), screens.contains(where: { $0.id == saved }) {
            currentID = saved
        }
    }

    func start() {
        guard loop == nil else { return }
        LoginItem.refresh(defaults: defaults)
        loop = Task { [weak self] in
            while !Task.isCancelled {
                guard let self else { return }
                // Fire `latency` before each whole second and draw that second, so the
                // panel changes close to when the second actually turns over.
                let now = Date().timeIntervalSince1970
                var target = floor(now) + 1
                if target - latency - now < 0.05 { target += 1 }
                try? await Task.sleep(nanoseconds: UInt64((target - latency - now) * 1e9))
                await self.tick(at: Date(timeIntervalSince1970: target))
            }
        }
    }

    // MARK: Settings

    func isEnabled(_ id: String) -> Bool { enabled[id] ?? true }

    func setEnabled(_ id: String, _ on: Bool) {
        enabled[id] = on
        defaults.set(enabled, forKey: "enabled")
    }

    func duration(_ id: String) -> Int { durations[id] ?? 15 }

    func setDuration(_ id: String, _ seconds: Int) {
        durations[id] = max(3, min(600, seconds))
        defaults.set(durations, forKey: "durations")
    }

    func show(_ id: String) {
        currentID = id
        screenStart = Date()
        defaults.set(id, forKey: "current")
        Task { await tick(at: Date()) }
    }

    func showNext() {
        let list = available
        guard let i = list.firstIndex(where: { $0.id == currentID }) else { return }
        show(list[(i + 1) % list.count].id)
    }

    var launchAtLogin: Bool {
        get { LoginItem.isEnabled }
        set {
            do {
                if newValue { try LoginItem.enable() } else { try LoginItem.disable() }
            } catch {
                status = "Open at login: \(error.localizedDescription)"
            }
            objectWillChange.send()
        }
    }

    // MARK: Loop

    private var available: [ScreenInfo] {
        let on = screens.filter { isEnabled($0.id) }
        return on.isEmpty ? [screens[0]] : on
    }

    private func tick(at time: Date) async {
        latestStats = await withCheckedContinuation { cont in
            sampleQueue.async { [sampler] in cont.resume(returning: sampler.sample()) }
        }

        rotate(at: time)
        guard let screen = catalog.first(where: { $0.id == currentID }) else { return }
        let context = ScreenContext(now: time, stats: latestStats, use24Hour: use24Hour)
        guard let frame = renderer.render(screen.view(context)) else { return }
        preview = frame.image
        if outputEnabled { push(frame.rgb565) }
    }

    private func rotate(at time: Date) {
        let list = available
        if !list.contains(where: { $0.id == currentID }) {
            currentID = list[0].id
            screenStart = time
        } else if !paused, time.timeIntervalSince(screenStart) >= Double(duration(currentID)) - 0.01, list.count > 1 {
            let i = list.firstIndex { $0.id == currentID }!
            currentID = list[(i + 1) % list.count].id
            screenStart = time
            defaults.set(currentID, forKey: "current")
        }
        let i = list.firstIndex { $0.id == currentID } ?? 0
        nextTitle = list.count > 1 ? list[(i + 1) % list.count].title : ""
        secondsLeft = max(0, duration(currentID) - Int(time.timeIntervalSince(screenStart).rounded()))
    }

    private func push(_ pixels: Data) {
        guard !pushing else { return } // the panel is still busy with the previous frame
        pushing = true
        link.send(pixels) { [weak self] result in
            Task { @MainActor in
                guard let self else { return }
                self.pushing = false
                switch result {
                case .sent(let seconds):
                    self.latency = self.latency * 0.8 + min(0.9, seconds) * 0.2
                    self.connected = true
                    self.status = "Connected"
                case .failed(let message):
                    self.connected = false
                    self.status = message
                }
            }
        }
    }
}

/// The USB side. Everything runs on one serial queue; reconnects every 2 s when unplugged.
final class PanelLink: @unchecked Sendable {
    enum Result { case sent(TimeInterval), failed(String) }

    private let queue = DispatchQueue(label: "ax206.usb", qos: .userInitiated)
    private var device: AX206Device?
    private var lastAttempt = Date.distantPast
    private var lastError = "Display not connected"

    func send(_ pixels: Data, completion: @escaping @Sendable (Result) -> Void) {
        queue.async { [self] in completion(sendOnQueue(pixels)) }
    }

    private func sendOnQueue(_ pixels: Data) -> Result {
        if device == nil {
            guard Date().timeIntervalSince(lastAttempt) >= 2 else { return .failed(lastError) }
            lastAttempt = Date()
            guard AX206Device.isPresent else {
                lastError = "Display not connected"
                return .failed(lastError)
            }
            do {
                device = try AX206Device.open()
            } catch {
                lastError = "\(error)"
                return .failed(lastError)
            }
        }
        guard let device else { return .failed(lastError) }

        let start = Date()
        do {
            try device.blit(pixels)
            return .sent(Date().timeIntervalSince(start))
        } catch {
            device.recover()
            do {
                try device.blit(pixels)
                return .sent(Date().timeIntervalSince(start))
            } catch {
                self.device = nil
                lastError = "Lost the display: \(error)"
                return .failed(lastError)
            }
        }
    }
}
