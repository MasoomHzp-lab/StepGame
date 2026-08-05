using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Builds an ordered stair path from BoxCollider children.
/// No world-space coordinates are stored in this component.
/// </summary>
public sealed class StairPathV2 : MonoBehaviour
{
    [Header("Path Reference")]
    [SerializeField] private Transform stairsRoot;

    [Tooltip("Direction of climbing in the local space of Stairs Root.")]
    [SerializeField] private Vector3 climbLocalDirection = Vector3.right;

    [Header("Fallback")]
    [Tooltip("Used only when fewer than two steps are available.")]
    [SerializeField, Min(0.001f)] private float fallbackStepRise = 0.20f;

    [Header("Step Filtering")]
    [Tooltip("Only colliders whose GameObject name starts with this value are treated as steps.")]
    [SerializeField] private string stepNamePrefix = "Step_";

    [Tooltip("When duplicate generated steps exist, keep only one collider for each numeric step name.")]
    [SerializeField] private bool removeDuplicateStepNames = true;

    [Header("Runtime - Read Only")]
    [SerializeField] private int stepCount;
    [SerializeField] private float inferredStepRise;

    private readonly List<BoxCollider> steps = new List<BoxCollider>();

    public int StepCount => steps.Count;

    public Vector3 ClimbWorldDirection
    {
        get
        {
            Transform root = stairsRoot != null ? stairsRoot : transform;
            Vector3 localDirection = climbLocalDirection.sqrMagnitude > 0.0001f
                ? climbLocalDirection.normalized
                : Vector3.right;

            return root.TransformDirection(localDirection).normalized;
        }
    }

    public float InferredStepRise => inferredStepRise > 0f
        ? inferredStepRise
        : fallbackStepRise;

    private void Awake()
    {
        if (stairsRoot == null)
        {
            stairsRoot = transform;
        }

        if (!RefreshSteps())
        {
            enabled = false;
        }
    }

    [ContextMenu("Refresh Steps")]
    public bool RefreshSteps()
    {
        steps.Clear();

        if (stairsRoot == null)
        {
            stairsRoot = transform;
        }

        if (climbLocalDirection.sqrMagnitude < 0.0001f)
        {
            Debug.LogError("Stair Path V2: Climb Local Direction cannot be zero.", this);
            return false;
        }

        BoxCollider[] found = stairsRoot.GetComponentsInChildren<BoxCollider>(true);
        Dictionary<int, BoxCollider> numberedSteps = new Dictionary<int, BoxCollider>();

        foreach (BoxCollider collider in found)
        {
            if (collider == null || collider.transform == stairsRoot)
            {
                continue;
            }

            if (!TryGetStepNumber(collider.name, out int stepNumber))
            {
                continue;
            }

            if (!removeDuplicateStepNames)
            {
                steps.Add(collider);
                continue;
            }

            // Rebuilding the stairs more than once left duplicate Step_XX objects
            // in the supplied scene. The latest/correct set has the highest tread
            // for a given number, so retain that collider deterministically.
            if (!numberedSteps.TryGetValue(stepNumber, out BoxCollider existing) ||
                GetStepTopCenter(collider).y > GetStepTopCenter(existing).y)
            {
                numberedSteps[stepNumber] = collider;
            }
        }

        if (removeDuplicateStepNames)
        {
            steps.AddRange(numberedSteps.Values);
        }

        Vector3 climbDirection = ClimbWorldDirection;
        steps.Sort((a, b) =>
        {
            float aDistance = Vector3.Dot(GetStepTopCenter(a), climbDirection);
            float bDistance = Vector3.Dot(GetStepTopCenter(b), climbDirection);
            return aDistance.CompareTo(bDistance);
        });

        stepCount = steps.Count;
        inferredStepRise = CalculateAverageRise();

        if (steps.Count == 0)
        {
            Debug.LogError("Stair Path V2: No child BoxColliders were found under Stairs Root.", this);
            return false;
        }

        Debug.Log(
            $"Stair Path V2 ready | Steps: {steps.Count} | Average rise: {InferredStepRise:F3}",
            this
        );

        return true;
    }

    private bool TryGetStepNumber(string objectName, out int stepNumber)
    {
        stepNumber = -1;

        if (string.IsNullOrWhiteSpace(stepNamePrefix) ||
            !objectName.StartsWith(stepNamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Match match = Regex.Match(objectName, @"(\d+)$");
        return match.Success && int.TryParse(match.Groups[1].Value, out stepNumber);
    }

    public bool TryGetStep(int index, out BoxCollider step)
    {
        step = null;

        if (index < 0 || index >= steps.Count)
        {
            return false;
        }

        step = steps[index];
        return step != null;
    }

    public bool TryGetStepTopCenter(int index, out Vector3 topCenter)
    {
        topCenter = Vector3.zero;

        if (!TryGetStep(index, out BoxCollider step))
        {
            return false;
        }

        topCenter = GetStepTopCenter(step);
        return true;
    }

    public float GetEstimatedStartSurfaceY()
    {
        if (!TryGetStepTopCenter(0, out Vector3 firstTop))
        {
            return transform.position.y;
        }

        return firstTop.y - InferredStepRise;
    }

    private static Vector3 GetStepTopCenter(BoxCollider step)
    {
        Vector3 localTop = step.center + Vector3.up * (step.size.y * 0.5f);
        return step.transform.TransformPoint(localTop);
    }

    private float CalculateAverageRise()
    {
        if (steps.Count < 2)
        {
            return fallbackStepRise;
        }

        float total = 0f;
        int validCount = 0;

        for (int i = 1; i < steps.Count; i++)
        {
            float previousY = GetStepTopCenter(steps[i - 1]).y;
            float currentY = GetStepTopCenter(steps[i]).y;
            float difference = currentY - previousY;

            if (difference > 0.0001f)
            {
                total += difference;
                validCount++;
            }
        }

        return validCount > 0 ? total / validCount : fallbackStepRise;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        fallbackStepRise = Mathf.Max(0.001f, fallbackStepRise);
    }
#endif
}
