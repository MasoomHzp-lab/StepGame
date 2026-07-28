using UnityEngine;

public class SideFollowCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Camera Position")]
    [SerializeField] private Vector3 offset =
        new Vector3(0f, 2.5f, -8f);

    [Header("Camera Look")]
    [SerializeField] private Vector3 lookOffset =
        new Vector3(0f, 1.2f, 0f);

    [Header("Smooth Follow")]
    [SerializeField] private float followSpeed = 6f;

    private void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 desiredPosition =
            target.position + offset;

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            followSpeed * Time.deltaTime
        );

        Vector3 lookPosition =
            target.position + lookOffset;

        transform.rotation = Quaternion.LookRotation(
            lookPosition - transform.position
        );
    }
}