const { app, BrowserWindow, ipcMain } = require('electron');
const path = require('path');
const { AX206Display } = require('./driver');
const { getStats } = require('./stats');
const { getActiveApp, getPlayingMedia } = require('./media');
const { fetchClaudeUsage, fetchAgUsage } = require('./providers');

let mainWindow = null;
let display = null;
let statsInterval = null;
let displayConnected = false;
let isWriting = false;

// Caches for API pollers
let claudeUsage = { ok: false, error: "Initial loading..." };
let agUsage = { available: false, groups: [], error: "Initial loading..." };
let lastClaudePoll = 0;
let lastAgPoll = 0;

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 950,
    height: 700,
    minWidth: 800,
    minHeight: 600,
    webPreferences: {
      nodeIntegration: true,
      contextIsolation: false
    },
    title: "AX206 SmartCool Display Manager",
    backgroundColor: '#0a0c10',
    show: false
  });

  mainWindow.loadFile('index.html');
  mainWindow.once('ready-to-show', () => {
    mainWindow.show();
  });

  mainWindow.on('closed', () => {
    mainWindow = null;
    cleanup();
  });
}

function cleanup() {
  stopStatsPolling();
  if (display) {
    try {
      display.close();
    } catch (e) {}
    display = null;
  }
}

// Stats & Media background polling
function startStatsPolling() {
  if (statsInterval) return;

  statsInterval = setInterval(async () => {
    if (!mainWindow) return;

    const now = Date.now();

    // Poll Claude usage every 60 seconds (non-blocking)
    if (now - lastClaudePoll > 60000) {
      lastClaudePoll = now;
      fetchClaudeUsage().then(data => {
        claudeUsage = data;
      }).catch(e => {
        claudeUsage = { ok: false, error: e.message };
      });
    }

    // Poll Antigravity status every 30 seconds (non-blocking)
    if (now - lastAgPoll > 30000) {
      lastAgPoll = now;
      fetchAgUsage().then(data => {
        agUsage = data;
      }).catch(e => {
        agUsage = { available: false, groups: [], error: e.message };
      });
    }

    // Collect fast-changing OS telemetry in parallel
    const [stats, activeApp, media] = await Promise.all([
      getStats(),
      getActiveApp(),
      getPlayingMedia()
    ]);

    // Send combined data payload to renderer UI
    mainWindow.webContents.send('tick-data', {
      stats,
      activeApp,
      media,
      claudeUsage,
      agUsage,
      displayConnected
    });
  }, 1000);
}


function stopStatsPolling() {
  if (statsInterval) {
    clearInterval(statsInterval);
    statsInterval = null;
  }
}

// Log a message in the renderer UI console
function logToUI(msg, type = 'info') {
  if (mainWindow) {
    mainWindow.webContents.send('log-message', { msg, type });
  }
}

// Display Connection Handling
async function connectDisplay() {
  if (displayConnected) return true;

  try {
    if (!display) {
      display = new AX206Display();
    }
    display.open();
    displayConnected = true;
    logToUI("AX206 Screen connected successfully!", "success");
    if (mainWindow) {
      mainWindow.webContents.send('display-status', { connected: true });
    }
    return true;
  } catch (e) {
    displayConnected = false;
    logToUI(`Connection failed: ${e.message}`, "error");
    if (mainWindow) {
      mainWindow.webContents.send('display-status', { connected: false });
    }
    return false;
  }
}

function disconnectDisplay() {
  if (display) {
    try {
      display.close();
    } catch (e) {}
    display = null;
  }
  displayConnected = false;
  logToUI("Disconnected from display.", "info");
  if (mainWindow) {
    mainWindow.webContents.send('display-status', { connected: false });
  }
}

// IPC Handlers

ipcMain.on('connect-request', async (event) => {
  const ok = await connectDisplay();
  event.reply('connect-response', ok);
});

ipcMain.on('disconnect-request', (event) => {
  disconnectDisplay();
  event.reply('disconnect-response', true);
});

// Receives 480x320 RGBA image buffer from renderer and pushes to USB screen
ipcMain.on('draw-frame', async (event, rgbaBuffer) => {
  if (!displayConnected || !display) return;
  if (isWriting) return; // Prevent overlapping USB writes

  isWriting = true;
  try {
    await display.drawRGBA(rgbaBuffer);
  } catch (e) {
    logToUI(`USB blit error: ${e.message}`, "error");
    displayConnected = false;
    if (mainWindow) {
      mainWindow.webContents.send('display-status', { connected: false });
    }
    
    // Attempt recovery
    logToUI("Attempting USB recovery...", "warning");
    try {
      await display.recover();
      logToUI("Recovery completed. Trying to reconnect...", "info");
      await connectDisplay();
    } catch (recErr) {
      logToUI(`Recovery failed: ${recErr.message}`, "error");
    }
  } finally {
    isWriting = false;
  }
});

// App Lifecycle
app.whenReady().then(() => {
  createWindow();
  startStatsPolling();

  // Auto-connect on start
  setTimeout(() => {
    connectDisplay();
  }, 1000);
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    createWindow();
    startStatsPolling();
  }
});
