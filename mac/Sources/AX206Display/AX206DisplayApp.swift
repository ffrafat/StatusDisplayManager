import AppKit
import SwiftUI

@main
struct AX206DisplayApp: App {
    @StateObject private var controller = DisplayController()

    var body: some Scene {
        MenuBarExtra {
            MenuContent()
                .environmentObject(controller)
                .onAppear { controller.start() }
        } label: {
            Image(systemName: controller.connected ? "rectangle.inset.filled" : "rectangle.dashed")
                .task { controller.start() }
        }
        .menuBarExtraStyle(.window)

        Window("AX206 Display", id: "main") {
            MainWindow()
                .environmentObject(controller)
        }
        .windowResizability(.contentSize)
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
            VStack(alignment: .leading, spacing: 10) {
                PanelPreview(image: controller.preview, width: 480)
                HStack {
                    StatusBadge()
                    Spacer()
                    Button {
                        controller.showNext()
                    } label: {
                        Label("Next screen", systemImage: "forward.fill")
                    }
                }
            }

            Form {
                Section("Screens") {
                    ForEach(controller.screens) { screen in
                        HStack {
                            Toggle(isOn: Binding(get: { controller.isEnabled(screen.id) },
                                                 set: { controller.setEnabled(screen.id, $0) })) {
                                Label(screen.title, systemImage: screen.symbol)
                            }
                            Spacer()
                            Stepper(value: Binding(get: { controller.duration(screen.id) },
                                                   set: { controller.setDuration(screen.id, $0) }),
                                    in: 3...600, step: 5) {
                                Text("\(controller.duration(screen.id)) s").monospacedDigit()
                            }
                            .fixedSize()
                        }
                    }
                    Toggle("Pause rotation", isOn: $controller.paused)
                }
                Section("Clock") {
                    Toggle("24-hour time", isOn: $controller.use24Hour)
                }
                Section("General") {
                    Toggle("Send frames to the display", isOn: $controller.outputEnabled)
                    Toggle("Open at login", isOn: Binding(get: { controller.launchAtLogin },
                                                         set: { controller.launchAtLogin = $0 }))
                }
            }
            .formStyle(.grouped)
            .frame(width: 320)
        }
        .padding(20)
        .fixedSize()
    }
}
