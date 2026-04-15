using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

/// <summary>
/// NavigationAgent — Navigates a 20x20 arena from a fixed spawn to a goal
/// while avoiding 20 solid obstacle cubes of varying size.
///
/// Observations (27 total):
///   [0-2]  Goal relative direction (normalized x, z) + normalized distance
///   [3-26] 8 world-aligned raycasts x 3 binary flags (isWall, isObstacle, isGoal)
///
/// Actions (1 discrete branch, 5 values):
///   0 = Idle (stop)   1 = North (+Z)   2 = South (-Z)
///   3 = West (-X)     4 = East (+X)
///
/// Rewards:
///   +1.0 on reaching goal (trigger enter)
///   -0.2 on colliding with an obstacle (episode ends)
///   -0.001 per step (time penalty)
///
/// Guaranteed path: the scene builder always leaves a clear L-shaped corridor
/// (left strip x <= -5, top strip z >= 5) so the agent can ALWAYS reach the goal.
/// The LSTM is not needed here — navigation is a reactive task.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class NavigationAgent : Agent
{
    // ============================================================
    // INSPECTOR FIELDS
    // ============================================================

    [Header("Scene References")]
    [Tooltip("The goal transform the agent must reach.")]
    [SerializeField] private Transform goal;

    [Header("Movement")]
    [Tooltip("World-space units per second when a move action is active.")]
    [SerializeField] private float moveSpeed = 4f;

    [Header("Raycast Observations")]
    [Tooltip("Max range of each observation ray.")]
    [SerializeField] private float rayDistance = 14f;

    [Tooltip("Layers the raycasts detect.")]
    [SerializeField] private LayerMask raycastLayers = ~0;

    // ============================================================
    // PRIVATE STATE
    // ============================================================

    private Rigidbody rb;
    private Vector3   startLocalPosition;

    // 8 world-aligned compass directions (degrees from +Z, clockwise)
    private static readonly float[] RayAngles =
        { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

    private const string TAG_WALL     = "Wall";
    private const string TAG_OBSTACLE = "Obstacle";
    private const string TAG_GOAL     = "NavGoal";

    // ============================================================
    // ML-AGENTS: INITIALIZATION
    // ============================================================

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        startLocalPosition = transform.localPosition;

        if (goal == null)
            Debug.LogError("[NavigationAgent] goal is not assigned.", this);

        rb.isKinematic = false;
        rb.useGravity  = true;

        // Keep the agent flat: freeze Y position and all rotation axes.
        // Y rotation frozen because we move in world-space cardinal directions,
        // not relative to the agent's facing.
        rb.constraints = RigidbodyConstraints.FreezePositionY
                       | RigidbodyConstraints.FreezeRotationX
                       | RigidbodyConstraints.FreezeRotationY
                       | RigidbodyConstraints.FreezeRotationZ;
    }

    // ============================================================
    // ML-AGENTS: EPISODE BEGIN
    // ============================================================

    public override void OnEpisodeBegin()
    {
        rb.linearVelocity  = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        transform.localPosition = startLocalPosition;
        rb.position = transform.position;
        rb.rotation = transform.rotation;
    }

    // ============================================================
    // ML-AGENTS: OBSERVATIONS  (27 values total)
    // ============================================================

    /// <summary>
    /// 3 goal observations + 8 rays x 3 binary flags = 27 total.
    ///
    /// Behavior Parameters -> Space Size must be 27.
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        // ── Goal direction and distance (3 values) ───────────────────
        Vector3 toGoal = (goal != null)
            ? goal.position - transform.position
            : Vector3.zero;

        float dist = toGoal.magnitude;
        sensor.AddObservation(dist > 0.001f ? toGoal.x / dist : 0f); // normalized X
        sensor.AddObservation(dist > 0.001f ? toGoal.z / dist : 0f); // normalized Z
        sensor.AddObservation(Mathf.Clamp01(dist / 28f));             // normalized distance

        // ── 8 world-aligned raycasts (24 values) ─────────────────────
        Vector3 origin = transform.position + Vector3.up * 0.3f;

        foreach (float angle in RayAngles)
        {
            // +Z is north; rotate clockwise by `angle` degrees
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;

            float isWall = 0f, isObstacle = 0f, isGoal = 0f;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, rayDistance,
                                raycastLayers, QueryTriggerInteraction.Collide))
            {
                if      (hit.collider.CompareTag(TAG_WALL))     isWall     = 1f;
                else if (hit.collider.CompareTag(TAG_OBSTACLE)) isObstacle = 1f;
                else if (hit.collider.CompareTag(TAG_GOAL))     isGoal     = 1f;

                Debug.DrawRay(origin, dir * hit.distance, Color.red,   Time.fixedDeltaTime);
            }
            else
            {
                Debug.DrawRay(origin, dir * rayDistance, Color.green, Time.fixedDeltaTime);
            }

            sensor.AddObservation(isWall);
            sensor.AddObservation(isObstacle);
            sensor.AddObservation(isGoal);
        }
    }

    // ============================================================
    // ML-AGENTS: ACTIONS
    // ============================================================

    /// <summary>
    /// Action 0 = Idle   Action 1 = North   Action 2 = South
    /// Action 3 = West   Action 4 = East
    ///
    /// Behavior Parameters -> Discrete Branches: 1 branch of size 5.
    /// </summary>
    public override void OnActionReceived(ActionBuffers actions)
    {
        int action = actions.DiscreteActions[0];

        Vector3 move = action switch
        {
            1 => Vector3.forward,
            2 => Vector3.back,
            3 => Vector3.left,
            4 => Vector3.right,
            _ => Vector3.zero
        };

        rb.linearVelocity = move * moveSpeed;
        AddReward(-0.001f); // step penalty — reach goal quickly
    }

    // ============================================================
    // ML-AGENTS: HEURISTIC  (WASD manual control for testing)
    // ============================================================

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Default idle. Legacy Input.GetKey() removed to prevent the
        // "Input System package" InvalidOperationException during training.
        actionsOut.DiscreteActions.Array[actionsOut.DiscreteActions.Offset] = 0;
    }

    // ============================================================
    // COLLISION / TRIGGER CALLBACKS
    // ============================================================

    /// <summary>Goal has isTrigger=true — agent walks through it to collect reward.</summary>
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(TAG_GOAL))
        {
            AddReward(1.0f);
            EndEpisode();
        }
    }

    /// <summary>Obstacles are solid — agent bumps into them and episode ends.</summary>
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag(TAG_OBSTACLE))
        {
            AddReward(-0.2f);
            EndEpisode();
        }
    }
}
