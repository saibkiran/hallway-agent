using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// PlayStateMonitor — Writes / deletes a marker file so train.ps1 can
/// detect whether Unity is currently in Play mode before sending Ctrl+P.
///
/// Problem it solves:
///   If Unity is already in Play mode when train.ps1 sends Ctrl+P,
///   the keystroke STOPS Play mode instead of starting it.
///   mlagents-learn then times out waiting for a connection that never comes.
///
/// Solution:
///   This script writes  .unity_playing  the moment Unity enters Play mode
///   and deletes it the moment Unity exits Play mode.
///   train.ps1 reads that file and stops Play mode first if necessary,
///   so Ctrl+P is always sent to a STOPPED Unity Editor.
/// </summary>
[InitializeOnLoad]
public static class PlayStateMonitor
{
    static readonly string PlayingFile =
        Path.Combine(Path.GetDirectoryName(Application.dataPath), ".unity_playing");

    static PlayStateMonitor()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;

        // Clean up stale marker from a previous crashed session
        if (!EditorApplication.isPlaying && File.Exists(PlayingFile))
            File.Delete(PlayingFile);
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            File.WriteAllText(PlayingFile, "playing");
        }
        else if (state == PlayModeStateChange.ExitingPlayMode ||
                 state == PlayModeStateChange.EnteredEditMode)
        {
            if (File.Exists(PlayingFile))
                File.Delete(PlayingFile);
        }
    }
}
