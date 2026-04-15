using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// HallwayBuilder — builds a standalone Windows executable.
/// Menu: HallwayAgent → Build Standalone EXE
///
/// For inference-only deployment (no Python needed), use:
///   HallwayAgent → Build Inference Executable (Deploy)
/// which first switches the scene to InferenceOnly + assigns the ONNX model,
/// then calls this Build() method.
/// </summary>
public static class HallwayBuilder
{
    const string BuildPath = @"build\HallwayAgent.exe";

    // Primary scene — built by HallwaySceneBuilder.BuildScene()
    // Falls back to SampleScene if HallwayScene has not been built yet.
    static string ScenePath
    {
        get
        {
            string hall = "Assets/Scenes/HallwayScene.unity";
            if (System.IO.File.Exists(System.IO.Path.Combine(
                    UnityEngine.Application.dataPath, "../", hall)))
                return hall;
            return "Assets/Scenes/SampleScene.unity";
        }
    }

    [MenuItem("HallwayAgent/Build Standalone EXE")]
    public static void Build()
    {
        string scene = ScenePath;
        Debug.Log($"[HallwayBuilder] Building from scene: {scene}");

        BuildPlayerOptions opts = new BuildPlayerOptions
        {
            scenes           = new[] { scene },
            locationPathName = BuildPath,
            target           = BuildTarget.StandaloneWindows64,
            options          = BuildOptions.None
        };

        BuildReport  report  = BuildPipeline.BuildPlayer(opts);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
            Debug.Log($"[HallwayBuilder] Build succeeded → {BuildPath}  " +
                      $"({summary.totalSize / 1024 / 1024} MB)");
        else
            Debug.LogError($"[HallwayBuilder] Build FAILED: {summary.result}");
    }
}
