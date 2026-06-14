const si = require('systeminformation');
const os = require('os');

let prevNetStats = null;
let prevNetTime = Date.now();

// Finds the main active network interface or returns default
async function getActiveInterface() {
  try {
    const ifs = await si.networkInterfaces();
    // Prefer non-virtual, active interfaces
    const active = ifs.find(i => i.operstate === 'up' && !i.virtual && i.ip4);
    return active ? active.iface : null;
  } catch (e) {
    return null;
  }
}

// Fetch local IPv4 address
function getIpAddress() {
  const nets = os.networkInterfaces();
  for (const name of Object.keys(nets)) {
    for (const net of nets[name]) {
      // Skip over non-IPv4 and internal (i.e. 127.0.0.1)
      if (net.family === 'IPv4' && !net.internal) {
        return net.address;
      }
    }
  }
  return '127.0.0.1';
}

async function getStats() {
  const now = Date.now();
  const dt = (now - prevNetTime) / 1000.0;
  prevNetTime = now;

  try {
    // Fire off async checks in parallel for speed
    const [load, mem, cpuSpeed, temp, disks, net] = await Promise.all([
      si.currentLoad(),
      si.mem(),
      si.cpuCurrentSpeed(),
      si.cpuTemperature(),
      si.fsSize(),
      si.networkStats()
    ]);

    // 1. CPU
    const cpuPct = load.currentLoad;
    const cpuFreq = cpuSpeed.avg; // in GHz (systeminformation returns GHz directly or MHz? Let's check: it returns GHz)
    const cpuTemp = temp.main || null; // main temperature

    // 2. RAM
    const ramPct = (mem.active / mem.total) * 100;
    const ramUsed = mem.active;
    const ramTotal = mem.total;

    // 3. Disk
    // Find the primary disk (usually root / on Linux/macOS or C: on Windows)
    const primaryDisk = disks.find(d => d.mount === '/' || d.mount === 'C:') || disks[0] || { use: 0, used: 0, size: 0 };
    const diskPct = primaryDisk.use;
    const diskUsed = primaryDisk.used;
    const diskTotal = primaryDisk.size;

    // 4. Network
    // Find the main interface stats
    const mainNet = net.find(n => n.operstate === 'up') || net[0] || { rx_bytes: 0, tx_bytes: 0 };
    let rxRate = 0;
    let txRate = 0;

    if (prevNetStats && dt > 0) {
      const prevMainNet = prevNetStats.find(n => n.iface === mainNet.iface) || prevNetStats[0];
      if (prevMainNet) {
        rxRate = Math.max(0, (mainNet.rx_bytes - prevMainNet.rx_bytes) / dt);
        txRate = Math.max(0, (mainNet.tx_bytes - prevMainNet.tx_bytes) / dt);
      }
    }
    prevNetStats = net;

    return {
      cpu: cpuPct,
      cpu_freq: cpuFreq, // GHz
      ram: ramPct,
      ram_used: ramUsed,
      ram_total: ramTotal,
      disk_root_pct: diskPct,
      disk_used: diskUsed,
      disk_total: diskTotal,
      rx: rxRate, // B/s
      tx: txRate, // B/s
      net_recv_total: mainNet.rx_bytes,
      net_sent_total: mainNet.tx_bytes,
      temp: cpuTemp,
      ip: getIpAddress(),
      hostname: os.hostname().toUpperCase()
    };
  } catch (e) {
    console.error("Error collecting system stats:", e);
    return {
      cpu: 0,
      cpu_freq: 0,
      ram: 0,
      ram_used: 0,
      ram_total: 1,
      disk_root_pct: 0,
      disk_used: 0,
      disk_total: 1,
      rx: 0,
      tx: 0,
      net_recv_total: 0,
      net_sent_total: 0,
      temp: null,
      ip: getIpAddress(),
      hostname: os.hostname().toUpperCase(),
      error: e.message
    };
  }
}

module.exports = {
  getStats
};
