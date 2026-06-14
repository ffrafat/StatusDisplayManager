const fs = require('fs');
const path = require('path');
const os = require('os');
const http = require('http');
const si = require('systeminformation');

// Cache for Antigravity server details
let cachedAgServer = null; // { port, csrf }

// --- Helpers ---

function parseResetTs(val) {
  if (!val) return 0;
  if (/^\d+(\.\d+)?$/.test(val)) {
    const ts = parseFloat(val);
    return ts > 1000000000 ? ts : 0;
  }
  try {
    return new Date(val).getTime() / 1000; // Return seconds
  } catch (e) {
    return 0;
  }
}

// Helper to make a standard HTTP POST request in Node.js
function postJson(url, port, path, headers, body) {
  return new Promise((resolve, reject) => {
    const data = JSON.stringify(body);
    const req = http.request({
      hostname: url,
      port: port,
      path: path,
      method: 'POST',
      headers: {
        ...headers,
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(data)
      },
      timeout: 3000
    }, (res) => {
      let rawData = '';
      res.setEncoding('utf8');
      res.on('data', (chunk) => { rawData += chunk; });
      res.on('end', () => {
        resolve({ status: res.statusCode, headers: res.headers, body: rawData });
      });
    });

    req.on('error', (e) => { reject(e); });
    req.on('timeout', () => {
      req.destroy();
      reject(new Error("Timeout"));
    });
    req.write(data);
    req.end();
  });
}

// --- Claude API Usage ---

function readClaudeToken() {
  const home = os.homedir();
  const localAppData = process.env.LOCALAPPDATA || path.join(home, 'AppData', 'Local');
  const candidates = [
    path.join(home, '.claude', '.credentials.json'),
    path.join(localAppData, 'Claude', '.credentials.json')
  ];

  for (const p of candidates) {
    try {
      if (fs.existsSync(p)) {
        const raw = fs.readFileSync(p, 'utf8');
        const data = JSON.parse(raw);
        let token = data.accessToken;
        if (!token && data.claudeAiOauth) {
          token = data.claudeAiOauth.accessToken;
        }
        if (token && token.trim()) {
          return token.trim();
        }
      }
    } catch (e) {
      // ignore
    }
  }
  return null;
}

async function fetchClaudeUsage() {
  const token = readClaudeToken();
  if (!token) {
    return { ok: false, error: "No Claude credentials found" };
  }

  const payload = JSON.stringify({
    model: "claude-haiku-4-5-20251001",
    max_tokens: 1,
    messages: [{ role: "user", content: "hi" }]
  });

  try {
    const response = await fetch("https://api.anthropic.com/v1/messages", {
      method: "POST",
      headers: {
        "Authorization": `Bearer ${token}`,
        "anthropic-version": "2023-06-01",
        "anthropic-beta": "oauth-2025-04-20",
        "Content-Type": "application/json"
      },
      body: payload
    });

    const headers = response.headers;
    const sessionUtil = parseFloat(headers.get("anthropic-ratelimit-unified-5h-utilization") || "0");
    const weeklyUtil = parseFloat(headers.get("anthropic-ratelimit-unified-7d-utilization") || "0");
    const sessionRst = headers.get("anthropic-ratelimit-unified-5h-reset") || "";
    const weeklyRst = headers.get("anthropic-ratelimit-unified-7d-reset") || "";

    return {
      ok: true,
      error: null,
      session_pct: sessionUtil * 100,
      weekly_pct: weeklyUtil * 100,
      session_reset_ts: parseResetTs(sessionRst),
      weekly_reset_ts: parseResetTs(weeklyRst),
      last_update: Date.now()
    };
  } catch (e) {
    return {
      ok: false,
      error: e.message,
      session_pct: 0,
      weekly_pct: 0,
      session_reset_ts: 0,
      weekly_reset_ts: 0,
      last_update: Date.now()
    };
  }
}

// --- Antigravity IDE Quota ---

async function findAgServer() {
  try {
    const procs = await si.processes();
    const agProcs = [];

    // 1. Find language server processes with CSRF token in args
    for (const p of procs.list) {
      if (p.name && p.name.toLowerCase().includes('language_server')) {
        const cmd = (p.command || '') + ' ' + (p.params || '');
        const match = cmd.match(/--csrf_token\s+(\S+)/);
        if (match) {
          agProcs.push({ pid: p.pid, csrf: match[1] });
        }
      }
    }

    if (agProcs.length === 0) return null;

    // 2. Find open listening TCP ports for these PIDs
    const conns = await si.networkConnections();
    const path = '/exa.language_server_pb.LanguageServerService/GetUserStatus';
    const body = { metadata: { ideName: 'antigravity', extensionName: 'antigravity', locale: 'en' } };

    for (const ap of agProcs) {
      const ports = conns
        .filter(c => c.pid === ap.pid && c.state.toUpperCase() === 'LISTEN')
        .map(c => parseInt(c.localPort))
        .filter(p => !isNaN(p));

      // Test each port
      for (const port of ports) {
        try {
          const res = await postJson('127.0.0.1', port, path, {
            'X-Codeium-Csrf-Token': ap.csrf,
            'Connect-Protocol-Version': '1'
          }, body);

          if (res.status === 200) {
            return { port, csrf: ap.csrf };
          }
        } catch (e) {
          // ignore failed ports
        }
      }
    }
  } catch (e) {
    console.error("Error finding Antigravity server:", e);
  }
  return null;
}

const AG_GROUPS = [
  { label: "Gemini Flash", color: [64, 196, 255], keywords: ["gemini", "flash"] },
  { label: "Gemini Pro",   color: [105, 240, 174], keywords: ["gemini", "pro"] },
  { label: "Claude",       color: [255, 171, 64], keywords: ["claude"] }
];

async function fetchAgUsage() {
  const server = cachedAgServer || await findAgServer();
  if (!server) {
    cachedAgServer = null;
    return { available: false, groups: [], error: "Antigravity not running" };
  }

  const path = '/exa.language_server_pb.LanguageServerService/GetUserStatus';
  const body = { metadata: { ideName: 'antigravity', extensionName: 'antigravity', locale: 'en' } };

  try {
    const res = await postJson('127.0.0.1', server.port, path, {
      'X-Codeium-Csrf-Token': server.csrf,
      'Connect-Protocol-Version': '1'
    }, body);

    if (res.status !== 200) {
      cachedAgServer = null; // Invalidate cache
      return { available: false, groups: [], error: `HTTP ${res.status}` };
    }

    cachedAgServer = server; // Cache working connection
    const data = JSON.parse(res.body);
    const models = data.userStatus?.cascadeModelConfigData?.clientModelConfigs || [];

    const groups = [];
    for (const group of AG_GROUPS) {
      // Find all config entries matching this group's keywords
      const matches = models.filter(m => {
        const label = (m.label || '').toLowerCase();
        return group.keywords.every(kw => label.includes(kw));
      });

      if (matches.length === 0) continue;

      // Find the min remaining fraction
      const fractions = matches
        .map(m => m.quotaInfo?.remainingFraction)
        .filter(f => f !== undefined && f !== null);

      if (fractions.length === 0) continue;

      const remaining = Math.min(...fractions) * 100;

      // Worst reset time (lowest remaining)
      const worst = matches.reduce((prev, curr) => {
        const pf = prev.quotaInfo?.remainingFraction ?? 1;
        const cf = curr.quotaInfo?.remainingFraction ?? 1;
        return cf < pf ? curr : prev;
      }, matches[0]);

      const resetStr = worst.quotaInfo?.resetTime || '';
      const resetTs = parseResetTs(resetStr);

      groups.push({
        label: group.label,
        color: group.color,
        remaining: remaining,
        reset_ts: resetTs
      });
    }

    return {
      available: true,
      groups: groups,
      error: null,
      last_update: Date.now()
    };

  } catch (e) {
    cachedAgServer = null; // Invalidate cache on connect error
    return { available: false, groups: [], error: "Connection lost" };
  }
}

module.exports = {
  fetchClaudeUsage,
  fetchAgUsage
};
