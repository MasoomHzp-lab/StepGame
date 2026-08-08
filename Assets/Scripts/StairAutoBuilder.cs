using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class StairAutoBuilder : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private Transform sourceStep;

    [Header("Stair Settings")]
    [Min(1)]
    [SerializeField] private int additionalSteps = 20;

    [Tooltip("اختلاف ارتفاع سطح هر پله با پله قبلی")]
    [Min(0.01f)]
    [SerializeField] private float stepRise = 0.20f;

    [Tooltip("فاصله افقی مرکز دو پله متوالی")]
    [Min(0.01f)]
    [SerializeField] private float horizontalStep = 0.60f;

    [Tooltip("جهت حرکت به طرف دیوار")]
    [SerializeField] private Vector3 localDirection = Vector3.left;

    [Header("Collider")]
    [Tooltip("ضخامت واقعی Collider روی سطح پله")]
    [Min(0.01f)]
    [SerializeField] private float colliderThickness = 0.08f;

    [ContextMenu("Generate Steps")]
    public void GenerateSteps()
    {
        if (sourceStep == null)
        {
            Debug.LogError("Source Step تعیین نشده است.", this);
            return;
        }

        if (sourceStep.IsChildOf(transform) &&
            sourceStep.GetComponent<GeneratedStepMarker>() != null)
        {
            Debug.LogError(
                "Source Step نباید یکی از پله‌های Generated زیر آبجکت stairs باشد. " +
                "یک Prefab یا آبجکت مرجع مستقل انتخاب کن.",
                this
            );
            return;
        }

        ClearGeneratedSteps();

        // Source Step فقط الگو است. Collider خود Prefab/مرجع را تغییر نمی‌دهیم.
        Vector3 sourcePosition = sourceStep.localPosition;
        Vector3 sourceScale = sourceStep.localScale;
        Quaternion sourceRotation = sourceStep.localRotation;
        Vector3 direction = GetSafeLocalDirection();

        for (int i = 1; i <= additionalSteps; i++)
        {
            GameObject clone = Instantiate(sourceStep.gameObject, transform);
            clone.name = $"Step_{i:00}";

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.RegisterCreatedObjectUndo(clone, "Generate Stair Step");
            }
#endif

            Transform step = clone.transform;

            // کف تمام بدنه‌ها در ارتفاع مبنا باقی می‌ماند؛ سطح هر پله بالا می‌رود.
            float newBodyHeight = sourceScale.y + (i * stepRise);

            step.localRotation = sourceRotation;
            step.localScale = new Vector3(
                sourceScale.x,
                newBodyHeight,
                sourceScale.z
            );

            step.localPosition =
                sourcePosition +
                direction * (horizontalStep * i) +
                Vector3.up * ((stepRise * i) / 2f);

            ConfigureTopCollider(step);

            if (clone.GetComponent<GeneratedStepMarker>() == null)
            {
                clone.AddComponent<GeneratedStepMarker>();
            }
        }

        RefreshStairPath();
        MarkSceneDirty();

        Debug.Log(
            $"Stair Auto Builder: {additionalSteps} پله تمیز ساخته شد | " +
            $"Rise: {stepRise:F3} | Horizontal: {horizontalStep:F3}",
            this
        );
    }

    /// <summary>
    /// Scene فعلی را بدون جابه‌جا کردن مجموعه صحیح تعمیر می‌کند:
    /// برای هر نام Step_XX فقط پله‌ای را نگه می‌دارد که سطح بالاتری دارد.
    /// این دقیقاً با انتخاب StairPathV2 هماهنگ است.
    /// </summary>
    [ContextMenu("Repair Existing Staircase (Remove Duplicates)")]
    public void RepairExistingStaircase()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            Debug.LogError("Repair را خارج از Play Mode اجرا کن.", this);
            return;
        }

        BoxCollider[] allColliders = GetComponentsInChildren<BoxCollider>(true);
        Dictionary<int, BoxCollider> keepers = new Dictionary<int, BoxCollider>();
        HashSet<GameObject> duplicates = new HashSet<GameObject>();

        foreach (BoxCollider collider in allColliders)
        {
            if (collider == null || collider.transform == transform)
            {
                continue;
            }

            if (!TryGetStepNumber(collider.name, out int stepNumber))
            {
                continue;
            }

            if (!keepers.TryGetValue(stepNumber, out BoxCollider existing))
            {
                keepers.Add(stepNumber, collider);
                continue;
            }

            // مجموعه جدید و صحیح برای هر شماره، سطح بالاتری دارد.
            if (GetStepTopWorldY(collider) > GetStepTopWorldY(existing))
            {
                duplicates.Add(existing.gameObject);
                keepers[stepNumber] = collider;
            }
            else
            {
                duplicates.Add(collider.gameObject);
            }
        }

        if (keepers.Count == 0)
        {
            Debug.LogError("هیچ پله‌ای با نام Step_XX زیر این آبجکت پیدا نشد.", this);
            return;
        }

        Undo.RecordObject(this, "Repair Staircase Settings");

        // تنظیمات Builder را از هندسه مجموعه‌ای که نگه می‌داریم استخراج می‌کنیم.
        List<KeyValuePair<int, BoxCollider>> ordered =
            new List<KeyValuePair<int, BoxCollider>>(keepers);
        ordered.Sort((a, b) => a.Key.CompareTo(b.Key));

        float detectedHorizontalStep = DetectHorizontalStep(ordered);
        float detectedStepRise = DetectStepRise(ordered);

        if (detectedHorizontalStep > 0.001f)
        {
            horizontalStep = detectedHorizontalStep;
        }

        if (detectedStepRise > 0.001f)
        {
            stepRise = detectedStepRise;
        }

        additionalSteps = ordered.Count;

        foreach (KeyValuePair<int, BoxCollider> pair in ordered)
        {
            GameObject keeper = pair.Value.gameObject;
            keeper.name = $"Step_{pair.Key:00}";

            if (keeper.GetComponent<GeneratedStepMarker>() == null)
            {
                Undo.AddComponent<GeneratedStepMarker>(keeper);
            }
        }

        int removedCount = 0;
        foreach (GameObject duplicate in duplicates)
        {
            if (duplicate == null)
            {
                continue;
            }

            Undo.DestroyObjectImmediate(duplicate);
            removedCount++;
        }

        RefreshStairPath();
        EditorUtility.SetDirty(this);
        MarkSceneDirty();

        Debug.Log(
            $"Stair Repair complete | Kept: {ordered.Count} | Removed duplicates: {removedCount} | " +
            $"Detected Rise: {stepRise:F3} | Detected Horizontal: {horizontalStep:F3}",
            this
        );
#else
        Debug.LogWarning("Repair Existing Staircase فقط داخل Unity Editor قابل اجراست.", this);
#endif
    }

    private void ConfigureTopCollider(Transform step)
    {
        BoxCollider boxCollider = step.GetComponent<BoxCollider>();

        if (boxCollider == null)
        {
            boxCollider = step.gameObject.AddComponent<BoxCollider>();
        }

        float bodyHeight = Mathf.Max(0.0001f, Mathf.Abs(step.localScale.y));
        float localColliderHeight = colliderThickness / bodyHeight;

        boxCollider.size = new Vector3(
            1f,
            localColliderHeight,
            1f
        );

        boxCollider.center = new Vector3(
            0f,
            0.5f - (localColliderHeight / 2f),
            0f
        );

        boxCollider.isTrigger = false;
        boxCollider.enabled = true;
    }

    [ContextMenu("Clear Generated Steps")]
    public void ClearGeneratedSteps()
    {
        GeneratedStepMarker[] generatedSteps =
            GetComponentsInChildren<GeneratedStepMarker>(true);

        int removedCount = 0;

        foreach (GeneratedStepMarker generatedStep in generatedSteps)
        {
            if (generatedStep == null || generatedStep.transform == sourceStep)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(generatedStep.gameObject);
            }
            else
            {
#if UNITY_EDITOR
                Undo.DestroyObjectImmediate(generatedStep.gameObject);
#else
                DestroyImmediate(generatedStep.gameObject);
#endif
            }

            removedCount++;
        }

        MarkSceneDirty();
        Debug.Log($"Stair Auto Builder: {removedCount} پله Generated حذف شد.", this);
    }

    private Vector3 GetSafeLocalDirection()
    {
        if (localDirection.sqrMagnitude < 0.0001f)
        {
            localDirection = Vector3.right;
        }

        return localDirection.normalized;
    }

    private static bool TryGetStepNumber(string objectName, out int stepNumber)
    {
        stepNumber = -1;

        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        Match match = Regex.Match(
            objectName,
            @"^Step_(\d+)$",
            RegexOptions.IgnoreCase
        );

        return match.Success && int.TryParse(match.Groups[1].Value, out stepNumber);
    }

    private static float GetStepTopWorldY(BoxCollider collider)
    {
        Vector3 localTop =
            collider.center + Vector3.up * (collider.size.y * 0.5f);

        return collider.transform.TransformPoint(localTop).y;
    }

    private float DetectHorizontalStep(
        List<KeyValuePair<int, BoxCollider>> ordered
    )
    {
        List<float> samples = new List<float>();
        Vector3 direction = GetSafeLocalDirection();

        for (int i = 1; i < ordered.Count; i++)
        {
            int numberDifference = ordered[i].Key - ordered[i - 1].Key;
            if (numberDifference <= 0)
            {
                continue;
            }

            Vector3 previousPosition =
                ordered[i - 1].Value.transform.localPosition;
            Vector3 currentPosition =
                ordered[i].Value.transform.localPosition;

            float distance = Vector3.Dot(
                currentPosition - previousPosition,
                direction
            ) / numberDifference;

            if (distance > 0.001f)
            {
                samples.Add(distance);
            }
        }

        return Median(samples);
    }

    private float DetectStepRise(
        List<KeyValuePair<int, BoxCollider>> ordered
    )
    {
        List<float> samples = new List<float>();

        for (int i = 1; i < ordered.Count; i++)
        {
            int numberDifference = ordered[i].Key - ordered[i - 1].Key;
            if (numberDifference <= 0)
            {
                continue;
            }

            float previousTop = GetStepTopWorldY(ordered[i - 1].Value);
            float currentTop = GetStepTopWorldY(ordered[i].Value);
            float rise = (currentTop - previousTop) / numberDifference;

            if (rise > 0.001f)
            {
                samples.Add(rise);
            }
        }

        return Median(samples);
    }

    private static float Median(List<float> values)
    {
        if (values == null || values.Count == 0)
        {
            return 0f;
        }

        values.Sort();
        int middle = values.Count / 2;

        if ((values.Count & 1) == 1)
        {
            return values[middle];
        }

        return (values[middle - 1] + values[middle]) * 0.5f;
    }

    private void RefreshStairPath()
    {
        StairPathV2 stairPath = GetComponent<StairPathV2>();
        if (stairPath != null)
        {
            stairPath.RefreshSteps();
        }
    }

    private void MarkSceneDirty()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        additionalSteps = Mathf.Max(1, additionalSteps);
        stepRise = Mathf.Max(0.01f, stepRise);
        horizontalStep = Mathf.Max(0.01f, horizontalStep);
        colliderThickness = Mathf.Max(0.01f, colliderThickness);

        if (localDirection.sqrMagnitude < 0.0001f)
        {
            localDirection = Vector3.right;
        }
    }
#endif
}

public class GeneratedStepMarker : MonoBehaviour
{
}