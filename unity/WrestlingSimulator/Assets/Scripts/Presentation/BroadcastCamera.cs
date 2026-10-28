using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Television-style camera on the officials' side of the mat: follows the action from a raised
    /// angle with damping so pushes and scrambles stay framed.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class BroadcastCamera : MonoBehaviour
    {
        [SerializeField] private float _distance = 6.2f;
        [SerializeField] private float _height = 3.3f;
        [SerializeField] private float _lookHeight = 0.6f;
        [SerializeField] private float _followSharpness = 3f;
        [SerializeField] private float _groundedZoom = 0.82f;

        private Vector3 _focus;
        private float _zoom = 1f;

        public void Snap(Vector3 focus, bool grounded)
        {
            _focus = focus;
            _zoom = grounded ? _groundedZoom : 1f;
            Apply();
        }

        public void Follow(Vector3 focus, bool grounded, float deltaTime)
        {
            float blend = 1f - Mathf.Exp(-_followSharpness * deltaTime);
            _focus = Vector3.Lerp(_focus, focus, blend);
            _zoom = Mathf.Lerp(_zoom, grounded ? _groundedZoom : 1f, blend);
            Apply();
        }

        private void Apply()
        {
            Vector3 target = new Vector3(_focus.x * 0.85f, _lookHeight, _focus.z * 0.6f);
            Vector3 offset = new Vector3(0f, _height, -_distance) * _zoom;
            transform.position = target + offset;
            transform.rotation = Quaternion.LookRotation(target - transform.position, Vector3.up);
        }
    }
}
