using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(LineRenderer))]
public class LegGuideVisualizer : MonoBehaviour
{
    public enum LegSide
    {
        Right,
        Left
    }

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Camera targetCamera;

    [Header("Leg Settings")]
    [SerializeField] private LegSide legSide = LegSide.Right;

    [Header("Visual Settings")]
    [SerializeField] private bool showGuide = true;

    [SerializeField, Min(0.001f)]
    private float lineWidth = 0.06f;

    [SerializeField, Min(0.001f)]
    private float markerSize = 0.12f;

    [Tooltip("Moves the guide slightly toward the camera so it is not hidden inside the character.")]
    [SerializeField, Min(0f)]
    private float cameraOffset = 0.12f;

    [SerializeField]
    private Color guideColor = Color.cyan;

    private Transform hipBone;
    private Transform kneeBone;
    private Transform ankleBone;

    private LineRenderer lineRenderer;

    private GameObject hipMarker;
    private GameObject kneeMarker;
    private GameObject ankleMarker;

    private Material runtimeMaterial;
    private bool initialized;

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        if (animator == null)
        {
            animator = GetComponentInParent<Animator>();
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (animator == null)
        {
            Debug.LogError(
                "Leg Guide Visualizer could not find an Animator.",
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

        if (!FindLegBones())
        {
            enabled = false;
            return;
        }

        ConfigureLineRenderer();
        CreateMarkers();

        initialized = true;

        Debug.Log(
            $"{legSide} leg guide initialized successfully.",
            this
        );
    }

    private void LateUpdate()
    {
        if (!initialized)
        {
            return;
        }

        SetGuideVisibility(showGuide);

        if (!showGuide)
        {
            return;
        }

        Vector3 visualOffset = Vector3.zero;

        if (targetCamera != null)
        {
            // Camera forward points into the scene.
            // Negative forward moves the guide toward the camera.
            visualOffset =
                -targetCamera.transform.forward * cameraOffset;
        }

        Vector3 hipPosition =
            hipBone.position + visualOffset;

        Vector3 kneePosition =
            kneeBone.position + visualOffset;

        Vector3 anklePosition =
            ankleBone.position + visualOffset;

        hipMarker.transform.position = hipPosition;
        kneeMarker.transform.position = kneePosition;
        ankleMarker.transform.position = anklePosition;

        lineRenderer.SetPosition(0, hipPosition);
        lineRenderer.SetPosition(1, kneePosition);
        lineRenderer.SetPosition(2, anklePosition);
    }

    private bool FindLegBones()
    {
        if (legSide == LegSide.Right)
        {
            hipBone = animator.GetBoneTransform(
                HumanBodyBones.RightUpperLeg
            );

            kneeBone = animator.GetBoneTransform(
                HumanBodyBones.RightLowerLeg
            );

            ankleBone = animator.GetBoneTransform(
                HumanBodyBones.RightFoot
            );
        }
        else
        {
            hipBone = animator.GetBoneTransform(
                HumanBodyBones.LeftUpperLeg
            );

            kneeBone = animator.GetBoneTransform(
                HumanBodyBones.LeftLowerLeg
            );

            ankleBone = animator.GetBoneTransform(
                HumanBodyBones.LeftFoot
            );
        }

        if (hipBone == null ||
            kneeBone == null ||
            ankleBone == null)
        {
            Debug.LogError(
                $"Could not find all required bones for the {legSide} leg.",
                this
            );

            return false;
        }

        return true;
    }

    private void ConfigureLineRenderer()
    {
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 3;

        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;

        lineRenderer.numCapVertices = 12;
        lineRenderer.numCornerVertices = 12;

        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.sortingOrder = 100;

        Shader shader = Shader.Find(
            "Universal Render Pipeline/Unlit"
        );

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            Debug.LogError(
                "A suitable shader for the leg guide was not found.",
                this
            );

            enabled = false;
            return;
        }

        runtimeMaterial = new Material(shader);

        runtimeMaterial.name =
            $"Runtime_{legSide}_LegGuide_Material";

        if (runtimeMaterial.HasProperty("_BaseColor"))
        {
            runtimeMaterial.SetColor(
                "_BaseColor",
                guideColor
            );
        }

        if (runtimeMaterial.HasProperty("_Color"))
        {
            runtimeMaterial.SetColor(
                "_Color",
                guideColor
            );
        }

        runtimeMaterial.color = guideColor;

        lineRenderer.material = runtimeMaterial;
        lineRenderer.startColor = guideColor;
        lineRenderer.endColor = guideColor;
    }

    private void CreateMarkers()
    {
        hipMarker = CreateMarker(
            $"{legSide}_Hip_Marker"
        );

        kneeMarker = CreateMarker(
            $"{legSide}_Knee_Marker"
        );

        ankleMarker = CreateMarker(
            $"{legSide}_Ankle_Marker"
        );
    }

    private GameObject CreateMarker(string markerName)
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
            markerRenderer.shadowCastingMode =
                ShadowCastingMode.Off;

            markerRenderer.receiveShadows = false;
            markerRenderer.sortingOrder = 100;

            if (runtimeMaterial != null)
            {
                markerRenderer.sharedMaterial =
                    runtimeMaterial;
            }
        }

        return marker;
    }

    private void SetGuideVisibility(bool isVisible)
    {
        lineRenderer.enabled = isVisible;

        if (hipMarker != null)
        {
            hipMarker.SetActive(isVisible);
        }

        if (kneeMarker != null)
        {
            kneeMarker.SetActive(isVisible);
        }

        if (ankleMarker != null)
        {
            ankleMarker.SetActive(isVisible);
        }
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
        }
    }
}