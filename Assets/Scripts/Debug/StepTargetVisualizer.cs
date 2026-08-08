using UnityEngine;

/// <summary>
/// Lightweight scene guide restored for the existing StepTargetGuide component.
/// It has no gameplay effect and only draws a target marker in the Scene view.
/// </summary>
public sealed class StepTargetVisualizer : MonoBehaviour
{
    [SerializeField, Min(0.005f)] private float markerRadius = 0.055f;
    [SerializeField, Min(0.02f)] private float crossSize = 0.16f;
    [SerializeField] private Color markerColor = new Color(0.1f, 0.85f, 1f, 0.95f);

    private void OnDrawGizmos()
    {
        Gizmos.color = markerColor;
        Vector3 position = transform.position;

        Gizmos.DrawWireSphere(position, markerRadius);
        Gizmos.DrawLine(position - transform.right * crossSize, position + transform.right * crossSize);
        Gizmos.DrawLine(position - transform.forward * crossSize, position + transform.forward * crossSize);
        Gizmos.DrawLine(position, position + transform.up * crossSize);
    }
}