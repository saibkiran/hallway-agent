using UnityEngine;

/// <summary>
/// GateTrigger — Attached to the invisible detection plane that spans the
/// full corridor width just past each gate's two coloured pillars.
///
/// When the agent crosses the plane, GateTrigger checks which side of the
/// corridor (left x less than 0, right x greater than 0) the agent is on
/// and calls HallwayAgent.OnGatePassed with the correct colour for that side.
///
/// One component per gate (NOT per pillar). The bool leftPillarIsGreen is
/// set by HallwaySceneBuilder at scene-build time.
/// </summary>
public class GateTrigger : MonoBehaviour
{
    [Tooltip("True if the LEFT pillar (x less than 0) of this gate is green.")]
    [SerializeField] public bool leftPillarIsGreen;

    // Latch so one crossing = one callback even if physics fires twice
    private bool triggered;

    public void ResetTrigger() { triggered = false; }

    private void OnTriggerEnter(Collider other)
    {
        if (triggered) return;

        HallwayAgent agent = other.GetComponent<HallwayAgent>();
        if (agent == null) return;

        triggered = true;

        // Determine which half of the corridor the agent crossed through
        bool onLeftSide  = other.transform.position.x < 0f;
        bool passedGreen = onLeftSide ? leftPillarIsGreen : !leftPillarIsGreen;

        agent.OnGatePassed(passedGreen);
    }
}
