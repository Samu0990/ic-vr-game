using System.Collections;
using System.Collections.Generic;
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
    /// O menu inicial, como nos jogos de VR: fora da vez o visitante fica numa sala escura com a
    /// logo e o botão INICIAR; o botão leva para a sala de cirurgia e começa a vez; no fim da vez
    /// o menu volta. A sala de cirurgia não é tocada.
    /// </summary>
    public class StartMenuTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private EventSessionController _session;
        private StartMenu _menu;
        private VisitorFit _fit;
        private Transform _head;
        private Transform _surgeon;
        private Transform _menuStance;
        private Transform _button;
        private Transform _hand;
        private GameObject _room;
        private bool _fog;

        [SetUp]
        public void SetUp() => _fog = RenderSettings.fog;

        [TearDown]
        public void TearDown()
        {
            RenderSettings.fog = _fog;
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

        private void Bench(bool headset)
        {
            // Built inactive so the definition is in place before Awake reads the round length.
            GameObject host = Spawn("Sessao");
            host.SetActive(false);
            _session = host.AddComponent<EventSessionController>();
            _session.Bind(EventSessionDefinition.Create(round: 150f, resultHold: 4f, scoreboardHold: 6f), null);
            host.SetActive(true);

            Transform rig = Spawn("XR Origin").transform;
            _head = new GameObject("Camera").transform;
            _head.SetParent(rig, false);
            _head.localPosition = new Vector3(0f, 1.7f, 0f);

            _surgeon = Spawn("PosicaoCirurgiao").transform;
            _surgeon.SetPositionAndRotation(new Vector3(2f, 0f, 1f), Quaternion.LookRotation(Vector3.left));

            _fit = rig.gameObject.AddComponent<VisitorFit>();
            _fit.Bind(rig, _head, _surgeon, _session);

            GameObject menuRoot = Spawn("MenuInicial");
            menuRoot.transform.position = new Vector3(400f, 0f, 0f);
            _room = new GameObject("SalaDoMenu");
            _room.transform.SetParent(menuRoot.transform, false);
            _room.SetActive(false);
            _menuStance = new GameObject("PosicaoMenu").transform;
            _menuStance.SetParent(menuRoot.transform, false);

            Transform layout = new GameObject("LogoEBotao").transform;
            layout.SetParent(_room.transform, false);
            Transform logo = new GameObject("Logo").transform;
            logo.SetParent(layout, false);
            logo.localPosition = new Vector3(0f, 0.15f, 2.4f);
            _button = new GameObject("BotaoIniciar").transform;
            _button.SetParent(layout, false);
            _button.localPosition = new Vector3(0f, -0.40f, 0.46f);
            _button.localRotation = Quaternion.LookRotation(_button.localPosition.normalized, Vector3.up);

            // Desligada: o teste move a mão na mão, sem o Update dela atrás.
            GameObject handObject = Spawn("Mao");
            ControllerHand hand = handObject.AddComponent<ControllerHand>();
            hand.enabled = false;
            _hand = handObject.transform;
            _hand.position = new Vector3(-50f, 0f, 0f);

            _menu = Spawn("Menu").AddComponent<StartMenu>();
            _menu.Bind(_session, _fit, _head, hand);
            _menu.BindRoom(_room, _menuStance, layout, logo, null);
            _menu.BindButton(_button, new Vector2(0.34f, 0.106f), null, null, null);
            _menu.ForceTravel = headset;
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.05f) { _menu.Tick(0.05f); }
        }

        private static void AssertOver(Transform head, Transform stance, string because)
        {
            Assert.AreEqual(stance.position.x, head.position.x, 1e-3f, because);
            Assert.AreEqual(stance.position.z, head.position.z, 1e-3f, because);
        }

        [Test]
        public void NaEspera_OVisitanteVaiParaASalaDoMenu_EOBotaoComecaAVezNaSalaDeCirurgia()
        {
            RenderSettings.fog = true;
            Bench(headset: true);

            Run(0.5f);
            Assert.IsTrue(_menu.IsOpen, "Fora da vez, o menu é a tela.");
            Assert.IsTrue(_menu.InRoom);
            Assert.IsTrue(_room.activeSelf, "A sala do menu aparece.");
            AssertOver(_head, _menuStance, "O visitante fica de pé no anel da sala do menu.");
            Assert.AreEqual(SessionState.Attract, _session.State, "Ninguém começa a vez sozinho.");
            Assert.IsFalse(RenderSettings.fog, "Sem a neblina cinza da sala de cirurgia: o céu do menu fica escuro.");

            _hand.position = _button.position;
            Run(3f);

            Assert.AreEqual(1, _menu.Starts);
            Assert.AreEqual(SessionState.Briefing, _session.State, "INICIAR começa a vez, como o botão de recomeçar.");
            Assert.IsFalse(_menu.IsOpen);
            Assert.IsFalse(_room.activeSelf, "A sala do menu some.");
            Assert.IsNull(_fit.StanceOverride);
            AssertOver(_head, _surgeon, "De volta ao lugar do cirurgião, de frente para o paciente.");
            Assert.AreEqual(0f, _menu.Fade01, 1e-3f, "A transição clareia de novo.");
            Assert.IsTrue(RenderSettings.fog, "A sala de cirurgia volta com a neblina que tinha.");
        }

        [Test]
        public void NoFimDaVez_OMenuVolta()
        {
            Bench(headset: true);
            Run(0.5f);
            _menu.StartTurn();
            Run(1f);
            Assert.AreEqual(SessionState.Briefing, _session.State);

            _session.StartRound();
            SurgeryEvents.RaiseSurgeryCompleted();
            Run(1f);
            Assert.IsFalse(_menu.IsOpen, "O resultado ainda é mostrado na sala de cirurgia.");

            for (int i = 0; i < 18; i++) { _session.Tick(0.25f); }
            Assert.AreEqual(SessionState.Scoreboard, _session.State);
            Run(1f);

            Assert.IsTrue(_menu.IsOpen, "Acabou a vez: de volta ao menu.");
            AssertOver(_head, _menuStance, "Na sala do menu.");
        }

        [Test]
        public void UmaMaoQuePassaPeloBotao_NaoComecaAVez()
        {
            Bench(headset: true);
            Run(2f);

            _hand.position = _button.position;
            Run(0.2f);
            Assert.Greater(_menu.Charge, 0f, "Encostou: o botão começa a carregar.");
            _hand.position = new Vector3(-50f, 0f, 0f);
            Run(1f);

            Assert.AreEqual(0, _menu.Starts, "Um toque de passagem não começa ninguém.");
            Assert.AreEqual(0f, _menu.Charge, 1e-3f);
            Assert.AreEqual(SessionState.Attract, _session.State);
        }

        [Test]
        public void AMaoQueJaEstaNoBotaoQuandoOMenuAbre_NaoComecaAVez()
        {
            Bench(headset: true);
            Run(0.05f);
            _hand.position = _button.position;
            Run(0.6f);

            Assert.AreEqual(0, _menu.Starts, "Uma mão que por acaso está onde o botão aparece não é uma escolha.");
        }

        [Test]
        public void SemOculos_NinguemEMovido_EOOperadorComecaComI()
        {
            Bench(headset: false);
            Vector3 before = _head.position;

            Run(0.5f);
            Assert.IsTrue(_menu.IsOpen);
            Assert.IsFalse(_menu.InRoom, "Sem óculos rodando, ninguém é levado para a sala do menu.");
            Assert.IsFalse(_room.activeSelf);
            Assert.IsNull(_fit.StanceOverride);
            Assert.AreEqual(before, _head.position);

            _menu.StartTurn();
            Run(0.2f);
            Assert.AreEqual(SessionState.Briefing, _session.State, "A tecla I começa a vez.");
        }

        [Test]
        public void ComOMenuAberto_RecentralizarFicaNaSalaDoMenu()
        {
            Bench(headset: true);
            Run(0.5f);

            _head.localPosition = new Vector3(0.6f, 1.7f, -0.4f);
            _fit.Fit();

            AssertOver(_head, _menuStance, "R e o óculos posto ajustam o visitante ao menu enquanto ele está aberto.");
        }

        [UnityTest]
        public IEnumerator ACena_TemOMenuComALogoEOBotao_LongeDaSalaDeCirurgia_ENaoComecaSozinha()
        {
            yield return HeadlessScene.Load("TransplanteCardiaco");

            StartMenu menu = Object.FindFirstObjectByType<StartMenu>();
            Assert.IsNotNull(menu, "Sem menu inicial na cena.");

            GameObject root = GameObject.Find("MenuInicial");
            Assert.IsNotNull(root);
            GameObject surgeon = GameObject.Find("PosicaoCirurgiao");
            Assert.Greater(Vector3.Distance(root.transform.position, surgeon.transform.position), 100f,
                "A sala do menu fica longe: nada dela se vê da sala de cirurgia, nem o contrário.");

            Renderer logo = null;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Logo") { logo = r; }
            }

            Assert.IsNotNull(logo, "Sem a logo no menu.");
            Assert.AreEqual("MenuLogo", logo.sharedMaterial.mainTexture != null ? logo.sharedMaterial.mainTexture.name : null);
            Assert.IsNotNull(root.transform.Find("SalaDoMenu/LogoEBotao/BotaoIniciar/Rotulo"), "Sem o botão INICIAR.");

            EventSessionController session = Object.FindFirstObjectByType<EventSessionController>();
            TransplantRoundBridge bridge = Object.FindFirstObjectByType<TransplantRoundBridge>();
            bridge.Tick(30f);
            Assert.AreEqual(SessionState.Attract, session.State, "Quem oferece a vez agora é o botão do menu, não um temporizador.");
        }
    }
}
