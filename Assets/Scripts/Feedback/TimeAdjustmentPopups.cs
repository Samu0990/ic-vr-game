using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Transplant;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// "+10 s · Esterno aberto" in green, "−5 s · Fora da linha" in red, with a chime or a buzz,
    /// right over the hand doing the work — the moment the clock changes and the reason why.
    ///
    /// In the room, never on the face: each one is left where it happened, turns to the viewer,
    /// rises a little and fades, the way the stage cards and the progress ring behave. Kept off
    /// the projector's layer, because nothing written may land on the mannequin.
    /// </summary>
    public class TimeAdjustmentPopups : MonoBehaviour
    {
        [SerializeField] private TimeScoring scoring;

        [SerializeField] private Color gain = new Color(0.35f, 1f, 0.45f);
        [SerializeField] private Color loss = new Color(1f, 0.32f, 0.28f);

        [Tooltip("How far above the working hand the line appears, in metres.")]
        [SerializeField, Min(0f)] private float above = 0.12f;

        [SerializeField, Min(0f)] private float riseMetres = 0.06f;
        [SerializeField, Min(0.3f)] private float lifeSeconds = 1.8f;

        [Tooltip("Height of a letter, in metres.")]
        [SerializeField, Min(0.005f)] private float letterHeight = 0.022f;

        [SerializeField, Range(0f, 1f)] private float volume = 0.6f;

        [Tooltip("Layer the lines are drawn on: one the projector does not see. -1 keeps Default.")]
        [SerializeField] private int layer = -1;

        private struct Line
        {
            public TextMesh Text;
            public Vector3 Origin;
            public Color Colour;
            public float Age;
        }

        private readonly List<Line> _lines = new List<Line>();
        private Camera _camera;
        private Font _font;

        /// <summary>Lines on screen now. Exposed for tests.</summary>
        public int Showing => _lines.Count;

        public string LastShown { get; private set; } = string.Empty;

        private void OnEnable()
        {
            if (scoring != null) { scoring.Adjusted += Show; }
        }

        private void OnDisable()
        {
            if (scoring != null) { scoring.Adjusted -= Show; }
        }

        public void Show(TimeAdjustment adjustment)
        {
            if (_font == null) { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }

            GameObject go = new GameObject("AjusteDeTempo");
            if (layer >= 0) { go.layer = layer; }
            go.transform.SetParent(transform, false);

            TextMesh text = go.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 96;
            text.characterSize = letterHeight * 10f / text.fontSize;
            text.text = adjustment.Label;
            Color colour = adjustment.Gained ? gain : loss;
            text.color = colour;
            if (_font != null)
            {
                text.font = _font;
                go.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
            }

            Vector3 origin = adjustment.Where + Vector3.up * above;
            go.transform.position = origin;
            Face(go.transform);

            _lines.Add(new Line { Text = text, Origin = origin, Colour = colour, Age = 0f });
            LastShown = adjustment.Label;

            if (Application.isPlaying)
            {
                AudioClip clip = adjustment.Gained ? ProceduralTones.SoftConfirm : ProceduralTones.ErrorBuzz;
                AudioSource.PlayClipAtPoint(clip, origin, volume);
            }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Rises, fades and retires the lines. Stepped by hand in tests.</summary>
        public void Tick(float deltaTime)
        {
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                Line line = _lines[i];
                line.Age += Mathf.Max(0f, deltaTime);
                if (line.Text == null || line.Age >= lifeSeconds)
                {
                    if (line.Text != null) { Destroy(line.Text.gameObject); }
                    _lines.RemoveAt(i);
                    continue;
                }

                float t = line.Age / lifeSeconds;
                line.Text.transform.position = line.Origin + Vector3.up * (riseMetres * t);
                Color c = line.Colour;
                c.a = t < 0.7f ? 1f : Mathf.Clamp01((1f - t) / 0.3f);
                line.Text.color = c;
                Face(line.Text.transform);
                _lines[i] = line;
            }
        }

        private void Face(Transform line)
        {
            if (_camera == null) { _camera = Camera.main; }
            if (_camera == null) { return; }

            Vector3 away = line.position - _camera.transform.position;
            if (away.sqrMagnitude > 1e-6f) { line.rotation = Quaternion.LookRotation(away, Vector3.up); }
        }

        public void Bind(TimeScoring source, int hiddenLayer)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { OnDisable(); }
            scoring = source;
            layer = hiddenLayer;
            if (live) { OnEnable(); }
        }
    }
}
