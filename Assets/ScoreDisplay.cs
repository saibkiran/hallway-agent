using UnityEngine;
using System.IO;
using System.Diagnostics;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ScoreDisplay — On-screen HUD and persistent score logger.
///
/// Attach to an empty "ScoreDisplay" GameObject in the scene
/// (done automatically by HallwaySceneBuilder).
///
/// HallwayAgent calls RecordEpisode() at the end of every episode.
/// The HUD is drawn with Unity's IMGUI (no Canvas required) so it
/// works in both the Editor Game view and standalone builds.
///
/// Scores are appended to scores.csv in the project root after each
/// episode so training progress can be reviewed externally.
/// </summary>
public class ScoreDisplay : MonoBehaviour
{
    // ── Runtime stats ────────────────────────────────────────────────
    private int   episodeCount   = 0;
    private float currentReward  = 0f;
    private float bestReward     = float.MinValue;
    private int   gatesPassed    = 0;
    private string statusMsg     = "WAITING FOR AGENT...";
    private Color  statusColor   = Color.yellow;
    private int   winCount       = 0;
    private int   lossCount      = 0;

    // ── CSV path ─────────────────────────────────────────────────────
    private string csvPath;

    // ── Cached GUI styles (created once in OnGUI) ────────────────────
    private GUIStyle panelStyle;
    private GUIStyle titleStyle;
    private GUIStyle rowStyle;
    private GUIStyle statusStyle;
    private GUIStyle winStyle;
    private GUIStyle lossStyle;
    private GUIStyle exitStyle;
    private Texture2D bgTex;
    private Texture2D exitBgTex;

    // ================================================================
    // UNITY LIFECYCLE
    // ================================================================

    private void Start()
    {
        // Write CSV next to the project root so it is easy to find
        string projectRoot = Application.dataPath.Replace("/Assets", "")
                                                  .Replace("\\Assets", "");
        csvPath = Path.Combine(projectRoot, "scores.csv");

        if (!File.Exists(csvPath))
            File.WriteAllText(csvPath, "Episode,Reward,GatesPassed,Status,Wins,Losses\n");
    }

    // ================================================================
    // PUBLIC API  (called by HallwayAgent)
    // ================================================================

    /// <summary>
    /// Call this just BEFORE EndEpisode() in HallwayAgent.
    /// </summary>
    public void RecordEpisode(float reward, int gates, string status)
    {
        episodeCount++;
        currentReward = reward;
        gatesPassed   = gates;
        statusMsg     = status;

        if (reward > bestReward) bestReward = reward;

        // Track wins/losses only at episode end (not mid-episode WRONG GATE events)
        if (status == "GOAL REACHED!")
        {
            if (gates == 5) winCount++;
            else            lossCount++;
        }
        else if (status == "WRONG GOAL")
        {
            lossCount++;
        }

        // Colour-code the status
        statusColor = status.Contains("GOAL REACHED") ? Color.green
                    : status.Contains("WRONG")        ? Color.red
                    : Color.yellow;

        // Append to CSV
        try
        {
            File.AppendAllText(csvPath,
                $"{episodeCount},{reward:F4},{gates},{status},{winCount},{lossCount}\n");
        }
        catch { /* ignore write errors during rapid episodes */ }
    }

    // ================================================================
    // IMGUI OVERLAY  (drawn every frame on top of the game view)
    // ================================================================

    private void OnGUI()
    {
        EnsureStyles();

        float w = 240f, h = 246f;
        float x = 12f, y = 12f;

        // Background panel
        GUI.DrawTexture(new Rect(x, y, w, h), bgTex);

        float lx = x + 12f;
        float ly = y + 10f;
        float lh = 26f;

        GUI.Label(new Rect(lx, ly,       w - 24f, 28f), "HALLWAY AGENT  |  SCORE", titleStyle);
        ly += 30f;

        GUI.Label(new Rect(lx, ly,       w - 24f, lh),
            $"Episode :  {episodeCount}", rowStyle);
        ly += lh;

        GUI.Label(new Rect(lx, ly,       w - 24f, lh),
            $"Reward  :  {currentReward:+0.000;-0.000;0.000}", rowStyle);
        ly += lh;

        GUI.Label(new Rect(lx, ly,       w - 24f, lh),
            $"Best    :  {(bestReward == float.MinValue ? "---" : bestReward.ToString("+0.000;-0.000"))}", rowStyle);
        ly += lh;

        GUI.Label(new Rect(lx, ly,       w - 24f, lh),
            $"Gates   :  {gatesPassed} / 5", rowStyle);
        ly += lh;

        // Win / Loss counts
        winStyle.normal.textColor  = Color.green;
        lossStyle.normal.textColor = Color.red;
        GUI.Label(new Rect(lx,          ly, (w - 24f) * 0.5f, lh), $"Win  :  {winCount}",  winStyle);
        GUI.Label(new Rect(lx + (w - 24f) * 0.5f, ly, (w - 24f) * 0.5f, lh), $"Loss :  {lossCount}", lossStyle);
        ly += lh;

        // Status line with colour
        statusStyle.normal.textColor = statusColor;
        GUI.Label(new Rect(lx, ly, w - 24f, lh), statusMsg, statusStyle);
        ly += lh + 4f;

        // Exit button — stops mlagents-learn and exits Play mode
        GUI.backgroundColor = new Color(0.8f, 0.1f, 0.1f, 1f);
        if (GUI.Button(new Rect(lx, ly, w - 24f, 28f), "STOP  &  EXIT", exitStyle))
            DoExit();
        GUI.backgroundColor = Color.white;
    }

    // ================================================================
    // EXIT LOGIC
    // ================================================================

    private void DoExit()
    {
        // Kill any mlagents-learn process still running
        try
        {
            foreach (Process p in Process.GetProcessesByName("mlagents-learn"))
            {
                p.Kill();
                p.WaitForExit(2000);
            }
            // Also kill the Python process that mlagents-learn spawns
            foreach (Process p in Process.GetProcessesByName("python"))
            {
                // Only kill if its command line contains "mlagents"
                p.Kill();
                p.WaitForExit(1000);
            }
        }
        catch { /* ignore — process may have already stopped */ }

#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ================================================================
    // STYLE HELPERS
    // ================================================================

    private void EnsureStyles()
    {
        if (bgTex != null) return; // already built

        // Semi-transparent dark background
        bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
        bgTex.Apply();

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 13,
            fontStyle = FontStyle.Bold
        };
        titleStyle.normal.textColor = new Color(0.4f, 0.9f, 1f);

        rowStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Normal
        };
        rowStyle.normal.textColor = Color.white;

        statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold
        };
        statusStyle.normal.textColor = Color.yellow;

        winStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold
        };
        winStyle.normal.textColor = Color.green;

        lossStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold
        };
        lossStyle.normal.textColor = Color.red;

        exitStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        exitStyle.normal.textColor  = Color.white;
        exitStyle.hover.textColor   = Color.white;
        exitStyle.active.textColor  = Color.white;
    }
}
