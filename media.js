const { exec } = require('child_process');

// Helper to run a shell command and return its stdout
function runCmd(cmd) {
  return new Promise((resolve) => {
    exec(cmd, { timeout: 3000 }, (err, stdout, stderr) => {
      if (err || stderr) {
        resolve(null);
      } else {
        resolve(stdout.trim());
      }
    });
  });
}

// Helper to run PowerShell commands cleanly on Windows
function runPowerShell(code) {
  // Use -Command with a base64 encoded string to avoid escaping hell
  const buffer = Buffer.from(code, 'utf16le');
  const base64 = buffer.toString('base64');
  const cmd = `powershell.exe -NoProfile -NonInteractive -EncodedCommand ${base64}`;
  return runCmd(cmd);
}

// --- Windows Helpers ---

const WINDOWS_MEDIA_PS = `
[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control.Playlists, ContentType=WindowsRuntime] | Out-Null
[Windows.Storage.Streams.DataReader, Windows.Storage.Streams, ContentType=WindowsRuntime] | Out-Null
try {
  $manager = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync().GetResults()
  $session = $null
  $sessions = $manager.GetSessions()
  if ($sessions) {
      foreach ($s in $sessions) {
          $info = $s.TryGetInfoAsync().GetResults()
          if ($info -and $info.PlaybackStatus.ToString() -eq 'Playing') {
              $session = $s
              break
          }
      }
      if (-not $session) {
          $session = $manager.GetCurrentSession()
      }
      if (-not $session -and $sessions.Count -gt 0) {
          $session = $sessions[0]
      }
  } else {
      $session = $manager.GetCurrentSession()
  }
  if ($session) {
      $info = $session.TryGetInfoAsync().GetResults()
      $props = $session.TryGetMediaPropertiesAsync().GetResults()
      
      $base64 = $null
      if ($props.Thumbnail) {
        try {
          $stream = $props.Thumbnail.OpenReadAsync().GetResults()
          $reader = New-Object Windows.Storage.Streams.DataReader($stream)
          $size = $stream.Size
          $reader.LoadAsync($size).GetResults() | Out-Null
          $bytes = New-Object byte[] $size
          $reader.ReadBytes($bytes)
          $base64 = [Convert]::ToBase64String($bytes)
        } catch {
          # Ignore thumbnail exceptions so the track metadata can still be returned
        }
      }

      $obj = [PSCustomObject]@{
          title = $props.Title
          artist = $props.Artist
          album = $props.AlbumTitle
          status = $info.PlaybackStatus.ToString()
          thumbnail = $base64
      }
      $obj | ConvertTo-Json -Compress
  } else {
      "null"
  }
} catch {
  "null"
}
`;

const WINDOWS_ACTIVE_APP_PS = `
Add-Type -TypeDefinition @"
  using System;
  using System.Runtime.InteropServices;
  public class Win32 {
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);
  }
"@
try {
  $hwnd = [Win32]::GetForegroundWindow()
  if ($hwnd -eq [IntPtr]::Zero) {
    "null"
    exit
  }
  $pid = 0
  [Win32]::GetWindowThreadProcessId($hwnd, [ref]$pid) | Out-Null
  if ($pid -gt 0) {
    $proc = Get-Process -Id $pid
    $obj = [PSCustomObject]@{
        app = $proc.ProcessName
        title = $proc.MainWindowTitle
    }
    $obj | ConvertTo-Json -Compress
  } else {
    "null"
  }
} catch {
  "null"
}
`;

// --- macOS Helpers ---

const MAC_ACTIVE_APP_OSASCRIPT = `osascript -e '
tell application "System Events"
  set frontProcess to first process whose frontmost is true
  set procName to name of frontProcess
  try
    set winTitle to name of first window of frontProcess
  on error
    set winTitle to ""
  end try
  return procName & "|||" & winTitle
end tell'`;

const MAC_MEDIA_OSASCRIPT = `osascript -e '
set isRunning to false
set trackInfo to ""
try
  tell application "System Events" to set isRunning to exists (process "Music")
  if isRunning then
    tell application "Music"
      if player state is playing then
        set trackInfo to "Music|||" & name of current track & "|||" & artist of current track & "|||" & album of current track
      end if
    end tell
  end if
end try
if trackInfo is "" then
  try
    tell application "System Events" to set isRunning to exists (process "Spotify")
    if isRunning then
      tell application "Spotify"
        if player state is playing then
          set trackInfo to "Spotify|||" & name of current track & "|||" & artist of current track & "|||" & album of current track
        end if
      end tell
    end if
  end try
end if
return trackInfo'`;

// --- Public APIs ---

/**
 * Gets the current active application name and title.
 * @returns {Promise<{app: string, title: string}|null>}
 */
async function getActiveApp() {
  const platform = process.platform;
  try {
    if (platform === 'win32') {
      const res = await runPowerShell(WINDOWS_ACTIVE_APP_PS);
      if (res && res !== 'null') {
        return JSON.parse(res);
      }
    } else if (platform === 'darwin') {
      const res = await runCmd(MAC_ACTIVE_APP_OSASCRIPT);
      if (res && res.includes('|||')) {
        const parts = res.split('|||');
        return { app: parts[0], title: parts[1] || '' };
      }
    } else if (platform === 'linux') {
      // Try running xdotool to get active window
      const app = await runCmd("xdotool getactivewindow getwindowclassname");
      const title = await runCmd("xdotool getactivewindow getwindowname");
      if (app) {
        return { app, title: title || '' };
      }
    }
  } catch (e) {
    console.error("Active app poll error:", e);
  }
  return null;
}

/**
 * Gets the currently playing media info (title, artist, album, status).
 * @returns {Promise<{title: string, artist: string, album: string, player: string, playing: boolean}|null>}
 */
async function getPlayingMedia() {
  const platform = process.platform;
  try {
    if (platform === 'win32') {
      const res = await runPowerShell(WINDOWS_MEDIA_PS);
      if (res && res !== 'null') {
        const data = JSON.parse(res);
        return {
          title: data.title || 'Unknown Title',
          artist: data.artist || 'Unknown Artist',
          album: data.album || '',
          player: 'System Media',
          playing: data.status === 'Playing',
          thumbnail: data.thumbnail || null
        };
      }
    } else if (platform === 'darwin') {
      const res = await runCmd(MAC_MEDIA_OSASCRIPT);
      if (res && res.includes('|||')) {
        const parts = res.split('|||');
        return {
          player: parts[0],
          title: parts[1] || 'Unknown Title',
          artist: parts[2] || 'Unknown Artist',
          album: parts[3] || '',
          playing: true
        };
      }
    } else if (platform === 'linux') {
      // Use playerctl metadata (standard on many Linux distros)
      const player = await runCmd("playerctl metadata --format '{{playerName}}'");
      if (player) {
        const title = await runCmd("playerctl metadata title");
        const artist = await runCmd("playerctl metadata artist");
        const album = await runCmd("playerctl metadata album");
        const status = await runCmd("playerctl status");
        return {
          player: player,
          title: title || 'Unknown Title',
          artist: artist || 'Unknown Artist',
          album: album || '',
          playing: status === 'Playing'
        };
      }
    }
  } catch (e) {
    console.error("Media poll error:", e);
  }
  return null;
}

module.exports = {
  getActiveApp,
  getPlayingMedia
};
