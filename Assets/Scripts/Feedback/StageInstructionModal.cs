using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Session;
using VRSurgery.Transplant;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// A short-lived card that appears in front of the visitor at each step of the operation and
    /// says what to do now.
    ///
    /// The monitor on the wall carries the same line all the time, but nobody looks at a wall
    /// while holding a scalpel. This puts the instruction where the eyes already are, once, when
    /// it changes, and takes it away again.
    ///
    /// It is placed in the world where the visitor is looking when the step starts and then left
    /// there, turned to face them. It never follows the head: a panel welded to the face is
    /// uncomfortable in a headset and hides the work, which is the rule the rest of the UI keeps.
    ///
    /// It owns no wording. The text is TransplantProcedure.CurrentInstruction, so this card and
    /// the monitor cannot disagree. It is shown again when that text changes inside a stage (the
    /// pump's steps), but not for the vessel counter, which changes with every stitch.
    /// </summary>
    public class StageInstructionModal : MonoBehaviour
    {
        [Header("Parts")]
        [SerializeField] private Transform body;
        [SerializeField] private Transform plate;
        [SerializeField] private Transform topEdge;
        [SerializeField] private TextMesh heading;
        [SerializeField] private TextMesh instruction;

        [Header("Sources")]
        [SerializeField] private TransplantProcedure procedure;

        [Tooltip("Optional. When set the card is only shown while the round is running.")]
        [SerializeField] private EventSessionController session;

        [Tooltip("The visitor's eyes. Camera.main when empty.")]
        [SerializeField] private Transform viewer;

        [Header("Placement")]
        [SerializeField, Min(0.5f)] private float distance = 1.1f;
        [SerializeField] private float heightBelowEyes = 0.08f;

        [Header("Timing")]
        [SerializeField, Min(1f)] private float showSeconds = 6f;
        [SerializeField, Min(0.05f)] private float popSeconds = 0.25f;
        [SerializeField, Min(0.05f)] private float fadeSeconds = 0.35f;

        [Header("Layout (metres)")]
        [Tooltip("Widest a line of instruction may be before it wraps. TextMesh does not wrap on its own.")]
        [SerializeField, Min(0.1f)] private float maxTextWidth = 0.46f;

        [SerializeField, Min(0f)] private float padding = 0.045f;
        [SerializeField, Min(0.1f)] private float minPlateWidth = 0.34f;

        [Tooltip("Room for the heading line above the instruction.")]
        [SerializeField, Min(0f)] private float headingBlock = 0.055f;

        private const float DefaultLineHeight = 0.034f;
        private const float AverageGlyphWidth = 0.55f;

        private string _key;
        private float _elapsed = float.PositiveInfinity;
        private Camera _camera;

        public bool IsShowing => _elapsed < showSeconds;

        /// <summary>The instruction on the card now, or empty when it is hidden.</summary>
        public string ShownText { get; private set; } = string.Empty;

        /// <summary>Width and height of the plate behind the text, in metres.</summary>
        public Vector2 PlateSize { get; private set; }

        /// <summary>The widest line of the instruction as laid out, in metres.</summary>
        public float WidestLine { get; private set; }

        /// <summary>Wires the parts. Used by the scene builder and by tests.</summary>
        public void Bind(Transform cardBody, Transform backPlate, Transform edge, TextMesh headingText,
            TextMesh instructionText, TransplantProcedure transplant, EventSessionController sessionController,
            Transform eyes)
        {
            body = cardBody;
            plate = backPlate;
            topEdge = edge;
            heading = headingText;
            instruction = instructionText;
            procedure = transplant;
            session = sessionController;
            viewer = eyes;
            SetVisible(false);
        }

        private void Awake() => SetVisible(false);

        private void Update() => Tick(Time.deltaTime);

        public void Hide()
        {
            _elapsed = float.PositiveInfinity;
            ShownText = string.Empty;
            SetVisible(false);
        }

        /// <summary>Advances the timer and shows the card when the instruction has changed.</summary>
        public void Tick(float deltaTime)
        {
            Poll();

            if (!IsShowing) { return; }

            _elapsed += Mathf.Max(0f, deltaTime);
            if (_elapsed >= showSeconds)
            {
                Hide();
                return;
            }

            Animate(_elapsed);
        }

        private void Poll()
        {
            bool allowed = procedure != null
                && (session == null || session.State == SessionState.Running)
                && procedure.Stage != TransplantStage.Idle
                && procedure.Stage != TransplantStage.Complete;

            if (!allowed)
            {
                // Forgetting the key means the step in progress is announced again when the
                // round starts, rather than being taken for something already said.
                _key = null;
                if (IsShowing) { Hide(); }
                return;
            }

            string key = KeyFor(procedure);
            if (key == _key) { return; }

            _key = key;
            Present(procedure.CurrentInstruction);
        }

        private static string KeyFor(TransplantProcedure transplant)
        {
            return transplant.Stage == TransplantStage.ConnectVessels
                ? transplant.Stage.ToString()
                : transplant.Stage + "|" + transplant.CurrentInstruction;
        }

        private void Present(string text)
        {
            ShownText = text ?? string.Empty;
            _elapsed = 0f;

            if (heading != null) { heading.text = "PRÓXIMO PASSO"; }
            if (instruction != null) { instruction.text = LayOut(ShownText); }
            else { LayOut(ShownText); }

            PlaceInFrontOfViewer();
            SetVisible(true);
            Animate(0f);
        }

        private void PlaceInFrontOfViewer()
        {
            Transform eyes = viewer;
            if (eyes == null)
            {
                if (_camera == null) { _camera = Camera.main; }
                eyes = _camera != null ? _camera.transform : null;
            }

            if (eyes == null) { return; }

            // Only the direction the head is turned in the horizontal plane, so looking down at
            // the table does not put the card on the floor.
            Vector3 forward = Vector3.ProjectOnPlane(eyes.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) { forward = Vector3.ProjectOnPlane(eyes.up, Vector3.up); }
            forward.Normalize();

            // +Z points away from the viewer, like the result card: the readable side is -Z.
            Vector3 at = eyes.position + forward * distance + Vector3.down * heightBelowEyes;
            transform.SetPositionAndRotation(at, Quaternion.LookRotation(forward, Vector3.up));
        }

        private void Animate(float t)
        {
            if (body == null) { return; }

            float pop = t < popSeconds
                ? Mathf.Sin(Mathf.Clamp01(t / popSeconds) * Mathf.PI * 0.75f) / Mathf.Sin(Mathf.PI * 0.75f)
                : 1f;
            float fade = Mathf.Clamp01((showSeconds - t) / fadeSeconds);
            body.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.Min(pop, 1.1f) * fade);
        }

        private void SetVisible(bool visible)
        {
            GameObject target = body != null ? body.gameObject : null;
            if (target != null && target.activeSelf != visible) { target.SetActive(visible); }
        }

        /// <summary>
        /// Breaks a sentence into lines no wider than <paramref name="maxWidth"/> by the given
        /// measure, on word boundaries. A single word wider than the limit stays on a line of its own.
        /// </summary>
        public static string[] WrapToWidth(string text, float maxWidth, System.Func<string, float> measure)
        {
            if (string.IsNullOrEmpty(text)) { return new string[0]; }

            List<string> lines = new List<string>();
            string line = string.Empty;
            foreach (string word in text.Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && measure(candidate) > maxWidth)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }

            lines.Add(line);
            return lines.ToArray();
        }

        private float LineHeight => instruction != null
            ? instruction.characterSize * instruction.fontSize / 10f
            : DefaultLineHeight;

        /// <summary>
        /// Width of a string as the card will draw it. Read from the font when there is one, so
        /// the plate fits the real glyphs; an estimate from the line height otherwise.
        /// </summary>
        private float Measure(string text)
        {
            if (instruction == null || instruction.font == null)
            {
                return text.Length * LineHeight * AverageGlyphWidth;
            }

            Font font = instruction.font;
            int size = instruction.fontSize;
            font.RequestCharactersInTexture(text, size, instruction.fontStyle);

            float pixels = 0f;
            foreach (char c in text)
            {
                pixels += font.GetCharacterInfo(c, out CharacterInfo info, size, instruction.fontStyle)
                    ? info.advance
                    : size * AverageGlyphWidth;
            }

            return pixels * instruction.characterSize / 10f;
        }

        /// <summary>
        /// Wraps the instruction and sizes the solid plate to the text, so a long sentence never
        /// runs off the edge and a short one does not sit in a card three times too big.
        /// </summary>
        private string LayOut(string text)
        {
            string[] lines = WrapToWidth(text, maxTextWidth, Measure);

            float widest = 0f;
            foreach (string line in lines) { widest = Mathf.Max(widest, Measure(line)); }
            WidestLine = widest;

            float textHeight = lines.Length * LineHeight * 1.2f;
            float width = Mathf.Max(minPlateWidth, widest + padding * 2f);
            float height = headingBlock + textHeight + padding * 2f;
            PlateSize = new Vector2(width, height);

            float top = height * 0.5f;
            if (plate != null) { plate.localScale = new Vector3(width, height, plate.localScale.z); }
            if (topEdge != null)
            {
                topEdge.localScale = new Vector3(width, topEdge.localScale.y, topEdge.localScale.z);
                topEdge.localPosition = new Vector3(0f, top - topEdge.localScale.y * 0.5f, topEdge.localPosition.z);
            }

            if (heading != null)
            {
                heading.transform.localPosition = new Vector3(0f, top - padding - 0.015f,
                    heading.transform.localPosition.z);
            }

            if (instruction != null)
            {
                float textTop = top - padding - headingBlock;
                instruction.transform.localPosition = new Vector3(0f, textTop - textHeight * 0.5f,
                    instruction.transform.localPosition.z);
            }

            return string.Join("\n", lines);
        }
    }
}
