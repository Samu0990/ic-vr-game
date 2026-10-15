using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// O piloto que faz a incisão sozinho, para a cena de demonstração.
    ///
    /// O que estes testes protegem não é o piloto: é o fato de a demonstração valer alguma coisa.
    /// Uma demonstração que desenhasse a ferida por conta própria mostraria sempre um corte
    /// perfeito e não diria nada sobre o jogo. Então o que se cobra aqui é que quem julga
    /// continue sendo o <see cref="SkinIncisionWorker"/>: sem a mão segurando, nada é cortado; com
    /// a lâmina torta, o jogo reclama e a nota cai; e o traço que o piloto faz é um que o jogo
    /// aceita — mesma velocidade mínima, mesma regra do fio, mesma tolerância de profundidade que
    /// o visitante enfrenta.
    ///
    /// Tudo em passos fixos, num retalho de pele plano, sem cena e sem óculos.
    /// </summary>
    public class IncisionAutopilotTests
    {
        private const float Step = 1f / 60f;
        private const float SkinY = 1f;

        /// <summary>Teto de segurança para os laços: 30 s a 60 Hz.</summary>
        private const int MaxSteps = 1800;

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

        private static MeshFilter Filter(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            return go.GetComponent<MeshFilter>();
        }

        /// <summary>Um retalho plano de 20 x 30 cm a 1 m do chão, como nos SkinStagesTests.</summary>
        private ChestSkinPatch FlatPatch()
        {
            const int columns = 6, rows = 11;
            GameObject root = Spawn("Patch");
            float[] heights = new float[rows * (columns * 2 - 1)];
            for (int i = 0; i < heights.Length; i++) { heights[i] = SkinY; }

            ChestSkinPatch patch = root.AddComponent<ChestSkinPatch>();
            patch.Bind(columns, rows, 0.10f, 0.15f, heights,
                Filter("L", root.transform), Filter("R", root.transform),
                Filter("WL", root.transform), Filter("WR", root.transform), new[] { Spawn("Cavity") });
            return patch;
        }

        /// <summary>
        /// Um bisturi de verdade para o piloto: interactable, corpo rígido e uma ponta filha, que
        /// é o que o worker lê. Deitado na "bandeja" a meio metro da pele, para a aproximação ter
        /// um caminho a percorrer.
        /// </summary>
        private SurgicalInteractable Scalpel(out Transform tip)
        {
            GameObject tool = Spawn("Bisturi");
            tool.transform.position = new Vector3(0.5f, SkinY, -0.3f);
            Rigidbody body = tool.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            GameObject tipObject = new GameObject("BladeTip");
            tipObject.transform.SetParent(tool.transform, false);
            tipObject.transform.localPosition = new Vector3(0f, 0f, 0.055f);
            tip = tipObject.transform;

            return tool.AddComponent<SurgicalInteractable>();
        }

        private TransplantProcedure SkinProcedure()
        {
            TransplantProcedure procedure = Spawn("Procedure").AddComponent<TransplantProcedure>();
            procedure.SetSkinStages(true);
            procedure.Begin();
            return procedure;
        }

        private struct Bench
        {
            public IncisionAutopilot Pilot;
            public SkinIncisionWorker Worker;
            public ChestSkinPatch Patch;
            public SurgicalInteractable Scalpel;
        }

        private Bench Build(float drift = 0f)
        {
            ChestSkinPatch patch = FlatPatch();
            TransplantProcedure procedure = SkinProcedure();
            SurgicalInteractable scalpel = Scalpel(out Transform tip);

            SkinIncisionWorker worker = Spawn("Incision").AddComponent<SkinIncisionWorker>();
            worker.Bind(tip, scalpel, patch, procedure, null, null, null);

            IncisionAutopilot pilot = Spawn("Piloto").AddComponent<IncisionAutopilot>();
            pilot.Bind(scalpel.transform, tip, scalpel, worker, patch, procedure);
            pilot.SetDrift(drift);
            pilot.DriveWorker(true);

            return new Bench { Pilot = pilot, Worker = worker, Patch = patch, Scalpel = scalpel };
        }

        /// <summary>Roda o piloto até ele parar, avançando a pele no mesmo passo.</summary>
        private static int RunToEnd(Bench bench)
        {
            int steps = 0;
            while (bench.Pilot.Current != IncisionAutopilot.Phase.Pronto && steps < MaxSteps)
            {
                bench.Pilot.Tick(Step);
                bench.Patch.Tick(Step);
                steps++;
            }

            Assert.Less(steps, MaxSteps, "O piloto não terminou; travou em " + bench.Pilot.Current);
            return steps;
        }

        [Test]
        public void OPilotoFazAIncisaoInteiraPelasRegrasDoJogo()
        {
            Bench bench = Build();

            RunToEnd(bench);

            Assert.IsTrue(bench.Worker.IsComplete,
                $"O piloto passou a lâmina e o jogo só deu {bench.Worker.Progress01 * 100f:F0}% da linha " +
                "por cortada. Ou o traço não obedece às regras do corte, ou o corte quebrou.");
        }

        [Test]
        public void ELaminaNoFio_SemPrecisarDaAjudaAdaptativa()
        {
            Bench bench = Build();

            RunToEnd(bench);

            Assert.IsFalse(bench.Worker.EdgeRuleRelaxed,
                "O corte só saiu porque a ajuda adaptativa desistiu de cobrar o fio depois de três " +
                "segundos de recusa. Uma demonstração assim mostra o jogo cedendo, não funcionando: " +
                "o piloto tem de segurar a lâmina de pé.");
        }

        [Test]
        public void UmaPassadaSo_ETresEstrelas()
        {
            Bench bench = Build();

            RunToEnd(bench);

            Assert.IsTrue(bench.Pilot.HasGrade, "A incisão terminou e o worker não emitiu nota.");
            Assert.AreEqual(1, bench.Worker.Strokes, "A incisão do livro é uma passada só.");
            Assert.AreEqual(0, bench.Worker.Scratches, "Uma passada na linha não deixa arranhão nenhum.");
            Assert.AreEqual(3, bench.Pilot.Grade.Stars,
                $"Traço reto, uma passada, na profundidade certa, e a nota foi {bench.Pilot.Grade.Score}/100 " +
                $"({bench.Pilot.Grade.Detail}).");
            Assert.IsFalse(bench.Pilot.Grade.TooDeep, "A lâmina do piloto chegou ao esterno.");
        }

        [Test]
        public void OWorkerSoContaComOBisturiNaMao()
        {
            Bench bench = Build();

            Assert.IsFalse(bench.Scalpel.IsHeld, "O piloto já estava segurando antes de começar.");

            // Um pedaço da aproximação basta para ele pegar a ferramenta.
            for (int i = 0; i < 120; i++) { bench.Pilot.Tick(Step); bench.Patch.Tick(Step); }

            Assert.IsTrue(bench.Scalpel.IsHeld,
                "O piloto moveu a lâmina sem segurar o bisturi. O SkinIncisionWorker ignora uma " +
                "lâmina solta, então uma demonstração assim cortaria no vazio.");

            RunToEnd(bench);
            Assert.IsFalse(bench.Scalpel.IsHeld, "No fim o piloto tem de largar a ferramenta.");
        }

        [Test]
        public void ComALaminaForaDaLinha_OJogoReclamaEANotaCai()
        {
            // 2,5 cm de desvio: mais que a tolerância lateral de 14 mm, e perto o bastante da
            // pele para o worker contar como erro em vez de ignorar como "está noutro lugar".
            Bench bench = Build(drift: 0.025f);

            int deviations = 0;
            bench.Worker.Deviated += () => deviations++;

            RunToEnd(bench);

            Assert.Greater(deviations, 0,
                "A lâmina correu 2,5 cm fora da linha média e o jogo não reclamou uma vez.");
            Assert.Greater(bench.Worker.Scratches, 0,
                "Um corte fora da linha tem de deixar arranhão na pele.");
            Assert.IsFalse(bench.Worker.IsComplete,
                "Uma passada inteira fora da linha não pode valer como incisão feita.");
        }

        [Test]
        public void DaParaRodarDeNovoSemRemontarACena()
        {
            Bench bench = Build();
            RunToEnd(bench);
            Assert.IsTrue(bench.Worker.IsComplete);

            bench.Pilot.Restart();

            Assert.AreEqual(IncisionAutopilot.Phase.Esperando, bench.Pilot.Current);
            Assert.IsFalse(bench.Worker.IsComplete, "O Restart deixou a ferida do corte anterior.");
            Assert.AreEqual(0, bench.Worker.CutCount);

            RunToEnd(bench);
            Assert.IsTrue(bench.Worker.IsComplete, "A segunda passada não cortou.");
        }
    }
}
