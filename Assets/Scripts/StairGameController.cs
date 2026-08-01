using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class StairGameController : MonoBehaviour
{
    public enum FootSide
    {
        Right,
        Left
    }

    [Header("References")]
    [SerializeField] private Transform stairsRoot;
    [SerializeField] private Animator animator;

    [Header("Animator States")]
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string rightStepStateName = "RightStep";
    [SerializeField] private string leftStepStateName = "LeftStep";

    [Header("Animation Settings")]
    [SerializeField, Min(0f)]
    private float transitionDuration = 0.08f;

    [SerializeField, Range(0.5f, 1f)]
    private float animationCompletionTime = 0.95f;

    [SerializeField, Min(1f)]
    private float animationTimeout = 5f;

    [Header("Stair Direction")]
    [SerializeField] private Vector3 climbLocalDirection = Vector3.right;

    [Header("Session Settings")]
    [SerializeField] private FootSide startingFoot = FootSide.Right;
    [SerializeField] private bool enableKeyboardTest = true;

    [Header("Runtime State - Read Only")]
    [SerializeField] private FootSide expectedFoot;
    [SerializeField] private int nextTargetStepIndex;
    [SerializeField] private int rightFootStepIndex = -1;
    [SerializeField] private int leftFootStepIndex = -1;
    [SerializeField] private bool sessionStarted;
    [SerializeField] private bool isAnimating;

    private readonly List<BoxCollider> steps =
        new List<BoxCollider>();

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator == null)
        {
            Debug.LogError(
                "Animator has not been assigned or found.",
                this
            );

            enabled = false;
            return;
        }

        // Character movement will be controlled manually later.
        animator.applyRootMotion = false;

        if (!BuildStepList())
        {
            enabled = false;
            return;
        }

        ResetSession();
    }

    private void Update()
    {
        if (!enableKeyboardTest || !sessionStarted)
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
            CommandRightFoot();
        }

        if (keyboard.lKey.wasPressedThisFrame)
        {
            CommandLeftFoot();
        }
    }

    public void CommandRightFoot()
    {
        TryStartStep(FootSide.Right);
    }

    public void CommandLeftFoot()
    {
        TryStartStep(FootSide.Left);
    }

    public bool TryStartStep(FootSide requestedFoot)
    {
        if (!sessionStarted)
        {
            Debug.LogWarning(
                "The session has not started yet.",
                this
            );

            return false;
        }

        if (isAnimating)
        {
            Debug.LogWarning(
                "Input ignored: a step animation is already playing.",
                this
            );

            return false;
        }

        if (nextTargetStepIndex >= steps.Count)
        {
            CompleteSession();
            return false;
        }

        if (requestedFoot != expectedFoot)
        {
            Debug.LogWarning(
                $"Step rejected: it is currently the " +
                $"{GetFootName(expectedFoot)} foot's turn.",
                this
            );

            return false;
        }

        StartCoroutine(
            PlayStepAnimation(requestedFoot)
        );

        return true;
    }

    private IEnumerator PlayStepAnimation(FootSide requestedFoot)
    {
        isAnimating = true;

        string targetStateName =
            requestedFoot == FootSide.Right
                ? rightStepStateName
                : leftStepStateName;

        int targetShortNameHash =
            Animator.StringToHash(targetStateName);

        animator.speed = 1f;
        animator.applyRootMotion = false;

        Debug.Log(
            $"Starting {GetFootName(requestedFoot)} foot animation.",
            this
        );

        animator.CrossFadeInFixedTime(
            targetStateName,
            transitionDuration,
            0,
            0f
        );

        float elapsedTime = 0f;
        bool animationStarted = false;
        bool animationCompleted = false;

        while (elapsedTime < animationTimeout)
        {
            AnimatorStateInfo stateInfo =
                animator.GetCurrentAnimatorStateInfo(0);

            bool isTargetState =
                stateInfo.shortNameHash == targetShortNameHash;

            if (isTargetState)
            {
                animationStarted = true;

                if (!animator.IsInTransition(0) &&
                    stateInfo.normalizedTime >= animationCompletionTime)
                {
                    animationCompleted = true;
                    break;
                }
            }

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        if (!animationStarted)
        {
            Debug.LogError(
                $"Animator state '{targetStateName}' was not found or did not start.",
                this
            );

            animator.CrossFadeInFixedTime(
                idleStateName,
                transitionDuration
            );

            isAnimating = false;
            yield break;
        }

        if (!animationCompleted)
        {
            Debug.LogWarning(
                $"Animation '{targetStateName}' reached the timeout limit.",
                this
            );
        }

        RegisterSuccessfulStep(requestedFoot);

        animator.CrossFadeInFixedTime(
            idleStateName,
            transitionDuration
        );

        isAnimating = false;
    }

    private void RegisterSuccessfulStep(FootSide requestedFoot)
    {
        if (nextTargetStepIndex >= steps.Count)
        {
            CompleteSession();
            return;
        }

        BoxCollider targetStep =
            steps[nextTargetStepIndex];

        if (requestedFoot == FootSide.Right)
        {
            rightFootStepIndex = nextTargetStepIndex;
        }
        else
        {
            leftFootStepIndex = nextTargetStepIndex;
        }

        Debug.Log(
            $"Successful step: {GetFootName(requestedFoot)} foot" +
            $" on {targetStep.name}" +
            $" | Step number: {nextTargetStepIndex + 1}",
            this
        );

        nextTargetStepIndex++;

        expectedFoot =
            requestedFoot == FootSide.Right
                ? FootSide.Left
                : FootSide.Right;

        if (nextTargetStepIndex >= steps.Count)
        {
            CompleteSession();
            return;
        }

        Debug.Log(
            $"Next movement: {GetFootName(expectedFoot)} foot" +
            $" to step number {nextTargetStepIndex + 1}.",
            this
        );
    }

    [ContextMenu("Reset Session")]
    public void ResetSession()
    {
        StopAllCoroutines();

        nextTargetStepIndex = 0;
        rightFootStepIndex = -1;
        leftFootStepIndex = -1;

        expectedFoot = startingFoot;
        sessionStarted = true;
        isAnimating = false;

        if (animator != null)
        {
            animator.speed = 1f;
            animator.applyRootMotion = false;

            animator.Play(
                idleStateName,
                0,
                0f
            );

            animator.Update(0f);
        }

        Debug.Log(
            $"Session started. Total steps: {steps.Count}" +
            $" | Starting foot: {GetFootName(expectedFoot)}.",
            this
        );
    }

    public void StopSession()
    {
        StopAllCoroutines();

        sessionStarted = false;
        isAnimating = false;

        if (animator != null)
        {
            animator.CrossFadeInFixedTime(
                idleStateName,
                transitionDuration
            );
        }

        Debug.Log(
            "Session stopped.",
            this
        );
    }

    private void CompleteSession()
    {
        sessionStarted = false;

        Debug.Log(
            $"Path completed. Successful steps: {nextTargetStepIndex}.",
            this
        );
    }

    private bool BuildStepList()
    {
        steps.Clear();

        if (stairsRoot == null)
        {
            Debug.LogError(
                "Stairs Root has not been assigned.",
                this
            );

            return false;
        }

        if (climbLocalDirection.sqrMagnitude < 0.001f)
        {
            Debug.LogError(
                "Climb Local Direction cannot be zero.",
                this
            );

            return false;
        }

        BoxCollider[] foundSteps =
            stairsRoot.GetComponentsInChildren<BoxCollider>(true);

        foreach (BoxCollider step in foundSteps)
        {
            if (step.transform == stairsRoot)
            {
                continue;
            }

            steps.Add(step);
        }

        Vector3 climbWorldDirection =
            stairsRoot.TransformDirection(
                climbLocalDirection.normalized
            );

        steps.Sort(
            (first, second) =>
            {
                float firstDistance = Vector3.Dot(
                    first.bounds.center,
                    climbWorldDirection
                );

                float secondDistance = Vector3.Dot(
                    second.bounds.center,
                    climbWorldDirection
                );

                return firstDistance.CompareTo(secondDistance);
            }
        );

        if (steps.Count == 0)
        {
            Debug.LogError(
                "No steps with Box Colliders were found.",
                this
            );

            return false;
        }

        Debug.Log(
            $"{steps.Count} steps were found and sorted.",
            this
        );

        return true;
    }

    private string GetFootName(FootSide foot)
    {
        return foot == FootSide.Right
            ? "right"
            : "left";
    }
}