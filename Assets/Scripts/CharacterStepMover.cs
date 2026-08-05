using UnityEngine;

/// <summary>
/// Keeps the character GameObject completely fixed in world space.
/// The Animator is allowed to move only the humanoid bones.
/// Stair/world progression must be handled by moving the environment,
/// not by translating the character root.
/// </summary>
public class CharacterStepMover : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Animator animator;

    [Tooltip("The transform that must remain completely fixed.")]
    [SerializeField]
    private Transform characterRoot;

    [Header("Root Lock")]
    [Tooltip("Locks the root position every frame.")]
    [SerializeField]
    private bool lockPosition = true;

    [Tooltip("Locks the root rotation every frame.")]
    [SerializeField]
    private bool lockRotation = true;

    [Header("Runtime State - Read Only")]
    [SerializeField]
    private Vector3 lockedWorldPosition;

    [SerializeField]
    private Quaternion lockedWorldRotation;

    [SerializeField]
    private bool rootCaptured;

    private void Awake()
    {
        if (!InitializeReferences())
        {
            enabled = false;
            return;
        }

        CaptureCurrentRootTransform();

        animator.applyRootMotion = false;
    }

    private void LateUpdate()
    {
        EnforceRootLock();
    }

    /// <summary>
    /// Kept for compatibility with StairGameController.
    /// No character translation is prepared.
    /// </summary>
    public bool PrepareMove(
        StairGameController.FootSide foot,
        BoxCollider targetStep,
        string sampleStateName,
        Vector3 climbWorldDirection,
        bool keepRootPosition = false
    )
    {
        if (!InitializeReferences())
        {
            return false;
        }

        if (targetStep == null)
        {
            Debug.LogError(
                "Character Step Mover received a null target step.",
                this
            );

            return false;
        }

        if (string.IsNullOrWhiteSpace(sampleStateName))
        {
            Debug.LogError(
                "The animation state name is empty.",
                this
            );

            return false;
        }

        /*
         * Do not move or sample the character root.
         * Only the Animator state will animate the humanoid bones.
         */
        EnforceRootLock();

        Debug.Log(
            $"Fixed-root movement prepared | " +
            $"Foot: {GetFootName(foot)} | " +
            $"Animation: {sampleStateName} | " +
            $"Target: {targetStep.name}",
            this
        );

        return true;
    }

    /// <summary>
    /// Intentionally does not move the character root.
    /// </summary>
    public void ApplyProgress(float normalizedProgress)
    {
        EnforceRootLock();
    }

    /// <summary>
    /// Keeps the root at its fixed world position.
    /// </summary>
    public void CompleteMove()
    {
        EnforceRootLock();

        Debug.Log(
            $"Character root remained fixed at {lockedWorldPosition}.",
            this
        );
    }

    public void CancelMove()
    {
        EnforceRootLock();

        Debug.LogWarning(
            "Character animation movement was cancelled. " +
            "The root remained fixed.",
            this
        );
    }

    /// <summary>
    /// Restores the originally captured world position and rotation.
    /// </summary>
    public void ResetCharacterPosition()
    {
        if (!InitializeReferences())
        {
            return;
        }

        if (!rootCaptured)
        {
            CaptureCurrentRootTransform();
        }

        EnforceRootLock();

        animator.applyRootMotion = false;

        Debug.Log(
            "Character root was reset to its fixed transform.",
            this
        );
    }

    /// <summary>
    /// Use this from the component context menu after manually
    /// placing the character in the desired starting position.
    /// </summary>
    [ContextMenu("Capture Current Root Transform")]
    public void CaptureCurrentRootTransform()
    {
        if (!InitializeReferences())
        {
            return;
        }

        lockedWorldPosition =
            characterRoot.position;

        lockedWorldRotation =
            characterRoot.rotation;

        rootCaptured = true;

        Debug.Log(
            $"Character root transform captured | " +
            $"Position: {lockedWorldPosition}",
            this
        );
    }

    private void EnforceRootLock()
    {
        if (!rootCaptured ||
            characterRoot == null)
        {
            return;
        }

        if (lockPosition)
        {
            characterRoot.position =
                lockedWorldPosition;
        }

        if (lockRotation)
        {
            characterRoot.rotation =
                lockedWorldRotation;
        }
    }

    private bool InitializeReferences()
    {
        if (animator == null)
        {
            animator =
                GetComponent<Animator>();
        }

        if (animator == null)
        {
            animator =
                GetComponentInParent<Animator>();
        }

        if (animator == null)
        {
            Debug.LogError(
                "Character Step Mover could not find an Animator.",
                this
            );

            return false;
        }

        if (!animator.isHuman)
        {
            Debug.LogError(
                "Character Step Mover requires a Humanoid Animator.",
                this
            );

            return false;
        }

        if (characterRoot == null)
        {
            characterRoot =
                animator.transform;
        }

        if (characterRoot == null)
        {
            Debug.LogError(
                "Character Root has not been assigned.",
                this
            );

            return false;
        }

        return true;
    }

    private string GetFootName(
        StairGameController.FootSide foot
    )
    {
        return foot ==
               StairGameController.FootSide.Right
            ? "right"
            : "left";
    }
}
