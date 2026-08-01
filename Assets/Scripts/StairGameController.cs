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
    [SerializeField]
    private Transform stairsRoot;

    [SerializeField]
    private Animator animator;

    [SerializeField]
    private CharacterStepMover characterStepMover;

    [Header("Animator States")]
    [SerializeField]
    private string idleStateName = "Idle";

    [SerializeField]
    private string rightLeadStateName = "RightLeadStep";

    [SerializeField]
    private string leftLeadStateName = "LeftLeadStep";

    [SerializeField]
    private string rightJoinStateName = "RightJoinStep";

    [SerializeField]
    private string leftJoinStateName = "LeftJoinStep";

    [Header("Animation Settings")]
    [SerializeField, Min(0f)]
    private float transitionDuration = 0.02f;

    [SerializeField, Range(0.5f, 1f)]
    private float animationCompletionTime = 0.98f;

    [SerializeField, Min(1f)]
    private float animationTimeout = 5f;

    [Tooltip("Delay between the lead movement and the automatic join movement.")]
    [SerializeField, Min(0f)]
    private float automaticJoinDelay = 0.08f;

    [Header("Stair Direction")]
    [SerializeField]
    private Vector3 climbLocalDirection = Vector3.right;

    [Header("Session Settings")]
    [Tooltip("Only controls which foot is initially highlighted. It does not lock the input.")]
    [SerializeField]
    private FootSide startingFoot = FootSide.Right;

    [SerializeField]
    private bool enableKeyboardTest = true;

    [Header("Runtime State - Read Only")]
    [SerializeField]
    private FootSide expectedFoot;

    [SerializeField]
    private int nextTargetStepIndex;

    [SerializeField]
    private int rightFootStepIndex = -1;

    [SerializeField]
    private int leftFootStepIndex = -1;

    [SerializeField]
    private bool sessionStarted;

    [SerializeField]
    private bool isAnimating;

    [SerializeField]
    private bool automaticJoinInProgress;

    private readonly List<BoxCollider> steps =
        new List<BoxCollider>();

    private bool lastMovementSucceeded;

    public FootSide ExpectedFoot
    {
        get
        {
            return expectedFoot;
        }
    }

    public bool SessionStarted
    {
        get
        {
            return sessionStarted;
        }
    }

    public bool IsAnimating
    {
        get
        {
            return isAnimating;
        }
    }

    public Vector3 ClimbWorldDirection
    {
        get
        {
            if (stairsRoot == null ||
                climbLocalDirection.sqrMagnitude < 0.001f)
            {
                return transform.forward;
            }

            return stairsRoot.TransformDirection(
                climbLocalDirection.normalized
            ).normalized;
        }
    }

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (animator == null)
        {
            animator = GetComponentInParent<Animator>();
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

        if (characterStepMover == null)
        {
            characterStepMover =
                GetComponent<CharacterStepMover>();
        }

        if (characterStepMover == null)
        {
            characterStepMover =
                GetComponentInParent<CharacterStepMover>();
        }

        if (characterStepMover == null)
        {
            Debug.LogError(
                "Character Step Mover has not been assigned or found.",
                this
            );

            enabled = false;
            return;
        }

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
        if (!enableKeyboardTest ||
            !sessionStarted)
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

    /// <summary>
    /// Starts one complete stair movement.
    /// The requested foot leads and the opposite foot joins automatically.
    /// </summary>
    public bool TryStartStep(
        FootSide requestedFoot
    )
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
                "Input ignored: a complete stair movement is already playing.",
                this
            );

            return false;
        }

        if (nextTargetStepIndex < 0 ||
            nextTargetStepIndex >= steps.Count)
        {
            CompleteSession();
            return false;
        }

        BoxCollider targetStep =
            steps[nextTargetStepIndex];

        /*
         * Both feet are valid.
         * The pressed key determines which foot leads.
         */
        expectedFoot = requestedFoot;

        StartCoroutine(
            PlayCompleteStairSequence(
                requestedFoot,
                targetStep,
                nextTargetStepIndex
            )
        );

        return true;
    }

    /// <summary>
    /// Plays the lead movement and then automatically
    /// plays the opposite foot's join movement.
    /// </summary>
    private IEnumerator PlayCompleteStairSequence(
        FootSide leadFoot,
        BoxCollider targetStep,
        int targetStepIndex
    )
    {
        isAnimating = true;
        automaticJoinInProgress = false;

        FootSide joinFoot =
            GetOppositeFoot(leadFoot);

        Debug.Log(
            $"Starting complete stair movement | " +
            $"Lead foot: {GetFootName(leadFoot)} | " +
            $"Automatic join foot: {GetFootName(joinFoot)} | " +
            $"Target: {targetStep.name}",
            this
        );

        /*
         * First movement:
         * The selected foot moves onto the next stair.
         */
        expectedFoot = leadFoot;

        yield return PlaySingleMovement(
            leadFoot,
            targetStep,
            targetStepIndex,
            false
        );

        if (!lastMovementSucceeded)
        {
            Debug.LogError(
                "The lead movement failed. " +
                "The automatic join movement was cancelled.",
                this
            );

            automaticJoinInProgress = false;
            isAnimating = false;

            yield break;
        }

        /*
         * Small natural pause before the second foot joins.
         */
        if (automaticJoinDelay > 0f)
        {
            yield return new WaitForSeconds(
                automaticJoinDelay
            );
        }

        /*
         * Second movement:
         * The opposite foot automatically joins the lead foot.
         * The player does not press another key.
         */
        automaticJoinInProgress = true;
        expectedFoot = joinFoot;

        Debug.Log(
            $"Lead movement finished. " +
            $"Starting automatic {GetFootName(joinFoot)} join movement.",
            this
        );

        yield return PlaySingleMovement(
            joinFoot,
            targetStep,
            targetStepIndex,
            true
        );

        automaticJoinInProgress = false;

        if (!lastMovementSucceeded)
        {
            Debug.LogError(
                "The automatic join movement failed.",
                this
            );

            isAnimating = false;
            yield break;
        }

        /*
         * Both feet have now completed the same stair.
         * Only now do we move the target to the next stair.
         */
        rightFootStepIndex = targetStepIndex;
        leftFootStepIndex = targetStepIndex;

        nextTargetStepIndex++;

        /*
         * This is only the suggested/highlighted foot.
         * The next R or L input is still accepted.
         */
        expectedFoot =
            GetOppositeFoot(leadFoot);

        isAnimating = false;

        Debug.Log(
            $"Stair completed | " +
            $"Target: {targetStep.name} | " +
            $"Both feet are now on step index {targetStepIndex}.",
            this
        );

        if (nextTargetStepIndex >= steps.Count)
        {
            CompleteSession();
            yield break;
        }

        Debug.Log(
            $"Ready for the next stair: " +
            $"{steps[nextTargetStepIndex].name}. " +
            "Press R or L to select the next lead foot.",
            this
        );
    }

    /// <summary>
    /// Plays one section of the stair movement:
    /// either LeadStep or JoinStep.
    /// </summary>
    private IEnumerator PlaySingleMovement(
        FootSide movingFoot,
        BoxCollider targetStep,
        int targetStepIndex,
        bool isJoinMovement
    )
    {
        lastMovementSucceeded = false;

        string targetStateName =
            GetAnimationStateName(
                movingFoot,
                isJoinMovement
            );

        int targetShortNameHash =
            Animator.StringToHash(
                targetStateName
            );

        string movementType =
            isJoinMovement
                ? "join"
                : "lead";

        bool movePrepared =
            characterStepMover.PrepareMove(
                movingFoot,
                targetStep,
                targetStateName,
                ClimbWorldDirection
            );

        if (!movePrepared)
        {
            Debug.LogError(
                $"Could not prepare the " +
                $"{GetFootName(movingFoot)} " +
                $"{movementType} movement.",
                this
            );

            yield break;
        }

        animator.applyRootMotion = false;
        animator.speed = 1f;

        Debug.Log(
            $"Playing {movementType} animation | " +
            $"Foot: {GetFootName(movingFoot)} | " +
            $"State: {targetStateName}",
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
            yield return null;

            AnimatorStateInfo stateInfo =
                animator.GetCurrentAnimatorStateInfo(0);

            bool isTargetState =
                stateInfo.shortNameHash ==
                targetShortNameHash;

            if (isTargetState)
            {
                animationStarted = true;

                float movementProgress =
                    Mathf.Clamp01(
                        stateInfo.normalizedTime /
                        animationCompletionTime
                    );

                characterStepMover.ApplyProgress(
                    movementProgress
                );

                if (!animator.IsInTransition(0) &&
                    stateInfo.normalizedTime >=
                    animationCompletionTime)
                {
                    animationCompleted = true;
                    break;
                }
            }

            elapsedTime += Time.deltaTime;
        }

        if (!animationStarted)
        {
            characterStepMover.CancelMove();

            Debug.LogError(
                $"Animator state '{targetStateName}' " +
                "was not found or did not start.",
                this
            );

            yield break;
        }

        if (!animationCompleted)
        {
            Debug.LogWarning(
                $"Animation '{targetStateName}' " +
                "reached the timeout limit. " +
                "The final position will still be applied.",
                this
            );
        }

        characterStepMover.CompleteMove();

        if (movingFoot == FootSide.Right)
        {
            rightFootStepIndex =
                targetStepIndex;
        }
        else
        {
            leftFootStepIndex =
                targetStepIndex;
        }

        /*
         * Freeze the final pose.
         * The next animation starts from this exact position.
         */
        animator.Play(
            targetStateName,
            0,
            0.999f
        );

        animator.Update(0f);
        animator.speed = 0f;

        lastMovementSucceeded = true;

        Debug.Log(
            $"{movementType} movement completed | " +
            $"Foot: {GetFootName(movingFoot)} | " +
            $"Step index: {targetStepIndex}",
            this
        );
    }

    private string GetAnimationStateName(
        FootSide foot,
        bool isJoinMovement
    )
    {
        if (!isJoinMovement)
        {
            return foot == FootSide.Right
                ? rightLeadStateName
                : leftLeadStateName;
        }

        return foot == FootSide.Right
            ? rightJoinStateName
            : leftJoinStateName;
    }

    private FootSide GetOppositeFoot(
        FootSide foot
    )
    {
        return foot == FootSide.Right
            ? FootSide.Left
            : FootSide.Right;
    }

    public bool TryGetCurrentTargetStep(
        out BoxCollider targetStep
    )
    {
        targetStep = null;

        if (!sessionStarted)
        {
            return false;
        }

        if (nextTargetStepIndex < 0 ||
            nextTargetStepIndex >= steps.Count)
        {
            return false;
        }

        targetStep =
            steps[nextTargetStepIndex];

        return targetStep != null;
    }

    [ContextMenu("Reset Session")]
    public void ResetSession()
    {
        StopAllCoroutines();

        if (characterStepMover != null)
        {
            characterStepMover
                .ResetCharacterPosition();
        }

        nextTargetStepIndex = 0;

        rightFootStepIndex = -1;
        leftFootStepIndex = -1;

        expectedFoot = startingFoot;

        sessionStarted = true;
        isAnimating = false;
        automaticJoinInProgress = false;
        lastMovementSucceeded = false;

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
            $"Session started | " +
            $"Total stairs: {steps.Count} | " +
            $"Initial suggested foot: {GetFootName(expectedFoot)} | " +
            "Both R and L inputs are accepted.",
            this
        );
    }

    public void StopSession()
    {
        StopAllCoroutines();

        if (characterStepMover != null)
        {
            characterStepMover.CancelMove();
        }

        sessionStarted = false;
        isAnimating = false;
        automaticJoinInProgress = false;
        lastMovementSucceeded = false;

        if (animator != null)
        {
            animator.speed = 1f;
            animator.applyRootMotion = false;

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
        isAnimating = false;
        automaticJoinInProgress = false;

        Debug.Log(
            $"Path completed | " +
            $"Completed stairs: {nextTargetStepIndex}.",
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
            stairsRoot.GetComponentsInChildren
            <BoxCollider>(true);

        foreach (BoxCollider step in foundSteps)
        {
            if (step == null ||
                step.transform == stairsRoot)
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
                float firstDistance =
                    Vector3.Dot(
                        first.bounds.center,
                        climbWorldDirection
                    );

                float secondDistance =
                    Vector3.Dot(
                        second.bounds.center,
                        climbWorldDirection
                    );

                return firstDistance.CompareTo(
                    secondDistance
                );
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

    private string GetFootName(
        FootSide foot
    )
    {
        return foot == FootSide.Right
            ? "right"
            : "left";
    }
}