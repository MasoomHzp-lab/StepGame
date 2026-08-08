using System.Collections;
using UnityEngine;

/// <summary>
/// Step sequencing, animation playback and root/body movement for StairClimbControllerV2.
/// Split from the previous large partial file; behaviour is unchanged.
/// </summary>
public sealed partial class StairClimbControllerV2
{
    [Header("First Step Only")]
    [Tooltip("Only for step 0: how much of the remaining FORWARD root movement is allowed during Join. Height still completes fully.")]
    [SerializeField, Range(0f, 1f)] private float firstStepJoinForwardShare = 0.45f;

    [Header("Whole Character Step Height")]
    [Tooltip("Raises the whole character and both foot IK targets together above every stair tread. Use this to keep shoe soles out of the stair without bending the knees.")]
    [SerializeField, Min(0f)] private float wholeCharacterStepLift = 0.02f;

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
            leadFootPlantBlendStart,
            leadPhaseCompletionTime,
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
        Vector3 supportPosition = targetStep == 0
            ? leadEndPosition
            : CalculateLeadSupportPosition();

        if (targetStep != 0)
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
            joinFootPlantBlendStart,
            GetJoinCompletionTime(),
            holdCompletedJoinPose,
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
            leadFootPlantBlendStart,
            leadPhaseCompletionTime,
            true,
            result => leadSucceeded = result
        );

        if (!leadSucceeded)
        {
            isAnimating = false;
            yield break;
        }

        Vector3 supportPosition = targetStep == 0
            ? leadEndPosition
            : CalculateLeadSupportPosition();

        if (targetStep != 0)
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
            joinFootPlantBlendStart,
            GetJoinCompletionTime(),
            holdCompletedJoinPose,
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
        if (targetStep != 0)
        {
            return pendingRootPosition;
        }

        Vector3 remaining = pendingRootPosition - stableRootPosition;
        Vector3 vertical = Vector3.Project(remaining, Vector3.up);
        Vector3 planar = remaining - vertical;

        // Keep all vertical movement so both feet reach the tread height,
        // but reduce only the forward travel on the first stair.
        return stableRootPosition
               + planar * firstStepJoinForwardShare
               + vertical;
    }

    private Vector3 CalculateLeadEndPosition()
    {
        Vector3 completeDisplacement = pendingRootPosition - stableRootPosition;
        Vector3 verticalDisplacement = Vector3.Project(completeDisplacement, Vector3.up);
        Vector3 planarDisplacement = completeDisplacement - verticalDisplacement;

        return stableRootPosition
               + planarDisplacement * leadForwardShare
               + verticalDisplacement * leadVerticalShare;
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
        float plantBlendStart,
        float phaseCompletionTime,
        bool freezeFinalPose,
        System.Action<bool> onComplete
    )
    {
        CancelCompletedPoseIkRelease();
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
                float effectiveMoveEnd = Mathf.Min(moveEnd, phaseCompletionTime);
                float moveProgress = Mathf.InverseLerp(moveStart, effectiveMoveEnd, normalized);
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
                    plantBlendStart,
                    phaseCompletionTime,
                    normalized
                );
                plantProgress = SmoothStep01(plantProgress);
                UpdateMovingFootPlant(movingFoot, plantProgress);

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
            // Freeze the pose that is already visible in this frame.
            // Re-sampling the state here can re-apply humanoid/root translation
            // and cause the visible post-animation forward snap.
            animator.speed = 0f;
        }

        onComplete?.Invoke(true);
    }

    private float GetJoinCompletionTime()
    {
        return holdCompletedJoinPose
            ? Mathf.Min(completedJoinPoseTime, animationCompletionTime)
            : animationCompletionTime;
    }

    private void FinishCompletedStepPose()
    {
        if (holdCompletedJoinPose)
        {
            // The Join animation is already sampled on the upright standing pose.
            animator.applyRootMotion = false;
            animator.speed = 0f;

            // Do not remove both foot locks in a single frame.
            // A hard 1 -> 0 IK change creates the visible knee/ankle pop seen at landing.
            BeginCompletedPoseIkRelease();
            return;
        }

        ReturnToIdle();
    }

    private void ReturnToIdle()
    {
        // Do not release either foot here. Both planted locks remain active
        // throughout the CrossFade and while Idle is playing.
        animator.speed = 1f;
        animator.applyRootMotion = false;
        animator.CrossFadeInFixedTime(idleStateName, transitionDuration, 0, 0f);
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
        wholeCharacterStepLift = Mathf.Max(0f, wholeCharacterStepLift);
        leadForwardShare = Mathf.Clamp(leadForwardShare, 0.1f, 0.9f);
        leadVerticalShare = Mathf.Clamp(leadVerticalShare, 0.5f, 1f);
        animationTimeout = Mathf.Max(0.5f, animationTimeout);
        plantedFootPositionWeight = Mathf.Clamp01(plantedFootPositionWeight);
        footGroundRaycastDistance = Mathf.Max(0.1f, footGroundRaycastDistance);
        footSoleExtraClearance = Mathf.Max(0f, footSoleExtraClearance);

        safeFootRotationWeight = Mathf.Clamp01(safeFootRotationWeight);
        maxFootRotationCorrection = Mathf.Clamp(maxFootRotationCorrection, 0f, 90f);
    }
#endif
}