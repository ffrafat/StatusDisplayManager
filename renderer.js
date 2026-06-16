const { ipcRenderer } = require('electron');

// Get UI Elements
const statusDot = document.getElementById('status-dot');
const statusText = document.getElementById('status-text');
const btnToggleConnect = document.getElementById('btn-toggle-connect');
const rotationBadge = document.getElementById('rotation-badge');
const consoleBox = document.getElementById('console-box');

// Get LCD Navigation Elements
const btnPrevScreen = document.getElementById('btn-prev-screen');
const btnNextScreen = document.getElementById('btn-next-screen');
const btnToggleRotation = document.getElementById('btn-toggle-rotation');
const rotationPlayPauseIcon = document.getElementById('rotation-play-pause-icon');

// Diagnostic Readouts
const diagCpu = document.getElementById('diag-cpu');
const diagRam = document.getElementById('diag-ram');
const diagApp = document.getElementById('diag-app');
const diagMusic = document.getElementById('diag-music');

// Screen Settings Toggles
const chkClock = document.getElementById('chk-clock');
const chkStats = document.getElementById('chk-stats');
const chkMusic = document.getElementById('chk-music');
const chkClaude = document.getElementById('chk-claude');
const chkAg = document.getElementById('chk-ag');
const chkBangla = document.getElementById('chk-bangla');

// Screen Duration Inputs
const durClock = document.getElementById('dur-clock');
const durStats = document.getElementById('dur-stats');
const durMusic = document.getElementById('dur-music');
const durClaude = document.getElementById('dur-claude');
const durAg = document.getElementById('dur-ag');
const durBangla = document.getElementById('dur-bangla');

// Hidden Assets
const imgClaudeLogo = document.getElementById('img-claude-logo');
const imgAgLogo = document.getElementById('img-antigravity-logo');
const imgBanglaLogo = document.getElementById('img-bangla-logo');
const imgProdPurno = document.getElementById('img-prod-purno');
const imgProdSothik = document.getElementById('img-prod-sothik');
const imgProdBanglaWord = document.getElementById('img-prod-banglaword');

// Canvas Setup
const lcdCanvas = document.getElementById('lcd-canvas');
const lcdCtx = lcdCanvas.getContext('2d');

// 2x Offscreen Canvas for Supersampling
const offscreenCanvas = document.createElement('canvas');
offscreenCanvas.width = 960;
offscreenCanvas.height = 640;
const offCtx = offscreenCanvas.getContext('2d');

// Global Application State
let displayConnected = false;
let currentStats = null;
let currentActiveApp = null;
let currentMedia = null;
let currentClaudeUsage = null;
let currentAgUsage = null;
let currentBanglaGovData = null;

// Artwork cache
let cachedArtworkImage = null;
let cachedArtworkBase64 = null;
let isArtworkLoading = false;

// Screen rotation variables
let screenList = ['clock', 'stats', 'music'];
let currentScreen = 'clock';
let screenStartTime = Date.now();
let autoRotationPaused = false;

// Load / Save Preferences
function loadPreferences() {
  chkClock.checked = localStorage.getItem('chk-clock') !== 'false';
  chkStats.checked = localStorage.getItem('chk-stats') !== 'false';
  chkMusic.checked = localStorage.getItem('chk-music') !== 'false';
  chkClaude.checked = localStorage.getItem('chk-claude') !== 'false';
  chkAg.checked = localStorage.getItem('chk-ag') !== 'false';
  chkBangla.checked = localStorage.getItem('chk-bangla') !== 'false';

  durClock.value = localStorage.getItem('dur-clock') || '10';
  durStats.value = localStorage.getItem('dur-stats') || '10';
  durMusic.value = localStorage.getItem('dur-music') || '10';
  durClaude.value = localStorage.getItem('dur-claude') || '10';
  durAg.value = localStorage.getItem('dur-ag') || '10';
  durBangla.value = localStorage.getItem('dur-bangla') || '10';
}

function savePreferences() {
  localStorage.setItem('chk-clock', chkClock.checked);
  localStorage.setItem('chk-stats', chkStats.checked);
  localStorage.setItem('chk-music', chkMusic.checked);
  localStorage.setItem('chk-claude', chkClaude.checked);
  localStorage.setItem('chk-ag', chkAg.checked);
  localStorage.setItem('chk-bangla', chkBangla.checked);

  localStorage.setItem('dur-clock', durClock.value);
  localStorage.setItem('dur-stats', durStats.value);
  localStorage.setItem('dur-music', durMusic.value);
  localStorage.setItem('dur-claude', durClaude.value);
  localStorage.setItem('dur-ag', durAg.value);
  localStorage.setItem('dur-bangla', durBangla.value);
}

// Attach listeners to save on change
[
  chkClock, chkStats, chkMusic, chkClaude, chkAg, chkBangla,
  durClock, durStats, durMusic, durClaude, durAg, durBangla
].forEach(el => {
  el.addEventListener('change', () => {
    savePreferences();
    updateScreenList();
  });
});

// Update the list of screens to rotate
function updateScreenList() {
  const isMediaActive = currentMedia && (currentMedia.playing || currentMedia.status === 'Playing' || currentMedia.status === 'Paused');
  const list = [];
  
  if (chkClock.checked) list.push('clock');
  if (chkStats.checked) list.push('stats');
  
  if (chkMusic.checked && isMediaActive) {
    list.push('music');
  }

  if (chkClaude.checked && currentClaudeUsage && currentClaudeUsage.ok) {
    list.push('claude');
  }

  const isAgActive = currentAgUsage && currentAgUsage.available;
  if (chkAg.checked && isAgActive) {
    list.push('ag');
  }

  if (chkBangla.checked && currentBanglaGovData && currentBanglaGovData.ok) {
    list.push('bangla');
  }

  const oldScreenList = [...screenList];
  screenList = list.length > 0 ? list : ['clock'];
  
  // Jump to music screen immediately if music just started playing
  if (screenList.includes('music') && !oldScreenList.includes('music')) {
    currentScreen = 'music';
    screenStartTime = Date.now();
  }
  // Otherwise, if the current screen is no longer valid/active, switch away immediately
  else if (!screenList.includes(currentScreen)) {
    currentScreen = screenList[0];
    screenStartTime = Date.now();
  }
}

// Display Connection Toggle
btnToggleConnect.addEventListener('click', () => {
  if (displayConnected) {
    appendLog("Disconnecting display...", "info");
    ipcRenderer.send('disconnect-request');
  } else {
    appendLog("Connecting display...", "info");
    ipcRenderer.send('connect-request');
  }
});

// LCD Navigation Controls Event Listeners
btnToggleRotation.addEventListener('click', () => {
  autoRotationPaused = !autoRotationPaused;
  if (autoRotationPaused) {
    // Show Play icon (polygon) to resume rotation
    rotationPlayPauseIcon.innerHTML = `<polygon points="5 3 19 12 5 21 5 3"/>`;
    appendLog("Auto-rotation paused. Display locked to current screen.", "info");
  } else {
    // Show Pause icon (rects) to pause rotation
    rotationPlayPauseIcon.innerHTML = `<rect x="14" y="4" width="4" height="16" rx="1"/><rect x="6" y="4" width="4" height="16" rx="1"/>`;
    screenStartTime = Date.now();
    appendLog("Auto-rotation resumed.", "info");
  }
});

btnPrevScreen.addEventListener('click', () => {
  if (screenList.length <= 1) return;
  const idx = screenList.indexOf(currentScreen);
  const nextIdx = idx === -1 ? 0 : (idx - 1 + screenList.length) % screenList.length;
  currentScreen = screenList[nextIdx];
  screenStartTime = Date.now();
  drawActiveScreen();
  appendLog(`Manually switched to: ${currentScreen.toUpperCase()}`, "info");
});

btnNextScreen.addEventListener('click', () => {
  if (screenList.length <= 1) return;
  const idx = screenList.indexOf(currentScreen);
  const nextIdx = idx === -1 ? 0 : (idx + 1) % screenList.length;
  currentScreen = screenList[nextIdx];
  screenStartTime = Date.now();
  drawActiveScreen();
  appendLog(`Manually switched to: ${currentScreen.toUpperCase()}`, "info");
});

// Logs Console helper
function appendLog(msg, type = 'info') {
  const line = document.createElement('div');
  line.classList.add('log-line', type);
  const time = new Date().toLocaleTimeString();
  line.textContent = `[${time}] ${msg}`;
  consoleBox.appendChild(line);
  consoleBox.scrollTop = consoleBox.scrollHeight;

  // Limit log lines to 100
  while (consoleBox.children.length > 100) {
    consoleBox.removeChild(consoleBox.firstChild);
  }
}

// Listen for connection changes from main process
ipcRenderer.on('display-status', (event, status) => {
  displayConnected = status.connected;
  if (displayConnected) {
    statusDot.className = 'status-dot connected';
    statusText.textContent = 'Connected';
    btnToggleConnect.textContent = 'Disconnect';
    btnToggleConnect.classList.add('connected-btn');
  } else {
    statusDot.className = 'status-dot disconnected';
    statusText.textContent = 'Disconnected';
    btnToggleConnect.textContent = 'Connect Display';
    btnToggleConnect.classList.remove('connected-btn');
  }
});

// Listen for log messages from main process
ipcRenderer.on('log-message', (event, log) => {
  appendLog(log.msg, log.type);
});

// Receive background telemetry data
ipcRenderer.on('tick-data', (event, data) => {
  currentStats = data.stats;
  currentActiveApp = data.activeApp;
  currentMedia = data.media;
  currentClaudeUsage = data.claudeUsage;
  currentAgUsage = data.agUsage;
  currentBanglaGovData = data.banglaGovData;
  displayConnected = data.displayConnected;

  // Handle album art loading and caching
  if (currentMedia && currentMedia.thumbnail) {
    if (cachedArtworkBase64 !== currentMedia.thumbnail) {
      cachedArtworkBase64 = currentMedia.thumbnail;
      isArtworkLoading = true;
      const img = new Image();
      img.onload = () => {
        cachedArtworkImage = img;
        isArtworkLoading = false;
      };
      img.onerror = () => {
        cachedArtworkImage = null;
        isArtworkLoading = false;
      };
      let mime = 'image/jpeg';
      if (currentMedia.thumbnail.startsWith('iVBORw0G')) {
        mime = 'image/png';
      }
      img.src = `data:${mime};base64,` + currentMedia.thumbnail;
    }
  } else {
    cachedArtworkBase64 = null;
    cachedArtworkImage = null;
  }

  // Update connection status
  if (displayConnected) {
    statusDot.className = 'status-dot connected';
    statusText.textContent = 'Connected';
    btnToggleConnect.textContent = 'Disconnect';
    btnToggleConnect.classList.add('connected-btn');
  } else {
    statusDot.className = 'status-dot disconnected';
    statusText.textContent = 'Disconnected';
    btnToggleConnect.textContent = 'Connect Display';
    btnToggleConnect.classList.remove('connected-btn');
  }

  // Update telemetry panel readouts
  if (currentStats) {
    diagCpu.textContent = `${Math.round(currentStats.cpu)}%`;
    diagRam.textContent = `${Math.round(currentStats.ram)}%`;
  }
  
  if (currentActiveApp) {
    diagApp.textContent = `${currentActiveApp.app} (${currentActiveApp.title})`;
  } else {
    diagApp.textContent = '--';
  }

  if (currentMedia && currentMedia.playing) {
    diagMusic.textContent = `${currentMedia.title} - ${currentMedia.artist}`;
  } else {
    diagMusic.textContent = 'Nothing playing';
  }

  // Dynamic Image Reloader for Bangla Gov assets
  const checkAndReloadImage = (imgId, localFile) => {
    const img = document.getElementById(imgId);
    if (img && (!img.complete || img.naturalWidth === 0)) {
      img.src = '';
      img.src = localFile;
    }
  };
  checkAndReloadImage('img-bangla-logo', 'bangla.png');
  checkAndReloadImage('img-prod-purno', 'product_purno.png');
  checkAndReloadImage('img-prod-sothik', 'product_sothik.png');
  checkAndReloadImage('img-prod-banglaword', 'product_banglaword.png');

  updateScreenList();
});

// --- RENDERERS FOR ALL SCREENS (Drawn at 960x640) ---

const PALETTE = {
  bg: '#0a0c10',
  panel: '#141820',
  ink: '#f0f4fc',
  muted: '#5e6d91',
  track: '#202738',
  cpu: '#60a5fa',
  ram: '#c084fc',
  disk: '#34d399',
  net: '#facc15',
  music: '#1DB954',
  activeApp: '#00e5ff'
};

function getDuration(screen) {
  if (screen === 'clock') return parseInt(durClock.value) || 10;
  if (screen === 'stats') return parseInt(durStats.value) || 10;
  if (screen === 'music') return parseInt(durMusic.value) || 10;
  if (screen === 'claude') return parseInt(durClaude.value) || 10;
  if (screen === 'ag') return parseInt(durAg.value) || 10;
  if (screen === 'bangla') return parseInt(durBangla.value) || 10;
  return 10;
}

// Helpers
function drawCard(ctx, x, y, w, h, r, color) {
  ctx.fillStyle = color;
  ctx.beginPath();
  ctx.roundRect(x, y, w, h, r);
  ctx.fill();
}

function drawProgressBar(ctx, x, y, w, h, r, percent, trackColor, barColor) {
  // Draw track background
  drawCard(ctx, x, y, w, h, r, trackColor);
  // Draw filled bar
  if (percent > 0) {
    const fillW = w * (Math.min(100, percent) / 100);
    drawCard(ctx, x, y, fillW, h, r, barColor);
  }
}

function drawArcGauge(ctx, cx, cy, radius, thickness, startAngle, endAngle, percent, trackColor, barColor) {
  ctx.lineCap = 'round';
  // Track
  ctx.beginPath();
  ctx.arc(cx, cy, radius, startAngle, endAngle);
  ctx.lineWidth = thickness;
  ctx.strokeStyle = trackColor;
  ctx.stroke();

  // Active Bar
  if (percent > 0) {
    const sweep = (endAngle - startAngle) * (Math.min(100, percent) / 100);
    ctx.beginPath();
    ctx.arc(cx, cy, radius, startAngle, startAngle + sweep);
    ctx.lineWidth = thickness;
    ctx.strokeStyle = barColor;
    ctx.stroke();
  }
}

// Minimal Line Vector Icons (Ported from Lucide icon paths)
function drawIconCpu(ctx, cx, cy, sz, color) {
  ctx.save();
  ctx.translate(cx, cy);
  ctx.scale(sz / 24, sz / 24);
  
  ctx.strokeStyle = color;
  ctx.lineWidth = 2;
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';

  // Outlined main body
  ctx.beginPath();
  ctx.roundRect(4 - 12, 4 - 12, 16, 16, 2);
  ctx.stroke();

  // Outlined inner die
  ctx.beginPath();
  ctx.roundRect(9 - 12, 9 - 12, 6, 6, 1);
  ctx.stroke();

  // Pins
  ctx.beginPath();
  // top
  ctx.moveTo(9 - 12, 1 - 12); ctx.lineTo(9 - 12, 4 - 12);
  ctx.moveTo(15 - 12, 1 - 12); ctx.lineTo(15 - 12, 4 - 12);
  // bottom
  ctx.moveTo(9 - 12, 20 - 12); ctx.lineTo(9 - 12, 23 - 12);
  ctx.moveTo(15 - 12, 20 - 12); ctx.lineTo(15 - 12, 23 - 12);
  // right
  ctx.moveTo(20 - 12, 9 - 12); ctx.lineTo(23 - 12, 9 - 12);
  ctx.moveTo(20 - 12, 15 - 12); ctx.lineTo(23 - 12, 15 - 12);
  // left
  ctx.moveTo(1 - 12, 9 - 12); ctx.lineTo(4 - 12, 9 - 12);
  ctx.moveTo(1 - 12, 15 - 12); ctx.lineTo(4 - 12, 15 - 12);
  ctx.stroke();

  ctx.restore();
}

function drawIconRam(ctx, cx, cy, sz, color) {
  ctx.save();
  ctx.translate(cx, cy);
  ctx.scale(sz / 24, sz / 24);
  
  ctx.strokeStyle = color;
  ctx.lineWidth = 2;
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';

  // Stick body
  ctx.beginPath();
  ctx.roundRect(2 - 12, 5 - 12, 20, 16, 2);
  ctx.stroke();

  // Bottom contact fingers
  ctx.beginPath();
  ctx.moveTo(6 - 12, 19 - 12); ctx.lineTo(6 - 12, 16 - 12);
  ctx.moveTo(10 - 12, 19 - 12); ctx.lineTo(10 - 12, 16 - 12);
  ctx.moveTo(14 - 12, 19 - 12); ctx.lineTo(14 - 12, 16 - 12);
  ctx.moveTo(18 - 12, 19 - 12); ctx.lineTo(18 - 12, 16 - 12);

  // Chips on board
  ctx.moveTo(8 - 12, 11 - 12); ctx.lineTo(8 - 12, 9 - 12);
  ctx.moveTo(12 - 12, 11 - 12); ctx.lineTo(12 - 12, 9 - 12);
  ctx.moveTo(16 - 12, 11 - 12); ctx.lineTo(16 - 12, 9 - 12);
  ctx.stroke();

  ctx.restore();
}

function drawIconDisk(ctx, cx, cy, sz, color) {
  ctx.save();
  ctx.translate(cx, cy);
  ctx.scale(sz / 24, sz / 24);
  
  ctx.strokeStyle = color;
  ctx.lineWidth = 2;
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';

  // Middle line
  ctx.beginPath();
  ctx.moveTo(2 - 12, 12 - 12);
  ctx.lineTo(22 - 12, 12 - 12);
  ctx.stroke();

  // Outlined main drive chassis path
  ctx.beginPath();
  ctx.moveTo(5.45 - 12, 5.11 - 12);
  ctx.lineTo(2 - 12, 16 - 12);
  ctx.lineTo(2 - 12, 19 - 12);
  // corner rounding
  ctx.arcTo(2 - 12, 21 - 12, 4 - 12, 21 - 12, 2);
  ctx.lineTo(20 - 12, 21 - 12);
  ctx.arcTo(22 - 12, 21 - 12, 22 - 12, 19 - 12, 2);
  ctx.lineTo(22 - 12, 16 - 12);
  ctx.lineTo(18.55 - 12, 5.11 - 12);
  ctx.arcTo(18.55 - 12, 4 - 12, 16.73 - 12, 4 - 12, 2);
  ctx.lineTo(7.27 - 12, 4 - 12);
  ctx.arcTo(5.45 - 12, 4 - 12, 5.45 - 12, 5.11 - 12, 2);
  ctx.closePath();
  ctx.stroke();

  // Screws / LEDs
  ctx.beginPath();
  ctx.moveTo(6 - 12, 16 - 12); ctx.lineTo(6.01 - 12, 16 - 12);
  ctx.moveTo(10 - 12, 16 - 12); ctx.lineTo(10.01 - 12, 16 - 12);
  ctx.stroke();

  ctx.restore();
}

function drawIconNetwork(ctx, cx, cy, sz, color) {
  ctx.save();
  ctx.translate(cx, cy);
  ctx.scale(sz / 24, sz / 24);
  
  ctx.strokeStyle = color;
  ctx.lineWidth = 2;
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';

  // 3 node boxes
  ctx.beginPath();
  ctx.roundRect(16 - 12, 16 - 12, 6, 6, 1);
  ctx.roundRect(2 - 12, 16 - 12, 6, 6, 1);
  ctx.roundRect(9 - 12, 2 - 12, 6, 6, 1);
  ctx.stroke();

  // Connecting lines
  ctx.beginPath();
  ctx.moveTo(5 - 12, 16 - 12);
  ctx.lineTo(5 - 12, 13 - 12);
  ctx.arcTo(5 - 12, 12 - 12, 6 - 12, 12 - 12, 1);
  ctx.lineTo(18 - 12, 12 - 12);
  ctx.arcTo(19 - 12, 12 - 12, 19 - 12, 13 - 12, 1);
  ctx.lineTo(19 - 12, 16 - 12);
  ctx.stroke();

  // Stem to top node
  ctx.beginPath();
  ctx.moveTo(12 - 12, 12 - 12);
  ctx.lineTo(12 - 12, 8 - 12);
  ctx.stroke();

  ctx.restore();
}

function drawIconMusic(ctx, cx, cy, sz, color) {
  ctx.save();
  ctx.translate(cx, cy);
  ctx.scale(sz / 24, sz / 24);
  
  ctx.strokeStyle = color;
  ctx.fillStyle = color;
  ctx.lineWidth = 2;
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';

  ctx.beginPath();
  ctx.moveTo(9 - 12, 18 - 12);
  ctx.lineTo(9 - 12, 5 - 12);
  ctx.lineTo(21 - 12, 3 - 12);
  ctx.lineTo(21 - 12, 16 - 12);
  ctx.stroke();

  ctx.beginPath();
  ctx.arc(6 - 12, 18 - 12, 3, 0, Math.PI * 2);
  ctx.arc(18 - 12, 16 - 12, 3, 0, Math.PI * 2);
  ctx.fill();

  ctx.restore();
}

// 1. Clock Screen Renderer
function renderClock(ctx) {
  ctx.fillStyle = PALETTE.bg;
  ctx.fillRect(0, 0, 960, 640);

  const t = new Date();
  const hh = String(t.getHours()).padStart(2, '0');
  const mm = String(t.getMinutes()).padStart(2, '0');
  const ss = t.getSeconds();

  // Draw Time
  ctx.font = 'bold 220px Outfit, sans-serif';
  ctx.textBaseline = 'middle';
  
  // Measure colon width to calculate stable offsets
  ctx.textAlign = 'center';
  const colonWidth = ctx.measureText(':').width;
  const offset = colonWidth / 2 + 10; // Spacing padding

  // 1. Draw hours (right-aligned)
  ctx.textAlign = 'right';
  ctx.fillStyle = PALETTE.ink;
  ctx.fillText(hh, 480 - offset, 270);

  // 2. Draw minutes (left-aligned)
  ctx.textAlign = 'left';
  ctx.fillStyle = PALETTE.ink;
  ctx.fillText(mm, 480 + offset, 270);

  // 3. Draw blinking colon (centered)
  ctx.textAlign = 'center';
  const showColon = (Math.floor(Date.now() / 500) % 2 === 0);
  ctx.fillStyle = showColon ? PALETTE.ink : PALETTE.bg;
  ctx.fillText(':', 480, 270);

  // Draw Date
  const days = ['SUN', 'MON', 'TUE', 'WED', 'THU', 'FRI', 'SAT'];
  const months = ['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN', 'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC'];
  const dateStr = `${days[t.getDay()]}  ·  ${t.getDate()} ${months[t.getMonth()]}`;
  
  ctx.font = '400 36px Inter, sans-serif';
  ctx.fillStyle = PALETTE.muted;
  ctx.fillText(dateStr, 480, 480);

  // Draw Seconds Bar
  const barY = 620;
  const barH = 10;
  const barMargin = 32;
  const barWidth = 960 - (barMargin * 2);
  const fillWidth = barWidth * (ss / 59);

  // Background track
  drawCard(ctx, barMargin, barY, barWidth, barH, 5, PALETTE.track);
  // Filled bar
  if (fillWidth > 0) {
    drawCard(ctx, barMargin, barY, fillWidth, barH, 5, PALETTE.cpu);
  }
}

// 2. Stats Screen Renderer
function renderStats(ctx) {
  ctx.fillStyle = PALETTE.bg;
  ctx.fillRect(0, 0, 960, 640);

  // Header line
  ctx.font = 'bold 30px Outfit, sans-serif';
  ctx.fillStyle = PALETTE.ink;
  ctx.textAlign = 'left';
  ctx.fillText(currentStats ? currentStats.hostname : 'LOCALHOST', 32, 50);

  ctx.font = '300 28px Inter, sans-serif';
  ctx.fillStyle = PALETTE.muted;
  ctx.textAlign = 'right';
  ctx.fillText(currentStats ? currentStats.ip : '127.0.0.1', 928, 50);

  ctx.strokeStyle = PALETTE.track;
  ctx.lineWidth = 4;
  ctx.beginPath();
  ctx.moveTo(32, 72);
  ctx.lineTo(928, 72);
  ctx.stroke();

  // Grid layout (2x2 cards)
  const cardW = 432;
  const cardH = 240;
  const pad = 32;
  const gap = 24;

  const positions = [
    { x: pad, y: 104 }, // CPU
    { x: pad + cardW + gap, y: 104 }, // RAM
    { x: pad, y: 104 + cardH + gap }, // Disk
    { x: pad + cardW + gap, y: 104 + cardH + gap } // Net
  ];

  if (!currentStats) {
    ctx.font = '300 32px Inter, sans-serif';
    ctx.fillStyle = PALETTE.muted;
    ctx.textAlign = 'center';
    ctx.fillText("Gathering metrics...", 480, 320);
    return;
  }

  const startAngle = 0.95 * Math.PI;
  const sweepAngle = 1.1 * Math.PI;

  // CARD 1: CPU
  const cpuX = positions[0].x;
  const cpuY = positions[0].y;
  drawCard(ctx, cpuX, cpuY, cardW, cardH, 20, PALETTE.panel);
  drawIconCpu(ctx, cpuX + cardW / 2, cpuY + 40, 48, PALETTE.cpu);
  ctx.fillStyle = PALETTE.ink;
  ctx.textAlign = 'center';
  ctx.font = 'bold 42px Outfit, sans-serif';
  ctx.fillText(`${Math.round(currentStats.cpu)}%`, cpuX + cardW / 2, cpuY + 106);
  ctx.fillStyle = PALETTE.muted;
  ctx.font = '300 24px Inter, sans-serif';
  const cpuSub = currentStats.temp ? `${Math.round(currentStats.temp)}°C  ·  ${currentStats.cpu_freq.toFixed(1)} GHz` : `${currentStats.cpu_freq.toFixed(1)} GHz`;
  ctx.fillText(cpuSub, cpuX + cardW / 2, cpuY + 154);
  drawProgressBar(ctx, cpuX + 40, cpuY + 195, cardW - 80, 14, 7, currentStats.cpu, PALETTE.track, PALETTE.cpu);

  // CARD 2: RAM
  const ramX = positions[1].x;
  const ramY = positions[1].y;
  drawCard(ctx, ramX, ramY, cardW, cardH, 20, PALETTE.panel);
  drawIconRam(ctx, ramX + cardW / 2, ramY + 40, 48, PALETTE.ram);
  ctx.fillStyle = PALETTE.ink;
  ctx.textAlign = 'center';
  ctx.font = 'bold 42px Outfit, sans-serif';
  ctx.fillText(`${Math.round(currentStats.ram)}%`, ramX + cardW / 2, ramY + 106);
  ctx.fillStyle = PALETTE.muted;
  ctx.font = '300 24px Inter, sans-serif';
  const usedGb = (currentStats.ram_used / 1e9).toFixed(1);
  const totalGb = (currentStats.ram_total / 1e9).toFixed(1);
  ctx.fillText(`${usedGb} / ${totalGb} GB`, ramX + cardW / 2, ramY + 154);
  drawProgressBar(ctx, ramX + 40, ramY + 195, cardW - 80, 14, 7, currentStats.ram, PALETTE.track, PALETTE.ram);

  // CARD 3: Disk
  const diskX = positions[2].x;
  const diskY = positions[2].y;
  drawCard(ctx, diskX, diskY, cardW, cardH, 20, PALETTE.panel);
  drawIconDisk(ctx, diskX + cardW / 2, diskY + 40, 48, PALETTE.disk);
  ctx.fillStyle = PALETTE.ink;
  ctx.textAlign = 'center';
  ctx.font = 'bold 42px Outfit, sans-serif';
  ctx.fillText(`${Math.round(currentStats.disk_root_pct)}%`, diskX + cardW / 2, diskY + 106);
  ctx.fillStyle = PALETTE.muted;
  ctx.font = '300 24px Inter, sans-serif';
  const diskUsedGb = (currentStats.disk_used / 1e9).toFixed(0);
  const diskTotalGb = (currentStats.disk_total / 1e9).toFixed(0);
  ctx.fillText(`${diskUsedGb} / ${diskTotalGb} GB`, diskX + cardW / 2, diskY + 154);
  drawProgressBar(ctx, diskX + 40, diskY + 195, cardW - 80, 14, 7, currentStats.disk_root_pct, PALETTE.track, PALETTE.disk);

  // CARD 4: Network
  const netX = positions[3].x;
  const netY = positions[3].y;
  drawCard(ctx, netX, netY, cardW, cardH, 20, PALETTE.panel);
  drawIconNetwork(ctx, netX + cardW / 2, netY + 40, 48, PALETTE.net);
  
  // Draw speeds
  function formatNet(bytes) {
    if (bytes >= 1e6) return `${(bytes / 1e6).toFixed(1)}M`;
    if (bytes >= 1e3) return `${(bytes / 1e3).toFixed(0)}K`;
    return `${Math.round(bytes)}B`;
  }
  ctx.fillStyle = PALETTE.ink;
  ctx.textAlign = 'center';
  ctx.font = 'bold 42px Outfit, sans-serif';
  ctx.fillText(`↓ ${formatNet(currentStats.rx)}   ↑ ${formatNet(currentStats.tx)}`, netX + cardW / 2, netY + 106);
  
  // Total transfer session
  ctx.fillStyle = PALETTE.muted;
  ctx.font = '300 24px Inter, sans-serif';
  const rxTotalGb = (currentStats.net_recv_total / 1e9).toFixed(1);
  const txTotalGb = (currentStats.net_sent_total / 1e9).toFixed(1);
  ctx.fillText(`Total: ↓${rxTotalGb}G  ↑${txTotalGb}G`, netX + cardW / 2, netY + 154);

  // Use network percentage (load factor based on 100Mbps links)
  const totalSpeedBytes = currentStats.rx + currentStats.tx;
  const netPct = Math.min(100, (totalSpeedBytes / 12.5e6) * 100); // 12.5MB/s = 100Mbps
  drawProgressBar(ctx, netX + 40, netY + 195, cardW - 80, 14, 7, netPct, PALETTE.track, PALETTE.net);
}

// 3. Music Player Screen Renderer
function renderMusic(ctx) {
  // Solid subtle bottle green tint on black
  ctx.fillStyle = '#05140b';
  ctx.fillRect(0, 0, 960, 640);

  // Left Side: Album Art / Placeholder
  const artX = 60;
  const artY = 70;
  const artSize = 500;

  if (cachedArtworkImage && !isArtworkLoading) {
    ctx.save();
    ctx.beginPath();
    ctx.roundRect(artX, artY, artSize, artSize, 16);
    ctx.clip();
    ctx.drawImage(cachedArtworkImage, artX, artY, artSize, artSize);
    ctx.restore();
  } else {
    // Elegant dark-green gradient placeholder
    ctx.save();
    ctx.beginPath();
    ctx.roundRect(artX, artY, artSize, artSize, 16);
    ctx.clip();
    const placeholderGrad = ctx.createLinearGradient(artX, artY, artX, artY + artSize);
    placeholderGrad.addColorStop(0, '#102e1b');
    placeholderGrad.addColorStop(1, '#05140b');
    ctx.fillStyle = placeholderGrad;
    ctx.fill();
    // Render central music symbol
    drawIconMusic(ctx, artX + artSize / 2, artY + artSize / 2, 110, PALETTE.music);
    ctx.restore();
  }

  // Right Side Details
  const detailsX = 600;
  const textTitle = (currentMedia && currentMedia.title) ? currentMedia.title : 'Unknown Song';
  const textArtist = (currentMedia && currentMedia.artist) ? currentMedia.artist : 'Unknown Artist';
  const textAlbum = (currentMedia && currentMedia.album) ? currentMedia.album : '';

  ctx.textAlign = 'left';
  ctx.textBaseline = 'top';

  // Title
  ctx.fillStyle = PALETTE.ink;
  ctx.font = 'bold 44px Outfit, sans-serif';
  // Wrap text if too long
  let displayTitle = textTitle;
  if (displayTitle.length > 18) displayTitle = displayTitle.substring(0, 16) + '...';
  ctx.fillText(displayTitle, detailsX, 200);

  // Artist
  ctx.fillStyle = PALETTE.music;
  ctx.font = '500 32px Outfit, sans-serif';
  let displayArtist = textArtist;
  if (displayArtist.length > 20) displayArtist = displayArtist.substring(0, 18) + '...';
  ctx.fillText(displayArtist, detailsX, 275);

  // Album
  if (textAlbum) {
    ctx.fillStyle = PALETTE.muted;
    ctx.font = '300 24px Inter, sans-serif';
    let displayAlbum = textAlbum;
    if (displayAlbum.length > 22) displayAlbum = displayAlbum.substring(0, 20) + '...';
    ctx.fillText(displayAlbum, detailsX, 345);
  }

  // Draw status pill (PAUSED or PLAYING)
  const isPaused = currentMedia && currentMedia.status === 'Paused';
  const isPlaying = currentMedia && currentMedia.status === 'Playing';
  
  if (isPaused || isPlaying) {
    const pillY = 410;
    const pillW = 130;
    const pillH = 40;
    const pillR = 20;

    ctx.save();
    if (isPaused) {
      ctx.strokeStyle = '#d07010';
      ctx.fillStyle = 'rgba(208, 112, 16, 0.15)';
      ctx.beginPath();
      ctx.roundRect(detailsX, pillY, pillW, pillH, pillR);
      ctx.fill();
      ctx.stroke();

      ctx.fillStyle = '#ffb300';
      ctx.font = 'bold 18px Outfit, sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText('PAUSED', detailsX + pillW / 2, pillY + pillH / 2);
    } else {
      ctx.strokeStyle = '#1db954';
      ctx.fillStyle = 'rgba(29, 185, 84, 0.15)';
      ctx.beginPath();
      ctx.roundRect(detailsX, pillY, pillW, pillH, pillR);
      ctx.fill();
      ctx.stroke();

      ctx.fillStyle = '#1db954';
      ctx.font = 'bold 18px Outfit, sans-serif';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText('PLAYING', detailsX + pillW / 2, pillY + pillH / 2);
    }
    ctx.restore();
  }
}

// Active App Screen is no longer needed/used, omitted.

// Helpers for Claude / Antigravity countdown resets
function fmtReset(resetTs) {
  if (!resetTs) return "Resets soon";
  const now = Math.floor(Date.now() / 1000);
  const secs = Math.max(0, Math.floor(resetTs - now));
  if (secs === 0) return "Resets soon";
  if (secs < 3600) {
    return `Resets in ${Math.floor(secs / 60)}m`;
  }
  if (secs < 86400) {
    const h = Math.floor(secs / 3600);
    const m = Math.floor((secs % 3600) / 60);
    return `Resets in ${h}h ${m}m`;
  }
  const d = Math.floor(secs / 86400);
  const h = Math.floor((secs % 86400) / 3600);
  return `Resets in ${d}d ${h}h`;
}

// 5. Claude Usage Screen Renderer
function renderClaudeUsage(ctx) {
  const CL_PALETTE = {
    bg: '#0a0c10',
    panel: '#1f1f1e', // #1f1f1e warm black
    text: '#faf9f5', // warm white
    dim: '#b0aea5',
    accent: '#d97757', // terracotta
    green: '#788c5d',
    red: '#c0392b',
    track: '#2a2a28'
  };

  ctx.fillStyle = CL_PALETTE.bg;
  ctx.fillRect(0, 0, 960, 640);

  // Header line
  ctx.font = 'bold 36px Outfit, sans-serif';
  ctx.fillStyle = CL_PALETTE.text;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText("Claude", 480, 50);

  // Draw Logo if loaded
  if (imgClaudeLogo && imgClaudeLogo.complete && imgClaudeLogo.naturalWidth !== 0) {
    ctx.drawImage(imgClaudeLogo, 32, 14, 72, 72);
  }

  ctx.strokeStyle = CL_PALETTE.track;
  ctx.lineWidth = 4;
  ctx.beginPath();
  ctx.moveTo(32, 100);
  ctx.lineTo(928, 100);
  ctx.stroke();

  if (!currentClaudeUsage || !currentClaudeUsage.ok) {
    ctx.font = '300 32px Inter, sans-serif';
    ctx.fillStyle = CL_PALETTE.dim;
    ctx.fillText(currentClaudeUsage ? currentClaudeUsage.error : "Loading credentials...", 480, 340);
    return;
  }

  // Draw Panels
  const drawClaudePanel = (y0, pct, label, resetStr) => {
    const pW = 896;
    const pH = 220;
    const pX = 32;

    drawCard(ctx, pX, y0, pW, pH, 20, CL_PALETTE.panel);

    // Percentage on left
    ctx.font = 'bold 64px Outfit, sans-serif';
    ctx.fillStyle = CL_PALETTE.text;
    ctx.textAlign = 'left';
    ctx.textBaseline = 'middle';
    ctx.fillText(`${Math.round(pct)}%`, pX + 40, y0 + 70);

    // Pill on right
    ctx.font = 'bold 24px Outfit, sans-serif';
    const labelW = ctx.measureText(label).width;
    const pillW = labelW + 40;
    const pillH = 46;
    const pillX = pX + pW - 40 - pillW;
    const pillY = y0 + 47;
    drawCard(ctx, pillX, pillY, pillW, pillH, pillH / 2, CL_PALETTE.track);
    ctx.fillStyle = CL_PALETTE.text;
    ctx.textAlign = 'center';
    ctx.fillText(label, pillX + pillW / 2, pillY + pillH / 2);

    // Progress Bar (sits at 60% of panel height)
    const barH = 20;
    const barW = pW - 80;
    const barX = pX + 40;
    const barY = y0 + 130;
    drawCard(ctx, barX, barY, barW, barH, barH / 2, CL_PALETTE.track);

    let barColor = CL_PALETTE.green;
    if (pct >= 80) barColor = CL_PALETTE.red;
    else if (pct >= 50) barColor = CL_PALETTE.accent;

    const fillW = barW * (Math.min(100, pct) / 100);
    if (fillW > 0) {
      drawCard(ctx, barX, barY, fillW, barH, barH / 2, barColor);
    }

    // Reset details text below progress bar
    ctx.fillStyle = CL_PALETTE.dim;
    ctx.font = '300 20px Inter, sans-serif';
    ctx.textAlign = 'left';
    ctx.fillText(resetStr, pX + 40, y0 + 185);
  };

  drawClaudePanel(120, currentClaudeUsage.session_pct, "Current · 5h", fmtReset(currentClaudeUsage.session_reset_ts));
  drawClaudePanel(365, currentClaudeUsage.weekly_pct, "Weekly · 7d", fmtReset(currentClaudeUsage.weekly_reset_ts));
}

// 6. Antigravity Quota Screen Renderer
function renderAgUsage(ctx) {
  ctx.fillStyle = PALETTE.bg;
  ctx.fillRect(0, 0, 960, 640);

  // Header line
  ctx.font = 'bold 36px Outfit, sans-serif';
  ctx.fillStyle = PALETTE.ink;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText("Antigravity", 480, 46);

  // Draw Logo if loaded
  if (imgAgLogo && imgAgLogo.complete && imgAgLogo.naturalWidth !== 0) {
    ctx.drawImage(imgAgLogo, 32, 10, 72, 72);
  }

  ctx.strokeStyle = PALETTE.track;
  ctx.lineWidth = 4;
  ctx.beginPath();
  ctx.moveTo(32, 92);
  ctx.lineTo(928, 92);
  ctx.stroke();

  if (!currentAgUsage || !currentAgUsage.available) {
    ctx.font = '300 32px Inter, sans-serif';
    ctx.fillStyle = PALETTE.muted;
    ctx.fillText(currentAgUsage ? currentAgUsage.error : "Loading IDE quota...", 480, 320);
    return;
  }

  const groups = currentAgUsage.groups || [];
  if (groups.length === 0) {
    ctx.font = '300 30px Inter, sans-serif';
    ctx.fillStyle = PALETTE.muted;
    ctx.fillText("No model quota details available.", 480, 320);
    return;
  }

  // Row layouts
  const contentTop = 140;
  const contentH = 460;
  const rowH = 150;
  const barMargin = 40;
  const barW = 960 - (barMargin * 2);

  groups.forEach((grp, idx) => {
    const ry = contentTop + (idx * rowH);
    const color = `rgb(${grp.color.join(',')})`;
    const remain = grp.remaining;
    const used = 100 - remain;
    const barColor = (remain < 20) ? 'rgb(255, 82, 82)' : color;

    // Line 1: label left, usage right
    ctx.font = 'bold 26px Outfit, sans-serif';
    ctx.fillStyle = color;
    ctx.textAlign = 'left';
    ctx.textBaseline = 'top';
    ctx.fillText(grp.label, barMargin, ry + 10);

    ctx.font = 'bold 26px Outfit, sans-serif';
    ctx.fillStyle = barColor;
    ctx.textAlign = 'right';
    ctx.fillText(`${Math.round(used)}% used`, 960 - barMargin, ry + 10);

    // Main bar (thicker progress bar: 38px height)
    const barH = 38;
    const barY = ry + 52;
    drawCard(ctx, barMargin, barY, barW, barH, barH / 2, PALETTE.track);
    const fillW = barW * (Math.min(100, used) / 100);
    if (fillW > 0) {
      drawCard(ctx, barMargin, barY, fillW, barH, barH / 2, barColor);
    }

    // Reset details text below progress bar
    ctx.fillStyle = PALETTE.muted;
    ctx.font = '300 20px Inter, sans-serif';
    ctx.textAlign = 'left';
    ctx.fillText(fmtReset(grp.reset_ts), barMargin, barY + 52); // Pos: barY + barH + 14 = barY + 52
  });
}

// 7. Bangla.gov.bd Tools Stats Screen Renderer
function renderBanglaGov(ctx) {
  ctx.fillStyle = PALETTE.bg;
  ctx.fillRect(0, 0, 960, 640);

  // Header line
  ctx.font = 'bold 36px Outfit, sans-serif';
  ctx.fillStyle = PALETTE.ink;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText("bangla.gov.bd", 480, 50);

  // Draw Logo if loaded
  if (imgBanglaLogo && imgBanglaLogo.complete && imgBanglaLogo.naturalWidth !== 0) {
    ctx.drawImage(imgBanglaLogo, 32, 15, 70, 70);
  }

  ctx.strokeStyle = PALETTE.track;
  ctx.lineWidth = 4;
  ctx.beginPath();
  ctx.moveTo(32, 100);
  ctx.lineTo(928, 100);
  ctx.stroke();

  if (!currentBanglaGovData || !currentBanglaGovData.ok) {
    ctx.font = '300 32px Inter, sans-serif';
    ctx.fillStyle = PALETTE.muted;
    ctx.textAlign = 'center';
    ctx.fillText(currentBanglaGovData ? currentBanglaGovData.error : "Loading tools statistics...", 480, 340);
    return;
  }

  const tools = currentBanglaGovData.tools || [];
  if (tools.length === 0) {
    ctx.font = '300 32px Inter, sans-serif';
    ctx.fillStyle = PALETTE.muted;
    ctx.textAlign = 'center';
    ctx.fillText("No tool details available.", 480, 340);
    return;
  }

  // Row layouts (3 products)
  const cardW = 896;
  const cardH = 140;
  const cardX = 32;
  const positionsY = [130, 290, 450];

  tools.forEach((tool, idx) => {
    if (idx >= 3) return; // Only fit 3 tools max
    const cardY = positionsY[idx];

    // Draw card panel
    drawCard(ctx, cardX, cardY, cardW, cardH, 20, PALETTE.panel);

    // Draw product icon
    let imgNode = null;
    if (tool.id === "02b6b237-2875-44a4-80ea-e720f8d7d488") imgNode = imgProdPurno;
    else if (tool.id === "904bfa8b-5dd4-4f9b-b0dc-568d381717af") imgNode = imgProdSothik;
    else if (tool.id === "602a728d-b718-47ee-906f-d153f05d99fc") imgNode = imgProdBanglaWord;

    const iconX = cardX + 30;
    const iconY = cardY + 30;
    const iconSize = 80;

    if (imgNode && imgNode.complete && imgNode.naturalWidth !== 0) {
      ctx.save();
      ctx.beginPath();
      ctx.roundRect(iconX, iconY, iconSize, iconSize, 14);
      ctx.clip();
      ctx.drawImage(imgNode, iconX, iconY, iconSize, iconSize);
      ctx.restore();
    } else {
      // Fallback placeholder
      ctx.save();
      ctx.beginPath();
      ctx.roundRect(iconX, iconY, iconSize, iconSize, 14);
      ctx.clip();
      ctx.fillStyle = PALETTE.track;
      ctx.fill();
      drawIconMusic(ctx, iconX + iconSize/2, iconY + iconSize/2, 36, PALETTE.music);
      ctx.restore();
    }

    // Title and Version/Type details
    ctx.textAlign = 'left';
    ctx.textBaseline = 'middle';

    // Title
    ctx.fillStyle = PALETTE.ink;
    ctx.font = 'bold 28px Outfit, sans-serif';
    ctx.fillText(tool.title_en, cardX + 135, cardY + 45);

    // Subtitle: Version & Type
    ctx.fillStyle = PALETTE.muted;
    ctx.font = '300 20px Inter, sans-serif';
    ctx.fillText(`v${tool.version}  ·  ${tool.type_en}`, cardX + 135, cardY + 95);

    // Downloads on Right
    ctx.textAlign = 'right';

    // Download number
    ctx.fillStyle = '#00e5ff'; // Aqua accent
    ctx.font = 'bold 36px Outfit, sans-serif';
    const numStr = Number(tool.downloadCount).toLocaleString();
    ctx.fillText(numStr, cardX + cardW - 30, cardY + 45);

    // DOWNLOADS label
    ctx.fillStyle = PALETTE.muted;
    ctx.font = 'bold 14px Inter, sans-serif';
    ctx.fillText("DOWNLOADS", cardX + cardW - 30, cardY + 95);
  });
}

// --- MASTER MAIN LOOP ---

function drawActiveScreen() {
  if (currentScreen === 'clock') {
    renderClock(offCtx);
  } else if (currentScreen === 'stats') {
    renderStats(offCtx);
  } else if (currentScreen === 'music') {
    renderMusic(offCtx);
  } else if (currentScreen === 'claude') {
    renderClaudeUsage(offCtx);
  } else if (currentScreen === 'ag') {
    renderAgUsage(offCtx);
  } else if (currentScreen === 'bangla') {
    renderBanglaGov(offCtx);
  }

  // Draw 2x scaled offscreen to main LCD Canvas
  lcdCtx.clearRect(0, 0, 480, 320);
  lcdCtx.drawImage(offscreenCanvas, 0, 0, 480, 320);

  // Push frame data buffer to USB display via Electron IPC
  if (displayConnected) {
    const frameData = lcdCtx.getImageData(0, 0, 480, 320).data;
    ipcRenderer.send('draw-frame', frameData);
  }
}

// Main scheduler
function runManagerLoop() {
  setInterval(() => {
    // 1. Calculate rotation
    const elapsed = (Date.now() - screenStartTime) / 1000.0;
    const limit = getDuration(currentScreen);

    if (!autoRotationPaused && elapsed >= limit) {
      // Advance to next screen
      const idx = screenList.indexOf(currentScreen);
      currentScreen = screenList[(idx + 1) % screenList.length];
      screenStartTime = Date.now();
    }

    // Update rotation progress badge in UI
    if (autoRotationPaused) {
      rotationBadge.textContent = `Active: ${currentScreen.toUpperCase()} (Rotation Paused)`;
    } else {
      const nextIdx = (screenList.indexOf(currentScreen) + 1) % screenList.length;
      const nextScreen = screenList[nextIdx];
      const rem = Math.max(0, Math.ceil(getDuration(currentScreen) - elapsed));
      const formattedNext = nextScreen.toUpperCase().replace('-', ' ');
      rotationBadge.textContent = `Active: ${currentScreen.toUpperCase()} (Next: ${formattedNext} in ${rem}s)`;
    }

    // 2. Draw active screen graphics
    drawActiveScreen();
  }, 1000); // 1 FPS is lightweight, extremely stable, and perfectly sufficient for 1-second ticks
}

// Start Up
loadPreferences();
updateScreenList();
appendLog("Electron Dashboard GUI initialized.", "system");
runManagerLoop();
