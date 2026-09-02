process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';
const { app, BrowserWindow, ipcMain, Tray, Menu, nativeImage, shell, safeStorage } = require('electron');
const path = require('path');
const fs = require('fs');
const { spawn } = require('child_process');

// Icons live in assets/ so they ship inside the app package (asar). build/ has
// the same files for electron-builder (installer + embedded exe icon) but that
// folder is NOT bundled into the app.
const ICON_ICO = path.join(__dirname, 'assets', 'icon.ico');
const ICON_PNG = path.join(__dirname, 'assets', 'icon.png');

let mainWindow = null;
let tray = null;
let backendProcess = null;
let displayConnected = false;
let backendBusy = false;
let isQuitting = false;
let closeHintShown = false;

// Launched by the Windows "run at login" entry -> boot straight to the tray.
const startHidden = process.argv.includes('--hidden');

// Must match the NSIS shortcut AUMID so Windows shows our icon in the taskbar
// (and groups / allows pinning correctly).
if (process.platform === 'win32') {
  app.setAppUserModelId('com.ax206.displaymanager');
}

// --- Single instance: focus the running window instead of opening a 2nd copy ---
const gotLock = app.requestSingleInstanceLock();
if (!gotLock) {
  app.quit();
} else {
  app.on('second-instance', () => showMainWindow());
}

// --- Persisted app settings (userData\manager-config.json) --------------------
function configPath() {
  return path.join(app.getPath('userData'), 'manager-config.json');
}
function readConfig() {
  try {
    return JSON.parse(fs.readFileSync(configPath(), 'utf8'));
  } catch (e) {
    // First run: default to starting with Windows, since that is the point of
    // installing this as a background display driver.
    return { autoStart: true };
  }
}
function writeConfig(cfg) {
  try {
    fs.writeFileSync(configPath(), JSON.stringify(cfg, null, 2));
  } catch (e) {
    console.error('Failed to persist config:', e.message);
  }
}

// --- Run-at-login registration (HKCU ...\Run via Electron) -------------------
function applyAutoStart(enabled) {
  if (!app.isPackaged) return; // never register the dev electron.exe
  app.setLoginItemSettings({
    openAtLogin: enabled,
    path: process.execPath,
    args: ['--hidden']
  });
}
function isAutoStartEnabled() {
  if (!app.isPackaged) return readConfig().autoStart === true;
  try {
    return app.getLoginItemSettings({ args: ['--hidden'] }).openAtLogin;
  } catch (e) {
    return false;
  }
}

// --- Claude access token (entered in the GUI, DPAPI-encrypted on disk) -------
function claudeTokenPath() {
  return path.join(app.getPath('userData'), 'claude-token.bin');
}
function readClaudeToken() {
  try {
    const buf = fs.readFileSync(claudeTokenPath());
    if (safeStorage.isEncryptionAvailable()) return safeStorage.decryptString(buf);
    return buf.toString('utf8');
  } catch (e) {
    return '';
  }
}
function writeClaudeToken(token) {
  try {
    if (!token) {
      fs.rmSync(claudeTokenPath(), { force: true });
      return;
    }
    const data = safeStorage.isEncryptionAvailable()
      ? safeStorage.encryptString(token)
      : Buffer.from(token, 'utf8');
    fs.writeFileSync(claudeTokenPath(), data);
  } catch (e) {
    console.error('Failed to persist Claude token:', e.message);
  }
}
function pushClaudeTokenToBackend() {
  sendBackendCommand({ cmd: 'set_claude_token', token: readClaudeToken() || '' });
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1120,
    height: 780,
    minWidth: 940,
    minHeight: 640,
    frame: false,
    backgroundColor: '#1c1c1c',
    backgroundMaterial: 'mica',
    webPreferences: {
      nodeIntegration: true,
      contextIsolation: false,
      // Keep the 1 FPS render loop running while minimised / hidden so the
      // physical LCD does not freeze when the window is in the tray.
      backgroundThrottling: false
    },
    icon: ICON_ICO,
    title: "AX206 SmartCool Display Manager",
    show: false
  });

  mainWindow.loadFile('index.html');
  mainWindow.once('ready-to-show', () => {
    if (!startHidden) mainWindow.show();
  });

  // Keep the renderer's title-bar maximize glyph in sync with real window state
  const sendMaxState = () => {
    if (mainWindow && !mainWindow.isDestroyed() && mainWindow.webContents) {
      mainWindow.webContents.send('window-maximized-state', mainWindow.isMaximized());
    }
  };
  mainWindow.on('maximize', sendMaxState);
  mainWindow.on('unmaximize', sendMaxState);

  // Closing the window hides it to the tray (the app keeps driving the LCD);
  // real exit is via the tray "Quit" item.
  mainWindow.on('close', (e) => {
    if (!isQuitting) {
      e.preventDefault();
      mainWindow.hide();
      if (!closeHintShown && tray) {
        closeHintShown = true;
        try {
          tray.displayBalloon({
            title: 'Still running',
            content: 'AX206 Display Manager keeps driving the display from the tray. Right-click the tray icon to Quit.'
          });
        } catch (e2) { /* balloons are best-effort */ }
      }
    }
  });

  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

function showMainWindow() {
  if (!mainWindow || mainWindow.isDestroyed()) {
    createWindow();
    mainWindow.once('ready-to-show', () => mainWindow.show());
    return;
  }
  if (mainWindow.isMinimized()) mainWindow.restore();
  mainWindow.show();
  mainWindow.focus();
}

function buildTrayMenu() {
  return Menu.buildFromTemplate([
    { label: 'Open AX206 Display Manager', click: () => showMainWindow() },
    { type: 'separator' },
    {
      label: 'Start with Windows',
      type: 'checkbox',
      checked: isAutoStartEnabled(),
      click: (item) => {
        applyAutoStart(item.checked);
        const cfg = readConfig();
        cfg.autoStart = item.checked;
        writeConfig(cfg);
        if (mainWindow && !mainWindow.isDestroyed()) {
          mainWindow.webContents.send('autostart-state', item.checked);
        }
      }
    },
    { type: 'separator' },
    { label: 'Quit', click: () => { isQuitting = true; app.quit(); } }
  ]);
}

function createTray() {
  if (tray) return;

  // Prefer the PNG for the tray (most reliable across Electron versions), fall
  // back to the ICO, then to the embedded exe icon.
  let img = nativeImage.createFromPath(ICON_PNG);
  if (img.isEmpty()) img = nativeImage.createFromPath(ICON_ICO);
  if (img.isEmpty()) img = nativeImage.createFromPath(process.execPath);
  if (!img.isEmpty()) {
    img = img.resize({ width: 16, height: 16 });
    img.setTemplateImage(false);
  } else {
    console.error('Tray icon: no image source resolved.');
  }

  tray = new Tray(img);
  tray.setToolTip('AX206 Display Manager');
  tray.setContextMenu(buildTrayMenu());
  tray.on('click', () => showMainWindow());
  tray.on('double-click', () => showMainWindow());
}

// Custom title-bar window controls (frameless window)
ipcMain.on('window-minimize', () => {
  if (mainWindow && !mainWindow.isDestroyed()) mainWindow.minimize();
});
ipcMain.on('window-maximize', () => {
  if (!mainWindow || mainWindow.isDestroyed()) return;
  if (mainWindow.isMaximized()) mainWindow.unmaximize();
  else mainWindow.maximize();
});
ipcMain.on('window-close', () => {
  if (mainWindow && !mainWindow.isDestroyed()) mainWindow.close();
});

// Claude access token from the renderer
ipcMain.handle('get-claude-token-status', () => ({ hasToken: !!readClaudeToken() }));
ipcMain.on('set-claude-token', (event, token) => {
  const clean = (token || '').trim();
  writeClaudeToken(clean);
  sendBackendCommand({ cmd: 'set_claude_token', token: clean });
});

// Run-at-login toggle from the renderer
ipcMain.handle('get-autostart', () => isAutoStartEnabled());
ipcMain.on('set-autostart', (event, enabled) => {
  applyAutoStart(!!enabled);
  const cfg = readConfig();
  cfg.autoStart = !!enabled;
  writeConfig(cfg);
  if (tray) tray.setContextMenu(buildTrayMenu());
});

function startBackend() {
  let binPath;
  if (app.isPackaged) {
    binPath = path.join(process.resourcesPath, 'bin', 'backend.exe');
  } else {
    // Development mode
    const devBin = path.join(__dirname, 'bin', 'backend.exe');
    if (fs.existsSync(devBin)) {
      binPath = devBin;
    } else {
      binPath = null;
    }
  }

  // Writable data dir for the backend (plugins, downloaded images). The install
  // location is read-only, so the backend must never write next to its exe.
  const dataDirArg = `--data-dir=${app.getPath('userData')}`;

  let cmd, args;
  if (binPath) {
    cmd = binPath;
    args = [dataDirArg];
  } else {
    cmd = 'python';
    args = [path.join(__dirname, 'backend.py'), dataDirArg];
  }

  console.log(`Spawning backend process: ${cmd} ${args.join(' ')}`);
  backendProcess = spawn(cmd, args);

  // Hand the backend the stored Claude token as soon as its stdin is up.
  setTimeout(pushClaudeTokenToBackend, 400);

  // Auto-connect on startup (allow 1.5 seconds for renderer and backend to initialize)
  setTimeout(() => {
    sendBackendCommand({ cmd: "connect" });
  }, 1500);

  // Read stdout line by line
  let stdoutBuffer = '';
  backendProcess.stdout.on('data', (data) => {
    stdoutBuffer += data.toString();
    let lines = stdoutBuffer.split(/\r?\n/);
    stdoutBuffer = lines.pop(); // Keep partial line
    
    for (const line of lines) {
      const trimmed = line.trim();
      if (trimmed.startsWith('{')) {
        try {
          const payload = JSON.parse(trimmed);
          handleBackendMessage(payload);
        } catch (e) {
          console.error("Failed to parse backend stdout line:", e.message, "| line:", trimmed.slice(0, 100));
        }
      }
    }
  });

  backendProcess.stderr.on('data', (data) => {
    console.error("Backend Stderr:", data.toString().trim());
  });

  backendProcess.on('close', (code) => {
    console.log(`Backend process exited with code ${code}`);
    logToUI(`Backend process stopped (code ${code}).`, 'error');
    backendProcess = null;
    displayConnected = false;
    backendBusy = false;
    if (mainWindow && !mainWindow.isDestroyed() && mainWindow.webContents) {
      mainWindow.webContents.send('display-status', { connected: false });
    }
  });
}

function stopBackend() {
  if (backendProcess) {
    console.log("Terminating backend process...");
    backendProcess.kill();
    backendProcess = null;
  }
}

function handleBackendMessage(payload) {
  if (!mainWindow || mainWindow.isDestroyed()) return;

  if (payload.type === 'telemetry') {
    mainWindow.webContents.send('tick-data', payload.data);
  } else if (payload.type === 'log') {
    logToUI(payload.msg, payload.level || 'info');
  } else if (payload.type === 'status') {
    displayConnected = payload.connected;
    mainWindow.webContents.send('display-status', { connected: displayConnected });
  } else if (payload.type === 'draw_done') {
    backendBusy = false;
  } else {
    // Relay all other event notifications from backend stdout to UI renderer
    mainWindow.webContents.send('backend-event', payload);
  }
}

function sendBackendCommand(cmdObj) {
  if (backendProcess && backendProcess.stdin && backendProcess.stdin.writable) {
    backendProcess.stdin.write(JSON.stringify(cmdObj) + '\n');
  }
}

// Log a message in the renderer UI console
function logToUI(msg, type = 'info') {
  if (mainWindow && !mainWindow.isDestroyed() && mainWindow.webContents) {
    mainWindow.webContents.send('log-message', { msg, type });
  }
}

// IPC Handlers
ipcMain.on('backend-command', (event, cmdObj) => {
  sendBackendCommand(cmdObj);
});

ipcMain.on('connect-request', (event) => {
  sendBackendCommand({ cmd: "connect" });
  event.reply('connect-response', true);
});

ipcMain.on('disconnect-request', (event) => {
  sendBackendCommand({ cmd: "disconnect" });
  event.reply('disconnect-response', true);
});

ipcMain.on('draw-frame', (event, rgbaBuffer) => {
  if (!displayConnected) return;
  if (backendBusy) return; // Skip frame if backend is busy rendering the previous one
  backendBusy = true;
  // Ensure we have a Node.js Buffer to support 'base64' encoding conversion
  const buf = Buffer.isBuffer(rgbaBuffer) ? rgbaBuffer : Buffer.from(rgbaBuffer);
  const base64Frame = buf.toString('base64');
  sendBackendCommand({ cmd: "draw", frame: base64Frame });
});

// App Lifecycle
app.whenReady().then(() => {
  // Reconcile the run-at-login entry with the stored preference every launch.
  const cfg = readConfig();
  applyAutoStart(cfg.autoStart === true);

  createWindow();
  createTray();
  startBackend();
});

app.on('before-quit', () => {
  isQuitting = true;
  stopBackend();
});

// Keep the process alive in the tray after the window is closed.
app.on('window-all-closed', () => {
  // no-op on Windows: exit only through the tray "Quit" item
});

app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    createWindow();
    startBackend();
  } else {
    showMainWindow();
  }
});

app.on('quit', () => {
  if (tray) { tray.destroy(); tray = null; }
});
