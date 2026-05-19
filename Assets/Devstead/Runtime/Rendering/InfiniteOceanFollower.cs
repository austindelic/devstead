using UnityEngine;

namespace Devstead.Rendering
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class InfiniteOceanFollower : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float waterLevel;
        [SerializeField, Min(1.0f)] private float followGridSize = 50.0f;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public float WaterLevel
        {
            get => waterLevel;
            set => waterLevel = value;
        }

        public float FollowGridSize
        {
            get => followGridSize;
            set => followGridSize = Mathf.Max(1.0f, value);
        }

        private void LateUpdate()
        {
            FollowTarget();
        }

        private void OnValidate()
        {
            followGridSize = Mathf.Max(1.0f, followGridSize);
            FollowTarget();
        }

        private void FollowTarget()
        {
            if (target == null && Camera.main != null)
            {
                target = Camera.main.transform;
            }

            if (target == null)
            {
                return;
            }

            var targetPosition = target.position;
            transform.position = new Vector3(
                SnapToGrid(targetPosition.x),
                waterLevel,
                SnapToGrid(targetPosition.z));
        }

        private float SnapToGrid(float value)
        {
            return Mathf.Round(value / followGridSize) * followGridSize;
        }
    }
}
