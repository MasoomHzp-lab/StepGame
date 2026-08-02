using UnityEngine;

public class CharacterStepMover : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Animator animator;

    [Tooltip("The Transform that physically moves between stairs.")]
    [SerializeField]
    private Transform characterRoot;

    [Header("Stable Landing Coordinates")]
    [Tooltip("Moves the character slightly deeper onto each stair.")]
    [SerializeField]
    private float landingDepthOffset = 0f;

    [Tooltip(
        "Extra vertical distance above the stair surface. " +
        "Keep this at the value that visually places the shoes correctly."
    )]
    [SerializeField, Min(0f)]
    private float extraLandingClearance = 0.4f;

    [Tooltip("Fine adjustment across the width of the stairs.")]
    [SerializeField]
    private float sideOffsetCorrection = 0f;

    [Tooltip(
        "Optional override for the first forward movement. " +
        "Zero automatically uses the target stair depth."
    )]
    [SerializeField, Min(0f)]
    private float firstStepAdvanceOverride = 0f;

    [Tooltip("Prevents the character root from moving downward while climbing.")]
    [SerializeField]
    private bool preventDownwardMovement = true;

    [Header("Initial Surface Detection")]
    [Tooltip("Raycast start height above the character root.")]
    [SerializeField, Min(0.1f)]
    private float supportRaycastHeight = 1f;

    [Tooltip("Maximum distance used to find the surface below the character.")]
    [SerializeField, Min(0.5f)]
    private float supportRaycastDistance = 4f;

    [SerializeField]
    private LayerMask groundLayers = ~0;

    [Header("Runtime State - Read Only")]
    [SerializeField]
    private bool movePrepared;

    [SerializeField]
    private bool stableOffsetsCaptured;

    [SerializeField]
    private Vector3 startRootPosition;

    [SerializeField]
    private Vector3 targetRootPosition;

    [SerializeField]
    private Vector3 stablePlanarOffset;

    [SerializeField]
    private float rootHeightAboveInitialSurface;

    [SerializeField]
    private Vector3 initialRootPosition;

    [SerializeField]
    private Quaternion initialRootRotation;

    private bool initialPositionCaptured;

    private void Awake()
    {
        if (!InitializeReferences())
        {
            enabled = false;
            return;
        }

        CaptureInitialPosition();

        animator.applyRootMotion = false;
    }

    /// <summary>
    /// Prepares a stable root movement.
    /// Lead movement uses fixed stair coordinates.
    /// Join movement keeps the root completely stationary.
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

        if (climbWorldDirection.sqrMagnitude < 0.001f)
        {
            Debug.LogError(
                "Climb direction cannot be zero.",
                this
            );

            return false;
        }

        startRootPosition = characterRoot.position;

        /*
         * The second foot only joins the lead foot.
         * The character has already reached the stair,
         * so its root must remain at exactly the same coordinates.
         */
        if (keepRootPosition)
        {
            targetRootPosition = startRootPosition;
            movePrepared = true;

            Debug.Log(
                $"Stationary join prepared | " +
                $"Foot: {GetFootName(foot)} | " +
                $"Root: {targetRootPosition}",
                this
            );

            return true;
        }

        Vector3 climbDirection =
            Vector3.ProjectOnPlane(
                climbWorldDirection,
                Vector3.up
            ).normalized;

        if (climbDirection.sqrMagnitude < 0.001f)
        {
            Debug.LogError(
                "The horizontal climb direction cannot be zero.",
                this
            );

            return false;
        }

        Vector3 sideDirection =
            Vector3.Cross(
                Vector3.up,
                climbDirection
            ).normalized;

        if (!stableOffsetsCaptured)
        {
            CaptureStableOffsets(
                targetStep,
                climbDirection
            );
        }

        /*
         * The root target is calculated from the stair itself,
         * not from the animated ankle. Therefore right, left and
         * mirrored clips cannot introduce coordinate drift.
         */
        Vector3 targetPosition =
            targetStep.bounds.center +
            stablePlanarOffset;

        targetPosition +=
            climbDirection *
            landingDepthOffset;

        targetPosition +=
            sideDirection *
            sideOffsetCorrection;

        targetPosition.y =
            targetStep.bounds.max.y +
            rootHeightAboveInitialSurface +
            extraLandingClearance;

        if (preventDownwardMovement)
        {
            targetPosition.y =
                Mathf.Max(
                    targetPosition.y,
                    startRootPosition.y
                );
        }

        targetRootPosition =
            targetPosition;

        movePrepared = true;

        Debug.Log(
            $"Stable character move prepared | " +
            $"Foot: {GetFootName(foot)} | " +
            $"Target step: {targetStep.name} | " +
            $"Start root: {startRootPosition} | " +
            $"Target root: {targetRootPosition} | " +
            $"Planar offset: {stablePlanarOffset}",
            this
        );

        return true;
    }

    /// <summary>
    /// Captures one permanent offset between the character and the stair path.
    /// This offset is reused for every stair.
    /// </summary>
    private void CaptureStableOffsets(
        BoxCollider firstTargetStep,
        Vector3 climbDirection
    )
    {
        float automaticAdvance =
            GetStairLengthAlongDirection(
                firstTargetStep,
                climbDirection
            );

        float firstAdvance =
            firstStepAdvanceOverride > 0f
                ? firstStepAdvanceOverride
                : automaticAdvance;

        Vector3 virtualPreviousStepCenter =
            firstTargetStep.bounds.center -
            climbDirection *
            firstAdvance;

        stablePlanarOffset =
            Vector3.ProjectOnPlane(
                characterRoot.position -
                virtualPreviousStepCenter,
                Vector3.up
            );

        float initialSurfaceHeight =
            FindSurfaceHeightBelow(
                characterRoot.position
            );

        rootHeightAboveInitialSurface =
            characterRoot.position.y -
            initialSurfaceHeight;

        /*
         * Protect against a bad raycast result.
         */
        if (rootHeightAboveInitialSurface < -0.5f ||
            rootHeightAboveInitialSurface > 2f)
        {
            Debug.LogWarning(
                "Initial root height measurement was invalid. " +
                "Using zero as the base surface offset.",
                this
            );

            rootHeightAboveInitialSurface = 0f;
        }

        stableOffsetsCaptured = true;

        Debug.Log(
            $"Stable stair offsets captured | " +
            $"First advance: {firstAdvance:F3} | " +
            $"Planar offset: {stablePlanarOffset} | " +
            $"Base root height: {rootHeightAboveInitialSurface:F3}",
            this
        );
    }

    private float GetStairLengthAlongDirection(
        BoxCollider step,
        Vector3 direction
    )
    {
        Vector3 absoluteDirection =
            new Vector3(
                Mathf.Abs(direction.x),
                Mathf.Abs(direction.y),
                Mathf.Abs(direction.z)
            );

        float length =
            Vector3.Dot(
                step.bounds.size,
                absoluteDirection
            );

        return Mathf.Max(
            length,
            0.01f
        );
    }

    private float FindSurfaceHeightBelow(
        Vector3 worldPosition
    )
    {
        Vector3 rayOrigin =
            worldPosition +
            Vector3.up *
            supportRaycastHeight;

        RaycastHit[] hits =
            Physics.RaycastAll(
                rayOrigin,
                Vector3.down,
                supportRaycastHeight +
                supportRaycastDistance,
                groundLayers,
                QueryTriggerInteraction.Ignore
            );

        bool foundSurface = false;
        float highestSurface =
            float.NegativeInfinity;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null)
            {
                continue;
            }

            if (hit.transform == characterRoot ||
                hit.transform.IsChildOf(characterRoot))
            {
                continue;
            }

            if (hit.point.y >
                worldPosition.y + 0.05f)
            {
                continue;
            }

            if (!foundSurface ||
                hit.point.y > highestSurface)
            {
                highestSurface = hit.point.y;
                foundSurface = true;
            }
        }

        if (!foundSurface)
        {
            Debug.LogWarning(
                "No support surface was found below the character. " +
                "The current root height will be used.",
                this
            );

            return worldPosition.y;
        }

        return highestSurface;
    }

    /// <summary>
    /// Moves the root smoothly to the fixed stair coordinate.
    /// </summary>
    public void ApplyProgress(
        float normalizedProgress
    )
    {
        if (!movePrepared ||
            characterRoot == null)
        {
            return;
        }

        float progress =
            Mathf.Clamp01(
                normalizedProgress
            );

        float smoothProgress =
            Mathf.SmoothStep(
                0f,
                1f,
                progress
            );

        characterRoot.position =
            Vector3.Lerp(
                startRootPosition,
                targetRootPosition,
                smoothProgress
            );
    }

    public void CompleteMove()
    {
        if (!movePrepared ||
            characterRoot == null)
        {
            return;
        }

        characterRoot.position =
            targetRootPosition;

        movePrepared = false;

        Debug.Log(
            $"Character movement completed at {targetRootPosition}.",
            this
        );
    }

    public void CancelMove()
    {
        if (!movePrepared ||
            characterRoot == null)
        {
            return;
        }

        characterRoot.position =
            startRootPosition;

        movePrepared = false;

        Debug.LogWarning(
            "Character movement was cancelled.",
            this
        );
    }

    public void ResetCharacterPosition()
    {
        if (!InitializeReferences())
        {
            return;
        }

        CaptureInitialPosition();

        movePrepared = false;
        stableOffsetsCaptured = false;
        stablePlanarOffset = Vector3.zero;
        rootHeightAboveInitialSurface = 0f;

        characterRoot.position =
            initialRootPosition;

        characterRoot.rotation =
            initialRootRotation;

        animator.applyRootMotion = false;

        Debug.Log(
            "Character position and stable stair offsets were reset.",
            this
        );
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

    private void CaptureInitialPosition()
    {
        if (initialPositionCaptured ||
            characterRoot == null)
        {
            return;
        }

        initialRootPosition =
            characterRoot.position;

        initialRootRotation =
            characterRoot.rotation;

        initialPositionCaptured = true;
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
