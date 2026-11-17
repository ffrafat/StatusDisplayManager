import CoreGraphics
import Foundation
import SwiftUI

/// A rendered frame: a 480x320 image for previews and the same pixels as RGB565 for the panel.
public struct Frame {
    public let image: CGImage
    public let rgb565: Data
}

/// Renders a SwiftUI view at 2x, downsamples to the panel's 480x320 and converts to
/// big-endian RGB565.
@MainActor
public final class FrameRenderer {
    public static let size = CGSize(width: AX206Device.width, height: AX206Device.height)

    private let context: CGContext

    public init() {
        context = CGContext(data: nil, width: AX206Device.width, height: AX206Device.height,
                            bitsPerComponent: 8, bytesPerRow: AX206Device.width * 4,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!,
                            bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)!
        context.interpolationQuality = .high
    }

    public func render<V: View>(_ view: V) -> Frame? {
        let content = view
            .frame(width: FrameRenderer.size.width, height: FrameRenderer.size.height)
            .background(Theme.background)
            .environment(\.colorScheme, .dark)
        let renderer = ImageRenderer(content: content)
        renderer.scale = 2
        renderer.isOpaque = true
        guard let hiRes = renderer.cgImage else { return nil }

        let rect = CGRect(origin: .zero, size: FrameRenderer.size)
        context.setFillColor(CGColor(gray: 0, alpha: 1))
        context.fill(rect)
        context.draw(hiRes, in: rect)
        guard let image = context.makeImage(), let base = context.data else { return nil }

        // BGRA (little-endian premultiplied-first) -> RGB565 big-endian.
        let src = base.bindMemory(to: UInt8.self, capacity: AX206Device.width * AX206Device.height * 4)
        var out = Data(count: AX206Device.frameBytes)
        out.withUnsafeMutableBytes { raw in
            let dst = raw.bindMemory(to: UInt8.self)
            for i in 0..<(AX206Device.width * AX206Device.height) {
                let b = UInt16(src[i * 4]), g = UInt16(src[i * 4 + 1]), r = UInt16(src[i * 4 + 2])
                let v = (r & 0xF8) << 8 | (g & 0xFC) << 3 | b >> 3
                dst[i * 2] = UInt8(v >> 8)
                dst[i * 2 + 1] = UInt8(v & 0xFF)
            }
        }
        return Frame(image: image, rgb565: out)
    }

    /// Writes a frame's preview image as PNG (used by `ax206ctl render`).
    public static func writePNG(_ image: CGImage, to url: URL) throws {
        guard let dest = CGImageDestinationCreateWithURL(url as CFURL, "public.png" as CFString, 1, nil) else {
            throw AX206Error("Can't write \(url.path)")
        }
        CGImageDestinationAddImage(dest, image, nil)
        guard CGImageDestinationFinalize(dest) else { throw AX206Error("Can't write \(url.path)") }
    }
}
