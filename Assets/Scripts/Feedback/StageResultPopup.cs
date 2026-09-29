using System;
using UnityEngine;
using VRSurgery.Transplant;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// The card that pops up over the chest when a step of the operation is finished: up to
    /// three stars, lit one after another, a title and one line on how it went.
    ///
    /// It is the reward beat every surgery game has and this one lacked — the stage used to just
    /// change, and a visitor who had cut a perfect incision got the same nothing as one who had
    /// hacked at it. It judges nothing itself: the worker grades, this shows the grade.
    ///
    /// Anchored in the world over the work, turned to face the viewer and never stuck to the
    /// face, for the same reason as the progress ring. Polls its own timer so tests can step it.
    /// </summary>
    public class StageResultPopup : MonoBehaviour
    {
        [Header("Parts")]
        [SerializeField] private Transform body;
        [SerializeField] private TextMesh title;
        [SerializeField] private TextMesh detail;
        [SerializeField] private Renderer[] stars = new Renderer[0];
        [SerializeField] private Material starLit;
        [SerializeField] private Material starDim;

        [Header("Sources")]
        [SerializeField] private SkinIncisionWorker incision;
        [SerializeField] private DefibrillationWorker defibrillation;
        [SerializeField] private SutureWorker suture;

        [Tooltip("Where the card appears when a source is graded: above the middle of the chest.")]
        [SerializeField] private Transform anchor;

        [Header("Timing")]
        [SerializeField, Min(0.5f)] private float showSeconds = 4.5f;
        [SerializeField, Min(0.05f)] private float popSeconds = 0.25f;
        [SerializeField, Min(0.05f)] private float starInterval = 0.22f;
        [SerializeField] private float rise = 0.02f;

        [SerializeField] private Color bestColor = new Color(1f, 0.83f, 0.3f);
        [SerializeField] private Color goodColor = new Color(0.85f, 0.95f, 1f);
        [SerializeField] private Color poorColor = new Color(1f, 0.6f, 0.35f);

        private float _elapsed = float.PositiveInfinity;
        private Vector3 _at;
        private int _stars;
        private int _revealed;
        private Camera _camera;
        private SkinIncisionWorker _subscribed;
        private DefibrillationWorker _subscribedDefib;
        private SutureWorker _subscribedSuture;

        public bool IsShowing => _elapsed < showSeconds;
        public string ShownTitle { get; private set; } = string.Empty;
        public string ShownDetail { get; private set; } = string.Empty;
        public int ShownStars => _stars;

        /// <summary>Stars lit so far on the card showing now.</summary>
        public int RevealedStars => _revealed;

        /// <summary>Raised as each star lights, with its index, so the room can chime.</summary>
        public event Action<int> StarRevealed;

        private void Awake() => SetVisible(false);

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (incision != null && _subscribed == null)
            {
                _subscribed = incision;
                _subscribed.Graded += HandleIncisionGraded;
            }

            if (defibrillation != null && _subscribedDefib == null)
            {
                _subscribedDefib = defibrillation;
                _subscribedDefib.Converted += HandleConverted;
            }

            if (suture != null && _subscribedSuture == null)
            {
                _subscribedSuture = suture;
                _subscribedSuture.Graded += HandleSutureGraded;
            }
        }

        private void Unsubscribe()
        {
            if (_subscribed != null)
            {
                _subscribed.Graded -= HandleIncisionGraded;
                _subscribed = null;
            }

            if (_subscribedDefib != null)
            {
                _subscribedDefib.Converted -= HandleConverted;
                _subscribedDefib = null;
            }

            if (_subscribedSuture != null)
            {
                _subscribedSuture.Graded -= HandleSutureGraded;
                _subscribedSuture = null;
            }
        }

        private void HandleSutureGraded(SutureGrade grade)
        {
            Vector3 at = anchor != null ? anchor.position : transform.position;
            Show(grade.Title, grade.Detail, grade.Stars, at);
        }

        /// <summary>No stars for a shock: whether it converts is the heart's doing, not the visitor's.</summary>
        private void HandleConverted(int shocks)
        {
            Vector3 at = anchor != null ? anchor.position : transform.position;
            int energy = _subscribedDefib != null ? _subscribedDefib.LastJoules : 0;
            Show("CORAÇÃO BATENDO!",
                $"ritmo sinusal após {shocks} choque{(shocks == 1 ? "" : "s")} de {energy} J", 0, at);
        }

        private void HandleIncisionGraded(IncisionGrade grade)
        {
            Vector3 at = anchor != null ? anchor.position : transform.position;
            Show(grade.Title, grade.Detail, grade.Stars, at);
        }

        /// <summary>Pops the card up at a point in the world. A new card replaces one still showing.</summary>
        public void Show(string heading, string line, int starCount, Vector3 at)
        {
            ShownTitle = heading ?? string.Empty;
            ShownDetail = line ?? string.Empty;
            _stars = Mathf.Clamp(starCount, 0, 3);
            _revealed = 0;
            _at = at;
            _elapsed = 0f;

            Color colour = _stars >= 3 ? bestColor : _stars == 2 || _stars == 0 ? goodColor : poorColor;
            if (title != null) { title.text = ShownTitle; title.color = colour; }
            if (detail != null) { detail.text = ShownDetail; }

            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i] == null) { continue; }
                // A card without a grade hides the row rather than showing three empty stars.
                stars[i].enabled = _stars > 0;
                if (starDim != null) { stars[i].sharedMaterial = starDim; }
            }

            SetVisible(true);
            Place(0f);
        }

        public void Hide()
        {
            _elapsed = float.PositiveInfinity;
            SetVisible(false);
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (!IsShowing) { return; }

            _elapsed += Mathf.Max(0f, deltaTime);
            if (_elapsed >= showSeconds)
            {
                Hide();
                return;
            }

            // The stars light one after another once the card has popped, the way every game
            // counts a result out rather than stamping it.
            while (_revealed < _stars && _elapsed >= popSeconds + starInterval * (_revealed + 1))
            {
                if (_revealed < stars.Length && stars[_revealed] != null && starLit != null)
                {
                    stars[_revealed].sharedMaterial = starLit;
                }

                StarRevealed?.Invoke(_revealed);
                _revealed++;
            }

            Place(_elapsed);
        }

        private void Place(float t)
        {
            float lift = rise * Mathf.SmoothStep(0f, 1f, t / showSeconds);
            transform.position = _at + Vector3.up * lift;

            if (_camera == null) { _camera = Camera.main; }
            if (_camera != null)
            {
                Vector3 away = transform.position - _camera.transform.position;
                if (away.sqrMagnitude > 1e-6f) { transform.rotation = Quaternion.LookRotation(away, Vector3.up); }
            }

            if (body == null) { return; }

            // Pops in with a small overshoot and shrinks away over the last quarter second.
            float pop = t < popSeconds
                ? Mathf.Sin(Mathf.Clamp01(t / popSeconds) * Mathf.PI * 0.75f) / Mathf.Sin(Mathf.PI * 0.75f)
                : 1f;
            float fade = Mathf.Clamp01((showSeconds - t) / 0.25f);
            body.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.Min(pop, 1.12f) * fade);
        }

        private void SetVisible(bool visible)
        {
            GameObject target = body != null ? body.gameObject : null;
            if (target != null && target.activeSelf != visible) { target.SetActive(visible); }
        }

        /// <summary>Adds the skin closure: a graded card when the last stitch is tied.</summary>
        public void BindSuture(SutureWorker worker)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { Unsubscribe(); }
            suture = worker;
            if (live) { Subscribe(); }
        }

        /// <summary>Adds the defibrillation: a card when the new heart is back in rhythm.</summary>
        public void BindDefibrillation(DefibrillationWorker worker)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { Unsubscribe(); }
            defibrillation = worker;
            if (live) { Subscribe(); }
        }

        public void Bind(Transform cardBody, TextMesh heading, TextMesh line, Renderer[] starRenderers,
            Material lit, Material dim, SkinIncisionWorker skinIncision, Transform where)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { Unsubscribe(); }

            body = cardBody;
            title = heading;
            detail = line;
            stars = starRenderers ?? new Renderer[0];
            starLit = lit;
            starDim = dim;
            incision = skinIncision;
            anchor = where;

            if (live) { Subscribe(); }
        }
    }
}
