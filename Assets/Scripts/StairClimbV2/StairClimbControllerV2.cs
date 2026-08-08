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
/// - Foot rotation IK uses a limited, weighted correction to stabilize the shoe without twisting the ankle.
/// - Foot-to-surface clearance is calibrated with a downward physics raycast.
/// - Body forward offset and foot forward offset are independent.
/// </summary>
public sealed partial class StairClimbControllerV2 : MonoBehaviour
{
    // Serialized tuning values must never be migrated or overwritten at runtime.
    // Field initializers below are the defaults for newly added components.
    private const int CurrentControllerDataVersion = 9;

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
    [Tooltip("How much of the forward stair displacement is applied while the first foot moves.")]
    [FormerlySerializedAs("leadBodyShare")]
    [SerializeField, Range(0.1f, 0.9f)] private float leadForwardShare = 0.58f;

    [Tooltip("How much of the vertical stair displacement is applied while the first foot moves. Keep this much higher than the forward share so the pelvis rises with the lead foot instead of popping upward during Join.")]
    [SerializeField, Range(0.5f, 1f)] private float leadVerticalShare = 0.92f;

    [Tooltip("Moves the BODY forward or backward relative to the tread center.")]
    [FormerlySerializedAs("landingOffsetAlongClimb")]
    [SerializeField] private float bodyLandingOffsetAlongClimb = -0.05f;

    [Tooltip("Optional vertical correction applied only to the character root on each step.")]
    [SerializeField] private float bodyHeightOffsetOnStep = 0f;

    [Header("Lead Support Settle")]
    [Tooltip("After the lead foot contacts the tread, smoothly transfers the body weight over that foot so the support knee does not remain deeply bent.")]
    [SerializeField] private bool enableLeadSupportSettle = true;

    [Tooltip("Final forward share reached after the lead foot is planted. Values near 0.90 place the pelvis over the support foot while leaving a small amount of movement for Join.")]
    [SerializeField, Range(0.65f, 1f)] private float leadSupportForwardShare = 0.90f;

    [Tooltip("Final vertical share reached after the lead foot is planted. Keep this at 1 so the pelvis reaches the stair height before Join.")]
    [SerializeField, Range(0.8f, 1.05f)] private float leadSupportVerticalShare = 1f;

    [Tooltip("Duration of the weight-transfer settle after lead-foot contact.")]
    [SerializeField, Min(0.01f)] private float leadSupportSettleDuration = 0.16f;

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

    [SerializeField, Range(0f, 1f)] private float plantedFootPositionWeight = 1f;

    [Header("Safe Foot Rotation IK")]
    [Tooltip("Stabilizes the shoe angle after contact. Disable only if the avatar twists unexpectedly.")]
    [SerializeField] private bool enableFootRotationIK = true;

    [Tooltip("Rotation influence after the foot is planted. A moderate value keeps the animation natural.")]
    [SerializeField, Range(0f, 1f)] private float safeFootRotationWeight = 0.55f;

    [Tooltip("Maximum rotation correction applied in one IK evaluation. This prevents severe ankle twisting after retargeting.")]
    [SerializeField, Range(0f, 90f)] private float maxFootRotationCorrection = 35f;

    [Tooltip("Optional local rotation correction for both shoes after contact. Usually keep this at zero.")]
    [SerializeField] private Vector3 footRotationOffsetEuler = Vector3.zero;

    [Header("Animation Synchronization")]
    [SerializeField, Min(0f)] private float transitionDuration = 0.03f;
    [SerializeField, Range(0f, 0.95f)] private float leadMoveStart = 0.20f;
    [SerializeField, Range(0.05f, 1f)] private float leadMoveEnd = 0.92f;
    [SerializeField, Range(0f, 0.95f)] private float joinMoveStart = 0.05f;
    [SerializeField, Range(0.05f, 1f)] private float joinMoveEnd = 0.90f;

    [Tooltip("Normalized animation time at which the LEAD foot starts blending to its step target.")]
    [SerializeField, Range(0f, 0.99f)] private float leadFootPlantBlendStart = 0.72f;

    [Tooltip("Normalized animation time at which the JOIN foot starts blending to its step target.")]
    [SerializeField, Range(0f, 0.99f)] private float joinFootPlantBlendStart = 0.68f;

    [Tooltip("Lead stops on the actual split-stance/contact pose instead of being forced to the last frame of the clip.")]
    [SerializeField, Range(0.5f, 0.99f)] private float leadPhaseCompletionTime = 0.88f;

    [Tooltip("Normalized completion time used when the full Join clip is allowed to finish.")]
    [SerializeField, Range(0.5f, 1f)] private float animationCompletionTime = 0.98f;

    [Header("Completed Step Pose")]
    [Tooltip("Keeps the upright Join pose after both feet reach the tread instead of crossfading to the unrelated Idle clip. This prevents the pelvis from dropping and the knees from bending at the end of the step.")]
    [SerializeField] private bool holdCompletedJoinPose = true;

    [Tooltip("Normalized Join time used as the standing hold pose. The last frames of the clip contain a downward settle, so the pose is held before that crouch begins.")]
    [SerializeField, Range(0.65f, 0.95f)] private float completedJoinPoseTime = 0.84f;

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

    [SerializeField, HideInInspector] private int controllerDataVersion = CurrentControllerDataVersion;

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
        // Do not change serialized Inspector tuning here. Awake must only initialize runtime state.
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

        animator.SetIKPositionWeight(goal, footLock.PositionWeight);
        animator.SetIKPosition(goal, footLock.Position);

        if (!enableFootRotationIK || footLock.RotationWeight <= 0f)
        {
            animator.SetIKRotationWeight(goal, 0f);
            return;
        }

        // Clamp the requested correction relative to the animation pose. This preserves
        // the useful foot angle lock while preventing mirrored/retargeted clips from
        // forcing a large ankle twist.
        Quaternion animatedRotation = animator.GetIKRotation(goal);
        Quaternion safeTargetRotation = Quaternion.RotateTowards(
            animatedRotation,
            footLock.Rotation,
            maxFootRotationCorrection
        );

        animator.SetIKRotationWeight(goal, footLock.RotationWeight);
        animator.SetIKRotation(goal, safeTargetRotation);
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
}