// swift-tools-version:6.0
import PackageDescription

let package = Package(
    name: "AX206Mac",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "AX206Display", targets: ["AX206Display"]),
        .executable(name: "ax206ctl", targets: ["ax206ctl"]),
    ],
    targets: [
        // Declarations for the private libIOReport (SoC energy counters).
        .target(
            name: "CIOReport",
            linkerSettings: [.linkedLibrary("IOReport")]
        ),
        // USB driver, sensors, renderer and screens. No UI.
        .target(
            name: "AX206Kit",
            dependencies: ["CIOReport"],
            linkerSettings: [
                .linkedFramework("IOKit"),
                .linkedFramework("IOUSBHost"),
            ]
        ),
        // Menu bar app with a live preview window.
        .executableTarget(name: "AX206Display", dependencies: ["AX206Kit"]),
        // Dev tool: list sensors, render screens to PNG, push test frames.
        .executableTarget(name: "ax206ctl", dependencies: ["AX206Kit"]),
    ],
    swiftLanguageModes: [.v5]
)
