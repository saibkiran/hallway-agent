# HallwayAgent — How to Use

An AI agent learns to navigate a corridor by remembering the colour of a block at the start, then choosing the matching gate side 5 times in a row.

---

## Quick Start (3 Steps)

### Step 1 — Setup
Double-click **`LAUNCH.bat`** to open the launcher, then click **`1. Setup`**.

This installs:
- Python 3.10 (downloads automatically if missing)
- `mlagents 1.1.0` and all required packages into `mlenv\`

Wait for the log to say **"Setup complete!"** before continuing.

---

### Step 2 — Build Game
Click **`2. Build Game`** in the launcher.

- **Unity is installed** → automatically compiles `build\HallwayAgent.exe` (2–5 min)
- **Unity not installed** → downloads the pre-built EXE from GitHub Releases

> You only need to build once. The EXE is reused every session.

---

### Step 3 — Train / Watch
Click **`3. Play / Train`** in the launcher.

What happens automatically:
1. Unity Editor opens and builds the 5-gate scene
2. `mlagents-learn` starts on port 5004
3. Unity enters Play mode and connects to the trainer
4. The blue agent starts moving within 30 seconds

You can open other apps freely — the launcher no longer sends keyboard shortcuts to Unity.

---

## The Game

```
[BACK WALL]
    │
[COLOUR BLOCK]  ← agent sees this (green or red) at episode start
    │
[GATE 1]  ← must pass the side matching the colour block
[GATE 2]
[GATE 3]
[GATE 4]
[GATE 5]
    │
[FINISH LINE]
```

Each episode:
1. The colour block is randomly set to **green** or **red**
2. The agent walks forward, remembering the colour (LSTM memory)
3. At each gate the agent must choose the **matching colour side**
4. Reaching the finish line ends the episode

---

## HUD (top-left of Game view)

| Field | Meaning |
|-------|---------|
| Episode | Total episodes completed so far |
| Reward | Cumulative reward this episode |
| Best | Best reward ever achieved |
| Gates | Correct gates passed this episode (out of 5) |
| Win | Episodes finished with all 5 gates correct |
| Loss | Episodes finished with at least 1 wrong gate |
| Status | Last event — GOAL REACHED / WRONG GATE / WRONG GOAL |

### STOP & EXIT button
Click the red **`STOP & EXIT`** button in the HUD to:
- Kill the `mlagents-learn` process
- Exit Unity Play mode

---

## Reward Structure

| Event | Reward |
|-------|--------|
| Correct gate | +0.10 |
| Wrong gate | −0.30 (episode continues) |
| Finish line reached | +1.00 base + 0.10 × correct gates |
| Each step (penalty) | −0.0003 |
| **Perfect run (all 5 correct)** | **+1.50 max** |

The agent starts near **−0.30** (random) and climbs toward **+1.50** as it learns.

---

## Watching Training Progress

Open TensorBoard by clicking **`TensorBoard`** in the launcher, then go to:
```
http://localhost:6006
```

Key chart to watch: **Cumulative Reward**
- Early training (~0–200k steps): reward is near 0 or negative
- Mid training (~200k–500k steps): reward starts climbing
- Late training (~500k–1M steps): reward stabilises above +1.0
- Trained (~1M–2M steps): reward consistently near +1.50

Scores are also saved to **`scores.csv`** in the project root after each episode.

---

## Stopping Training

**Option A — HUD button:** Click `STOP & EXIT` in the Game view (kills mlagents + exits Play mode)

**Option B — Launcher:** Click `Stop` in the launcher window

**Option C — Manual:** Press `Ctrl+P` in the Unity Editor to exit Play mode, then close the mlagents terminal window

---

## After Training — Deploy a Standalone EXE

Once TensorBoard shows reward consistently above **+1.0**:

1. Click **`4. Deploy EXE`** in the launcher
2. The launcher will:
   - Find the trained model (`results\...\HallwayAgent.onnx`)
   - Rebuild the scene in inference mode (no Python trainer needed)
   - Compile `build\HallwayAgent.exe`
3. Zip the `build\` folder and share it — no Unity or Python required to run it

---

## File Structure

```
D:\results\
├── LAUNCH.bat              ← double-click to open launcher
├── launcher.ps1            ← launcher UI (PowerShell)
├── train.ps1               ← starts mlagents + Unity Play mode
├── hallway_config.yaml     ← PPO training config (2M steps, LSTM)
├── scores.csv              ← per-episode scores (written during training)
├── mlenv\                  ← Python virtual environment (created by Setup)
├── results\                ← mlagents checkpoints + final .onnx model
│   └── hallway_run1\
│       └── HallwayAgent.onnx
├── build\                  ← standalone EXE (created by Build Game)
│   └── HallwayAgent.exe
└── Assets\
    ├── HallwayAgent.cs     ← RL agent (observations, actions, rewards)
    ├── HallwayManager.cs   ← episode reset coordinator
    ├── ScoreDisplay.cs     ← HUD overlay (episode, reward, win/loss)
    ├── BlockRandomizer.cs  ← randomises colour block each episode
    ├── GateTrigger.cs      ← detects which gate side the agent chose
    └── Editor\
        ├── HallwaySceneBuilder.cs  ← builds the 5-gate scene
        ├── AutoPlayMonitor.cs      ← auto-enters Play when mlagents starts
        └── AutoBuilder.cs          ← auto-builds EXE on trigger file
```

---

## Troubleshooting

**Agent is moving randomly and not improving**
→ `mlagents-learn` is not connected. Click `Stop` then `3. Play / Train` again.
→ Check the mlagents terminal window for errors.

**"mlagents missing" shown in launcher status bar**
→ Click `1. Setup` and wait for it to complete.

**Unity times out (mlagents crashes after 60 s)**
→ `AutoPlayMonitor` handles Play mode automatically — do not press Ctrl+P manually while the launcher is running as it may interfere.

**Chrome / another app opened a Print dialog**
→ This was a known bug (fixed). Update to the latest `train.ps1` — the launcher no longer sends `Ctrl+P` globally.

**Build timed out**
→ Check Unity for a licensing error: `Help → Manage License → Activate`.
→ Training in Editor mode (`3. Play / Train`) works without a build.

**TensorBoard shows no data**
→ Training must run for at least ~1000 steps before charts appear.
→ Ensure `results\` folder exists: it is created after the first training session.
