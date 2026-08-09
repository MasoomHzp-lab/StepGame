using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Local-only Muscle Strength simulator.
/// R = right sample, L = left sample.
/// The latest displayed sample(s) remain visible for 5 seconds, then all values reset to zero.
/// This component auto-installs itself at runtime, so no manual Inspector setup is required.
/// </summary>
[DisallowMultipleComponent]
public sealed class StairGameKeyboardPowerTest : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private StairGamePowerUI powerUI;

    [Header("Keyboard Test")]
    [SerializeField] private bool enableKeyboardTest = true;
    [SerializeField, Range(0f, 100f)] private float minimumStrength = 62f;
    [SerializeField, Range(0f, 100f)] private float maximumStrength = 88f;
    [SerializeField, Range(0.05f, 1f)] private float response = 0.70f;
    [SerializeField, Min(0.1f)] private float displayDurationSeconds = 5f;
    [SerializeField] private bool logSamplesToConsole = true;

    private bool hasRightSample;
    private bool hasLeftSample;
    private float simulatedRight;
    private float simulatedLeft;
    private float remainingDisplayTime;
    private bool resetTimerActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureKeyboardPowerTestExists()
    {
        StairGamePowerUI ui = FindObjectOfType<StairGamePowerUI>();

        if (ui == null)
        {
            GameObject panel = GameObject.Find("MusclePowerPanel");
            if (panel != null)
                ui = panel.AddComponent<StairGamePowerUI>();
        }

        if (ui == null)
        {
            Debug.LogWarning("[StairGame Power Test] MusclePowerPanel / StairGamePowerUI was not found. Keyboard power test could not start.");
            return;
        }

        ui.AutoWireReferences();

        StairGameKeyboardPowerTest tester = ui.GetComponent<StairGameKeyboardPowerTest>();
        if (tester == null)
            tester = ui.gameObject.AddComponent<StairGameKeyboardPowerTest>();

        tester.powerUI = ui;
    }

    private void Awake()
    {
        ResolvePowerUI();
    }

    private void Update()
    {
        if (!enableKeyboardTest)
            return;

        if (powerUI == null)
        {
            ResolvePowerUI();
            if (powerUI == null)
                return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.rKey.wasPressedThisFrame)
                SimulateRightStep();

            if (keyboard.lKey.wasPressedThisFrame)
                SimulateLeftStep();
        }

        UpdateAutoResetTimer();
    }

    private void SimulateRightStep()
    {
        float sample = GetSample();
        simulatedRight = hasRightSample ? Mathf.Lerp(simulatedRight, sample, response) : sample;
        hasRightSample = true;

        PushValues();
        RestartAutoResetTimer();

        if (logSamplesToConsole)
            Debug.Log($"[StairGame Power Test] RIGHT = {simulatedRight:0}% | display reset in {displayDurationSeconds:0.0}s");
    }

    private void SimulateLeftStep()
    {
        float sample = GetSample();
        simulatedLeft = hasLeftSample ? Mathf.Lerp(simulatedLeft, sample, response) : sample;
        hasLeftSample = true;

        PushValues();
        RestartAutoResetTimer();

        if (logSamplesToConsole)
            Debug.Log($"[StairGame Power Test] LEFT = {simulatedLeft:0}% | display reset in {displayDurationSeconds:0.0}s");
    }

    private float GetSample()
    {
        float min = Mathf.Min(minimumStrength, maximumStrength);
        float max = Mathf.Max(minimumStrength, maximumStrength);
        return Random.Range(min, max);
    }

    private void PushValues()
    {
        float total;

        if (hasRightSample && hasLeftSample)
            total = (simulatedRight + simulatedLeft) * 0.5f;
        else if (hasRightSample)
            total = simulatedRight;
        else if (hasLeftSample)
            total = simulatedLeft;
        else
            total = 0f;

        powerUI.SetPowerValues(simulatedRight, simulatedLeft, total);
    }

    private void RestartAutoResetTimer()
    {
        remainingDisplayTime = Mathf.Max(0.1f, displayDurationSeconds);
        resetTimerActive = true;
    }

    private void UpdateAutoResetTimer()
    {
        if (!resetTimerActive)
            return;

        remainingDisplayTime -= Time.unscaledDeltaTime;
        if (remainingDisplayTime > 0f)
            return;

        ResetSimulatedStrength();
    }

    private void ResolvePowerUI()
    {
        if (powerUI != null)
            return;

        powerUI = GetComponent<StairGamePowerUI>();
        if (powerUI == null)
            powerUI = GetComponentInParent<StairGamePowerUI>();
        if (powerUI == null)
            powerUI = FindObjectOfType<StairGamePowerUI>();

        if (powerUI != null)
            powerUI.AutoWireReferences();
    }

    public void SetKeyboardTestEnabled(bool enabled)
    {
        enableKeyboardTest = enabled;
    }

    [ContextMenu("Test RIGHT Now")]
    private void TestRightNow()
    {
        ResolvePowerUI();
        if (powerUI != null)
            SimulateRightStep();
    }

    [ContextMenu("Test LEFT Now")]
    private void TestLeftNow()
    {
        ResolvePowerUI();
        if (powerUI != null)
            SimulateLeftStep();
    }

    [ContextMenu("Reset Simulated Strength")]
    private void ResetSimulatedStrength()
    {
        hasRightSample = false;
        hasLeftSample = false;
        simulatedRight = 0f;
        simulatedLeft = 0f;
        remainingDisplayTime = 0f;
        resetTimerActive = false;

        if (powerUI != null)
            powerUI.SetPowerValues(0f, 0f, 0f);

        if (logSamplesToConsole)
            Debug.Log("[StairGame Power Test] Power display reset to 0.");
    }
}
