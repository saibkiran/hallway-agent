# launcher.ps1 -- HallwayAgent Game Launcher
# Double-click LAUNCH.bat to open.

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinAPI {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
}
"@
[System.Windows.Forms.Application]::EnableVisualStyles()

$root      = Split-Path -Parent $MyInvocation.MyCommand.Path
$unityExe  = "C:\Program Files\Unity\Hub\Editor\6000.4.2f1\Editor\Unity.exe"
$mlExe     = "$root\mlenv\Scripts\mlagents-learn.exe"
$envExe    = "$root\build\HallwayAgent.exe"
$config    = "$root\hallway_config.yaml"

# ── GitHub Releases URL ──────────────────────────────────────────────────────
# DEVELOPER: After building in Unity (File > Build Settings > Build),
#   zip the build\ folder and upload it to GitHub Releases.
#   Then paste the direct download URL for the zip below.
#   Evaluators without Unity will download it automatically.
#
#   Example: "https://github.com/YourName/HallwayAgent/releases/latest/download/HallwayAgent-build-windows.zip"
$releaseUrl = "https://github.com/YOUR_USERNAME/HallwayAgent/releases/latest/download/HallwayAgent-build-windows.zip"

$script:mlProc        = $null
$script:watchTimer    = $null
$script:playTimer     = $null
$script:standaloneLog = "$root\mlagents_out.txt"
$script:deployProc    = $null
$script:deployTimer   = $null
$script:deployTicks   = 0

# ====================================================================
# COLOUR HELPERS
# ====================================================================
function C($r,$g,$b){ [System.Drawing.Color]::FromArgb([int]$r,[int]$g,[int]$b) }
$BG      = C 18  18  26
$PANEL   = C 12  12  18
$INPUT   = C  8   8  14
$GREEN   = C 72  200 110
$YELLOW  = C 230 185  55
$RED     = C 220  65  65
$CYAN    = C 75  175 225
$GRAY    = C 115 115 138
$WHITE   = C 195 195 208
$ORANGE  = C 220 130  40

# ====================================================================
# FORM
# ====================================================================
$form               = New-Object System.Windows.Forms.Form
$form.Text          = "HallwayAgent  |  AI Game Launcher"
$form.Size          = New-Object System.Drawing.Size(700, 620)
$form.MinimumSize   = $form.Size
$form.StartPosition = "CenterScreen"
$form.BackColor     = $BG
$form.ForeColor     = $WHITE
$form.FormBorderStyle = "FixedSingle"
$form.MaximizeBox   = $false

# ── Header ──────────────────────────────────────────────────────────
$pnlH            = New-Object System.Windows.Forms.Panel
$pnlH.Dock       = "Top"
$pnlH.Height     = 80
$pnlH.BackColor  = $PANEL
$form.Controls.Add($pnlH)

$lblGame          = New-Object System.Windows.Forms.Label
$lblGame.Text     = "  HallwayAgent"
$lblGame.Font     = New-Object System.Drawing.Font("Segoe UI", 24, [System.Drawing.FontStyle]::Bold)
$lblGame.ForeColor= $GREEN
$lblGame.Location = New-Object System.Drawing.Point(6, 6)
$lblGame.Size     = New-Object System.Drawing.Size(480, 46)
$pnlH.Controls.Add($lblGame)

$lblTagline       = New-Object System.Windows.Forms.Label
$lblTagline.Text  = "  Watch an AI learn to navigate a memory maze using Reinforcement Learning"
$lblTagline.Font  = New-Object System.Drawing.Font("Segoe UI", 8)
$lblTagline.ForeColor = $GRAY
$lblTagline.Location  = New-Object System.Drawing.Point(6, 52)
$lblTagline.Size      = New-Object System.Drawing.Size(680, 20)
$pnlH.Controls.Add($lblTagline)

# ── Status indicators ────────────────────────────────────────────────
$pnlStat           = New-Object System.Windows.Forms.Panel
$pnlStat.Location  = New-Object System.Drawing.Point(12, 88)
$pnlStat.Size      = New-Object System.Drawing.Size(672, 48)
$pnlStat.BackColor = C 24 24 34
$form.Controls.Add($pnlStat)

function Lbl($text, $x) {
    $l = New-Object System.Windows.Forms.Label
    $l.Font     = New-Object System.Drawing.Font("Consolas", 9)
    $l.Location = New-Object System.Drawing.Point($x, 8)
    $l.Size     = New-Object System.Drawing.Size(220, 32)
    $l.Text     = $text
    $pnlStat.Controls.Add($l)
    return $l
}
$lblStatML    = Lbl "" 8
$lblStatBuild = Lbl "" 234
$lblStatMode  = Lbl "" 460

function Test-UnityLicense {
    $log = "$root\Logs\Editor.log"
    if (-not (Test-Path $log)) { return $true }   # no log = assume OK
    $tail = Get-Content $log -Tail 80 -ErrorAction SilentlyContinue
    if ($tail -match "Licensing initialization failed") { return $false }
    return $true
}

function Refresh-Status {
    $ml  = Test-Path $mlExe
    $exe = Test-Path $envExe
    if ($ml)  { $lblStatML.ForeColor=$GREEN;  $lblStatML.Text   ="[+] mlagents ready" }
    else      { $lblStatML.ForeColor=$RED;    $lblStatML.Text   ="[!] mlagents missing" }
    if ($exe) { $lblStatBuild.ForeColor=$GREEN;  $lblStatBuild.Text="[+] game build ready" }
    else      { $lblStatBuild.ForeColor=$YELLOW; $lblStatBuild.Text="[~] no game build yet" }
    if ($ml -and $exe) {
        $lblStatMode.ForeColor=$GREEN;  $lblStatMode.Text="[>] ready to play"
    } elseif ($ml) {
        $lblStatMode.ForeColor=$YELLOW; $lblStatMode.Text="[~] needs game build"
    } else {
        $lblStatMode.ForeColor=$GRAY;   $lblStatMode.Text="[ ] run Setup first"
    }
}

# ── Log area ─────────────────────────────────────────────────────────
$txtLog            = New-Object System.Windows.Forms.RichTextBox
$txtLog.Location   = New-Object System.Drawing.Point(12, 144)
$txtLog.Size       = New-Object System.Drawing.Size(672, 350)
$txtLog.BackColor  = $INPUT
$txtLog.Font       = New-Object System.Drawing.Font("Consolas", 9)
$txtLog.ReadOnly   = $true
$txtLog.ScrollBars = "Vertical"
$txtLog.BorderStyle= "None"
$form.Controls.Add($txtLog)

function Log($msg, $col = "White") {
    $c = switch ($col) {
        "Green"  {$GREEN} "Yellow"{$YELLOW} "Red"   {$RED}
        "Cyan"   {$CYAN}  "Gray"  {$GRAY}   "Orange"{$ORANGE} default{$WHITE}
    }
    $txtLog.SelectionStart  = $txtLog.TextLength
    $txtLog.SelectionLength = 0
    $txtLog.SelectionColor  = $c
    $txtLog.AppendText("$msg`n")
    $txtLog.ScrollToCaret()
    $form.Refresh()
}

# ── Buttons ──────────────────────────────────────────────────────────
function Btn($t,$x,$y,$w,$r,$g,$b) {
    $btn = New-Object System.Windows.Forms.Button
    $btn.Text      = $t
    $btn.Location  = New-Object System.Drawing.Point($x, $y)
    $btn.Size      = New-Object System.Drawing.Size($w, 50)
    $btn.BackColor = C $r $g $b
    $btn.ForeColor = [System.Drawing.Color]::White
    $btn.Font      = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
    $btn.FlatStyle = "Flat"
    $btn.FlatAppearance.BorderSize = 0
    $btn.Cursor    = [System.Windows.Forms.Cursors]::Hand
    $form.Controls.Add($btn)
    return $btn
}

$btnSetup  = Btn "  1. Setup"       12  506 150  40 105  50
$btnBuild  = Btn "  2. Build Game"  170 506 150  85  65  25
$btnPlay   = Btn "  3. Play / Train"328 506 180  30  80 160
$btnStop   = Btn "  Stop"           516 506 170 130  38  38

$btnBoard  = Btn "  TensorBoard"    12  562 180  70  44 110
$btnDeploy = Btn "  4. Deploy EXE" 200  562 260  30 110  50
$lblHelp   = New-Object System.Windows.Forms.Label
$lblHelp.Text      = "Deploy: build standalone EXE after training is done."
$lblHelp.Font      = New-Object System.Drawing.Font("Segoe UI", 8)
$lblHelp.ForeColor = $GRAY
$lblHelp.Location  = New-Object System.Drawing.Point(468, 574)
$lblHelp.Size      = New-Object System.Drawing.Size(222, 18)
$form.Controls.Add($lblHelp)

# ====================================================================
# 1. SETUP
# ====================================================================
$btnSetup.Add_Click({
    $btnSetup.Enabled = $false
    $btnSetup.Text    = "  Installing..."
    Log ""; Log "[ SETUP ]  Installing required packages..." "Cyan"

    if (Test-Path $mlExe) {
        Log "  mlagents already installed. Nothing to do." "Green"
        $btnSetup.Text = "  1. Setup"; $btnSetup.Enabled = $true; return
    }

    $py = $null
    try { if ((& py -3.10 --version 2>&1) -match "3\.10") { $py = "py -3.10" } } catch {}
    if (-not $py) {
        try { if ((& python --version 2>&1) -match "3\.10") { $py = "python" } } catch {}
    }

    if (-not $py) {
        Log "  Downloading Python 3.10.11..." "Yellow"
        $dl = "$env:TEMP\py310.exe"
        try {
            Invoke-WebRequest "https://www.python.org/ftp/python/3.10.11/python-3.10.11-amd64.exe" `
                -OutFile $dl -UseBasicParsing
            Start-Process $dl -ArgumentList "/quiet InstallAllUsers=0 PrependPath=1 Include_test=0" -Wait
            Remove-Item $dl -ErrorAction SilentlyContinue
            $env:PATH = [Environment]::GetEnvironmentVariable("PATH","User")+";"+$env:PATH
            $py = "py -3.10"
            Log "  Python 3.10.11 installed." "Green"
        } catch {
            Log "  ERROR: Download failed. Install Python 3.10 from python.org then retry." "Red"
            $btnSetup.Text = "  1. Setup"; $btnSetup.Enabled = $true; return
        }
    } else {
        Log "  Python 3.10 found." "Green"
    }

    Log "  Creating virtual environment..." "Yellow"
    & cmd /c "$py -m venv `"$root\mlenv`"" 2>&1 | Out-Null

    $pip = "$root\mlenv\Scripts\pip.exe"

    Log "  Installing mlagents 1.1.0  (2-3 min, please wait)..." "Yellow"
    & $pip install --quiet mlagents==1.1.0 2>&1 | Out-Null

    Log "  Pinning numpy (mlagents needs <1.24, numpy 2.x breaks training)..." "Yellow"
    & $pip install --quiet "numpy>=1.23.5,<1.24.0" --force-reinstall 2>&1 | Out-Null

    Log "  Pinning protobuf (mlagents needs <3.21)..." "Yellow"
    & $pip install --quiet "protobuf>=3.20.2,<3.21" --force-reinstall 2>&1 | Out-Null

    Log "  Installing onnx 1.15.0 + onnxscript (model export)..." "Yellow"
    & $pip install --quiet "onnx==1.15.0" onnxscript 2>&1 | Out-Null

    if (Test-Path $mlExe) {
        Log "  Setup complete! All packages installed." "Green"
        Log "  Next: click  [ 2. Build Game ]  or  [ 3. Play / Train ]" "Green"
    } else {
        Log "  ERROR: installation failed. Check your internet connection." "Red"
    }
    Refresh-Status
    $btnSetup.Text = "  1. Setup"; $btnSetup.Enabled = $true
})

# ====================================================================
# 2. BUILD / GET GAME
#    • Unity present  → build from source via AutoBuilder.cs
#    • Unity absent   → download pre-built zip from GitHub Releases
# ====================================================================
$btnBuild.Add_Click({

    if (Test-Path $envExe) {
        Log ""; Log "  build\HallwayAgent.exe already exists." "Green"
        Log "  Delete the build\ folder and click again to get a fresh copy." "Gray"
        return
    }

    $hasUnity = Test-Path $unityExe

    # ================================================================
    # PATH A: No Unity → download pre-built release zip
    # ================================================================
    if (-not $hasUnity) {
        Log ""; Log "[ GET BUILD ]  Unity not found - downloading pre-built game..." "Cyan"

        if ($releaseUrl -match "YOUR_USERNAME") {
            Log "  Release URL not configured in launcher.ps1." "Red"
            Log ""
            Log "  DEVELOPER STEPS:" "Yellow"
            Log "    1. Open project in Unity (File -> Open Project)" "White"
            Log "    2. File -> Build Settings -> Platform: Windows -> Build" "White"
            Log "    3. Save output into:  $root\build\" "White"
            Log "    4. Zip the build\ folder as HallwayAgent-build-windows.zip" "White"
            Log "    5. Upload to GitHub Releases" "White"
            Log "    6. Paste the download URL into `$releaseUrl in launcher.ps1" "White"
            return
        }

        $btnBuild.Enabled = $false; $btnBuild.Text = "  Downloading..."
        $btnPlay.Enabled  = $false
        $zip = "$env:TEMP\HallwayAgent-build.zip"

        try {
            Log "  URL: $releaseUrl" "Gray"
            Log "  Downloading... (size depends on build, may take a minute)" "Yellow"
            Invoke-WebRequest $releaseUrl -OutFile $zip -UseBasicParsing

            Log "  Extracting game files..." "Yellow"
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            New-Item -Path "$root\build" -ItemType Directory -Force | Out-Null
            [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, "$root\build")
            Remove-Item $zip -ErrorAction SilentlyContinue

            if (Test-Path $envExe) {
                Log ""
                Log "  Game downloaded and ready!" "Green"
                Log "  Click  [ 3. Play / Train ]  to watch the AI!" "Green"
                Refresh-Status
                [System.Windows.Forms.MessageBox]::Show(
                    "Download complete!`n`nClick  [ 3. Play / Train ]  to watch the AI play.",
                    "Ready", [System.Windows.Forms.MessageBoxButtons]::OK,
                    [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
            } else {
                Log "  ERROR: HallwayAgent.exe not found after extraction." "Red"
                Log "  Expected path: $envExe" "Yellow"
                Log "  Ensure the zip contains HallwayAgent.exe directly inside (not in a sub-folder)." "Yellow"
            }
        } catch {
            Log "  ERROR: Download failed - $($_.Exception.Message)" "Red"
            Log "  Check your internet connection and the release URL." "Yellow"
        }

        $btnBuild.Text = "  2. Build Game"; $btnBuild.Enabled = $true
        $btnPlay.Enabled = $true
        return
    }

    # ================================================================
    # PATH B: Unity present → build from source via AutoBuilder.cs
    # ================================================================
    Log ""; Log "[ BUILD GAME ]  Starting automated Unity build..." "Cyan"

    $btnBuild.Enabled = $false; $btnBuild.Text = "  Building..."
    $btnPlay.Enabled  = $false

    $script:triggerFile      = "$root\.autobuild"
    $script:projectEditorLog = "$root\Logs\Editor.log"

    # Detect if Unity is already open with a proper window
    $openUnity = Get-Process -Name "Unity" -ErrorAction SilentlyContinue |
                 Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1

    if ($openUnity) {
        Log "  Unity Editor already open (PID $($openUnity.Id))." "Green"
        Log "  Waiting for scripts to compile (30 s)..." "Yellow"
        $script:buildPhase = "settle"; $script:buildTicks = 0
    } else {
        Get-Process -Name "Unity" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 600
        Remove-Item $script:projectEditorLog -ErrorAction SilentlyContinue

        Log "  Launching Unity Editor directly..." "Yellow"
        Start-Process $unityExe -ArgumentList "-projectPath `"$root`""
        $script:buildPhase = "open"; $script:buildTicks = 0
    }

    # ---- State machine timer ----------------------------------------
    $script:buildTimer = New-Object System.Windows.Forms.Timer
    $script:buildTimer.Interval = 4000

    $script:buildTimer.Add_Tick({
        if ($script:buildPhase -eq "open") {
            $w      = Get-Process -Name "Unity" -ErrorAction SilentlyContinue |
                      Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
            $hasLog = Test-Path $script:projectEditorLog
            if ($w -and $hasLog) {
                Log "  Unity Editor open. Waiting for scripts to compile (30 s)..." "Yellow"
                $script:buildPhase = "settle"; $script:buildTicks = 0
            } else {
                $script:buildTicks++
                if ($script:buildTicks % 5 -eq 0) {
                    Log "  Waiting for Unity to load... ($($script:buildTicks * 4) s)" "Gray"
                }
                if ($script:buildTicks -gt 60) {
                    $script:buildTimer.Stop()
                    Log "  ERROR: Unity did not open in time." "Red"
                    Log "  Open Unity Hub manually, open project D:\results, then click Build again." "Yellow"
                    $btnBuild.Text = "  2. Build Game"; $btnBuild.Enabled = $true
                    $btnPlay.Enabled = $true
                }
            }
        }
        elseif ($script:buildPhase -eq "settle") {
            $script:buildTicks++
            if ($script:buildTicks -ge 8) {
                New-Item -Path $script:triggerFile -ItemType File -Force | Out-Null
                Log "  Build triggered. Building scene + EXE..." "Yellow"
                Log "  (2-5 min - watch Unity's progress bar)" "Gray"
                $script:buildPhase = "triggered"; $script:buildTicks = 0
            } else {
                Log "  Compiling... ($($script:buildTicks * 4) / 32 s)" "Gray"
            }
        }
        elseif ($script:buildPhase -eq "triggered") {
            if (Test-Path $envExe) {
                $script:buildTimer.Stop()
                Log ""; Log "  Build complete!  build\HallwayAgent.exe is ready." "Green"
                Log "  YOU ONLY NEED TO BUILD ONCE -- the EXE is reused every session." "Cyan"
                Log "  You can close Unity now." "Green"
                Log "  Click  [ 3. Play / Train ]  to watch the AI!" "Green"
                Refresh-Status
                $btnBuild.Text = "  2. Build Game"; $btnBuild.Enabled = $true
                $btnPlay.Enabled = $true
                [System.Windows.Forms.MessageBox]::Show(
                    "Build complete!`n`nClick  [ 3. Play / Train ]  to watch the AI play.",
                    "Done", [System.Windows.Forms.MessageBoxButtons]::OK,
                    [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
            } else {
                $script:buildTicks++
                if ($script:buildTicks % 5 -eq 0) {
                    Log "  Still building... ($($script:buildTicks * 4) s elapsed)" "Gray"
                }
                if ($script:buildTicks -gt 90) {
                    $script:buildTimer.Stop()
                    Log "  ERROR: Build timed out after 6 min. build\HallwayAgent.exe not found." "Red"
                    if (-not (Test-UnityLicense)) {
                        Log ""
                        Log "  LICENSING ISSUE DETECTED in Unity Editor.log:" "Red"
                        Log "  Unity 6000.4.2f1 failed to initialize its license." "Yellow"
                        Log "  This blocks BuildPipeline.BuildPlayer but NOT Play mode." "Yellow"
                        Log ""
                        Log "  FIX (choose one):" "Cyan"
                        Log "    Option A: In Unity menu -> Help -> Manage License" "White"
                        Log "              -> Activate Personal/Pro license, then retry." "White"
                        Log "    Option B: Click  [ 3. Play / Train ]  now." "White"
                        Log "              Training runs inside the Unity Editor (no build needed)." "White"
                        Log "    Option C: Go to unity.com/releases/editor/archive" "White"
                        Log "              Download 6000.4.2f1 installer directly (bypasses Hub)." "White"
                    }
                    $btnBuild.Text = "  2. Build Game"; $btnBuild.Enabled = $true
                    $btnPlay.Enabled = $true
                }
            }
        }
    })

    $existing = Get-Process -Name "Unity" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($existing) {
        Log "  Unity is open (PID $($existing.Id)). Waiting for compile to settle..." "Green"
        $script:buildPhase = "settle"; $script:buildTicks = 0
    } else {
        Log "  Opening Unity Editor (60-90 s to load)..." "Yellow"
        Start-Process $unityExe -ArgumentList "-projectPath `"$root`""
    }

    $script:buildTimer.Start()
})

# ====================================================================
# 3. PLAY / TRAIN
# ====================================================================
$btnPlay.Add_Click({
    if (-not (Test-Path $mlExe)) {
        Log ""; Log "  mlagents not installed. Click  [ 1. Setup ]  first." "Red"; return
    }

    Log ""; Log "[ PLAY / TRAIN ]  Launching AI game session..." "Cyan"

    # ── Always use Unity Editor mode ─────────────────────────────────
    # Editor mode:  no rebuild ever needed, always uses the latest scene,
    # the camera follows the agent, and the score HUD is visible in the
    # Unity Game view window.
    # The standalone EXE (Build Game) is only needed for distribution.

    if (-not (Test-Path "$root\train.ps1")) {
        Log "  ERROR: train.ps1 not found in project root." "Red"; return
    }

    Log "  Starting Editor-mode training..." "Yellow"
    Log "  Steps:  1. Open Unity  2. Build scene  3. Start mlagents  4. Press Play" "Gray"
    Log ""

    $script:mlProc = Start-Process powershell `
        -ArgumentList "-ExecutionPolicy Bypass -File `"$root\train.ps1`"" -PassThru

    Log "  Training launcher started (PID $($script:mlProc.Id))" "Green"
    Log "  Unity Game view will open with the 5-gate colour-memory corridor." "Green"
    Log "  The camera follows the blue agent automatically." "Green"
    Log "  Score HUD (top-left corner) tracks episode reward in real time." "Cyan"
    Log "  Scores also saved to:  $root\scores.csv" "Gray"
    Log ""
    Log "  The agent will start moving within 30 s of Unity entering Play mode." "Gray"
    Log "  Reward climbs toward +1.75 as the AI masters the task." "Gray"
    Log "  Click  [ Stop ]  to end the session." "Gray"
})

# ====================================================================
# TENSORBOARD
# ====================================================================
$btnBoard.Add_Click({
    $tb = "$root\mlenv\Scripts\tensorboard.exe"
    if (-not (Test-Path $tb)) { Log ""; Log "  Run Setup first." "Red"; return }
    Log ""; Log "[ TENSORBOARD ]  Opening http://localhost:6006 ..." "Cyan"
    Start-Process powershell -ArgumentList "-NoExit -Command `"& '$tb' --logdir '$root\results'`""
    Start-Sleep -Milliseconds 2000
    Start-Process "http://localhost:6006"
    Log "  Browser opened. Charts appear after the first ~1000 training steps." "Green"
})

# ====================================================================
# 4. DEPLOY  -- Copy ONNX, rebuild for inference, build standalone EXE
# ====================================================================
$btnDeploy.Add_Click({
    if (-not (Test-Path $unityExe)) {
        Log ""; Log "  Unity not found -- cannot build standalone EXE." "Red"
        Log "  Deploy requires Unity Editor to compile the EXE." "Yellow"
        return
    }

    $onnxSearch = Get-ChildItem "$root\results" -Recurse -Filter "HallwayAgent.onnx" `
                  -ErrorAction SilentlyContinue |
                  Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $onnxSearch) {
        Log ""; Log "[ DEPLOY ]  No trained model found." "Red"
        Log "  Train the agent first (Play/Train), then click Deploy." "Yellow"
        Log "  Training is sufficient when TensorBoard reward > 1.0." "Gray"
        return
    }

    Log ""; Log "[ DEPLOY ]  Building standalone inference EXE..." "Cyan"
    Log "  Model: $($onnxSearch.FullName)" "Gray"
    Log ""
    Log "  This will:" "White"
    Log "    1. Copy trained ONNX to Assets\Models\" "Gray"
    Log "    2. Rebuild scene (BehaviorType = InferenceOnly)" "Gray"
    Log "    3. Assign the model to BehaviorParameters" "Gray"
    Log "    4. Compile build\HallwayAgent.exe" "Gray"
    Log ""
    Log "  Unity Editor must be open. Watching for build completion..." "Yellow"

    $btnDeploy.Enabled = $false; $btnDeploy.Text = "  Deploying..."
    $btnPlay.Enabled   = $false

    $script:deployProc = Start-Process powershell `
        -ArgumentList "-ExecutionPolicy Bypass -File `"$root\deploy.ps1`"" `
        -PassThru

    # Poll for build\HallwayAgent.exe and .deploy_ready
    $script:deployTimer = New-Object System.Windows.Forms.Timer
    $script:deployTimer.Interval = 4000
    $script:deployTicks = 0

    $script:deployTimer.Add_Tick({
        $script:deployTicks++

        $ready = Test-Path "$root\.deploy_ready"
        $exe   = Test-Path "$root\build\HallwayAgent.exe"

        if ($ready -and $exe) {
            $script:deployTimer.Stop()
            $sz = [int]((Get-Item "$root\build\HallwayAgent.exe").Length / 1MB)
            Log ""
            Log "  DEPLOY COMPLETE!" "Green"
            Log "  build\HallwayAgent.exe (~$sz MB + Data folder)" "Cyan"
            Log ""
            Log "  Standalone mode -- no Unity, Python, or internet needed." "Green"
            Log "  To distribute: zip the build\ folder and share it." "Cyan"
            Log ""
            $btnDeploy.Text = "  4. Deploy EXE"; $btnDeploy.Enabled = $true
            $btnPlay.Enabled = $true
            Refresh-Status
            [System.Windows.Forms.MessageBox]::Show(
                "Deploy complete!`n`nbuild\HallwayAgent.exe is ready.`n`n" +
                "The AI runs fully standalone:`n" +
                "  - No Unity Editor needed`n" +
                "  - No Python needed`n`n" +
                "Zip the build\ folder to distribute.",
                "Deploy Complete",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
        } elseif ($script:deployTicks -gt 150) {
            $script:deployTimer.Stop()
            Log "  WARNING: Deploy timed out (10 min). Check Unity for errors." "Yellow"
            $btnDeploy.Text = "  4. Deploy EXE"; $btnDeploy.Enabled = $true
            $btnPlay.Enabled = $true
        } elseif ($script:deployTicks % 5 -eq 0) {
            Log "  Building... ($($script:deployTicks * 4) s)" "Gray"
        }
    })

    $script:deployTimer.Start()
})

# ====================================================================
# STOP
# ====================================================================
$btnStop.Add_Click({
    Log ""
    Log "[ STOP ]  Stopping all processes..." "Red"
    $stopped = $false

    # 1. Cancel any pending timers
    foreach ($t in @($script:playTimer, $script:watchTimer, $script:buildTimer, $script:deployTimer)) {
        if ($t -and $t.Enabled) { $t.Stop() }
    }
    $script:playTimer = $null

    # 2. Kill the train.ps1 launcher process
    if ($script:mlProc -and -not $script:mlProc.HasExited) {
        Stop-Process -Id $script:mlProc.Id -Force -ErrorAction SilentlyContinue
        $script:mlProc = $null
        $stopped = $true
        Log "  train.ps1 stopped." "Red"
    }

    # 3. Kill mlagents-learn
    $ml = Get-Process -Name "mlagents-learn" -ErrorAction SilentlyContinue
    if ($ml) {
        $ml | Stop-Process -Force -ErrorAction SilentlyContinue
        $stopped = $true
        Log "  mlagents-learn stopped." "Red"
    }

    # 4. Kill Python processes (mlagents trainer runs under python.exe)
    $py = Get-Process -Name "python" -ErrorAction SilentlyContinue
    if ($py) {
        $py | Stop-Process -Force -ErrorAction SilentlyContinue
        $stopped = $true
        Log "  Python trainer stopped." "Red"
    }

    # 5. Kill standalone game EXE if running
    $game = Get-Process -Name "HallwayAgent" -ErrorAction SilentlyContinue
    if ($game) {
        $game | Stop-Process -Force -ErrorAction SilentlyContinue
        $stopped = $true
        Log "  HallwayAgent.exe stopped." "Red"
    }

    # 6. Stop Unity Play mode by writing a stop-flag file that AutoPlayMonitor
    #    can detect, then send Ctrl+P ONLY if Unity is confirmed in the foreground.
    $playingFile = "$root\.unity_playing"
    if (Test-Path $playingFile) {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public class StopWin32 {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool  SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool  ShowWindow(IntPtr h, int n);
}
"@ -ErrorAction SilentlyContinue

        $unityProc = Get-Process -Name "Unity" -ErrorAction SilentlyContinue |
                     Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1

        if ($unityProc) {
            $h = $unityProc.MainWindowHandle
            [StopWin32]::ShowWindow($h, 9)       | Out-Null   # SW_RESTORE
            [StopWin32]::SetForegroundWindow($h) | Out-Null
            Start-Sleep -Milliseconds 800
            $fg = [StopWin32]::GetForegroundWindow()
            if ($fg -eq $h) {
                [System.Windows.Forms.SendKeys]::SendWait("^p")
                Log "  Unity Play mode stopped (Ctrl+P sent)." "Red"
                $stopped = $true
            } else {
                Log "  Could not focus Unity — press Ctrl+P in Unity manually." "Yellow"
            }
        }
    }

    if ($stopped) { Log "  All done. Session fully stopped." "Red" }
    else          { Log "  No active session found." "Yellow" }

    Refresh-Status
})

# ====================================================================
# STARTUP MESSAGE
# ====================================================================
Refresh-Status

Log "  HallwayAgent  --  AI Game Launcher" "Cyan"
Log "  ===================================" "Cyan"
Log ""
Log "  An AI agent learns to navigate a hallway using memory." "White"
Log "  It must remember the color of a block to reach the correct goal." "Gray"
Log ""

$ml       = Test-Path $mlExe
$exe      = Test-Path $envExe
$hasUnity = Test-Path $unityExe
$hasUrl   = -not ($releaseUrl -match "YOUR_USERNAME")

if (-not $ml -and -not $exe) {
    Log "  Getting started:" "Yellow"
    Log "    Step 1 ->  Click  [ 1. Setup ]       install Python + AI libraries" "White"
    if ($hasUrl -and -not $hasUnity) {
        Log "    Step 2 ->  Click  [ 2. Build Game ]  download pre-built game (no Unity needed!)" "Cyan"
    } elseif ($hasUnity) {
        Log "    Step 2 ->  Click  [ 2. Build Game ]  compile the game in Unity" "White"
    } else {
        Log "    Step 2 ->  Click  [ 2. Build Game ]  get the game build" "White"
    }
    Log "    Step 3 ->  Click  [ 3. Play / Train ] watch the AI learn!" "White"
} elseif (-not $ml) {
    Log "  Click  [ 1. Setup ]  to install Python + mlagents." "Yellow"
} elseif (-not $exe) {
    if ($hasUrl -and -not $hasUnity) {
        Log "  mlagents ready. Click  [ 2. Build Game ]  to download the pre-built game." "Cyan"
    } else {
        Log "  mlagents ready. Click  [ 2. Build Game ]  to compile / get the game." "Yellow"
    }
} else {
    Log "  All ready!  Click  [ 3. Play / Train ]  to watch the AI!" "Green"
    Log "  The game EXE is ready -- no need to build again." "Gray"
}

if (-not $hasUnity) {
    Log ""
    Log "  Unity not detected on this machine." "Gray"
    if ($hasUrl) {
        Log "  [ 2. Build Game ] will download the game automatically." "Cyan"
    } else {
        Log "  To run without Unity: developer must upload a build to GitHub Releases." "Gray"
        Log "  Then set `$releaseUrl in launcher.ps1." "Gray"
    }
}
Log ""

$form.ShowDialog() | Out-Null
