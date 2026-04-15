using UnityEngine;

/// <summary>
/// HallwayManager — Coordinates episode-level environment resets.
///
/// Responsibilities:
///   1. Reset all GateTrigger and FinishTrigger guards BEFORE repositioning the agent.
///   2. Randomize the colored block color for the new episode.
///   3. Reposition the agent at the spawn point.
///
/// Architecture Note:
///   The manager is the single source of "episode truth" for the scene.
///   HallwayAgent.OnEpisodeBegin() calls ResetEnvironment() here, keeping
///   all reset logic in one place rather than scattered across components.
/// </summary>
public class HallwayManager : MonoBehaviour
{
    // ============================================================
    // INSPECTOR FIELDS
    // ============================================================

    [Header("References")]
    [Tooltip("The HallwayAgent in the scene.")]
    [SerializeField] private HallwayAgent agent;

    [Tooltip("The BlockRandomizer attached to the color indicator block.")]
    [SerializeField] private BlockRandomizer blockRandomizer;

    [Tooltip("All 5 GateTriggers along the corridor (auto-wired by HallwaySceneBuilder).")]
    [SerializeField] private GateTrigger[] gateTriggers;

    [Tooltip("The FinishTrigger at the far end of the corridor.")]
    [SerializeField] private FinishTrigger finishTrigger;

    [Header("Agent Spawn")]
    [Tooltip("Local position where the agent spawns each episode.")]
    [SerializeField] private Vector3 agentSpawnPosition = new Vector3(0f, 0.5f, -27f);

    [Tooltip("Max random yaw rotation (degrees) at spawn. Keeps agent facing roughly forward.")]
    [SerializeField] private float spawnYawVariance = 10f;

    // ============================================================
    // UNITY LIFECYCLE
    // ============================================================

    private void Start()
    {
        if (agent == null)
            Debug.LogError("[HallwayManager] agent is not assigned.", this);
        if (blockRandomizer == null)
            Debug.LogError("[HallwayManager] blockRandomizer is not assigned.", this);
        if (gateTriggers == null || gateTriggers.Length == 0)
            Debug.LogWarning("[HallwayManager] gateTriggers array is empty.", this);
        if (finishTrigger == null)
            Debug.LogWarning("[HallwayManager] finishTrigger is not assigned.", this);
    }

    // ============================================================
    // PUBLIC API
    // ============================================================

    /// <summary>
    /// Resets the entire environment for a new training episode.
    /// Called by HallwayAgent.OnEpisodeBegin().
    ///
    /// Order is critical:
    ///   1. Reset ALL trigger guards FIRST so no stale OnTriggerEnter from
    ///      the previous episode bleeds into the new one.
    ///   2. Randomize target colour — both GateTrigger and FinishTrigger
    ///      need the correct IsGreen state before the agent moves.
    ///   3. Reposition agent LAST — its first observations see the freshly
    ///      randomized colour indicator with no leftover trigger state.
    /// </summary>
    public void ResetEnvironment()
    {
        // Step 1: Reset all gate trigger latch flags
        if (gateTriggers != null)
        {
            foreach (GateTrigger gt in gateTriggers)
                if (gt != null) gt.ResetTrigger();
        }

        // Reset finish trigger latch flag
        if (finishTrigger != null)
            finishTrigger.ResetTrigger();

        // Step 2: Randomize target colour (green or red, 50/50 each episode)
        if (blockRandomizer != null)
            blockRandomizer.RandomizeBlock();

        // Step 3: Place agent at spawn with small random yaw so it faces down the corridor
        if (agent != null)
        {
            float yaw = Random.Range(-spawnYawVariance, spawnYawVariance);
            agent.ResetAgent(agentSpawnPosition, Quaternion.Euler(0f, yaw, 0f));
        }
    }
}
