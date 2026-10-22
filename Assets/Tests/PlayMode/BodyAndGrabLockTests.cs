using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// O corpo como volume (o que está escondido dentro dele, o que entrou nele pela mão) e a
    /// trava do coração doente, montados à mão para cada regra ficar visível sozinha.
    /// </summary>
    public class BodyAndGrabLockTests
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

        /// <summary>
        /// A flat-topped patient: skin at 1.20 m over x in [-0.2, 0.2], z in [-1, 1], on a table
        /// at 0.95 m; a 15 x 25 cm window at the origin, the open chest's floor at 1.07 m.
        /// </summary>
        private PatientBodyVolume Body()
        {
            const float cell = 0.02f;
            int columns = 20, rows = 100;
            float[] heights = new float[columns * rows];
            for (int i = 0; i < heights.Length; i++) { heights[i] = 1.20f; }

            PatientBodyVolume body = Spawn("Body").AddComponent<PatientBodyVolume>();
            body.Bake(new Vector2(-0.2f, -1f), cell, columns, rows, heights, 0.95f,
                Vector2.zero, new Vector2(0.075f, 0.125f), 1.07f);
            return body;
        }

        // ------------------------------------------------------------------ 1.8 / 1.9 o volume

        [Test]
        public void DentroDoCorpoFechado_EstaEscondido_NoToraxAbertoNao()
        {
            PatientBodyVolume body = Body();

            Assert.IsTrue(body.Hides(new Vector3(0f, 1.00f, -0.40f)), "No abdome, na mesa, sob a pele: perdido.");
            Assert.IsTrue(body.Hides(new Vector3(0f, 1.00f, 0f)),
                "Abaixo do fundo do tórax aberto (pele fechada, sem fundo): também perdido.");
            Assert.IsFalse(body.Hides(new Vector3(0f, 1.10f, 0f)), "Em cima do fundo do tórax aberto: no peito, à vista.");
            Assert.IsFalse(body.Hides(new Vector3(0f, 1.30f, -0.40f)), "Acima da pele: fora do corpo.");
            Assert.IsFalse(body.Hides(new Vector3(0.5f, 1.00f, 0f)), "Fora do corpo, ao lado da mesa.");
        }

        [Test]
        public void ObjetoSeguradoQueEntraNoCorpoForaDaJanela_ViraVermelhoEVibra()
        {
            PatientBodyVolume body = Body();
            GameObject heart = Spawn("Heart");
            heart.AddComponent<BoxCollider>().size = Vector3.one * 0.1f;
            SurgicalInteractable held = heart.AddComponent<SurgicalInteractable>();

            BodyIntrusionWarning warning = Spawn("Aviso").AddComponent<BodyIntrusionWarning>();
            warning.Bind(body, new[] { held }, null);

            held.OnGrabbed(null);
            heart.transform.position = new Vector3(0f, 1.10f, 0f);
            warning.Tick(0.1f);
            Assert.IsFalse(warning.IsWarning(held), "Dentro da janela, no peito aberto: é onde ele deve estar.");

            heart.transform.position = new Vector3(0f, 1.05f, -0.40f);
            warning.Tick(0.1f);
            Assert.IsTrue(warning.IsWarning(held), "Atravessando o abdome com o coração na mão: aviso.");

            heart.transform.position = new Vector3(0f, 1.40f, -0.40f);
            warning.Tick(0.1f);
            Assert.IsFalse(warning.IsWarning(held), "Saiu do corpo, o aviso some.");

            heart.transform.position = new Vector3(0f, 1.05f, -0.40f);
            held.OnReleased();
            warning.Tick(0.1f);
            Assert.IsFalse(warning.IsWarning(held), "Solto, não é a mão que está errando: quem cuida é a recuperação.");
        }

        // ------------------------------------------------------------------ 1.3 uma vez por tentativa

        [Test]
        public void ATravaDoCoracaoDoente_AvisaUmaVezPorTentativa()
        {
            TransplantProcedure procedure = Spawn("Sistemas").AddComponent<TransplantProcedure>();
            procedure.SetDifficulty(SurgicalDifficulty.Medio);
            procedure.Begin();
            Assert.IsTrue(procedure.CompleteStage(TransplantStage.OpenChest));
            Assert.AreEqual(TransplantStage.GoOnBypass, procedure.Stage);

            GameObject heart = Spawn("Heart");
            heart.AddComponent<SurgicalInteractable>();
            GrabbableOrgan organ = heart.AddComponent<GrabbableOrgan>();
            organ.Bind(OrganRole.Native, Spawn("Assento").transform, procedure);

            List<string> refusals = new List<string>();
            organ.GrabRefused += (_, reason) => refusals.Add(reason);

            Assert.IsFalse(organ.ConsiderGrab(false), "Fora da hora ele não sai, nem para o contorno de pega.");
            Assert.AreEqual(0, refusals.Count, "Só a mão passando perto não é tentativa.");

            Assert.IsFalse(organ.ConsiderGrab(true));
            Assert.IsFalse(organ.ConsiderGrab(true));
            Assert.IsFalse(organ.ConsiderGrab(true));
            Assert.AreEqual(1, refusals.Count, "Segurar o gatilho apertado é uma tentativa só.");
            StringAssert.Contains("ainda não pode sair", procedure.CurrentInstruction);

            organ.ConsiderGrab(false);
            organ.ConsiderGrab(true);
            Assert.AreEqual(2, refusals.Count, "Soltou e tentou de novo: avisa de novo.");
        }
    }
}
