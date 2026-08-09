#if UNITY_EDITOR
using StairGame.Api.Unity;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class StairGameApiInstaller
{
    [MenuItem("Tools/Stair Game/Install API Bridge")]
    public static void InstallApiBridge()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Stair Game API", "Open the StepGame scene first.", "OK");
            return;
        }

        StairClimbControllerV2 controller = Object.FindFirstObjectByType<StairClimbControllerV2>();
        StairPathV2 path = Object.FindFirstObjectByType<StairPathV2>();

        if (controller == null || path == null)
        {
            EditorUtility.DisplayDialog(
                "Stair Game API",
                "StairClimbControllerV2 or StairPathV2 is missing from the active scene.",
                "OK"
            );
            return;
        }

        StairGamePowerUI powerUI = EnsurePowerUI(scene);

        StairGameApiBridge bridge = Object.FindFirstObjectByType<StairGameApiBridge>();
        StairMovementEvaluator evaluator = Object.FindFirstObjectByType<StairMovementEvaluator>();

        if (bridge == null)
        {
            GameObject apiObject = FindSceneObjectByTrimmedName(scene, "StairGame_API");
            if (apiObject == null)
            {
                apiObject = new GameObject("StairGame_API");
                Undo.RegisterCreatedObjectUndo(apiObject, "Create StairGame API");
            }

            evaluator = apiObject.GetComponent<StairMovementEvaluator>();
            if (evaluator == null)
            {
                evaluator = Undo.AddComponent<StairMovementEvaluator>(apiObject);
            }

            bridge = apiObject.GetComponent<StairGameApiBridge>();
            if (bridge == null)
            {
                bridge = Undo.AddComponent<StairGameApiBridge>(apiObject);
            }
        }
        else if (evaluator == null)
        {
            evaluator = bridge.GetComponent<StairMovementEvaluator>();
            if (evaluator == null)
            {
                evaluator = Undo.AddComponent<StairMovementEvaluator>(bridge.gameObject);
            }
        }

        SerializedObject bridgeSerialized = new SerializedObject(bridge);
        SetObjectReference(bridgeSerialized, "stairController", controller);
        SetObjectReference(bridgeSerialized, "stairPath", path);
        SetObjectReference(bridgeSerialized, "powerUI", powerUI);
        SetObjectReference(bridgeSerialized, "movementEvaluator", evaluator);
        bridgeSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(bridge);
        EditorUtility.SetDirty(evaluator);
        if (powerUI != null)
        {
            EditorUtility.SetDirty(powerUI);
        }

        EditorSceneManager.MarkSceneDirty(scene);

        Selection.activeGameObject = bridge.gameObject;
        EditorGUIUtility.PingObject(bridge.gameObject);

        EditorUtility.DisplayDialog(
            "Stair Game API",
            powerUI != null
                ? "API Bridge, Movement Evaluator and Power UI are installed and wired."
                : "API Bridge and Movement Evaluator are installed. Power UI could not be auto-wired; add StairGamePowerUI manually to MusclePowerPanel.",
            "OK"
        );
    }

    private static StairGamePowerUI EnsurePowerUI(Scene scene)
    {
        StairGamePowerUI existing = Object.FindFirstObjectByType<StairGamePowerUI>();
        if (existing != null)
        {
            return existing;
        }

        GameObject panel = FindSceneObjectByTrimmedName(scene, "MusclePowerPanel");
        if (panel == null)
        {
            return null;
        }

        StairGamePowerUI powerUI = Undo.AddComponent<StairGamePowerUI>(panel);

        GameObject rightRow = FindSceneObjectByTrimmedName(scene, "RightPowerRow");
        GameObject leftRow = FindSceneObjectByTrimmedName(scene, "LeftPowerRow");
        GameObject totalRow = FindSceneObjectByTrimmedName(scene, "TotalPowerRow");

        SerializedObject serialized = new SerializedObject(powerUI);
        SetObjectReference(serialized, "rightPowerText", FindChildComponentByName<TMP_Text>(rightRow, "Total value"));
        SetObjectReference(serialized, "leftPowerText", FindChildComponentByName<TMP_Text>(leftRow, "Total value"));
        SetObjectReference(serialized, "totalPowerText", FindChildComponentByName<TMP_Text>(totalRow, "Total value"));
        SetObjectReference(serialized, "rightPowerBar", FindChildComponentByName<Slider>(rightRow, "Slider"));
        SetObjectReference(serialized, "leftPowerBar", FindChildComponentByName<Slider>(leftRow, "Slider"));
        SetObjectReference(serialized, "totalPowerBar", FindChildComponentByName<Slider>(totalRow, "Slider"));
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return powerUI;
    }

    private static void SetObjectReference(
        SerializedObject serializedObject,
        string propertyName,
        Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.objectReferenceValue = value;
        }
    }

    private static T FindChildComponentByName<T>(GameObject parent, string childName)
        where T : Component
    {
        if (parent == null)
        {
            return null;
        }

        T[] components = parent.GetComponentsInChildren<T>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] != null &&
                components[i].gameObject.name.Trim() == childName.Trim())
            {
                return components[i];
            }
        }

        return null;
    }

    private static GameObject FindSceneObjectByTrimmedName(Scene scene, string objectName)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject found = FindRecursive(roots[i].transform, objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static GameObject FindRecursive(Transform root, string objectName)
    {
        if (root.gameObject.name.Trim() == objectName.Trim())
        {
            return root.gameObject;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            GameObject found = FindRecursive(root.GetChild(i), objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
#endif
