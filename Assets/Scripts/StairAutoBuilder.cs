using UnityEngine;

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
            Debug.LogError("Source Step تعیین نشده است.");
            return;
        }

        ClearGeneratedSteps();
        ConfigureTopCollider(sourceStep);

        Vector3 sourcePosition = sourceStep.localPosition;
        Vector3 sourceScale = sourceStep.localScale;
        Vector3 direction = localDirection.normalized;

        for (int i = 1; i <= additionalSteps; i++)
        {
            GameObject clone = Instantiate(sourceStep.gameObject, transform);
            clone.name = $"Step_{i:00}";

            Transform step = clone.transform;

            /*
             * کف تمام بدنه‌ها در همان ارتفاع اولین پله باقی می‌ماند.
             * فقط سطح بالایی هر پله به اندازه Step Rise بالا می‌رود.
             */
            float newBodyHeight = sourceScale.y + (i * stepRise);

            step.localRotation = sourceStep.localRotation;

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
                clone.AddComponent<GeneratedStepMarker>();
        }

        Debug.Log($"{additionalSteps} پله جدید ساخته شد.");
    }

    private void ConfigureTopCollider(Transform step)
    {
        BoxCollider boxCollider = step.GetComponent<BoxCollider>();

        if (boxCollider == null)
            boxCollider = step.gameObject.AddComponent<BoxCollider>();

        /*
         * چون Scale Y بدنه پله‌ها متفاوت است،
         * اندازه محلی Collider معکوس Scale تنظیم می‌شود
         * تا ضخامت واقعی Collider همیشه ثابت بماند.
         */
        float bodyHeight = Mathf.Abs(step.localScale.y);
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
    }

    [ContextMenu("Clear Generated Steps")]
    public void ClearGeneratedSteps()
    {
        GeneratedStepMarker[] generatedSteps =
            GetComponentsInChildren<GeneratedStepMarker>(true);

        foreach (GeneratedStepMarker generatedStep in generatedSteps)
        {
            if (Application.isPlaying)
                Destroy(generatedStep.gameObject);
            else
                DestroyImmediate(generatedStep.gameObject);
        }
    }
}

public class GeneratedStepMarker : MonoBehaviour
{
}