using System;
using System.Collections.Generic;
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
        private readonly Queue<(string title, string detail, int stars, Vector3 at)> _waiting =
            new Queue<(string, string, int, Vector3)>();

        [Header("Operation summary (optional)")]
        [SerializeField] private TransplantProcedure procedure;
        private TransplantProcedure _subscribedProcedure;
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

            if (procedure != null && _subscribedProcedure == null)
            {
                _subscribedProcedure = procedure;
                _subscribedProcedure.ProcedureCompleted += HandleProcedureCompleted;
                _subscribedProcedure.StageChanged += HandleStageChanged;
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

            if (_subscribedProcedure != null)
            {
                _subscribedProcedure.ProcedureCompleted -= HandleProcedureCompleted;
                _subscribedProcedure.StageChanged -= HandleStageChanged;
                _subscribedProcedure = null;
            }
        }

        /// <summary>
        /// The last card: the operation done, with the grades of the steps that have one. Its
        /// stars are the rounded mean of theirs, so the summary never contradicts the step cards.
        /// </summary>
        private void HandleProcedureCompleted()
        {
            List<string> parts = new List<string>();
            int total = 0, graded = 0;

            if (incision != null && incision.IsComplete)
            {
                parts.Add($"incisão {incision.Grade.Stars}/3");
                total += incision.Grade.Stars;
                graded++;
            }

            if (suture != null && suture.IsComplete)
            {
                parts.Add($"sutura {suture.Grade.Stars}/3");
                total += suture.Grade.Stars;
                graded++;
            }

            if (defibrillation != null && defibrillation.Shocks > 0)
            {
                parts.Add($"{defibrillation.Shocks} choque{(defibrillation.Shocks == 1 ? "" : "s")}");
            }

            int stars = graded == 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(total / (float)graded), 1, 3);
            Vector3 at = anchor != null ? anchor.position : transform.position;
            Show("TRANSPLANTE CONCLUÍDO!", parts.Count > 0 ? string.Join(" · ", parts) : "o coração novo está batendo", stars, at);
        }

        /// <summary>The patient reset for the next visitor: last visitor's cards go with them.</summary>
        private void HandleStageChanged(TransplantStage stage)
        {
            if (stage == TransplantStage.Idle) { Clear(); }
        }

        /// <summary>Adds the end-of-operation summary card.</summary>
        public void BindProcedure(TransplantProcedure transplant)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { Unsubscribe(); }
            procedure = transplant;
            if (live) { Subscribe(); }
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

        /// <summary>Cards waiting for the one on screen to finish.</summary>
        public int Waiting => _waiting.Count;

        /// <summary>
        /// Pops the card up at a point in the world. If one is still up, this one waits its turn:
        /// the last stitch and the end of the operation happen in the same instant, and both cards
        /// deserve to be read.
        /// </summary>
        public void Show(string heading, string line, int starCount, Vector3 at)
        {
            if (IsShowing)
            {
                if (_waiting.Count < 4) { _waiting.Enqueue((heading, line, starCount, at)); }
                return;
            }

            Present(heading, line, starCount, at);
        }

        private void Present(string heading, string line, int starCount, Vector3 at)
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

        /// <summary>Drops the card on screen and any waiting: a new visitor starts with a clean slate.</summary>
        public void Clear()
        {
            _waiting.Clear();
            Hide();
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (!IsShowing) { return; }

            _elapsed += Mathf.Max(0f, deltaTime);
            if (_elapsed >= showSeconds)
            {
                Hide();
                if (_waiting.Count > 0)
                {
                    (string heading, string line, int count, Vector3 at) = _waiting.Dequeue();
                    Present(heading, line, count, at);
                }

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
