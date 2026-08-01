using UnityEngine;
using UnityEngine.Rendering;

public class StepTargetVisualizer : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private StairGameController gameController;

    [SerializeField]
    private Animator animator;

    [SerializeField]
    private Camera targetCamera;

    [Header("Target Settings")]
    [Tooltip("Extra height above the step edge.")]
    [SerializeField, Min(0f)]
    private float stepClearance = 0.1f;

    [Tooltip("Moves the landing point deeper onto the step.")]
    [SerializeField]
    private float landingDepthOffset = 0f;

    [Header("Visual Settings")]
    [SerializeField]
    private bool showGuide = true;

    [SerializeField, Min(0.001f)]
    private float lineWidth = 0.05f;

    [SerializeField, Min(0.001f)]
    private float markerSize = 0.11f;

    [SerializeField, Min(0f)]
    private float cameraOffset = 0.16f;

    [SerializeField]
    private Color liftColor = Color.green;

    [SerializeField]
    private Color forwardColor = Color.yellow;

    [SerializeField]
    private Color targetColor = Color.white;

    [Header("Runtime Measurements - Read Only")]
    [SerializeField]
    private float requiredLiftDistance;

    [SerializeField]
    private float requiredForwardDistance;

    private Transform rightAnkle;
    private Transform leftAnkle;

    private LineRenderer liftLine;
    private LineRenderer forwardLine;

    private GameObject startMarker;
    private GameObject liftMarker;
    private GameObject targetMarker;

    private Material liftMaterial;
    private Material forwardMaterial;
    private Material targetMaterial;

    private int lastTargetInstanceId = -1;
    private StairGameController.FootSide lastExpectedFoot;

    private bool initialized;

    private void Awake()
    {
        if (gameController == null)
        {
            gameController =
                GetComponentInParent<StairGameController>();
        }

        if (animator == null)
        {
            animator = GetComponentInParent<Animator>();
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (gameController == null)
        {
            Debug.LogError(
                "Step Target Visualizer could not find StairGameController.",
                this
            );

            enabled = false;
            return;
        }

        if (animator == null)
        {
            Debug.LogError(
                "Step Target Visualizer could not find an Animator.",
                this
            );

            enabled = false;
            return;
        }

        if (!animator.isHuman)
        {
            Debug.LogError(
                "The assigned Animator must use a Humanoid avatar.",
                this
            );

            enabled = false;
            return;
        }

        rightAnkle = animator.GetBoneTransform(
            HumanBodyBones.RightFoot
        );

        leftAnkle = animator.GetBoneTransform(
            HumanBodyBones.LeftFoot
        );

        if (rightAnkle == null || leftAnkle == null)
        {
            Debug.LogError(
                "The right or left foot bone could not be found.",
                this
            );

            enabled = false;
            return;
        }

        CreateVisualObjects();

        initialized = true;

        Debug.Log(
            "Step target visualizer initialized successfully.",
            this
        );
    }

    private void LateUpdate()
    {
        if (!initialized)
        {
            return;
        }

        if (!showGuide ||
            !gameController.SessionStarted ||
            !gameController.TryGetCurrentTargetStep(
                out BoxCollider targetStep
            ))
        {
            SetVisibility(false);
            return;
        }

        SetVisibility(true);

        StairGameController.FootSide expectedFoot =
            gameController.ExpectedFoot;

        Transform activeAnkle =
            expectedFoot ==
            StairGameController.FootSide.Right
                ? rightAnkle
                : leftAnkle;

        Vector3 visualOffset = Vector3.zero;

        if (targetCamera != null)
        {
            visualOffset =
                -targetCamera.transform.forward *
                cameraOffset;
        }

        Vector3 startPosition =
            activeAnkle.position + visualOffset;

        Vector3 climbDirection =
            gameController.ClimbWorldDirection;

        Vector3 targetPosition =
            GetStepTopPosition(targetStep);

        targetPosition +=
            climbDirection * landingDepthOffset;

        targetPosition += visualOffset;

        float liftHeight =
            Mathf.Max(
                startPosition.y,
                targetPosition.y
            ) + stepClearance;

        Vector3 liftPosition =
            new Vector3(
                startPosition.x,
                liftHeight,
                startPosition.z
            );

        liftLine.SetPosition(
            0,
            startPosition
        );

        liftLine.SetPosition(
            1,
            liftPosition
        );

        forwardLine.SetPosition(
            0,
            liftPosition
        );

        forwardLine.SetPosition(
            1,
            targetPosition
        );

        startMarker.transform.position =
            startPosition;

        liftMarker.transform.position =
            liftPosition;

        targetMarker.transform.position =
            targetPosition;

        requiredLiftDistance =
            Mathf.Max(
                0f,
                liftPosition.y -
                startPosition.y
            );

        requiredForwardDistance =
            Mathf.Max(
                0f,
                Vector3.Dot(
                    targetPosition -
                    startPosition,
                    climbDirection
                )
            );

        LogGuideChange(
            targetStep,
            expectedFoot
        );
    }

    private Vector3 GetStepTopPosition(
        BoxCollider step
    )
    {
        Bounds bounds = step.bounds;

        return new Vector3(
            bounds.center.x,
            bounds.max.y,
            bounds.center.z
        );
    }

    private void CreateVisualObjects()
    {
        liftMaterial =
            CreateMaterial(
                "Lift_Guide_Material",
                liftColor
            );

        forwardMaterial =
            CreateMaterial(
                "Forward_Guide_Material",
                forwardColor
            );

        targetMaterial =
            CreateMaterial(
                "Target_Guide_Material",
                targetColor
            );

        liftLine = CreateLineRenderer(
            "RequiredLiftLine",
            liftMaterial,
            liftColor
        );

        forwardLine = CreateLineRenderer(
            "RequiredForwardLine",
            forwardMaterial,
            forwardColor
        );

        startMarker = CreateMarker(
            "MovementStartMarker",
            liftMaterial
        );

        liftMarker = CreateMarker(
            "MaximumLiftMarker",
            liftMaterial
        );

        targetMarker = CreateMarker(
            "StepTargetMarker",
            targetMaterial
        );
    }

    private LineRenderer CreateLineRenderer(
        string objectName,
        Material material,
        Color color
    )
    {
        GameObject lineObject =
            new GameObject(objectName);

        lineObject.transform.SetParent(
            transform,
            false
        );

        LineRenderer line =
            lineObject.AddComponent<LineRenderer>();

        line.useWorldSpace = true;
        line.positionCount = 2;

        line.startWidth = lineWidth;
        line.endWidth = lineWidth;

        line.numCapVertices = 12;
        line.numCornerVertices = 12;

        line.alignment = LineAlignment.View;

        line.shadowCastingMode =
            ShadowCastingMode.Off;

        line.receiveShadows = false;
        line.sortingOrder = 110;

        line.material = material;
        line.startColor = color;
        line.endColor = color;

        return line;
    }

    private GameObject CreateMarker(
        string markerName,
        Material material
    )
    {
        GameObject marker =
            GameObject.CreatePrimitive(
                PrimitiveType.Sphere
            );

        marker.name = markerName;

        marker.transform.SetParent(
            transform,
            true
        );

        marker.transform.localScale =
            Vector3.one * markerSize;

        Collider markerCollider =
            marker.GetComponent<Collider>();

        if (markerCollider != null)
        {
            Destroy(markerCollider);
        }

        MeshRenderer markerRenderer =
            marker.GetComponent<MeshRenderer>();

        if (markerRenderer != null)
        {
            markerRenderer.sharedMaterial =
                material;

            markerRenderer.shadowCastingMode =
                ShadowCastingMode.Off;

            markerRenderer.receiveShadows = false;
            markerRenderer.sortingOrder = 110;
        }

        return marker;
    }

    private Material CreateMaterial(
        string materialName,
        Color color
    )
    {
        Shader shader = Shader.Find(
            "Universal Render Pipeline/Unlit"
        );

        if (shader == null)
        {
            shader = Shader.Find(
                "Sprites/Default"
            );
        }

        if (shader == null)
        {
            Debug.LogError(
                "A suitable shader for the step guide was not found.",
                this
            );

            return null;
        }

        Material material =
            new Material(shader);

        material.name = materialName;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor(
                "_BaseColor",
                color
            );
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor(
                "_Color",
                color
            );
        }

        material.color = color;

        return material;
    }

    private void SetVisibility(bool isVisible)
    {
        if (liftLine != null)
        {
            liftLine.enabled = isVisible;
        }

        if (forwardLine != null)
        {
            forwardLine.enabled = isVisible;
        }

        if (startMarker != null)
        {
            startMarker.SetActive(isVisible);
        }

        if (liftMarker != null)
        {
            liftMarker.SetActive(isVisible);
        }

        if (targetMarker != null)
        {
            targetMarker.SetActive(isVisible);
        }
    }

    private void LogGuideChange(
        BoxCollider targetStep,
        StairGameController.FootSide foot
    )
    {
        int currentTargetId =
            targetStep.GetInstanceID();

        if (currentTargetId ==
                lastTargetInstanceId &&
            foot == lastExpectedFoot)
        {
            return;
        }

        lastTargetInstanceId =
            currentTargetId;

        lastExpectedFoot = foot;

        Debug.Log(
            $"Movement guide updated | " +
            $"Foot: {foot.ToString().ToLower()} | " +
            $"Target: {targetStep.name} | " +
            $"Required lift: {requiredLiftDistance:F2} m | " +
            $"Required forward movement: {requiredForwardDistance:F2} m",
            this
        );
    }

    private void OnDestroy()
    {
        if (liftMaterial != null)
        {
            Destroy(liftMaterial);
        }

        if (forwardMaterial != null)
        {
            Destroy(forwardMaterial);
        }

        if (targetMaterial != null)
        {
            Destroy(targetMaterial);
        }
    }
}