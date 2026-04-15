using UnityEngine;

/// <summary>
/// CameraFollow — Smoothly tracks the agent through the corridor.
///
/// Attach to the Main Camera. Wire the agent's Transform in the Inspector
/// (done automatically by HallwaySceneBuilder).
///
/// The camera stays above and slightly behind the agent, always looking
/// at it. This keeps the colour indicator block, the current gate, and
/// the upcoming gates all visible as the agent navigates.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Tooltip("The Transform to follow (the agent).")]
    [SerializeField] public Transform target;

    [Tooltip("World-space offset from the target position.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 10f, -8f);

    [Tooltip("Smoothing speed — lower = more lag, higher = snappier.")]
    [SerializeField] private float smoothSpeed = 4f;

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);

        // Always look at the agent (slightly above ground level)
        transform.LookAt(target.position + Vector3.up * 0.5f);
    }
}
