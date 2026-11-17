import Foundation
import IOKit
import IOUSBHost

public struct AX206Error: Error, CustomStringConvertible {
    public let description: String
    init(_ description: String) { self.description = description }
}

/// AX206 USB LCD (VID 1908 / PID 0102, 480x320) over its vendor SCSI-over-bulk protocol,
/// using Apple's IOUSBHost framework. No kernel extension or libusb needed: macOS leaves the
/// display's vendor interface (class 0xDC) unclaimed, so user space can open it directly.
///
/// Every command is a 31-byte Command Block Wrapper ("USBC") on endpoint 0x01, optional data,
/// then a 13-byte Command Status Wrapper ("USBS") from endpoint 0x81. Pixels are RGB565
/// big-endian. Same protocol as the Windows driver in windows/AX206.Core/Usb/Ax206Device.cs.
public final class AX206Device {
    public static let vendorID = 0x1908
    public static let productID = 0x0102
    public static let width = 480
    public static let height = 320
    public static let frameBytes = width * height * 2

    private let interface: IOUSBHostInterface
    private let pipeOut: IOUSBHostPipe
    private let pipeIn: IOUSBHostPipe
    public let registryID: UInt64

    private init(interface: IOUSBHostInterface, pipeOut: IOUSBHostPipe, pipeIn: IOUSBHostPipe, registryID: UInt64) {
        self.interface = interface
        self.pipeOut = pipeOut
        self.pipeIn = pipeIn
        self.registryID = registryID
    }

    deinit { interface.destroy() }

    /// True when an AX206 is plugged in (doesn't open it).
    public static var isPresent: Bool {
        let service = matchInterface()
        if service != 0 { IOObjectRelease(service) }
        return service != 0
    }

    private static func matchInterface() -> io_service_t {
        let matching = IOServiceMatching("IOUSBHostInterface") as NSMutableDictionary
        matching["idVendor"] = vendorID
        matching["idProduct"] = productID
        matching["bConfigurationValue"] = 1
        matching["bInterfaceNumber"] = 0
        return IOServiceGetMatchingService(kIOMainPortDefault, matching)
    }

    public static func open() throws -> AX206Device {
        let service = matchInterface()
        guard service != 0 else { throw AX206Error("AX206 display not found (VID 1908 / PID 0102).") }
        defer { IOObjectRelease(service) }

        var id: UInt64 = 0
        IORegistryEntryGetRegistryEntryID(service, &id)

        let interface: IOUSBHostInterface
        do {
            interface = try IOUSBHostInterface(__ioService: service, options: [], queue: nil, interestHandler: nil)
        } catch {
            throw AX206Error("Couldn't open the display's USB interface: \(error.localizedDescription)")
        }
        do {
            let out = try interface.copyPipe(withAddress: 0x01)
            let inp = try interface.copyPipe(withAddress: 0x81)
            try? out.clearStall()
            try? inp.clearStall()
            return AX206Device(interface: interface, pipeOut: out, pipeIn: inp, registryID: id)
        } catch {
            interface.destroy()
            throw AX206Error("Couldn't open the display's bulk endpoints: \(error.localizedDescription)")
        }
    }

    /// Sends a full 480x320 frame of big-endian RGB565 pixels.
    public func blit(_ rgb565BE: Data) throws {
        try blit(x0: 0, y0: 0, x1: AX206Device.width, y1: AX206Device.height, rgb565BE)
    }

    public func blit(x0: Int, y0: Int, x1: Int, y1: Int, _ pixels: Data) throws {
        let bytes = (x1 - x0) * (y1 - y0) * 2
        guard pixels.count == bytes else { throw AX206Error("Expected \(bytes) bytes, got \(pixels.count).") }

        var cdb = [UInt8](repeating: 0, count: 16)
        cdb[0] = 0xCD
        cdb[5] = 0x06
        cdb[6] = 0x12 // BLIT
        put16(&cdb, 7, x0)
        put16(&cdb, 9, y0)
        put16(&cdb, 11, x1 - 1)
        put16(&cdb, 13, y1 - 1)

        try write(AX206Device.commandBlock(dataLength: bytes, directionIn: false, cdb: cdb), "CBW")
        try write(pixels, "frame data")
        try readStatus()
    }

    /// Bulk-Only Mass Storage Reset, then clear both endpoints and drain stale status.
    public func recover() {
        let request = IOUSBDeviceRequest(bmRequestType: 0x21, bRequest: 0xFF, wValue: 0, wIndex: 0, wLength: 0)
        var done = 0
        try? interface.__send(request, data: nil, bytesTransferred: &done, completionTimeout: 4)
        try? pipeOut.clearStall()
        try? pipeIn.clearStall()
        for _ in 0..<3 {
            let drain = NSMutableData(length: 64)!
            var n = 0
            guard (try? pipeIn.__sendIORequest(with: drain, bytesTransferred: &n, completionTimeout: 0.06)) != nil, n > 0 else { break }
        }
    }

    // MARK: Protocol

    private static func commandBlock(dataLength: Int, directionIn: Bool, cdb: [UInt8]) -> Data {
        var cbw = [UInt8](repeating: 0, count: 31)
        cbw[0...3] = [0x55, 0x53, 0x42, 0x43] // "USBC"
        cbw[4...7] = [0xDE, 0xAD, 0xBE, 0xEF] // tag
        for i in 0..<4 { cbw[8 + i] = UInt8((dataLength >> (8 * i)) & 0xFF) }
        cbw[12] = directionIn ? 0x80 : 0x00
        cbw[13] = 0  // LUN
        cbw[14] = 16 // CDB length
        cbw.replaceSubrange(15..<31, with: cdb)
        return Data(cbw)
    }

    private func put16(_ b: inout [UInt8], _ at: Int, _ v: Int) {
        b[at] = UInt8(v & 0xFF)
        b[at + 1] = UInt8((v >> 8) & 0xFF)
    }

    private func write(_ data: Data, _ what: String) throws {
        let buffer = NSMutableData(data: data)
        var sent = 0
        do {
            try pipeOut.__sendIORequest(with: buffer, bytesTransferred: &sent, completionTimeout: 4)
        } catch {
            throw AX206Error("Bulk write of \(what) failed: \(error.localizedDescription)")
        }
        guard sent == data.count else { throw AX206Error("Bulk write of \(what) was short (\(sent)/\(data.count)).") }
    }

    private func readStatus() throws {
        let csw = NSMutableData(length: 13)!
        var got = 0
        do {
            try pipeIn.__sendIORequest(with: csw, bytesTransferred: &got, completionTimeout: 2)
        } catch {
            throw AX206Error("Status read failed: \(error.localizedDescription)")
        }
        let b = [UInt8](csw as Data)
        guard got >= 13 else { throw AX206Error("Status read was short (\(got) bytes).") }
        guard b[0...3] == [0x55, 0x53, 0x42, 0x53], b[12] == 0 else {
            throw AX206Error("Display reported failure (status \(b[12])).")
        }
    }
}
