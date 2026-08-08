using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays right / left / total muscle power.
/// The future API only needs to call SetPowerValues(...).
/// Default display is percentage to match the current UI design.
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

    private float rightPower;
    private float leftPower;
    private float totalPower;

    private void Start()
    {
        ConfigureBar(rightPowerBar);
        ConfigureBar(leftPowerBar);
        ConfigureBar(totalPowerBar);

        SetPowerValues(0f, 0f);
    }

    /// <summary>
    /// Use when total power is the average of right and left.
    /// </summary>
    public void SetPowerValues(float right, float left)
    {
        SetPowerValues(right, left, (right + left) * 0.5f);
    }

    /// <summary>
    /// Use this overload if the API provides an independent total-power value.
    /// </summary>
    public void SetPowerValues(float right, float left, float total)
    {
        rightPower = Mathf.Clamp(right, 0f, maximumValue);
        leftPower = Mathf.Clamp(left, 0f, maximumValue);
        totalPower = Mathf.Clamp(total, 0f, maximumValue);

        Refresh();
    }

    public void SetMaximumValue(float maxValue)
    {
        maximumValue = Mathf.Max(1f, maxValue);

        ConfigureBar(rightPowerBar);
        ConfigureBar(leftPowerBar);
        ConfigureBar(totalPowerBar);

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

    [ContextMenu("Preview 72 / 68 / 70")]
    private void PreviewValues()
    {
        SetPowerValues(72f, 68f, 70f);
    }
}