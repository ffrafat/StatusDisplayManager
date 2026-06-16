const { exec } = require('child_process');
const path = require('path');
const fs = require('fs');
const os = require('os');

const WINDOWS_MEDIA_DEBUG_PS = `
[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control.Playlists, ContentType=WindowsRuntime] | Out-Null
[Windows.Storage.Streams.DataReader, Windows.Storage.Streams, ContentType=WindowsRuntime] | Out-Null
Add-Type -AssemblyName System.Runtime.WindowsRuntime

$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { 
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation\`1' 
})[0]

function Await-WinRT {
    param($WinRtTask, $ResultType)
    $asTask = $asTaskGeneric.MakeGenericMethod($ResultType)
    $netTask = $asTask.Invoke($null, @($WinRtTask))
    $netTask.Wait(-1) | Out-Null
    return $netTask.Result
}

# Minimal C# inspector - no WinRT-specific assembly references needed
try {
Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Reflection;

public static class AsyncInspector {
    public static string Describe(object obj) {
        if (obj == null) return "null";
        var sb = new StringBuilder();
        var type = obj.GetType();
        sb.AppendLine("Type: " + type.FullName);
        var ifaces = type.GetInterfaces();
        sb.AppendLine("Interfaces (" + ifaces.Length + "):");
        foreach (var iface in ifaces) sb.AppendLine("  " + iface.FullName);
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        sb.AppendLine("Properties (" + props.Length + "):");
        foreach (var p in props) {
            try { sb.AppendLine("  " + p.Name + " = " + p.GetValue(obj)); }
            catch (Exception ex) { sb.AppendLine("  " + p.Name + " = <error: " + ex.InnerException?.Message ?? ex.Message + ">"); }
        }
        return sb.ToString();
    }
    
    public static object TryGetResults(object asyncOp) {
        if (asyncOp == null) return null;
        try {
            var m = asyncOp.GetType().GetMethod("GetResults");
            if (m == null) return null;
            return m.Invoke(asyncOp, null);
        } catch { return null; }
    }
}
"@
  Write-Output "AsyncInspector compiled OK"
} catch {
  Write-Output "AsyncInspector compile failed: $_"
}

try {
  $managerType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]
  $manager = Await-WinRT ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) $managerType
  
  $sessions = $manager.GetSessions()
  if ($sessions) {
      $count = $sessions.Count
      Write-Output "Found $count media sessions\`n"
      foreach ($s in $sessions) {
          try {
              $playback = $s.GetPlaybackInfo()
              $status = "Unknown"
              if ($playback -and $playback.PlaybackStatus) {
                  $status = $playback.PlaybackStatus.ToString()
              }
              
              $propsType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties]
              $props = Await-WinRT ($s.TryGetMediaPropertiesAsync()) $propsType
              $title = $props.Title
              $artist = $props.Artist
              
              $app = $s.SourceAppUserModelId
              if ($app -and $app.Contains("!")) { $app = $app.Split("!")[0] }
              if ($app -and $app.Contains("\\")) { $app = [System.IO.Path]::GetFileName($app) }
              if (-not $app) { $app = "Unknown App" }
              
              Write-Output "$app"
              Write-Output "Title: $title"
              Write-Output "Artist: $artist"
              Write-Output "Status: $status"
              
              if ($props.Thumbnail) {
                  Write-Output "Thumbnail: Found ref"
                  
                  # Attempt 1: Try calling GetResults() immediately (synchronous completion)
                  $asyncOp = $props.Thumbnail.OpenReadAsync()
                  Write-Output "AsyncOp type: $($asyncOp.GetType().FullName)"
                  
                  try {
                      $stream = $asyncOp.GetResults()
                      Write-Output "GetResults() succeeded immediately! Stream type: $($stream.GetType().FullName)"
                      Write-Output "Stream size: $($stream.Size)"
                      
                      $reader = New-Object Windows.Storage.Streams.DataReader($stream)
                      $size = $stream.Size
                      $loaded = Await-WinRT ($reader.LoadAsync($size)) [System.UInt32]
                      $bytes = New-Object byte[] $size
                      $reader.ReadBytes($bytes)
                      $base64 = [Convert]::ToBase64String($bytes)
                      Write-Output "Thumbnail base64 length: $($base64.Length)"
                      $reader.Dispose()
                      $stream.Dispose()
                  } catch {
                      Write-Output "GetResults() immediately failed: $_"
                  }
                  
                  # Attempt 2: Inspect async op object via C# reflection if compiled
                  if (Get-Command -Name "AsyncInspector" -ErrorAction SilentlyContinue -CommandType All 2>$null) {
                  } elseif ([System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetType("AsyncInspector") -ne $null } | Select-Object -First 1) {
                      Write-Output ([AsyncInspector]::Describe($asyncOp))
                  }
              } else {
                  Write-Output "Thumbnail: No thumbnail ref"
              }
              Write-Output ""
          } catch {
              Write-Output "Error reading session: $_"
              Write-Output ""
          }
      }
  } else {
      Write-Output "Found 0 media sessions"
  }
} catch {
  Write-Output "Failed to retrieve media session manager: $_"
}
`;

function runPowerShell(code) {
  const tempScriptPath = path.join(os.tmpdir(), 'ax206_media_debug.ps1');
  fs.writeFileSync(tempScriptPath, code, 'utf8');
  const cmd = `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "${tempScriptPath}"`;
  return new Promise((resolve) => {
    exec(cmd, { timeout: 20000 }, (err, stdout, stderr) => {
      if (err) resolve(`Error: ${err.message}\n${stderr}`);
      else resolve(stdout.trim());
    });
  });
}

async function main() {
  if (process.platform !== 'win32') { console.log("Windows only."); process.exit(0); }
  console.log("Querying Windows GSMTC for active media sessions...\n");
  const output = await runPowerShell(WINDOWS_MEDIA_DEBUG_PS);
  console.log(output);
}

main();
