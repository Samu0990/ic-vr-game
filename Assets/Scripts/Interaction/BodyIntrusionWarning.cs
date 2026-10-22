using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSurgery.Transplant;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// Tells the hand when what it holds has gone into the patient's closed body: a red rim on
    /// the object and a buzz in the controller, for as long as it stays there.
    ///
    /// A warning rather than a wall, on purpose. Colliders on the torso and the ribs would stop a
    /// held heart, but the heart is wider than the operative window (17.6 cm against 15) and than
    /// the gap the retractor opens between the rib halves (about 11 cm): any wall that kept it
    /// out of the body would also trap it in the chest, and taking it out is the point of the
    /// operation. The tracked hand passes through everything anyway — there is no physical hand
    /// to stop — so the honest fix is to say so, the way a surgeon's own hands feel resistance.
    ///
    /// Only the centre of each collider is asked about, not its corners: a heart sitting
    /// correctly in its seat reaches past the window's edge under the skin, and must not be told
    /// off for being where it belongs.
    /// </summary>
    public class BodyIntrusionWarning : MonoBehaviour
    {
        [SerializeField] private PatientBodyVolume body;

        [Tooltip("The hearts and the instruments: anything a hand can carry into the patient.")]
        [SerializeField] private SurgicalInteractable[] watched = new SurgicalInteractable[0];

        [Tooltip("The grab rim's material; the warning uses the same rim in red.")]
        [SerializeField] private GrabOutlines outlines;

        [SerializeField] private Color warningColour = new Color(1f, 0.16f, 0.12f, 1f);

        [Tooltip("How far around the operative window the work is left alone, in metres.")]
        [SerializeField, Min(0f)] private float windowMargin = 0.03f;

        [SerializeField, Range(0f, 1f)] private float amplitude = 0.45f;
        [SerializeField, Min(0.02f)] private float pulseSeconds = 0.08f;
        [SerializeField, Min(0.05f)] private float pulseEvery = 0.3f;

        private readonly HashSet<SurgicalInteractable> _warned = new HashSet<SurgicalInteractable>();
        private readonly Dictionary<SurgicalInteractable, Collider[]> _colliders =
            new Dictionary<SurgicalInteractable, Collider[]>();
        private Material _warning;
        private float _sincePulse;

        /// <summary>Raised when a held object first goes into the closed body.</summary>
        public event Action<SurgicalInteractable> Entered;

        public bool IsWarning(SurgicalInteractable item) => item != null && _warned.Contains(item);

        private void Awake()
        {
            Material rim = outlines != null ? outlines.Material : null;
            if (rim == null) { return; }

            _warning = new Material(rim) { name = "ContornoAviso (runtime)" };
            if (_warning.HasProperty("_Color")) { _warning.SetColor("_Color", warningColour); }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Checks every watched object. Stepped by hand in tests.</summary>
        public void Tick(float deltaTime)
        {
            _sincePulse += Mathf.Max(0f, deltaTime);
            bool pulse = _sincePulse >= pulseEvery;
            if (pulse) { _sincePulse = 0f; }

            for (int i = 0; i < watched.Length; i++)
            {
                SurgicalInteractable item = watched[i];
                if (item == null) { continue; }

                bool inside = body != null && item.IsHeld && item.isActiveAndEnabled && Intrudes(item);
                if (inside)
                {
                    if (_warned.Add(item))
                    {
                        Paint(item, true);
                        Buzz(item);
                        Entered?.Invoke(item);
                    }
                    else if (pulse)
                    {
                        Buzz(item);
                    }
                }
                else if (_warned.Remove(item))
                {
                    Paint(item, false);
                }
            }
        }

        private bool Intrudes(SurgicalInteractable item)
        {
            if (!_colliders.TryGetValue(item, out Collider[] parts))
            {
                parts = item.GetComponentsInChildren<Collider>(true);
                _colliders[item] = parts;
            }

            if (parts.Length == 0) { return body.InsideClosedBody(item.transform.position, windowMargin); }

            for (int i = 0; i < parts.Length; i++)
            {
                Collider part = parts[i];
                if (part == null || !part.enabled) { continue; }
                if (body.InsideClosedBody(part.bounds.center, windowMargin)) { return true; }
            }

            return false;
        }

        private void Paint(SurgicalInteractable item, bool on)
        {
            if (_warning == null) { return; }
            GrabOutlines.OutlineOf(item.gameObject).Show(on, _warning);
        }

        private void Buzz(SurgicalInteractable item)
        {
            if (!(item.CurrentHolder is Component holder) || holder == null) { return; }

            XRBaseInputInteractor hand = holder.GetComponent<XRBaseInputInteractor>();
            if (hand != null) { hand.SendHapticImpulse(amplitude, pulseSeconds); }
        }

        public void Bind(PatientBodyVolume patient, SurgicalInteractable[] carried, GrabOutlines rim)
        {
            body = patient;
            watched = carried ?? new SurgicalInteractable[0];
            outlines = rim;
            _colliders.Clear();
            if (Application.isPlaying) { Awake(); }
        }
    }
}
