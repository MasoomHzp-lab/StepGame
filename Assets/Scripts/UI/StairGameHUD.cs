using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Runtime HUD for StepGame.
/// v3: exercise mode can be changed after completed stairs without resetting progress.
/// </summary>
public class StairGameHUD : MonoBehaviour
{
    [Header("Game References")]
    [SerializeField] private StairClimbControllerV2 stairController;
    [SerializeField] private StairPathV2 stairPath;
    [SerializeField] private StairGameAudioManager audioManager;

    [Header("Step Counter")]
    [SerializeField] private TMP_Text currentStepText;
    [SerializeField] private TMP_Text totalStepText;

    [Header("Progress")]
    [SerializeField] private Slider progressSlider;
    [SerializeField] private TMP_Text progressPercentText;

    [Header("Exercise Mode Buttons")]
    [SerializeField] private Button bothFeetButton;
    [SerializeField] private Button rightOnlyButton;
    [SerializeField] private Button leftOnlyButton;

    [Header("Mode Selected Indicators")]
    [SerializeField] private GameObject bothFeetSelected;
    [SerializeField] private GameObject rightOnlySelected;
    [SerializeField] private GameObject leftOnlySelected;

    [Header("Milestone Toast")]
    [SerializeField] private CanvasGroup milestoneToast;
    [SerializeField] private TMP_Text milestoneTitleText;
    [SerializeField] private TMP_Text milestoneMessageText;
    [SerializeField, Min(1)] private int milestoneInterval = 20;
    [SerializeField, Min(0.1f)] private float milestoneVisibleSeconds = 2.4f;
    [SerializeField, Min(0.01f)] private float milestoneFadeSeconds = 0.22f;

    [Header("Exit")]
    [SerializeField] private Button exitButton;
    [Tooltip("If filled, Exit loads this scene. If empty, the application quits.")]
    [SerializeField] private string menuSceneName = "";

    [Header("Fallback")]
    [SerializeField, Min(1)] private int fallbackTotalSteps = 100;

    private int totalSteps;
    private int lastDisplayedSteps = -1;
    private int lastMilestoneShown;
    private StairClimbControllerV2.LegActivationMode lastDisplayedMode;
    private bool hasDisplayedMode;
    private Coroutine milestoneRoutine;

    public int CompletedSteps => CalculateCompletedSteps();
    public int TotalSteps => totalSteps;

    private void Awake()
    {
        if (bothFeetButton != null)
            bothFeetButton.onClick.AddListener(SetBothFeetMode);

        if (rightOnlyButton != null)
            rightOnlyButton.onClick.AddListener(SetRightOnlyMode);

        if (leftOnlyButton != null)
            leftOnlyButton.onClick.AddListener(SetLeftOnlyMode);

        if (exitButton != null)
            exitButton.onClick.AddListener(ExitGame);
    }

    private void Start()
    {
        ResolveTotalSteps();

        if (stairController != null && !stairController.SessionStarted)
            stairController.ResetSession();

        ConfigureProgress();
        HideMilestoneImmediately();
        RefreshModeSelection();
        RefreshHUD(true);
    }

    private void Update()
    {
        RefreshHUD(false);
        RefreshModeStateIfNeeded();
        RefreshModeButtonInteractability();
    }

    private void ResolveTotalSteps()
    {
        if (stairPath != null)
        {
            stairPath.RefreshSteps();
            totalSteps = stairPath.StepCount;
        }

        if (totalSteps <= 0)
            totalSteps = fallbackTotalSteps;
    }

    private void ConfigureProgress()
    {
        if (progressSlider != null)
        {
            progressSlider.minValue = 0f;
            progressSlider.maxValue = totalSteps;
            progressSlider.value = 0f;
            progressSlider.interactable = false;
        }

        if (totalStepText != null)
            totalStepText.text = $"of {totalSteps}";
    }

    private void RefreshHUD(bool force)
    {
        int completed = CalculateCompletedSteps();

        if (!force && completed == lastDisplayedSteps)
            return;

        lastDisplayedSteps = completed;

        if (currentStepText != null)
            currentStepText.text = completed.ToString();

        if (progressSlider != null)
            progressSlider.value = completed;

        if (progressPercentText != null)
        {
            float percent = totalSteps > 0
                ? (completed / (float)totalSteps) * 100f
                : 0f;

            progressPercentText.text = $"{Mathf.RoundToInt(percent)}%";
        }

        CheckMilestone(completed);
    }

    private int CalculateCompletedSteps()
    {
        if (stairController == null)
            return 0;

        // In the current controller, single-leg mode still automatically
        // completes the join movement, so both indexes normally end equal.
        // This mode-aware version also remains correct if that changes later.
        int completedIndex;

        switch (stairController.ActivationMode)
        {
            case StairClimbControllerV2.LegActivationMode.RightOnly:
                completedIndex = stairController.RightFootStepIndex;
                break;

            case StairClimbControllerV2.LegActivationMode.LeftOnly:
                completedIndex = stairController.LeftFootStepIndex;
                break;

            default:
                completedIndex = Mathf.Min(
                    stairController.RightFootStepIndex,
                    stairController.LeftFootStepIndex
                );
                break;
        }

        return Mathf.Clamp(completedIndex + 1, 0, totalSteps);
    }

    public void SetBothFeetMode()
    {
        TryChangeMode(StairClimbControllerV2.LegActivationMode.BothFeet);
    }

    public void SetRightOnlyMode()
    {
        TryChangeMode(StairClimbControllerV2.LegActivationMode.RightOnly);
    }

    public void SetLeftOnlyMode()
    {
        TryChangeMode(StairClimbControllerV2.LegActivationMode.LeftOnly);
    }

    private void TryChangeMode(StairClimbControllerV2.LegActivationMode newMode)
    {
        if (stairController == null)
            return;

        if (!stairController.TrySetActivationModeWithoutReset(newMode))
            return;

        RefreshModeSelection();
        RefreshHUD(true);
    }

    private void RefreshModeStateIfNeeded()
    {
        if (stairController == null)
            return;

        if (!hasDisplayedMode ||
            stairController.ActivationMode != lastDisplayedMode)
        {
            RefreshModeSelection();
        }
    }

    private void RefreshModeSelection()
    {
        if (stairController == null)
            return;

        var mode = stairController.ActivationMode;

        if (bothFeetSelected != null)
            bothFeetSelected.SetActive(
                mode == StairClimbControllerV2.LegActivationMode.BothFeet
            );

        if (rightOnlySelected != null)
            rightOnlySelected.SetActive(
                mode == StairClimbControllerV2.LegActivationMode.RightOnly
            );

        if (leftOnlySelected != null)
            leftOnlySelected.SetActive(
                mode == StairClimbControllerV2.LegActivationMode.LeftOnly
            );

        lastDisplayedMode = mode;
        hasDisplayedMode = true;
    }

    private void RefreshModeButtonInteractability()
    {
        if (stairController == null)
            return;

        bool canChange = stairController.CanChangeActivationModeWithoutReset;

        // Keep the currently selected button visually active/click-safe.
        // Other choices are disabled only during an unsafe half-step/animation.
        if (bothFeetButton != null)
            bothFeetButton.interactable =
                canChange ||
                stairController.ActivationMode ==
                StairClimbControllerV2.LegActivationMode.BothFeet;

        if (rightOnlyButton != null)
            rightOnlyButton.interactable =
                canChange ||
                stairController.ActivationMode ==
                StairClimbControllerV2.LegActivationMode.RightOnly;

        if (leftOnlyButton != null)
            leftOnlyButton.interactable =
                canChange ||
                stairController.ActivationMode ==
                StairClimbControllerV2.LegActivationMode.LeftOnly;
    }

    private void CheckMilestone(int completed)
    {
        if (completed <= 0 ||
            milestoneInterval <= 0 ||
            completed % milestoneInterval != 0 ||
            completed == lastMilestoneShown)
        {
            return;
        }

        lastMilestoneShown = completed;

        if (milestoneRoutine != null)
            StopCoroutine(milestoneRoutine);

        milestoneRoutine = StartCoroutine(ShowMilestone(completed));
    }

    private IEnumerator ShowMilestone(int completed)
    {
        if (milestoneTitleText != null)
            milestoneTitleText.text = "Congratulations!";

        if (milestoneMessageText != null)
            milestoneMessageText.text =
                $"You successfully completed {completed} stairs!";

        audioManager?.PlayMilestoneSound();

        if (milestoneToast == null)
            yield break;

        milestoneToast.gameObject.SetActive(true);
        milestoneToast.blocksRaycasts = false;
        milestoneToast.interactable = false;

        float t = 0f;
        while (t < milestoneFadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            milestoneToast.alpha =
                Mathf.Clamp01(t / milestoneFadeSeconds);
            yield return null;
        }

        milestoneToast.alpha = 1f;

        yield return new WaitForSecondsRealtime(milestoneVisibleSeconds);

        t = 0f;
        while (t < milestoneFadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            milestoneToast.alpha =
                1f - Mathf.Clamp01(t / milestoneFadeSeconds);
            yield return null;
        }

        milestoneToast.alpha = 0f;
        milestoneToast.gameObject.SetActive(false);
        milestoneRoutine = null;
    }

    private void HideMilestoneImmediately()
    {
        if (milestoneToast == null)
            return;

        milestoneToast.alpha = 0f;
        milestoneToast.blocksRaycasts = false;
        milestoneToast.interactable = false;
        milestoneToast.gameObject.SetActive(false);
    }

    public void ExitGame()
    {
        Time.timeScale = 1f;

        if (stairController != null)
            stairController.StopSession();

        if (!string.IsNullOrWhiteSpace(menuSceneName))
        {
            SceneManager.LoadScene(menuSceneName);
            return;
        }

        Application.Quit();

#if UNITY_EDITOR
        Debug.Log(
            "Exit pressed. Application.Quit() does not stop Play Mode inside the Unity Editor.",
            this
        );
#endif
    }
}