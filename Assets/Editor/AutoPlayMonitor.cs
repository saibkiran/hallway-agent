using UnityEditor;
using UnityEngine;
using System.Net.Sockets;

/// <summary>
/// AutoPlayMonitor — Automatically enters Play mode and focuses the Game view
/// when mlagents-learn is detected on port 5004.
///
/// PROBLEM THIS SOLVES:
///   mlagents-learn (Editor mode) waits 60 seconds for Unity to enter Play mode.
///   If the user runs `mlagents-learn` from a terminal without pressing Ctrl+P,
///   the 60-second window expires and training crashes with UnityTimeOutException.
///
///   Additionally, Unity defaults to showing the Scene view on Play. The user
///   needs to see the Game view (where the agent is visible) not the editor.
///
/// HOW IT WORKS:
///   Every 2 seconds this script checks whether port 5004 is accepting connections.
///   When mlagents-learn is detected it:
///     1. Rebuilds the 5-gate scene
///     2. Opens and maximises the Game view window
///     3. Enters Play mode → mlagents connects and training begins
///
///   On PlayModeStateChange.EnteredPlayMode it re-focuses the Game view because
///   Unity sometimes switches focus back to the Scene view during the Play transition.
/// </summary>
[InitializeOnLoad]
public static class AutoPlayMonitor
{
    static readonly System.Diagnostics.Stopwatch _clock =
        System.Diagnostics.Stopwatch.StartNew();

    static bool _triggered;

    // Cached type handle — resolves once, reused every frame
    static readonly System.Type GameViewType =
        System.Type.GetType("UnityEditor.GameView,UnityEditor");

    static AutoPlayMonitor()
    {
        EditorApplication.update               += Poll;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void EnterPlayMode()
    {
        // Called via delayCall — one editor frame after BuildScene() so the
        // scene save is guaranteed to be flushed to disk.
        FocusGameView();
        EditorApplication.isPlaying = true;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            case PlayModeStateChange.EnteredPlayMode:
                // Unity sometimes steals focus back to Scene view during the
                // Play-mode domain reload. Re-focus the Game view here.
                FocusGameView();
                break;

            case PlayModeStateChange.ExitingPlayMode:
                // Allow the NEXT mlagents session to also auto-trigger Play
                _triggered = false;
                break;
        }
    }

    static void Poll()
    {
        if (EditorApplication.isPlaying)   return; // already training
        if (EditorApplication.isCompiling) return; // wait for compile
        if (_triggered)                    return; // already fired this session

        // Check every 2 seconds — avoid hammering the TCP stack
        if (_clock.Elapsed.TotalSeconds < 2.0) return;
        _clock.Restart();

        if (!IsMlAgentsListening()) return;

        _triggered = true;
        Debug.Log("[AutoPlayMonitor] mlagents detected on port 5004 — " +
                  "rebuilding scene, opening Game view, entering Play mode.");

        // Step 1: Rebuild scene
        bool buildOk = true;
        try { HallwaySceneBuilder.BuildScene(); }
        catch (System.Exception e)
        {
            buildOk = false;
            Debug.LogError("[AutoPlayMonitor] Scene rebuild FAILED: " + e.Message +
                           "\n" + e.StackTrace);
        }

        if (!buildOk)
        {
            // Don't enter Play with a broken scene — let the user fix it manually
            _triggered = false;
            return;
        }

        // Step 2: Open the Game view so the user sees the agent, not the editor grid
        FocusGameView();

        // Step 3: Defer Play mode entry by one editor frame so EditorSceneManager.SaveScene()
        // fully flushes serialized references to disk before Unity reloads for Play mode.
        // Without this, SerializedObject reference assignments from BuildScene() can
        // arrive too late and load as null, crashing OnEpisodeBegin().
        EditorApplication.delayCall += EnterPlayMode;
    }

    /// <summary>
    /// Opens the Game view window, brings it to the front, and maximises it
    /// so the agent gameplay fills the screen rather than a small docked tab.
    /// </summary>
    static void FocusGameView()
    {
        if (GameViewType == null)
        {
            // Fallback: open via menu (always available)
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            return;
        }

        // GetWindow opens the tab if it doesn't exist; brings it to front if it does.
        EditorWindow gameView = EditorWindow.GetWindow(GameViewType,
                                                       false,          // not utility window
                                                       "Game",         // title
                                                       true);          // focus it
        if (gameView != null)
        {
            gameView.Show();
            gameView.Focus();
        }
    }

    /// <summary>
    /// Returns true when localhost:5004 is accepting connections (mlagents-learn running).
    /// Uses a 50 ms non-blocking check so the Editor never stutters.
    /// </summary>
    static bool IsMlAgentsListening()
    {
        try
        {
            using (var tcp = new TcpClient())
            {
                var ar = tcp.BeginConnect("localhost", 5004, null, null);
                if (ar.AsyncWaitHandle.WaitOne(50) && tcp.Connected)
                {
                    tcp.EndConnect(ar);
                    return true;
                }
                return false;
            }
        }
        catch
        {
            return false;
        }
    }
}
