using UnityEngine;

/// <summary>
/// GoalTrigger — Attached to each goal zone collider.
///
/// Detects when the agent enters the goal zone and notifies the agent,
/// which then computes the reward and ends the episode.
///
/// Design choice — why not compute reward here?
///   GoalTrigger only knows "which goal was entered".
///   HallwayAgent knows "which color is correct" (via BlockRandomizer).
///   Reward logic lives in the agent so it stays co-located with
///   AddReward() and EndEpisode() — the ML-Agents contract.
///
/// Scene setup:
///   - Attach to the Green Goal → set isGreenGoal = true
///   - Attach to the Red Goal   → set isGreenGoal = false
///   - The goal collider MUST have "Is Trigger" checked.
///   - Tag goals "GreenGoal" / "RedGoal" for raycast detection.
///   - Wire both triggers into HallwayManager.goalTriggers[] in the Inspector.
/// </summary>
public class GoalTrigger : MonoBehaviour
{
    // ============================================================
    // INSPECTOR FIELDS
    // ============================================================

    [Header("Goal Identity")]
    [Tooltip("TRUE  → this is the Green goal zone.\n" +
             "FALSE → this is the Red goal zone.")]
    [SerializeField] private bool isGreenGoal;

    // ============================================================
    // PRIVATE STATE
    // ============================================================

    // BUG FIX: Guard against double-fire.
    //
    // Problem: After EndEpisode() is called, the physics engine may not
    // immediately move the agent out of the trigger volume. A second
    // OnTriggerEnter (or OnTriggerStay on fast rigidbodies) can fire in
    // the same frame or the next, calling AddReward + EndEpisode on the
    // BRAND NEW episode — silently corrupting training reward signals.
    //
    // Fix: Latch `triggered` on first contact. HallwayManager.ResetEnvironment()
    // calls ResetTrigger() to unlatch it for the next episode.
    private bool triggered;

    // ============================================================
    // PUBLIC API
    // ============================================================

    /// <summary>
    /// Resets the trigger guard for a new episode.
    /// Called by HallwayManager.ResetEnvironment() before agent repositioning.
    /// </summary>
    public void ResetTrigger()
    {
        triggered = false;
    }

    // ============================================================
    // TRIGGER DETECTION
    // ============================================================

    private void OnTriggerEnter(Collider other)
    {
        // Guard: ignore any re-entry until HallwayManager resets this trigger
        if (triggered) return;

        HallwayAgent agent = other.GetComponent<HallwayAgent>();
        if (agent != null)
        {
            triggered = true; // Latch — no further callbacks this episode
            agent.OnGoalReached(isGreenGoal);
        }
    }
}
