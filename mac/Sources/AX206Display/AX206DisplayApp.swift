import AppKit
import SwiftUI

extension Notification.Name {
    static let openSettings = Notification.Name("AX206OpenSettings")
}

/// Opening the app again (Finder, Spotlight, `open`) while it runs shows the Settings window.
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows: Bool) -> Bool {
        NotificationCenter.default.post(name: .openSettings, object: nil)
        return false
    }
}

@main
struct AX206DisplayApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var controller = DisplayController()

    var body: some Scene {
        MenuBarExtra {
            MenuContent()
                .environmentObject(controller)
                .onAppear { controller.start() }
        } label: {
            MenuBarIcon()
                .environmentObject(controller)
        }
        .menuBarExtraStyle(.window)

        Window("AX206 Display", id: "main") {
            MainWindow()
                .environmentObject(controller)
        }
        .windowResizability(.contentSize)
    }
}

/// The menu bar icon. Always alive, so it also starts the loop and opens Settings on request.
struct MenuBarIcon: View {
    @EnvironmentObject var controller: DisplayController
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Image(systemName: controller.connected ? "rectangle.inset.filled" : "rectangle.dashed")
            .task { controller.start() }
            .onReceive(NotificationCenter.default.publisher(for: .openSettings)) { _ in
                openWindow(id: "main")
                NSApp.activate(ignoringOtherApps: true)
            }
    }
}

/// A 480x320 frame shown at a given width with a bezel.
struct PanelPreview: View {
    let image: CGImage?
    var width: CGFloat = 480

    var body: some View {
        Group {
            if let image {
                Image(decorative: image, scale: 1)
                    .resizable()
                    .interpolation(.high)
            } else {
                Color.black
            }
        }
        .frame(width: width, height: width * 2 / 3)
        .clipShape(RoundedRectangle(cornerRadius: 6, style: .continuous))
        .padding(6)
        .background(RoundedRectangle(cornerRadius: 11, style: .continuous).fill(Color(white: 0.08)))
        .overlay(RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(.white.opacity(0.08)))
    }
}

struct StatusBadge: View {
    @EnvironmentObject var controller: DisplayController

    var body: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(controller.connected ? Color.green : Color.orange)
                .frame(width: 7, height: 7)
            Text(controller.outputEnabled ? controller.status : "Output paused")
                .lineLimit(1)
                .truncationMode(.tail)
        }
        .font(.callout)
        .foregroundStyle(.secondary)
    }
}

struct MenuContent: View {
    @EnvironmentObject var controller: DisplayController
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            PanelPreview(image: controller.preview, width: 288)
            StatusBadge()

            VStack(spacing: 2) {
                ForEach(controller.screens) { screen in
                    ScreenRow(screen: screen)
                }
            }

            HStack {
                Toggle("Pause rotation", isOn: $controller.paused)
                    .toggleStyle(.switch)
                    .controlSize(.small)
                Spacer()
                if !controller.paused, !controller.nextTitle.isEmpty {
                    Text("\(controller.nextTitle) in \(controller.secondsLeft)s")
                        .font(.caption)
                        .monospacedDigit()
                        .foregroundStyle(.secondary)
                }
            }

            Divider()

            HStack {
                Button("Settings…") {
                    openWindow(id: "main")
                    NSApp.activate(ignoringOtherApps: true)
                }
                Spacer()
                Button("Quit") { NSApp.terminate(nil) }
            }
            .buttonStyle(.borderless)
        }
        .padding(12)
        .frame(width: 324)
    }
}

struct ScreenRow: View {
    @EnvironmentObject var controller: DisplayController
    let screen: DisplayController.ScreenInfo

    var body: some View {
        let current = controller.currentID == screen.id
        HStack(spacing: 8) {
            Image(systemName: screen.symbol)
                .frame(width: 18)
                .foregroundStyle(current ? Color.accentColor : .secondary)
            Text(screen.title)
                .fontWeight(current ? .semibold : .regular)
            Spacer()
            Toggle("", isOn: Binding(get: { controller.isEnabled(screen.id) },
                                     set: { controller.setEnabled(screen.id, $0) }))
                .toggleStyle(.switch)
                .controlSize(.mini)
                .labelsHidden()
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 5)
        .background(RoundedRectangle(cornerRadius: 6).fill(current ? Color.accentColor.opacity(0.15) : .clear))
        .contentShape(Rectangle())
        .onTapGesture { controller.show(screen.id) }
    }
}

struct MainWindow: View {
    @EnvironmentObject var controller: DisplayController

    var body: some View {
        HStack(alignment: .top, spacing: 20) {
            VStack(alignment: .leading, spacing: 12) {
                PanelPreview(image: controller.preview, width: 480)
                HStack {
                    StatusBadge()
                    Spacer()
                    Button {
                        controller.showNext()
                    } label: {
                        Label("Next Screen", systemImage: "forward.fill")
                    }
                }
            }
            .frame(width: 492)

            VStack(alignment: .leading, spacing: 18) {
                SettingsSection("Screens") {
                    ForEach(Array(controller.screens.enumerated()), id: \.element.id) { i, screen in
                        if i > 0 { Divider() }
                        ScreenSettingsRow(screen: screen)
                    }
                    Divider()
                    SettingsRow("Pause rotation") {
                        Toggle("", isOn: $controller.paused).labelsHidden()
                    }
                }
                SettingsSection("Clock") {
                    SettingsRow("24-hour time") {
                        Toggle("", isOn: $controller.use24Hour).labelsHidden()
                    }
                }
                SettingsSection("General") {
                    SettingsRow("Send frames to the display") {
                        Toggle("", isOn: $controller.outputEnabled).labelsHidden()
                    }
                    Divider()
                    SettingsRow("Open at login") {
                        Toggle("", isOn: Binding(get: { controller.launchAtLogin },
                                                 set: { controller.launchAtLogin = $0 })).labelsHidden()
                    }
                }
            }
            .toggleStyle(.switch)
            .controlSize(.small)
            .frame(width: 330)
        }
        .padding(20)
        .fixedSize()
    }
}

/// A titled, rounded group of rows in the style of System Settings.
struct SettingsSection<Content: View>: View {
    let title: String
    let content: Content

    init(_ title: String, @ViewBuilder content: () -> Content) {
        self.title = title
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title)
                .font(.headline)
                .padding(.leading, 4)
            VStack(spacing: 0) { content }
                .padding(.horizontal, 12)
                .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Color(nsColor: .controlBackgroundColor)))
                .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Color(nsColor: .separatorColor)))
        }
    }
}

struct SettingsRow<Control: View>: View {
    let title: String
    let control: Control

    init(_ title: String, @ViewBuilder control: () -> Control) {
        self.title = title
        self.control = control()
    }

    var body: some View {
        HStack {
            Text(title)
            Spacer(minLength: 12)
            control
        }
        .frame(minHeight: 34)
    }
}

/// Screen on/off plus how long it stays up.
struct ScreenSettingsRow: View {
    @EnvironmentObject var controller: DisplayController
    let screen: DisplayController.ScreenInfo

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: screen.symbol)
                .foregroundStyle(.secondary)
                .frame(width: 20)
            Text(screen.title)
            Spacer(minLength: 12)
            Text("\(controller.duration(screen.id)) s")
                .monospacedDigit()
                .foregroundStyle(.secondary)
                .frame(width: 44, alignment: .trailing)
            Stepper("", value: Binding(get: { controller.duration(screen.id) },
                                       set: { controller.setDuration(screen.id, $0) }),
                    in: 5...600, step: 5)
                .labelsHidden()
            Toggle("", isOn: Binding(get: { controller.isEnabled(screen.id) },
                                     set: { controller.setEnabled(screen.id, $0) }))
                .labelsHidden()
                .padding(.leading, 6)
        }
        .frame(minHeight: 34)
    }
}
