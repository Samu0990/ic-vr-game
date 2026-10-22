using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// A operação inteira nos três níveis, montada como o construtor monta a cena: a bomba com os
    /// gestos do nível, o desfibrilador só onde retirar o clampe é um gesto próprio, a ponte da
    /// rodada segurando o coração novo, o monitor lendo os dois corações.
    ///
    /// Existe porque a cena é gerada num nível só (Médio) e os testes de cena nunca viam os
    /// outros dois. Foi assim que o Fácil empacou na CEC e terminou com o coração novo parado sem
    /// ninguém perceber: os defeitos estavam justamente na combinação que só aquele nível tem —
    /// a equipe já pôs o paciente em bomba, e não existe desfibrilação.
    /// </summary>
    public class LevelFlowTests
    {
        private const float Step = 1f / 60f;

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

        private sealed class Rig
        {
            public TransplantProcedure Procedure;
            public BypassWorker Pump;
            public List<BypassSite> Sites;
            public Transform Tip;
            public Heartbeat Donor;
            public Heartbeat Native;
            public NativeHeartRhythm NativeRhythm;
            public DefibrillationWorker Defib;
            public Transform PaddleCentre;
            public VitalSignsMonitor Monitor;
            public TransplantRoundBridge Bridge;
        }

        /// <summary>The parts of WireProcedure that decide how the pump and the two hearts behave, per level.</summary>
        private Rig Build(SurgicalDifficulty level)
        {
            Rig rig = new Rig();
            GameObject systems = Spawn("Systems");
            rig.Procedure = systems.AddComponent<TransplantProcedure>();
            rig.Procedure.SetDifficulty(level);

            BypassPlan plan = BypassPlan.For(level);
            rig.Sites = new List<BypassSite>();
            bool ownUnclamp = false;
            for (int i = 0; i < plan.Gestures.Count; i++)
            {
                BypassGesture gesture = plan.Gestures[i];
                Transform point = Spawn("Bypass_" + gesture.Anchor).transform;
                point.position = new Vector3(i * 0.3f, 1f, 0f);
                rig.Sites.Add(new BypassSite
                {
                    Step = gesture.Anchor, Point = point, Radius = 0.028f, Seconds = gesture.Seconds,
                    Chain = gesture.Steps,
                });

                if (gesture.Steps.Length == 1 && gesture.Steps[0] == BypassStep.Unclamp) { ownUnclamp = true; }
            }

            rig.Tip = Spawn("SurgeonHandTip").transform;
            rig.Tip.position = new Vector3(0f, 5f, 0f);
            rig.Pump = systems.AddComponent<BypassWorker>();
            rig.Pump.Bind(rig.Tip, rig.Sites, rig.Procedure);

            GameObject donor = Spawn("DonorHeart");
            donor.transform.position = new Vector3(0f, 1f, 2f);
            rig.Donor = donor.AddComponent<Heartbeat>();

            rig.Native = Spawn("Heart").AddComponent<Heartbeat>();
            rig.NativeRhythm = systems.AddComponent<NativeHeartRhythm>();
            rig.NativeRhythm.Bind(rig.Procedure, rig.Native);

            if (ownUnclamp)
            {
                rig.PaddleCentre = Spawn("PaddleCentre").transform;
                rig.Defib = systems.AddComponent<DefibrillationWorker>();
                rig.Defib.Bind(rig.PaddleCentre, null, rig.Procedure, rig.Donor, donor.transform);
                rig.Defib.Configure(1f, 0f, 2);
                rig.Pump.AddGate(rig.Defib);
            }

            rig.Bridge = systems.AddComponent<TransplantRoundBridge>();
            rig.Bridge.Bind(null, rig.Procedure, null, null, new GrabbableOrgan[0], new VesselAnastomosis[0], rig.Donor);
            if (rig.Defib != null) { rig.Bridge.BindDefibrillation(rig.Defib); }

            rig.Monitor = systems.AddComponent<VitalSignsMonitor>();
            rig.Monitor.Bind(rig.Procedure, rig.Donor, null, null, null, null, null, null, null, null);
            rig.Monitor.BindNativeHeart(rig.Native);
            rig.Monitor.BindDefibrillation(rig.Defib);
            return rig;
        }

        /// <summary>Holds the hand on the site whose gesture starts with the step the pump is waiting for.</summary>
        private static void PerformNextGesture(Rig rig)
        {
            BypassStep expected = rig.Procedure.Bypass.NextStep;
            BypassSite site = rig.Sites.Find(s => (s.Chain != null && s.Chain.Length > 0 ? s.Chain[0] : s.Step) == expected);
            Assert.IsNotNull(site, $"Nenhum gesto começa por {expected} neste nível.");

            int before = rig.Procedure.Bypass.CompletedSteps;
            rig.Tip.position = site.Point.position;
            for (float t = 0f; t < site.Seconds + 1f && rig.Procedure.Bypass.CompletedSteps == before; t += Step)
            {
                rig.Pump.Tick(Step);
                if (rig.Defib != null) { rig.Defib.Tick(Step); }
            }

            rig.Tip.position = new Vector3(0f, 5f, 0f);
            Assert.Greater(rig.Procedure.Bypass.CompletedSteps, before,
                $"O gesto '{site.Step}' não fez nada: {rig.Pump.LastRefusal}");
        }

        /// <summary>The paddles on the new heart until it converts, as the visitor does.</summary>
        private static void Defibrillate(Rig rig)
        {
            if (rig.Defib == null) { return; }

            rig.Defib.Tick(Step);
            if (!rig.Defib.IsFibrillating) { return; }

            rig.PaddleCentre.position = rig.Donor.transform.position;
            for (float t = 0f; t < 10f && rig.Defib.IsFibrillating; t += Step) { rig.Defib.Tick(Step); }
            Assert.IsFalse(rig.Defib.IsFibrillating, "As pás no coração não reverteram a fibrilação.");
        }

        /// <summary>From the first cut to the last stitch, through what each level actually asks of the hands.</summary>
        private static void Operate(Rig rig)
        {
            TransplantProcedure procedure = rig.Procedure;
            procedure.Begin();

            for (int guard = 0; guard < 40 && procedure.Stage != TransplantStage.Complete; guard++)
            {
                TransplantStage stage = procedure.Stage;
                switch (stage)
                {
                    case TransplantStage.GoOnBypass:
                        PerformNextGesture(rig);
                        break;

                    case TransplantStage.ConnectVessels:
                        while (procedure.ConnectVessel()) { }
                        break;

                    case TransplantStage.Restart:
                        PerformNextGesture(rig);
                        Defibrillate(rig);
                        break;

                    default:
                        Assert.IsTrue(procedure.CompleteStage(stage), $"A etapa {stage} recusou ser concluída.");
                        break;
                }
            }

            Assert.AreEqual(TransplantStage.Complete, procedure.Stage, "A operação não chegou ao fim.");
        }

        [TestCase(SurgicalDifficulty.Facil)]
        [TestCase(SurgicalDifficulty.Medio)]
        [TestCase(SurgicalDifficulty.Dificil)]
        public void AEtapaDaBomba_NaoTravaEmNenhumNivel(SurgicalDifficulty level)
        {
            Rig rig = Build(level);
            rig.Procedure.Begin();
            Assert.IsTrue(rig.Procedure.CompleteStage(TransplantStage.OpenChest));

            if (level == SurgicalDifficulty.Facil)
            {
                // A equipe pôs o paciente em bomba antes do visitante chegar, e o único gesto do
                // Fácil é a SAÍDA de bomba, recusada antes do implante. Ninguém concluía a etapa.
                Assert.AreEqual(TransplantStage.RemoveNativeHeart, rig.Procedure.Stage,
                    "No Fácil o paciente já chega em bomba: a etapa da CEC tem de se concluir sozinha.");
                StringAssert.Contains("equipe já colocou o paciente em bomba", rig.Procedure.CurrentInstruction,
                    "E o visitante precisa ler por que ela passou sozinha.");
                return;
            }

            Assert.AreEqual(TransplantStage.GoOnBypass, rig.Procedure.Stage,
                "Onde o visitante entra em bomba, a etapa espera o gesto dele.");
            while (rig.Procedure.Stage == TransplantStage.GoOnBypass) { PerformNextGesture(rig); }
            Assert.AreEqual(TransplantStage.RemoveNativeHeart, rig.Procedure.Stage);
        }

        [Test]
        public void NoFacil_AEtapaFeitaPelaEquipeEAvisada_ESaiDoMonitorSozinha()
        {
            Rig rig = Build(SurgicalDifficulty.Facil);
            List<(TransplantStage stage, bool byTeam)> completed = new List<(TransplantStage, bool)>();
            rig.Procedure.StageCompleted += (stage, byTeam) => completed.Add((stage, byTeam));

            rig.Procedure.Begin();
            rig.Procedure.CompleteStage(TransplantStage.OpenChest);

            CollectionAssert.AreEqual(
                new[] { (TransplantStage.OpenChest, false), (TransplantStage.GoOnBypass, true) }, completed,
                "A CEC do Fácil conta como concluída pela equipe, não pelo visitante.");
            Assert.AreEqual(TransplantProcedure.TeamOnBypassNotice, rig.Procedure.CurrentInstruction);

            rig.Procedure.Tick(TransplantProcedure.NoticeSeconds + 0.1f);
            Assert.AreEqual("Retire o coração doente", rig.Procedure.CurrentInstruction,
                "Passados alguns segundos, a instrução volta a ser o que fazer agora.");
        }

        [Test]
        public void AUrgenciaDaFibrilacao_NaoSomeComOTempo()
        {
            Rig rig = Build(SurgicalDifficulty.Medio);
            rig.Procedure.Begin();
            rig.Procedure.SetUrgentInstruction("Fibrilação ventricular!");

            rig.Procedure.ShowNotice("um aviso qualquer");
            rig.Procedure.Tick(30f);

            Assert.AreEqual("Fibrilação ventricular!", rig.Procedure.CurrentInstruction,
                "A urgência da fibrilação continua até o choque; só os avisos passam sozinhos.");
        }

        [Test]
        public void OMonitor_NaoSegueCoracaoForaDoPaciente()
        {
            Rig rig = Build(SurgicalDifficulty.Medio);
            Transform seat = Spawn("PericardialSeat").transform;
            seat.position = rig.Native.transform.position;
            rig.Monitor.BindSeat(seat);
            rig.Procedure.Begin();

            rig.Native.Configure(120f, 0.03f, 0f);
            rig.Native.StartBeating();
            rig.Native.transform.position = seat.position + new Vector3(0.8f, 0f, 0f);
            for (int i = 0; i < 90; i++) { rig.Native.Tick(Step); rig.Monitor.Tick(Step); }
            rig.Monitor.Tick(1.1f);

            Assert.AreNotEqual(120, rig.Monitor.HeartRate,
                "Um coração batendo numa cuba não é a circulação do paciente; o monitor não o segue.");

            rig.Native.transform.position = seat.position;
            for (int i = 0; i < 90; i++) { rig.Native.Tick(Step); rig.Monitor.Tick(Step); }
            Assert.AreEqual(120, rig.Monitor.HeartRate, "No peito, sim.");
        }

        [TestCase(SurgicalDifficulty.Facil)]
        [TestCase(SurgicalDifficulty.Medio)]
        [TestCase(SurgicalDifficulty.Dificil)]
        public void NoFim_OCoracaoNovoBate_EOMonitorMostraOCoracaoNovo(SurgicalDifficulty level)
        {
            Rig rig = Build(level);

            Operate(rig);
            rig.Monitor.Tick(0.2f);

            Assert.IsTrue(rig.Donor.IsBeating,
                $"{level}: fora de bomba, o coração novo tem de bater — é o momento para o qual a operação existe.");
            Assert.AreEqual(VitalSignsMonitor.VitalsState.NewHeart, rig.Monitor.State,
                $"{level}: o monitor tem de mostrar o ritmo do coração novo, não o do doente.");
        }

        [TestCase(SurgicalDifficulty.Medio)]
        [TestCase(SurgicalDifficulty.Dificil)]
        public void OCoracaoDoente_NaoVoltaABaterDepoisDoDesclampe(SurgicalDifficulty level)
        {
            Rig rig = Build(level);
            TransplantProcedure procedure = rig.Procedure;
            procedure.Begin();

            rig.NativeRhythm.Tick();
            Assert.IsTrue(rig.Native.IsBeating, "Antes da bomba o coração doente bate, fraco e irregular.");

            Assert.IsTrue(procedure.Bypass.Attempt(BypassStep.Cannulate).Accepted);
            Assert.IsTrue(procedure.Bypass.Attempt(BypassStep.ClampAorta).Accepted);
            Assert.IsTrue(procedure.Bypass.Attempt(BypassStep.Cardioplegia).Accepted);
            rig.NativeRhythm.Tick();
            Assert.IsFalse(rig.Native.IsBeating, "A cardioplegia para o coração.");

            // Implantado o doador, o clampe sai: o sangue chega ao coração NOVO. O antigo já está
            // fora do corpo, na cuba, e não tem por que voltar a bater.
            Assert.IsTrue(procedure.Bypass.Attempt(BypassStep.Unclamp, true).Accepted);
            rig.NativeRhythm.Tick();
            Assert.IsFalse(rig.Native.IsBeating,
                "Depois da cardioplegia o coração doente não bate mais nesta rodada.");

            Assert.IsTrue(procedure.Bypass.Attempt(BypassStep.DeAir, true).Accepted);
            rig.NativeRhythm.Tick();
            Assert.IsFalse(rig.Native.IsBeating);
        }

        [Test]
        public void ODoenteVoltaABaterParaOProximoVisitante()
        {
            Rig rig = Build(SurgicalDifficulty.Medio);
            Operate(rig);
            rig.NativeRhythm.Tick();
            Assert.IsFalse(rig.Native.IsBeating);

            rig.Procedure.ResetProcedure();
            rig.Procedure.Begin();
            rig.NativeRhythm.Tick();
            Assert.IsTrue(rig.Native.IsBeating, "Paciente novo, coração doente batendo de novo.");
        }

        [Test]
        public void ODesfibrilador_SoFuncionaComOCoracaoNoAssento()
        {
            Rig rig = Build(SurgicalDifficulty.Medio);
            TransplantProcedure procedure = rig.Procedure;

            // O doador com o seu contrato de órgão e o assento no peito, como na cena.
            Transform seat = Spawn("PericardialSeat").transform;
            seat.position = new Vector3(0f, 1.1f, 0.45f);
            rig.Donor.gameObject.AddComponent<VRSurgery.Interaction.SurgicalInteractable>();
            GrabbableOrgan organ = rig.Donor.gameObject.AddComponent<GrabbableOrgan>();
            organ.Bind(OrganRole.Donor, seat, procedure);

            procedure.Begin();
            procedure.Bypass.Attempt(BypassStep.Cannulate);
            procedure.Bypass.Attempt(BypassStep.ClampAorta);
            procedure.Bypass.Attempt(BypassStep.Cardioplegia);
            Assert.IsTrue(procedure.Bypass.Attempt(BypassStep.Unclamp, true).Accepted);

            rig.Defib.Tick(Step);
            Assert.IsTrue(rig.Defib.IsFibrillating);

            // O coração está longe do peito (na bancada, na mão, no chão): as pás nele não podem
            // desfibrilar o paciente.
            rig.Donor.transform.position = seat.position + new Vector3(0.6f, 0f, 0f);
            rig.PaddleCentre.position = rig.Donor.transform.position;
            for (float t = 0f; t < 6f; t += Step) { rig.Defib.Tick(Step); }

            Assert.AreEqual(0, rig.Defib.Shocks, "Choque num coração fora do peito não trata o paciente.");
            Assert.IsTrue(rig.Defib.IsFibrillating);
        }
    }
}
