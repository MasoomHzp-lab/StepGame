using System.Collections;
using UnityEngine;

public class StairClimber : MonoBehaviour
{
    private enum ExpectedFoot
    {
        Right,
        Left
    }

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform characterRoot;
    [SerializeField] private Transform stairsDirectionReference;

    [Header("Animator Names")]
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string stairStateName = "StairStep";
    [SerializeField] private string stepTriggerName = "Step";

    [Header("Stair Settings")]
    [SerializeField] private float stepHeight = 0.2f;
    [SerializeField] private float stepDepth = 0.6f;

    [Header("Animation Settings")]
    [Range(0.1f, 0.9f)]
    [SerializeField] private float rightFootPlantTime = 0.5f;

    [SerializeField] private float animationTransitionTime = 0.05f;

    [Header("Game Settings")]
    [SerializeField] private int targetStepCount = 20;

    private ExpectedFoot expectedFoot = ExpectedFoot.Right;

    private bool phaseIsPlaying;
    private int completedSteps;

    private Vector3 stepStartPosition;
    private Vector3 stepMiddlePosition;
    private Vector3 stepEndPosition;

    private int stepTriggerHash;

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

        stepTriggerHash = Animator.StringToHash(stepTriggerName);

        if (animator != null)
        {
            animator.applyRootMotion = false;
        }
        else
        {
            Debug.LogError(
                "StairClimber: کامپوننت Animator پیدا نشد.",
                this
            );
        }
    }

    private void Update()
    {
        // تست پای راست با کلید R
        if (Input.GetKeyDown(KeyCode.R))
        {
            CommandRightFoot();
        }

        // تست پای چپ با کلید L
        if (Input.GetKeyDown(KeyCode.L))
        {
            CommandLeftFoot();
        }

        // کلیک چپ، پای نوبتی را حرکت می‌دهد.
        if (Input.GetMouseButtonDown(0))
        {
            CommandNextFoot();
        }
    }

    private void CommandNextFoot()
    {
        if (expectedFoot == ExpectedFoot.Right)
        {
            CommandRightFoot();
        }
        else
        {
            CommandLeftFoot();
        }
    }

    public void CommandRightFoot()
    {
        if (phaseIsPlaying)
        {
            return;
        }

        if (expectedFoot != ExpectedFoot.Right)
        {
            Debug.Log("الان نوبت پای راست نیست.");
            return;
        }

        if (completedSteps >= targetStepCount)
        {
            Debug.Log("تعداد پله‌های هدف کامل شده است.");
            return;
        }

        StartCoroutine(PlayRightFootPhase());
    }

    public void CommandLeftFoot()
    {
        if (phaseIsPlaying)
        {
            return;
        }

        if (expectedFoot != ExpectedFoot.Left)
        {
            Debug.Log("الان نوبت پای چپ نیست.");
            return;
        }

        if (completedSteps >= targetStepCount)
        {
            Debug.Log("تعداد پله‌های هدف کامل شده است.");
            return;
        }

        StartCoroutine(PlayLeftFootPhase());
    }

    private IEnumerator PlayRightFootPhase()
    {
        if (animator == null)
        {
            Debug.LogError("Animator مشخص نشده است.", this);
            yield break;
        }

        phaseIsPlaying = true;

        CalculateStepPositions();

        animator.speed = 1f;
        animator.ResetTrigger(stepTriggerHash);
        animator.SetTrigger(stepTriggerHash);

        float stateWaitTimer = 0f;

        // صبر می‌کنیم تا Animator وارد StairStep شود.
        while (
            !animator
                .GetCurrentAnimatorStateInfo(0)
                .IsName(stairStateName)
        )
        {
            stateWaitTimer += Time.deltaTime;

            if (stateWaitTimer >= 2f)
            {
                Debug.LogError(
                    "Animator وارد State مربوط به پله نشد. " +
                    "نام StairStep، Trigger و Transition را بررسی کن.",
                    this
                );

                phaseIsPlaying = false;
                yield break;
            }

            yield return null;
        }

        // اجرای بخش اول انیمیشن؛ حرکت پای راست.
        while (true)
        {
            AnimatorStateInfo stateInfo =
                animator.GetCurrentAnimatorStateInfo(0);

            if (!stateInfo.IsName(stairStateName))
            {
                Debug.LogError(
                    "انیمیشن قبل از رسیدن پای راست متوقف شد.",
                    this
                );

                phaseIsPlaying = false;
                yield break;
            }

            float normalizedTime =
                Mathf.Clamp01(stateInfo.normalizedTime);

            float movementProgress = Mathf.InverseLerp(
                0f,
                rightFootPlantTime,
                normalizedTime
            );

            characterRoot.position = Vector3.Lerp(
                stepStartPosition,
                stepMiddlePosition,
                SmoothStep(movementProgress)
            );

            if (normalizedTime >= rightFootPlantTime)
            {
                break;
            }

            yield return null;
        }

        characterRoot.position = stepMiddlePosition;

        // انیمیشن در لحظه قرارگرفتن پای راست متوقف می‌شود.
        animator.speed = 0f;

        expectedFoot = ExpectedFoot.Left;
        phaseIsPlaying = false;

        Debug.Log(
            "پای راست روی پله قرار گرفت. حالا نوبت پای چپ است."
        );
    }

    private IEnumerator PlayLeftFootPhase()
    {
        if (animator == null)
        {
            Debug.LogError("Animator مشخص نشده است.", this);
            yield break;
        }

        phaseIsPlaying = true;

        // ادامه انیمیشن از محل توقف پای راست
        animator.speed = 1f;

        while (true)
        {
            AnimatorStateInfo stateInfo =
                animator.GetCurrentAnimatorStateInfo(0);

            if (!stateInfo.IsName(stairStateName))
            {
                break;
            }

            float normalizedTime =
                Mathf.Clamp01(stateInfo.normalizedTime);

            float movementProgress = Mathf.InverseLerp(
                rightFootPlantTime,
                1f,
                normalizedTime
            );

            characterRoot.position = Vector3.Lerp(
                stepMiddlePosition,
                stepEndPosition,
                SmoothStep(movementProgress)
            );

            if (normalizedTime >= 0.98f)
            {
                break;
            }

            yield return null;
        }

        characterRoot.position = stepEndPosition;

        animator.speed = 1f;

        animator.CrossFade(
            idleStateName,
            animationTransitionTime
        );

        completedSteps++;

        expectedFoot = ExpectedFoot.Right;
        phaseIsPlaying = false;

        Debug.Log(
            "یک پله کامل شد. تعداد: " +
            completedSteps +
            "/" +
            targetStepCount
        );

        if (completedSteps >= targetStepCount)
        {
            Debug.Log("هدف ۲۰ پله کامل شد.");
        }
    }

    private void CalculateStepPositions()
    {
        Vector3 stairForward;

        if (stairsDirectionReference != null)
        {
            stairForward = stairsDirectionReference.right;
        }
        else
        {
            stairForward = Vector3.right;
        }

        stairForward.y = 0f;

        if (stairForward.sqrMagnitude < 0.001f)
        {
            stairForward = Vector3.right;
        }

        stairForward.Normalize();

        Vector3 fullStepMovement =
            stairForward * stepDepth +
            Vector3.up * stepHeight;

        stepStartPosition = characterRoot.position;

        stepMiddlePosition =
            stepStartPosition +
            fullStepMovement * 0.5f;

        stepEndPosition =
            stepStartPosition +
            fullStepMovement;
    }

    private float SmoothStep(float value)
    {
        value = Mathf.Clamp01(value);

        return value * value * (3f - 2f * value);
    }

    private void OnDisable()
    {
        if (animator != null)
        {
            animator.speed = 1f;
        }
    }
}