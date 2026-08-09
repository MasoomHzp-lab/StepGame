#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class StairGamePowerKeyboardInstaller
{
    [MenuItem("Tools/Stair Game/Install Keyboard Muscle Strength Test")]
    public static void Install()
    {
        GameObject panel = FindSceneObject("MusclePowerPanel");
        if (panel == null)
        {
            Debug.LogError("MusclePowerPanel was not found in the active scene.");
            return;
        }

        StairGamePowerUI powerUi = panel.GetComponent<StairGamePowerUI>();
        if (powerUi == null)
            powerUi = Undo.AddComponent<StairGamePowerUI>(panel);

        SerializedObject serialized = new SerializedObject(powerUi);

        Transform rightRow = FindDescendant(panel.transform, "RightPowerRow");
        Transform leftRow = FindDescendant(panel.transform, "LeftPowerRow");
        Transform totalRow = FindDescendant(panel.transform, "TotalPowerRow");

        AssignObject(serialized, "rightPowerText", FindValueText(rightRow));
        AssignObject(serialized, "leftPowerText", FindValueText(leftRow));
        AssignObject(serialized, "totalPowerText", FindValueText(totalRow));

        AssignObject(serialized, "rightPowerBar", rightRow != null ? rightRow.GetComponentInChildren<Slider>(true) : null);
        AssignObject(serialized, "leftPowerBar", leftRow != null ? leftRow.GetComponentInChildren<Slider>(true) : null);
        AssignObject(serialized, "totalPowerBar", totalRow != null ? totalRow.GetComponentInChildren<Slider>(true) : null);

        StairClimbControllerV2 controller = Object.FindObjectOfType<StairClimbControllerV2>();
        AssignObject(serialized, "keyboardTestController", controller);

        SetBool(serialized, "enableKeyboardPowerTest", true);
        SetBool(serialized, "useTestValues", false);
        SetBool(serialized, "autoResolveUiReferences", true);

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(powerUi);

        if (SceneManager.GetActiveScene().IsValid())
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log(
            "Keyboard Muscle Strength Test installed. Enter Play Mode: R updates Right strength, L updates Left strength. Values are simulated test data only.",
            panel
        );

        Selection.activeGameObject = panel;
    }

    private static void AssignObject(SerializedObject serialized, string propertyName, Object value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static void SetBool(SerializedObject serialized, string propertyName, bool value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
            property.boolValue = value;
    }

    private static GameObject FindSceneObject(string exactName)
    {
        GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject go in all)
        {
            if (go == null || EditorUtility.IsPersistent(go))
                continue;

            if (!go.scene.IsValid())
                continue;

            if (go.name == exactName)
                return go;
        }

        return null;
    }

    private static Transform FindDescendant(Transform root, string trimmedName)
    {
        if (root == null)
            return null;

        foreach (Transform child in root)
        {
            if (string.Equals(child.name.Trim(), trimmedName, System.StringComparison.OrdinalIgnoreCase))
                return child;

            Transform nested = FindDescendant(child, trimmedName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static TMP_Text FindValueText(Transform row)
    {
        if (row == null)
            return null;

        TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text.gameObject.name.IndexOf("value", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return text;
        }

        foreach (TMP_Text text in texts)
        {
            if (!string.Equals(text.gameObject.name.Trim(), "Text", System.StringComparison.OrdinalIgnoreCase))
                return text;
        }

        return texts.Length > 0 ? texts[0] : null;
    }
}
#endif
