using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays right / left / total muscle-strength values.
/// References are auto-wired from MusclePowerPanel so the UI keeps working
/// even if serialized references were lost while scripts were replaced.
/// </summary>
[DisallowMultipleComponent]
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

    [Header("Debug Preview")]
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

    private void Awake()
    {
        AutoWireReferences();
    }

    private void Start()
    {
        AutoWireReferences();
        ConfigureBar(rightPowerBar);
        ConfigureBar(leftPowerBar);
        ConfigureBar(totalPowerBar);

        if (useTestValues)
            ApplyTestValues();
        else
            ApplyValues(0f, 0f, 0f);
    }

    private void Update()
    {
        if (useTestValues)
            ApplyTestValues();
    }

    public void SetPowerValues(float right, float left)
    {
        SetPowerValues(right, left, (right + left) * 0.5f);
    }

    public void SetPowerValues(float right, float left, float total)
    {
        useTestValues = false;
        AutoWireReferences();
        ApplyValues(right, left, total);
    }

    public void SetRightPowerFromApi(float right)
    {
        useTestValues = false;
        AutoWireReferences();
        ApplyValues(right, leftPower, (right + leftPower) * 0.5f);
    }

    public void SetLeftPowerFromApi(float left)
    {
        useTestValues = false;
        AutoWireReferences();
        ApplyValues(rightPower, left, (rightPower + left) * 0.5f);
    }

    public void SetTotalPowerFromApi(float total)
    {
        useTestValues = false;
        AutoWireReferences();
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

    /// <summary>
    /// Reconnects the existing StepGame UI by hierarchy names:
    /// RightPowerRow / LeftPowerRow / TotalPowerRow and each row's "Total value" text.
    /// Safe to call repeatedly.
    /// </summary>
    [ContextMenu("Auto Wire Power UI References")]
    public void AutoWireReferences()
    {
        WireRow("RightPowerRow", ref rightPowerText, ref rightPowerBar);
        WireRow("LeftPowerRow", ref leftPowerText, ref leftPowerBar);
        WireRow("TotalPowerRow", ref totalPowerText, ref totalPowerBar);
    }

    public bool HasDisplayReferences()
    {
        return rightPowerText != null && leftPowerText != null && totalPowerText != null
            && rightPowerBar != null && leftPowerBar != null && totalPowerBar != null;
    }

    private void WireRow(string rowName, ref TMP_Text valueText, ref Slider slider)
    {
        Transform row = FindDescendantByTrimmedName(transform, rowName);
        if (row == null)
            return;

        if (slider == null)
            slider = row.GetComponentInChildren<Slider>(true);

        if (valueText == null)
        {
            TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);

            // The current StepGame scene names the numeric field "Total value"
            // in all three power rows.
            for (int i = 0; i < texts.Length; i++)
            {
                if (NormalizeName(texts[i].gameObject.name) == "total value")
                {
                    valueText = texts[i];
                    break;
                }
            }

            // Fallback: choose a text whose name contains "value".
            if (valueText == null)
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    if (NormalizeName(texts[i].gameObject.name).Contains("value"))
                    {
                        valueText = texts[i];
                        break;
                    }
                }
            }
        }
    }

    private static Transform FindDescendantByTrimmedName(Transform root, string wantedName)
    {
        string wanted = NormalizeName(wantedName);

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (NormalizeName(child.name) == wanted)
                return child;

            Transform nested = FindDescendantByTrimmedName(child, wantedName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static string NormalizeName(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : value.Trim().ToLowerInvariant();
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
        AutoWireReferences();
        ApplyTestValues();
    }

    [ContextMenu("Disable Test Values")]
    private void DisablePreviewValues()
    {
        useTestValues = false;
    }
}
