using System.Collections;
using UnityEngine;

/// <summary>
/// Foot target calculation, Foot IK locking/calibration and smooth IK release
/// for StairClimbControllerV2.
/// Split from the previous large partial file; behaviour is unchanged.
/// </summary>
public sealed partial class StairClimbControllerV2
{
    [Header("Completed Pose IK Release")]
    [Tooltip("How long both planted feet fade from full IK to animation control after the Join pose is frozen.")]
    [SerializeField, Min(0f)] private float completedPoseIkReleaseDuration = 0.12f;

    private Coroutine completedPoseIkReleaseCoroutine;

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
            initialRightFootClearance
            + footSoleExtraClearance
            + movingFootOffset
            + wholeCharacterStepLift
        );

        pendingRightPlantedTarget = BuildFootTarget(
            stepTopCenter,
            sharedForwardOffset,
            initialRightLateralOffset + sharedLateralOffset,
            initialRightFootClearance
            + footSoleExtraClearance
            + plantedFootOffset
            + wholeCharacterStepLift
        );

        pendingLeftMovingTarget = BuildFootTarget(
            stepTopCenter,
            sharedForwardOffset,
            initialLeftLateralOffset + sharedLateralOffset,
            initialLeftFootClearance
            + footSoleExtraClearance
            + movingFootOffset
            + wholeCharacterStepLift
        );

        pendingLeftPlantedTarget = BuildFootTarget(
            stepTopCenter,
            sharedForwardOffset,
            initialLeftLateralOffset + sharedLateralOffset,
            initialLeftFootClearance
            + footSoleExtraClearance
            + plantedFootOffset
            + wholeCharacterStepLift
        );

        Quaternion footRotationOffset = Quaternion.Euler(footRotationOffsetEuler);

        pendingRightTargetRotation =
            stableRootRotation * initialRightFootRotationRelativeToRoot * footRotationOffset;
        pendingLeftTargetRotation =
            stableRootRotation * initialLeftFootRotationRelativeToRoot * footRotationOffset;

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
        footLock.RotationWeight = safeFootRotationWeight * progress;
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
        footLock.RotationWeight = safeFootRotationWeight;
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
        footLock.RotationWeight = safeFootRotationWeight;
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

    private void BeginCompletedPoseIkRelease()
    {
        CancelCompletedPoseIkRelease();

        if (!enableFootPlantIK || completedPoseIkReleaseDuration <= 0f)
        {
            ClearAllFootLocks();
            return;
        }

        completedPoseIkReleaseCoroutine =
            StartCoroutine(FadeOutCompletedPoseFootIK());
    }

    private IEnumerator FadeOutCompletedPoseFootIK()
    {
        float rightStartPositionWeight = rightFootLock.PositionWeight;
        float rightStartRotationWeight = rightFootLock.RotationWeight;
        float leftStartPositionWeight = leftFootLock.PositionWeight;
        float leftStartRotationWeight = leftFootLock.RotationWeight;

        bool rightWasActive = rightFootLock.Active;
        bool leftWasActive = leftFootLock.Active;

        float elapsed = 0f;

        while (elapsed < completedPoseIkReleaseDuration)
        {
            float normalized =
                Mathf.Clamp01(elapsed / completedPoseIkReleaseDuration);
            float eased = SmoothStep01(normalized);
            float remaining = 1f - eased;

            if (rightWasActive)
            {
                rightFootLock.PositionWeight =
                    rightStartPositionWeight * remaining;
                rightFootLock.RotationWeight =
                    rightStartRotationWeight * remaining;
            }

            if (leftWasActive)
            {
                leftFootLock.PositionWeight =
                    leftStartPositionWeight * remaining;
                leftFootLock.RotationWeight =
                    leftStartRotationWeight * remaining;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        ClearAllFootLocks();
        completedPoseIkReleaseCoroutine = null;
    }

    private void CancelCompletedPoseIkRelease()
    {
        if (completedPoseIkReleaseCoroutine == null)
        {
            return;
        }

        StopCoroutine(completedPoseIkReleaseCoroutine);
        completedPoseIkReleaseCoroutine = null;

        // A new movement phase will immediately rebuild the correct support-foot lock.
        ClearAllFootLocks();
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
            $"Stair Climb V2 calibration | Right clearance: {initialRightFootClearance:F3} | Left clearance: {initialLeftFootClearance:F3} | Extra sole lift: {footSoleExtraClearance:F3} | Whole character lift: {wholeCharacterStepLift:F3}",
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

}