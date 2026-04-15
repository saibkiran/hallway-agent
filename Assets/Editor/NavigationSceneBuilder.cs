using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;

/// <summary>
/// NavigationSceneBuilder — Builds the 20x20 obstacle-navigation training arena.
///
/// Run via: Unity menu bar -> NavigationAgent -> Build Navigation Scene
///
/// Layout:
///   Ground:     20x20 flat plane centred at origin
///   Agent:      blue cube at (-8, 0.5, -8)  — bottom-left
///   Goal:       green trigger cube at (8, 0.5, 8) — top-right
///   Obstacles:  20 cubes, randomised size (0.5-1.5 per axis) and position
///
/// Guaranteed path (by construction — no obstacle can appear here):
///   Left strip   x in [-10, -5]  (full Z range)  — agent walks north along left wall
///   Top strip    z in [  5, 10]  (full X range)  — agent walks east to goal
///   The L-shaped path from (-8,-8) to (-8,+8) to (+8,+8) is always clear.
///   Obstacles are placed only inside the zone  x in [-4, 8]  AND  z in [-8, 4].
/// </summary>
public static class NavigationSceneBuilder
{
    // Tags used by NavigationAgent raycasts and collision callbacks
    static readonly string[] RequiredTags = { "Wall", "Obstacle", "NavGoal" };

    // Obstacle placement bounds (guaranteed to never overlap safe corridors)
    const float OBS_X_MIN = -4f;
    const float OBS_X_MAX =  8f;
    const float OBS_Z_MIN = -8f;
    const float OBS_Z_MAX =  4f;
    const int   OBS_COUNT = 20;

    // Minimum separation between obstacle centres (prevents total blocking)
    const float MIN_SEPARATION = 2.0f;

    [MenuItem("NavigationAgent/Build Navigation Scene")]
    public static void BuildScene()
    {
        // ── 1. Tags ──────────────────────────────────────────────────
        AddRequiredTags();

        // ── 2. Materials ─────────────────────────────────────────────
        Material agentMat    = LoadOrCreateMaterial("AgentMat",      new Color(0.20f, 0.55f, 0.90f));
        Material goalMat     = LoadOrCreateMaterial("NavGoalMat",    new Color(0.18f, 0.85f, 0.18f));
        Material obstacleMat = LoadOrCreateMaterial("ObstacleMat",   new Color(0.90f, 0.55f, 0.10f));
        Material groundMat   = LoadOrCreateMaterial("NavGroundMat",  new Color(0.25f, 0.25f, 0.25f));
        Material wallMat     = LoadOrCreateMaterial("WallMat",       new Color(0.55f, 0.55f, 0.55f));

        // ── 3. Environment root ──────────────────────────────────────
        GameObject old = GameObject.Find("NavigationEnvironment");
        if (old != null) Object.DestroyImmediate(old);

        GameObject env = new GameObject("NavigationEnvironment");

        // ── 4. Ground + boundary walls ───────────────────────────────
        BuildGround(env, groundMat);
        BuildWalls(env, wallMat);

        // ── 5. Goal (trigger — agent walks through it) ───────────────
        GameObject goalGO = BuildGoal(env, goalMat);

        // ── 6. 20 obstacles (randomly sized and placed) ──────────────
        BuildObstacles(env, obstacleMat);

        // ── 7. Agent ──────────────────────────────────────────────────
        GameObject agentGO = BuildAgent(env, agentMat, goalGO.transform);

        // ── 8. Camera — top-down view of the full arena ──────────────
        PositionCamera();

        // ── 9. Save to Assets/Scenes/NavigationScene.unity ───────────
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);

        string scenePath = scene.path;
        if (string.IsNullOrEmpty(scenePath))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            scenePath = "Assets/Scenes/NavigationScene.unity";
        }
        EditorSceneManager.SaveScene(scene, scenePath);

        Debug.Log("[NavigationSceneBuilder] Navigation scene built and saved to: " + scenePath);
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
                Debug.Log("[NavigationSceneBuilder] Added tag: " + tag);
            }
        }
        tagManager.ApplyModifiedProperties();
    }

    // ================================================================
    //  MATERIAL HELPER
    // ================================================================

    static Material LoadOrCreateMaterial(string name, Color color)
    {
        string path = "Assets/" + name + ".mat";
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
    //  GROUND  (20x20 flat plane, y top surface at 0)
    // ================================================================

    static void BuildGround(GameObject parent, Material mat)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.SetParent(parent.transform, false);
        ground.transform.localPosition = new Vector3(0f, -0.25f, 0f);
        ground.transform.localScale    = new Vector3(20f, 0.5f, 20f);
        ground.GetComponent<Renderer>().sharedMaterial = mat;
        // No Rigidbody — static
    }

    // ================================================================
    //  BOUNDARY WALLS  (4 thin invisible barriers, tagged "Wall")
    //  Prevent the agent from falling off the edge.
    // ================================================================

    static void BuildWalls(GameObject parent, Material mat)
    {
        // (name, position, scale)
        var walls = new (string n, Vector3 p, Vector3 s)[]
        {
            ("Wall_North", new Vector3(  0f, 1f,  10.25f), new Vector3(20.5f, 2f, 0.5f)),
            ("Wall_South", new Vector3(  0f, 1f, -10.25f), new Vector3(20.5f, 2f, 0.5f)),
            ("Wall_East",  new Vector3( 10.25f, 1f, 0f),   new Vector3(0.5f,  2f, 20.5f)),
            ("Wall_West",  new Vector3(-10.25f, 1f, 0f),   new Vector3(0.5f,  2f, 20.5f)),
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
    //  GOAL  (green trigger cube at top-right corner)
    // ================================================================

    static GameObject BuildGoal(GameObject parent, Material mat)
    {
        GameObject goal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        goal.name = "Goal";
        goal.transform.SetParent(parent.transform, false);
        goal.transform.localPosition = new Vector3(8f, 0.5f, 8f);
        goal.transform.localScale    = new Vector3(1.2f, 1.2f, 1.2f);
        goal.tag = "NavGoal";
        goal.GetComponent<Renderer>().sharedMaterial = mat;

        // isTrigger = true so the agent walks through it (trigger, not solid)
        goal.GetComponent<BoxCollider>().isTrigger = true;

        return goal;
    }

    // ================================================================
    //  OBSTACLES  (20 solid cubes, random size and position)
    //
    //  Placement zone: x in [-4, 8], z in [-8, 4]
    //  This keeps the left corridor (x <= -5) and top corridor (z >= 5) clear,
    //  guaranteeing an L-shaped path from spawn (-8,-8) to goal (8,8).
    //
    //  A minimum separation of 2 units between obstacle centres is enforced
    //  so no two obstacles completely block a passage.
    // ================================================================

    static void BuildObstacles(GameObject parent, Material mat)
    {
        // Orange-brown material tinted per obstacle for visual variety
        var placed = new List<Vector2>(OBS_COUNT);
        int maxAttempts = 200;

        for (int i = 0; i < OBS_COUNT; i++)
        {
            // Find a valid position
            Vector2 pos = Vector2.zero;
            bool found  = false;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                float x = Random.Range(OBS_X_MIN, OBS_X_MAX);
                float z = Random.Range(OBS_Z_MIN, OBS_Z_MAX);
                pos = new Vector2(x, z);

                if (!TooClose(pos, placed, MIN_SEPARATION))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Debug.LogWarning($"[NavigationSceneBuilder] Obstacle {i} could not find " +
                                  "a clear spot after " + maxAttempts + " attempts — skipped.");
                continue;
            }

            placed.Add(pos);

            // Random scale: each axis independent in [0.5, 1.5]
            float sx = Random.Range(0.5f, 1.5f);
            float sy = Random.Range(0.5f, 1.5f);
            float sz = Random.Range(0.5f, 1.5f);

            GameObject obs = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obs.name = $"Obstacle_{i:00}";
            obs.transform.SetParent(parent.transform, false);

            // Position Y so bottom sits flush on ground (y = sy/2)
            obs.transform.localPosition = new Vector3(pos.x, sy * 0.5f, pos.y);
            obs.transform.localScale    = new Vector3(sx, sy, sz);
            obs.tag = "Obstacle";

            // Tint each obstacle slightly differently for visual clarity
            Material obsMat = new Material(mat);
            obsMat.color = new Color(
                Random.Range(0.7f, 1.0f),
                Random.Range(0.3f, 0.6f),
                Random.Range(0.0f, 0.2f));
            obs.GetComponent<Renderer>().sharedMaterial = obsMat;

            // BoxCollider is solid by default (isTrigger = false) — DO NOT change this.
            // The agent receives -0.2 reward and EndEpisode on collision.
        }
    }

    static bool TooClose(Vector2 pos, List<Vector2> existing, float minDist)
    {
        foreach (var p in existing)
            if (Vector2.Distance(pos, p) < minDist) return true;
        return false;
    }

    // ================================================================
    //  AGENT
    // ================================================================

    static GameObject BuildAgent(GameObject parent, Material mat, Transform goalTransform)
    {
        GameObject agentGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        agentGO.name = "NavigationAgent";
        agentGO.transform.SetParent(parent.transform, false);
        agentGO.transform.localPosition = new Vector3(-8f, 0.5f, -8f);
        agentGO.transform.localScale    = new Vector3(0.6f, 0.6f, 0.6f);
        agentGO.GetComponent<Renderer>().sharedMaterial = mat;

        // ── Rigidbody ────────────────────────────────────────────────
        Rigidbody rb = agentGO.AddComponent<Rigidbody>();
        rb.mass        = 1f;
        rb.useGravity  = true;
        rb.isKinematic = false;

        // ── NavigationAgent ──────────────────────────────────────────
        NavigationAgent agent = agentGO.AddComponent<NavigationAgent>();
        agent.MaxStep = 1000;

        // Wire goal reference
        SerializedObject agentSO = new SerializedObject(agent);
        agentSO.FindProperty("goal").objectReferenceValue = goalTransform;
        agentSO.ApplyModifiedProperties();

        // ── BehaviorParameters ───────────────────────────────────────
        BehaviorParameters bp = agentGO.AddComponent<BehaviorParameters>();
        bp.BehaviorName = "NavigationAgent";
        bp.BehaviorType = BehaviorType.Default;
        bp.BrainParameters.VectorObservationSize = 27; // 3 goal + 8 rays x 3
        bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(5); // 0-4

        // ── DecisionRequester ────────────────────────────────────────
        DecisionRequester dr = agentGO.AddComponent<DecisionRequester>();
        SerializedObject  drSO = new SerializedObject(dr);
        SerializedProperty decPeriod = drSO.FindProperty("DecisionPeriod");
        if (decPeriod != null)
        {
            decPeriod.intValue = 5;
            drSO.ApplyModifiedProperties();
        }

        return agentGO;
    }

    // ================================================================
    //  CAMERA — top-down overview
    // ================================================================

    static void PositionCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        cam.transform.position = new Vector3(0f, 28f, 0f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // straight down
        cam.orthographic       = true;
        cam.orthographicSize   = 12f; // shows the full 20x20 arena
    }
}
