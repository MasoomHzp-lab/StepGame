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

    private bool joinSpinePoseCaptured;
    private bool joinChestPoseCaptured;
    private bool joinUpperChestPoseCaptured;
    private Quaternion joinSpineStartLocalRotation;
    private Quaternion joinChestStartLocalRotation;
    private Quaternion joinUpperChestStartLocalRotation;

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

        float p = Mathf.Clamp01(proceduralJoinSwingProgress);
        float blendInEnd = Mathf.Clamp(joinPelvisClampBlendInEnd, 0.05f, 0.40f);
        float releaseStart = Mathf.Clamp(joinPelvisClampReleaseStart, blendInEnd + 0.15f, 0.95f);

        float fadeIn = SmoothStep01(Mathf.InverseLerp(0f, blendInEnd, p));
        float fadeOut = 1f - SmoothStep01(Mathf.InverseLerp(releaseStart, 1f, p));
        float weight = Mathf.Clamp01(joinPelvisForwardClampStrength * fadeIn * fadeOut);

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
        joinSpinePoseCaptured = false;
        joinChestPoseCaptured = false;
        joinUpperChestPoseCaptured = false;

        if (animator == null)
        {
            return;
        }

        Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        Transform upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);

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
        if (!enableNaturalJoinBodyCoordination ||
            !proceduralJoinSwingActive ||
            animator == null ||
            joinTorsoStabilizationWeight <= 0f)
        {
            return;
        }

        float p = Mathf.Clamp01(proceduralJoinSwingProgress);
        float start = Mathf.Clamp(joinTorsoStabilizationStart, 0f, 0.40f);
        float end = Mathf.Clamp(joinTorsoStabilizationEnd, 0.55f, 1f);

        float fadeIn = SmoothStep01(Mathf.InverseLerp(start, Mathf.Min(start + 0.18f, end), p));
        float fadeOut = 1f - SmoothStep01(Mathf.InverseLerp(Mathf.Max(start, end - 0.22f), end, p));
        float weight = Mathf.Clamp01(joinTorsoStabilizationWeight * fadeIn * fadeOut);

        if (weight <= 0.001f)
        {
            return;
        }

        StabilizeJoinBone(HumanBodyBones.Spine, joinSpinePoseCaptured, joinSpineStartLocalRotation, weight);
        StabilizeJoinBone(HumanBodyBones.Chest, joinChestPoseCaptured, joinChestStartLocalRotation, weight * 0.90f);
        StabilizeJoinBone(HumanBodyBones.UpperChest, joinUpperChestPoseCaptured, joinUpperChestStartLocalRotation, weight * 0.75f);
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
        joinSpinePoseCaptured = false;
        joinChestPoseCaptured = false;
        joinUpperChestPoseCaptured = false;
        joinPelvisReferenceCaptured = false;
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
        float totalBlendTime = Mathf.Max(completedStepIdleBlendDuration, releaseDuration);
        float elapsed = 0f;

        while (elapsed < totalBlendTime)
        {
            movementRoot.position = stableRootPosition;
            if (preserveInitialRotation)
            {
                movementRoot.rotation = stableRootRotation;
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
            Vector3 desiredFeetCenter = (desiredRightFoot + desiredLeftFoot) * 0.5f;
            Vector3 actualFeetCenter = (rightFootBone.position + leftFootBone.position) * 0.5f;

            float heightCorrection = Mathf.Clamp(
                desiredFeetCenter.y - actualFeetCenter.y,
                -completedIdleMaxRootHeightCorrection,
                completedIdleMaxRootHeightCorrection
            );

            if (Mathf.Abs(heightCorrection) > 0.0001f)
            {
                stableRootPosition += Vector3.up * heightCorrection;
                movementRoot.position = stableRootPosition;
                yield return null;
            }
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