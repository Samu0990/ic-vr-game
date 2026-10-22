using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.Surgery;
using VRSurgery.Transplant;
using VRSurgery.VR;

namespace VRSurgery.Tests
{
    /// <summary>
    /// "Não sei o que fazer agora": cada etapa de cada nível diz o que fazer, aponta onde e tem
    /// fala — e cada visitante fica na própria altura, com o operador podendo corrigir ±2 cm
    /// entre as vezes.
    /// </summary>
    public class GuidanceAndHeightTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null) { Object.DestroyImmediate(go); }
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

        // ------------------------------------------------------------------ 5.1 orientação

        [Test]
        public void NoFacil_SairDeBombaEUmGestoSo_EAInstrucaoDizIsso()
        {
            TransplantProcedure procedure = Spawn("Sistemas").AddComponent<TransplantProcedure>();
            procedure.SetDifficulty(SurgicalDifficulty.Facil);
            procedure.Begin();
            procedure.CompleteStage(TransplantStage.OpenChest);
            procedure.CompleteStage(TransplantStage.RemoveNativeHeart);
            procedure.CompleteStage(TransplantStage.PlaceDonorHeart);
            while (procedure.ConnectVessel()) { }
            Assert.AreEqual(TransplantStage.Restart, procedure.Stage);

            StringAssert.StartsWith("Sair de bomba", procedure.StageInstruction,
                "No Fácil a saída é um gesto só (desclampar, desarejar e sair): dizer só " +
                "'retire o clampe' deixa o visitante esperando passos que não vêm.");
        }

        [Test]
        public void NaFibrilacao_ASetaApontaOCoracao_NaoOProximoPontoDaBomba()
        {
            TransplantProcedure procedure = Spawn("Sistemas").AddComponent<TransplantProcedure>();
            procedure.SetDifficulty(SurgicalDifficulty.Medio);
            procedure.Begin();
            procedure.Bypass.Attempt(BypassStep.Cannulate);
            procedure.Bypass.Attempt(BypassStep.ClampAorta);
            procedure.Bypass.Attempt(BypassStep.Cardioplegia);
            procedure.CompleteStage(TransplantStage.OpenChest);
            procedure.CompleteStage(TransplantStage.GoOnBypass);
            procedure.CompleteStage(TransplantStage.RemoveNativeHeart);
            procedure.CompleteStage(TransplantStage.PlaceDonorHeart);
            while (procedure.ConnectVessel()) { }
            Assert.IsTrue(procedure.Bypass.Attempt(BypassStep.Unclamp, true).Accepted);

            Transform donor = Spawn("DonorHeart").transform;
            Heartbeat beat = donor.gameObject.AddComponent<Heartbeat>();
            DefibrillationWorker defib = Spawn("Desfibrilador").AddComponent<DefibrillationWorker>();
            defib.Bind(Spawn("Pas").transform, null, procedure, beat, donor);
            defib.Configure(1f, 0f, 2);
            defib.Tick(0.02f);
            Assert.IsTrue(defib.IsFibrillating);

            Transform deAirSite = Spawn("Bypass_DeAir").transform;
            BypassWorker pump = Spawn("Bomba").AddComponent<BypassWorker>();
            pump.Bind(Spawn("Mao").transform, new List<BypassSite>
            {
                new BypassSite { Step = BypassStep.DeAir, Point = deAirSite, Radius = 0.03f, Seconds = 7f },
            }, procedure);

            NextStepArrow arrow = Spawn("Seta").AddComponent<NextStepArrow>();
            arrow.Bind(procedure, pump, new VesselAnastomosis[0], null, null, donor, null, null, null);
            arrow.BindDefibrillation(defib);
            arrow.Tick(0.02f);

            Assert.AreEqual(donor, arrow.Target, "Com o coração fibrilando, o lugar do passo é o coração, entre as pás.");
            StringAssert.Contains("pás", arrow.Instruction);
        }

        [Test]
        public void TodaEtapaTemFala_EmTodoNivel()
        {
            HashSet<string> clips = new HashSet<string>();
            foreach (AudioClip clip in Resources.LoadAll<AudioClip>("Voz")) { clips.Add(clip.name); }
            if (clips.Count == 0)
            {
                Assert.Fail("Assets/Resources/Voz está vazio: o estande fica mudo. Rode bash Tools/voz/gerar_vozes.sh.");
            }

            foreach (TransplantStage stage in new[]
                     {
                         TransplantStage.SkinIncision, TransplantStage.OpenChest, TransplantStage.OpenPericardium,
                         TransplantStage.GoOnBypass, TransplantStage.RemoveNativeHeart, TransplantStage.PlaceDonorHeart,
                         TransplantStage.ConnectVessels, TransplantStage.Restart, TransplantStage.CloseSkin,
                         TransplantStage.Complete,
                     })
            {
                string line = VoiceGuide.StageLine(stage);
                Assert.IsNotNull(line, $"{stage} sem fala.");
                Assert.IsTrue(clips.Contains(line), $"A fala '{line}' ({stage}) não tem arquivo em Resources/Voz.");
            }

            foreach (string extra in new[] { "equipe_bomba", "etapa_sair_bomba_facil", "fibrilacao", "desfibrilar_aqui", "desarejar", "boas_vindas" })
            {
                Assert.IsTrue(clips.Contains(extra), $"A fala '{extra}' não tem arquivo em Resources/Voz.");
            }
        }

        // ------------------------------------------------------------------ 5.3 altura real

        private VisitorFit Rig(float eyeHeight, EventSessionController session, out Transform head)
        {
            Transform rig = Spawn("XR Origin").transform;
            head = new GameObject("Camera").transform;
            head.SetParent(rig, false);
            head.localPosition = new Vector3(0.5f, eyeHeight, -0.3f);

            Transform stance = Spawn("Stance").transform;
            stance.SetPositionAndRotation(new Vector3(2f, 0f, 1f), Quaternion.LookRotation(Vector3.left));

            VisitorFit fit = rig.gameObject.AddComponent<VisitorFit>();
            fit.Bind(rig, head, stance, session);
            return fit;
        }

        [Test]
        public void CadaVisitanteNaPropriaAltura_EOOperadorCorrige2cmForaDaRodada()
        {
            EventSessionController session = Spawn("Sessao").AddComponent<EventSessionController>();
            session.Bind(EventSessionDefinition.Create(round: 150f), null);
            VisitorFit fit = Rig(1.72f, session, out Transform head);

            fit.Fit();
            Assert.AreEqual(1.72f, head.position.y, 1e-3f, "Um adulto de 1,72 m de olhos fica com 1,72 m.");

            Assert.IsTrue(fit.Nudge(1));
            Assert.AreEqual(1.74f, head.position.y, 1e-3f, "↑ sobe 2 cm.");
            Assert.IsTrue(fit.Nudge(-1));
            Assert.IsTrue(fit.Nudge(-1));
            Assert.AreEqual(1.70f, head.position.y, 1e-3f, "↓ desce 2 cm.");

            fit.Fit();
            Assert.AreEqual(1.70f, head.position.y, 1e-3f, "Recentralizar mantém a correção do operador.");

            // Uma vez nova é um visitante novo: começa da altura medida dele, sem a correção do anterior.
            session.BeginSession();
            Assert.AreEqual(0f, fit.OperatorOffset, "Visitante novo começa da altura medida dele.");
            Assert.AreEqual(1.72f, head.position.y, 1e-3f);

            Assert.IsTrue(fit.Nudge(1), "No briefing, antes do relógio, o operador ainda pode corrigir.");
            session.StartRound();
            Assert.IsFalse(fit.Nudge(1), "Com a rodada correndo a mesa não se mexe sob as mãos do visitante.");
            Assert.AreEqual(1.74f, head.position.y, 1e-3f);
        }

        [Test]
        public void ACriancaContinuaSendoLevantada()
        {
            VisitorFit fit = Rig(1.2f, null, out Transform head);
            fit.Fit();
            Assert.AreEqual(0.4f, fit.Lift, 1e-3f);
            Assert.AreEqual(1.6f, head.position.y, 1e-3f, "Modo baixinho: os olhos da criança onde estariam os de um adulto.");
        }
    }
}
