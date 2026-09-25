using UnityEngine;
using VRSurgery.Session;

namespace VRSurgery.Surgery
{
    /// <summary>
    /// Turns the room's light toward the colour of the risk as the clock runs down — the
    /// "a cor da cena muda conforme o risco aumenta" the concept asks for.
    ///
    /// It drives the directional light and the ambient colour rather than a post-processing
    /// override or a tinted quad in front of the eyes. Two reasons, and both are about the
    /// headset: a full-screen colour layer is the classic way to make people queasy, and a
    /// post-processing stack costs a Quest far more than shifting two colours that the renderer
    /// was already going to read this frame.
    ///
    /// Nothing here is authored per-state: the whole effect hangs off Urgency01, so the tint and
    /// the clock can never disagree about how bad things are.
    /// </summary>
    public class SceneUrgencyTint : MonoBehaviour
    {
        [SerializeField] private EventSessionController session;
        [SerializeField] private Light keyLight;

        [Header("Colours")]
        [Tooltip("Operating-room white, used while the round is calm and whenever it is not running.")]
        [SerializeField] private Color calmLight = new Color(1f, 0.98f, 0.94f);

        [Tooltip("Where the light lands as the clock runs out.")]
        [SerializeField] private Color urgentLight = new Color(1f, 0.62f, 0.55f);

        [SerializeField] private Color calmAmbient = new Color(0.42f, 0.45f, 0.50f);
        [SerializeField] private Color urgentAmbient = new Color(0.38f, 0.20f, 0.20f);

        [Tooltip("Urgency below this leaves the room alone, so the shift reads as a late warning " +
                 "rather than a light that was pink from the first second.")]
        [SerializeField, Range(0f, 1f)] private float onsetUrgency = 0.45f;

        [Tooltip("Seconds the colour takes to catch up. Stops a reset from snapping the room.")]
        [SerializeField, Min(0.01f)] private float responseSeconds = 0.35f;

        private Color _baseAmbient;
        private float _baseIntensity;
        private float _applied;

        /// <summary>0 while the room is untouched, 1 at full alarm. Read by tests.</summary>
        public float Tint01 => _applied;

        private void Awake()
        {
            _baseAmbient = RenderSettings.ambientLight;
            _baseIntensity = keyLight != null ? keyLight.intensity : 1f;
        }

        private void OnDisable()
        {
            // Leaving the room pink for the next scene load would be a cross-session bug that only
            // shows up on the second visitor of the day.
            RenderSettings.ambientLight = _baseAmbient;
            if (keyLight != null)
            {
                keyLight.color = calmLight;
                keyLight.intensity = _baseIntensity;
            }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advances the tint. Split out so tests can step it without waiting on frames.</summary>
        public void Tick(float deltaTime)
        {
            float target = 0f;

            if (session != null && session.IsRunning && onsetUrgency < 1f)
            {
                target = Mathf.InverseLerp(onsetUrgency, 1f, session.Urgency01);
            }

            _applied = responseSeconds <= 0f
                ? target
                : Mathf.MoveTowards(_applied, target, deltaTime / responseSeconds);

            Apply(_applied);
        }

        private void Apply(float amount)
        {
            RenderSettings.ambientLight = Color.Lerp(calmAmbient, urgentAmbient, amount);

            if (keyLight != null)
            {
                keyLight.color = Color.Lerp(calmLight, urgentLight, amount);

                // Dropping the key a little as it reddens reads as the room closing in. Kept
                // shallow: an operating field the player cannot see is not tension, it is a bug.
                keyLight.intensity = _baseIntensity * Mathf.Lerp(1f, 0.82f, amount);
            }
        }

        public void Bind(EventSessionController controller, Light light)
        {
            session = controller;
            keyLight = light;
            _baseIntensity = light != null ? light.intensity : 1f;
        }
    }
}
