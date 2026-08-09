using System.Collections;
using UnityEngine;

/// <summary>
/// Lead-foot guarding, Join motion/IK coordination, and completed-step pose recovery.
/// Kept separate from the main animation sequencer so torso/landing fixes remain isolated.
/// </summary>
public sealed partial class StairClimbControllerV2
{
    [Header("Lead Swing Foot Guard - No Backstep")]
    [Tooltip("Prevents the swing foot from travelling behind its exact take-off position at the start of every Lead step.")]
    [SerializeField] private bool enableLeadTakeoffGuard = true;

    [Tooltip("The guard stays fully active until this normalized Lead time.")]
    [SerializeField, Range(0f, 0.40f)] private float leadTakeoffGuardReleaseStart = 0.20f;

    [Tooltip("The guard fades out by this normalized Lead time, after the foot has genuinely started lifting/advancing.")]
    [SerializeField, Range(0.10f, 0.60f)] private float leadTakeoffGuardReleaseEnd = 0.42f;


    private bool leadTakeoffGuardActive;
    private FootSide leadTakeoffGuardFoot;
    private float leadTakeoffGuardWeight;
    private Vector3 leadTakeoffFootStartPosition;

    // Runtime moving-foot owner for collider-backed surface protection.
    private bool movingFootSurfaceGuardActive;
    private FootSide movingFootSurfaceGuardFoot;

    [Header("Procedural Join Swing - Knee Bend")]
    [Tooltip("Overrides only the trailing/Join foot with a controlled swing arc: lift first, bend the knee, travel forward, then extend and plant. Lead animation is untouched.")]
    [SerializeField] private bool enableProceduralJoinSwing = true;

    [Tooltip("Extra height above the higher of take-off/landing used at the middle of the Join swing. Increase slightly if the trailing knee still looks too straight.")]
    [SerializeField, Range(0.02f, 0.25f)] private float joinSwingLiftHeight = 0.12f;

    [Tooltip("End of the first Join sub-phase. Before this point the foot mostly rises and only moves a little forward.")]
    [SerializeField, Range(0.15f, 0.55f)] private float joinSwingLiftPhaseEnd = 0.36f;

    [Tooltip("End of the travel sub-phase. After this point the leg extends and the sole settles onto the tread.")]
    [SerializeField, Range(0.55f, 0.92f)] private float joinSwingTravelPhaseEnd = 0.78f;

    [Tooltip("How far in front of the animated knee the Humanoid IK hint is placed during Join. This encourages a visible forward knee bend instead of a straight vertical leg.")]
    [SerializeField, Range(0.02f, 0.45f)] private float joinKneeHintForwardDistance = 0.20f;

    [Tooltip("Maximum influence of the knee hint around the middle of Join.")]
    [SerializeField, Range(0f, 1f)] private float joinKneeHintWeight = 1.00f;

    [Header("Join Knee Flex Geometry")]
    [Tooltip("At peak Join flexion, limits hip-to-ankle reach to this fraction of the leg length. Lower values force a deeper knee bend. 0.82 is a natural starting point.")]
    [SerializeField, Range(0.65f, 0.98f)] private float joinFlexMaxReachRatio = 0.82f;

    [Tooltip("How much of the early Join swing completes before the body/root is allowed to rise. Holding the pelvis briefly gives the trailing knee room to fold instead of stretching straight.")]
    [SerializeField, Range(0f, 0.50f)] private float joinRootRiseHoldProgress = 0.28f;

    [Header("Natural Join Body Coordination")]
    [Tooltip("Lets the pelvis begin moving immediately during Join instead of freezing completely while the trailing knee folds.")]
    [SerializeField] private bool enableNaturalJoinBodyCoordination = true;

    [Tooltip("Fraction of the Join root displacement reached by the end of the early knee-fold phase. A small value prevents the torso from folding while preserving knee flexion.")]
    [SerializeField, Range(0f, 0.30f)] private float joinEarlyRootProgress = 0.12f;

    [Tooltip("How strongly Spine/Chest are kept near the upright split-stance pose during the middle of Join. This affects only the torso, not the knee flex geometry.")]
    [SerializeField, Range(0f, 1f)] private float joinTorsoStabilizationWeight = 0.0f;

    [Tooltip("Join progress where torso stabilization begins to fade in.")]
    [SerializeField, Range(0f, 0.40f)] private float joinTorsoStabilizationStart = 0.08f;

    [Tooltip("Join progress where torso stabilization has fully faded out so the landing/Idle pose remains untouched.")]
    [SerializeField, Range(0.55f, 1f)] private float joinTorsoStabilizationEnd = 0.88f;


    [Header("Join Root Weight Transfer - Fix 11")]
    [Tooltip("Uses separate horizontal and vertical root timing during Join. Horizontal weight transfer begins earlier while vertical rise stays slightly delayed, which keeps the torso more natural without reducing knee flexion.")]
    [SerializeField] private bool useSeparatedJoinRootAxes = true;

    [Tooltip("How early the pelvis shifts forward during Join. Higher values move body weight toward the lead foot sooner without lifting the hips too quickly.")]
    [SerializeField, Range(0f, 0.35f)] private float joinForwardEarlyShare = 0.18f;

    [Tooltip("Normalized Join progress where the vertical rise starts contributing strongly. This is a soft delay, not a hard hold.")]
    [SerializeField, Range(0f, 0.40f)] private float joinVerticalRiseDelay = 0.14f;

    [Tooltip("Small vertical share allowed before the main rise. Prevents the pelvis from looking frozen while the knee folds.")]
    [SerializeField, Range(0f, 0.25f)] private float joinVerticalEarlyShare = 0.10f;

    [Tooltip("Minimum bend-direction hint offset, measured forward from the hip/ankle midpoint.")]
    [SerializeField, Range(0.02f, 0.50f)] private float joinKneeHintBaseForward = 0.24f;

    [Header("Join Pelvis Forward Clamp - Fix 11C")]
    [Tooltip("Limits only the EXTRA forward translation of the Humanoid Hips bone during Join. Root movement, vertical pelvis motion, pelvis rotation, knee flex and foot IK remain untouched.")]
    [SerializeField] private bool enableJoinPelvisForwardClamp = true;

    [Tooltip("Maximum extra forward drift allowed for the Hips bone relative to its take-off position and the moving root. 0.015 = 1.5 cm.")]
    [SerializeField, Range(0f, 0.08f)] private float joinPelvisMaxExtraForward = 0.015f;

    [Tooltip("Strength of the pelvis clamp. Keep below 1 for a natural amount of residual hip motion.")]
    [SerializeField, Range(0f, 1f)] private float joinPelvisForwardClampStrength = 0.90f;

    [Tooltip("Join progress where the pelvis clamp has smoothly reached full strength.")]
    [SerializeField, Range(0.05f, 0.40f)] private float joinPelvisClampBlendInEnd = 0.18f;

    [Tooltip("Join progress where the pelvis clamp starts releasing so the transition into the final planted/Idle pose stays smooth.")]
    [SerializeField, Range(0.55f, 0.95f)] private float joinPelvisClampReleaseStart = 0.80f;

    private bool joinPelvisReferenceCaptured;
    private float joinPelvisStartForwardRelativeToRoot;

    [Header("Join Upper Body Animator Layer - Alternative")]
    [Tooltip("During Join, blend only the upper body from the already-natural Lead animation. Legs remain controlled by the current Join + IK solution.")]
    [SerializeField] private bool useJoinUpperBodyAnimatorLayer = true;

    [Tooltip("Animator layer installed by Tools > Stair Game > Install Join Upper Body Layer.")]
    [SerializeField] private string joinUpperBodyLayerName = "Join Upper Body";

    [Tooltip("Maximum contribution of the Lead upper-body motion during the middle of Join.")]
    [SerializeField, Range(0f, 1f)] private float joinUpperBodyLayerWeight = 0.62f;

    [Tooltip("Join progress where the upper-body layer has fully blended in.")]
    [SerializeField, Range(0.05f, 0.40f)] private float joinUpperBodyBlendInEnd = 0.22f;

    [Tooltip("Join progress where the upper-body layer starts blending out toward the normal landing/Idle pose.")]
    [SerializeField, Range(0.45f, 0.95f)] private float joinUpperBodyBlendOutStart = 0.70f;

    [Tooltip("Skip the backward anticipation at the start of the Lead clip used only for the Join upper body.")]
    [SerializeField, Range(0f, 0.35f)] private float joinUpperBodyLeadSampleStart = 0.16f;

    private const string JoinUpperBodyLeftState = "JoinUpperBody_Left";
    private const string JoinUpperBodyRightState = "JoinUpperBody_Right";
    private int joinUpperBodyLayerIndex = -1;
    private bool joinUpperBodyLayerActive;

    private bool proceduralJoinSwingActive;
    private FootSide proceduralJoinSwingFoot;
    private Vector3 proceduralJoinSwingStart;
    private Vector3 proceduralJoinSwingTarget;
    private Vector3 proceduralJoinSwingPosition;
    private float proceduralJoinSwingProgress;
    private float proceduralJoinSwingWeight;
    private float proceduralJoinLegLength;

    private bool joinHipsPoseCaptured;
    private bool joinSpinePoseCaptured;
    private bool joinChestPoseCaptured;
    private bool joinUpperChestPoseCaptured;
    private Quaternion joinHipsStartLocalRotation;
    private Quaternion joinSpineStartLocalRotation;
    private Quaternion joinChestStartLocalRotation;
    private Quaternion joinUpperChestStartLocalRotation;

    // Final fix: keep the completed Join pose stable while Idle is sampled, then
    // release it gradually. This guard affects only forward pelvis drift and torso
    // rotations; it never moves a foot target by itself.
    private bool completedIdlePoseGuardActive;
    private float completedIdlePoseGuardWeight;
    private float completedIdleHipsForwardRelativeToRoot;
    private bool completedIdleHipsPoseCaptured;
    private bool completedIdleSpinePoseCaptured;
    private bool completedIdleChestPoseCaptured;
    private bool completedIdleUpperChestPoseCaptured;
    private Quaternion completedIdleHipsLocalRotation;
    private Quaternion completedIdleSpineLocalRotation;
    private Quaternion completedIdleChestLocalRotation;
    private Quaternion completedIdleUpperChestLocalRotation;

    private void BeginJoinUpperBodyLayer(FootSide movingFoot)
    {
        EndJoinUpperBodyLayer();

        if (!useJoinUpperBodyAnimatorLayer || animator == null)
        {
            return;
        }

        joinUpperBodyLayerIndex = animator.GetLayerIndex(joinUpperBodyLayerName);
        if (joinUpperBodyLayerIndex < 0)
        {
            return;
        }

        string stateName = movingFoot == FootSide.Right
            ? JoinUpperBodyRightState
            : JoinUpperBodyLeftState;

        float startTime = Mathf.Clamp01(joinUpperBodyLeadSampleStart);
        animator.Play(stateName, joinUpperBodyLayerIndex, startTime);
        animator.SetLayerWeight(joinUpperBodyLayerIndex, 0f);
        joinUpperBodyLayerActive = true;
    }

    private void UpdateJoinUpperBodyLayer(float joinProgress)
    {
        if (!joinUpperBodyLayerActive || animator == null || joinUpperBodyLayerIndex < 0)
        {
            return;
        }

        float p = Mathf.Clamp01(joinProgress);
        float blendInEnd = Mathf.Clamp(joinUpperBodyBlendInEnd, 0.05f, 0.40f);
        float blendOutStart = Mathf.Clamp(joinUpperBodyBlendOutStart, blendInEnd + 0.10f, 0.95f);

        float fadeIn = SmoothStep01(Mathf.InverseLerp(0f, blendInEnd, p));
        float fadeOut = 1f - SmoothStep01(Mathf.InverseLerp(blendOutStart, 1f, p));
        float weight = Mathf.Clamp01(joinUpperBodyLayerWeight * fadeIn * fadeOut);

        // Re-sample the good Lead upper-body motion in sync with Join progress.
        // The AvatarMask on this layer excludes both legs and all foot IK.
        float startTime = Mathf.Clamp01(joinUpperBodyLeadSampleStart);
        float sampleTime = Mathf.Lerp(startTime, 0.92f, p);
        string expectedState = proceduralJoinSwingFoot == FootSide.Right
            ? JoinUpperBodyRightState
            : JoinUpperBodyLeftState;

        animator.Play(expectedState, joinUpperBodyLayerIndex, sampleTime);
        animator.SetLayerWeight(joinUpperBodyLayerIndex, weight);
    }

    private void EndJoinUpperBodyLayer()
    {
        if (animator != null && joinUpperBodyLayerIndex >= 0)
        {
            animator.SetLayerWeight(joinUpperBodyLayerIndex, 0f);
        }

        joinUpperBodyLayerActive = false;
        joinUpperBodyLayerIndex = -1;
    }

    private void BeginProceduralJoinSwing(FootSide movingFoot)
    {
        EndProceduralJoinSwing();

        if (!enableProceduralJoinSwing || animator == null)
        {
            return;
        }

        Transform footBone = GetFootBone(movingFoot);
        if (footBone == null)
        {
            return;
        }

        proceduralJoinSwingActive = true;
        proceduralJoinSwingFoot = movingFoot;
        proceduralJoinSwingStart = footBone.position;
        proceduralJoinSwingTarget = GetPendingPlantedTarget(movingFoot);
        proceduralJoinSwingPosition = proceduralJoinSwingStart;
        proceduralJoinSwingProgress = 0f;
        proceduralJoinSwingWeight = 1f;

        HumanBodyBones upperLegBone = movingFoot == FootSide.Right
            ? HumanBodyBones.RightUpperLeg
            : HumanBodyBones.LeftUpperLeg;
        HumanBodyBones lowerLegBone = movingFoot == FootSide.Right
            ? HumanBodyBones.RightLowerLeg
            : HumanBodyBones.LeftLowerLeg;
        HumanBodyBones footHumanBone = movingFoot == FootSide.Right
            ? HumanBodyBones.RightFoot
            : HumanBodyBones.LeftFoot;

        Transform upperLeg = animator.GetBoneTransform(upperLegBone);
        Transform lowerLeg = animator.GetBoneTransform(lowerLegBone);
        Transform ankle = animator.GetBoneTransform(footHumanBone);

        proceduralJoinLegLength =
            upperLeg != null && lowerLeg != null && ankle != null
                ? Vector3.Distance(upperLeg.position, lowerLeg.position)
                  + Vector3.Distance(lowerLeg.position, ankle.position)
                : 0f;

        CaptureJoinTorsoPose();
        CaptureJoinPelvisForwardReference();
        BeginJoinUpperBodyLayer(movingFoot);
    }

    private void CaptureJoinPelvisForwardReference()
    {
        joinPelvisReferenceCaptured = false;

        if (!enableJoinPelvisForwardClamp || animator == null || movementRoot == null)
        {
            return;
        }

        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null)
        {
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(climbDirection, Vector3.up);
        if (forward.sqrMagnitude <= 0.000001f)
        {
            forward = Vector3.ProjectOnPlane(movementRoot.forward, Vector3.up);
        }

        if (forward.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        forward.Normalize();
        joinPelvisStartForwardRelativeToRoot = Vector3.Dot(
            hips.position - movementRoot.position,
            forward
        );
        joinPelvisReferenceCaptured = true;
    }

    private void ApplyJoinPelvisForwardClamp()
    {
        if (!enableJoinPelvisForwardClamp ||
            !proceduralJoinSwingActive ||
            !joinPelvisReferenceCaptured ||
            animator == null ||
            movementRoot == null)
        {
            return;
        }

        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null)
        {
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(climbDirection, Vector3.up);
        if (forward.sqrMagnitude <= 0.000001f)
        {
            forward = Vector3.ProjectOnPlane(movementRoot.forward, Vector3.up);
        }

        if (forward.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        forward.Normalize();

        // Keep the clamp active through the completed Join. Releasing it in the
        // final 20% let the Humanoid Hips translation return in one short window,
        // which is the visible late forward lunge in the recordings.
        float weight = Mathf.Clamp01(Mathf.Max(joinPelvisForwardClampStrength, 0.90f));

        if (weight <= 0.001f)
        {
            return;
        }

        float currentForwardRelativeToRoot = Vector3.Dot(
            hips.position - movementRoot.position,
            forward
        );
        float extraForward = currentForwardRelativeToRoot - joinPelvisStartForwardRelativeToRoot;
        float maxExtra = Mathf.Max(0f, joinPelvisMaxExtraForward);

        if (extraForward <= maxExtra)
        {
            return;
        }

        float correction = (extraForward - maxExtra) * weight;
        hips.position -= forward * correction;
    }

    private void UpdateProceduralJoinSwing(
        float normalizedTime,
        float swingStartTime,
        float swingEndTime
    )
    {
        if (!proceduralJoinSwingActive)
        {
            return;
        }

        float safeEnd = Mathf.Max(swingStartTime + 0.01f, swingEndTime);
        float p = Mathf.Clamp01(Mathf.InverseLerp(swingStartTime, safeEnd, normalizedTime));
        proceduralJoinSwingProgress = p;
        proceduralJoinSwingWeight = 1f;
        UpdateJoinUpperBodyLayer(p);

        float liftEnd = Mathf.Clamp(joinSwingLiftPhaseEnd, 0.15f, 0.55f);
        float travelEnd = Mathf.Clamp(joinSwingTravelPhaseEnd, liftEnd + 0.05f, 0.92f);

        // Use a three-stage trajectory rather than a straight interpolation:
        // 1) lift with very little forward travel -> knee folds,
        // 2) carry the folded leg forward above the riser,
        // 3) extend/lower onto the final planted target.
        float forwardShare;
        float y;
        float apexY = Mathf.Max(proceduralJoinSwingStart.y, proceduralJoinSwingTarget.y)
                      + Mathf.Max(0f, joinSwingLiftHeight);
        float prePlantY = proceduralJoinSwingTarget.y
                          + Mathf.Max(0f, joinSwingLiftHeight) * 0.32f;

        if (p <= liftEnd)
        {
            float t = SmoothStep01(Mathf.InverseLerp(0f, liftEnd, p));
            forwardShare = Mathf.Lerp(0f, 0.14f, t);
            y = Mathf.Lerp(proceduralJoinSwingStart.y, apexY, t);
        }
        else if (p <= travelEnd)
        {
            float t = SmoothStep01(Mathf.InverseLerp(liftEnd, travelEnd, p));
            forwardShare = Mathf.Lerp(0.14f, 0.88f, t);
            y = Mathf.Lerp(apexY, prePlantY, t);
        }
        else
        {
            float t = SmoothStep01(Mathf.InverseLerp(travelEnd, 1f, p));
            forwardShare = Mathf.Lerp(0.88f, 1f, t);
            y = Mathf.Lerp(prePlantY, proceduralJoinSwingTarget.y, t);
        }

        Vector3 position = Vector3.LerpUnclamped(
            proceduralJoinSwingStart,
            proceduralJoinSwingTarget,
            forwardShare
        );
        position.y = y;

        // A knee hint only controls bend direction. It cannot create flexion while
        // the leg remains geometrically close to full extension. Around mid-swing
        // we constrain hip-to-ankle reach so the two-bone IK solution must flex.
        // Prefer raising Y only so the established forward path/contact fixes remain.
        if (proceduralJoinLegLength > 0.001f && animator != null)
        {
            HumanBodyBones upperLegBone = proceduralJoinSwingFoot == FootSide.Right
                ? HumanBodyBones.RightUpperLeg
                : HumanBodyBones.LeftUpperLeg;
            Transform upperLeg = animator.GetBoneTransform(upperLegBone);

            if (upperLeg != null)
            {
                float bell = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI);
                float reachRatio = Mathf.Lerp(
                    0.985f,
                    Mathf.Clamp(joinFlexMaxReachRatio, 0.65f, 0.98f),
                    bell
                );
                float maxReach = proceduralJoinLegLength * reachRatio;

                Vector3 hip = upperLeg.position;
                Vector3 planarDelta = Vector3.ProjectOnPlane(position - hip, Vector3.up);
                float planarDistance = planarDelta.magnitude;

                if (planarDistance < maxReach)
                {
                    float maxVerticalSeparation = Mathf.Sqrt(
                        Mathf.Max(0f, maxReach * maxReach - planarDistance * planarDistance)
                    );
                    float minimumFootY = hip.y - maxVerticalSeparation;
                    if (position.y < minimumFootY)
                    {
                        position.y = minimumFootY;
                    }
                }
                else if (planarDistance > 0.0001f)
                {
                    Vector3 clampedPlanar = planarDelta.normalized * (maxReach * 0.98f);
                    position = hip + clampedPlanar + Vector3.up * (position.y - hip.y);
                }
            }
        }

        proceduralJoinSwingPosition = position;
    }

    private float GetProceduralJoinRootProgress(
        float normalizedTime,
        float swingStartTime,
        float swingEndTime
    )
    {
        float safeEnd = Mathf.Max(swingStartTime + 0.01f, swingEndTime);
        float p = Mathf.Clamp01(Mathf.InverseLerp(swingStartTime, safeEnd, normalizedTime));
        float hold = Mathf.Clamp(joinRootRiseHoldProgress, 0f, 0.50f);

        if (!enableNaturalJoinBodyCoordination || hold <= 0.001f)
        {
            return SmoothStep01(p);
        }

        // Fix 10: a complete pelvis freeze made the animation continue in the torso
        // while the world-space root stayed still, producing the unnatural upper-body
        // fold seen in Rec 0014. Let a small amount of body travel happen during the
        // knee-fold phase, then complete the remaining displacement smoothly.
        float earlyShare = Mathf.Clamp(joinEarlyRootProgress, 0f, 0.30f);
        if (p <= hold)
        {
            float early = SmoothStep01(Mathf.InverseLerp(0f, hold, p));
            return earlyShare * early;
        }

        float late = SmoothStep01(Mathf.InverseLerp(hold, 1f, p));
        return Mathf.Lerp(earlyShare, 1f, late);
    }

    private float GetProceduralJoinForwardProgress(
        float normalizedTime,
        float swingStartTime,
        float swingEndTime
    )
    {
        float safeEnd = Mathf.Max(swingStartTime + 0.01f, swingEndTime);
        float p = Mathf.Clamp01(Mathf.InverseLerp(swingStartTime, safeEnd, normalizedTime));
        float earlyShare = Mathf.Clamp(joinForwardEarlyShare, 0f, 0.35f);

        // Weight shifts toward the lead foot early, but without an abrupt snap.
        float early = SmoothStep01(Mathf.InverseLerp(0f, 0.35f, p));
        float late = SmoothStep01(Mathf.InverseLerp(0.20f, 1f, p));
        return Mathf.Clamp01(Mathf.Max(earlyShare * early, late));
    }

    private float GetProceduralJoinVerticalProgress(
        float normalizedTime,
        float swingStartTime,
        float swingEndTime
    )
    {
        float safeEnd = Mathf.Max(swingStartTime + 0.01f, swingEndTime);
        float p = Mathf.Clamp01(Mathf.InverseLerp(swingStartTime, safeEnd, normalizedTime));
        float delay = Mathf.Clamp(joinVerticalRiseDelay, 0f, 0.40f);
        float earlyShare = Mathf.Clamp(joinVerticalEarlyShare, 0f, 0.25f);

        float early = SmoothStep01(Mathf.InverseLerp(0f, Mathf.Max(0.01f, delay + 0.10f), p));
        float main = SmoothStep01(Mathf.InverseLerp(delay, 1f, p));
        return Mathf.Clamp01(Mathf.Lerp(earlyShare * early, 1f, main));
    }

    private void CaptureJoinTorsoPose()
    {
        joinHipsPoseCaptured = false;
        joinSpinePoseCaptured = false;
        joinChestPoseCaptured = false;
        joinUpperChestPoseCaptured = false;

        if (animator == null)
        {
            return;
        }

        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        Transform upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);

        if (hips != null)
        {
            joinHipsPoseCaptured = true;
            joinHipsStartLocalRotation = hips.localRotation;
        }

        if (spine != null)
        {
            joinSpinePoseCaptured = true;
            joinSpineStartLocalRotation = spine.localRotation;
        }

        if (chest != null)
        {
            joinChestPoseCaptured = true;
            joinChestStartLocalRotation = chest.localRotation;
        }

        if (upperChest != null)
        {
            joinUpperChestPoseCaptured = true;
            joinUpperChestStartLocalRotation = upperChest.localRotation;
        }
    }

    private void ApplyProceduralJoinTorsoStabilizer()
    {
        if (!proceduralJoinSwingActive || animator == null)
        {
            return;
        }

        // This method existed before but was never called from OnAnimatorIK, and the
        // scene also serialized its old weight as 0. The result was that the Join
        // clip could lean the whole torso backward even though stabilization code
        // was present. Use fixed, conservative weights so the fix does not depend
        // on stale Inspector data.
        StabilizeJoinBone(
            HumanBodyBones.Hips,
            joinHipsPoseCaptured,
            joinHipsStartLocalRotation,
            0.72f
        );
        StabilizeJoinBone(
            HumanBodyBones.Spine,
            joinSpinePoseCaptured,
            joinSpineStartLocalRotation,
            0.96f
        );
        StabilizeJoinBone(
            HumanBodyBones.Chest,
            joinChestPoseCaptured,
            joinChestStartLocalRotation,
            0.92f
        );
        StabilizeJoinBone(
            HumanBodyBones.UpperChest,
            joinUpperChestPoseCaptured,
            joinUpperChestStartLocalRotation,
            0.82f
        );
    }

    private void StabilizeJoinBone(
        HumanBodyBones bone,
        bool captured,
        Quaternion referenceLocalRotation,
        float weight
    )
    {
        if (!captured || weight <= 0f || animator == null)
        {
            return;
        }

        Transform boneTransform = animator.GetBoneTransform(bone);
        if (boneTransform == null)
        {
            return;
        }

        Quaternion corrected = Quaternion.Slerp(
            boneTransform.localRotation,
            referenceLocalRotation,
            Mathf.Clamp01(weight)
        );
        animator.SetBoneLocalRotation(bone, corrected);
    }

    private void ApplyProceduralJoinKneeHint()
    {
        // Fix 11C: suppress the clip's extra Hips lunge before solving the knee and
        // foot IK. This changes only the Hips translation along the climb direction;
        // Y motion and rotation remain owned by the existing animation/root solution.
        ApplyJoinPelvisForwardClamp();
        ApplyProceduralJoinTorsoStabilizer();
        ApplyCompletedIdlePoseGuard();

        AvatarIKHint rightHint = AvatarIKHint.RightKnee;
        AvatarIKHint leftHint = AvatarIKHint.LeftKnee;

        if (!proceduralJoinSwingActive || animator == null)
        {
            animator?.SetIKHintPositionWeight(rightHint, 0f);
            animator?.SetIKHintPositionWeight(leftHint, 0f);
            return;
        }

        AvatarIKHint hint = proceduralJoinSwingFoot == FootSide.Right
            ? rightHint
            : leftHint;
        AvatarIKHint otherHint = proceduralJoinSwingFoot == FootSide.Right
            ? leftHint
            : rightHint;

        HumanBodyBones upperLegBone = proceduralJoinSwingFoot == FootSide.Right
            ? HumanBodyBones.RightUpperLeg
            : HumanBodyBones.LeftUpperLeg;
        Transform upperLeg = animator.GetBoneTransform(upperLegBone);

        animator.SetIKHintPositionWeight(otherHint, 0f);

        if (upperLeg == null)
        {
            animator.SetIKHintPositionWeight(hint, 0f);
            return;
        }

        // Build the hint from the hip/ankle midpoint, not from the already-straight
        // animated knee. This creates an unambiguous forward bend plane.
        float bell = Mathf.Sin(Mathf.Clamp01(proceduralJoinSwingProgress) * Mathf.PI);
        float weight = Mathf.Clamp01(joinKneeHintWeight * bell);
        Vector3 mid = Vector3.Lerp(
            upperLeg.position,
            proceduralJoinSwingPosition,
            0.50f
        );
        float forwardDistance = Mathf.Max(
            joinKneeHintBaseForward,
            joinKneeHintForwardDistance
        );
        Vector3 hintPosition = mid + climbDirection * forwardDistance;

        animator.SetIKHintPositionWeight(hint, weight);
        animator.SetIKHintPosition(hint, hintPosition);
    }

    private void EndProceduralJoinSwing()
    {
        EndJoinUpperBodyLayer();
        proceduralJoinSwingActive = false;
        proceduralJoinSwingProgress = 0f;
        proceduralJoinSwingWeight = 0f;
        joinHipsPoseCaptured = false;
        joinSpinePoseCaptured = false;
        joinChestPoseCaptured = false;
        joinUpperChestPoseCaptured = false;
        joinPelvisReferenceCaptured = false;
    }

    private void BeginCompletedIdlePoseGuard()
    {
        EndCompletedIdlePoseGuard();

        if (animator == null || movementRoot == null)
        {
            return;
        }

        Vector3 forward = GetPlanarClimbForward();
        if (forward.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        Transform upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);

        if (hips == null)
        {
            return;
        }

        completedIdleHipsForwardRelativeToRoot = Vector3.Dot(
            hips.position - movementRoot.position,
            forward
        );
        completedIdleHipsPoseCaptured = true;
        completedIdleHipsLocalRotation = hips.localRotation;

        if (spine != null)
        {
            completedIdleSpinePoseCaptured = true;
            completedIdleSpineLocalRotation = spine.localRotation;
        }

        if (chest != null)
        {
            completedIdleChestPoseCaptured = true;
            completedIdleChestLocalRotation = chest.localRotation;
        }

        if (upperChest != null)
        {
            completedIdleUpperChestPoseCaptured = true;
            completedIdleUpperChestLocalRotation = upperChest.localRotation;
        }

        completedIdlePoseGuardWeight = 1f;
        completedIdlePoseGuardActive = true;
    }

    private void ApplyCompletedIdlePoseGuard()
    {
        if (!completedIdlePoseGuardActive ||
            completedIdlePoseGuardWeight <= 0.001f ||
            animator == null ||
            movementRoot == null)
        {
            return;
        }

        Vector3 forward = GetPlanarClimbForward();
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);

        if (hips != null && forward.sqrMagnitude > 0.000001f)
        {
            float current = Vector3.Dot(hips.position - movementRoot.position, forward);
            float error = current - completedIdleHipsForwardRelativeToRoot;
            hips.position -= forward * (error * completedIdlePoseGuardWeight);
        }

        StabilizeJoinBone(
            HumanBodyBones.Hips,
            completedIdleHipsPoseCaptured,
            completedIdleHipsLocalRotation,
            completedIdlePoseGuardWeight * 0.70f
        );
        StabilizeJoinBone(
            HumanBodyBones.Spine,
            completedIdleSpinePoseCaptured,
            completedIdleSpineLocalRotation,
            completedIdlePoseGuardWeight
        );
        StabilizeJoinBone(
            HumanBodyBones.Chest,
            completedIdleChestPoseCaptured,
            completedIdleChestLocalRotation,
            completedIdlePoseGuardWeight * 0.95f
        );
        StabilizeJoinBone(
            HumanBodyBones.UpperChest,
            completedIdleUpperChestPoseCaptured,
            completedIdleUpperChestLocalRotation,
            completedIdlePoseGuardWeight * 0.85f
        );
    }

    private void EndCompletedIdlePoseGuard()
    {
        completedIdlePoseGuardActive = false;
        completedIdlePoseGuardWeight = 0f;
        completedIdleHipsPoseCaptured = false;
        completedIdleSpinePoseCaptured = false;
        completedIdleChestPoseCaptured = false;
        completedIdleUpperChestPoseCaptured = false;
    }

    private Vector3 GetPlanarClimbForward()
    {
        Vector3 forward = Vector3.ProjectOnPlane(climbDirection, Vector3.up);
        if (forward.sqrMagnitude <= 0.000001f && movementRoot != null)
        {
            forward = Vector3.ProjectOnPlane(movementRoot.forward, Vector3.up);
        }

        return forward.sqrMagnitude > 0.000001f
            ? forward.normalized
            : Vector3.zero;
    }

    private void AlignRootToCompletedFeet(
        Vector3 desiredRightFoot,
        Vector3 desiredLeftFoot,
        float strength
    )
    {
        if (movementRoot == null || rightFootBone == null || leftFootBone == null)
        {
            return;
        }

        Vector3 desiredCenter = (desiredRightFoot + desiredLeftFoot) * 0.5f;
        Vector3 actualCenter = (rightFootBone.position + leftFootBone.position) * 0.5f;
        Vector3 error = desiredCenter - actualCenter;

        Vector3 forward = GetPlanarClimbForward();
        float forwardCorrection = forward.sqrMagnitude > 0.000001f
            ? Mathf.Clamp(Vector3.Dot(error, forward), -0.14f, 0.14f)
            : 0f;
        float heightCorrection = Mathf.Clamp(
            error.y,
            -completedIdleMaxRootHeightCorrection,
            completedIdleMaxRootHeightCorrection
        );

        float t = Mathf.Clamp01(strength);
        Vector3 correction =
            forward * (forwardCorrection * t) +
            Vector3.up * (heightCorrection * t);

        if (correction.sqrMagnitude > 0.00000001f)
        {
            stableRootPosition += correction;
            movementRoot.position = stableRootPosition;
        }
    }

    private void BeginLeadTakeoffGuard(FootSide movingFoot)
    {
        EndLeadTakeoffGuard();

        if (!enableLeadTakeoffGuard || animator == null || movementRoot == null)
        {
            return;
        }

        Transform footBone = GetFootBone(movingFoot);
        if (footBone == null)
        {
            return;
        }

        leadTakeoffGuardActive = true;
        leadTakeoffGuardFoot = movingFoot;
        leadTakeoffGuardWeight = 1f;
        leadTakeoffFootStartPosition = footBone.position;
    }

    private void UpdateLeadTakeoffGuard(float normalizedTime)
    {
        if (!leadTakeoffGuardActive)
        {
            return;
        }

        float releaseStart = Mathf.Min(
            leadTakeoffGuardReleaseStart,
            leadTakeoffGuardReleaseEnd - 0.01f
        );
        float releaseEnd = Mathf.Max(
            releaseStart + 0.01f,
            leadTakeoffGuardReleaseEnd
        );

        if (normalizedTime <= releaseStart)
        {
            leadTakeoffGuardWeight = 1f;
            return;
        }

        float releaseProgress = Mathf.InverseLerp(
            releaseStart,
            releaseEnd,
            normalizedTime
        );
        leadTakeoffGuardWeight = 1f - SmoothStep01(releaseProgress);

        if (normalizedTime >= releaseEnd)
        {
            EndLeadTakeoffGuard();
        }
    }

    private void EndLeadTakeoffGuard()
    {
        leadTakeoffGuardActive = false;
        leadTakeoffGuardWeight = 0f;
    }

    private float GetJoinCompletionTime()
    {
        return holdCompletedJoinPose
            ? Mathf.Min(completedJoinPoseTime, animationCompletionTime)
            : animationCompletionTime;
    }

    private void FinishCompletedStepPose()
    {
        if (returnToIdleAfterCompletedStep)
        {
            if (completedIdleRecoveryCoroutine != null)
            {
                StopCoroutine(completedIdleRecoveryCoroutine);
                completedIdleRecoveryCoroutine = null;
            }

            completedIdleRecoveryCoroutine = StartCoroutine(ReturnToIdleAfterCompletedStep());
            return;
        }

        if (holdCompletedJoinPose)
        {
            animator.applyRootMotion = false;
            animator.speed = 0f;
            BeginCompletedPoseIkRelease();
            return;
        }

        ReturnToIdle(transitionDuration);
    }

    private IEnumerator ReturnToIdleAfterCompletedStep()
    {
        // Fix 05: the previous versions crossfaded to Idle while both feet stayed
        // hard-locked at their Join targets. Humanoid then had only one way to satisfy
        // both constraints: lower the pelvis and bend both knees. That is the crouched
        // "Idle" seen in Rec 0009.
        isAnimating = true;
        EndLeadTakeoffGuard();
        CancelCompletedPoseIkRelease();

        Vector3 desiredRightFoot = rightFootLock.Active && rightFootLock.PositionWeight > 0f
            ? rightFootLock.Position
            : (rightFootBone != null ? rightFootBone.position : Vector3.zero);
        Vector3 desiredLeftFoot = leftFootLock.Active && leftFootLock.PositionWeight > 0f
            ? leftFootLock.Position
            : (leftFootBone != null ? leftFootBone.position : Vector3.zero);

        float rightStartPositionWeight = rightFootLock.PositionWeight;
        float rightStartRotationWeight = rightFootLock.RotationWeight;
        float leftStartPositionWeight = leftFootLock.PositionWeight;
        float leftStartRotationWeight = leftFootLock.RotationWeight;

        BeginCompletedIdlePoseGuard();

        animator.speed = 1f;
        animator.applyRootMotion = false;
        animator.CrossFadeInFixedTime(
            idleStateName,
            Mathf.Max(0f, completedStepIdleBlendDuration),
            0,
            0f
        );

        float releaseDuration = releaseFootLocksDuringCompletedIdle
            ? Mathf.Max(0.02f, completedIdleFootReleaseDuration)
            : 0f;
        float baseBlendTime = Mathf.Max(completedStepIdleBlendDuration, releaseDuration);
        float poseGuardReleaseDuration = 0.14f;
        float totalBlendTime = baseBlendTime + poseGuardReleaseDuration;
        float elapsed = 0f;

        while (elapsed < totalBlendTime)
        {
            movementRoot.position = stableRootPosition;
            if (preserveInitialRotation)
            {
                movementRoot.rotation = stableRootRotation;
            }

            if (completedIdlePoseGuardActive)
            {
                // Hold the completed upright Join pose until the Idle crossfade and
                // foot release are complete, then release the pose gradually.
                float guardRelease = SmoothStep01(
                    Mathf.InverseLerp(baseBlendTime, totalBlendTime, elapsed)
                );
                completedIdlePoseGuardWeight = 1f - guardRelease;
            }

            if (releaseFootLocksDuringCompletedIdle && releaseDuration > 0f)
            {
                float releaseProgress = SmoothStep01(
                    Mathf.Clamp01(elapsed / releaseDuration)
                );
                float remaining = 1f - releaseProgress;

                if (rightFootLock.Active)
                {
                    rightFootLock.PositionWeight = rightStartPositionWeight * remaining;
                    rightFootLock.RotationWeight = rightStartRotationWeight * remaining;
                }

                if (leftFootLock.Active)
                {
                    leftFootLock.PositionWeight = leftStartPositionWeight * remaining;
                    leftFootLock.RotationWeight = leftStartRotationWeight * remaining;
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (releaseFootLocksDuringCompletedIdle)
        {
            ClearAllFootLocks();
        }

        EndCompletedIdlePoseGuard();

        // Wait briefly for the actual Idle pose to be sampled with free legs.
        int idleHash = Animator.StringToHash(idleStateName);
        float idleWait = 0f;
        while (idleWait < 0.5f)
        {
            yield return null;
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash == idleHash && !animator.IsInTransition(0))
            {
                break;
            }
            idleWait += Time.deltaTime;
        }

        yield return null;

        if (alignRootHeightAfterCompletedIdle &&
            rightFootBone != null &&
            leftFootBone != null)
        {
            // The old recovery corrected Y only. If the Idle clip placed both feet
            // several centimetres forward relative to Join, the body had to pop
            // forward or the legs stayed visibly stretched. Align the whole root in
            // the climb direction AND vertically after both foot locks are free.
            AlignRootToCompletedFeet(desiredRightFoot, desiredLeftFoot, 1f);
            yield return null;
        }

        // Collider-backed final standing correction. A BoxCollider does not push an
        // Animator-driven foot bone out of a stair, so sample the surface and lift the
        // ROOT only by the small amount still required to keep both soles above it.
        if (enableStandingFootSurfaceCorrection)
        {
            float surfaceLift = CalculateStandingSurfaceLift();
            if (surfaceLift > 0.0001f)
            {
                stableRootPosition += Vector3.up * surfaceLift;
                movementRoot.position = stableRootPosition;
                yield return null;
            }
        }

        // Leave Idle unconstrained. The next movement phase will lock the support foot
        // at the CURRENT upright Idle pose, which avoids carrying the old Join crouch
        // into the next Lead step.
        ClearAllFootLocks();
        animator.speed = 1f;
        animator.applyRootMotion = false;
        isAnimating = false;
        completedIdleRecoveryCoroutine = null;
    }

    private void ReturnToIdle(float blendDuration)
    {
        animator.speed = 1f;
        animator.applyRootMotion = false;
        ClearAllFootLocks();
        animator.CrossFadeInFixedTime(
            idleStateName,
            Mathf.Max(0f, blendDuration),
            0,
            0f
        );
    }
    }

