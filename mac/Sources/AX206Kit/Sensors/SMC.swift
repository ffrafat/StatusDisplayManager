import Foundation
import IOKit

/// Read-only access to the System Management Controller (AppleSMC user client).
/// On Apple Silicon this exposes fans, total system power and temperature sensors
/// without root.
public final class SMC {
    // Mirrors SMCKeyData_t from the AppleSMC kext (80 bytes).
    private struct KeyData {
        typealias Bytes = (UInt8, UInt8, UInt8, UInt8, UInt8, UInt8, UInt8, UInt8,
                           UInt8, UInt8, UInt8, UInt8, UInt8, UInt8, UInt8, UInt8,
                           UInt8, UInt8, UInt8, UInt8, UInt8, UInt8, UInt8, UInt8,
                           UInt8, UInt8, UInt8, UInt8, UInt8, UInt8, UInt8, UInt8)
        struct Version { var major: UInt8 = 0, minor: UInt8 = 0, build: UInt8 = 0, reserved: UInt8 = 0; var release: UInt16 = 0 }
        struct PLimit { var version: UInt16 = 0, length: UInt16 = 0; var cpu: UInt32 = 0, gpu: UInt32 = 0, mem: UInt32 = 0 }
        struct KeyInfo { var dataSize: UInt32 = 0, dataType: UInt32 = 0; var attributes: UInt8 = 0; var pad: (UInt8, UInt8, UInt8) = (0, 0, 0) }

        var key: UInt32 = 0
        var vers = Version()
        var pLimit = PLimit()
        var keyInfo = KeyInfo()
        var result: UInt8 = 0
        var status: UInt8 = 0
        var data8: UInt8 = 0
        var data32: UInt32 = 0
        var bytes: Bytes = (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
    }

    private enum Selector: UInt8 { case readKey = 5, readIndex = 8, readKeyInfo = 9 }
    private static let kernelIndex: UInt32 = 2

    public struct Value {
        public let key: String
        public let type: String
        public let bytes: [UInt8]

        /// Decodes the common numeric SMC types to a Double.
        public var number: Double? {
            switch type {
            case "flt ":
                guard bytes.count >= 4 else { return nil }
                let bits = UInt32(bytes[0]) | UInt32(bytes[1]) << 8 | UInt32(bytes[2]) << 16 | UInt32(bytes[3]) << 24
                return Double(Float(bitPattern: bits))
            case "ui8 ": return bytes.first.map(Double.init)
            case "ui16": return bytes.count >= 2 ? Double(UInt16(bytes[0]) << 8 | UInt16(bytes[1])) : nil
            case "ui32":
                guard bytes.count >= 4 else { return nil }
                return Double(UInt32(bytes[0]) << 24 | UInt32(bytes[1]) << 16 | UInt32(bytes[2]) << 8 | UInt32(bytes[3]))
            case "sp78": return bytes.count >= 2 ? Double(Int16(bitPattern: UInt16(bytes[0]) << 8 | UInt16(bytes[1]))) / 256 : nil
            case "fpe2": return bytes.count >= 2 ? Double(UInt16(bytes[0]) << 8 | UInt16(bytes[1])) / 4 : nil
            case "ioft":
                guard bytes.count >= 8 else { return nil }
                var v: UInt64 = 0
                for i in 0..<8 { v |= UInt64(bytes[i]) << (8 * UInt64(i)) }
                return Double(v) / 65536
            default: return nil
            }
        }
    }

    private var connection: io_connect_t = 0
    private var infoCache: [UInt32: KeyData.KeyInfo] = [:]
    private let lock = NSLock()

    public init?() {
        let service = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("AppleSMC"))
        guard service != 0 else { return nil }
        defer { IOObjectRelease(service) }
        guard IOServiceOpen(service, mach_task_self_, 0, &connection) == kIOReturnSuccess else { return nil }
    }

    deinit { if connection != 0 { IOServiceClose(connection) } }

    private func call(_ input: inout KeyData) -> KeyData? {
        var output = KeyData()
        var outSize = MemoryLayout<KeyData>.stride
        let r = IOConnectCallStructMethod(connection, SMC.kernelIndex,
                                          &input, MemoryLayout<KeyData>.stride, &output, &outSize)
        guard r == kIOReturnSuccess, output.result == 0 else { return nil }
        return output
    }

    private func info(_ key: UInt32) -> KeyData.KeyInfo? {
        if let cached = infoCache[key] { return cached }
        var input = KeyData()
        input.key = key
        input.data8 = Selector.readKeyInfo.rawValue
        guard let out = call(&input) else { return nil }
        infoCache[key] = out.keyInfo
        return out.keyInfo
    }

    public func read(_ key: String) -> Value? {
        lock.lock(); defer { lock.unlock() }
        let code = SMC.fourCC(key)
        guard let info = info(code), info.dataSize > 0 else { return nil }
        var input = KeyData()
        input.key = code
        input.keyInfo.dataSize = info.dataSize
        input.data8 = Selector.readKey.rawValue
        guard let out = call(&input) else { return nil }
        let all = withUnsafeBytes(of: out.bytes) { Array($0) }
        return Value(key: key, type: SMC.string(info.dataType), bytes: Array(all.prefix(Int(min(info.dataSize, 32)))))
    }

    public func number(_ key: String) -> Double? { read(key)?.number }

    public var keyCount: Int { Int(number("#KEY") ?? 0) }

    /// Key name at a given index (for enumerating sensors once at start-up).
    public func key(at index: Int) -> String? {
        lock.lock(); defer { lock.unlock() }
        var input = KeyData()
        input.data8 = Selector.readIndex.rawValue
        input.data32 = UInt32(index)
        guard let out = call(&input) else { return nil }
        return SMC.string(out.key)
    }

    public func allKeys() -> [String] {
        (0..<keyCount).compactMap { key(at: $0) }
    }

    static func fourCC(_ s: String) -> UInt32 {
        s.utf8.prefix(4).reduce(0) { $0 << 8 | UInt32($1) }
    }

    static func string(_ v: UInt32) -> String {
        String(bytes: [UInt8(v >> 24 & 0xFF), UInt8(v >> 16 & 0xFF), UInt8(v >> 8 & 0xFF), UInt8(v & 0xFF)], encoding: .ascii) ?? "????"
    }
}
