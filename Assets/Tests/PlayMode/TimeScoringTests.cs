using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// O relógio como pontuação: +10 s por etapa concluída pelo visitante, −5 s por erro dele,
    /// o mesmo erro no máximo uma vez a cada 3 s, nada durante o briefing, nunca abaixo de zero,
    /// e zero encerra a vez. E a vez inteira cabendo em ~4 minutos em todo nível.
    /// </summary>
    public class TimeScoringTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private EventSessionController _session;
        private TransplantProcedure _procedure;
        private TimeScoring _scoring;
        private List<TimeAdjustment> _adjustments;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null) { UnityEngine.Object.DestroyImmediate(go); }
            }

            _spawned.Clear();
            SurgeryEvents.ResetAll();
        }

        private GameObject Spawn(string name)
        {
            GameObject go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        /// <summary>A running round on the given level, scored, with the procedure at its first stage.</summary>
        private void Running(SurgicalDifficulty level = SurgicalDifficulty.Medio)
        {
            GameObject systems = Spawn("Systems");
            _procedure = systems.AddComponent<TransplantProcedure>();
            _procedure.SetDifficulty(level);
            _session = systems.AddComponent<EventSessionController>();
            _session.Bind(EventSessionDefinition.Create(round: 180f), null);
            _scoring = systems.AddComponent<TimeScoring>();
            _scoring.Configure(10f, 5f, 3f, 240f);
            _scoring.Bind(_session, _procedure, null, null);
            _adjustments = new List<TimeAdjustment>();
            _scoring.Adjusted += a => _adjustments.Add(a);

            _procedure.Begin();
            _scoring.ApplyRoundLength();
            _session.BeginSession();
            _session.StartRound();
        }

        private static void Raise(object source, string eventName, params object[] args)
        {
            FieldInfo field = source.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{source.GetType().Name} não tem o evento {eventName}.");
            (field.GetValue(source) as Delegate)?.DynamicInvoke(args);
        }

        // ------------------------------------------------------------------ bônus

        [Test]
        public void CadaEtapaConcluida_Vale10Segundos_MasOComecoDaRodadaNao()
        {
            Running();
            float start = _session.RemainingSeconds;
            Assert.IsEmpty(_adjustments, "Começar a rodada não ganha nada.");

            Assert.IsTrue(_procedure.CompleteStage(TransplantStage.OpenChest));

            Assert.AreEqual(start + 10f, _session.RemainingSeconds, 1e-3f);
            Assert.AreEqual("+10 s · Esterno aberto", _adjustments[0].Label);
            Assert.AreEqual(TransplantStage.OpenChest, _adjustments[0].Stage, "Registrado com a etapa que ganhou o bônus.");
        }

        [Test]
        public void NoFacil_AEtapaQueAEquipeFez_NaoDaBonus()
        {
            Running(SurgicalDifficulty.Facil);
            float start = _session.RemainingSeconds;

            _procedure.CompleteStage(TransplantStage.OpenChest);

            Assert.AreEqual(1, _adjustments.Count, "Só a etapa do visitante conta; a CEC foi da equipe.");
            Assert.AreEqual(start + 10f, _session.RemainingSeconds, 1e-3f);
        }

        [Test]
        public void AUltimaEtapa_ContaAntesDoFimDaRodada()
        {
            Running();
            _procedure.SetSkinStages(false);
            int result = 0;
            _session.RoundEnded += r => result = r.Score;

            // What the round bridge does in the scene: the finished operation wins the round.
            _procedure.ProcedureCompleted += SurgeryEvents.RaiseSurgeryCompleted;

            foreach (TransplantStage stage in new List<TransplantStage>(_procedure.Order))
            {
                if (stage == TransplantStage.GoOnBypass)
                {
                    _procedure.Bypass.Attempt(BypassStep.Cannulate);
                    _procedure.Bypass.Attempt(BypassStep.ClampAorta);
                    _procedure.Bypass.Attempt(BypassStep.Cardioplegia);
                }

                if (stage == TransplantStage.ConnectVessels) { while (_procedure.ConnectVessel()) { } continue; }

                if (stage == TransplantStage.Restart)
                {
                    _procedure.Bypass.Attempt(BypassStep.Unclamp, true);
                    _procedure.Bypass.Attempt(BypassStep.DeAir, true);
                    _procedure.Bypass.Attempt(BypassStep.Wean, true);
                }

                _procedure.CompleteStage(stage);
            }

            Assert.AreEqual(_procedure.Order.Count, _adjustments.Count);
            Assert.AreEqual(SessionState.Success, _session.State);
            Assert.AreEqual(Mathf.RoundToInt(_session.LastResult.RemainingSeconds * 100f), result,
                "Os pontos já incluem o bônus da última etapa.");
        }

        // ------------------------------------------------------------------ penalidades

        [Test]
        public void ErrosDoVisitante_Custam5Segundos_UmaVezACada3s()
        {
            Running();
            SkinIncisionWorker incision = Spawn("Bisturi").AddComponent<SkinIncisionWorker>();
            VesselAnastomosis vessel = Spawn("Aorta").AddComponent<VesselAnastomosis>();
            _scoring.BindSources(incision, new[] { vessel }, null, null, null);
            float start = _session.RemainingSeconds;

            Raise(incision, "Deviated");
            Raise(incision, "Deviated");
            Assert.AreEqual(start - 5f, _session.RemainingSeconds, 1e-3f, "O mesmo erro em seguida conta uma vez só.");

            Raise(incision, "DeepCut");
            Raise(vessel, "BleedingStarted", vessel);
            Assert.AreEqual(start - 15f, _session.RemainingSeconds, 1e-3f, "Erros diferentes contam cada um.");

            _scoring.Tick(3.1f);
            Raise(incision, "Deviated");
            Assert.AreEqual(start - 20f, _session.RemainingSeconds, 1e-3f, "Passados 3 s, o mesmo erro volta a contar.");

            CollectionAssert.AreEqual(
                new[] { "−5 s · Fora da linha", "−5 s · Corte fundo demais", "−5 s · Vaso vazando", "−5 s · Fora da linha" },
                _adjustments.ConvertAll(a => a.Label));
        }

        [Test]
        public void AvisosEFisiologia_NaoPunem()
        {
            Running();
            SkinIncisionWorker incision = Spawn("Bisturi").AddComponent<SkinIncisionWorker>();
            DefibrillationWorker defib = Spawn("Desfibrilador").AddComponent<DefibrillationWorker>();
            _scoring.BindSources(incision, null, null, defib, null);
            float start = _session.RemainingSeconds;

            Raise(incision, "BladeMisaligned");
            Raise(defib, "FibrillationStarted");
            Raise(defib, "Shocked", false);
            SurgeryEvents.RaiseError(ErrorSeverity.Warning, "A equipe vai ajudar: os gestos de segurar ficaram mais rápidos.");

            GameObject tool = Spawn("Pinça");
            SurgicalInteractable held = tool.AddComponent<SurgicalInteractable>();
            ReturnHomeOnRelease recovery = tool.AddComponent<ReturnHomeOnRelease>();
            recovery.Bind(held);
            recovery.UseDropAndRespawn(Vector3.zero, 0.5f, -10f);
            held.OnGrabbed(null);
            tool.transform.position = new Vector3(9f, 0f, 0f);
            held.OnReleased();
            recovery.Tick(1f);

            Assert.AreEqual(start, _session.RemainingSeconds, 1e-3f,
                "Dica da lâmina, fibrilação, choque que não reverte, ajuda da equipe e instrumento caído não custam tempo.");
            Assert.IsEmpty(_adjustments);
        }

        [Test]
        public void PassoDaBombaForaDeOrdem_SoContaQuandoABombaEOTrabalho()
        {
            Running();
            Transform tip = Spawn("Mao").transform;
            Transform clampSite = Spawn("Clampe").transform;
            Transform plegiaSite = Spawn("Cardioplegia").transform;
            plegiaSite.position = new Vector3(1f, 0f, 0f);
            BypassWorker pump = _procedure.gameObject.AddComponent<BypassWorker>();
            pump.Bind(tip, new List<BypassSite>
            {
                new BypassSite { Step = BypassStep.ClampAorta, Point = clampSite, Radius = 0.03f, Seconds = 5f },
                new BypassSite { Step = BypassStep.Cardioplegia, Point = plegiaSite, Radius = 0.03f, Seconds = 5f },
            }, _procedure);
            List<string> refusals = new List<string>();
            pump.StepRefused += refusals.Add;
            _scoring.BindSources(null, null, pump, null, null);

            _procedure.CompleteStage(TransplantStage.OpenChest);
            float afterBonus = _session.RemainingSeconds;
            tip.position = clampSite.position;
            pump.Tick(0.1f);
            Assert.AreEqual(afterBonus - 5f, _session.RemainingSeconds, 1e-3f,
                "Clampear antes de canular, na etapa da bomba, é passo fora de ordem.");

            // Na etapa dos vasos a mão costura em cima dos mesmos vasos onde ficam os pontos da
            // bomba: a bomba recusa, mas é a mão passando, não um passo errado.
            FieldInfo stageField = typeof(TransplantProcedure).GetField("<Stage>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            stageField.SetValue(_procedure, TransplantStage.ConnectVessels);
            _scoring.Tick(5f);
            tip.position = plegiaSite.position;
            pump.Tick(0.1f);
            Assert.AreEqual(2, refusals.Count, "A bomba recusou de novo (outro passo)...");
            Assert.AreEqual(afterBonus - 5f, _session.RemainingSeconds, 1e-3f, "...mas fora da etapa da bomba isso não é erro.");
        }

        [Test]
        public void PegarOCoracaoDoenteAntesDaHora_EDeixarUmCoracaoSePerder_Custam()
        {
            Running();
            GameObject heart = Spawn("Heart");
            heart.AddComponent<SurgicalInteractable>();
            GrabbableOrgan native = heart.AddComponent<GrabbableOrgan>();
            native.Bind(OrganRole.Native, Spawn("Assento").transform, _procedure);

            GameObject donorObject = Spawn("DonorHeart");
            SurgicalInteractable donorHeld = donorObject.AddComponent<SurgicalInteractable>();
            ReturnHomeOnRelease recovery = donorObject.AddComponent<ReturnHomeOnRelease>();
            recovery.Bind(donorHeld);
            recovery.UseDropAndRespawn(Vector3.zero, 0.5f, -10f);
            GrabbableOrgan donor = donorObject.AddComponent<GrabbableOrgan>();
            donor.Bind(OrganRole.Donor, Spawn("AssentoDoador").transform, _procedure);

            _scoring.BindSources(null, null, null, null, new[] { native, donor });
            float start = _session.RemainingSeconds;

            native.ConsiderGrab(true);
            Assert.AreEqual(start - 5f, _session.RemainingSeconds, 1e-3f, "Tentar tirar o coração antes da hora custa.");

            donorHeld.OnGrabbed(null);
            donorObject.transform.position = new Vector3(9f, 0f, 0f);
            donorHeld.OnReleased();
            recovery.Tick(1f);
            Assert.AreEqual(start - 10f, _session.RemainingSeconds, 1e-3f, "Deixar um coração se perder custa.");
            Assert.AreEqual("−5 s · Coração caiu", _adjustments[1].Label);
        }

        // ------------------------------------------------------------------ limites

        [Test]
        public void NadaContaDuranteOBriefing()
        {
            Running();
            _session.ReturnToAttract();
            _session.BeginSession();
            Assert.AreEqual(SessionState.Briefing, _session.State);
            float start = _session.RemainingSeconds;

            Assert.IsFalse(_scoring.Apply(-5f, "Fora da linha", "linha"));
            Assert.IsFalse(_scoring.Apply(10f, "Esterno aberto", null));
            Assert.AreEqual(start, _session.RemainingSeconds, 1e-3f);
        }

        [Test]
        public void OTempoNaoFicaNegativo_EZeradoEncerraARodada()
        {
            Running();
            _session.Tick(_session.RemainingSeconds - 3f);
            Assert.AreEqual(3f, _session.RemainingSeconds, 1e-3f);

            _scoring.Apply(-5f, "Vaso vazando", "vaso");

            Assert.AreEqual(0f, _session.RemainingSeconds, "Nunca abaixo de zero.");
            Assert.AreEqual(SessionState.Failure, _session.State, "Relógio zerado por erro encerra a vez, como o tempo esgotado.");
            Assert.AreEqual(0, _session.LastResult.Score);
        }

        [TestCase(SurgicalDifficulty.Facil, 160f)]
        [TestCase(SurgicalDifficulty.Medio, 150f)]
        [TestCase(SurgicalDifficulty.Dificil, 150f)]
        public void AVezInteiraCabeEm4Minutos(SurgicalDifficulty level, float expectedStart)
        {
            Running(level);
            _procedure.SetSkinStages(true);
            _procedure.SetPericardiumStage(true);
            _scoring.ApplyRoundLength();

            int stages = TimeScoring.BonusStages(_procedure.Order, _procedure.Plan);
            Assert.AreEqual(expectedStart, _session.RoundSeconds, 1e-3f);
            Assert.LessOrEqual(_session.RoundSeconds + stages * _scoring.BonusSeconds, 240f,
                "Tempo inicial + todos os bônus possíveis cabem no limite da vez.");
            Assert.AreEqual(240f, _session.TurnLimitSeconds);
        }
    }
}
