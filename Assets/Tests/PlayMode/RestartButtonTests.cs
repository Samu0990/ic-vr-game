using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.Surgery;

namespace VRSurgery.Tests
{
    /// <summary>
    /// O botão de recomeçar. O que importa: que encoste não baste, que o toque de raspão não
    /// acumule, e que uma mão parada em cima dele não dispare uma rodada atrás da outra.
    /// </summary>
    public class RestartButtonTests
    {
        private GameObject _button;
        private GameObject _tip;
        private GameObject _sessionObject;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject g in new[] { _button, _tip, _sessionObject })
            {
                if (g != null) { Object.DestroyImmediate(g); }
            }

            SurgeryEvents.ResetAll();
        }

        private RestartButton Make(out Transform tip, out EventSessionController session)
        {
            _button = new GameObject("BotaoRecomecar");
            _button.transform.position = Vector3.zero;

            _tip = new GameObject("Mao");
            // Desligada: o teste move a mão na mão, sem o Update dela atrás.
            ControllerHand hand = _tip.AddComponent<ControllerHand>();
            hand.enabled = false;
            tip = _tip.transform;

            _sessionObject = new GameObject("Sessao");
            session = _sessionObject.AddComponent<EventSessionController>();

            // O botão só existe depois da rodada; os testes partem de uma rodada terminada.
            Set(session, "<LastResult>k__BackingField", new SessionResult(true, 120f, 180f, 300));
            Set(session, "<HasResult>k__BackingField", true);
            Set(session, "<State>k__BackingField", SessionState.Success);

            RestartButton button = _button.AddComponent<RestartButton>();
            button.Bind(session, hand);
            Set(button, "holdSeconds", 1f);
            Set(button, "radius", 0.05f);
            Set(button, "cooldownSeconds", 1f);
            return button;
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, $"Sem campo '{field}'.");
            info.SetValue(target, value);
        }

        /// <summary>Mantém a ponta em cima do botão por um tempo, em passos de quadro.</summary>
        private static void Hold(RestartButton button, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.05f) { button.Tick(0.05f); }
        }

        [Test]
        public void ApontarSemGatilho_NaoCarrega()
        {
            RestartButton button = Make(out Transform tip, out _);

            // Longe e mirando em cheio, mas sem apertar nada. Um visitante varre o controle pela
            // sala inteira olhando em volta; se mirar bastasse, ele reiniciaria a cirurgia de
            // alguém sem encostar em nada.
            tip.position = new Vector3(0f, 0f, -1.2f);
            tip.rotation = Quaternion.Euler(40f, 0f, 0f);

            Hold(button, 2f);

            Assert.AreEqual(0, button.RestartCount);
            Assert.AreEqual(0f, button.Charge);
        }

        [Test]
        public void DuranteACirurgia_OBotaoNemExiste()
        {
            RestartButton button = Make(out Transform tip, out EventSessionController session);
            Set(session, "<HasResult>k__BackingField", false);
            Set(session, "<State>k__BackingField", SessionState.Running);
            tip.position = Vector3.zero;

            // A mão em cima dele com o relógio correndo: não há botão nenhum ali para apertar.
            Hold(button, 3f);

            Assert.IsFalse(button.IsAvailable);
            Assert.AreEqual(0, button.RestartCount,
                "Com a cirurgia em andamento o botão não está na sala, então não há como " +
                "esbarrar nele e apagar a rodada de alguém.");
        }

        [Test]
        public void MaoLonge_NaoCarrega()
        {
            RestartButton button = Make(out Transform tip, out _);
            tip.position = new Vector3(0.4f, 0f, 0f);

            Hold(button, 2f);

            Assert.IsFalse(button.IsPressed);
            Assert.AreEqual(0f, button.Charge);
            Assert.AreEqual(0, button.RestartCount);
        }

        [Test]
        public void EncostarENaoBasta_PrecisaSegurar()
        {
            RestartButton button = Make(out Transform tip, out _);
            tip.position = Vector3.zero;

            // Meio segundo num botão de um segundo: carregou, mas não disparou.
            Hold(button, 0.5f);

            Assert.Greater(button.Charge, 0.3f);
            Assert.AreEqual(0, button.RestartCount,
                "Um botão de reiniciar que dispara no encostar apaga a rodada de quem esbarrar nele.");
        }

        [Test]
        public void SegurandoOTempoTodo_Reinicia()
        {
            RestartButton button = Make(out Transform tip, out EventSessionController session);
            tip.position = Vector3.zero;

            Hold(button, 1.2f);

            Assert.AreEqual(1, button.RestartCount);
            Assert.AreEqual(SessionState.Briefing, session.State,
                "Volta para o briefing, não direto para o relógio correndo: o visitante começa " +
                "a própria rodada pegando um instrumento, como quem chega numa sala intocada.");
            Assert.IsFalse(session.HasResult, "O resultado do visitante anterior sai da tela.");
        }

        [Test]
        public void DoisRaspoes_NaoSomam()
        {
            RestartButton button = Make(out Transform tip, out _);

            // Raspão: entra, fica pouco, sai e volta. A carga tem de escoar no intervalo.
            for (int i = 0; i < 3; i++)
            {
                tip.position = Vector3.zero;
                Hold(button, 0.3f);
                tip.position = new Vector3(0.5f, 0f, 0f);
                Hold(button, 0.5f);
            }

            Assert.AreEqual(0, button.RestartCount);
            Assert.AreEqual(0f, button.Charge, 1e-4f);
        }

        [Test]
        public void MaoParadaEmCima_ReiniciaUmaVezSo()
        {
            RestartButton button = Make(out Transform tip, out EventSessionController session);
            tip.position = Vector3.zero;

            // Quatro segundos sem tirar a mão de cima. Reiniciar devolve a sessão ao briefing,
            // e aí o próprio botão sai do ar: não há rodada terminada para recomeçar de novo.
            Hold(button, 4f);

            Assert.AreEqual(1, button.RestartCount);
            Assert.IsFalse(button.IsAvailable,
                "Reiniciado, não sobra nada para o botão fazer até a próxima rodada acabar.");
            Assert.AreEqual(SessionState.Briefing, session.State);
        }

    }
}
