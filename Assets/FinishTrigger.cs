using UnityEngine;

/// <summary>
/// FinishTrigger — Attached to the invisible detection plane at the far end
/// of the corridor, past all 15 gates.
///
/// When the agent crosses this plane it has successfully navigated every gate.
/// Calls HallwayAgent.OnFinishReached() for the terminal +1.0 reward.
///
/// One component per scene. Latched so double-fire is impossible.
/// </summary>
public class FinishTrigger : MonoBehaviour
{
    private bool triggered;

    public void ResetTrigger() { triggered = false; }

    private void OnTriggerEnter(Collider other)
    {
        if (triggered) return;

        HallwayAgent agent = other.GetComponent<HallwayAgent>();
        if (agent == null) return;

        triggered = true;
        agent.OnFinishReached();
    }
}
