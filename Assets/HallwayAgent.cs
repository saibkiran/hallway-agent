using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

/// <summary>
/// HallwayAgent — Core RL agent for the Hallway Memory Task.
///
/// Architecture Summary:
///   The agent casts 6 directional raycasts (30 values) + lateral position (1 value) = 31 observations.
///   walls, a green/red colored block, and the two goal zones.
///   An LSTM recurrent layer (configured in YAML) gives the agent memory,
///   allowing it to REMEMBER the block color after it passes out of view and
///   navigate to the correct matching goal.
///
/// Why LSTM?
///   After the agent moves past the colored block it can no longer see it.
///   The LSTM hidden state carries the "memory" of what color was observed
///   forward through time — this is the central memory challenge.
///
/// Why no isGreenCorrect observation?
///   Directly feeding the correct answer removes the memory challenge.
///   The agent must genuinely observe the block color via raycasts and
///   retain that knowledge through its LSTM hidden state.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class HallwayAgent : Agent
{
    // ============================================================
    // INSPECTOR FIELDS
    // ============================================================

    [Header("Scene References")]
    [Tooltip("The HallwayManager that coordinates episode resets.")]
    [SerializeField] private HallwayManager hallwayManager;

    [Tooltip("The BlockRandomizer that controls the colored block.")]
    [SerializeField] private BlockRandomizer blockRandomizer;

    [Tooltip("Optional HUD — shows episode score and saves to scores.csv.")]
    [SerializeField] private ScoreDisplay scoreDisplay;

    [Header("Movement")]
    [Tooltip("Linear speed when moving forward or backward (units/sec).")]
    [SerializeField] private float moveSpeed = 5f;

    [Tooltip("Angular speed when rotating left or right (degrees/sec).")]
    [SerializeField] private float rotateSpeed = 200f;

    [Header("Raycast Observations")]
    [Tooltip("Max distance each observation ray will travel.")]
    [SerializeField] private float rayDistance = 40f;

    [Tooltip("Physics layers the raycasts will detect. Default: Everything.")]
    [SerializeField] private LayerMask raycastLayers = ~0;

    // ============================================================
    // PRIVATE STATE
    // ============================================================

    private Rigidbody rb;
    private int gatesPassedThisEpisode = 0;

    // 6 ray angles (degrees) relative to the agent's forward direction.
    // Covers: hard-left, front-left, forward, front-right, hard-right, backward.
    // 6 rays × 5 values each = 30 total observations.
    private static readonly float[] RayAngles = { -90f, -45f, 0f, 45f, 90f, 180f };

    // Unity tags — must be added in Edit > Project Settings > Tags & Layers
    private const string TAG_WALL         = "Wall";
    private const string TAG_GREEN_BLOCK  = "GreenBlock";
    private const string TAG_RED_BLOCK    = "RedBlock";
    private const string TAG_GREEN_GOAL   = "GreenGoal";
    private const string TAG_RED_GOAL     = "RedGoal";

    // ============================================================
    // ML-AGENTS: INITIALIZATION
    // ============================================================

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();

        // Serialized references are wired by HallwaySceneBuilder via SerializedObject.
        // If that wiring silently failed (scene rebuilt mid-frame, domain reload race, etc.)
        // fall back to FindObjectOfType so the agent is ALWAYS fully wired at runtime.
        // FindObjectOfType is slow but only runs once per Play session.
        if (hallwayManager == null)
        {
            hallwayManager = FindObjectOfType<HallwayManager>();
            if (hallwayManager == null)
                Debug.LogError("[HallwayAgent] HallwayManager not found in scene.", this);
            else
                Debug.Log("[HallwayAgent] hallwayManager resolved via FindObjectOfType.");
        }
        if (blockRandomizer == null)
        {
            blockRandomizer = FindObjectOfType<BlockRandomizer>();
            if (blockRandomizer == null)
                Debug.LogError("[HallwayAgent] BlockRandomizer not found in scene.", this);
            else
                Debug.Log("[HallwayAgent] blockRandomizer resolved via FindObjectOfType.");
        }
        if (scoreDisplay == null)
            scoreDisplay = FindObjectOfType<ScoreDisplay>();

        // Keep non-kinematic so rb.linearVelocity drives movement with full physics
        // collision response. FreezePositionY prevents gravity from pulling the
        // agent off the floor; FreezeRotationX/Z prevents tipping.
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.FreezePositionY
                       | RigidbodyConstraints.FreezeRotationX
                       | RigidbodyConstraints.FreezeRotationZ;
    }

    // ============================================================
    // ML-AGENTS: EPISODE BEGIN
    // ============================================================

    public override void OnEpisodeBegin()
    {
        gatesPassedThisEpisode = 0;
        if (hallwayManager != null)
            hallwayManager.ResetEnvironment();
        else
            Debug.LogError("[HallwayAgent] Cannot reset — hallwayManager is null.", this);
    }

    // ============================================================
    // ML-AGENTS: OBSERVATIONS  (30 values total)
    // ============================================================

    /// <summary>
    /// Casts 6 rays (30 values) + 1 lateral position value = 31 observations total.
    ///
    /// Per ray (×6 = 30 values):
    ///   [0] isWall       — 1.0 if ray hit a wall
    ///   [1] isGreenBlock — 1.0 if ray hit the green color block
    ///   [2] isRedBlock   — 1.0 if ray hit the red color block
    ///   [3] isGreenGoal  — 1.0 if ray hit the green goal zone
    ///   [4] isRedGoal    — 1.0 if ray hit the red goal zone
    ///
    /// Observation [30] — normalised lateral position:
    ///   agent.x / 5.0  →  range [-1, +1]  (-1 = hard left wall, +1 = hard right wall)
    ///   This tells the agent which side of the corridor it is currently on so it
    ///   can learn "I need to be on the green side → steer left / right." Without
    ///   this the network has to infer lateral position purely from wall-distance
    ///   asymmetries in the raycasts, which is much harder to learn.
    ///
    /// Behavior Parameters → Space Size must be set to 31 in the Inspector.
    /// (HallwaySceneBuilder and BehaviorNameFixer both set this automatically.)
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        // Raise origin slightly so it doesn't clip the floor
        Vector3 origin = transform.position + Vector3.up * 0.5f;

        foreach (float angle in RayAngles)
        {
            // Rotate forward direction by `angle` degrees around world Y axis
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * transform.forward;

            // Binary hit flags — all default to 0 (nothing detected)
            float isWall = 0f, isGreenBlock = 0f, isRedBlock = 0f;
            float isGreenGoal = 0f, isRedGoal = 0f;

            // QueryTriggerInteraction.Collide forces raycasts to hit trigger
            // colliders (color panels, goal zones) regardless of the project-level
            // Physics.queriesHitTriggers setting.
            if (Physics.Raycast(origin, dir, out RaycastHit hit, rayDistance,
                                raycastLayers, QueryTriggerInteraction.Collide))
            {
                if      (hit.collider.CompareTag(TAG_WALL))        isWall = 1f;
                else if (hit.collider.CompareTag(TAG_GREEN_BLOCK)) isGreenBlock = 1f;
                else if (hit.collider.CompareTag(TAG_RED_BLOCK))   isRedBlock = 1f;
                else if (hit.collider.CompareTag(TAG_GREEN_GOAL))  isGreenGoal = 1f;
                else if (hit.collider.CompareTag(TAG_RED_GOAL))    isRedGoal = 1f;

                Debug.DrawRay(origin, dir * hit.distance, Color.red, Time.fixedDeltaTime);
            }
            else
            {
                Debug.DrawRay(origin, dir * rayDistance, Color.green, Time.fixedDeltaTime);
            }

            // 5 observations for this ray (6 rays × 5 = 30 values)
            sensor.AddObservation(isWall);
            sensor.AddObservation(isGreenBlock);
            sensor.AddObservation(isRedBlock);
            sensor.AddObservation(isGreenGoal);
            sensor.AddObservation(isRedGoal);
        }

        // Observation [30]: lateral position in corridor, normalised to [-1, +1].
        // Corridor walls are at x=±5; dividing by 5 maps x to [-1, +1].
        // This single value dramatically reduces the sample complexity of learning
        // the correct-side navigation behaviour.
        sensor.AddObservation(transform.localPosition.x / 5f);
    }

    // ============================================================
    // ML-AGENTS: ACTIONS
    // ============================================================

    /// <summary>
    /// Executes one of 4 discrete movement actions per decision step.
    ///
    ///   Action 0 → Move Forward
    ///   Action 1 → Move Backward
    ///   Action 2 → Rotate Left
    ///   Action 3 → Rotate Right
    ///
    /// Unity 6: Uses rb.linearVelocity (replaces deprecated rb.linearVelocity).
    /// This ensures physics collisions work correctly and prevents
    /// tunneling through walls at high speeds.
    ///
    /// Behavior Parameters → Discrete Branches: 1 branch of size 4.
    /// </summary>
    public override void OnActionReceived(ActionBuffers actions)
    {
        int action = actions.DiscreteActions[0];

        switch (action)
        {
            case 0: // Move Forward
                rb.linearVelocity = transform.forward * moveSpeed;
                break;

            case 1: // Move Backward
                rb.linearVelocity = -transform.forward * moveSpeed;
                break;

            case 2: // Rotate Left
                // BUG FIX: rb.MoveRotation communicates the new orientation to
                // the physics engine correctly. The old transform.Rotate() bypassed
                // the Rigidbody, causing the physics engine to use stale rotation
                // data for collision responses until the next FixedUpdate sync.
                rb.linearVelocity = Vector3.zero;
                rb.MoveRotation(rb.rotation *
                    Quaternion.Euler(0f, -rotateSpeed * Time.fixedDeltaTime, 0f));
                break;

            case 3: // Rotate Right
                rb.linearVelocity = Vector3.zero;
                rb.MoveRotation(rb.rotation *
                    Quaternion.Euler(0f, rotateSpeed * Time.fixedDeltaTime, 0f));
                break;
        }

        // Existential penalty: small per-step cost encourages the agent to
        // solve the task quickly rather than wandering indefinitely.
        AddReward(-0.0003f);
    }

    // ============================================================
    // GATE PASSED CALLBACK  (called by GateTrigger)
    // ============================================================

    /// <summary>
    /// Called by GateTrigger.OnTriggerEnter when the agent crosses a gate plane.
    ///
    /// Reward structure:
    ///   +0.10 per gate → agent passed the colour side that matches the starting block
    ///   -0.30 per gate → agent passed the wrong side; episode continues
    ///
    /// Why no EndEpisode on wrong gate:
    ///   Ending immediately means the LSTM sees at most 1 gate per episode and
    ///   has almost no sequence to learn a memory from. Keeping the episode alive
    ///   gives the recurrent layer a full corridor run (5 gate decisions) of
    ///   temporal context — the key requirement for memory formation.
    ///
    /// passedGreen is computed by GateTrigger from the agent's x position and
    /// the gate's leftPillarIsGreen setting.
    /// </summary>
    public void OnGatePassed(bool passedGreen)
    {
        bool correct = (passedGreen == blockRandomizer.IsGreen);
        if (correct)
        {
            gatesPassedThisEpisode++;
            AddReward(0.10f);
        }
        else
        {
            AddReward(-0.30f);
            // Display wrong-gate feedback but do NOT end the episode.
            // The agent must continue and face all remaining gates so the LSTM
            // accumulates a full-length sequence of experience per episode.
            if (scoreDisplay != null)
                scoreDisplay.RecordEpisode(GetCumulativeReward(), gatesPassedThisEpisode, "WRONG GATE");
        }
    }

    // ============================================================
    // FINISH LINE CALLBACK  (called by FinishTrigger)
    // ============================================================

    /// <summary>
    /// Called by FinishTrigger when the agent crosses the finish line at the
    /// far end of the corridor, past all 5 gates.
    ///
    /// Base finish reward: +1.0
    /// Bonus: +0.10 per gate that was crossed correctly this episode.
    /// Maximum: +1.0 + 5×0.10 = +1.50 (perfect run, all gates correct).
    ///
    /// The bonus makes the gradient between "random walk to finish" (+1.0 but
    /// with wrong-gate penalties) and "perfect memory run" (+1.50) large enough
    /// for PPO to reliably discover the correct strategy.
    /// </summary>
    public void OnFinishReached()
    {
        AddReward(1.0f + gatesPassedThisEpisode * 0.10f);
        NotifyScore("GOAL REACHED!");
        EndEpisode();
    }

    // ============================================================
    // GOAL REACHED CALLBACK  (called by GoalTrigger, kept for compatibility)
    // ============================================================

    /// <summary>
    /// Legacy callback — kept so GoalTrigger.cs compiles cleanly.
    /// Not used in the 5-gate corridor layout.
    /// </summary>
    public void OnGoalReached(bool isGreenGoal)
    {
        bool correct = (isGreenGoal == blockRandomizer.IsGreen);
        AddReward(correct ? 1f : -0.1f);
        NotifyScore(correct ? "GOAL REACHED!" : "WRONG GOAL");
        EndEpisode();
    }

    // ============================================================
    // SCORE HELPER
    // ============================================================

    private void NotifyScore(string status)
    {
        if (scoreDisplay != null)
            scoreDisplay.RecordEpisode(GetCumulativeReward(), gatesPassedThisEpisode, status);
    }

    // ============================================================
    // ML-AGENTS: HEURISTIC  (keyboard control for testing)
    // ============================================================

    /// <summary>
    /// Heuristic is only active when BehaviorType = Heuristic Only (manual testing).
    /// During normal training (BehaviorType = Default) this method is never called.
    ///
    /// NOTE: Unity Input.GetKey() requires "Input Manager (Old)" to be selected in
    /// Project Settings > Player > Active Input Handling. This method avoids that
    /// dependency so no Input System errors are thrown during training runs.
    /// </summary>
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Default: idle. Manual WASD control requires Legacy Input Manager enabled
        // in Player Settings. Safe to leave as-is for autonomous training.
        actionsOut.DiscreteActions.Array[actionsOut.DiscreteActions.Offset] = 0;
    }

    // ============================================================
    // RESET HELPER  (called by HallwayManager)
    // ============================================================

    /// <summary>
    /// Resets physics state and repositions the agent.
    /// Separated from OnEpisodeBegin so HallwayManager owns spawn logic.
    /// </summary>
    public void ResetAgent(Vector3 localPosition, Quaternion localRotation)
    {
        rb.linearVelocity        = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Set transform first (works in local space for child objects inside
        // a training area parent), then sync the Rigidbody to world coordinates.
        // BUG FIX: Without explicitly setting rb.position / rb.rotation, the
        // physics engine can hold a stale cached position for one frame, causing
        // the agent to flicker or trigger goal zones it was just reset away from.
        transform.localPosition = localPosition;
        transform.localRotation = localRotation;
        rb.position = transform.position;
        rb.rotation = transform.rotation;
    }
}
