using UnityEngine;

/// <summary>
/// BlockRandomizer — Controls the colored block in the hallway.
///
/// Each episode it randomly picks green or red (50/50), then:
///   1. Swaps the Renderer material so the block is visually the right color.
///   2. Changes the GameObject's tag to "GreenBlock" or "RedBlock" so the
///      agent's raycasts can detect WHICH color block was observed.
///
/// Why change the tag instead of exposing the color directly?
///   The HallwayAgent must PERCEIVE the color through its raycast sensor,
///   just like a real agent perceiving its environment. There is no shortcut
///   "isGreenCorrect" observation — the agent sees "GreenBlock" or "RedBlock"
///   via raycast, and the LSTM layer must remember that signal over time.
///
/// Scene setup:
///   - Attach to the colored block GameObject.
///   - Assign Green Material and Red Material in the Inspector.
///   - The default tag can be either "GreenBlock" or "RedBlock" — it will
///     be overwritten at the start of every episode.
///   - Ensure both "GreenBlock" and "RedBlock" tags exist in
///     Edit > Project Settings > Tags & Layers.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class BlockRandomizer : MonoBehaviour
{
    // ============================================================
    // INSPECTOR FIELDS
    // ============================================================

    [Header("Materials")]
    [Tooltip("Material used when block is green (agent should go to GreenGoal).")]
    [SerializeField] private Material greenMaterial;

    [Tooltip("Material used when block is red (agent should go to RedGoal).")]
    [SerializeField] private Material redMaterial;

    // ============================================================
    // PUBLIC PROPERTY
    // ============================================================

    /// <summary>
    /// True when the current episode's block is green.
    /// Read by HallwayAgent.OnGoalReached() to evaluate correctness.
    /// </summary>
    public bool IsGreen { get; private set; }

    // ============================================================
    // PRIVATE FIELDS
    // ============================================================

    private Renderer blockRenderer;

    // ============================================================
    // UNITY LIFECYCLE
    // ============================================================

    private void Awake()
    {
        blockRenderer = GetComponent<Renderer>();
    }

    // ============================================================
    // PUBLIC API
    // ============================================================

    /// <summary>
    /// Randomizes the block for a new episode.
    /// Called by HallwayManager.ResetEnvironment() at the start of each episode.
    ///
    /// Steps:
    ///   1. Randomly decide green or red (uniform 50/50).
    ///   2. Apply the matching material.
    ///   3. Update the tag so raycasts from HallwayAgent detect the right color.
    /// </summary>
    public void RandomizeBlock()
    {
        if (greenMaterial == null || redMaterial == null)
        {
            Debug.LogError("[BlockRandomizer] Green or Red material is not assigned.", this);
            return;
        }

        IsGreen = (Random.value > 0.5f);

        // BUG FIX: sharedMaterial assigns the asset reference directly.
        // The old `.material` property creates a NEW material instance each call.
        // Over a 500k-step training run that means ~10k+ orphaned instances piling
        // up in memory, slowing GC and eventually causing allocation spikes.
        blockRenderer.sharedMaterial = IsGreen ? greenMaterial : redMaterial;

        // Tag update — enables raycast color detection without exposing IsGreen
        // to the observation vector (which would trivialize the memory task).
        gameObject.tag = IsGreen ? "GreenBlock" : "RedBlock";
    }
}
