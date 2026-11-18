import Foundation
import ServiceManagement

/// Starts the app at login with a per-user LaunchAgent that runs `open` on the app bundle.
///
/// SMAppService.mainApp is unreliable for ad-hoc signed apps (no team ID), and an agent that
/// runs the executable directly can't start an app kept under ~/Documents. `open` goes through
/// LaunchServices like a double-click in Finder, so it works wherever the app lives.
enum LoginItem {
    static let label = "io.github.ffrafat.AX206Display"

    static var plistURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/LaunchAgents/\(label).plist")
    }

    static var isEnabled: Bool { FileManager.default.fileExists(atPath: plistURL.path) }

    static func enable() throws {
        let plist: [String: Any] = [
            "Label": label,
            "ProgramArguments": ["/usr/bin/open", "-a", Bundle.main.bundlePath],
            "RunAtLoad": true,
            "LimitLoadToSessionType": "Aqua",
            // Lets System Settings > Login Items show this as "AX206 Display".
            "AssociatedBundleIdentifiers": [Bundle.main.bundleIdentifier ?? label],
        ]
        let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
        try FileManager.default.createDirectory(at: plistURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        try data.write(to: plistURL, options: .atomic)
    }

    static func disable() throws {
        if isEnabled { try FileManager.default.removeItem(at: plistURL) }
    }

    /// Call at launch. Turns the login item on the first time the app runs from a bundle,
    /// keeps the agent pointing at wherever the app now lives, and drops any older
    /// SMAppService registration so the app isn't started twice.
    static func refresh(defaults: UserDefaults = .standard) {
        guard Bundle.main.bundlePath.hasSuffix(".app") else { return }
        if SMAppService.mainApp.status == .enabled { try? SMAppService.mainApp.unregister() }
        if !defaults.bool(forKey: "loginItemConfigured") {
            defaults.set(true, forKey: "loginItemConfigured")
            try? enable()
        } else if isEnabled {
            try? enable()
        }
    }
}
