using System;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Interaction;
using VRSurgery.Surgery;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// The moment the cross-clamp comes off and blood reaches the new heart for the first time.
    ///
    /// A reperfused heart very often fibrillates instead of beating — reported anywhere from
    /// 10% to 80% after cross-clamp release — and the fix is a shock straight to the heart with
    /// internal paddles, usually 10 to 20 joules. It is the most recognisable beat of heart
    /// surgery and the one this operation skipped: the new heart used to simply start.
    ///
    /// Here the donor heart quivers, the monitor shows ventricular fibrillation and alarms, and
    /// the pump refuses to be weaned (a fibrillating heart cannot take the circulation back).
    /// The visitor takes the paddles, holds the heart between them while they charge, and the
    /// shock fires. A first shock does not always convert, as in life; the second always does
    /// here, because a booth round has a clock.
    /// </summary>
    public class DefibrillationWorker : MonoBehaviour, IWorkProgressSource, IBypassGate
    {
        [Header("Parts")]
        [Tooltip("The point midway between the two paddle cups: the heart goes here.")]
        [SerializeField] private Transform paddleCentre;

        [Tooltip("The paddles. They only charge while held.")]
        [SerializeField] private SurgicalInteractable paddles;

        [SerializeField] private TransplantProcedure procedure;

        [Tooltip("The donor heart's rhythm. Started by a successful shock.")]
        [SerializeField] private Heartbeat donorHeart;

        [Tooltip("What quivers while the heart fibrillates. Defaults to the heartbeat's object.")]
        [SerializeField] private Transform heart;

        [Header("Rhythm")]
        [Tooltip("How often the reperfused heart fibrillates. 1 teaches it every time.")]
        [SerializeField, Range(0f, 1f)] private float fibrillationChance = 1f;

        [Tooltip("Chance that a shock converts the rhythm. The literature puts a first internal " +
                 "shock at roughly 60%.")]
        [SerializeField, Range(0f, 1f)] private float shockSuccessChance = 0.6f;

        [Tooltip("This shock always converts, so a round is never lost to bad luck.")]
        [SerializeField, Min(1)] private int guaranteedByShock = 2;

        [Tooltip("Energy of each shock in turn, in joules: start low and escalate, as the protocol does.")]
        [SerializeField] private int[] joules = { 10, 20, 30 };

        [Header("Paddles")]
        [Tooltip("How close the paddle centre must be to the heart's centre, in metres.")]
        [SerializeField, Min(0.005f)] private float radius = 0.05f;

        [SerializeField, Min(0.1f)] private float chargeSeconds = 1.2f;

        [Tooltip("Seconds after a shock before the paddles charge again.")]
        [SerializeField, Min(0f)] private float recoverSeconds = 1f;

        [Header("Look")]
        [Tooltip("How much the fibrillating muscle's outline shivers, as a fraction of its size.")]
        [SerializeField, Range(0f, 0.05f)] private float quiver = 0.014f;

        [SerializeField, Min(1f)] private float quiverHz = 9f;

        private bool _decided;
        private float _recover;
        private float _clock;
        private Vector3 _restScale;
        private bool _quivering;
        private float _jolt;

        /// <summary>True from the moment the reperfused heart fibrillates until a shock converts it.</summary>
        public bool IsFibrillating { get; private set; }

        /// <summary>True while the paddles are on the heart, charging.</summary>
        public bool IsWorking { get; private set; }

        public float Charge01 { get; private set; }

        /// <summary>Shocks delivered to this patient.</summary>
        public int Shocks { get; private set; }

        /// <summary>Energy the next shock will deliver, in joules.</summary>
        public int NextJoules => joules == null || joules.Length == 0 ? 10 : joules[Mathf.Min(Shocks, joules.Length - 1)];

        /// <summary>Energy of the shock that converted, or of the last one delivered.</summary>
        public int LastJoules { get; private set; }

        float IWorkProgressSource.WorkProgress01 => Charge01;
        Vector3 IWorkProgressSource.WorkPoint => HeartCentre();
        string IWorkProgressSource.WorkLabel => $"CARREGANDO {NextJoules} J";
        bool IWorkProgressSource.IsAlarm => true;

        public event Action FibrillationStarted;

        /// <summary>Raised on the first frame of each charge.</summary>
        public event Action ChargeStarted;

        /// <summary>Raised for every shock, with whether it converted the rhythm.</summary>
        public event Action<bool> Shocked;

        /// <summary>Raised once the heart is back in sinus rhythm, with the number of shocks it took.</summary>
        public event Action<int> Converted;

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            IsWorking = false;
            if (deltaTime <= 0f || procedure == null) { return; }

            _clock += deltaTime;

            // A new patient on the table: forget the last one's heart.
            if (procedure.Bypass.Step == BypassStep.NotStarted && (_decided || IsFibrillating))
            {
                ResetDefibrillation();
                return;
            }

            if (!_decided && procedure.Bypass.Step == BypassStep.Unclamp)
            {
                _decided = true;
                if (Roll(fibrillationChance)) { BeginFibrillation(); }
            }

            if (!IsFibrillating)
            {
                Charge01 = 0f;
                return;
            }

            Quiver(deltaTime);

            if (_recover > 0f)
            {
                _recover -= deltaTime;
                Charge01 = 0f;
                return;
            }

            bool held = paddles == null || paddles.IsHeld;
            bool onHeart = paddleCentre != null && (paddleCentre.position - HeartCentre()).sqrMagnitude <= radius * radius;

            if (!held || !onHeart)
            {
                // Lifting the paddles dumps the charge, the way a real unit disarms.
                Charge01 = Mathf.Max(0f, Charge01 - deltaTime / Mathf.Max(0.05f, chargeSeconds * 0.5f));
                return;
            }

            if (Charge01 <= 0f) { ChargeStarted?.Invoke(); }

            IsWorking = true;
            Charge01 = Mathf.Min(1f, Charge01 + deltaTime / chargeSeconds);
            if (Charge01 >= 1f) { Shock(); }
        }

        /// <summary>True with the given chance; exactly never at 0 and always at 1.</summary>
        private static bool Roll(float chance) =>
            chance >= 1f || (chance > 0f && UnityEngine.Random.value < chance);

        private void BeginFibrillation()
        {
            IsFibrillating = true;
            Shocks = 0;
            Charge01 = 0f;
            if (heart == null && donorHeart != null) { heart = donorHeart.transform; }
            if (heart != null) { _restScale = heart.localScale; _quivering = true; }

            if (procedure != null)
            {
                procedure.SetUrgentInstruction("Fibrilação ventricular! Pegue as pás internas e segure o coração entre elas");
            }

            SurgeryEvents.RaiseError(ErrorSeverity.Warning,
                "O coração novo fibrilou ao receber sangue — é comum. Desfibrile com as pás internas.");
            FibrillationStarted?.Invoke();
        }

        private void Shock()
        {
            LastJoules = NextJoules;
            Shocks++;
            Charge01 = 0f;
            _jolt = 1f;

            bool converted = Shocks >= guaranteedByShock || Roll(shockSuccessChance);
            Shocked?.Invoke(converted);

            if (!converted)
            {
                _recover = recoverSeconds;
                SurgeryEvents.RaiseError(ErrorSeverity.Warning,
                    $"Choque de {LastJoules} J sem reverter. Mantenha as pás no coração: próximo com {NextJoules} J.");
                return;
            }

            IsFibrillating = false;
            StopQuiver();
            if (procedure != null) { procedure.SetUrgentInstruction(null); }
            if (donorHeart != null) { donorHeart.StartBeating(); }
            Converted?.Invoke(Shocks);
        }

        /// <summary>
        /// A fibrillating ventricle does not contract, it writhes: a fast, disorganised shiver of
        /// the whole outline, plus the jolt of each shock.
        /// </summary>
        private void Quiver(float deltaTime)
        {
            if (!_quivering || heart == null) { return; }

            float t = _clock * quiverHz;
            float a = (Mathf.PerlinNoise(t, 0.37f) - 0.5f) * 2f;
            float b = (Mathf.PerlinNoise(0.71f, t * 1.3f) - 0.5f) * 2f;
            _jolt = Mathf.MoveTowards(_jolt, 0f, deltaTime * 6f);
            float jolt = 1f + 0.08f * _jolt;

            heart.localScale = Vector3.Scale(_restScale,
                new Vector3(1f + quiver * a, 1f + quiver * b, 1f - quiver * a * 0.5f) * jolt);
        }

        private void StopQuiver()
        {
            if (_quivering && heart != null) { heart.localScale = _restScale; }
            _quivering = false;
        }

        private Vector3 HeartCentre()
        {
            Transform organ = heart != null ? heart : donorHeart != null ? donorHeart.transform : null;
            if (organ == null) { return transform.position; }

            if (_body == null || _bodyOf != organ)
            {
                _bodyOf = organ;
                _body = organ.GetComponentInChildren<Renderer>();
            }

            return _body != null ? _body.bounds.center : organ.position;
        }

        private Renderer _body;
        private Transform _bodyOf;

        /// <summary>Whether the pump may take this step: not while the heart fibrillates.</summary>
        public string Blocks(BypassStep step)
        {
            if (!IsFibrillating) { return null; }
            if (step != BypassStep.DeAir && step != BypassStep.Wean) { return null; }
            return "O coração está fibrilando e não assume a circulação: desfibrile com as pás internas primeiro.";
        }

        public void ResetDefibrillation()
        {
            StopQuiver();
            IsFibrillating = false;
            IsWorking = false;
            _decided = false;
            _recover = 0f;
            _jolt = 0f;
            Charge01 = 0f;
            Shocks = 0;
            LastJoules = 0;
            if (procedure != null) { procedure.SetUrgentInstruction(null); }
        }

        /// <summary>For tests and levels: how often the heart fibrillates and how often a shock works.</summary>
        public void Configure(float fibrillation, float shockSuccess, int guaranteedBy)
        {
            fibrillationChance = Mathf.Clamp01(fibrillation);
            shockSuccessChance = Mathf.Clamp01(shockSuccess);
            guaranteedByShock = Mathf.Max(1, guaranteedBy);
        }

        public void Bind(Transform centre, SurgicalInteractable paddleTool, TransplantProcedure transplant,
            Heartbeat donor, Transform organ)
        {
            paddleCentre = centre;
            paddles = paddleTool;
            procedure = transplant;
            donorHeart = donor;
            heart = organ;
        }
    }
}
