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

        // Toe bones sit much closer to the actual front sole than the ankle/Foot bone.
        // Calibrating them separately lets the surface guard protect the visible shoe tip.
        initialRightToeClearance = rightToeBone != null
            ? MeasurePointClearance(rightToeBone.position, estimatedStartSurfaceY)
            : initialRightFootClearance;
        initialLeftToeClearance = leftToeBone != null
            ? MeasurePointClearance(leftToeBone.position, estimatedStartSurfaceY)
            : initialLeftFootClearance;

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

    private float GetInitialFootClearance(FootSide foot)
    {
        return foot == FootSide.Right
            ? initialRightFootClearance
            : initialLeftFootClearance;
    }

    private float GetInitialToeClearance(FootSide foot)
    {
        return foot == FootSide.Right
            ? initialRightToeClearance
            : initialLeftToeClearance;
    }

    private Transform GetToeBone(FootSide foot)
    {
        return foot == FootSide.Right ? rightToeBone : leftToeBone;
    }

    private Vector3 GetFootForwardOnPlane(FootSide foot, Vector3 estimatedFootPosition)
    {
        Transform toeBone = GetToeBone(foot);
        if (toeBone != null)
        {
            Vector3 direction = toeBone.position - estimatedFootPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                return direction.normalized;
            }
        }

        return climbDirection.sqrMagnitude > 0.0001f
            ? climbDirection.normalized
            : movementRoot.forward;
    }

    private bool TryGetSurfaceYBelowPoint(Vector3 point, out float surfaceY)
    {
        surfaceY = 0f;

        const float originLift = 0.35f;
        Vector3 origin = point + Vector3.up * originLift;
        float maxDistance = Mathf.Max(0.1f, footGroundRaycastDistance) + originLift;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            maxDistance,
            footGroundRaycastMask,
            QueryTriggerInteraction.Ignore
        );

        bool found = false;
        float highest = float.NegativeInfinity;

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

            // Reject geometry far above the current animated foot position.
            if (hit.point.y > point.y + movingFootSurfaceMaxCorrection)
            {
                continue;
            }

            if (!found || hit.point.y > highest)
            {
                found = true;
                highest = hit.point.y;
            }
        }

        if (!found)
        {
            return false;
        }

        surfaceY = highest;
        return true;
    }

    private bool TryGetSurfaceYNearPoint(Vector3 point, out float surfaceY)
    {
        surfaceY = 0f;

        // A sphere cast catches the top edge slightly before a toe/heel sample crosses
        // the tread boundary. This is much more reliable than one zero-radius ray.
        float radius = enableMultiPointSoleGuard
            ? Mathf.Max(0.001f, soleSurfaceProbeRadius)
            : 0.001f;
        const float originLift = 0.35f;
        Vector3 origin = point + Vector3.up * (originLift + radius);
        float maxDistance = Mathf.Max(0.1f, footGroundRaycastDistance) + originLift + radius;

        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            radius,
            Vector3.down,
            maxDistance,
            footGroundRaycastMask,
            QueryTriggerInteraction.Ignore
        );

        bool found = false;
        float highest = float.NegativeInfinity;

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

            // We only want walkable/upward-facing tread surfaces, not the vertical riser.
            if (hit.normal.y < 0.45f)
            {
                continue;
            }

            // Ignore unrelated geometry far above the animated sole sample.
            if (hit.point.y > point.y + movingFootSurfaceMaxCorrection + radius)
            {
                continue;
            }

            if (!found || hit.point.y > highest)
            {
                found = true;
                highest = hit.point.y;
            }
        }

        if (!found)
        {
            // Fall back to the exact ray method used by Fix 06.
            return TryGetSurfaceYBelowPoint(point, out surfaceY);
        }

        surfaceY = highest;
        return true;
    }

    private float CalculatePointSurfaceLift(
        Vector3 point,
        float calibratedClearance,
        float margin
    )
    {
        if (!TryGetSurfaceYNearPoint(point, out float surfaceY))
        {
            return 0f;
        }

        float minimumY = surfaceY + Mathf.Max(0f, calibratedClearance) + margin;
        float correction = minimumY - point.y;
        if (correction <= 0f || correction > movingFootSurfaceMaxCorrection)
        {
            return 0f;
        }

        return correction;
    }

    private float CalculateMovingSoleSurfaceLift(
        FootSide foot,
        Vector3 animatedFootPosition,
        Vector3 effectiveFootPosition
    )
    {
        float requiredLift = CalculatePointSurfaceLift(
            effectiveFootPosition,
            GetInitialFootClearance(foot),
            movingFootSurfaceMargin
        );

        if (!enableMultiPointSoleGuard)
        {
            return requiredLift;
        }

        Vector3 footTranslation = effectiveFootPosition - animatedFootPosition;
        Transform toeBone = GetToeBone(foot);
        Vector3 forward = GetFootForwardOnPlane(foot, animatedFootPosition);

        if (toeBone != null)
        {
            Vector3 estimatedToe = toeBone.position + footTranslation;
            requiredLift = Mathf.Max(
                requiredLift,
                CalculatePointSurfaceLift(
                    estimatedToe,
                    GetInitialToeClearance(foot),
                    movingFootSurfaceMargin
                )
            );

            if (soleToeTipProbeDistance > 0f)
            {
                Vector3 toeTip = estimatedToe + forward * soleToeTipProbeDistance;
                requiredLift = Mathf.Max(
                    requiredLift,
                    CalculatePointSurfaceLift(
                        toeTip,
                        GetInitialToeClearance(foot),
                        movingFootSurfaceMargin
                    )
                );
            }
        }

        if (soleHeelProbeDistance > 0f)
        {
            Vector3 heelPoint = effectiveFootPosition - forward * soleHeelProbeDistance;
            requiredLift = Mathf.Max(
                requiredLift,
                CalculatePointSurfaceLift(
                    heelPoint,
                    GetInitialFootClearance(foot),
                    movingFootSurfaceMargin
                )
            );
        }

        return requiredLift;
    }

    private float CalculateStandingSurfaceLift()
    {
        float requiredLift = 0f;
        requiredLift = Mathf.Max(requiredLift, CalculateFootSurfaceLift(FootSide.Right, rightFootBone));
        requiredLift = Mathf.Max(requiredLift, CalculateFootSurfaceLift(FootSide.Left, leftFootBone));
        return requiredLift;
    }

    private float CalculateFootSurfaceLift(FootSide foot, Transform footBone)
    {
        if (footBone == null)
        {
            return 0f;
        }

        float requiredLift = CalculatePointSurfaceLift(
            footBone.position,
            GetInitialFootClearance(foot),
            standingFootSurfaceMargin
        );

        if (!enableMultiPointSoleGuard)
        {
            return requiredLift;
        }

        Transform toeBone = GetToeBone(foot);
        Vector3 forward = GetFootForwardOnPlane(foot, footBone.position);

        if (toeBone != null)
        {
            requiredLift = Mathf.Max(
                requiredLift,
                CalculatePointSurfaceLift(
                    toeBone.position,
                    GetInitialToeClearance(foot),
                    standingFootSurfaceMargin
                )
            );

            if (soleToeTipProbeDistance > 0f)
            {
                Vector3 toeTip = toeBone.position + forward * soleToeTipProbeDistance;
                requiredLift = Mathf.Max(
                    requiredLift,
                    CalculatePointSurfaceLift(
                        toeTip,
                        GetInitialToeClearance(foot),
                        standingFootSurfaceMargin
                    )
                );
            }
        }

        if (soleHeelProbeDistance > 0f)
        {
            Vector3 heelPoint = footBone.position - forward * soleHeelProbeDistance;
            requiredLift = Mathf.Max(
                requiredLift,
                CalculatePointSurfaceLift(
                    heelPoint,
                    GetInitialFootClearance(foot),
                    standingFootSurfaceMargin
                )
            );
        }

        return requiredLift;
    }

    private float MeasurePointClearance(Vector3 point, float fallbackSurfaceY)
    {
        float fallbackClearance = Mathf.Max(0f, point.y - fallbackSurfaceY);
        if (!useGroundRaycastForFootClearance)
        {
            return fallbackClearance;
        }

        const float originLift = 0.15f;
        Vector3 origin = point + Vector3.up * originLift;
        float maxDistance = Mathf.Max(0.1f, footGroundRaycastDistance) + originLift;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            maxDistance,
            footGroundRaycastMask,
            QueryTriggerInteraction.Ignore
        );

        bool found = false;
        float highest = float.NegativeInfinity;
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

            if (hit.point.y > point.y + 0.01f)
            {
                continue;
            }

            if (!found || hit.point.y > highest)
            {
                found = true;
                highest = hit.point.y;
            }
        }

        return found ? Mathf.Max(0f, point.y - highest) : fallbackClearance;
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