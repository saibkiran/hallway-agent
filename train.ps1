# train.ps1 -- Scene setup + mlagents-learn + Unity Play
# Called by launcher.ps1 when "3. Play / Train" is clicked.

param(
    [string]$RunId       = "hallway_run1",
    [string]$UnityExe    = "C:\Program Files\Unity\Hub\Editor\6000.4.2f1\Editor\Unity.exe",
    [string]$ProjectPath = "D:\results"
)

$VenvML       = "$ProjectPath\mlenv\Scripts\mlagents-learn.exe"
$Config       = "$ProjectPath\hallway_config.yaml"
$LogFile      = "$ProjectPath\mlagents_out.txt"
$SceneTrigger = "$ProjectPath\.autoscene"
$SceneReady   = "$ProjectPath\.scene_ready"
$PlayingFile  = "$ProjectPath\.unity_playing"   # written by PlayStateMonitor.cs

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
}
"@
Add-Type -AssemblyName System.Windows.Forms

function Get-UnityProcess {
    return Get-Process -Name "Unity" -ErrorAction SilentlyContinue | Select-Object -First 1
}
function Get-UnityWindow {
    $w = Get-Process -Name "Unity" -ErrorAction SilentlyContinue |
         Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $w) {
        $w = Get-Process -Name "Unity" -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    return $w
}

# Send Ctrl+P to Unity. Minimized delay so mlagents does not time out.
function Send-PlayToUnity ($proc) {
    $fresh = Get-Process -Name "Unity" -ErrorAction SilentlyContinue |
             Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    $handle = if ($fresh) { $fresh.MainWindowHandle } else { $proc.MainWindowHandle }

    if ($handle -ne 0) {
        [Win32]::ShowWindow($handle, 9)       | Out-Null   # SW_RESTORE
        [Win32]::SetForegroundWindow($handle) | Out-Null
        Start-Sleep -Milliseconds 600                       # reduced from 1500ms
    } else {
        Write-Host "  No window handle -- trying SendKeys anyway..." -ForegroundColor Yellow
        Start-Sleep -Milliseconds 400
    }
    [System.Windows.Forms.SendKeys]::SendWait("^p")
    Write-Host "  Ctrl+P sent -- Play mode starting." -ForegroundColor Green
}

# ====================================================================
# STEP 0  If Unity is already in Play mode, STOP it first.
#         PlayStateMonitor.cs writes .unity_playing on enter and deletes
#         it on exit. Without this check, Ctrl+P in step 5 would STOP
#         Play instead of starting it, causing a mlagents timeout.
# ====================================================================
Write-Host ""
Write-Host "[ 0/5 ]  Checking Play mode state..." -ForegroundColor Cyan

if (Test-Path $PlayingFile) {
    Write-Host "  Unity is currently in Play mode. Stopping it first..." -ForegroundColor Yellow
    $unity0 = Get-UnityWindow
    if ($unity0) {
        $fresh0 = Get-Process -Name "Unity" -ErrorAction SilentlyContinue |
                  Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        $h0 = if ($fresh0) { $fresh0.MainWindowHandle } else { $unity0.MainWindowHandle }
        if ($h0 -ne 0) {
            # Bring Unity to front, VERIFY it is foreground, THEN send key.
            # This prevents Ctrl+P going to Chrome or any other focused app.
            [Win32]::ShowWindow($h0, 9)       | Out-Null   # SW_RESTORE
            [Win32]::SetForegroundWindow($h0) | Out-Null
            Start-Sleep -Milliseconds 800   # give OS time to switch focus
            Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Focus {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
"@ -ErrorAction SilentlyContinue
            $fg = [Win32Focus]::GetForegroundWindow()
            if ($fg -eq $h0) {
                [System.Windows.Forms.SendKeys]::SendWait("^p")
                Write-Host "  Ctrl+P sent to Unity (confirmed foreground)." -ForegroundColor Green
            } else {
                Write-Host "  Unity could not be focused. Please press Ctrl+P in Unity manually to stop Play mode, then rerun train.ps1." -ForegroundColor Red
                exit 1
            }
        }
    }
    # Wait for .unity_playing to be deleted by PlayStateMonitor
    $waited0 = 0
    while ((Test-Path $PlayingFile) -and $waited0 -lt 15) {
        Start-Sleep -Seconds 1; $waited0++
    }
    Write-Host "  Play mode stopped. Proceeding..." -ForegroundColor Green
} else {
    Write-Host "  Unity is not in Play mode. Good." -ForegroundColor Green
}

# ====================================================================
# STEP 1  Open / verify Unity Editor
# ====================================================================
Write-Host ""
Write-Host "[ 1/5 ]  Checking Unity Editor..." -ForegroundColor Cyan

$unity = Get-UnityWindow
if ($unity) {
    Write-Host "  Unity already open (PID $($unity.Id))." -ForegroundColor Green
} else {
    $loading = Get-UnityProcess
    if ($loading) {
        Write-Host "  Unity loading (PID $($loading.Id))..." -ForegroundColor Yellow
    } else {
        if (-not (Test-Path $UnityExe)) {
            Write-Host "  ERROR: Unity.exe not found at:" -ForegroundColor Red
            Write-Host "    $UnityExe" -ForegroundColor Yellow
            Write-Host "  Update the UnityExe path in train.ps1." -ForegroundColor Yellow
            exit 1
        }
        Write-Host "  Launching Unity Editor..." -ForegroundColor Yellow
        Start-Process $UnityExe -ArgumentList "-projectPath `"$ProjectPath`""
    }
    $waited = 0
    while ($waited -lt 180) {
        Start-Sleep -Seconds 4; $waited += 4
        $unity = Get-UnityWindow
        if ($unity) {
            Write-Host "  Unity window ready ($waited s)." -ForegroundColor Green
            Start-Sleep -Seconds 5
            break
        }
        Write-Host "  Waiting for Unity... ($waited s)" -ForegroundColor DarkGray
    }
}

if (-not $unity) {
    Write-Host "  ERROR: Unity did not open." -ForegroundColor Red; exit 1
}

# ====================================================================
# STEP 2  Scene setup -- drop .autoscene, wait for .scene_ready
#         (AutoBuilder.cs only runs when NOT in Play mode, which is
#          guaranteed by step 0 above.)
# ====================================================================
Write-Host ""
Write-Host "[ 2/5 ]  Building HallwayAgent scene..." -ForegroundColor Cyan

Remove-Item $SceneReady   -ErrorAction SilentlyContinue
Remove-Item $SceneTrigger -ErrorAction SilentlyContinue

New-Item -Path $SceneTrigger -ItemType File -Force | Out-Null
Write-Host "  Scene trigger sent. Waiting for Unity to build..." -ForegroundColor Yellow

$waited = 0; $sceneOk = $false
while ($waited -lt 120) {
    Start-Sleep -Seconds 2; $waited += 2
    if (Test-Path $SceneReady) { $sceneOk = $true; break }
    if ($waited % 10 -eq 0) {
        Write-Host "  Building scene... ($waited s)" -ForegroundColor DarkGray
    }
}

if (-not $sceneOk) {
    Write-Host "  WARNING: Scene build timed out -- proceeding anyway." -ForegroundColor Yellow
} else {
    Write-Host "  Scene ready!" -ForegroundColor Green
    Remove-Item $SceneReady -ErrorAction SilentlyContinue
}

# ====================================================================
# STEP 3  Start mlagents-learn  (Editor mode -- NO --env flag)
# ====================================================================
Write-Host ""
Write-Host "[ 3/5 ]  Starting mlagents-learn (run-id: $RunId)..." -ForegroundColor Cyan

Remove-Item $LogFile -ErrorAction SilentlyContinue

$mlArgs = "`"$VenvML`" `"$Config`" --run-id=$RunId --force"
$mlProc = Start-Process powershell `
    -ArgumentList "-NoExit -Command `"$mlArgs 2>&1 | Tee-Object -FilePath '$LogFile'`"" `
    -PassThru

Write-Host "  mlagents PID: $($mlProc.Id)" -ForegroundColor Green

# ====================================================================
# STEP 4  Wait for port 5004 to be open  (mlagents is ready for Unity)
#         TCP port check is more reliable than log-file parsing because
#         Tee-Object can buffer output for 10-30 s before flushing to disk,
#         causing train.ps1 to send Ctrl+P too late and mlagents to time out.
# ====================================================================
Write-Host ""
Write-Host "[ 4/5 ]  Waiting for mlagents to open port 5004..." -ForegroundColor Cyan

$timeout = 90; $elapsed = 0; $ready = $false
while ($elapsed -lt $timeout) {
    Start-Sleep -Seconds 2; $elapsed += 2
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.Connect("localhost", 5004)
        $tcp.Close()
        $ready = $true
        Write-Host "  Port 5004 is open -- mlagents ready ($elapsed s)." -ForegroundColor Green
        break
    } catch {
        if ($elapsed % 10 -eq 0) {
            Write-Host "  Waiting for mlagents... ($elapsed s)" -ForegroundColor DarkGray
        }
    }
}

if (-not $ready) {
    Write-Host "  ERROR: mlagents did not open port 5004 in $timeout s. Check the mlagents window." -ForegroundColor Red
    exit 1
}
Write-Host "  mlagents confirmed on port 5004. Sending Play to Unity NOW..." -ForegroundColor Green

# ====================================================================
# STEP 5  AutoPlayMonitor.cs (in the Unity Editor) polls port 5004
#         every 2 seconds and enters Play mode automatically when it
#         detects mlagents-learn is listening.
#
#         We do NOT send Ctrl+P via SendKeys here because SendKeys
#         targets the FOCUSED window — if the user switches to any
#         other app (e.g. Chrome) Ctrl+P would trigger that app's
#         print dialog instead of starting Unity Play mode.
# ====================================================================
Write-Host ""
Write-Host "[ 5/5 ]  Waiting for AutoPlayMonitor to start Play mode..." -ForegroundColor Cyan
Write-Host "  AutoPlayMonitor.cs is polling port 5004 inside the Unity Editor." -ForegroundColor White
Write-Host "  Unity will enter Play mode automatically within ~2 seconds." -ForegroundColor White
Write-Host "  (You do NOT need to click anything in Unity or avoid other apps.)" -ForegroundColor DarkGray

Write-Host ""
Write-Host "=======================================" -ForegroundColor Green
Write-Host "  TRAINING STARTED!" -ForegroundColor Green
Write-Host "  The blue cube agent will start moving" -ForegroundColor White
Write-Host "  within 15-30 s. Watch the Game view." -ForegroundColor White
Write-Host ""
Write-Host "  Score HUD: top-left of the Game view" -ForegroundColor Cyan
Write-Host "  Scores saved to: $ProjectPath\scores.csv" -ForegroundColor Cyan
Write-Host "  TensorBoard: tensorboard --logdir results" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Green
