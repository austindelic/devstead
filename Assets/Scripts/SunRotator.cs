using UnityEngine;

public sealed class SunRotator : MonoBehaviour
{
    [SerializeField] private float rotationSpeedDegreesPerSecond = 10.0f;
    [SerializeField] private Vector3 rotationAxis = Vector3.up;

    private void Update()
    {
        transform.Rotate(rotationAxis.normalized, rotationSpeedDegreesPerSecond * Time.deltaTime, Space.World);
    }
}
