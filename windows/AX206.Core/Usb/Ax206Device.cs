using System.Buffers.Binary;
using System.Runtime.InteropServices;

// Only load libusb0.dll from System32, never from the app folder.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace AX206.Core.Usb;

/// <summary>
/// AX206 USB LCD (VID 1908 / PID 0102) over the vendor SCSI-over-bulk (BOT) protocol.
/// Talks to libusb0.dll, the libusb-win32 compatibility layer installed with the libusbK
/// driver - the same path pyusb used in backend.py.
/// </summary>
public sealed class Ax206Device : IDisposable
{
    public const ushort VendorId = 0x1908;
    public const ushort ProductId = 0x0102;
    public const int Width = 480;
    public const int Height = 320;
    public const int FrameBytes = Width * Height * 2;

    private const int EpOut = 0x01;
    private const int EpIn = 0x81;
    private const int Interface = 0;

    private IntPtr _handle;

    public bool IsOpen => _handle != IntPtr.Zero;
    public string? DevicePath { get; private set; }

    public static Ax206Device Open()
    {
        LibUsb0.usb_init();
        LibUsb0.usb_find_busses();
        LibUsb0.usb_find_devices();

        IntPtr dev = FindDevice(out string path);
        if (dev == IntPtr.Zero)
            throw new Ax206Exception("AX206 display not found (VID 1908 / PID 0102).");

        IntPtr handle = LibUsb0.usb_open(dev);
        if (handle == IntPtr.Zero)
            throw new Ax206Exception($"usb_open failed: {LibUsb0.LastError()}");

        var device = new Ax206Device { _handle = handle, DevicePath = path };
        try
        {
            device.Check(LibUsb0.usb_set_configuration(handle, 1), "usb_set_configuration");
            device.Check(LibUsb0.usb_claim_interface(handle, Interface), "usb_claim_interface");
            LibUsb0.usb_clear_halt(handle, EpOut);
            LibUsb0.usb_clear_halt(handle, EpIn);
        }
        catch
        {
            device.Dispose();
            throw;
        }
        return device;
    }

    // Walks libusb0's bus/device linked lists. Offsets follow lusb0_usb.h on x64 with
    // LIBUSB_PATH_MAX = 512: usb_bus.devices @528, usb_device.filename @16,
    // usb_device.descriptor @536 (idVendor +8, idProduct +10).
    private static IntPtr FindDevice(out string path)
    {
        const int pathMax = 512;
        for (IntPtr bus = LibUsb0.usb_get_busses(); bus != IntPtr.Zero; bus = Marshal.ReadIntPtr(bus, 0))
        {
            for (IntPtr dev = Marshal.ReadIntPtr(bus, 16 + pathMax); dev != IntPtr.Zero; dev = Marshal.ReadIntPtr(dev, 0))
            {
                int descriptor = 16 + pathMax + IntPtr.Size;
                ushort vid = (ushort)Marshal.ReadInt16(dev, descriptor + 8);
                ushort pid = (ushort)Marshal.ReadInt16(dev, descriptor + 10);
                if (vid == VendorId && pid == ProductId)
                {
                    path = Marshal.PtrToStringAnsi(dev + 16) ?? "";
                    return dev;
                }
            }
        }
        path = "";
        return IntPtr.Zero;
    }

    /// <summary>Blits a full 480x320 frame of big-endian RGB565 pixels.</summary>
    public void Blit(ReadOnlySpan<byte> rgb565Be) => Blit(0, 0, Width, Height, rgb565Be);

    public void Blit(int x0, int y0, int x1, int y1, ReadOnlySpan<byte> rgb565Be)
    {
        int bytes = (x1 - x0) * (y1 - y0) * 2;
        if (rgb565Be.Length != bytes)
            throw new ArgumentException($"Expected {bytes} bytes, got {rgb565Be.Length}.");

        Span<byte> cdb = stackalloc byte[16];
        cdb[0] = 0xCD;
        cdb[5] = 0x06;
        cdb[6] = 0x12; // BLIT
        BinaryPrimitives.WriteUInt16LittleEndian(cdb[7..], (ushort)x0);
        BinaryPrimitives.WriteUInt16LittleEndian(cdb[9..], (ushort)y0);
        BinaryPrimitives.WriteUInt16LittleEndian(cdb[11..], (ushort)(x1 - 1));
        BinaryPrimitives.WriteUInt16LittleEndian(cdb[13..], (ushort)(y1 - 1));

        WriteBulk(BuildCbw(bytes, directionIn: false, cdb), "CBW");
        WriteBulk(rgb565Be.ToArray(), "frame data");
        ReadCsw();
    }

    private static byte[] BuildCbw(int dataLength, bool directionIn, ReadOnlySpan<byte> cdb)
    {
        var cbw = new byte[31];
        "USBC"u8.CopyTo(cbw);
        BinaryPrimitives.WriteUInt32LittleEndian(cbw.AsSpan(4), 0xEFBEADDE); // tag
        BinaryPrimitives.WriteInt32LittleEndian(cbw.AsSpan(8), dataLength);
        cbw[12] = directionIn ? (byte)0x80 : (byte)0x00;
        cbw[13] = 0;  // LUN
        cbw[14] = 16; // CDB length
        cdb.CopyTo(cbw.AsSpan(15));
        return cbw;
    }

    private void WriteBulk(byte[] data, string what)
    {
        int n = LibUsb0.usb_bulk_write(_handle, EpOut, data, data.Length, 4000);
        if (n != data.Length)
            throw new Ax206Exception($"Bulk write of {what} failed ({n}/{data.Length}): {LibUsb0.LastError()}");
    }

    private void ReadCsw()
    {
        var csw = new byte[13];
        int n = LibUsb0.usb_bulk_read(_handle, EpIn, csw, csw.Length, 2000);
        if (n < 13)
            throw new Ax206Exception($"CSW read failed ({n} bytes): {LibUsb0.LastError()}");
        if (!csw.AsSpan(0, 4).SequenceEqual("USBS"u8) || csw[12] != 0)
            throw new Ax206Exception($"CSW reported failure: status {csw[12]}");
    }

    /// <summary>Bulk-Only Mass Storage Reset, then clear both endpoints (as backend.py's recover()).</summary>
    public void Recover()
    {
        LibUsb0.usb_control_msg(_handle, 0x21, 0xFF, 0, 0, null, 0, 4000);
        LibUsb0.usb_clear_halt(_handle, EpOut);
        LibUsb0.usb_clear_halt(_handle, EpIn);
        var drain = new byte[64];
        for (int i = 0; i < 3 && LibUsb0.usb_bulk_read(_handle, EpIn, drain, drain.Length, 60) > 0; i++) { }
    }

    private void Check(int result, string call)
    {
        if (result < 0)
            throw new Ax206Exception($"{call} failed: {LibUsb0.LastError()}");
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        LibUsb0.usb_release_interface(_handle, Interface);
        LibUsb0.usb_close(_handle);
        _handle = IntPtr.Zero;
    }
}

public sealed class Ax206Exception(string message) : Exception(message);

internal static class LibUsb0
{
    private const string Dll = "libusb0.dll";

    [DllImport(Dll)] public static extern void usb_init();
    [DllImport(Dll)] public static extern int usb_find_busses();
    [DllImport(Dll)] public static extern int usb_find_devices();
    [DllImport(Dll)] public static extern IntPtr usb_get_busses();
    [DllImport(Dll)] public static extern IntPtr usb_open(IntPtr dev);
    [DllImport(Dll)] public static extern int usb_close(IntPtr handle);
    [DllImport(Dll)] public static extern int usb_set_configuration(IntPtr handle, int configuration);
    [DllImport(Dll)] public static extern int usb_claim_interface(IntPtr handle, int iface);
    [DllImport(Dll)] public static extern int usb_release_interface(IntPtr handle, int iface);
    [DllImport(Dll)] public static extern int usb_clear_halt(IntPtr handle, uint ep);
    [DllImport(Dll)] public static extern int usb_bulk_write(IntPtr handle, int ep, byte[] bytes, int size, int timeout);
    [DllImport(Dll)] public static extern int usb_bulk_read(IntPtr handle, int ep, byte[] bytes, int size, int timeout);
    [DllImport(Dll)] public static extern int usb_control_msg(IntPtr handle, int requestType, int request, int value, int index, byte[]? bytes, int size, int timeout);
    [DllImport(Dll)] private static extern IntPtr usb_strerror();

    public static string LastError() => Marshal.PtrToStringAnsi(usb_strerror()) ?? "unknown error";
}
