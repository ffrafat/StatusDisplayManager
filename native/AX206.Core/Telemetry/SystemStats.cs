using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace AX206.Core.Telemetry;

public sealed record SystemStats(
    double CpuPercent,
    double CpuGhz,
    double RamPercent,
    ulong RamUsed,
    ulong RamTotal,
    double DiskPercent,
    ulong DiskUsed,
    ulong DiskTotal,
    double RxBytesPerSec,
    double TxBytesPerSec,
    long RxTotal,
    long TxTotal,
    string Ip,
    string Hostname);

/// <summary>
/// Samples the same figures backend.py got from psutil, straight from Windows APIs.
/// Rates (CPU %, network) are deltas, so call <see cref="Sample"/> about once a second.
/// </summary>
public sealed class SystemStatsSampler
{
    private long _lastIdle, _lastKernel, _lastUser;
    private long _lastRx, _lastTx;
    private DateTime _lastSample;
    private string _ip = "127.0.0.1";
    private DateTime _ipCheckedAt = DateTime.MinValue;

    public SystemStats Sample()
    {
        var now = DateTime.UtcNow;
        double dt = _lastSample == default ? 0 : (now - _lastSample).TotalSeconds;
        _lastSample = now;

        double cpu = SampleCpu();

        var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref mem);
        ulong ramUsed = mem.TotalPhys - mem.AvailPhys;

        ulong diskTotal = 0, diskUsed = 0;
        try
        {
            var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            diskTotal = (ulong)c.TotalSize;
            diskUsed = diskTotal - (ulong)c.TotalFreeSpace;
        }
        catch (IOException) { }

        var (rx, tx) = NetworkTotals();
        double rxRate = 0, txRate = 0;
        if (dt > 0 && _lastRx > 0)
        {
            rxRate = Math.Max(0, (rx - _lastRx) / dt);
            txRate = Math.Max(0, (tx - _lastTx) / dt);
        }
        _lastRx = rx;
        _lastTx = tx;

        if (now - _ipCheckedAt > TimeSpan.FromSeconds(30))
        {
            _ip = LocalIp();
            _ipCheckedAt = now;
        }

        return new SystemStats(
            cpu,
            CpuGhz(),
            mem.TotalPhys == 0 ? 0 : 100.0 * ramUsed / mem.TotalPhys,
            ramUsed,
            mem.TotalPhys,
            diskTotal == 0 ? 0 : 100.0 * diskUsed / diskTotal,
            diskUsed,
            diskTotal,
            rxRate,
            txRate,
            rx,
            tx,
            _ip,
            Environment.MachineName.ToUpperInvariant());
    }

    private double SampleCpu()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return 0;
        long dIdle = idle - _lastIdle, dKernel = kernel - _lastKernel, dUser = user - _lastUser;
        bool first = _lastKernel == 0;
        (_lastIdle, _lastKernel, _lastUser) = (idle, kernel, user);
        long total = dKernel + dUser; // kernel time includes idle time
        return first || total <= 0 ? 0 : Math.Clamp(100.0 * (total - dIdle) / total, 0, 100);
    }

    private static double CpuGhz()
    {
        var info = new ProcessorPowerInformation[Environment.ProcessorCount];
        uint size = (uint)(Marshal.SizeOf<ProcessorPowerInformation>() * info.Length);
        return CallNtPowerInformation(11 /* ProcessorInformation */, IntPtr.Zero, 0, info, size) == 0
            ? info[0].CurrentMhz / 1000.0
            : 0;
    }

    private static (long rx, long tx) NetworkTotals()
    {
        long rx = 0, tx = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try
            {
                var s = nic.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
            catch (NetworkInformationException) { }
        }
        return (rx, tx);
    }

    // Same trick as backend.py: "connect" a UDP socket (no packet is sent) and read the
    // local address the OS picked for the default route.
    private static string LocalIp()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 80);
            return (s.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
        }
        catch (SocketException)
        {
            return "127.0.0.1";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    // FILETIMEs read as 64-bit integers (low dword first, which matches little-endian).
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputSize,
        [Out] ProcessorPowerInformation[] output, uint outputSize);
}
