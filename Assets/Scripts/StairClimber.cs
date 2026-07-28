using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class StairClimber : MonoBehaviour
{
    private enum FootSide
    {
        Right,
        Left
    }

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform characterRoot;
    [SerializeField] private Transform stairsRoot;

    [Header("Animator State Names")]
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string rightStepStateName = "RightStep";
    [SerializeField] private string leftStepStateName = "LeftStep";

    [Header("Stair Direction")]
    [Tooltip("جهت بالا رفتن پله‌ها نسبت به آبجکت stairs")]
    [SerializeField] private Vector3 climbLocalDirection = Vector3.right;

    [Header("Foot Placement")]
    [Tooltip("فاصله استخوان Foot تا کف کفش")]
    [SerializeField] private float footBoneToSoleHeight = 0.18f;

    [Tooltip("جابه‌جایی محل قرارگرفتن پا روی عمق پله")]
    [SerializeField] private float landingDepthOffset = 0f;

    [Header("Animation Matching")]
    [Range(0f, 0.4f)]
    [SerializeField] private float leadingFootMatchStart = 0.05f;

    [Range(0.25f, 0.7f)]
    [SerializeField] private float leadingFootMatchEnd = 0.48f;

    [Range(0.4f, 0.8f)]
    [SerializeField] private float trailingFootMatchStart = 0.52f;

    [Range(0.7f, 1f)]
    [SerializeField] private float trailingFootMatchEnd = 0.94f;

    [SerializeField] private float transitionDuration = 0.08f;

    [Header("Final Correction")]
    [SerializeField] private float maximumFinalCorrection = 0.35f;

    [Header("Input Queue")]
    [Min(1)]
    [SerializeField] private int maximumQueuedSteps = 1;

    private readonly List<BoxCollider> steps = new List<BoxCollider>();
    private readonly Queue<FootSide> commands = new Queue<FootSide>();

    private Vector3 climbWorldDirection;
    private Vector3 sideWorldDirection;

    private int nextStepIndex;
    private bool isMoving;
    private bool isReady;

    // پله‌ای که کاراکتر بعد از پایان انیمیشن روی آن ایستاده است.
    private BoxCollider standingStep;

    // هنگام Idle کف پا روی سطح پله قفل می‌ماند.
    private bool lockStandingHeight;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (characterRoot == null)
        {
            characterRoot = transform;
        }

        BuildStepList();
    }

    private IEnumerator Start()
    {
        if (!ValidateReferences())
        {
            yield break;
        }

        // در حالت Idle نباید Root Motion موقعیت کاراکتر را تغییر بدهد.
        animator.applyRootMotion = false;
        animator.speed = 1f;
        animator.Play(idleStateName, 0, 0f);
        animator.Update(0f);

        // اولین پله‌ای که جلوی کاراکتر است انتخاب می‌شود.
        nextStepIndex = FindFirstStepInFront();

        isReady = true;
        yield return null;
    }

    private void Update()
    {
        if (!isReady)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            EnqueueStep(FootSide.Right);
        }

        if (keyboard != null && keyboard.lKey.wasPressedThisFrame)
        {
            EnqueueStep(FootSide.Left);
        }

        // کلیک چپ = پای راست
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            EnqueueStep(FootSide.Right);
        }

        // کلیک راست = پای چپ
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            EnqueueStep(FootSide.Left);
        }

        if (!isMoving && commands.Count > 0)
        {
            FootSide command = commands.Dequeue();
            StartCoroutine(PlayStep(command));
        }
    }

    private void LateUpdate()
    {
        if (!isReady || !lockStandingHeight || standingStep == null)
        {
            return;
        }

        if (animator == null || characterRoot == null)
        {
            return;
        }

        // Animator ابتدا Pose را محاسبه می‌کند؛ سپس ارتفاع کف پا اصلاح می‌شود.
        LockFeetHeightToStep(standingStep);
    }

    public void CommandRightFoot()
    {
        EnqueueStep(FootSide.Right);
    }

    public void CommandLeftFoot()
    {
        EnqueueStep(FootSide.Left);
    }

    private void EnqueueStep(FootSide foot)
    {
        if (!isReady)
        {
            return;
        }

        if (nextStepIndex + commands.Count >= steps.Count)
        {
            Debug.Log("تمام پله‌ها طی شده‌اند.", this);
            return;
        }

        if (commands.Count >= maximumQueuedSteps)
        {
            Debug.Log("صف حرکت‌ها پر است.", this);
            return;
        }

        // فعلاً برای تست، راست و چپ هر دو به‌صورت مستقل قابل اجرا هستند.
        commands.Enqueue(foot);
    }

    private IEnumerator PlayStep(FootSide leadingSide)
    {
        isMoving = true;

        // قفل پله قبلی آزاد می‌شود تا انیمیشن بتواند حرکت کند.
        lockStandingHeight = false;
        standingStep = null;

        if (nextStepIndex >= steps.Count)
        {
            isMoving = false;
            yield break;
        }

        BoxCollider targetStep = steps[nextStepIndex];

        string stateName =
            leadingSide == FootSide.Right
                ? rightStepStateName
                : leftStepStateName;

        HumanBodyBones leadingBoneName =
            leadingSide == FootSide.Right
                ? HumanBodyBones.RightFoot
                : HumanBodyBones.LeftFoot;

        HumanBodyBones trailingBoneName =
            leadingSide == FootSide.Right
                ? HumanBodyBones.LeftFoot
                : HumanBodyBones.RightFoot;

        AvatarTarget leadingAvatarTarget =
            leadingSide == FootSide.Right
                ? AvatarTarget.RightFoot
                : AvatarTarget.LeftFoot;

        AvatarTarget trailingAvatarTarget =
            leadingSide == FootSide.Right
                ? AvatarTarget.LeftFoot
                : AvatarTarget.RightFoot;

        Transform leadingFoot = animator.GetBoneTransform(leadingBoneName);
        Transform trailingFoot = animator.GetBoneTransform(trailingBoneName);

        if (leadingFoot == null || trailingFoot == null)
        {
            Debug.LogError(
                "استخوان پای راست یا چپ پیدا نشد. Rig باید Humanoid باشد.",
                this
            );

            animator.applyRootMotion = false;
            isMoving = false;
            yield break;
        }

        Vector3 leadingTargetPosition = GetFootTarget(targetStep, leadingFoot);
        Vector3 trailingTargetPosition = GetFootTarget(targetStep, trailingFoot);

        animator.speed = 1f;

        // Root Motion فقط هنگام اجرای قدم روشن است.
        animator.applyRootMotion = true;

        animator.CrossFadeInFixedTime(
            stateName,
            transitionDuration,
            0,
            0f
        );

        float enterTimeout = 0f;

        while (
            animator.IsInTransition(0) ||
            !animator.GetCurrentAnimatorStateInfo(0).IsName(stateName)
        )
        {
            enterTimeout += Time.deltaTime;

            if (enterTimeout > 2f)
            {
                Debug.LogError(
                    "Animator وارد State با نام " + stateName + " نشد.",
                    this
                );

                animator.applyRootMotion = false;
                isMoving = false;
                yield break;
            }

            yield return null;
        }

        bool leadingMatchStarted = false;
        bool trailingMatchStarted = false;

        // در پروژه فعلی جهت بالا رفتن محور X است؛ محور عرضی Z قفل نمی‌شود.
        MatchTargetWeightMask matchWeight =
            new MatchTargetWeightMask(
                new Vector3(1f, 1f, 0f),
                0f
            );

        while (true)
        {
            AnimatorStateInfo stateInfo =
                animator.GetCurrentAnimatorStateInfo(0);

            if (!stateInfo.IsName(stateName))
            {
                break;
            }

            float normalizedTime = stateInfo.normalizedTime;

            if (!leadingMatchStarted &&
                !animator.isMatchingTarget &&
                normalizedTime < leadingFootMatchEnd)
            {
                float safeStart = Mathf.Max(
                    leadingFootMatchStart,
                    normalizedTime + 0.001f
                );

                if (safeStart < leadingFootMatchEnd)
                {
                    animator.MatchTarget(
                        leadingTargetPosition,
                        leadingFoot.rotation,
                        leadingAvatarTarget,
                        matchWeight,
                        safeStart,
                        leadingFootMatchEnd
                    );

                    leadingMatchStarted = true;
                }
            }

            if (leadingMatchStarted &&
                !trailingMatchStarted &&
                !animator.isMatchingTarget &&
                normalizedTime >= trailingFootMatchStart &&
                normalizedTime < trailingFootMatchEnd)
            {
                float safeStart = Mathf.Max(
                    trailingFootMatchStart,
                    normalizedTime + 0.001f
                );

                if (safeStart < trailingFootMatchEnd)
                {
                    animator.MatchTarget(
                        trailingTargetPosition,
                        trailingFoot.rotation,
                        trailingAvatarTarget,
                        matchWeight,
                        safeStart,
                        trailingFootMatchEnd
                    );

                    trailingMatchStarted = true;
                }
            }

            if (normalizedTime >= 0.99f)
            {
                break;
            }

            yield return null;
        }

        if (animator.isMatchingTarget)
        {
            animator.InterruptMatchTarget(true);
        }

        // قبل از Idle یک اصلاح محدود انجام می‌شود تا پرش بزرگ ایجاد نشود.
        CorrectFinalFootPosition(targetStep, false);

        nextStepIndex++;

        // از اینجا به بعد کاراکتر باید روی همین پله بایستد.
        standingStep = targetStep;

        // Idle نباید Root کاراکتر را پایین بکشد.
        animator.applyRootMotion = false;
        lockStandingHeight = true;

        animator.CrossFadeInFixedTime(
            idleStateName,
            transitionDuration,
            0,
            0f
        );

        float idleTimeout = 0f;

        while (
            animator.IsInTransition(0) ||
            !animator.GetCurrentAnimatorStateInfo(0).IsName(idleStateName)
        )
        {
            idleTimeout += Time.deltaTime;

            if (idleTimeout > 2f)
            {
                Debug.LogWarning(
                    "Animator نتوانست به‌موقع وارد Idle شود.",
                    this
                );

                break;
            }

            yield return null;
        }

        // بعد از کامل‌شدن Transition نیز یک اصلاح محدود انجام می‌شود.
        CorrectFinalFootPosition(targetStep, false);

        Debug.Log(
            "قدم شماره " +
            nextStepIndex +
            " با پای " +
            (leadingSide == FootSide.Right ? "راست" : "چپ") +
            " کامل شد.",
            this
        );

        isMoving = false;
    }

    private Vector3 GetFootTarget(BoxCollider step, Transform foot)
    {
        Bounds bounds = step.bounds;
        Vector3 target = bounds.center;

        target.y = bounds.max.y + footBoneToSoleHeight;
        target += climbWorldDirection * landingDepthOffset;

        // موقعیت عرضی فعلی هر پا حفظ می‌شود.
        float footSidePosition =
            Vector3.Dot(foot.position, sideWorldDirection);

        float targetSidePosition =
            Vector3.Dot(target, sideWorldDirection);

        target +=
            sideWorldDirection *
            (footSidePosition - targetSidePosition);

        return target;
    }

    private void CorrectFinalFootPosition(
        BoxCollider step,
        bool exactCorrection
    )
    {
        Transform leftFoot =
            animator.GetBoneTransform(HumanBodyBones.LeftFoot);

        Transform rightFoot =
            animator.GetBoneTransform(HumanBodyBones.RightFoot);

        if (leftFoot == null || rightFoot == null)
        {
            return;
        }

        Vector3 currentFeetCenter =
            (leftFoot.position + rightFoot.position) * 0.5f;

        Bounds bounds = step.bounds;

        Vector3 desiredFeetCenter =
            bounds.center +
            climbWorldDirection * landingDepthOffset;

        desiredFeetCenter.y =
            bounds.max.y + footBoneToSoleHeight;

        Vector3 correction = desiredFeetCenter - currentFeetCenter;

        // در عرض پله کاراکتر جابه‌جا نشود.
        correction -=
            sideWorldDirection *
            Vector3.Dot(correction, sideWorldDirection);

        if (!exactCorrection)
        {
            correction = Vector3.ClampMagnitude(
                correction,
                maximumFinalCorrection
            );
        }

        characterRoot.position += correction;
    }

    private void LockFeetHeightToStep(BoxCollider step)
    {
        Transform leftFoot =
            animator.GetBoneTransform(HumanBodyBones.LeftFoot);

        Transform rightFoot =
            animator.GetBoneTransform(HumanBodyBones.RightFoot);

        if (leftFoot == null || rightFoot == null)
        {
            return;
        }

        float currentFeetHeight =
            (leftFoot.position.y + rightFoot.position.y) * 0.5f;

        float desiredFeetHeight =
            step.bounds.max.y + footBoneToSoleHeight;

        float verticalCorrection =
            desiredFeetHeight - currentFeetHeight;

        if (Mathf.Abs(verticalCorrection) < 0.0001f)
        {
            return;
        }

        characterRoot.position +=
            Vector3.up * verticalCorrection;
    }

    private void BuildStepList()
    {
        steps.Clear();

        if (stairsRoot == null)
        {
            return;
        }

        climbWorldDirection =
            stairsRoot.TransformDirection(climbLocalDirection);

        climbWorldDirection.y = 0f;

        if (climbWorldDirection.sqrMagnitude < 0.001f)
        {
            climbWorldDirection = Vector3.right;
        }

        climbWorldDirection.Normalize();

        sideWorldDirection =
            Vector3.Cross(
                Vector3.up,
                climbWorldDirection
            ).normalized;

        BoxCollider[] found =
            stairsRoot.GetComponentsInChildren<BoxCollider>(true);

        foreach (BoxCollider collider in found)
        {
            if (collider != null &&
                collider.enabled &&
                collider.gameObject.activeInHierarchy)
            {
                steps.Add(collider);
            }
        }

        steps.Sort(
            (first, second) =>
            {
                float firstPosition =
                    Vector3.Dot(
                        first.bounds.center,
                        climbWorldDirection
                    );

                float secondPosition =
                    Vector3.Dot(
                        second.bounds.center,
                        climbWorldDirection
                    );

                return firstPosition.CompareTo(secondPosition);
            }
        );
    }

    private int FindFirstStepInFront()
    {
        Transform leftFoot =
            animator.GetBoneTransform(HumanBodyBones.LeftFoot);

        Transform rightFoot =
            animator.GetBoneTransform(HumanBodyBones.RightFoot);

        Vector3 currentPosition = characterRoot.position;

        if (leftFoot != null && rightFoot != null)
        {
            currentPosition =
                (leftFoot.position + rightFoot.position) * 0.5f;
        }

        float currentForwardPosition =
            Vector3.Dot(
                currentPosition,
                climbWorldDirection
            );

        for (int i = 0; i < steps.Count; i++)
        {
            float stepForwardPosition =
                Vector3.Dot(
                    steps[i].bounds.center,
                    climbWorldDirection
                );

            if (stepForwardPosition > currentForwardPosition + 0.05f)
            {
                return i;
            }
        }

        return 0;
    }

    private bool ValidateReferences()
    {
        if (animator == null)
        {
            Debug.LogError("Animator تعیین نشده است.", this);
            return false;
        }

        if (!animator.isHuman)
        {
            Debug.LogError(
                "Rig کاراکتر باید روی Humanoid تنظیم شده باشد.",
                this
            );
            return false;
        }

        if (characterRoot == null)
        {
            Debug.LogError("Character Root تعیین نشده است.", this);
            return false;
        }

        if (stairsRoot == null)
        {
            Debug.LogError("Stairs Root تعیین نشده است.", this);
            return false;
        }

        if (steps.Count == 0)
        {
            Debug.LogError(
                "هیچ BoxColliderای زیر stairs پیدا نشد.",
                this
            );
            return false;
        }

        return true;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        commands.Clear();

        standingStep = null;
        lockStandingHeight = false;
        isMoving = false;
        isReady = false;

        if (animator != null)
        {
            animator.speed = 1f;
            animator.applyRootMotion = false;

            if (animator.isMatchingTarget)
            {
                animator.InterruptMatchTarget(false);
            }
        }
    }
}
