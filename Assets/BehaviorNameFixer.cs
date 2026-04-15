using UnityEngine;
using Unity.MLAgents.Policies;

/// <summary>
/// BehaviorNameFixer — sets BehaviorName AND VectorObservationSize in Awake()
/// so the correct brain spec reaches the Python trainer.
///
/// WHY Awake() and not Initialize():
///   Unity's event order per frame is:  Awake → OnEnable → Start → FixedUpdate.
///   BehaviorParameters.OnEnable() is where the brain spec (including
///   VectorObservationSize and BehaviorName) is registered with the Academy.
///   Agent.Initialize() is called from LazyInitialize() which runs on the
///   first FixedUpdate — much later, AFTER OnEnable().
///
///   Setting either value in Initialize() is therefore too late: the brain
///   spec has already been sent to Python with the wrong (serialized) values.
///   Setting both values here in Awake() at order -500 ensures they are
///   correct BEFORE BehaviorParameters.OnEnable() reads them.
///
/// WHY the disconnect ("Communicator has exited") was happening:
///   The serialized VectorObservationSize was 1 (Unity default). Python
///   allocated a 1-slot buffer. Unity then sent 31 observations per step.
///   That size mismatch caused Python to close the socket immediately.
///
/// NOTE: No [RequireComponent(typeof(BehaviorParameters))] here.
///   That attribute would cause AddComponent<BehaviorNameFixer>() to
///   auto-add a second BehaviorParameters, breaking ML-Agents entirely.
///   HallwaySceneBuilder adds the one BehaviorParameters manually.
/// </summary>
[DefaultExecutionOrder(-500)]
public class BehaviorNameFixer : MonoBehaviour
{
    [Tooltip("Must match the key under 'behaviors:' in hallway_config.yaml")]
    [SerializeField] public string targetBehaviorName = "HallwayAgent";

    [Tooltip("6 rays × 5 flags + 1 lateral position = 31. Must match CollectObservations output.")]
    [SerializeField] public int vectorObservationSize = 31;

    private void Awake()
    {
        BehaviorParameters bp = GetComponent<BehaviorParameters>();
        if (bp == null) return;

        // ── Fix BehaviorName ─────────────────────────────────────────
        if (bp.BehaviorName != targetBehaviorName)
        {
            Debug.Log($"[BehaviorNameFixer] BehaviorName: '{bp.BehaviorName}' → '{targetBehaviorName}'");
            bp.BehaviorName = targetBehaviorName;
        }

        // ── Fix VectorObservationSize ────────────────────────────────
        // THIS is what was causing "Communicator has exited".
        // Unity serializes VectorObservationSize as 1 (default). Python
        // allocates a 1-slot buffer. Unity then sends 30 values. Mismatch
        // → Python closes the socket. Setting it here in Awake() means
        // BehaviorParameters.OnEnable() reads 30 when it builds the brain
        // spec — so Python and Unity agree from the very first step.
        if (bp.BrainParameters.VectorObservationSize != vectorObservationSize)
        {
            Debug.Log($"[BehaviorNameFixer] VectorObservationSize: " +
                      $"{bp.BrainParameters.VectorObservationSize} → {vectorObservationSize}");
            bp.BrainParameters.VectorObservationSize = vectorObservationSize;
        }

        Debug.Log($"[BehaviorNameFixer] Brain spec ready — " +
                  $"name='{targetBehaviorName}'  obsSize={vectorObservationSize}");
    }
}
