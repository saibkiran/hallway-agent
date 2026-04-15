using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// AutoBuilder -- automated build/scene trigger system.
///
/// Three trigger files (dropped by launcher.ps1 / deploy.ps1 in the project root):
///
///   .autoscene   -- scene-only setup (no Unity license required)
///                   Calls HallwaySceneBuilder.BuildScene() then writes .scene_ready.
///                   Used by the "Play / Train" button for Editor-mode training.
///
///   .autobuild   -- full standalone EXE build (requires Unity license)
///                   Calls BuildScene() then HallwayBuilder.Build().
///                   Used by the "Build Game" button.
///
///   .autodeploy  -- inference EXE build for standalone distribution (requires Unity license)
///                   Loads Assets/Models/HallwayAgent.onnx, sets BehaviorType=InferenceOnly,
///                   rebuilds scene, builds EXE, then writes .deploy_ready.
///                   Used by the "Deploy" button / deploy.ps1.
/// </summary>
[InitializeOnLoad]
public static class AutoBuilder
{
    static readonly string ProjectRoot =
        Path.GetDirectoryName(Application.dataPath);

    static readonly string BuildTrigger  = Path.Combine(ProjectRoot, ".autobuild");
    static readonly string SceneTrigger  = Path.Combine(ProjectRoot, ".autoscene");
    static readonly string SceneReady    = Path.Combine(ProjectRoot, ".scene_ready");
    static readonly string DeployTrigger = Path.Combine(ProjectRoot, ".autodeploy");
    static readonly string DeployReady   = Path.Combine(ProjectRoot, ".deploy_ready");

    static AutoBuilder()
    {
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.isPlaying)   return;
        if (EditorApplication.isCompiling) return;

        // ── Scene-only trigger (no license needed) ──────────────────
        if (File.Exists(SceneTrigger))
        {
            File.Delete(SceneTrigger);
            // Remove stale ready-marker from any previous run
            File.Delete(SceneReady);

            Debug.Log("[AutoBuilder] .autoscene detected -- building scene for Editor training...");
            EditorUtility.DisplayProgressBar("AutoBuilder", "Setting up HallwayAgent scene...", 0.3f);
            try
            {
                HallwaySceneBuilder.BuildScene();
                // Signal to launcher that scene is ready
                File.WriteAllText(SceneReady, "ready");
                Debug.Log("[AutoBuilder] Scene ready. Launcher can now start mlagents + Play.");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[AutoBuilder] Scene setup failed: " + e.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return;
        }

        // ── Inference deploy trigger (license required) ─────────────
        if (File.Exists(DeployTrigger))
        {
            File.Delete(DeployTrigger);
            File.Delete(DeployReady);

            Debug.Log("[AutoBuilder] .autodeploy detected -- building inference executable...");
            EditorUtility.DisplayProgressBar("AutoBuilder", "Building inference EXE...", 0.1f);
            try
            {
                // Ensure the ONNX is imported before trying to load it
                AssetDatabase.Refresh();

                UnityEngine.Object model = InferenceDeployBuilder.FindModelAsset();

                if (model == null)
                {
                    Debug.LogError(
                        $"[AutoBuilder] No ONNX model found in '{InferenceDeployBuilder.ModelFolder}'. " +
                        "Run deploy.ps1 (which copies the ONNX) then try again.");
                }
                else
                {
                    InferenceDeployBuilder.BuildInferenceScene(model);
                    HallwayBuilder.Build();
                    File.WriteAllText(DeployReady, "ready");
                    Debug.Log("[AutoBuilder] Inference EXE built successfully. " +
                              "build\\HallwayAgent.exe is ready for standalone distribution.");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[AutoBuilder] Deploy build failed: " + e.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return;
        }

        // ── Full standalone build trigger (license required) ────────
        if (!File.Exists(BuildTrigger)) return;

        File.Delete(BuildTrigger);

        Debug.Log("[AutoBuilder] .autobuild detected -- building scene + standalone EXE...");
        EditorUtility.DisplayProgressBar("AutoBuilder", "Building scene...", 0.1f);

        try
        {
            EditorUtility.DisplayProgressBar("AutoBuilder", "Building scene...", 0.2f);
            HallwaySceneBuilder.BuildScene();

            EditorUtility.DisplayProgressBar("AutoBuilder", "Compiling standalone EXE...", 0.5f);
            HallwayBuilder.Build();

            Debug.Log("[AutoBuilder] Build complete -- EXE is in the build\\ folder.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[AutoBuilder] Build failed: " + e.Message);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
