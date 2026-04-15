using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.MLAgents.Policies;

/// <summary>
/// InferenceDeployBuilder — One-click deployment to standalone EXE.
///
/// What it does:
///   1. Loads the trained ONNX from Assets/Models/HallwayAgent.onnx
///   2. Rebuilds the 7-gate scene (geometry identical to training)
///   3. Switches BehaviorParameters to InferenceOnly + assigns the model
///   4. Saves the scene
///   5. Calls HallwayBuilder to compile a Windows standalone EXE
///
/// The resulting EXE runs the trained AI agent with ZERO external dependencies:
///   - No Unity Editor
///   - No Python or mlagents-learn
///   - No internet connection
///   - Just double-click HallwayAgent.exe
///
/// Prerequisite:
///   Train the agent (Play/Train) until rewards are consistently above 1.0,
///   then run deploy.ps1 (which copies the ONNX and triggers this builder)
///   OR manually copy:
///     results\<run-id>\HallwayAgent.onnx  →  Assets\Models\HallwayAgent.onnx
///   and click  HallwayAgent → Build Inference Executable (Deploy).
/// </summary>
public static class InferenceDeployBuilder
{
    /// <summary>Folder where deploy.ps1 copies the trained ONNX for Unity to import.</summary>
    public const string ModelFolder    = "Assets/Models";
    // Kept for backward compatibility (deploy.ps1 may copy "HallwayAgent.onnx").
    public const string ModelAssetPath = "Assets/Models/HallwayAgent.onnx";

    // ================================================================
    // MENU ENTRY — manual trigger
    // ================================================================

    [MenuItem("HallwayAgent/Build Inference Executable (Deploy)")]
    public static void BuildAndDeploy()
    {
        // Refresh asset database so any recently copied ONNX is imported
        AssetDatabase.Refresh();

        UnityEngine.Object model = FindModelAsset();

        if (model == null)
        {
            string msg =
                $"No trained model found in {ModelFolder}\n\n" +
                "Steps to fix:\n" +
                "  1. Train the agent (launcher → Play/Train)\n" +
                "  2. Wait until episode reward > 1.0\n" +
                "  3. Click Stop, then run deploy.ps1\n" +
                "     (it copies the ONNX automatically)\n\n" +
                "Or copy manually:\n" +
                "  results\\<run-id>\\<BehaviorName>.onnx\n" +
                $"  → {ModelFolder}\\<BehaviorName>.onnx";

            Debug.LogError("[InferenceDeployBuilder] " + msg);
            EditorUtility.DisplayDialog("Model Not Found", msg, "OK");
            return;
        }

        Debug.Log($"[InferenceDeployBuilder] Model found: {model.name}  ({model.GetType().Name})");

        BuildInferenceScene(model);
        HallwayBuilder.Build();
    }

    // ================================================================
    // MODEL DISCOVERY
    // Searches Assets/Models/ for any imported ONNX/model asset.
    // The file may be named "HallwayAgent.onnx", "My Behavior.onnx", etc.
    // depending on what behavior name Unity reported to mlagents during training.
    // ================================================================

    public static UnityEngine.Object FindModelAsset()
    {
        if (!AssetDatabase.IsValidFolder(ModelFolder))
            return null;

        // Find all asset GUIDs in Assets/Models
        string[] guids = AssetDatabase.FindAssets("", new[] { ModelFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".onnx", System.StringComparison.OrdinalIgnoreCase)) continue;

            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj != null)
            {
                Debug.Log($"[InferenceDeployBuilder] Found model asset: {path}");
                return obj;
            }
        }
        return null;
    }

    // ================================================================
    // INFERENCE SCENE SETUP
    // Called by BuildAndDeploy() above AND by AutoBuilder on .autodeploy
    // ================================================================

    /// <summary>
    /// Rebuilds the 7-gate scene with BehaviorType = InferenceOnly and
    /// the supplied trained model assigned to BehaviorParameters.
    /// </summary>
    public static void BuildInferenceScene(UnityEngine.Object model)
    {
        EditorUtility.DisplayProgressBar("Deploy", "Building inference scene...", 0.25f);
        try
        {
            // ── Step 1: Build scene geometry (same as training run) ──────
            HallwaySceneBuilder.BuildScene();

            // ── Step 2: Find BehaviorParameters created by BuildScene ────
            BehaviorParameters bp = Object.FindObjectOfType<BehaviorParameters>();
            if (bp == null)
            {
                Debug.LogError(
                    "[InferenceDeployBuilder] BehaviorParameters not found in scene after " +
                    "HallwaySceneBuilder.BuildScene(). Cannot configure inference mode.");
                return;
            }

            // ── Step 3: Assign model + switch to InferenceOnly ───────────
            // Use SerializedObject so changes definitely persist through
            // Unity's serialization pipeline (direct assignment can silently revert).
            SerializedObject bpSO = new SerializedObject(bp);

            // Assign the trained ONNX model
            SerializedProperty modelProp = bpSO.FindProperty("m_Model");
            if (modelProp != null)
            {
                modelProp.objectReferenceValue = model;
            }
            else
            {
                // Field name may differ by ML-Agents version; try alternate names
                modelProp = bpSO.FindProperty("Model");
                if (modelProp != null) modelProp.objectReferenceValue = model;
                else Debug.LogWarning("[InferenceDeployBuilder] Could not find 'm_Model' field " +
                                      "on BehaviorParameters — model assignment may not persist.");
            }

            // BehaviorType.InferenceOnly = 2
            SerializedProperty typeProp = bpSO.FindProperty("m_BehaviorType");
            if (typeProp != null) typeProp.intValue = (int)BehaviorType.InferenceOnly;

            // InferenceDevice.CPU = 0  (broadest hardware compatibility for distribution)
            SerializedProperty deviceProp = bpSO.FindProperty("m_InferenceDevice");
            if (deviceProp != null) deviceProp.intValue = 0; // CPU

            bpSO.ApplyModifiedProperties();

            // Belt-and-suspenders: direct assignment + SetDirty
            bp.BehaviorType = BehaviorType.InferenceOnly;
            EditorUtility.SetDirty(bp);

            // ── Step 4: Save scene ───────────────────────────────────────
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, scene.path);

            Debug.Log($"[InferenceDeployBuilder] Inference scene saved: " +
                      $"model={model.name}  BehaviorType=InferenceOnly  Device=CPU");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
