using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;

/// <summary>
/// HallwaySceneBuilder — Builds the 5-gate colour-memory corridor.
///
/// Run via: Unity menu bar -> HallwayAgent -> Build Complete Scene
/// (AutoPlayMonitor.cs also triggers a rebuild automatically when mlagents starts.)
///
/// Layout (all Z positions in world/local space):
///   z = -26   Back wall
///   z = -23   Agent spawn
///   z = -20   Colour indicator block (trigger — agent walks through it)
///   z = -10 .. +10  5 gates, spaced 5 units apart
///   z = +18   Finish trigger
///   z = +22   Front wall
///
/// Each gate = two colour panels (triggers) + solid centre divider + scoring plane:
///   Left panel:     x=-2.5, scale_x=4.6 → covers x=-4.8 to -0.2 (trigger, passable)
///   Centre divider: x= 0.0, scale_x=0.4 → solid wall; agent MUST commit to a side
///   Right panel:    x=+2.5, scale_x=4.6 → covers x=+0.2 to +4.8 (trigger, passable)
///   Panels are tagged GreenBlock / RedBlock so raycasts detect colour.
///   Invisible GateTrigger plane at z = gateZ + 0.55 fires on crossing and checks
///   agent.x < 0 to determine which colour side was chosen.
///
/// Reward structure (defined in HallwayAgent):
///   Correct gate side:  +0.05 per gate
///   Wrong gate side:    -0.50, EndEpisode
///   Finish line:        +1.00, EndEpisode
///   Per-step penalty:   -0.0003
/// </summary>
public static class HallwaySceneBuilder
{
    // Tags the raycasts, GateTrigger, and FinishTrigger depend on
    static readonly string[] RequiredTags =
        { "Wall", "GreenBlock", "RedBlock", "GreenGoal", "RedGoal" };

    // Z positions for the 5 gates (spacing = 5 units)
    static readonly float[] GateZ =
    {
        -10f, -5f, 0f, 5f, 10f
    };

    [MenuItem("HallwayAgent/Build Complete Scene")]
    public static void BuildScene()
    {
        // ── 1. Tags ──────────────────────────────────────────────────
        AddRequiredTags();

        // ── 2. Materials ─────────────────────────────────────────────
        Material greenMat  = LoadOrCreateMaterial("GreenMat",  new Color(0.18f, 0.75f, 0.18f));
        Material redMat    = LoadOrCreateMaterial("RedMat",    new Color(0.85f, 0.15f, 0.15f));
        Material wallMat   = LoadOrCreateMaterial("WallMat",   new Color(0.55f, 0.55f, 0.55f));
        Material floorMat  = LoadOrCreateMaterial("FloorMat",  new Color(0.20f, 0.20f, 0.20f));
        Material agentMat  = LoadOrCreateMaterial("AgentMat",  new Color(0.20f, 0.55f, 0.90f));
        Material finishMat = LoadOrCreateMaterial("FinishMat", new Color(1.00f, 0.85f, 0.10f));

        // ── 3. Environment root (remove stale build if re-running) ───
        GameObject old = GameObject.Find("HallwayEnvironment");
        if (old != null) Object.DestroyImmediate(old);

        GameObject env = new GameObject("HallwayEnvironment");

        // ── 4. Arena geometry ─────────────────────────────────────────
        BuildFloor(env, floorMat);
        BuildWalls(env, wallMat);

        // ── 5. Colour indicator block ─────────────────────────────────
        GameObject colorBlock = BuildColorBlock(env, greenMat, redMat);

        // ── 6. 5 gates + collect their GateTrigger components ────────
        GateTrigger[] gateTriggers = new GateTrigger[GateZ.Length];
        for (int i = 0; i < GateZ.Length; i++)
            gateTriggers[i] = BuildGate(env, i, GateZ[i], greenMat, redMat, wallMat);

        // ── 7. Finish trigger ─────────────────────────────────────────
        FinishTrigger finishTrigger = BuildFinish(env, finishMat);

        // ── 8. Score display HUD ─────────────────────────────────────
        ScoreDisplay scoreDisplay = BuildScoreDisplay(env);

        // ── 9. Agent ──────────────────────────────────────────────────
        GameObject agentGO = BuildAgent(env, agentMat, scoreDisplay);

        // ── 10. Manager — wire all references ────────────────────────
        BuildManager(env, agentGO, colorBlock, gateTriggers, finishTrigger, scoreDisplay);

        // ── 11. Camera — follow the agent ────────────────────────────
        SetupCamera(agentGO);

        // ── 12. Save scene — write to a fixed path so no dialog appears ──
        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);

        // If the scene has never been saved it has no path (shows as "Untitled").
        // SaveOpenScenes() would pop a Save-As dialog in that case, so we instead
        // save directly to Assets/Scenes/HallwayScene.unity.
        string scenePath = activeScene.path;
        if (string.IsNullOrEmpty(scenePath))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            scenePath = "Assets/Scenes/HallwayScene.unity";
        }
        EditorSceneManager.SaveScene(activeScene, scenePath);

        Debug.Log("[HallwaySceneBuilder] 5-gate corridor built and saved to: " + scenePath +
                  " -- Start mlagents-learn then press Play.");

        Selection.activeGameObject = env;
        EditorGUIUtility.PingObject(env);
    }

    // ================================================================
    //  TAG SETUP
    // ================================================================

    static void AddRequiredTags()
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");

        foreach (string tag in RequiredTags)
        {
            bool exists = false;
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag)
                { exists = true; break; }
            }
            if (!exists)
            {
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
                Debug.Log($"[HallwaySceneBuilder] Added tag: {tag}");
            }
        }
        tagManager.ApplyModifiedProperties();
    }

    // ================================================================
    //  MATERIAL HELPERS
    // ================================================================

    static Material LoadOrCreateMaterial(string name, Color color)
    {
        string path = $"Assets/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");
        Material mat = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        return mat;
    }

    // ================================================================
    //  FLOOR
    //  Center at z=-2, spanning z=-27 to z=+23 (covers full corridor)
    // ================================================================

    static void BuildFloor(GameObject parent, Material mat)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(parent.transform, false);
        floor.transform.localPosition = new Vector3(0f, -0.25f, -2f);
        floor.transform.localScale    = new Vector3(10f, 0.5f, 50f);
        floor.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // ================================================================
    //  WALLS  (left, right, back, front)
    // ================================================================

    static void BuildWalls(GameObject parent, Material mat)
    {
        var walls = new (string name, Vector3 pos, Vector3 scale)[]
        {
            ("Wall_Left",  new Vector3(-5.25f, 1.25f,   -2f), new Vector3(0.5f, 2.5f, 50f)),
            ("Wall_Right", new Vector3(+5.25f, 1.25f,   -2f), new Vector3(0.5f, 2.5f, 50f)),
            ("Wall_Back",  new Vector3(  0f,   1.25f, -26.25f), new Vector3(11f, 2.5f, 0.5f)),
            ("Wall_Front", new Vector3(  0f,   1.25f, +22.25f), new Vector3(11f, 2.5f, 0.5f)),
        };

        foreach (var (n, p, s) in walls)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = n;
            wall.transform.SetParent(parent.transform, false);
            wall.transform.localPosition = p;
            wall.transform.localScale    = s;
            wall.tag = "Wall";
            wall.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    // ================================================================
    //  COLOUR INDICATOR BLOCK
    //  Sits at z=-24, between the back wall and gate 0.
    //  BlockRandomizer changes its colour and tag each episode.
    // ================================================================

    static GameObject BuildColorBlock(GameObject parent,
                                       Material greenMat, Material redMat)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "ColorIndicator";
        block.transform.SetParent(parent.transform, false);
        block.transform.localPosition = new Vector3(0f, 0.75f, -20f);
        block.transform.localScale    = new Vector3(1.5f, 1.5f, 1.5f);
        block.tag = "GreenBlock"; // BlockRandomizer overrides this at runtime

        block.GetComponent<Renderer>().sharedMaterial = greenMat;

        // Make the collider a trigger so the agent can walk through the block.
        // Without this the block physically blocks the corridor — the agent starts
        // at z=-23 and the block is at z=-20, so it can never reach the gates.
        // Raycasts still detect the block because CollectObservations uses
        // QueryTriggerInteraction.Collide.
        block.GetComponent<BoxCollider>().isTrigger = true;

        BlockRandomizer randomizer = block.AddComponent<BlockRandomizer>();

        SerializedObject so = new SerializedObject(randomizer);
        so.FindProperty("greenMaterial").objectReferenceValue = greenMat;
        so.FindProperty("redMaterial").objectReferenceValue   = redMat;
        so.ApplyModifiedProperties();

        return block;
    }

    // ================================================================
    //  GATE  (two colour panels + solid centre divider + scoring trigger)
    //
    //  Corridor is 10 units wide (x = -5 to +5).
    //  Each gate has THREE objects:
    //
    //    Left colour panel  → x=-2.5, scale_x=4.6  (trigger, passable)
    //                         covers x = -4.8 to -0.2
    //    Centre divider     → x= 0.0, scale_x=0.4  (SOLID wall)
    //                         covers x = -0.2 to +0.2
    //    Right colour panel → x=+2.5, scale_x=4.6  (trigger, passable)
    //                         covers x = +0.2 to +4.8
    //
    //  The centre divider is SOLID — the agent (scale_x=0.5) physically cannot
    //  walk straight through the middle. It MUST commit to the left or right
    //  half of the corridor BEFORE reaching the gate.
    //
    //  Both colour panels are TRIGGERS — the agent walks through the coloured
    //  surface freely; the reward signal teaches the correct side to choose.
    //  Panels are tagged GreenBlock / RedBlock so raycasts detect colour.
    //
    //  The invisible GateTrigger plane at z = gateZ+0.55 fires when the agent
    //  crosses and checks agent.x < 0 (left=green?) to issue the reward.
    //
    //  Colours alternate gate-by-gate (even index → left=Green).
    // ================================================================

    static GateTrigger BuildGate(GameObject parent, int index, float gateZ,
                                  Material greenMat, Material redMat, Material wallMat)
    {
        bool leftIsGreen = (index % 2 == 0);

        // ── Left colour panel (trigger — agent walks through) ────────
        // scale_x=4.6 → panel covers x=-4.8 to x=-0.2, leaving a 0.4-unit
        // gap at centre that the solid divider fills.
        GameObject left = GameObject.CreatePrimitive(PrimitiveType.Cube);
        left.name = $"Gate{index:00}_Left_{(leftIsGreen ? "Green" : "Red")}";
        left.transform.SetParent(parent.transform, false);
        left.transform.localPosition = new Vector3(-2.5f, 1.25f, gateZ);
        left.transform.localScale    = new Vector3(4.6f,  2.5f,  0.3f);
        left.tag = leftIsGreen ? "GreenBlock" : "RedBlock";
        left.GetComponent<Renderer>().sharedMaterial = leftIsGreen ? greenMat : redMat;
        left.GetComponent<BoxCollider>().isTrigger = true;   // passable

        // ── Right colour panel (trigger — agent walks through) ───────
        GameObject right = GameObject.CreatePrimitive(PrimitiveType.Cube);
        right.name = $"Gate{index:00}_Right_{(leftIsGreen ? "Red" : "Green")}";
        right.transform.SetParent(parent.transform, false);
        right.transform.localPosition = new Vector3(+2.5f, 1.25f, gateZ);
        right.transform.localScale    = new Vector3(4.6f,  2.5f,  0.3f);
        right.tag = leftIsGreen ? "RedBlock" : "GreenBlock";
        right.GetComponent<Renderer>().sharedMaterial = leftIsGreen ? redMat : greenMat;
        right.GetComponent<BoxCollider>().isTrigger = true;  // passable

        // ── Centre divider (SOLID — agent cannot pass through) ───────
        // scale_x=0.4: narrower than the agent (scale_x=0.5 → ~0.5 unit) so the
        // agent physically cannot squeeze through — it must pick a side.
        // Tagged "Wall" so raycasts detect it as an obstacle.
        GameObject divider = GameObject.CreatePrimitive(PrimitiveType.Cube);
        divider.name = $"Gate{index:00}_Centre_Divider";
        divider.transform.SetParent(parent.transform, false);
        divider.transform.localPosition = new Vector3(0f, 1.25f, gateZ);
        divider.transform.localScale    = new Vector3(0.4f, 2.5f, 0.4f);
        divider.tag = "Wall";
        divider.GetComponent<Renderer>().sharedMaterial = wallMat;
        // BoxCollider is solid by default — intentionally NOT set to isTrigger

        // ── Scoring trigger plane ────────────────────────────────────
        // Invisible, placed just past the colour panels.
        // Two separate halves (left and right) with a gap at centre matching
        // the solid divider — prevents the trigger from firing if the agent
        // somehow clips through the divider's edge.
        for (int side = -1; side <= 1; side += 2)  // -1 = left, +1 = right
        {
            GameObject halfTrigger = new GameObject($"Gate{index:00}_Trigger_{(side < 0 ? "L" : "R")}");
            halfTrigger.transform.SetParent(parent.transform, false);
            halfTrigger.transform.localPosition = new Vector3(side * 2.5f, 0.75f, gateZ + 0.55f);
            halfTrigger.transform.localScale    = new Vector3(4.6f, 1.5f, 0.1f);
            halfTrigger.AddComponent<BoxCollider>().isTrigger = true;
        }

        // Master trigger GO — holds the GateTrigger component.
        // Uses a thin full-width plane; the split half-triggers above add
        // spatial precision but the master is what the component attaches to.
        GameObject triggerGO = new GameObject($"Gate{index:00}_Trigger");
        triggerGO.transform.SetParent(parent.transform, false);
        triggerGO.transform.localPosition = new Vector3(0f, 0.75f, gateZ + 0.55f);
        triggerGO.transform.localScale    = new Vector3(10f, 1.5f, 0.1f);

        BoxCollider bc = triggerGO.AddComponent<BoxCollider>();
        bc.isTrigger = true;

        GateTrigger gt = triggerGO.AddComponent<GateTrigger>();
        SerializedObject so = new SerializedObject(gt);
        so.FindProperty("leftPillarIsGreen").boolValue = leftIsGreen;
        so.ApplyModifiedProperties();

        return gt;
    }

    // ================================================================
    //  FINISH TRIGGER
    //  Wide invisible plane at z=+27. Fires OnFinishReached on the agent.
    // ================================================================

    static FinishTrigger BuildFinish(GameObject parent, Material mat)
    {
        // Visible finish line strip on the floor
        GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        strip.name = "FinishLine_Visual";
        strip.transform.SetParent(parent.transform, false);
        strip.transform.localPosition = new Vector3(0f, 0.01f, 18f);
        strip.transform.localScale    = new Vector3(10f, 0.05f, 0.6f);
        strip.GetComponent<Renderer>().sharedMaterial = mat;
        strip.GetComponent<BoxCollider>().isTrigger = true; // visual only, no physics

        // Invisible detection trigger
        GameObject triggerGO = new GameObject("FinishTrigger");
        triggerGO.transform.SetParent(parent.transform, false);
        triggerGO.transform.localPosition = new Vector3(0f, 0.75f, 18f);
        triggerGO.transform.localScale    = new Vector3(10f, 1.5f, 0.1f);

        BoxCollider bc = triggerGO.AddComponent<BoxCollider>();
        bc.isTrigger = true;

        return triggerGO.AddComponent<FinishTrigger>();
    }

    // ================================================================
    //  SCORE DISPLAY HUD
    // ================================================================

    static ScoreDisplay BuildScoreDisplay(GameObject parent)
    {
        GameObject go = new GameObject("ScoreDisplay");
        go.transform.SetParent(parent.transform, false);
        return go.AddComponent<ScoreDisplay>();
    }

    // ================================================================
    //  AGENT
    // ================================================================

    static GameObject BuildAgent(GameObject parent, Material mat, ScoreDisplay scoreDisplay)
    {
        GameObject agentGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        agentGO.name = "Agent";
        agentGO.transform.SetParent(parent.transform, false);
        agentGO.transform.localPosition = new Vector3(0f, 0.5f, -23f);
        agentGO.transform.localScale    = new Vector3(0.5f, 0.5f, 0.5f);
        agentGO.GetComponent<Renderer>().sharedMaterial = mat;

        // ── Rigidbody ────────────────────────────────────────────────
        Rigidbody rb = agentGO.AddComponent<Rigidbody>();
        rb.mass        = 1f;
        rb.useGravity  = true;
        rb.isKinematic = false;

        // ── HallwayAgent ─────────────────────────────────────────────
        HallwayAgent agent = agentGO.AddComponent<HallwayAgent>();
        agent.MaxStep = 1000;

        // Wire scoreDisplay
        SerializedObject agentSO0 = new SerializedObject(agent);
        agentSO0.FindProperty("scoreDisplay").objectReferenceValue = scoreDisplay;
        agentSO0.ApplyModifiedProperties();

        // ── BehaviorNameFixer ────────────────────────────────────────
        // This component runs at DefaultExecutionOrder(-500), BEFORE
        // BehaviorParameters registers with the Academy. It guarantees
        // "HallwayAgent" is the name sent to the Python trainer even if
        // Unity's serialization reverts the BehaviorParameters name to
        // the default "My Behavior".
        BehaviorNameFixer nameFixer = agentGO.AddComponent<BehaviorNameFixer>();
        SerializedObject fixerSO = new SerializedObject(nameFixer);
        SerializedProperty fixerName = fixerSO.FindProperty("targetBehaviorName");
        if (fixerName != null) fixerName.stringValue = "HallwayAgent";
        SerializedProperty fixerObsSize = fixerSO.FindProperty("vectorObservationSize");
        if (fixerObsSize != null) fixerObsSize.intValue = 31;
        fixerSO.ApplyModifiedProperties();
        nameFixer.targetBehaviorName = "HallwayAgent";
        nameFixer.vectorObservationSize = 31;
        EditorUtility.SetDirty(nameFixer);

        // ── BehaviorParameters ───────────────────────────────────────
        // IMPORTANT: Use SerializedObject to set BrainParameters fields.
        // Direct property assignment (bp.BrainParameters.VectorObservationSize = 31)
        // does NOT persist through Unity serialization without SetDirty,
        // causing the value to silently revert to 1 (the default) at runtime.
        // Using SerializedObject ensures the value is written into the asset.
        BehaviorParameters bp = agentGO.AddComponent<BehaviorParameters>();

        SerializedObject bpSO = new SerializedObject(bp);

        SerializedProperty bpName = bpSO.FindProperty("m_BehaviorName");
        if (bpName != null) bpName.stringValue = "HallwayAgent";

        SerializedProperty bpType = bpSO.FindProperty("m_BehaviorType");
        if (bpType != null) bpType.intValue = 0; // 0 = BehaviorType.Default

        SerializedProperty brainP = bpSO.FindProperty("m_BrainParameters");
        if (brainP != null)
        {
            SerializedProperty vecObs = brainP.FindPropertyRelative("m_VectorObservationSize");
            if (vecObs != null)
                vecObs.intValue = 31; // 6 rays × 5 values + 1 lateral position = 31

            // ActionSpec: 1 discrete branch of size 4
            SerializedProperty actionSpec = brainP.FindPropertyRelative("m_ActionSpec");
            if (actionSpec != null)
            {
                SerializedProperty branches = actionSpec.FindPropertyRelative("BranchSizes");
                if (branches != null)
                {
                    branches.arraySize = 1;
                    branches.GetArrayElementAtIndex(0).intValue = 4;
                }
            }
        }
        bpSO.ApplyModifiedProperties();

        // Belt-and-suspenders: also set directly and mark dirty.
        // If the m_ field names differ in the installed ML-Agents version,
        // the direct assignment with SetDirty ensures the value still sticks.
        bp.BehaviorName = "HallwayAgent";
        bp.BehaviorType = BehaviorType.Default;
        bp.BrainParameters.VectorObservationSize = 31;
        bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(4);
        EditorUtility.SetDirty(bp);

        // ── DecisionRequester ────────────────────────────────────────
        DecisionRequester dr = agentGO.AddComponent<DecisionRequester>();
        SerializedObject drSO = new SerializedObject(dr);
        SerializedProperty decPeriod = drSO.FindProperty("DecisionPeriod");
        if (decPeriod != null)
        {
            decPeriod.intValue = 5;
            drSO.ApplyModifiedProperties();
        }

        return agentGO;
    }

    // ================================================================
    //  MANAGER — wire all Inspector references automatically
    // ================================================================

    static void BuildManager(GameObject parent,
                              GameObject agentGO,
                              GameObject colorBlock,
                              GateTrigger[] gateTriggers,
                              FinishTrigger finishTrigger,
                              ScoreDisplay scoreDisplay)
    {
        GameObject managerGO = new GameObject("HallwayManager");
        managerGO.transform.SetParent(parent.transform, false);

        HallwayManager manager = managerGO.AddComponent<HallwayManager>();
        SerializedObject so    = new SerializedObject(manager);

        so.FindProperty("agent").objectReferenceValue =
            agentGO.GetComponent<HallwayAgent>();
        so.FindProperty("blockRandomizer").objectReferenceValue =
            colorBlock.GetComponent<BlockRandomizer>();
        so.FindProperty("finishTrigger").objectReferenceValue = finishTrigger;

        // Wire all 5 GateTriggers into the array
        SerializedProperty gateArr = so.FindProperty("gateTriggers");
        gateArr.arraySize = gateTriggers.Length;
        for (int i = 0; i < gateTriggers.Length; i++)
            gateArr.GetArrayElementAtIndex(i).objectReferenceValue = gateTriggers[i];

        // Set spawn defaults that match the new layout
        so.FindProperty("agentSpawnPosition").vector3Value =
            new Vector3(0f, 0.5f, -23f);
        so.FindProperty("spawnYawVariance").floatValue = 10f;

        so.ApplyModifiedProperties();

        // Wire HallwayAgent -> manager, blockRandomizer, scoreDisplay
        HallwayAgent agentScript = agentGO.GetComponent<HallwayAgent>();
        SerializedObject agentSO = new SerializedObject(agentScript);
        agentSO.FindProperty("hallwayManager").objectReferenceValue  = manager;
        agentSO.FindProperty("blockRandomizer").objectReferenceValue =
            colorBlock.GetComponent<BlockRandomizer>();
        agentSO.FindProperty("scoreDisplay").objectReferenceValue    = scoreDisplay;
        agentSO.ApplyModifiedProperties();
    }

    // ================================================================
    //  CAMERA — follows the agent so the game is always visible
    // ================================================================

    static void SetupCamera(GameObject agentGO)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // Remove any existing CameraFollow (in case this is a rebuild)
        CameraFollow existing = cam.GetComponent<CameraFollow>();
        if (existing != null) Object.DestroyImmediate(existing);

        // Start the camera at a position that shows the whole corridor
        cam.transform.position = new Vector3(0f, 10f, -30f);
        cam.transform.rotation = Quaternion.Euler(35f, 0f, 0f);

        // Add CameraFollow — it will smoothly track the agent during play
        CameraFollow cf = cam.gameObject.AddComponent<CameraFollow>();
        SerializedObject cfSO = new SerializedObject(cf);
        cfSO.FindProperty("target").objectReferenceValue = agentGO.transform;
        cfSO.ApplyModifiedProperties();
    }
}
