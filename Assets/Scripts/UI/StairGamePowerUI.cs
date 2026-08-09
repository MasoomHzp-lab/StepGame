using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Right / left / total muscle-power display.
/// v3 adds:
/// - inspector live-test mode
/// - separate API entry points
/// - clear distinction between test data and real external data
/// </summary>
public class StairGamePowerUI : MonoBehaviour
{
    [Header("Texts")]
    [SerializeField] private TMP_Text rightPowerText;
    [SerializeField] private TMP_Text leftPowerText;
    [SerializeField] private TMP_Text totalPowerText;

    [Header("Bars")]
    [SerializeField] private Slider rightPowerBar;
    [SerializeField] private Slider leftPowerBar;
    [SerializeField] private Slider totalPowerBar;

    [Header("Display")]
    [SerializeField, Min(1f)] private float maximumValue = 100f;
    [SerializeField] private string unit = "%";

    [Header("Debug Test - Disable When API Is Connected")]
    [SerializeField] private bool useTestValues = false;
    [SerializeField, Range(0f, 100f)] private float testRightPower = 72f;
    [SerializeField, Range(0f, 100f)] private float testLeftPower = 68f;
    [SerializeField, Range(0f, 100f)] private float testTotalPower = 70f;

    private float rightPower;
    private float leftPower;
    private float totalPower;

    public float RightPower => rightPower;
    public float LeftPower => leftPower;
    public float TotalPower => totalPower;

    private void Start()
    {
        ConfigureBar(rightPowerBar);
        ConfigureBar(leftPowerBar);
        ConfigureBar(totalPowerBar);

        if (useTestValues)
            ApplyTestValues();
        else
            SetPowerValues(0f, 0f, 0f);
    }

    private void Update()
    {
        // Lets you move the three Inspector test sliders during Play Mode
        // and instantly verify that the UI wiring works.
        if (useTestValues)
            ApplyTestValues();
    }

    /// <summary>
    /// API entry point when total = average of right and left.
    /// </summary>
    public void SetPowerValues(float right, float left)
    {
        SetPowerValues(right, left, (right + left) * 0.5f);
    }

    /// <summary>
    /// API entry point when the API provides right, left and total separately.
    /// </summary>
    public void SetPowerValues(float right, float left, float total)
    {
        // Real API data should take control once explicitly received.
        useTestValues = false;

        ApplyValues(right, left, total);
    }

    public void SetRightPowerFromApi(float right)
    {
        useTestValues = false;
        ApplyValues(right, leftPower, (right + leftPower) * 0.5f);
    }

    public void SetLeftPowerFromApi(float left)
    {
        useTestValues = false;
        ApplyValues(rightPower, left, (rightPower + left) * 0.5f);
    }

    public void SetTotalPowerFromApi(float total)
    {
        useTestValues = false;
        ApplyValues(rightPower, leftPower, total);
    }

    public void SetMaximumValue(float maxValue)
    {
        maximumValue = Mathf.Max(1f, maxValue);

        ConfigureBar(rightPowerBar);
        ConfigureBar(leftPowerBar);
        ConfigureBar(totalPowerBar);

        Refresh();
    }

    private void ApplyTestValues()
    {
        ApplyValues(testRightPower, testLeftPower, testTotalPower);
    }

    private void ApplyValues(float right, float left, float total)
    {
        rightPower = Mathf.Clamp(right, 0f, maximumValue);
        leftPower = Mathf.Clamp(left, 0f, maximumValue);
        totalPower = Mathf.Clamp(total, 0f, maximumValue);

        Refresh();
    }

    private void Refresh()
    {
        if (rightPowerText != null)
            rightPowerText.text = $"{rightPower:0}{unit}";

        if (leftPowerText != null)
            leftPowerText.text = $"{leftPower:0}{unit}";

        if (totalPowerText != null)
            totalPowerText.text = $"{totalPower:0}{unit}";

        if (rightPowerBar != null)
            rightPowerBar.value = rightPower;

        if (leftPowerBar != null)
            leftPowerBar.value = leftPower;

        if (totalPowerBar != null)
            totalPowerBar.value = totalPower;
    }

    private void ConfigureBar(Slider bar)
    {
        if (bar == null)
            return;

        bar.minValue = 0f;
        bar.maxValue = maximumValue;
        bar.interactable = false;
    }

    [ContextMenu("Enable Test 72 / 68 / 70")]
    private void EnablePreviewValues()
    {
        useTestValues = true;
        testRightPower = 72f;
        testLeftPower = 68f;
        testTotalPower = 70f;
        ApplyTestValues();
    }

    [ContextMenu("Disable Test Values")]
    private void DisablePreviewValues()
    {
        useTestValues = false;
    }
}