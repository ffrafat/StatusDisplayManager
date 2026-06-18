process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';
const { app, BrowserWindow, ipcMain } = require('electron');
const path = require('path');
const fs = require('fs');
const { spawn } = require('child_process');

let mainWindow = null;
let backendProcess = null;
let displayConnected = false;
let backendBusy = false;

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
  });
}

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

  let cmd, args;
  if (binPath) {
    cmd = binPath;
    args = [];
  } else {
    cmd = 'python';
    args = [path.join(__dirname, 'backend.py')];
  }

  console.log(`Spawning backend process: ${cmd} ${args.join(' ')}`);
  backendProcess = spawn(cmd, args);

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
  createWindow();
  startBackend();
});

app.on('window-all-closed', () => {
  stopBackend();
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    createWindow();
    startBackend();
  }
});
