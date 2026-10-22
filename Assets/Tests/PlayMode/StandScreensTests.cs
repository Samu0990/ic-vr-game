using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.Surgery;
using VRSurgery.Transplant;
using VRSurgery.VR;

namespace VRSurgery.Tests
{
    /// <summary>
    /// As três telas do estande no PC: o monitor da fila (visão do cirurgião com tempo, pontos,
    /// etapa e ±segundos), o projetor no manequim (só o corpo, fundo preto, sem texto) e a
    /// calibração feita no local.
    /// </summary>
    public class StandScreensTests
    {
        private const string SceneName = "TransplanteCardiaco";

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

        // ------------------------------------------------------------------ projetor, na cena

        private static GameObject FindIncludingInactive(string name)
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name == name && t.parent == null) { return t.gameObject; }
            }

            return null;
        }

        /// <summary>
        /// Drawn by the projector: not on the layer its camera leaves out, and not switched off for
        /// it. Read from the layer rather than the camera's mask, which the headless runner blanks
        /// on every camera so nothing is drawn against the null graphics device.
        /// </summary>
        private static bool Projected(Camera camera, ProjectionView view, Renderer renderer) =>
            renderer.gameObject.layer != LayerMask.NameToLayer("OperatorOnly") && !view.Hides(renderer);

        [UnityTest]
        public IEnumerator OProjetor_MostraSoOPacienteEOQueAconteceNele()
        {
            yield return HeadlessScene.Load(SceneName);

            GameObject holder = GameObject.Find("ProjectionCamera");
            Assert.IsNotNull(holder);
            Camera camera = holder.GetComponent<Camera>();
            ProjectionView view = holder.GetComponent<ProjectionView>();
            Assert.IsNotNull(view, "A câmera de cima precisa saber o que não vai para o manequim.");

            foreach (string offProjector in new[] { "OperatingTable", "CubaRetirada", "MesasDeInstrumentos", "CentroCirurgico", "SetaProximoPasso", "TelaDeFim" })
            {
                GameObject root = FindIncludingInactive(offProjector);
                Assert.IsNotNull(root, $"A cena perdeu '{offProjector}'.");
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.IsFalse(Projected(camera, view, r), $"'{r.name}' ({offProjector}) aparece no manequim.");
                }
            }

            Renderer basin = GameObject.Find("DonorStand").transform.Find("Basin").GetComponent<Renderer>();
            Assert.IsFalse(Projected(camera, view, basin), "O suporte do doador não vai para o manequim.");

            foreach (TextMesh text in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Renderer r = text.GetComponent<Renderer>();
                Assert.IsFalse(Projected(camera, view, r), $"Texto '{text.text}' ({text.name}) aparece no manequim.");
            }

            foreach (Renderer r in GameObject.Find("Patient").GetComponentsInChildren<Renderer>(true))
            {
                Assert.IsTrue(Projected(camera, view, r), $"'{r.name}' é do corpo e tem de ir para o manequim.");
            }

            foreach (SurgicalInteractable item in Object.FindObjectsByType<SurgicalInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (Renderer r in item.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.GetComponent<TextMesh>() != null) { continue; }
                    Assert.IsTrue(Projected(camera, view, r), $"'{r.name}' ({item.name}) sobre o corpo tem de aparecer.");
                }
            }

            int gloves = 0;
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name != "LuvaEsquerda" && t.name != "LuvaDireita") { continue; }
                // The skinned glove itself; the poke pointer the hand moves onto the fingertip at
                // runtime is a rig gizmo and stays off the mannequin.
                foreach (SkinnedMeshRenderer r in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (r.name.StartsWith("Pinch_Pointer")) { continue; }
                    Assert.IsTrue(Projected(camera, view, r), $"A luva '{r.name}' sobre o corpo tem de aparecer.");
                    gloves++;
                }
            }

            Assert.Greater(gloves, 0, "A cena perdeu as luvas.");
        }

        [UnityTest]
        public IEnumerator OProjetor_EnquadraOTroncoComFundoPreto()
        {
            yield return HeadlessScene.Load(SceneName);

            Camera camera = GameObject.Find("ProjectionCamera").GetComponent<Camera>();
            Assert.IsTrue(camera.orthographic);
            Assert.AreEqual(CameraClearFlags.SolidColor, camera.clearFlags);
            Assert.AreEqual(Color.black, camera.backgroundColor);

            Transform seat = GameObject.Find("PericardialSeat").transform;
            Vector3 chest = camera.WorldToViewportPoint(seat.position);
            Assert.That(chest.x, Is.InRange(0.05f, 0.95f), "O tórax tem de estar no quadro.");
            Assert.That(chest.y, Is.InRange(0.05f, 0.95f), "O tórax tem de estar no quadro.");

            Transform basin = GameObject.Find("DonorStand").transform.Find("Basin");
            Vector3 stand = camera.WorldToViewportPoint(basin.position);
            bool inFrame = stand.x > 0f && stand.x < 1f && stand.y > 0f && stand.y < 1f;
            Assert.IsFalse(inFrame, "O suporte do doador fica fora do enquadramento do corpo.");
        }

        // ------------------------------------------------------------------ calibração

        [Test]
        public void ACalibracao_SalvaNoPCELeDeVolta()
        {
            string path = Path.Combine(Application.temporaryCachePath, "teste_calibracao_projetor.json");
            ProjectionCalibration saved = new ProjectionCalibration
            {
                offsetX = 0.12f, offsetY = -0.05f, scale = 1.3f, rotation = 4.5f, mirrorX = true,
            };
            saved.Save(path);

            ProjectionCalibration loaded = ProjectionCalibration.Load(path);
            File.Delete(path);

            Assert.AreEqual(0.12f, loaded.offsetX, 1e-5f);
            Assert.AreEqual(-0.05f, loaded.offsetY, 1e-5f);
            Assert.AreEqual(1.3f, loaded.scale, 1e-5f);
            Assert.AreEqual(4.5f, loaded.rotation, 1e-5f);
            Assert.IsTrue(loaded.mirrorX);
            Assert.IsFalse(loaded.mirrorY);

            Assert.AreEqual(1f, ProjectionCalibration.Load(path).scale,
                "Sem arquivo (primeira vez no local), a imagem sai sem ajuste nenhum.");
        }

        [UnityTest]
        public IEnumerator ACalibracao_MoveEscalaGiraEEspelhaAImagem_EPTrocaOMonitor()
        {
            Camera overhead = Spawn("CameraDeCima").AddComponent<Camera>();
            overhead.orthographic = true;

            // Nada a desenhar: o teste confere onde a imagem vai, não o que há nela — e uma câmera
            // que desenha a cena contra a placa de vídeo nula do modo headless derruba o Editor.
            overhead.cullingMask = 0;
            ProjectionOutput output = Spawn("Saida").AddComponent<ProjectionOutput>();
            output.Bind(overhead, null);
            yield return null;

            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.IsNotNull(overhead.targetTexture, "A câmera de cima desenha numa textura que vai para o projetor.");
            }
            RectTransform picture = output.transform.Find("ProjetorSaida/Imagem") as RectTransform;
            Assert.IsNotNull(picture);

            output.ToggleCalibration();
            Assert.IsTrue(output.Calibrating);
            Assert.IsTrue(ProjectionOutput.AnyCalibrating, "Calibrando, as setas são da calibração.");
            Assert.IsTrue(output.transform.Find("ProjetorSaida/Imagem/PadraoDeTeste").gameObject.activeSelf,
                "Calibrando, o padrão de teste aparece por cima.");

            output.Calibration.scale = 1.5f;
            output.Calibration.rotation = 10f;
            output.Calibration.mirrorX = true;
            output.Calibration.offsetX = 0.1f;
            output.Apply();

            Assert.AreEqual(-1.5f, picture.localScale.x, 1e-4f, "Espelhar em X inverte a imagem, na escala pedida.");
            Assert.AreEqual(1.5f, picture.localScale.y, 1e-4f);
            Assert.AreEqual(10f, picture.localEulerAngles.z, 1e-3f);
            Assert.Greater(picture.anchoredPosition.x, 0f);

            output.ToggleCalibration();
            Assert.IsFalse(ProjectionOutput.AnyCalibrating);

            Canvas canvas = output.GetComponentInChildren<Canvas>(true);
            output.SwapMonitor();
            Assert.IsTrue(output.OnMainMonitor);
            Assert.AreEqual(0, canvas.targetDisplay, "P põe a visão de cima no monitor principal.");
            Assert.IsTrue(canvas.enabled);
            output.SwapMonitor();
            Assert.IsFalse(output.OnMainMonitor);
        }

        // ------------------------------------------------------------------ monitor da fila

        [UnityTest]
        public IEnumerator OMonitorDaFila_MostraTempoPontosEtapaEOsSegundos()
        {
            GameObject systems = Spawn("Systems");
            TransplantProcedure procedure = systems.AddComponent<TransplantProcedure>();
            EventSessionController session = systems.AddComponent<EventSessionController>();
            session.Bind(EventSessionDefinition.Create(round: 180f), null);

            SpectatorHud hud = Spawn("Hud").AddComponent<SpectatorHud>();
            hud.Bind(session, procedure, null);
            yield return null;

            procedure.Begin();
            session.BeginSession();
            session.StartRound();
            hud.Tick(0.1f);

            StringAssert.StartsWith("Etapa 1 de", hud.StageLabel);
            StringAssert.Contains(StageText.Name(procedure.Stage), hud.StageLabel);

            hud.ShowAdjustment("+10 s · Esterno aberto", true);
            hud.ShowAdjustment("−5 s · Fora da linha", false);
            CollectionAssert.AreEqual(new[] { "+10 s · Esterno aberto", "−5 s · Fora da linha" }, hud.Feed);

            hud.Tick(10f);
            Assert.IsEmpty(hud.Feed, "Os ±segundos passam; o monitor não acumula a rodada inteira.");

            StringAssert.Contains("C ", SpectatorHud.HelpText);
            StringAssert.Contains("P ", SpectatorHud.HelpText);
            StringAssert.Contains("R ", SpectatorHud.HelpText);
            StringAssert.Contains("N ", SpectatorHud.HelpText);
        }
    }
}
