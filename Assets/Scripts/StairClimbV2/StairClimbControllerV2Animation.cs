using System.Collections;
using UnityEngine;

/// <summary>
/// Step sequencing, animation playback and root/body movement for StairClimbControllerV2.
/// Split from the previous large partial file; behaviour is unchanged.
/// </summary>
public sealed partial class StairClimbControllerV2
{
    [Header("Movement Playback Speed")]
    [Tooltip("Playback speed used only while Lead/Join movement states are running. 1.12 is a small, safe increase without changing Idle speed.")]
    [SerializeField, Range(0.75f, 1.50f)] private float movementAnimationSpeed = 1.15f;

    [Header("First Step Only - Legacy Override")]
    [Tooltip("Legacy compatibility switch. Keep OFF so the first stair uses the same geometry-based final root destination as every later stair. The old 0.11 share left the body/trailing foot too far backward after step 0.")]
    [SerializeField] private bool useFirstStepJoinForwardOverride = false;

    [Tooltip("Legacy-only: fraction of the remaining planar Join movement used on step 0 when the override above is enabled.")]
    [SerializeField, Range(0f, 1f)] private float firstStepJoinForwardShare = 0.45f;

    [Header("Whole Character Step Height")]
    [Tooltip("Raises the whole character and both foot IK targets together above every stair tread. Use this to keep shoe soles out of the stair without bending the knees.")]
    [SerializeField, Min(0f)] private float wholeCharacterStepLift = 0.02f;

    [Header("Split Stance Body Timing")]
    [Tooltip("During Lead, keep most of the body on the lower/support leg. The remaining vertical rise is completed during Join when the trailing foot comes up.")]
    [SerializeField] private bool useTwoStageBodyRise = true;

    [Tooltip("How much of one stair height the character root may rise during Lead. 0.35 keeps a clear split stance without over-stretching the lower support leg.")]
    [SerializeField, Range(0f, 0.65f)] private float splitStanceVerticalShare = 0.35f;

    [Header("No Back-Step Foot Release")]
    [Tooltip("Keeps the lead foot planted at its current position through the anticipation frames, then releases it into the swing.")]
    [SerializeField, Range(0f, 0.35f)] private float leadFootReleaseTime = 0.12f;

    [Tooltip("Keeps the trailing/join foot planted at its current position through the anticipation frames, then releases it into the swing.")]
    [SerializeField, Range(0f, 0.35f)] private float joinFootReleaseTime = 0.10f;

    [Tooltip("How gradually the moving foot IK releases after the anticipation lock. A short fade prevents the one-frame backward snap when animation control takes over.")]
    [SerializeField, Range(0.01f, 0.25f)] private float movingFootReleaseBlendDuration = 0.08f;

    [Header("Stable Step-to-Step Start")]
    [Tooltip("Normalized start time used for the first Lead from the ground. The first step already looks correct, so this stays conservative.")]
    [SerializeField, Range(0f, 0.30f)] private float leadAnimationStartTime = 0.12f;

    [Tooltip("Normalized start time used for Lead steps after the character is already standing on a stair. This skips more of the recorded backward anticipation without changing the first step.")]
    [SerializeField, Range(0f, 0.35f)] private float stairLeadAnimationStartTime = 0.22f;

    [Tooltip("Extra time that the moving foot stays exactly planted after a Lead state begins. This prevents the clip from pulling the shoe backward while the animation pose blends in.")]
    [SerializeField, Range(0f, 0.15f)] private float leadExtraFootHoldAfterStart = 0.05f;

    [Tooltip("After both feet land on a tread, return to the regular Idle state.")]
    [SerializeField] private bool returnToIdleAfterCompletedStep = true;

    [Tooltip("Blend time used to normalize the completed Join pose back to Idle.")]
    [SerializeField, Range(0f, 0.25f)] private float completedStepIdleBlendDuration = 0.10f;

    [Header("Completed Step Idle Recovery")]
    [Tooltip("Release both planted-foot IK locks while blending to Idle. Keeping both locks active forces the Humanoid solver into a crouch.")]
    [SerializeField] private bool releaseFootLocksDuringCompletedIdle = true;

    [Tooltip("How quickly the two planted-foot IK weights fade out while Idle is restored.")]
    [SerializeField, Range(0.02f, 0.25f)] private float completedIdleFootReleaseDuration = 0.08f;

    [Tooltip("After Idle is sampled with free legs, shift only the character root height so the feet remain on the tread without bending the knees.")]
    [SerializeField] private bool alignRootHeightAfterCompletedIdle = true;

    [Tooltip("Safety limit for the automatic root-height correction after returning to Idle.")]
    [SerializeField, Range(0f, 0.20f)] private float completedIdleMaxRootHeightCorrection = 0.12f;

    private Coroutine completedIdleRecoveryCoroutine;

    private IEnumerator PlayManualLead(FootSide leadFoot, int targetStep)
    {
        isAnimating = true;

        if (!PrepareCompleteStep(targetStep))
        {
            isAnimating = false;
            yield break;
        }

        Vector3 leadEndPosition = CalculateLeadEndPosition();

        bool succeeded = false;
        yield return PlayAnimationPhase(
            GetLeadStateName(leadFoot),
            leadFoot,
            stableRootPosition,
            leadEndPosition,
            leadMoveStart,
            leadMoveEnd,
            leadFootReleaseTime,
            leadFootPlantBlendStart,
            leadPhaseCompletionTime,
            GetLeadAnimationStartTime(targetStep),
            true,
            result => succeeded = result
        );

        if (!succeeded)
        {
            isAnimating = false;
            yield break;
        }

        // FIRST STEP FIX:
        // On step 0 the trailing foot is still on the ground. Moving the pelvis almost
        // all the way over the planted lead foot while the trailing foot is IK-locked
        // stretches the rear leg and creates the ugly first-step pose.
        //
        // Keep the root at the normal Lead end position on the first stair.
        // The remaining root movement is completed naturally during Join, while the
        // second foot is actually moving up.
        // In the requested two-stage motion, the trailing/support foot must remain
        // on the lower tread after Lead. Do NOT finish lifting the pelvis here.
        // The remaining body rise belongs to Join, together with the trailing foot.
        Vector3 supportPosition = useTwoStageBodyRise
            ? leadEndPosition
            : (targetStep == 0 ? leadEndPosition : CalculateLeadSupportPosition());

        if (!useTwoStageBodyRise && targetStep != 0)
        {
            yield return SettleBodyOverLeadFoot(leadEndPosition, supportPosition);
        }

        stableRootPosition = supportPosition;
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

        // Step 0 is intentionally independent from later stairs.
        // It starts from the ground/Idle pose, so applying the same full forward
        // destination used by later stairs pushes the character too far into step 1.
        //
        // On the first stair only:
        // - vertical movement completes fully;
        // - forward movement uses its own share.
        Vector3 joinEndPosition = GetJoinRootEndPosition(targetStep);

        bool succeeded = false;
        yield return PlayAnimationPhase(
            GetJoinStateName(joinFoot),
            joinFoot,
            stableRootPosition,
            joinEndPosition,
            joinMoveStart,
            joinMoveEnd,
            joinFootReleaseTime,
            joinFootPlantBlendStart,
            GetJoinCompletionTime(),
            0f,
            holdCompletedJoinPose && !returnToIdleAfterCompletedStep,
            result => succeeded = result
        );

        if (!succeeded)
        {
            isAnimating = false;
            yield break;
        }

        stableRootPosition = joinEndPosition;
        SetFootStepIndex(joinFoot, targetStep);
        waitingForOppositeFoot = false;
        pendingTargetStepIndex = -1;
        isAnimating = false;

        FinishCompletedStepPose();
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

        Vector3 leadEndPosition = CalculateLeadEndPosition();

        bool leadSucceeded = false;
        yield return PlayAnimationPhase(
            GetLeadStateName(leadFoot),
            leadFoot,
            stableRootPosition,
            leadEndPosition,
            leadMoveStart,
            leadMoveEnd,
            leadFootReleaseTime,
            leadFootPlantBlendStart,
            leadPhaseCompletionTime,
            GetLeadAnimationStartTime(targetStep),
            true,
            result => leadSucceeded = result
        );

        if (!leadSucceeded)
        {
            isAnimating = false;
            yield break;
        }

        Vector3 supportPosition = useTwoStageBodyRise
            ? leadEndPosition
            : (targetStep == 0 ? leadEndPosition : CalculateLeadSupportPosition());

        if (!useTwoStageBodyRise && targetStep != 0)
        {
            yield return SettleBodyOverLeadFoot(leadEndPosition, supportPosition);
        }

        stableRootPosition = supportPosition;
        SetFootStepIndex(leadFoot, targetStep);

        if (automaticJoinDelay > 0f)
        {
            yield return new WaitForSeconds(automaticJoinDelay);
        }

        FootSide joinFoot = Opposite(leadFoot);
        Vector3 joinEndPosition = GetJoinRootEndPosition(targetStep);
        bool joinSucceeded = false;

        yield return PlayAnimationPhase(
            GetJoinStateName(joinFoot),
            joinFoot,
            stableRootPosition,
            joinEndPosition,
            joinMoveStart,
            joinMoveEnd,
            joinFootReleaseTime,
            joinFootPlantBlendStart,
            GetJoinCompletionTime(),
            0f,
            holdCompletedJoinPose && !returnToIdleAfterCompletedStep,
            result => joinSucceeded = result
        );

        if (!joinSucceeded)
        {
            isAnimating = false;
            yield break;
        }

        stableRootPosition = joinEndPosition;
        rightFootStepIndex = targetStep;
        leftFootStepIndex = targetStep;
        pendingTargetStepIndex = -1;
        waitingForOppositeFoot = false;
        isAnimating = false;

        FinishCompletedStepPose();
        CheckForPathCompletion();

        Debug.Log(
            $"Automatic complete step finished | Lead: {leadFoot} | Both feet: step {targetStep}",
            this
        );
    }

    private Vector3 GetJoinRootEndPosition(int targetStep)
    {
        // Fix 06: the supplied scene still serializes firstStepJoinForwardShare = 0.11.
        // That legacy patch completes only 11% of the remaining planar motion on step 0,
        // leaving the root/trailing foot behind. Step 0 now normally uses the same
        // geometry-based destination as every later stair.
        if (targetStep != 0 || !useFirstStepJoinForwardOverride)
        {
            return pendingRootPosition;
        }

        Vector3 remaining = pendingRootPosition - stableRootPosition;
        Vector3 vertical = Vector3.Project(remaining, Vector3.up);
        Vector3 planar = remaining - vertical;

        return stableRootPosition
               + planar * firstStepJoinForwardShare
               + vertical;
    }

    private Vector3 CalculateLeadEndPosition()
    {
        Vector3 completeDisplacement = pendingRootPosition - stableRootPosition;
        Vector3 verticalDisplacement = Vector3.Project(completeDisplacement, Vector3.up);
        Vector3 planarDisplacement = completeDisplacement - verticalDisplacement;

        float verticalShare = useTwoStageBodyRise
            ? splitStanceVerticalShare
            : leadVerticalShare;

        return stableRootPosition
               + planarDisplacement * leadForwardShare
               + verticalDisplacement * verticalShare;
    }

    private Vector3 CalculateLeadSupportPosition()
    {
        if (!enableLeadSupportSettle)
        {
            return CalculateLeadEndPosition();
        }

        Vector3 completeDisplacement = pendingRootPosition - stableRootPosition;
        Vector3 verticalDisplacement = Vector3.Project(completeDisplacement, Vector3.up);
        Vector3 planarDisplacement = completeDisplacement - verticalDisplacement;

        float safeForwardShare = Mathf.Max(leadForwardShare, leadSupportForwardShare);
        float safeVerticalShare = Mathf.Max(leadVerticalShare, leadSupportVerticalShare);

        return stableRootPosition
               + planarDisplacement * safeForwardShare
               + verticalDisplacement * safeVerticalShare;
    }

    private IEnumerator SettleBodyOverLeadFoot(
        Vector3 startPosition,
        Vector3 supportPosition
    )
    {
        if (!enableLeadSupportSettle ||
            leadSupportSettleDuration <= 0f ||
            Vector3.SqrMagnitude(supportPosition - startPosition) <= 0.000001f)
        {
            movementRoot.position = supportPosition;
            yield break;
        }

        // At this point the lead animation is frozen and both feet are locked in world space.
        // Moving the root transfers the pelvis over the planted lead foot, which opens the
        // support knee instead of leaving the character in a crouched split stance.
        float elapsed = 0f;
        while (elapsed < leadSupportSettleDuration)
        {
            float normalized = Mathf.Clamp01(elapsed / leadSupportSettleDuration);
            float eased = SmoothStep01(normalized);

            movementRoot.position = Vector3.LerpUnclamped(
                startPosition,
                supportPosition,
                eased
            );

            if (preserveInitialRotation)
            {
                movementRoot.rotation = stableRootRotation;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        movementRoot.position = supportPosition;
        if (preserveInitialRotation)
        {
            movementRoot.rotation = stableRootRotation;
        }
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

        // IMPORTANT:
        // The planted-foot target is raised by footSoleExtraClearance + plantedFootOffset.
        // The body/root must receive the same final vertical correction; otherwise both
        // feet are IK-locked higher than the body expects and the only way the Humanoid
        // solver can reach them is by bending both knees.
        float finalPlantedFootLift =
            footSoleExtraClearance
            + plantedFootOffset
            + wholeCharacterStepLift;

        float targetFeetCenterY =
            stepTopCenter.y
            + initialAverageFootClearance
            + finalPlantedFootLift
            + bodyHeightOffsetOnStep;

        float verticalDistance = targetFeetCenterY - feetCenter.y;

        rootDisplacement =
            climbDirection * forwardDistance + Vector3.up * verticalDistance;

        Debug.Log(
            $"Root destination prepared | Target: {targetStep} | Forward: {forwardDistance:F3} | Up: {verticalDistance:F3} | Final foot lift: {finalPlantedFootLift:F3}",
            this
        );

        return true;
    }

    private IEnumerator PlayAnimationPhase(
        string stateName,
        FootSide movingFoot,
        Vector3 startPosition,
        Vector3 endPosition,
        float moveStart,
        float moveEnd,
        float footReleaseTime,
        float plantBlendStart,
        float phaseCompletionTime,
        float animationStartNormalizedTime,
        bool freezeFinalPose,
        System.Action<bool> onComplete
    )
    {
        CancelCompletedPoseIkRelease();
        PrepareFootLocksForMovement(movingFoot);

        movingFootSurfaceGuardActive = enableMovingFootSurfaceGuard;
        movingFootSurfaceGuardFoot = movingFoot;

        bool isLeadPhase =
            stateName == rightLeadStateName ||
            stateName == leftLeadStateName;
        bool isJoinPhase =
            stateName == rightJoinStateName ||
            stateName == leftJoinStateName;

        if (isLeadPhase)
        {
            BeginLeadTakeoffGuard(movingFoot);
            EndProceduralJoinSwing();
        }
        else
        {
            EndLeadTakeoffGuard();
            if (isJoinPhase)
            {
                BeginProceduralJoinSwing(movingFoot);
            }
            else
            {
                EndProceduralJoinSwing();
            }
        }

        // Keep the moving foot exactly where it is through the clip's anticipation
        // frames. The previous patch released the IK lock in a single frame; if the
        // animation foot was still slightly behind at that exact sample, the shoe
        // visibly snapped backward. Keep the same lock, then fade it out smoothly.
        if (enableFootPlantIK)
        {
            LockFootAtCurrentPose(movingFoot);
        }

        FootLockState movingFootLock = enableFootPlantIK ? GetFootLock(movingFoot) : null;
        float releaseStartPositionWeight = movingFootLock != null
            ? movingFootLock.PositionWeight
            : 0f;
        float releaseStartRotationWeight = movingFootLock != null
            ? movingFootLock.RotationWeight
            : 0f;
        bool movingFootReleased = !enableFootPlantIK;

        animator.applyRootMotion = false;
        animator.speed = Mathf.Clamp(movementAnimationSpeed, 0.75f, 1.50f);

        int stateHash = Animator.StringToHash(stateName);

        // Lead clips contain a short recorded anticipation where the torso and swing
        // foot travel backward before take-off. Starting the Lead state after that
        // section removes the step-to-step recoil without changing the clip asset.
        float startNormalizedTime = Mathf.Clamp01(animationStartNormalizedTime);
        float effectiveFootReleaseTime = isLeadPhase
            ? Mathf.Max(footReleaseTime, startNormalizedTime + leadExtraFootHoldAfterStart)
            : footReleaseTime;

        animator.CrossFadeInFixedTime(
            stateName,
            transitionDuration,
            0,
            startNormalizedTime
        );

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

                if (isLeadPhase)
                {
                    UpdateLeadTakeoffGuard(normalized);
                }

                if (isJoinPhase)
                {
                    UpdateProceduralJoinSwing(
                        normalized,
                        effectiveFootReleaseTime,
                        phaseCompletionTime
                    );
                }

                float effectiveMoveEnd = Mathf.Min(moveEnd, phaseCompletionTime);
                float moveProgress = Mathf.InverseLerp(moveStart, effectiveMoveEnd, normalized);
                float easedProgress = SmoothStep01(moveProgress);

                // Give the trailing knee time to fold before the pelvis/root rises.
                // If hip and ankle rise together from frame one, the limb remains
                // near full extension and the avatar compensates with an ugly pose.
                if (isJoinPhase && enableProceduralJoinSwing && proceduralJoinSwingActive)
                {
                    if (useSeparatedJoinRootAxes)
                    {
                        float forwardProgress = GetProceduralJoinForwardProgress(
                            normalized,
                            effectiveFootReleaseTime,
                            phaseCompletionTime
                        );
                        float verticalProgress = GetProceduralJoinVerticalProgress(
                            normalized,
                            effectiveFootReleaseTime,
                            phaseCompletionTime
                        );

                        Vector3 delta = endPosition - startPosition;
                        Vector3 planarDelta = new Vector3(delta.x, 0f, delta.z);
                        movementRoot.position =
                            startPosition +
                            planarDelta * forwardProgress +
                            Vector3.up * (delta.y * verticalProgress);
                    }
                    else
                    {
                        easedProgress = GetProceduralJoinRootProgress(
                            normalized,
                            effectiveFootReleaseTime,
                            phaseCompletionTime
                        );

                        movementRoot.position = Vector3.LerpUnclamped(
                            startPosition,
                            endPosition,
                            easedProgress
                        );
                    }
                }
                else
                {
                    movementRoot.position = Vector3.LerpUnclamped(
                        startPosition,
                        endPosition,
                        easedProgress
                    );
                }

                if (preserveInitialRotation)
                {
                    movementRoot.rotation = stableRootRotation;
                }

                if (!movingFootReleased && normalized >= effectiveFootReleaseTime)
                {
                    float releaseEnd = Mathf.Min(
                        effectiveFootReleaseTime + movingFootReleaseBlendDuration,
                        Mathf.Max(effectiveFootReleaseTime + 0.001f, plantBlendStart - 0.01f)
                    );

                    float releaseProgress = Mathf.InverseLerp(
                        effectiveFootReleaseTime,
                        releaseEnd,
                        normalized
                    );
                    releaseProgress = SmoothStep01(releaseProgress);

                    if (releaseProgress >= 0.999f)
                    {
                        ReleaseFootLock(movingFoot);
                        movingFootReleased = true;
                    }
                    else if (movingFootLock != null)
                    {
                        // Fade from the exact planted world-space pose into the
                        // animation instead of switching 1 -> 0 in one frame.
                        // This preserves continuity at take-off and removes most
                        // of the visible backward pop.
                        movingFootLock.Active = true;
                        movingFootLock.PositionWeight = Mathf.Lerp(
                            releaseStartPositionWeight,
                            0f,
                            releaseProgress
                        );
                        movingFootLock.RotationWeight = Mathf.Lerp(
                            releaseStartRotationWeight,
                            0f,
                            releaseProgress
                        );
                    }
                }

                float plantProgress = Mathf.InverseLerp(
                    plantBlendStart,
                    phaseCompletionTime,
                    normalized
                );
                plantProgress = SmoothStep01(plantProgress);
                if (movingFootReleased)
                {
                    UpdateMovingFootPlant(movingFoot, plantProgress);
                }

                if (!animator.IsInTransition(0) &&
                    stateInfo.normalizedTime >= phaseCompletionTime)
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
            EndLeadTakeoffGuard();
            EndProceduralJoinSwing();
            movingFootSurfaceGuardActive = false;
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

        EndLeadTakeoffGuard();
        EndProceduralJoinSwing();
        movingFootSurfaceGuardActive = false;
        LockFootAtPendingTarget(movingFoot);

        if (freezeFinalPose)
        {
            // Freeze the pose that is already visible in this frame.
            // Re-sampling the state here can re-apply humanoid/root translation
            // and cause the visible post-animation forward snap.
            animator.speed = 0f;
        }

        onComplete?.Invoke(true);
    }

    private float GetLeadAnimationStartTime(int targetStep)
    {
        return targetStep <= 0
            ? leadAnimationStartTime
            : stairLeadAnimationStartTime;
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
        // Mark the component as current without replacing any Inspector tuning values.
        controllerDataVersion = CurrentControllerDataVersion;

        leadPhaseCompletionTime = Mathf.Clamp(leadPhaseCompletionTime, 0.5f, 0.99f);
        animationCompletionTime = Mathf.Clamp(animationCompletionTime, 0.5f, 1f);
        completedJoinPoseTime = Mathf.Clamp(completedJoinPoseTime, 0.65f, 0.95f);
        float effectiveJoinCompletionTime = GetJoinCompletionTime();

        leadMoveEnd = Mathf.Clamp(
            Mathf.Max(leadMoveEnd, leadMoveStart + 0.01f),
            leadMoveStart + 0.01f,
            leadPhaseCompletionTime
        );
        joinMoveEnd = Mathf.Clamp(
            Mathf.Max(joinMoveEnd, joinMoveStart + 0.01f),
            joinMoveStart + 0.01f,
            effectiveJoinCompletionTime
        );

        leadFootPlantBlendStart = Mathf.Clamp(
            leadFootPlantBlendStart,
            0f,
            Mathf.Max(0f, leadPhaseCompletionTime - 0.01f)
        );
        joinFootPlantBlendStart = Mathf.Clamp(
            joinFootPlantBlendStart,
            0f,
            Mathf.Max(0f, effectiveJoinCompletionTime - 0.01f)
        );

        firstStepJoinForwardShare = Mathf.Clamp01(firstStepJoinForwardShare);
        joinPelvisMaxExtraForward = Mathf.Clamp(joinPelvisMaxExtraForward, 0f, 0.08f);
        joinPelvisForwardClampStrength = Mathf.Clamp01(joinPelvisForwardClampStrength);
        joinPelvisClampBlendInEnd = Mathf.Clamp(joinPelvisClampBlendInEnd, 0.05f, 0.40f);
        joinPelvisClampReleaseStart = Mathf.Clamp(joinPelvisClampReleaseStart, 0.55f, 0.95f);
        wholeCharacterStepLift = Mathf.Max(0f, wholeCharacterStepLift);
        splitStanceVerticalShare = Mathf.Clamp(splitStanceVerticalShare, 0f, 0.65f);
        leadFootReleaseTime = Mathf.Clamp(leadFootReleaseTime, 0f, 0.35f);
        joinFootReleaseTime = Mathf.Clamp(joinFootReleaseTime, 0f, 0.35f);
        movingFootReleaseBlendDuration = Mathf.Clamp(
            movingFootReleaseBlendDuration,
            0.01f,
            0.25f
        );
        leadAnimationStartTime = Mathf.Clamp(leadAnimationStartTime, 0f, 0.30f);
        stairLeadAnimationStartTime = Mathf.Clamp(stairLeadAnimationStartTime, 0f, 0.35f);
        leadExtraFootHoldAfterStart = Mathf.Clamp(leadExtraFootHoldAfterStart, 0f, 0.15f);
        completedStepIdleBlendDuration = Mathf.Clamp(
            completedStepIdleBlendDuration,
            0f,
            0.25f
        );
        completedIdleFootReleaseDuration = Mathf.Clamp(
            completedIdleFootReleaseDuration,
            0.02f,
            0.25f
        );
        completedIdleMaxRootHeightCorrection = Mathf.Clamp(
            completedIdleMaxRootHeightCorrection,
            0f,
            0.20f
        );
        leadTakeoffGuardReleaseStart = Mathf.Clamp(
            leadTakeoffGuardReleaseStart,
            0f,
            0.40f
        );
        leadTakeoffGuardReleaseEnd = Mathf.Clamp(
            leadTakeoffGuardReleaseEnd,
            Mathf.Max(0.10f, leadTakeoffGuardReleaseStart + 0.01f),
            0.60f
        );
        leadForwardShare = Mathf.Clamp(leadForwardShare, 0.1f, 0.9f);
        leadVerticalShare = Mathf.Clamp(leadVerticalShare, 0.5f, 1f);
        animationTimeout = Mathf.Max(0.5f, animationTimeout);
        plantedFootPositionWeight = Mathf.Clamp01(plantedFootPositionWeight);
        footGroundRaycastDistance = Mathf.Max(0.1f, footGroundRaycastDistance);
        footSoleExtraClearance = Mathf.Max(0f, footSoleExtraClearance);
        movingFootSurfaceMargin = Mathf.Clamp(movingFootSurfaceMargin, 0f, 0.03f);
        movingFootSurfaceMaxCorrection = Mathf.Clamp(movingFootSurfaceMaxCorrection, 0.01f, 0.20f);

        joinSwingLiftHeight = Mathf.Clamp(joinSwingLiftHeight, 0.02f, 0.25f);
        joinSwingLiftPhaseEnd = Mathf.Clamp(joinSwingLiftPhaseEnd, 0.15f, 0.55f);
        joinSwingTravelPhaseEnd = Mathf.Clamp(
            joinSwingTravelPhaseEnd,
            joinSwingLiftPhaseEnd + 0.05f,
            0.92f
        );
        joinKneeHintForwardDistance = Mathf.Clamp(joinKneeHintForwardDistance, 0.02f, 0.45f);
        joinKneeHintWeight = Mathf.Clamp01(joinKneeHintWeight);
        joinFlexMaxReachRatio = Mathf.Clamp(joinFlexMaxReachRatio, 0.65f, 0.98f);
        movementAnimationSpeed = Mathf.Clamp(movementAnimationSpeed, 0.75f, 1.50f);
        joinEarlyRootProgress = Mathf.Clamp(joinEarlyRootProgress, 0f, 0.30f);
        joinTorsoStabilizationWeight = Mathf.Clamp01(joinTorsoStabilizationWeight);
        joinTorsoStabilizationStart = Mathf.Clamp(joinTorsoStabilizationStart, 0f, 0.40f);
        joinTorsoStabilizationEnd = Mathf.Clamp(joinTorsoStabilizationEnd, 0.55f, 1f);
        joinRootRiseHoldProgress = Mathf.Clamp(joinRootRiseHoldProgress, 0f, 0.50f);
        joinForwardEarlyShare = Mathf.Clamp(joinForwardEarlyShare, 0f, 0.35f);
        joinVerticalRiseDelay = Mathf.Clamp(joinVerticalRiseDelay, 0f, 0.40f);
        joinVerticalEarlyShare = Mathf.Clamp(joinVerticalEarlyShare, 0f, 0.25f);
        joinKneeHintBaseForward = Mathf.Clamp(joinKneeHintBaseForward, 0.02f, 0.50f);
        standingFootSurfaceMargin = Mathf.Clamp(standingFootSurfaceMargin, 0f, 0.03f);
        soleSurfaceProbeRadius = Mathf.Clamp(soleSurfaceProbeRadius, 0.005f, 0.06f);
        soleToeTipProbeDistance = Mathf.Clamp(soleToeTipProbeDistance, 0f, 0.10f);
        soleHeelProbeDistance = Mathf.Clamp(soleHeelProbeDistance, 0f, 0.10f);
        surfaceContactRotationWeight = Mathf.Clamp01(surfaceContactRotationWeight);

        safeFootRotationWeight = Mathf.Clamp01(safeFootRotationWeight);
        maxFootRotationCorrection = Mathf.Clamp(maxFootRotationCorrection, 0f, 90f);
    }
#endif
}