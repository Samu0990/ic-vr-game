using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// A seta que diz onde agir. O que importa: que ela aponte para o lugar da etapa em curso,
    /// que a frase embaixo seja uma instrução e não o nome do procedimento, e que ela suma
    /// quando não há nada a apontar — seta parada em cima de coisa já feita ensina a ignorar
    /// seta.
    /// </summary>
    public class NextStepArrowTests
    {
        private GameObject _host;
        private GameObject _arrow;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject g in new[] { _host, _arrow })
            {
                if (g != null) { Object.DestroyImmediate(g); }
            }

            SurgeryEvents.ResetAll();
        }

        private NextStepArrow Make(out TransplantProcedure procedure, out Transform midline,
            out Transform sick)
        {
            _host = new GameObject("Sistemas");
            procedure = _host.AddComponent<TransplantProcedure>();

            _arrow = new GameObject("Seta");
            TextMesh label = new GameObject("Frase").AddComponent<TextMesh>();
            label.transform.SetParent(_arrow.transform, false);

            midline = new GameObject("SternalMidline").transform;
            midline.SetParent(_host.transform, false);
            midline.localPosition = new Vector3(0f, 1.2f, 0f);

            sick = new GameObject("Heart").transform;
            sick.SetParent(_host.transform, false);
            sick.localPosition = new Vector3(0f, 1.15f, 0.1f);

            NextStepArrow arrow = _host.AddComponent<NextStepArrow>();
            arrow.Bind(procedure, null, new VesselAnastomosis[0], midline, sick, null, null,
                _arrow.transform, label);
            return arrow;
        }

        private static void Stage(TransplantProcedure procedure, TransplantStage stage)
        {
            FieldInfo field = typeof(TransplantProcedure).GetProperty("Stage")?.DeclaringType
                ?.GetField("<Stage>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Sem campo de apoio para Stage.");
            field.SetValue(procedure, stage);
        }

        [Test]
        public void ParadaAntesDeComecar_NaoApontaNada()
        {
            NextStepArrow arrow = Make(out TransplantProcedure procedure, out _, out _);
            Stage(procedure, TransplantStage.Idle);

            arrow.Tick(0.1f);

            Assert.IsNull(arrow.Target);
            Assert.IsEmpty(arrow.Instruction);
            Assert.IsFalse(_arrow.activeSelf, "Sem etapa, a seta fica apagada.");
        }

        [Test]
        public void NoCorteDaPele_ApontaALinhaDoEsterno()
        {
            NextStepArrow arrow = Make(out TransplantProcedure procedure, out Transform midline, out _);
            Stage(procedure, TransplantStage.SkinIncision);

            arrow.Tick(0.1f);

            Assert.AreSame(midline, arrow.Target);
            Assert.IsNotEmpty(arrow.Instruction);
            Assert.IsTrue(_arrow.activeSelf);
        }

        [Test]
        public void AoTirarOCoracao_ApontaOCoracaoDoente()
        {
            NextStepArrow arrow = Make(out TransplantProcedure procedure, out _, out Transform sick);
            Stage(procedure, TransplantStage.RemoveNativeHeart);

            arrow.Tick(0.1f);

            Assert.AreSame(sick, arrow.Target);
            StringAssert.Contains("solte", arrow.Instruction.ToLowerInvariant(),
                "Tirar o coração só conta quando ele é solto fora do peito, e a frase tem de " +
                "dizer isso — senão o visitante segura o órgão esperando algo acontecer.");
        }

        [Test]
        public void AsFrasesSaoInstrucoes_NaoNomesDeEtapa()
        {
            NextStepArrow arrow = Make(out TransplantProcedure procedure, out _, out _);

            foreach (TransplantStage stage in new[]
                     {
                         TransplantStage.SkinIncision, TransplantStage.OpenChest,
                         TransplantStage.RemoveNativeHeart, TransplantStage.CloseSkin,
                     })
            {
                Stage(procedure, stage);
                arrow.Tick(0.1f);

                Assert.IsNotEmpty(arrow.Instruction, $"Etapa {stage} ficou sem frase.");
                Assert.Less(arrow.Instruction.Length, 48,
                    $"'{arrow.Instruction}' é longa demais para ser lida de relance no meio da " +
                    "cirurgia.");
                StringAssert.DoesNotContain(stage.ToString(), arrow.Instruction,
                    "A frase tem de dizer o gesto, não repetir o nome interno da etapa.");
            }
        }

        [Test]
        public void SemAlvoParaAEtapa_ASetaSomeEmVezDeApontarOChao()
        {
            NextStepArrow arrow = Make(out TransplantProcedure procedure, out _, out _);

            // Sem bomba ligada, a etapa de CEC não tem para onde apontar. O erro a evitar é a
            // seta ficar na origem do mundo, debaixo da mesa, apontando para nada.
            Stage(procedure, TransplantStage.GoOnBypass);
            arrow.Tick(0.1f);

            Assert.IsNull(arrow.Target);
            Assert.IsFalse(_arrow.activeSelf);
        }
    }
}
