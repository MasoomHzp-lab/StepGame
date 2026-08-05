using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Stair-climbing controller with independent root movement and persistent foot planting.
///
/// Main differences from the previous version:
/// - Root movement no longer receives the planted-foot correction.
/// - The moving foot blends to its own step target near contact.
/// - Each foot owns an independent world-space position lock.
/// - Planted feet stay position-locked through the transition back to Idle.
/// - Foot rotation IK is disabled to prevent ankle twisting after Humanoid retargeting.
/// - Foot-to-surface clearance is calibrated with a downward physics raycast.
/// - Body forward offset and foot forward offset are independent.
/// </summary>
public sealed class StairClimbControllerV2 : MonoBehaviour
{
    public enum FootSide
    {
        Right,
        Left
    }

    public enum LegActivationMode
    {
        BothFeet,
        RightOnly,
        LeftOnly
    }

    private sealed class FootLockState
    {
        public bool Active;
        public Vector3 Position;
        public Quaternion Rotation;
        public float PositionWeight;
        public float RotationWeight;
    }

    [Header("References")]
    [SerializeField] private StairPathV2 stairPath;
    [SerializeField] private Animator animator;

    [Tooltip("Transform moved through the stair path. Usually the Animator GameObject.")]
    [SerializeField] private Transform movementRoot;

    [Header("Session Mode")]
    [SerializeField] private LegActivationMode activationMode = LegActivationMode.BothFeet;
    [SerializeField] private bool autoStartSession = true;
    [SerializeField] private bool enableKeyboardTest = true;

    [Header("Animator States")]
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string rightLeadStateName = "RightLeadStep";
    [SerializeField] private string leftLeadStateName = "LeftLeadStep";
    [SerializeField] private string rightJoinStateName = "RightJoinStep";
    [SerializeField] private string leftJoinStateName = "LeftJoinStep";

    [Header("Body Movement")]
    [Tooltip("Percentage of the complete stair displacement applied during the lead movement.")]
    [SerializeField, Range(0.1f, 0.9f)] private float leadBodyShare = 0.58f;

    [Tooltip("Moves the BODY forward or backward relative to the tread center.")]
    [FormerlySerializedAs("landingOffsetAlongClimb")]
    [SerializeField] private float bodyLandingOffsetAlongClimb = -0.05f;

    [Tooltip("Optional vertical correction applied only to the character root on each step.")]
    [SerializeField] private float bodyHeightOffsetOnStep = 0f;

    [Header("Foot Placement")]
    [Tooltip("Moves only the planted FEET forward or backward without moving the body.")]
    [SerializeField] private float footLandingOffsetAlongClimb = -0.08f;

    [Tooltip("Vertical foot target correction while the foot is approaching the step.")]
    [SerializeField] private float movingFootOffset = 0f;

    [Tooltip("Vertical foot target correction after the foot is planted.")]
    [FormerlySerializedAs("footSurfaceOffset")]
    [SerializeField] private float plantedFootOffset = 0f;

    [Header("Foot Surface Calibration")]
    [Tooltip("Measures the real ankle-to-ground distance at session start instead of estimating it from the first stair.")]
    [SerializeField] private bool useGroundRaycastForFootClearance = true;

    [Tooltip("Maximum downward distance used to find the surface under each foot at session start.")]
    [SerializeField, Min(0.1f)] private float footGroundRaycastDistance = 1.2f;

    [Tooltip("Layers that may be used as floor or stair surfaces during foot calibration.")]
    [SerializeField] private LayerMask footGroundRaycastMask = ~0;

    [Tooltip("Small extra lift above the measured surface. Use this to keep the shoe mesh out of the stair.")]
    [SerializeField, Min(0f)] private float footSoleExtraClearance = 0.02f;

    [SerializeField] private bool preserveInitialRotation = true;

    [Header("Foot Lock")]
    [Tooltip("Keeps planted feet fixed in world space while the root and Animator move.")]
    [SerializeField] private bool enableFootPlantIK = true;

    // These names are intentionally different from the old fields so an old
    // serialized value of Rotation Weight = 0 does not silently override the new defaults.
    [SerializeField, Range(0f, 1f)] private float plantedFootPositionWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float plantedFootRotationWeight = 0f;

    [Header("Animation Synchronization")]
    [SerializeField, Min(0f)] private float transitionDuration = 0.03f;
    [SerializeField, Range(0f, 0.95f)] private float leadMoveStart = 0.20f;
    [SerializeField, Range(0.05f, 1f)] private float leadMoveEnd = 0.92f;
    [SerializeField, Range(0f, 0.95f)] private float joinMoveStart = 0.05f;
    [SerializeField, Range(0.05f, 1f)] private float joinMoveEnd = 0.90f;

    [Tooltip("Normalized animation time at which the moving foot starts blending to its step target.")]
    [SerializeField, Range(0f, 0.99f)] private float footPlantBlendStart = 0.74f;

    [SerializeField, Range(0.5f, 1f)] private float animationCompletionTime = 0.98f;
    [SerializeField, Min(0.5f)] private float animationTimeout = 5f;
    [SerializeField, Min(0f)] private float automaticJoinDelay = 0.08f;

    [Header("Runtime - Read Only")]
    [SerializeField] private int rightFootStepIndex = -1;
    [SerializeField] private int leftFootStepIndex = -1;
    [SerializeField] private bool sessionStarted;
    [SerializeField] private bool isAnimating;
    [SerializeField] private bool waitingForOppositeFoot;
    [SerializeField] private FootSide requiredNextFoot;
    [SerializeField] private int pendingTargetStepIndex = -1;

    [SerializeField, HideInInspector] private int controllerDataVersion;

    private Transform rightFootBone;
    private Transform leftFootBone;

    private Vector3 initialRootPosition;
    private Quaternion initialRootRotation;
    private Vector3 stableRootPosition;
    private Quaternion stableRootRotation;

    private Vector3 pendingRootPosition;

    private Vector3 pendingRightMovingTarget;
    private Vector3 pendingRightPlantedTarget;
    private Vector3 pendingLeftMovingTarget;
    private Vector3 pendingLeftPlantedTarget;

    private Quaternion pendingRightTargetRotation;
    private Quaternion pendingLeftTargetRotation;

    private float initialRightFootClearance;
    private float initialLeftFootClearance;
    private float initialAverageFootClearance;

    private float initialRightForwardOffset;
    private float initialLeftForwardOffset;
    private float initialRightLateralOffset;
    private float initialLeftLateralOffset;
    private float initialFeetCenterLateralOffset;

    private Quaternion initialRightFootRotationRelativeToRoot;
    private Quaternion initialLeftFootRotationRelativeToRoot;

    private Vector3 climbDirection;
    private Vector3 sideDirection;

    private bool initialized;

    private readonly FootLockState rightFootLock = new FootLockState();
    private readonly FootLockState leftFootLock = new FootLockState();

    public LegActivationMode ActivationMode => activationMode;
    public bool SessionStarted => sessionStarted;
    public bool IsAnimating => isAnimating;
    public int RightFootStepIndex => rightFootStepIndex;
    public int LeftFootStepIndex => leftFootStepIndex;
    public bool WaitingForOppositeFoot => waitingForOppositeFoot;
    public FootSide RequiredNextFoot => requiredNextFoot;

    private void Awake()
    {
        ApplyVersionedDefaults();

        if (!Initialize())
        {
            enabled = false;
            return;
        }

        if (autoStartSession)
        {
            ResetSession();
        }
    }

    private void Update()
    {
        if (!enableKeyboardTest || !sessionStarted || isAnimating)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.rKey.wasPressedThisFrame)
        {
            SubmitMovementResult(FootSide.Right, true);
        }

        if (keyboard.lKey.wasPressedThisFrame)
        {
            SubmitMovementResult(FootSide.Left, true);
        }
    }

    private void LateUpdate()
    {
        if (!initialized || movementRoot == null)
        {
            return;
        }

        if (!isAnimating)
        {
            movementRoot.position = stableRootPosition;

            if (preserveInitialRotation)
            {
                movementRoot.rotation = stableRootRotation;
            }
        }
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!enableFootPlantIK || animator == null)
        {
            return;
        }

        ApplyFootLock(AvatarIKGoal.RightFoot, rightFootLock);
        ApplyFootLock(AvatarIKGoal.LeftFoot, leftFootLock);
    }

    private void ApplyFootLock(AvatarIKGoal goal, FootLockState footLock)
    {
        if (!footLock.Active)
        {
            animator.SetIKPositionWeight(goal, 0f);
            animator.SetIKRotationWeight(goal, 0f);
            return;
        }

        // Lock only the world-space position.
        // Rotation IK is deliberately disabled because Humanoid retargeting / mirrored clips
        // can twist the ankle when a cached foot-bone rotation is forced back onto the avatar.
        animator.SetIKPositionWeight(goal, footLock.PositionWeight);
        animator.SetIKRotationWeight(goal, 0f);
        animator.SetIKPosition(goal, footLock.Position);
    }

    /// <summary>
    /// Entry point for keyboard testing and future API integration.
    /// A failed threshold result leaves the character on the current level.
    /// </summary>
    public bool SubmitMovementResult(FootSide foot, bool passedThreshold)
    {
        if (!passedThreshold)
        {
            Debug.Log($"Movement rejected by threshold evaluation | Foot: {foot}", this);
            return false;
        }

        return TryRequestFootMovement(foot);
    }

    public bool TryRequestFootMovement(FootSide requestedFoot)
    {
        if (!sessionStarted)
        {
            Debug.LogWarning("Stair Climb V2: The session has not started.", this);
            return false;
        }

        if (isAnimating)
        {
            Debug.LogWarning("Stair Climb V2: Input ignored while an animation is running.", this);
            return false;
        }

        if (!IsFootEnabled(requestedFoot))
        {
            Debug.LogWarning(
                $"Stair Climb V2: {requestedFoot} input is disabled in {activationMode} mode.",
                this
            );
            return false;
        }

        if (activationMode == LegActivationMode.BothFeet)
        {
            return HandleBothFeetRequest(requestedFoot);
        }

        return HandleSingleFootRequest(requestedFoot);
    }

    public void SetActivationMode(LegActivationMode mode)
    {
        if (isAnimating)
        {
            Debug.LogWarning("Stair Climb V2: Mode cannot change during an animation.", this);
            return;
        }

        activationMode = mode;
        ResetSession();
    }

    public void SetBothFeetMode() => SetActivationMode(LegActivationMode.BothFeet);
    public void SetRightOnlyMode() => SetActivationMode(LegActivationMode.RightOnly);
    public void SetLeftOnlyMode() => SetActivationMode(LegActivationMode.LeftOnly);

    [ContextMenu("Reset Session")]
    public void ResetSession()
    {
        if (!Initialize())
        {
            return;
        }

        StopAllCoroutines();
        ClearAllFootLocks();

        movementRoot.position = initialRootPosition;
        movementRoot.rotation = initialRootRotation;

        stableRootPosition = initialRootPosition;
        stableRootRotation = initialRootRotation;
        pendingRootPosition = initialRootPosition;

        rightFootStepIndex = -1;
        leftFootStepIndex = -1;
        pendingTargetStepIndex = -1;

        isAnimating = false;
        waitingForOppositeFoot = false;
        requiredNextFoot = FootSide.Right;
        sessionStarted = true;

        animator.applyRootMotion = false;
        animator.speed = 1f;
        animator.Play(idleStateName, 0, 0f);
        animator.Update(0f);

        CaptureFootCalibration();
        LockFootAtCurrentPose(FootSide.Right);
        LockFootAtCurrentPose(FootSide.Left);

        Debug.Log(
            $"Stair Climb V2 session started | Mode: {activationMode} | Steps: {stairPath.StepCount}",
            this
        );
    }

    public void StopSession()
    {
        StopAllCoroutines();
        isAnimating = false;
        waitingForOppositeFoot = false;
        sessionStarted = false;
        ClearAllFootLocks();

        if (animator != null)
        {
            animator.speed = 1f;
            animator.applyRootMotion = false;
            animator.CrossFadeInFixedTime(idleStateName, transitionDuration);
        }

        Debug.Log("Stair Climb V2 session stopped.", this);
    }

    private bool HandleBothFeetRequest(FootSide requestedFoot)
    {
        if (rightFootStepIndex == leftFootStepIndex)
        {
            int targetStep = rightFootStepIndex + 1;
            if (!CanMoveToStep(targetStep))
            {
                CompleteSession();
                return false;
            }

            StartCoroutine(PlayManualLead(requestedFoot, targetStep));
            return true;
        }

        FootSide trailingFoot = rightFootStepIndex < leftFootStepIndex
            ? FootSide.Right
            : FootSide.Left;

        if (requestedFoot != trailingFoot)
        {
            Debug.LogWarning(
                $"Stair Climb V2: {trailingFoot} must move next because it is on the lower step.",
                this
            );
            return false;
        }

        StartCoroutine(PlayManualJoin(requestedFoot));
        return true;
    }

    private bool HandleSingleFootRequest(FootSide requestedFoot)
    {
        int sharedStep = Mathf.Min(rightFootStepIndex, leftFootStepIndex);
        int targetStep = sharedStep + 1;

        if (!CanMoveToStep(targetStep))
        {
            CompleteSession();
            return false;
        }

        StartCoroutine(PlayAutomaticCompleteStep(requestedFoot, targetStep));
        return true;
    }

    private IEnumerator PlayManualLead(FootSide leadFoot, int targetStep)
    {
        isAnimating = true;

        if (!PrepareCompleteStep(targetStep))
        {
            isAnimating = false;
            yield break;
        }

        Vector3 leadEndPosition = Vector3.Lerp(
            stableRootPosition,
            pendingRootPosition,
            leadBodyShare
        );

        bool succeeded = false;
        yield return PlayAnimationPhase(
            GetLeadStateName(leadFoot),
            leadFoot,
            stableRootPosition,
            leadEndPosition,
            leadMoveStart,
            leadMoveEnd,
            true,
            result => succeeded = result
        );

        if (!succeeded)
        {
            isAnimating = false;
            yield break;
        }

        stableRootPosition = leadEndPosition;
        SetFootStepIndex(leadFoot, targetStep);

        waitingForOppositeFoot = true;
        requiredNextFoot = Opposite(leadFoot);
        isAnimating = false;

        Debug.Log(
            $"Lead completed | {leadFoot} is on step {targetStep} | Required next foot: {requiredNextFoot}",
            this
        );
    }

    private IEnumerator PlayManualJoin(FootSide joinFoot)
    {
        isAnimating = true;

        int targetStep = Mathf.Max(rightFootStepIndex, leftFootStepIndex);
        if (pendingTargetStepIndex != targetStep)
        {
            Debug.LogError("Stair Climb V2: Pending target state is invalid. Reset the session.", this);
            isAnimating = false;
            yield break;
        }

        bool succeeded = false;
        yield return PlayAnimationPhase(
            GetJoinStateName(joinFoot),
            joinFoot,
            stableRootPosition,
            pendingRootPosition,
            joinMoveStart,
            joinMoveEnd,
            false,
            result => succeeded = result
        );

        if (!succeeded)
        {
            isAnimating = false;
            yield break;
        }

        stableRootPosition = pendingRootPosition;
        SetFootStepIndex(joinFoot, targetStep);
        waitingForOppositeFoot = false;
        pendingTargetStepIndex = -1;
        isAnimating = false;

        ReturnToIdle();
        CheckForPathCompletion();

        Debug.Log($"Join completed | Both feet are on step {targetStep}", this);
    }

    private IEnumerator PlayAutomaticCompleteStep(FootSide leadFoot, int targetStep)
    {
        isAnimating = true;

        if (!PrepareCompleteStep(targetStep))
        {
            isAnimating = false;
            yield break;
        }

        Vector3 leadEndPosition = Vector3.Lerp(
            stableRootPosition,
            pendingRootPosition,
            leadBodyShare
        );

        bool leadSucceeded = false;
        yield return PlayAnimationPhase(
            GetLeadStateName(leadFoot),
            leadFoot,
            stableRootPosition,
            leadEndPosition,
            leadMoveStart,
            leadMoveEnd,
            true,
            result => leadSucceeded = result
        );

        if (!leadSucceeded)
        {
            isAnimating = false;
            yield break;
        }

        stableRootPosition = leadEndPosition;
        SetFootStepIndex(leadFoot, targetStep);

        if (automaticJoinDelay > 0f)
        {
            yield return new WaitForSeconds(automaticJoinDelay);
        }

        FootSide joinFoot = Opposite(leadFoot);
        bool joinSucceeded = false;

        yield return PlayAnimationPhase(
            GetJoinStateName(joinFoot),
            joinFoot,
            stableRootPosition,
            pendingRootPosition,
            joinMoveStart,
            joinMoveEnd,
            false,
            result => joinSucceeded = result
        );

        if (!joinSucceeded)
        {
            isAnimating = false;
            yield break;
        }

        stableRootPosition = pendingRootPosition;
        rightFootStepIndex = targetStep;
        leftFootStepIndex = targetStep;
        pendingTargetStepIndex = -1;
        waitingForOppositeFoot = false;
        isAnimating = false;

        ReturnToIdle();
        CheckForPathCompletion();

        Debug.Log(
            $"Automatic complete step finished | Lead: {leadFoot} | Both feet: step {targetStep}",
            this
        );
    }

    private bool PrepareCompleteStep(int targetStep)
    {
        if (!CalculateRootDestination(targetStep, out Vector3 rootDisplacement))
        {
            return false;
        }

        if (!CalculateFootTargets(targetStep))
        {
            return false;
        }

        pendingTargetStepIndex = targetStep;
        pendingRootPosition = stableRootPosition + rootDisplacement;
        return true;
    }

    private bool CalculateRootDestination(int targetStep, out Vector3 rootDisplacement)
    {
        rootDisplacement = Vector3.zero;

        if (!stairPath.TryGetStepTopCenter(targetStep, out Vector3 stepTopCenter))
        {
            Debug.LogError($"Stair Climb V2: Step {targetStep} is not available.", this);
            return false;
        }

        if (rightFootBone == null || leftFootBone == null)
        {
            Debug.LogError("Stair Climb V2: Humanoid foot bones are missing.", this);
            return false;
        }

        Vector3 feetCenter = (rightFootBone.position + leftFootBone.position) * 0.5f;

        float forwardDistance = Vector3.Dot(stepTopCenter - feetCenter, climbDirection);
        forwardDistance += bodyLandingOffsetAlongClimb;
        forwardDistance = Mathf.Max(0f, forwardDistance);

        float targetFeetCenterY =
            stepTopCenter.y + initialAverageFootClearance + bodyHeightOffsetOnStep;
        float verticalDistance = targetFeetCenterY - feetCenter.y;

        rootDisplacement =
            climbDirection * forwardDistance + Vector3.up * verticalDistance;

        Debug.Log(
            $"Root destination prepared | Target: {targetStep} | Forward: {forwardDistance:F3} | Up: {verticalDistance:F3}",
            this
        );

        return true;
    }

    private bool CalculateFootTargets(int targetStep)
    {
        if (!stairPath.TryGetStepTopCenter(targetStep, out Vector3 stepTopCenter))
        {
            Debug.LogError($"Stair Climb V2: Step {targetStep} is not available.", this);
            return false;
        }

        float sharedForwardOffset = footLandingOffsetAlongClimb;
        float sharedLateralOffset = initialFeetCenterLateralOffset;

        // Do not carry the initial forward stagger of the Idle pose onto the stair.
        // Both feet use one shared tread depth; only their left/right separation is preserved.
        pendingRightMovingTarget = BuildFootTarget(
            stepTopCenter,
            sharedForwardOffset,
            initialRightLateralOffset + sharedLateralOffset,
            initialRightFootClearance + footSoleExtraClearance + movingFootOffset
        );

        pendingRightPlantedTarget = BuildFootTarget(
            stepTopCenter,
            sharedForwardOffset,
            initialRightLateralOffset + sharedLateralOffset,
            initialRightFootClearance + footSoleExtraClearance + plantedFootOffset
        );

        pendingLeftMovingTarget = BuildFootTarget(
            stepTopCenter,
            sharedForwardOffset,
            initialLeftLateralOffset + sharedLateralOffset,
            initialLeftFootClearance + footSoleExtraClearance + movingFootOffset
        );

        pendingLeftPlantedTarget = BuildFootTarget(
            stepTopCenter,
            sharedForwardOffset,
            initialLeftLateralOffset + sharedLateralOffset,
            initialLeftFootClearance + footSoleExtraClearance + plantedFootOffset
        );

        pendingRightTargetRotation =
            stableRootRotation * initialRightFootRotationRelativeToRoot;
        pendingLeftTargetRotation =
            stableRootRotation * initialLeftFootRotationRelativeToRoot;

        return true;
    }

    private Vector3 BuildFootTarget(
        Vector3 stepTopCenter,
        float sharedFootForwardOffset,
        float lateralOffset,
        float verticalClearance
    )
    {
        return stepTopCenter
               + climbDirection * sharedFootForwardOffset
               + sideDirection * lateralOffset
               + Vector3.up * verticalClearance;
    }

    private IEnumerator PlayAnimationPhase(
        string stateName,
        FootSide movingFoot,
        Vector3 startPosition,
        Vector3 endPosition,
        float moveStart,
        float moveEnd,
        bool freezeFinalPose,
        System.Action<bool> onComplete
    )
    {
        PrepareFootLocksForMovement(movingFoot);

        animator.applyRootMotion = false;
        animator.speed = 1f;

        int stateHash = Animator.StringToHash(stateName);
        animator.CrossFadeInFixedTime(stateName, transitionDuration, 0, 0f);

        float elapsed = 0f;
        bool stateStarted = false;
        bool completed = false;

        while (elapsed < animationTimeout)
        {
            yield return null;

            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            bool isTargetState = stateInfo.shortNameHash == stateHash;

            if (isTargetState)
            {
                stateStarted = true;

                float normalized = Mathf.Clamp01(stateInfo.normalizedTime);
                float moveProgress = Mathf.InverseLerp(moveStart, moveEnd, normalized);
                float easedProgress = SmoothStep01(moveProgress);

                movementRoot.position = Vector3.LerpUnclamped(
                    startPosition,
                    endPosition,
                    easedProgress
                );

                if (preserveInitialRotation)
                {
                    movementRoot.rotation = stableRootRotation;
                }

                float plantProgress = Mathf.InverseLerp(
                    footPlantBlendStart,
                    animationCompletionTime,
                    normalized
                );
                plantProgress = SmoothStep01(plantProgress);
                UpdateMovingFootPlant(movingFoot, plantProgress);

                if (!animator.IsInTransition(0) &&
                    stateInfo.normalizedTime >= animationCompletionTime)
                {
                    completed = true;
                    break;
                }
            }

            elapsed += Time.deltaTime;
        }

        movementRoot.position = endPosition;
        if (preserveInitialRotation)
        {
            movementRoot.rotation = stableRootRotation;
        }

        if (!stateStarted)
        {
            LockFootAtCurrentPose(movingFoot);
            Debug.LogError($"Stair Climb V2: Animator state '{stateName}' did not start.", this);
            onComplete?.Invoke(false);
            yield break;
        }

        if (!completed)
        {
            Debug.LogWarning(
                $"Stair Climb V2: Animation '{stateName}' reached the timeout. Final position was applied.",
                this
            );
        }

        LockFootAtPendingTarget(movingFoot);

        if (freezeFinalPose)
        {
            animator.Play(stateName, 0, 0.999f);
            animator.Update(0f);
            animator.speed = 0f;
        }

        onComplete?.Invoke(true);
    }

    private void PrepareFootLocksForMovement(FootSide movingFoot)
    {
        FootSide supportFoot = Opposite(movingFoot);

        EnsureFootLocked(supportFoot);
        ReleaseFootLock(movingFoot);
    }

    private void UpdateMovingFootPlant(FootSide foot, float progress)
    {
        if (!enableFootPlantIK)
        {
            return;
        }

        FootLockState footLock = GetFootLock(foot);
        Vector3 movingTarget = GetPendingMovingTarget(foot);
        Vector3 plantedTarget = GetPendingPlantedTarget(foot);

        footLock.Active = progress > 0f;
        footLock.Position = Vector3.LerpUnclamped(movingTarget, plantedTarget, progress);
        footLock.Rotation = GetPendingTargetRotation(foot);
        footLock.PositionWeight = plantedFootPositionWeight * progress;
        footLock.RotationWeight = plantedFootRotationWeight * progress;
    }

    private void LockFootAtPendingTarget(FootSide foot)
    {
        if (!enableFootPlantIK)
        {
            return;
        }

        FootLockState footLock = GetFootLock(foot);
        footLock.Active = true;
        footLock.Position = GetPendingPlantedTarget(foot);
        footLock.Rotation = GetPendingTargetRotation(foot);
        footLock.PositionWeight = plantedFootPositionWeight;
        footLock.RotationWeight = plantedFootRotationWeight;
    }

    private void LockFootAtCurrentPose(FootSide foot)
    {
        if (!enableFootPlantIK)
        {
            return;
        }

        Transform footBone = GetFootBone(foot);
        if (footBone == null)
        {
            return;
        }

        FootLockState footLock = GetFootLock(foot);
        footLock.Active = true;
        footLock.Position = footBone.position;
        footLock.Rotation = footBone.rotation;
        footLock.PositionWeight = plantedFootPositionWeight;
        footLock.RotationWeight = plantedFootRotationWeight;
    }

    private void EnsureFootLocked(FootSide foot)
    {
        FootLockState footLock = GetFootLock(foot);
        if (!footLock.Active)
        {
            LockFootAtCurrentPose(foot);
        }
    }

    private void ReleaseFootLock(FootSide foot)
    {
        FootLockState footLock = GetFootLock(foot);
        footLock.Active = false;
        footLock.PositionWeight = 0f;
        footLock.RotationWeight = 0f;
    }

    private void ClearAllFootLocks()
    {
        rightFootLock.Active = false;
        rightFootLock.PositionWeight = 0f;
        rightFootLock.RotationWeight = 0f;

        leftFootLock.Active = false;
        leftFootLock.PositionWeight = 0f;
        leftFootLock.RotationWeight = 0f;
    }

    private FootLockState GetFootLock(FootSide foot)
    {
        return foot == FootSide.Right ? rightFootLock : leftFootLock;
    }

    private Transform GetFootBone(FootSide foot)
    {
        return foot == FootSide.Right ? rightFootBone : leftFootBone;
    }

    private Vector3 GetPendingMovingTarget(FootSide foot)
    {
        return foot == FootSide.Right
            ? pendingRightMovingTarget
            : pendingLeftMovingTarget;
    }

    private Vector3 GetPendingPlantedTarget(FootSide foot)
    {
        return foot == FootSide.Right
            ? pendingRightPlantedTarget
            : pendingLeftPlantedTarget;
    }

    private Quaternion GetPendingTargetRotation(FootSide foot)
    {
        return foot == FootSide.Right
            ? pendingRightTargetRotation
            : pendingLeftTargetRotation;
    }

    private void ReturnToIdle()
    {
        // Do not release either foot here. Both planted locks remain active
        // throughout the CrossFade and while Idle is playing.
        animator.speed = 1f;
        animator.applyRootMotion = false;
        animator.CrossFadeInFixedTime(idleStateName, transitionDuration, 0, 0f);
    }

    private void CaptureFootCalibration()
    {
        climbDirection = stairPath.ClimbWorldDirection;
        climbDirection.y = 0f;

        if (climbDirection.sqrMagnitude < 0.0001f)
        {
            climbDirection = movementRoot.forward;
            climbDirection.y = 0f;
        }

        climbDirection.Normalize();
        sideDirection = Vector3.Cross(Vector3.up, climbDirection).normalized;

        Vector3 feetCenter = (rightFootBone.position + leftFootBone.position) * 0.5f;
        float estimatedStartSurfaceY = stairPath.GetEstimatedStartSurfaceY();

        initialRightFootClearance = MeasureFootClearance(
            rightFootBone,
            estimatedStartSurfaceY
        );
        initialLeftFootClearance = MeasureFootClearance(
            leftFootBone,
            estimatedStartSurfaceY
        );
        initialAverageFootClearance =
            (initialRightFootClearance + initialLeftFootClearance) * 0.5f;

        initialRightForwardOffset =
            Vector3.Dot(rightFootBone.position - feetCenter, climbDirection);
        initialLeftForwardOffset =
            Vector3.Dot(leftFootBone.position - feetCenter, climbDirection);

        initialRightLateralOffset =
            Vector3.Dot(rightFootBone.position - feetCenter, sideDirection);
        initialLeftLateralOffset =
            Vector3.Dot(leftFootBone.position - feetCenter, sideDirection);

        initialFeetCenterLateralOffset = 0f;
        if (stairPath.TryGetStepTopCenter(0, out Vector3 firstStepTopCenter))
        {
            initialFeetCenterLateralOffset =
                Vector3.Dot(feetCenter - firstStepTopCenter, sideDirection);
        }

        initialRightFootRotationRelativeToRoot =
            Quaternion.Inverse(stableRootRotation) * rightFootBone.rotation;
        initialLeftFootRotationRelativeToRoot =
            Quaternion.Inverse(stableRootRotation) * leftFootBone.rotation;

        Debug.Log(
            $"Stair Climb V2 calibration | Right clearance: {initialRightFootClearance:F3} | Left clearance: {initialLeftFootClearance:F3} | Extra sole lift: {footSoleExtraClearance:F3}",
            this
        );
    }

    private float MeasureFootClearance(Transform footBone, float fallbackSurfaceY)
    {
        float fallbackClearance = Mathf.Max(0f, footBone.position.y - fallbackSurfaceY);

        if (!useGroundRaycastForFootClearance)
        {
            return fallbackClearance;
        }

        const float originLift = 0.15f;
        Vector3 origin = footBone.position + Vector3.up * originLift;
        float maxDistance = Mathf.Max(0.1f, footGroundRaycastDistance) + originLift;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            maxDistance,
            footGroundRaycastMask,
            QueryTriggerInteraction.Ignore
        );

        bool foundSurface = false;
        float highestSurfaceY = float.NegativeInfinity;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
            {
                continue;
            }

            Transform hitTransform = hit.collider.transform;
            if (hitTransform == movementRoot || hitTransform.IsChildOf(movementRoot))
            {
                continue;
            }

            // Ignore anything above the ankle. We only need the supporting surface below it.
            if (hit.point.y > footBone.position.y + 0.01f)
            {
                continue;
            }

            if (!foundSurface || hit.point.y > highestSurfaceY)
            {
                foundSurface = true;
                highestSurfaceY = hit.point.y;
            }
        }

        if (!foundSurface)
        {
            Debug.LogWarning(
                $"Stair Climb V2: No floor hit found below '{footBone.name}'. Using stair-path fallback clearance {fallbackClearance:F3}.",
                this
            );
            return fallbackClearance;
        }

        return Mathf.Max(0f, footBone.position.y - highestSurfaceY);
    }


    private void ApplyVersionedDefaults()
    {
        if (controllerDataVersion >= 5)
        {
            return;
        }

        // One-time migration for scenes that used the earlier target calculation.
        enableFootPlantIK = true;
        plantedFootPositionWeight = 1f;
        plantedFootRotationWeight = 0f;

        // The measured ankle-to-floor clearance now provides the base height.
        // These remain zero and are only for tiny manual tuning after calibration.
        movingFootOffset = 0f;
        plantedFootOffset = 0f;
        useGroundRaycastForFootClearance = true;
        footGroundRaycastDistance = 1.2f;
        footGroundRaycastMask = ~0;
        footSoleExtraClearance = 0.02f;

        // Keep the shoe safely behind the next riser.
        footLandingOffsetAlongClimb = -0.12f;
        bodyHeightOffsetOnStep = 0f;
        footPlantBlendStart = 0.74f;
        controllerDataVersion = 5;
    }

    private bool Initialize()
    {
        if (initialized)
        {
            return true;
        }

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator == null)
        {
            Debug.LogError("Stair Climb V2: Animator is missing.", this);
            return false;
        }

        if (!animator.isHuman)
        {
            Debug.LogError("Stair Climb V2: The Animator must use a Humanoid avatar.", this);
            return false;
        }

        if (movementRoot == null)
        {
            movementRoot = animator.transform;
        }

        if (stairPath == null)
        {
            stairPath = FindFirstObjectByType<StairPathV2>();
        }

        if (stairPath == null)
        {
            Debug.LogError("Stair Climb V2: Stair Path V2 is missing.", this);
            return false;
        }

        rightFootBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        leftFootBone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);

        if (rightFootBone == null || leftFootBone == null)
        {
            Debug.LogError("Stair Climb V2: Right or left foot bone could not be found.", this);
            return false;
        }

        animator.applyRootMotion = false;

        initialRootPosition = movementRoot.position;
        initialRootRotation = movementRoot.rotation;
        stableRootPosition = initialRootPosition;
        stableRootRotation = initialRootRotation;

        initialized = true;
        return true;
    }

    private bool IsFootEnabled(FootSide foot)
    {
        return activationMode == LegActivationMode.BothFeet ||
               (activationMode == LegActivationMode.RightOnly && foot == FootSide.Right) ||
               (activationMode == LegActivationMode.LeftOnly && foot == FootSide.Left);
    }

    private bool CanMoveToStep(int stepIndex)
    {
        return stepIndex >= 0 && stepIndex < stairPath.StepCount;
    }

    private void SetFootStepIndex(FootSide foot, int stepIndex)
    {
        if (foot == FootSide.Right)
        {
            rightFootStepIndex = stepIndex;
        }
        else
        {
            leftFootStepIndex = stepIndex;
        }
    }

    private string GetLeadStateName(FootSide foot)
    {
        return foot == FootSide.Right ? rightLeadStateName : leftLeadStateName;
    }

    private string GetJoinStateName(FootSide foot)
    {
        return foot == FootSide.Right ? rightJoinStateName : leftJoinStateName;
    }

    private static FootSide Opposite(FootSide foot)
    {
        return foot == FootSide.Right ? FootSide.Left : FootSide.Right;
    }

    private static float SmoothStep01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private void CheckForPathCompletion()
    {
        if (rightFootStepIndex >= stairPath.StepCount - 1 &&
            leftFootStepIndex >= stairPath.StepCount - 1)
        {
            CompleteSession();
        }
    }

    private void CompleteSession()
    {
        sessionStarted = false;
        waitingForOppositeFoot = false;
        Debug.Log("Stair Climb V2: The stair path is complete.", this);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        leadMoveEnd = Mathf.Max(leadMoveEnd, leadMoveStart + 0.01f);
        joinMoveEnd = Mathf.Max(joinMoveEnd, joinMoveStart + 0.01f);
        animationCompletionTime = Mathf.Clamp(animationCompletionTime, 0.5f, 1f);
        footPlantBlendStart = Mathf.Clamp(
            footPlantBlendStart,
            0f,
            Mathf.Max(0f, animationCompletionTime - 0.01f)
        );
        animationTimeout = Mathf.Max(0.5f, animationTimeout);
        plantedFootPositionWeight = Mathf.Clamp01(plantedFootPositionWeight);
        footGroundRaycastDistance = Mathf.Max(0.1f, footGroundRaycastDistance);
        footSoleExtraClearance = Mathf.Max(0f, footSoleExtraClearance);

        // Keep ankle rotation under Animator control.
        plantedFootRotationWeight = 0f;
    }
#endif
}