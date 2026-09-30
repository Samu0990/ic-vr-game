using UnityEngine;

namespace Skyborne.Flight
{
    /// <summary>
    /// First-person camera for the flying body. Adds the things that actually communicate
    /// speed without a speedometer: bank into turns, FOV stretch, a head that lags the body
    /// under acceleration, and buffet once the airflow gets violent.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FlightCameraRig : MonoBehaviour
    {
        [SerializeField] private FlyerController flyer;

        [Header("Placement")]
        [Tooltip("Eye offset from the body origin, in body space.")]
        [SerializeField] private Vector3 eyeOffset = new Vector3(0f, 0.72f, 0.1f);

        [Header("Field of view")]
        [SerializeField] private float baseFieldOfView = 68f;
        [SerializeField] private float maxFieldOfView = 104f;
        [Tooltip("Speed (m/s) at which the FOV stretch is fully applied.")]
        [SerializeField] private float fieldOfViewReferenceSpeed = 150f;
        [SerializeField] private float fieldOfViewResponse = 3.5f;

        [Header("Head spring")]
        [Tooltip("Metres the head is pushed back per m/s^2 of body acceleration.")]
        [SerializeField] private float accelerationLag = 0.012f;
        [SerializeField] private float maxLag = 0.28f;
        [SerializeField] private float lagResponse = 9f;

        [Header("Buffet")]
        [Tooltip("Speed (m/s) above which airflow starts shaking the view.")]
        [SerializeField] private float buffetOnsetSpeed = 90f;
        [SerializeField] private float buffetAmplitude = 0.035f;
        [SerializeField] private float buffetFrequency = 22f;

        private Camera _camera;
        private Vector3 _previousVelocity;
        private Vector3 _lag;
        private float _fieldOfView;
        private float _noiseSeed;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _fieldOfView = baseFieldOfView;
            _camera.fieldOfView = baseFieldOfView;
            _noiseSeed = Random.value * 100f;

            if (flyer == null)
            {
                flyer = GetComponentInParent<FlyerController>();
            }
        }

        private void LateUpdate()
        {
            if (flyer == null || flyer.Body == null)
            {
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            Vector3 velocity = flyer.Body.linearVelocity;
            Vector3 acceleration = (velocity - _previousVelocity) / dt;
            _previousVelocity = velocity;

            ApplyRotation();
            ApplyPosition(acceleration, velocity.magnitude, dt);
            ApplyFieldOfView(velocity.magnitude, dt);
        }

        private void ApplyRotation()
        {
            // Aim drives yaw/pitch; the bank is the cosmetic roll from the coordinated turn.
            transform.rotation = flyer.AimRotation * Quaternion.Euler(0f, 0f, flyer.BankAngle);
        }

        private void ApplyPosition(Vector3 acceleration, float speed, float dt)
        {
            Transform body = flyer.transform;

            // The head resists the body's acceleration, so hard thrust shoves the view back
            // and braking pushes it forward. Converted to body space so it survives rolling.
            Vector3 targetLag = Vector3.ClampMagnitude(
                body.InverseTransformDirection(-acceleration) * accelerationLag, maxLag);

            _lag = Vector3.Lerp(_lag, targetLag, 1f - Mathf.Exp(-lagResponse * dt));

            transform.position = body.TransformPoint(eyeOffset + _lag + BuffetOffset(speed));
        }

        private Vector3 BuffetOffset(float speed)
        {
            if (speed <= buffetOnsetSpeed || buffetAmplitude <= 0f)
            {
                return Vector3.zero;
            }

            float intensity = Mathf.Clamp01((speed - buffetOnsetSpeed) / Mathf.Max(1f, buffetOnsetSpeed));
            float t = Time.time * buffetFrequency;

            // Perlin rather than Random so the shake is continuous instead of a jitter.
            float x = Mathf.PerlinNoise(_noiseSeed, t) - 0.5f;
            float y = Mathf.PerlinNoise(_noiseSeed + 17f, t) - 0.5f;

            return new Vector3(x, y, 0f) * (buffetAmplitude * intensity * 2f);
        }

        private void ApplyFieldOfView(float speed, float dt)
        {
            float t = Mathf.Clamp01(speed / Mathf.Max(1f, fieldOfViewReferenceSpeed));
            float target = Mathf.Lerp(baseFieldOfView, maxFieldOfView, t * t);
            _fieldOfView = Mathf.Lerp(_fieldOfView, target, 1f - Mathf.Exp(-fieldOfViewResponse * dt));
            _camera.fieldOfView = _fieldOfView;
        }
    }
}
