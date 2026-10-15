using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Transplant;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// A cena de demonstração: a mesma sala do transplante, com o bisturi sendo puxado sozinho
    /// pela linha média.
    ///
    /// Existe porque o corte é a parte do jogo mais difícil de conferir sem óculos. Ele depende
    /// de onde a ponta está, de que ângulo a lâmina faz com a pele, de quão rápido ela anda — três
    /// coisas que ninguém consegue reproduzir à mão arrastando objetos na Hierarchy, e que também
    /// não aparecem numa foto estática da cena.
    ///
    /// Duas maneiras de ver:
    ///
    /// <b>Play</b> — <c>VRSurgery/Demonstração — construir cena do corte automático</c> grava
    /// <c>DemoCorteAutomatico.unity</c>. Abra e aperte Play: a câmera já está enquadrando o tórax,
    /// o piloto pega o bisturi e corta, e o texto no canto mostra o que o SkinIncisionWorker está
    /// enxergando quadro a quadro — se a lâmina está no fio, quanto da linha já foi, a nota no fim.
    ///
    /// <b>Fotos</b> — <c>Demonstração — fotografar o corte</c> roda o mesmo piloto passo a passo
    /// sem entrar em Play e grava a sequência em PNG, mais um resumo em texto. Funciona em
    /// batchmode, que é como isto é verificado numa máquina sem óculos:
    ///
    /// <code>
    /// ~/Unity/Hub/Editor/6000.3.22f1/Editor/Unity -batchmode -quit -projectPath . \
    ///   -executeMethod VRSurgery.EditorTools.CorteAutomaticoDemo.RunFromCommandLine -out Captures/corte
    /// </code>
    /// </summary>
    public static class CorteAutomaticoDemo
    {
        private const string SourceScene = "Assets/Scenes/TransplanteCardiaco.unity";
        private const string DemoScene = "Assets/Scenes/DemoCorteAutomatico.unity";

        /// <summary>Passo fixo do piloto nas fotos: 60 Hz, o mesmo do óculos.</summary>
        private const float Step = 1f / 60f;

        /// <summary>Teto de segurança: 40 s de corte a 60 Hz. Um piloto travado para aqui.</summary>
        private const int MaxSteps = 2400;

        private const int ShotWidth = 1400;
        private const int ShotHeight = 900;

        [MenuItem("VRSurgery/Demonstração — construir cena do corte automático")]
        public static void BuildMenu()
        {
            Build();
            EditorUtility.DisplayDialog("Corte automático",
                "Cena gravada em " + DemoScene + ".\n\nAbra e aperte Play: o bisturi corta sozinho e o " +
                "texto no canto mostra o que o jogo está julgando.", "OK");
        }

        [MenuItem("VRSurgery/Demonstração — fotografar o corte")]
        public static void CaptureMenu() => Capture("Captures/corte-automatico");

        public static void RunFromCommandLine()
        {
            string output = "Captures/corte-automatico";
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-out") { output = args[i + 1]; }
            }

            try
            {
                Build();
                Capture(output);
                Debug.Log("[Corte] CORTE_DEMO_OK");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[Corte] CORTE_DEMO_FAILED " + exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Monta a cena de demonstração a partir da cena real. Nada é remontado do zero: se o
        /// bisturi mudar de forma ou a pele de lugar, a demonstração acompanha, porque ela lê a
        /// cena em vez de repetir as coordenadas dela.
        /// </summary>
        public static void Build()
        {
            EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);

            Rig rig = ReadRig();
            IncisionAutopilot pilot = new GameObject("PilotoDoCorte").AddComponent<IncisionAutopilot>();
            pilot.Bind(rig.Scalpel, rig.BladeTip, rig.Held, rig.Incision, rig.Patch, rig.Procedure);

            BuildDemoCamera(rig);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), DemoScene, true);
            Debug.Log($"[Corte] demonstração gravada em {DemoScene}: bisturi '{rig.Scalpel.name}', " +
                      $"incisão de {rig.Patch.IncisionLength * 100f:F0} cm sobre a linha média");
        }

        /// <summary>
        /// Roda o piloto passo a passo e fotografa. Sem Play: o piloto e o worker recebem o
        /// mesmo delta na mão, e o Editor renderiza entre um passo e outro.
        /// </summary>
        public static void Capture(string folder)
        {
            if (EditorSceneManager.GetActiveScene().path != DemoScene)
            {
                EditorSceneManager.OpenScene(DemoScene, OpenSceneMode.Single);
            }

            Directory.CreateDirectory(folder);

            IncisionAutopilot pilot = Object.FindFirstObjectByType<IncisionAutopilot>();
            if (pilot == null) { throw new System.InvalidOperationException("Cena sem PilotoDoCorte."); }

            Rig rig = ReadRig();
            string settled = SettleAsIfPlayHadJustStarted(rig);

            // Fora do Play ninguém chama o Update do worker nem o da pele: quem avança as duas
            // coisas aqui é este laço, com o mesmo delta que move a lâmina.
            pilot.DriveWorker(true);
            IncisionBleeding bleeding = Object.FindFirstObjectByType<IncisionBleeding>(FindObjectsInactive.Include);

            Camera camera = SetUpCaptureCamera(rig);
            StringBuilder log = new StringBuilder();
            log.AppendLine("Corte automático — o que o SkinIncisionWorker registrou");
            log.AppendLine($"Incisão: {rig.Patch.IncisionLength * 100f:F1} cm, {rig.Incision.Bins} trechos");
            log.AppendLine($"Regra do fio exigida: {(rig.Incision.RequiresEdge ? "sim" : "não")}");
            log.AppendLine(settled);
            log.AppendLine();
            log.AppendLine("t(s)  fase          linha  fio  prof.  passadas");

            int shots = 0;
            int steps = 0;
            float sinceShot = float.PositiveInfinity;
            float sinceLine = float.PositiveInfinity;

            while (pilot.Current != IncisionAutopilot.Phase.Pronto && steps < MaxSteps)
            {
                bool wasComplete = rig.Incision.IsComplete;
                pilot.Tick(Step);

                // O instante exato em que o worker dá a incisão por feita, antes de a pele
                // receber o passo que começa a abri-la. É o único quadro em que o traço aparece
                // como traço, e é a pergunta que a demonstração existe para responder: um quadro
                // depois a ferida já está se escancarando para a esternotomia e some.
                if (!wasComplete && rig.Incision.IsComplete)
                {
                    ShootAround(camera, rig, folder, "corte_recem_feito");
                }

                rig.Patch.Tick(Step);
                if (bleeding != null) { bleeding.Tick(Step); }
                steps++;
                sinceShot += Step;
                sinceLine += Step;

                // Uma foto a cada terço de segundo durante a passada, mais esparsas fora dela:
                // o que interessa ver quadro a quadro é a lâmina abrindo a pele.
                float gap = pilot.Current == IncisionAutopilot.Phase.Cortando ? 0.33f : 0.5f;
                if (sinceShot >= gap)
                {
                    sinceShot = 0f;
                    Shoot(camera, Path.Combine(folder, $"passo_{shots:00}.png"));
                    shots++;
                }

                if (sinceLine >= 0.25f)
                {
                    sinceLine = 0f;
                    log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,5:F2}  {1,-12}  {2,4:F0}%  {3,-3}  {4,4:F0}%  {5}",
                        steps * Step, pilot.Current, rig.Incision.Progress01 * 100f,
                        rig.Incision.BladeOnEdge ? "sim" : "não",
                        rig.Incision.CutDepth01 * 100f, rig.Incision.Strokes));
                }
            }

            // E o mesmo depois, com a ferida já aberta: é assim que o visitante a deixa para a
            // esternotomia.
            ShootAround(camera, rig, folder, "final_aberto");

            log.AppendLine();
            log.AppendLine(Verdict(rig, pilot, steps));
            File.WriteAllText(Path.Combine(folder, "resumo.txt"), log.ToString());

            Object.DestroyImmediate(camera.gameObject);
            Debug.Log($"[Corte] {shots + 6} fotos e resumo.txt em {folder}\n{Verdict(rig, pilot, steps)}");
        }

        /// <summary>A ferida de três ângulos: de cima, de lado e de perto, do lado do cirurgião.</summary>
        private static void ShootAround(Camera camera, Rig rig, string folder, string prefix)
        {
            Aim(camera, rig, 0.30f, new Vector3(0.30f, 0.90f, 0.10f));
            Shoot(camera, Path.Combine(folder, prefix + "_de_cima.png"));
            Aim(camera, rig, 0.26f, new Vector3(0.80f, 0.50f, 0.20f));
            Shoot(camera, Path.Combine(folder, prefix + "_de_lado.png"));
            Aim(camera, rig, 0.17f, new Vector3(0.45f, 0.72f, -0.52f));
            Shoot(camera, Path.Combine(folder, prefix + "_perto.png"));
        }

        /// <summary>
        /// Põe a cena no estado em que o Play a deixaria no primeiro quadro.
        ///
        /// Fora do Play, Awake não roda — e é no Awake que metade destas peças se escondem. Sem
        /// isto, a foto mostra o afastador de Finochietto estacionado sobre um tórax ainda
        /// fechado e o pericárdio à mostra através da pele, e quem olhasse acharia que o jogo
        /// tem um defeito que ele não tem. Uma foto de diagnóstico que inventa defeito é pior
        /// que nenhuma, então o que foi acertado aqui vai escrito no resumo.
        /// </summary>
        private static string SettleAsIfPlayHadJustStarted(Rig rig)
        {
            List<string> done = new List<string>();

            rig.Patch.Rebuild(0f);
            done.Add("pele fechada");

            foreach (SternotomyController sternum in
                     Object.FindObjectsByType<SternotomyController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                sternum.ResetClosed();
                done.Add("esterno fechado, vísceras escondidas");
            }

            foreach (SternalRetractor retractor in
                     Object.FindObjectsByType<SternalRetractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                retractor.Apply(0f);
                done.Add("afastador recolhido");
            }

            foreach (ToolPresenter presenter in
                     Object.FindObjectsByType<ToolPresenter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                presenter.Withdraw();
                done.Add("pás fora da bandeja");
            }

            return "Cena assentada como no primeiro quadro do Play: " + string.Join(", ", done) + ".";
        }

        /// <summary>O veredito em texto: o que o worker aceitou e o que reprovou.</summary>
        private static string Verdict(Rig rig, IncisionAutopilot pilot, int steps)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine($"Terminou em {steps * Step:F1} s na fase {pilot.Current}.");
            text.AppendLine($"Linha cortada: {rig.Incision.CutCount}/{rig.Incision.Bins} trechos " +
                            $"({rig.Incision.Progress01 * 100f:F0}%).");
            text.AppendLine($"Incisão dada por completa: {(rig.Incision.IsComplete ? "sim" : "NÃO")}.");
            text.AppendLine($"Passadas: {rig.Incision.Strokes}.  Arranhões fora da linha: {rig.Incision.Scratches}.");
            text.AppendLine($"Ajuda adaptativa acionada: {(rig.Incision.EdgeRuleRelaxed ? "SIM — a lâmina foi recusada por 3 s" : "não")}.");

            if (pilot.HasGrade)
            {
                IncisionGrade grade = pilot.Grade;
                text.AppendLine($"Nota: {grade.Title} — {grade.Score}/100, {grade.Stars} estrela(s). {grade.Detail}");
            }
            else
            {
                text.AppendLine("Nota: o worker não emitiu nota — a incisão não chegou ao fim.");
            }

            // Onde a lâmina passou, trecho a trecho: um desenho da ferida em texto.
            StringBuilder map = new StringBuilder();
            for (int bin = 0; bin < rig.Incision.Bins; bin++)
            {
                map.Append(!rig.Incision.IsBinCut(bin) ? '.' : rig.Incision.IsBinDeep(bin) ? '#' : '=');
            }

            text.AppendLine($"Trechos (pés → cabeça, '=' cortado, '#' fundo demais, '.' intacto):");
            text.AppendLine("  " + map);
            return text.ToString();
        }

        // ---------------------------------------------------------------- cena

        /// <summary>
        /// A câmera que fica gravada na cena, para quem apertar Play. Profundidade alta: a câmera
        /// do óculos continua lá, e sem óculos ela olharia para onde o visitante estaria de pé.
        /// </summary>
        private static void BuildDemoCamera(Rig rig)
        {
            GameObject go = GameObject.Find("CameraDemo");
            if (go == null) { go = new GameObject("CameraDemo"); }

            Camera camera = go.GetComponent<Camera>();
            if (camera == null) { camera = go.AddComponent<Camera>(); }

            camera.depth = 100f;
            camera.fieldOfView = 38f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 60f;
            Aim(camera, rig, 0.34f, new Vector3(0.62f, 0.66f, -0.05f));
        }

        private static Camera SetUpCaptureCamera(Rig rig)
        {
            GameObject holder = new GameObject("CameraDaFoto");
            Camera camera = holder.AddComponent<Camera>();
            camera.fieldOfView = 38f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 60f;
            Aim(camera, rig, 0.34f, new Vector3(0.62f, 0.66f, -0.05f));
            return camera;
        }

        /// <summary>
        /// Aponta a câmera para o meio da incisão. A distância sai da trigonometria do campo de
        /// visão: pedir "tantos centímetros de altura na tela" dá o mesmo enquadramento se a
        /// abertura da lente mudar, o que um múltiplo do tamanho não dá.
        /// </summary>
        private static void Aim(Camera camera, Rig rig, float span, Vector3 direction)
        {
            Vector3 look = rig.Patch.IncisionPoint(0.5f, 0f, 0f);
            float reach = span * 0.5f / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 from = look + direction.normalized * reach;
            camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation((look - from).normalized, Vector3.up));
        }

        private static void Shoot(Camera camera, string path)
        {
            RenderTexture target = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
            };

            camera.targetTexture = target;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D shot = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
            shot.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(path, shot.EncodeToPNG());

            camera.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(shot);
        }

        // ---------------------------------------------------------------- leitura da cena

        private struct Rig
        {
            public SkinIncisionWorker Incision;
            public ChestSkinPatch Patch;
            public TransplantProcedure Procedure;
            public Transform Scalpel;
            public Transform BladeTip;
            public SurgicalInteractable Held;
        }

        /// <summary>
        /// Descobre as peças pelas ligações que o construtor já fez, e não por nome. O worker
        /// aponta para a lâmina, para a pele e para o procedimento; perguntar a ele é o que
        /// garante que a demonstração está movendo exatamente o bisturi que o jogo julga.
        /// </summary>
        private static Rig ReadRig()
        {
            SkinIncisionWorker worker = Object.FindFirstObjectByType<SkinIncisionWorker>(FindObjectsInactive.Include);
            if (worker == null)
            {
                throw new System.InvalidOperationException(
                    "Cena sem SkinIncisionWorker: reconstrua com VRSurgery/Transplante — nível Médio.");
            }

            SerializedObject wired = new SerializedObject(worker);
            Transform tip = Read<Transform>(wired, "bladeTip");
            SurgicalInteractable held = Read<SurgicalInteractable>(wired, "scalpel");
            ChestSkinPatch patch = Read<ChestSkinPatch>(wired, "patch");
            TransplantProcedure procedure = Read<TransplantProcedure>(wired, "procedure");

            if (tip == null || held == null || patch == null)
            {
                throw new System.InvalidOperationException(
                    $"SkinIncisionWorker incompleto: lâmina={tip}, bisturi={held}, pele={patch}.");
            }

            return new Rig
            {
                Incision = worker,
                Patch = patch,
                Procedure = procedure,
                Scalpel = held.transform,
                BladeTip = tip,
                Held = held,
            };
        }

        private static T Read<T>(SerializedObject target, string field) where T : Object
        {
            SerializedProperty property = target.FindProperty(field);
            return property != null ? property.objectReferenceValue as T : null;
        }
    }
}
