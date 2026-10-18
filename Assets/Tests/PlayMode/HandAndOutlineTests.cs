using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Data;
using VRSurgery.Interaction;
using VRSurgery.Surgery;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The gloved hands and the light-blue grab outline.
    ///
    /// The hands are driven on a small skeleton built here with the OpenXR joint names the real
    /// model uses, laid out like a hand resting palm down with the fingers along +Z: enough to
    /// check that a squeeze closes the right fingers toward the palm, that the model is turned
    /// to hold the controller's handle, and that nothing depends on how the model was authored.
    /// </summary>
    public class HandAndOutlineTests
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

        // ------------------------------------------------------------------ a hand to test on

        /// <summary>Palm down (facing -Y), fingers along +Z, thumb on the inside.</summary>
        private GameObject Skeleton(bool left, Transform parent = null)
        {
            string p = left ? "L_" : "R_";
            float side = left ? -1f : 1f; // a right hand's thumb and index are on -X
            GameObject hand = Spawn(left ? "LeftHand" : "RightHand");
            if (parent != null) { hand.transform.SetParent(parent, false); }

            Transform wrist = Joint(hand.transform, p + "Wrist", Vector3.zero);
            Joint(wrist, p + "Palm", new Vector3(0f, 0f, 0.05f));

            (string finger, float x)[] fingers =
            {
                ("Index", -0.025f), ("Middle", -0.008f), ("Ring", 0.01f), ("Little", 0.027f),
            };
            foreach ((string finger, float x) in fingers)
            {
                float fx = x * side;
                Transform meta = Joint(wrist, p + finger + "Metacarpal", new Vector3(fx * 0.4f, 0f, 0.02f));
                Transform proximal = Joint(meta, p + finger + "Proximal", new Vector3(fx, 0f, 0.09f));
                Transform middle = Joint(proximal, p + finger + "Intermediate", new Vector3(fx, 0f, 0.135f));
                Transform distal = Joint(middle, p + finger + "Distal", new Vector3(fx, 0f, 0.165f));
                Joint(distal, p + finger + "Tip", new Vector3(fx, 0f, 0.19f));
            }

            Transform tm = Joint(wrist, p + "ThumbMetacarpal", new Vector3(-0.02f * side, -0.01f, 0.03f));
            Transform tp = Joint(tm, p + "ThumbProximal", new Vector3(-0.045f * side, -0.01f, 0.06f));
            Transform td = Joint(tp, p + "ThumbDistal", new Vector3(-0.06f * side, -0.01f, 0.09f));
            Joint(td, p + "ThumbTip", new Vector3(-0.07f * side, -0.01f, 0.115f));
            return hand;
        }

        private static Transform Joint(Transform parent, string name, Vector3 worldPosition)
        {
            Transform joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.position = parent.root.TransformPoint(worldPosition);
            return joint;
        }

        private static Vector3 Local(Transform hand, string joint) =>
            hand.InverseTransformPoint(HandPoser.FindBone(hand, joint).position);

        private HandPoser Poser(bool left, out GameObject hand)
        {
            hand = Skeleton(left);
            HandPoser poser = hand.AddComponent<HandPoser>();
            poser.Bind(left);
            poser.Calibrate();
            return poser;
        }

        // ------------------------------------------------------------------ fingers

        [Test]
        public void ThePalmFacesDownOnAHandLyingPalmDown_LeftOrRight()
        {
            foreach (bool left in new[] { false, true })
            {
                GameObject hand = Skeleton(left);
                Vector3 normal = HandPoser.PalmNormal(
                    HandPoser.FindBone(hand.transform, "Wrist").position,
                    HandPoser.FindBone(hand.transform, "IndexProximal").position,
                    HandPoser.FindBone(hand.transform, "LittleProximal").position, left);

                Assert.Greater(Vector3.Dot(normal, Vector3.down), 0.99f, left ? "mão esquerda" : "mão direita");
            }
        }

        [Test]
        public void SqueezingTheGripClosesTheLastThreeFingersIntoThePalm_NotTheIndex()
        {
            HandPoser poser = Poser(false, out GameObject hand);
            Assert.IsTrue(poser.IsCalibrated);
            Assert.AreEqual(15, poser.JointCount, "Three joints on each of five digits.");

            float middleOpen = ToPalm(hand, "MiddleTip");
            Vector3 indexOpen = Local(hand.transform, "IndexTip");

            poser.Tick(0f, 1f, 0f);

            Assert.Less(ToPalm(hand, "MiddleTip"), middleOpen - 0.07f, "The middle fingertip closes into the palm,");
            Assert.Less(Local(hand.transform, "MiddleTip").y, -0.02f, "on the palm side, not the back of the hand.");
            Assert.Less(Vector3.Distance(Local(hand.transform, "IndexTip"), indexOpen), 0.001f, "The index waits for the trigger.");
        }

        /// <summary>From a fingertip to the middle of the palm.</summary>
        private static float ToPalm(GameObject hand, string tip) =>
            Vector3.Distance(Local(hand.transform, tip), Local(hand.transform, "Palm"));

        [Test]
        public void PullingTheTriggerBendsOnlyTheIndex()
        {
            HandPoser poser = Poser(false, out GameObject hand);
            Vector3 ringOpen = Local(hand.transform, "RingTip");
            float indexOpen = ToPalm(hand, "IndexTip");

            poser.Tick(0f, 0f, 1f);

            Assert.Less(ToPalm(hand, "IndexTip"), indexOpen - 0.05f);
            Assert.Less(Local(hand.transform, "IndexTip").y, -0.02f);
            Assert.Less(Vector3.Distance(Local(hand.transform, "RingTip"), ringOpen), 0.001f);
        }

        [Test]
        public void TheLeftHandCurlsTheSameWayMirrored()
        {
            HandPoser poser = Poser(true, out GameObject hand);
            Vector3 open = Local(hand.transform, "LittleTip");
            float openToPalm = ToPalm(hand, "LittleTip");

            poser.Tick(0f, 1f, 0f);

            Vector3 closed = Local(hand.transform, "LittleTip");
            Assert.Less(ToPalm(hand, "LittleTip"), openToPalm - 0.07f);
            Assert.Less(closed.y, -0.02f, "Toward the palm, not the back of the hand.");
            Assert.AreEqual(open.x, closed.x, 0.01f, "Bending, not swinging sideways.");
        }

        [Test]
        public void LettingGoTheHandRelaxesOverAFewFrames_NotInstantly()
        {
            HandPoser poser = Poser(false, out _);
            poser.Tick(0f, 1f, 1f);
            Assert.AreEqual(1f, poser.Curl(HandPoser.GripGroup), 1e-4f);

            poser.Tick(1f / 72f, 0f, 0f);
            float oneFrame = poser.Curl(HandPoser.GripGroup);
            Assert.Less(oneFrame, 1f);
            Assert.Greater(oneFrame, 0.5f, "Fingers follow the button, they do not snap.");

            for (int i = 0; i < 72; i++) { poser.Tick(1f / 72f, 0f, 0f); }
            Assert.Less(poser.Curl(HandPoser.GripGroup), 0.2f, "A second later the hand is relaxed again.");
            Assert.Greater(poser.Curl(HandPoser.GripGroup), 0.05f, "Relaxed, never flat.");
        }

        // ------------------------------------------------------------------ hand on the controller

        [Test]
        public void TheModelIsTurnedToWrapTheHandle_PalmInFingersDownPalmOffTheAxis()
        {
            foreach (bool left in new[] { false, true })
            {
                GameObject root = Spawn("Mao");
                root.transform.SetPositionAndRotation(new Vector3(0.3f, 1.1f, 0.2f), Quaternion.Euler(20f, 70f, -10f));
                GameObject model = Skeleton(left, root.transform);
                // Authored any old way: the alignment must not care.
                model.transform.localRotation = Quaternion.Euler(-90f, 35f, 12f);
                model.transform.localPosition = new Vector3(0.1f, -0.2f, 0.05f);

                ControllerHand hand = root.AddComponent<ControllerHand>();
                hand.enabled = false;
                hand.Bind(left, null, model.transform, null, null, null, null);
                hand.AlignModel();

                Vector3 w = Local(root.transform, "Wrist");
                Vector3 m = Local(root.transform, "MiddleProximal");
                Vector3 palm = HandPoser.PalmNormal(w, Local(root.transform, "IndexProximal"), Local(root.transform, "LittleProximal"), left);
                Vector3 fingers = (m - w).normalized;
                Vector3 wantPalm = left ? Vector3.right : Vector3.left;

                string which = left ? "esquerda" : "direita";
                Assert.Greater(Vector3.Dot(palm, wantPalm), 0.98f, $"Palma voltada para o cabo ({which}).");
                Assert.Greater(Vector3.Dot(fingers, Vector3.down), 0.98f, $"Dedos descendo para envolver o cabo ({which}).");

                Vector3 palmCentre = Local(root.transform, "Palm");
                Assert.Less(Vector3.Distance(palmCentre, -wantPalm * 0.025f), 0.001f, $"Palma a 2,5 cm do eixo do cabo ({which}).");

                // Doing it again changes nothing.
                hand.AlignModel();
                Assert.Less(Vector3.Distance(Local(root.transform, "Palm"), palmCentre), 1e-4f);
            }
        }

        /// <summary>
        /// O sintoma relatado: "agarrar os itens tá meio estranho, o bisturi é o mais estranho".
        ///
        /// O ponto de pega é filho da transform do controle, que segue a pose de APONTAR, mas a
        /// luva segue a pose de EMPUNHAR. Enquanto só a posição vinha da mão, a ferramenta
        /// recebia a direção da mira e ficava atravessada na luva que supostamente a segurava —
        /// uns 40 graus de diferença num Touch. Num bisturi, que é comprido e fino, isso é a
        /// diferença entre segurar e espetar.
        /// </summary>
        [Test]
        public void OInstrumentoFicaNaMao_NaoNaDirecaoEmQueOControleAponta()
        {
            foreach (bool left in new[] { false, true })
            {
                string which = left ? "esquerda" : "direita";

                // A mão na pose de empunhar, e o controle apontando para outro lado: é assim que
                // as duas poses chegam de um Touch.
                GameObject root = Spawn("Mao");
                root.transform.SetPositionAndRotation(new Vector3(0.2f, 1.05f, 0.1f), Quaternion.Euler(15f, 40f, 0f));
                GameObject controller = Spawn("Controle");
                controller.transform.SetPositionAndRotation(root.transform.position, Quaternion.Euler(60f, 40f, 0f));

                GameObject model = Skeleton(left, root.transform);
                Transform fist = new GameObject("PontoDePega").transform;
                fist.SetParent(controller.transform, false);

                ControllerHand hand = root.AddComponent<ControllerHand>();
                hand.enabled = false;
                hand.Bind(left, controller.transform, model.transform, null, null, fist, null);
                hand.AlignModel();
                hand.PlaceGrabPoint();

                Assert.Less(Vector3.Distance(fist.position, hand.FistPosition), 1e-4f,
                    $"O instrumento tem de ficar no punho ({which}).");

                // O eixo do cabo é +Z da mão, os dedos descem por -Y: a ferramenta sai por cima do
                // polegar e cai na direção dos dedos, que é como se pega uma caneta ou um bisturi.
                Vector3 nose = fist.forward;
                Assert.Greater(Vector3.Dot(nose, root.transform.forward), 0.3f,
                    $"A ponta tem de sair por cima do polegar, ao longo do cabo ({which}).");
                Assert.Less(Vector3.Dot(nose, root.transform.up), -0.3f,
                    $"A ponta tem de cair na direção dos dedos, e não ficar reta no ar ({which}).");
                Assert.Less(Mathf.Abs(Vector3.Dot(nose, root.transform.right)), 0.05f,
                    $"Nada de torcer o instrumento para o lado ({which}).");

                // O eixo X é a face da lâmina — é por ele que o SkinIncisionWorker sabe se o
                // bisturi está de pé. Inclinar a ferramenta não pode mexer nisso.
                Assert.Greater(Vector3.Dot(fist.right, root.transform.right), 0.999f,
                    $"A face da lâmina tem de continuar sendo o eixo da palma ({which}).");

                // E o controle pode apontar para onde quiser: quem manda na pega é a mão.
                Quaternion before = fist.rotation;
                controller.transform.rotation = Quaternion.Euler(-30f, 200f, 25f);
                hand.PlaceGrabPoint();
                Assert.Less(Quaternion.Angle(before, fist.rotation), 0.01f,
                    $"Girar o controle girou a ferramenta na mão parada ({which}).");
            }
        }

        // ------------------------------------------------------------------ outline

        private static Material OutlineMaterial()
        {
            Shader shader = Shader.Find("VRSurgery/GrabOutline");
            return new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
        }

        // --------------------------------------------- o cabo assentado onde os dedos fecham

        [Test]
        public void OCaboAssentaNoVaoDaMao_NaoNaOrigemDoControle()
        {
            GameObject root = Spawn("Mao");
            root.transform.SetPositionAndRotation(new Vector3(0.2f, 1.05f, 0.1f), Quaternion.Euler(15f, 40f, 0f));
            GameObject controller = Spawn("Controle");
            controller.transform.SetPositionAndRotation(root.transform.position, root.transform.rotation);

            GameObject model = Skeleton(false, root.transform);
            HandPoser poser = model.AddComponent<HandPoser>();
            poser.Bind(false);

            Transform fist = new GameObject("PontoDePega").transform;
            fist.SetParent(controller.transform, false);

            ControllerHand hand = root.AddComponent<ControllerHand>();
            hand.enabled = false;
            hand.Bind(false, controller.transform, model.transform, poser, null, fist, null);
            hand.AlignModel();

            // A origem da mão é o eixo do controle; o modelo foi deslocado para fora dele de
            // propósito. Assentar o instrumento na origem era o que o deixava boiando ao lado.
            float fromOrigin = Vector3.Distance(hand.FistPosition, root.transform.position);
            Assert.That(fromOrigin, Is.GreaterThan(0.005f),
                "O cabo tem de sair da origem do controle e ir para o vão da mão.");

            Transform palm = HandPoser.FindBone(model.transform, "Palm");
            Assert.That(Vector3.Distance(hand.FistPosition, palm.position), Is.LessThan(0.09f),
                "E tem de parar perto da palma, não em qualquer lugar.");
        }

        [Test]
        public void CadaDedoFechaOQuantoPrecisa_NaoTodosOMesmoTanto()
        {
            GameObject hand = Skeleton(false);

            // The generic fixture originally authored every fingertip at exactly z=0.19, so
            // middle and little were geometrically the same length along this test's handle
            // axis. That contradicted the assertion below and forced any correct solver to
            // return the same closure. Shorten the little finger like the real hand rig before
            // calibration so this actually exercises different finger lengths.
            Vector3 shorten = hand.transform.forward;
            HandPoser.FindBone(hand.transform, "LittleIntermediate").position -= shorten * 0.005f;
            HandPoser.FindBone(hand.transform, "LittleDistal").position -= shorten * 0.004f;
            HandPoser.FindBone(hand.transform, "LittleTip").position -= shorten * 0.003f;

            HandPoser poser = hand.AddComponent<HandPoser>();
            poser.Bind(false);
            poser.Calibrate();

            // Um cabo alcançável passando rente à palma. O esqueleto mínimo desta fixture é
            // mais comprido e abre mais do que a mão do jogo; 4 cm coloca a superfície do cabo
            // no trecho de aproximação dos dois dedos que o teste quer comparar.
            Transform palm = HandPoser.FindBone(hand.transform, "Palm");
            Vector3 origin = palm.position;
            Vector3 axis = hand.transform.right;

            float middle = poser.SolveClosure(HandPoser.Middle, origin, axis, 0.04f, out float _);
            float little = poser.SolveClosure(HandPoser.Little, origin, axis, 0.04f, out float _);

            Assert.That(Mathf.Abs(middle - little), Is.GreaterThan(0.01f),
                "Dedos de comprimentos diferentes não alcançam o mesmo cabo no mesmo fechamento.");
        }

        [Test]
        public void OSolverParaNaPrimeiraAproximacao_NaoFechaAtravesDoCabo()
        {
            HandPoser poser = Poser(false, out GameObject hand);
            Transform palm = HandPoser.FindBone(hand.transform, "Palm");
            Vector3 origin = palm.position;
            Vector3 axis = hand.transform.right;
            const float radius = 0.008f;

            float solved = poser.SolveClosure(HandPoser.Middle, origin, axis, radius, out float error);

            poser.SetFingerImmediate(HandPoser.Middle, solved);
            float atSolved = DistanceToAxis(poser.TipPosition(HandPoser.Middle), origin, axis);

            poser.SetFingerImmediate(HandPoser.Middle, 1f);
            float atFull = DistanceToAxis(poser.TipPosition(HandPoser.Middle), origin, axis);

            Assert.That(atSolved, Is.LessThanOrEqualTo(atFull + 1e-4f),
                "Fechar até o fim passa do cabo e sai do outro lado; o solver não pode escolher esse ramo.");
            Assert.That(error, Is.LessThan(1f));
        }

        [Test]
        public void OsDedosPodemTerFechamentosDiferentesAoMesmoTempo()
        {
            HandPoser poser = Poser(false, out GameObject _);
            float[] closures = { 0.1f, 0.9f, 0.5f, 0.3f, 0.7f };

            poser.TickFingers(1f, closures);

            Assert.That(poser.FingerCurl(HandPoser.Thumb), Is.EqualTo(0.1f).Within(0.02f));
            Assert.That(poser.FingerCurl(HandPoser.Index), Is.EqualTo(0.9f).Within(0.02f));
            Assert.That(poser.FingerCurl(HandPoser.Little), Is.EqualTo(0.7f).Within(0.02f));
        }

        private static float DistanceToAxis(Vector3 point, Vector3 origin, Vector3 direction)
        {
            Vector3 v = point - origin;
            return (v - Vector3.Project(v, direction.normalized)).magnitude;
        }

        // ------------------------------------------------- a pega em volta do que esta na mao

        [Test]
        public void SegurarUmInstrumentoNaoFechaAMaoNumPunho()
        {
            HandPoser poser = Poser(false, out GameObject _);
            HandHold scalpel = new HandHold
            {
                gripClosure = 0.58f, indexClosure = 0.22f, thumbClosure = 0.5f, pitchDegrees = 40f,
            };

            // Um frame longo para chegar no alvo sem iterar.
            poser.TickTo(1f, scalpel.indexClosure, scalpel.gripClosure, scalpel.thumbClosure);

            Assert.That(poser.Curl(HandPoser.GripGroup), Is.EqualTo(0.58f).Within(0.02f),
                "Os dedos tem que parar no cabo, nao fechar um punho.");
            Assert.That(poser.Curl(HandPoser.GripGroup), Is.LessThan(0.95f),
                "Um punho cheio atravessa uma haste de 6 mm.");
            Assert.That(poser.Curl(HandPoser.IndexGroup), Is.LessThan(0.4f),
                "O indice fica ao longo da haste, quase reto.");
        }

        [Test]
        public void OIndiceAindaRespondeAoGatilhoComOInstrumentoNaMao()
        {
            HandPoser poser = Poser(false, out GameObject _);
            const float rest = 0.22f;

            poser.TickTo(1f, Mathf.Lerp(rest, 1f, 0f), 0.58f, 0.5f);
            float off = poser.Curl(HandPoser.IndexGroup);

            poser.TickTo(1f, Mathf.Lerp(rest, 1f, 1f), 0.58f, 0.5f);
            float on = poser.Curl(HandPoser.IndexGroup);

            Assert.That(off, Is.EqualTo(rest).Within(0.02f));
            Assert.That(on, Is.GreaterThan(0.9f), "Puxar o gatilho ainda tem que dobrar o indice.");
        }

        [Test]
        public void CadaInstrumentoUsaAPegaQueEleMesmoDeclara()
        {
            HandHold forceps = new HandHold
            {
                gripClosure = 0.5f, indexClosure = 0.3f, thumbClosure = 0.42f, pitchDegrees = 32f,
            };
            ToolDefinition definition = ToolDefinition.Create(
                "forceps", "Pinca", default, default, null, forceps);

            GameObject tool = Spawn("Pinca");
            tool.AddComponent<Rigidbody>();
            SurgicalInteractable instrument = tool.AddComponent<SurgicalInteractable>();
            typeof(SurgicalInteractable)
                .GetField("toolDefinition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(instrument, definition);

            HandHold chosen = ControllerHand.HoldFor(instrument, HandHold.Default);

            Assert.That(chosen.pitchDegrees, Is.EqualTo(32f).Within(0.01f),
                "A pinca inclina no proprio angulo, nao nos 40 graus globais do bisturi.");
            Assert.That(chosen.gripClosure, Is.EqualTo(0.5f).Within(0.01f));
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void SemPegaDeclarada_CaiNaPadraoEmVezDoPunhoCheio()
        {
            HandHold fallback = HandHold.Default;

            HandHold nothingHeld = ControllerHand.HoldFor(null, fallback);

            Assert.That(nothingHeld.gripClosure, Is.EqualTo(fallback.gripClosure).Within(0.001f));
            Assert.That(nothingHeld.gripClosure, Is.LessThan(1f), "A padrao nunca e um punho cheio.");
        }

        [Test]
        public void UmaPegaTodaZeroContaComoNaoDeclarada()
        {
            // Importa porque a Unity le um struct ausente do .asset como tudo zero, e uma pega
            // toda zero significaria segurar o instrumento com a palma aberta.
            Assert.That(default(HandHold).IsConfigured, Is.False);
            Assert.That(HandHold.Default.IsConfigured, Is.True);
        }

        [Test]
        public void PegaDePrecisaoMantemAnelarEMinimoForaDoCabo()
        {
            HandHold scalpel = new HandHold
            {
                gripClosure = 0.52f,
                indexClosure = 0.42f,
                thumbClosure = 0.78f,
                handleRadius = 0.007f,
                pitchDegrees = 14f,
                precisionPinch = true,
                seatOffset = new Vector3(0.006f, 0.004f, 0.001f),
            };

            Assert.IsTrue(scalpel.precisionPinch);
            Assert.That(scalpel.gripClosure, Is.LessThan(0.6f),
                "Anelar e minimo devem ficar recolhidos, sem serem resolvidos contra o bisturi.");
            Assert.That(scalpel.seatOffset.magnitude, Is.LessThan(0.01f),
                "A correcao posiciona o cabo dentro da pinca; nao pode esconder um erro com um salto grande.");
        }

        [Test]
        public void TheOutlineTracesTheSolidPartsOnly_AndCostsNothingUntilShown()
        {
            GameObject tool = Spawn("Instrumento");
            GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.transform.SetParent(tool.transform, false);
            Object.DestroyImmediate(blade.GetComponent<Collider>());

            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.transform.SetParent(tool.transform, false);
            Material glass = new Material(glow.GetComponent<Renderer>().sharedMaterial) { renderQueue = 3000 };
            glow.GetComponent<Renderer>().sharedMaterial = glass;

            GameObject label = new GameObject("Rotulo");
            label.transform.SetParent(tool.transform, false);
            label.AddComponent<TextMesh>().text = "BISTURI";

            InteractableOutline outline = tool.AddComponent<InteractableOutline>();
            Assert.AreEqual(0, outline.ShellCount, "Nothing is built before anyone reaches for it.");

            Material material = OutlineMaterial();
            outline.Show(true, material);

            Assert.AreEqual(1, outline.ShellCount, "The blade, not the glow nor the label.");
            Transform shell = blade.transform.Find(InteractableOutline.ShellName);
            Assert.IsNotNull(shell);
            Renderer rim = shell.GetComponent<Renderer>();
            Assert.IsTrue(rim.enabled);
            Assert.AreSame(material, rim.sharedMaterial);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, rim.shadowCastingMode);
            Assert.AreSame(blade.GetComponent<MeshFilter>().sharedMesh, shell.GetComponent<MeshFilter>().sharedMesh);

            outline.Show(false, material);
            Assert.IsFalse(rim.enabled);

            outline.Show(true, material);
            Assert.AreEqual(1, outline.ShellCount, "Built once, then only switched.");
        }

        [Test]
        public void OnlyWhatTheHandsWouldTakeIsOutlined_AndTheRestGoesDark()
        {
            GrabOutlines outlines = Spawn("Contornos").AddComponent<GrabOutlines>();
            outlines.Bind(OutlineMaterial(), null);

            InteractableOutline Make(string name)
            {
                GameObject tool = Spawn(name);
                GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.transform.SetParent(tool.transform, false);
                return GrabOutlines.OutlineOf(tool);
            }

            InteractableOutline scalpel = Make("Bisturi");
            InteractableOutline forceps = Make("Pinca");
            InteractableOutline holder = Make("PortaAgulha");

            outlines.Apply(new[] { scalpel, forceps });
            Assert.IsTrue(scalpel.IsShown);
            Assert.IsTrue(forceps.IsShown);
            Assert.IsFalse(holder.IsShown);

            // The left hand took the forceps; the right moved on to the needle holder.
            outlines.Apply(new[] { holder });
            Assert.IsFalse(scalpel.IsShown);
            Assert.IsFalse(forceps.IsShown);
            Assert.IsTrue(holder.IsShown);
            Assert.AreEqual(1, outlines.Shown.Count);

            outlines.Apply(new InteractableOutline[0]);
            Assert.IsFalse(holder.IsShown);
            Assert.AreSame(GrabOutlines.OutlineOf(holder.gameObject), holder, "One outline per object, reused.");
        }
    }
}
