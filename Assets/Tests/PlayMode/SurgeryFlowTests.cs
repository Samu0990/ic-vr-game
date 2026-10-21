using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// A cirurgia inteira, na cena que o jogo realmente carrega.
    ///
    /// Existe porque os testes de procedimento montavam um TransplantProcedure num GameObject
    /// vazio, e assim nunca tocavam no que estava quebrado: o que o Editor constrói e o que o
    /// arquivo da cena guarda não eram a mesma coisa. Um campo que o Unity não serializa volta
    /// no valor padrão, e nenhum teste que constrói o objeto na mão enxerga isso.
    ///
    /// O sintoma era esse: na canulação o jogo travava e não dava para seguir. O nível Médio junta
    /// canular, clampear a aorta e dar a cardioplegia num gesto só, mas a corrente de passos não
    /// era gravada; ao abrir a cena sobrava só a âncora, que é o ÚLTIMO passo — cardioplegia. O
    /// procedimento continuava esperando a canulação, recusava, e recusava para sempre.
    /// </summary>
    public class SurgeryFlowTests
    {
        private const string SceneName = "TransplanteCardiaco";

        private TransplantProcedure _procedure;
        private BypassWorker _worker;
        private List<BypassSite> _sites;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return HeadlessScene.Load(SceneName);

            _procedure = Object.FindFirstObjectByType<TransplantProcedure>();
            _worker = Object.FindFirstObjectByType<BypassWorker>();
            Assert.IsNotNull(_procedure, "Sem TransplantProcedure na cena.");
            Assert.IsNotNull(_worker, "Sem BypassWorker na cena.");

            FieldInfo field = typeof(BypassWorker).GetField("sites",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _sites = field?.GetValue(_worker) as List<BypassSite>;
            Assert.IsNotNull(_sites, "Sem pontos de CEC na cena.");
            Assert.Greater(_sites.Count, 0);
        }

        [TearDown]
        public void TearDown() => SurgeryEvents.ResetAll();

        [UnityTest]
        public IEnumerator ACenaGuardaOGestoInteiro_NaoSoAAncora()
        {
            foreach (BypassSite site in _sites)
            {
                Assert.IsNotNull(site.Chain, $"'{site.Step}' veio da cena sem corrente de passos.");
                Assert.Greater(site.Chain.Length, 0,
                    $"'{site.Step}' veio da cena com a corrente vazia. É assim que a canulação " +
                    "some: sobra a âncora, que é o último passo do gesto, e o primeiro nunca " +
                    "chega a ser tentado.");

                Assert.AreEqual(site.Step, site.Chain[site.Chain.Length - 1],
                    "A âncora tem de ser o último passo da corrente.");
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator OPlanoVemDoNivelDaCena_NaoDoPadraoDaClasse()
        {
            // O campo começa em Difícil. Se o nível não fosse gravado, toda cena voltaria Difícil
            // e os gestos combinados dos níveis mais fáceis pediriam passos que o jogador já não
            // tem como executar.
            BypassPlan plan = _procedure.Plan;
            Assert.IsNotNull(plan);
            Assert.AreEqual(_sites.Count, plan.Gestures.Count,
                $"A cena tem {_sites.Count} gesto(s) de CEC e o plano carregado prevê " +
                $"{plan.Gestures.Count}. Os dois têm de descrever a mesma operação.");

            for (int i = 0; i < _sites.Count; i++)
            {
                Assert.AreEqual(plan.Gestures[i].Anchor, _sites[i].Step,
                    "Ponto e gesto fora de ordem entre a cena e o plano.");
            }

            yield break;
        }

        /// <summary>
        /// Da canulação ao último ponto da pele, usando só o que a cena oferece: cada gesto de
        /// CEC é executado pela corrente que está gravada nele, como o BypassWorker faz quando a
        /// mão fica parada em cima do ponto.
        /// </summary>
        [UnityTest]
        public IEnumerator DaParaOperarDoComecoAoFim()
        {
            _procedure.Begin();

            foreach (TransplantStage stage in new List<TransplantStage>(_procedure.Order))
            {
                switch (stage)
                {
                    case TransplantStage.GoOnBypass:
                        PerformGesture(0);
                        break;

                    case TransplantStage.ConnectVessels:
                        SewEveryVessel();
                        break;

                    case TransplantStage.Restart:
                        for (int i = 1; i < _sites.Count; i++) { PerformGesture(i); }
                        break;
                }

                // Algumas etapas se fecham sozinhas quando o gesto que as define termina — é o
                // caso dos vasos, que avançam no último ponto costurado. Nesses casos não há o
                // que concluir à mão, e exigir isso seria o teste cobrando uma formalidade que o
                // jogo, acertadamente, dispensa.
                if (_procedure.Stage == stage)
                {
                    Assert.IsTrue(_procedure.CompleteStage(stage),
                        $"A operação travou em {stage}. Estado atual: {_procedure.Stage}.");
                }
                else
                {
                    Assert.AreNotEqual(stage, _procedure.Stage,
                        $"A etapa {stage} não foi concluída nem avançou sozinha.");
                }
            }

            Assert.AreEqual(TransplantStage.Complete, _procedure.Stage,
                "Percorridas todas as etapas, a cirurgia tem de terminar concluída.");

            yield break;
        }

        /// <summary>
        /// Costura os cinco vasos pela via de verdade: a mão parada em cima de cada um até a
        /// anastomose fechar. Sem isto a bomba recusa o desclampe, e com razão — desclampar com
        /// vaso aberto esvazia o paciente dentro do tórax.
        /// </summary>
        private void SewEveryVessel()
        {
            VesselAnastomosis[] vessels =
                Object.FindObjectsByType<VesselAnastomosis>(FindObjectsSortMode.None);
            Assert.Greater(vessels.Length, 0, "Sem vasos para anastomosar na cena.");

            foreach (VesselAnastomosis vessel in vessels)
            {
                for (int step = 0; step < 600 && !vessel.IsJoined; step++)
                {
                    vessel.Work(vessel.transform.position, 0.1f);
                }

                Assert.IsTrue(vessel.IsJoined,
                    $"O vaso '{vessel.DisplayName}' não fechou nem com a mão parada em cima dele.");
            }

            Assert.IsTrue(_procedure.IsImplantComplete,
                $"Costurados {vessels.Length} vasos, o implante ainda não conta como completo " +
                $"({_procedure.VesselsConnected} contabilizados).");
        }

        /// <summary>
        /// Executa um gesto de CEC exatamente como o worker: os passos da corrente, um a um e na
        /// ordem. Se a corrente estiver vazia — o defeito que isto guarda — o primeiro passo
        /// nunca é tentado e a recusa aparece aqui.
        /// </summary>
        private void PerformGesture(int index)
        {
            BypassSite site = _sites[index];
            BypassStep[] chain = site.Chain != null && site.Chain.Length > 0
                ? site.Chain
                : new[] { site.Step };

            foreach (BypassStep step in chain)
            {
                BypassAttempt attempt = _procedure.Bypass.Attempt(step, _procedure.IsImplantComplete);
                Assert.IsTrue(attempt.Accepted,
                    $"A bomba recusou '{step}' no gesto '{site.Step}': {attempt.Reason}");
            }
        }
    }
}
