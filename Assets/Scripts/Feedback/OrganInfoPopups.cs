using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.Transplant;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// The first time a visitor picks up each heart, a short line says what they are holding:
    /// the sick one is dilated and weak, the donor's is healthy. Two hearts that look alike to a
    /// layman are the whole story of the operation, and the hand is where it is learned.
    ///
    /// Over the heart, following it for a few seconds, then gone; once per heart per visitor.
    /// Off the projector's layer like every line of text. The words come from the stand's text
    /// file (StandTexts), where the advisor can change them.
    /// </summary>
    public class OrganInfoPopups : MonoBehaviour
    {
        public const string SickKey = "popup_coracao_doente";
        public const string DonorKey = "popup_coracao_doador";

        [SerializeField] private GrabbableOrgan[] organs = new GrabbableOrgan[0];
        [SerializeField] private EventSessionController session;

        [SerializeField, Min(0.5f)] private float showSeconds = 3.5f;
        [SerializeField, Min(0f)] private float above = 0.14f;
        [SerializeField, Min(0.005f)] private float letterHeight = 0.018f;
        [SerializeField] private Color sickColour = new Color(1f, 0.78f, 0.45f);
        [SerializeField] private Color donorColour = new Color(0.55f, 1f, 0.6f);
        [SerializeField] private int layer = -1;

        private readonly HashSet<GrabbableOrgan> _seen = new HashSet<GrabbableOrgan>();
        private readonly List<(SurgicalInteractable source, System.Action<SurgicalInteractable, IHandInteractor> handler)> _hooks =
            new List<(SurgicalInteractable, System.Action<SurgicalInteractable, IHandInteractor>)>();

        private TextMesh _text;
        private Transform _following;
        private float _left;
        private Camera _camera;

        public string Showing => _text != null && _left > 0f ? _text.text : string.Empty;

        private void OnEnable()
        {
            _hooks.Clear();
            foreach (GrabbableOrgan organ in organs)
            {
                if (organ == null) { continue; }
                SurgicalInteractable handle = organ.GetComponent<SurgicalInteractable>();
                if (handle == null) { continue; }

                GrabbableOrgan captured = organ;
                System.Action<SurgicalInteractable, IHandInteractor> handler = (_, __) => FirstTouch(captured);
                handle.Grabbed += handler;
                _hooks.Add((handle, handler));
            }

            if (session != null) { session.StateChanged += HandleSession; }
        }

        private void OnDisable()
        {
            foreach ((SurgicalInteractable source, System.Action<SurgicalInteractable, IHandInteractor> handler) in _hooks)
            {
                if (source != null) { source.Grabbed -= handler; }
            }

            _hooks.Clear();
            if (session != null) { session.StateChanged -= HandleSession; }
        }

        private void HandleSession(SessionState state)
        {
            if (state == SessionState.Briefing) { ResetForVisitor(); }
        }

        /// <summary>A new visitor sees both lines again.</summary>
        public void ResetForVisitor()
        {
            _seen.Clear();
            _left = 0f;
            if (_text != null) { _text.gameObject.SetActive(false); }
        }

        private void FirstTouch(GrabbableOrgan organ)
        {
            if (organ == null || !_seen.Add(organ)) { return; }

            bool sick = organ.Role == OrganRole.Native;
            string line = sick
                ? StandTexts.Get(SickKey, "Coração doente\ndilatado e fraco, não bombeia direito")
                : StandTexts.Get(DonorKey, "Coração do doador\nsaudável");
            Show(line, sick ? sickColour : donorColour, organ.transform);
        }

        private void Show(string line, Color colour, Transform over)
        {
            if (_text == null)
            {
                GameObject go = new GameObject("InfoDoCoracao");
                if (layer >= 0) { go.layer = layer; }
                go.transform.SetParent(transform, false);
                _text = go.AddComponent<TextMesh>();
                _text.anchor = TextAnchor.LowerCenter;
                _text.alignment = TextAlignment.Center;
                _text.fontSize = 96;
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null)
                {
                    _text.font = font;
                    go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                }
            }

            _text.characterSize = letterHeight * 10f / _text.fontSize;
            _text.text = line;
            _text.color = colour;
            _text.gameObject.SetActive(true);
            _following = over;
            _left = showSeconds;
            Place();
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Follows the heart and fades the line out. Stepped by hand in tests.</summary>
        public void Tick(float deltaTime)
        {
            if (_text == null || _left <= 0f) { return; }

            _left -= Mathf.Max(0f, deltaTime);
            if (_left <= 0f)
            {
                _text.gameObject.SetActive(false);
                return;
            }

            Color c = _text.color;
            c.a = Mathf.Clamp01(_left / 0.5f);
            _text.color = c;
            Place();
        }

        private void Place()
        {
            if (_following == null) { return; }
            _text.transform.position = _following.position + Vector3.up * above;

            if (_camera == null) { _camera = Camera.main; }
            if (_camera == null) { return; }
            Vector3 away = _text.transform.position - _camera.transform.position;
            if (away.sqrMagnitude > 1e-6f) { _text.transform.rotation = Quaternion.LookRotation(away, Vector3.up); }
        }

        public void Bind(GrabbableOrgan[] hearts, EventSessionController controller, int hiddenLayer)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { OnDisable(); }
            organs = hearts ?? new GrabbableOrgan[0];
            session = controller;
            layer = hiddenLayer;
            if (live) { OnEnable(); }
        }
    }
}
