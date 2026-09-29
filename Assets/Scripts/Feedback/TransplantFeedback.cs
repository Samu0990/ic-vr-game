using System.Collections.Generic;
using UnityEngine;
using VRSurgery.Haptics;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// What the operation sounds and feels like: a chime and a firm pulse when a step lands, a
    /// buzz when one is refused, an alarm and a shaking hand while a vessel leaks, and a light
    /// tick in the working hand for as long as a hold is advancing.
    ///
    /// Before this the transplant scene had no audio and no haptics at all — SurgeryAudio and
    /// HapticManager were only ever wired into the older scenes — so a visitor's only feedback was
    /// text on a monitor across the table. In a headset, the hand is where attention already is.
    ///
    /// Everything is optional: a missing worker or a rig with no hands just means that channel
    /// stays quiet.
    /// </summary>
    public class TransplantFeedback : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private BypassWorker bypass;
        [SerializeField] private AnastomosisWorker anastomosis;
        [SerializeField] private Heartbeat donorHeart;
        [SerializeField] private WorkProgressIndicator progress;

        [Tooltip("Where the working hand is. The pulse goes to whichever controller is nearest.")]
        [SerializeField] private Transform workingTip;

        [Tooltip("The rig's hand interactors, one per controller.")]
        [SerializeField] private List<XRHandInteractor> hands = new List<XRHandInteractor>();

        [Header("Skin stages (optional)")]
        [SerializeField] private SkinIncisionWorker incision;
        [SerializeField] private SutureWorker suture;

        [Tooltip("The tools themselves: a pulse goes to whoever holds them.")]
        [SerializeField] private SurgicalInteractable scalpel;
        [SerializeField] private SurgicalInteractable needleHolder;

        [Header("Sternal saw (optional)")]
        [SerializeField] private SternotomyWorker sternotomy;
        [SerializeField] private SurgicalInteractable saw;

        [Tooltip("Seconds between the saw's pulses in the hand while it is cutting bone.")]
        [SerializeField, Min(0.02f)] private float sawPulseInterval = 0.07f;

        [Header("Output")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("Seconds between alarm repeats while any vessel leaks.")]
        [SerializeField, Min(0.2f)] private float alarmInterval = 0.9f;

        [Tooltip("Seconds between the light ticks in the hand while a hold is advancing.")]
        [SerializeField, Min(0.05f)] private float tickInterval = 0.2f;

        private HapticProfile _tick;
        private HapticProfile _confirm;
        private HapticProfile _strong;
        private HapticProfile _alarm;

        private readonly List<VesselAnastomosis> _subscribed = new List<VesselAnastomosis>();
        private float _alarmElapsed;
        private float _tickElapsed;
        private float _sinceSlice = float.PositiveInfinity;
        private float _sinceSawPulse;
        private AudioSource _sawSource;

        /// <summary>True while the saw's loop is playing. Exposed for tests.</summary>
        public bool IsSawing { get; private set; }

        /// <summary>Pulses sent since the scene started. Exposed for tests.</summary>
        public int PulsesSent { get; private set; }

        /// <summary>Clips played since the scene started. Exposed for tests.</summary>
        public int SoundsPlayed { get; private set; }

        private void Awake()
        {
            _tick = HapticProfile.Create(HapticIntensity.Soft, 0.12f, 0.02f);
            _confirm = HapticProfile.Create(HapticIntensity.Medium, 0.45f, 0.08f);
            _strong = HapticProfile.Create(HapticIntensity.Strong, 0.8f, 0.2f);
            _alarm = HapticProfile.Create(HapticIntensity.Critical, 0.6f, 0.12f);

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null) { audioSource = gameObject.AddComponent<AudioSource>(); }
            }

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            // Its own source: a loop that starts and stops with the blade must not cut off the
            // one-shot chimes and alarms playing on the main one.
            _sawSource = gameObject.AddComponent<AudioSource>();
            _sawSource.playOnAwake = false;
            _sawSource.loop = true;
            _sawSource.spatialBlend = 0f;
            _sawSource.volume = 0.45f;
        }

        private void OnEnable()
        {
            if (procedure != null) { procedure.StageChanged += HandleStageChanged; }
            if (bypass != null)
            {
                bypass.StepRefused += HandleRefused;
                bypass.StepPerformed += HandleBypassStep;
            }

            if (donorHeart != null) { donorHeart.Started += HandleHeartStarted; }
            if (progress != null) { progress.Completed += HandleGestureCompleted; }

            SurgeryEvents.OnError += HandleError;
            SubscribeVessels();

            if (incision != null) { incision.Cutting += HandleCutting; }
            if (suture != null)
            {
                suture.NeedlePassed += HandleNeedlePassed;
                suture.Tied += HandleTied;
            }
        }

        private void OnDisable()
        {
            if (procedure != null) { procedure.StageChanged -= HandleStageChanged; }
            if (bypass != null)
            {
                bypass.StepRefused -= HandleRefused;
                bypass.StepPerformed -= HandleBypassStep;
            }

            if (donorHeart != null) { donorHeart.Started -= HandleHeartStarted; }
            if (progress != null) { progress.Completed -= HandleGestureCompleted; }

            SurgeryEvents.OnError -= HandleError;
            UnsubscribeVessels();

            if (incision != null) { incision.Cutting -= HandleCutting; }
            if (suture != null)
            {
                suture.NeedlePassed -= HandleNeedlePassed;
                suture.Tied -= HandleTied;
            }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Drives the repeating channels: the leak alarm and the working tick.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) { return; }

            _sinceSlice += deltaTime;
            _sinceSizzle += deltaTime;
            TickSaw(deltaTime);

            bool leaking = anastomosis != null && anastomosis.BleedingSite != null;
            if (leaking)
            {
                _alarmElapsed += deltaTime;
                if (_alarmElapsed >= alarmInterval)
                {
                    _alarmElapsed = 0f;
                    Play(ProceduralTones.BleedAlarm, 0.6f);
                    Pulse(_alarm);
                }
            }
            else
            {
                _alarmElapsed = alarmInterval;
            }

            if (progress != null && progress.IsActive && !progress.IsAlarm)
            {
                _tickElapsed += deltaTime;
                if (_tickElapsed >= tickInterval)
                {
                    _tickElapsed = 0f;
                    Pulse(_tick);
                }
            }
            else
            {
                _tickElapsed = 0f;
            }
        }

        private void HandleStageChanged(TransplantStage stage)
        {
            if (stage == TransplantStage.Idle) { return; }

            if (stage == TransplantStage.Complete)
            {
                Play(ProceduralTones.Triumph, 0.9f);
                Pulse(_strong);
                return;
            }

            Play(ProceduralTones.StepChime, 0.7f);
            Pulse(_confirm);
        }

        private void HandleBypassStep(BypassStep step) => Pulse(_confirm);

        private void HandleRefused(string reason)
        {
            Play(ProceduralTones.ErrorBuzz, 0.7f);
            Pulse(_strong);
        }

        private void HandleError(ErrorSeverity severity, string message)
        {
            Play(ProceduralTones.ErrorBuzz, 0.6f);
            Pulse(_strong);
        }

        private void HandleHeartStarted()
        {
            Play(ProceduralTones.Triumph, 0.8f);
            Pulse(_strong);
        }

        private void HandleGestureCompleted()
        {
            Play(ProceduralTones.SoftConfirm, 0.6f);
            Pulse(_confirm);
        }

        /// <summary>The saw rasps and judders in the hand for exactly as long as it is on the bone.</summary>
        private void TickSaw(float deltaTime)
        {
            bool sawing = sternotomy != null && sternotomy.UsesSaw && sternotomy.IsWorking;

            if (sawing != IsSawing)
            {
                IsSawing = sawing;
                if (_sawSource != null)
                {
                    if (sawing)
                    {
                        _sawSource.clip = ProceduralTones.SawBuzz;
                        _sawSource.Play();
                    }
                    else
                    {
                        _sawSource.Stop();
                    }
                }
            }

            if (!sawing) { _sinceSawPulse = 0f; return; }

            _sinceSawPulse += deltaTime;
            if (_sinceSawPulse >= sawPulseInterval)
            {
                _sinceSawPulse = 0f;
                PulseHolder(saw, _confirm);
            }
        }

        private void HandleCutting()
        {
            // The blade advances most frames of a stroke; one hiss per stroke-length is enough.
            if (_sinceSlice < 0.14f) { return; }
            _sinceSlice = 0f;

            // Resistance in the hand follows depth: skin barely drags, fat drags more, and a blade
            // down on bone judders.
            float depth = incision != null ? incision.CutDepth01 : 0f;
            HapticProfile feel = depth < 0.35f ? _tick : depth < 0.85f ? _confirm : _strong;
            Play(ProceduralTones.Slice, Mathf.Lerp(0.4f, 0.75f, depth));
            PulseHolder(scalpel, feel);
        }

        private void HandleNeedlePassed()
        {
            Play(ProceduralTones.Stitch, 0.7f);
            PulseHolder(needleHolder, _confirm);
        }

        private void HandleTied(int count) => Play(ProceduralTones.SoftConfirm, 0.5f);

        /// <summary>Pulses the hand holding a tool, or the nearest hand if nobody is.</summary>
        private void PulseHolder(SurgicalInteractable tool, HapticProfile profile)
        {
            if (tool != null && tool.IsHeld && tool.CurrentHolder != null && profile != null)
            {
                tool.CurrentHolder.SendHapticFeedback(profile);
                PulsesSent++;
                return;
            }

            Pulse(profile);
        }

        private void HandleVesselBleeding(VesselAnastomosis site)
        {
            Play(ProceduralTones.BleedAlarm, 0.8f);
            Pulse(_alarm);
            _alarmElapsed = 0f;
        }

        private void SubscribeVessels()
        {
            UnsubscribeVessels();
            if (anastomosis == null || anastomosis.Sites == null) { return; }

            foreach (VesselAnastomosis site in anastomosis.Sites)
            {
                if (site == null) { continue; }
                site.BleedingStarted += HandleVesselBleeding;
                _subscribed.Add(site);
            }
        }

        private void UnsubscribeVessels()
        {
            foreach (VesselAnastomosis site in _subscribed)
            {
                if (site != null) { site.BleedingStarted -= HandleVesselBleeding; }
            }

            _subscribed.Clear();
        }

        private void Play(AudioClip clip, float volume)
        {
            if (clip == null || audioSource == null) { return; }
            audioSource.PlayOneShot(clip, volume);
            SoundsPlayed++;
        }

        /// <summary>Sends one pulse to the controller nearest the working tip.</summary>
        public void Pulse(HapticProfile profile)
        {
            XRHandInteractor hand = NearestHand();
            if (hand == null || profile == null) { return; }

            hand.SendHapticFeedback(profile);
            PulsesSent++;
        }

        private XRHandInteractor NearestHand()
        {
            XRHandInteractor best = null;
            float bestDistance = float.MaxValue;
            Vector3 from = workingTip != null ? workingTip.position : transform.position;

            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i] == null) { continue; }
                float distance = (hands[i].transform.position - from).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = hands[i];
                }
            }

            return best;
        }

        private CauteryWorker _cautery;
        private SurgicalInteractable _cauteryPen;
        private float _sinceSizzle = float.PositiveInfinity;

        /// <summary>Adds the cautery pen: sizzle and a buzz in the hand while it burns, a firm pulse when a bleeder seals.</summary>
        public void BindCautery(CauteryWorker cautery, SurgicalInteractable pen)
        {
            if (_cautery != null)
            {
                _cautery.Burning -= HandleBurning;
                _cautery.Sealed -= HandleSealed;
            }

            _cautery = cautery;
            _cauteryPen = pen;

            // Subscribed here rather than in OnEnable: the builder binds this in edit mode, and a
            // live component binds it again at runtime through the serialized fields below.
            if (Application.isPlaying && _cautery != null)
            {
                _cautery.Burning += HandleBurning;
                _cautery.Sealed += HandleSealed;
            }

            cauteryWorker = cautery;
            cauteryPen = pen;
        }

        [Header("Cautery (optional)")]
        [SerializeField] private CauteryWorker cauteryWorker;
        [SerializeField] private SurgicalInteractable cauteryPen;

        private void Start()
        {
            if (cauteryWorker != null && _cautery == null) { BindCautery(cauteryWorker, cauteryPen); }
        }

        private void HandleBurning()
        {
            if (_sinceSizzle < 0.3f) { return; }
            _sinceSizzle = 0f;
            Play(ProceduralTones.Sizzle, 0.6f);
            PulseHolder(_cauteryPen, _tick);
        }

        private void HandleSealed(int bleeder)
        {
            Play(ProceduralTones.SoftConfirm, 0.5f);
            PulseHolder(_cauteryPen, _confirm);
        }

        /// <summary>Adds the sternal saw: its sound and its vibration while it cuts.</summary>
        public void BindSaw(SternotomyWorker sternotomyWorker, SurgicalInteractable sternalSaw)
        {
            sternotomy = sternotomyWorker;
            saw = sternalSaw;
        }

        /// <summary>Adds the scalpel and the needle to what this component answers.</summary>
        public void BindSkinStages(SkinIncisionWorker skinIncision, SutureWorker skinSuture,
            SurgicalInteractable scalpelTool, SurgicalInteractable needleTool)
        {
            bool wasEnabled = Application.isPlaying && isActiveAndEnabled;
            if (wasEnabled) { OnDisable(); }

            incision = skinIncision;
            suture = skinSuture;
            scalpel = scalpelTool;
            needleHolder = needleTool;

            if (wasEnabled) { OnEnable(); }
        }

        public void Bind(TransplantProcedure transplant, BypassWorker bypassWorker,
            AnastomosisWorker anastomosisWorker, Heartbeat donor, WorkProgressIndicator indicator,
            Transform tip, IEnumerable<XRHandInteractor> rigHands)
        {
            // Only a live component holds subscriptions; the scene builder binds this in edit mode.
            bool wasEnabled = Application.isPlaying && isActiveAndEnabled;
            if (wasEnabled) { OnDisable(); }

            procedure = transplant;
            bypass = bypassWorker;
            anastomosis = anastomosisWorker;
            donorHeart = donor;
            progress = indicator;
            workingTip = tip;
            hands = rigHands != null ? new List<XRHandInteractor>(rigHands) : new List<XRHandInteractor>();

            if (wasEnabled) { OnEnable(); }
        }
    }
}
