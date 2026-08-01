using UnityEngine;

public class CharacterStepMover : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Animator animator;

    [Tooltip("The Transform that physically moves between steps.")]
    [SerializeField]
    private Transform characterRoot;

    [Header("Landing Settings")]
    [Tooltip("Moves the landing point deeper onto the target step.")]
    [SerializeField]
    private float landingDepthOffset = 0f;

    [Tooltip("Fallback ankle height above the surface.")]
    [SerializeField, Min(0f)]
    private float defaultAnkleHeight = 0.08f;

    [Tooltip("Raycast start height above the ankle.")]
    [SerializeField, Min(0.1f)]
    private float ankleRaycastHeight = 0.4f;

    [Tooltip("Maximum downward raycast distance.")]
    [SerializeField, Min(0.5f)]
    private float ankleRaycastDistance = 2f;

    [SerializeField]
    private LayerMask groundLayers = ~0;

    [Header("Runtime State - Read Only")]
    [SerializeField]
    private bool movePrepared;

    [SerializeField]
    private Vector3 startRootPosition;

    [SerializeField]
    private Vector3 targetRootPosition;

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
    /// Calculates the target root position before the real animation starts.
    /// The final frame of the requested animation is sampled temporarily.
    /// </summary>
    public bool PrepareMove(
        StairGameController.FootSide foot,
        BoxCollider targetStep,
        string sampleStateName,
        Vector3 climbWorldDirection
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

        Transform activeAnkle =
            GetFootBone(foot);

        if (activeAnkle == null)
        {
            Debug.LogError(
                $"The {GetFootName(foot)} foot bone could not be found.",
                this
            );

            return false;
        }

        climbWorldDirection.Normalize();

        Vector3 sideDirection =
            Vector3.Cross(
                Vector3.up,
                climbWorldDirection
            ).normalized;

        if (sideDirection.sqrMagnitude < 0.001f)
        {
            sideDirection =
                characterRoot.right;
        }

        startRootPosition =
            characterRoot.position;

        Quaternion savedRootRotation =
            characterRoot.rotation;

        float savedAnimatorSpeed =
            animator.speed;

        bool savedApplyRootMotion =
            animator.applyRootMotion;

        /*
         * Save the exact Animator state currently visible.
         * This may be Idle, LeadStep or JoinStep.
         */
        AnimatorStateInfo savedStateInfo =
            animator.GetCurrentAnimatorStateInfo(0);

        int savedStateHash =
            savedStateInfo.fullPathHash;

        float savedNormalizedTime =
            savedStateInfo.normalizedTime;

        float ankleHeightAboveSurface =
            GetAnkleHeightAboveSurface(
                activeAnkle
            );

        /*
         * Preserve the left/right position of the active foot.
         * This prevents both feet from being placed at the exact
         * same lateral coordinate.
         */
        float lateralFootOffset =
            Vector3.Dot(
                activeAnkle.position -
                characterRoot.position,
                sideDirection
            );

        Vector3 targetAnklePosition =
            new Vector3(
                targetStep.bounds.center.x,
                targetStep.bounds.max.y +
                ankleHeightAboveSurface,
                targetStep.bounds.center.z
            );

        targetAnklePosition +=
            climbWorldDirection *
            landingDepthOffset;

        targetAnklePosition +=
            sideDirection *
            lateralFootOffset;

        /*
         * Temporarily sample the final frame of the requested animation.
         * This determines the ankle's final offset relative to the root.
         */
        animator.speed = 1f;
        animator.applyRootMotion = false;

        animator.Play(
            sampleStateName,
            0,
            0.999f
        );

        animator.Update(0f);

        Vector3 finalAnkleOffset =
            activeAnkle.position -
            characterRoot.position;

        targetRootPosition =
            targetAnklePosition -
            finalAnkleOffset;

        /*
         * Restore the root before restoring the previous animation pose.
         */
        characterRoot.position =
            startRootPosition;

        characterRoot.rotation =
            savedRootRotation;

        /*
         * Restore the exact pose that was active before sampling.
         * This is important for the transition between Lead and Join.
         */
        if (savedStateHash != 0)
        {
            animator.Play(
                savedStateHash,
                0,
                savedNormalizedTime
            );

            animator.Update(0f);
        }

        characterRoot.position =
            startRootPosition;

        characterRoot.rotation =
            savedRootRotation;

        animator.speed =
            savedAnimatorSpeed;

        animator.applyRootMotion =
            savedApplyRootMotion;

        movePrepared = true;

        Debug.Log(
            $"Character move prepared | " +
            $"Foot: {GetFootName(foot)} | " +
            $"Target step: {targetStep.name} | " +
            $"Start root: {startRootPosition} | " +
            $"Target root: {targetRootPosition}",
            this
        );

        return true;
    }

    /// <summary>
    /// Moves the character root smoothly between the prepared positions.
    /// Value must be between 0 and 1.
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

        float clampedProgress =
            Mathf.Clamp01(
                normalizedProgress
            );

        float smoothProgress =
            Mathf.SmoothStep(
                0f,
                1f,
                clampedProgress
            );

        characterRoot.position =
            Vector3.Lerp(
                startRootPosition,
                targetRootPosition,
                smoothProgress
            );
    }

    /// <summary>
    /// Places the character exactly at the calculated target position.
    /// </summary>
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

    /// <summary>
    /// Cancels the current movement and restores the starting position.
    /// </summary>
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

    /// <summary>
    /// Returns the character to the position captured at scene start.
    /// </summary>
    public void ResetCharacterPosition()
    {
        if (!InitializeReferences())
        {
            return;
        }

        CaptureInitialPosition();

        movePrepared = false;

        characterRoot.position =
            initialRootPosition;

        characterRoot.rotation =
            initialRootRotation;

        animator.applyRootMotion = false;

        Debug.Log(
            "Character position was reset.",
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

    private Transform GetFootBone(
        StairGameController.FootSide foot
    )
    {
        HumanBodyBones footBone =
            foot ==
            StairGameController.FootSide.Right
                ? HumanBodyBones.RightFoot
                : HumanBodyBones.LeftFoot;

        return animator.GetBoneTransform(
            footBone
        );
    }

    /// <summary>
    /// Calculates the current ankle distance above the surface below it.
    /// Character colliders are ignored.
    /// </summary>
    private float GetAnkleHeightAboveSurface(
        Transform ankle
    )
    {
        if (ankle == null)
        {
            return defaultAnkleHeight;
        }

        Vector3 rayOrigin =
            ankle.position +
            Vector3.up *
            ankleRaycastHeight;

        RaycastHit[] hits =
            Physics.RaycastAll(
                rayOrigin,
                Vector3.down,
                ankleRaycastHeight +
                ankleRaycastDistance,
                groundLayers,
                QueryTriggerInteraction.Ignore
            );

        float closestDistance =
            float.PositiveInfinity;

        RaycastHit closestHit =
            new RaycastHit();

        bool foundSurface = false;

        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == null)
            {
                continue;
            }

            /*
             * Ignore all colliders belonging to the character.
             */
            if (hit.transform ==
                    characterRoot ||
                hit.transform.IsChildOf(
                    characterRoot
                ))
            {
                continue;
            }

            if (hit.distance >=
                closestDistance)
            {
                continue;
            }

            closestDistance =
                hit.distance;

            closestHit =
                hit;

            foundSurface = true;
        }

        if (!foundSurface)
        {
            return defaultAnkleHeight;
        }

        float ankleHeight =
            ankle.position.y -
            closestHit.point.y;

        return Mathf.Clamp(
            ankleHeight,
            0.02f,
            0.3f
        );
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