using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Session;
using VRSurgery.Surgery;

namespace VRSurgery.Tests
{
    /// <summary>
    /// A tela que fecha a vez do visitante. O que importa aqui é que ela só aparece quando a
    /// rodada acabou de verdade, que diga qual dos dois fins foi, e que o tempo saia legível.
    /// </summary>
    public class SurgeryEndScreenTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) { Object.DestroyImmediate(_root); }
            SurgeryEvents.ResetAll();
        }

        private SurgeryEndScreen Screen(out GameObject body, out TextMesh headline, out TextMesh detail,
            out TextMesh prompt)
        {
            _root = new GameObject("TelaDeFim");
            body = new GameObject("Corpo");
            body.transform.SetParent(_root.transform, false);

            headline = new GameObject("Headline").AddComponent<TextMesh>();
            detail = new GameObject("Detail").AddComponent<TextMesh>();
            prompt = new GameObject("Prompt").AddComponent<TextMesh>();
            foreach (TextMesh t in new[] { headline, detail, prompt })
            {
                t.transform.SetParent(body.transform, false);
            }

            SurgeryEndScreen screen = _root.AddComponent<SurgeryEndScreen>();
            Set(screen, "body", body);
            Set(screen, "headlineText", headline);
            Set(screen, "detailText", detail);
            Set(screen, "promptText", prompt);
            return screen;
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, $"Sem campo '{field}' em {target.GetType().Name}.");
            info.SetValue(target, value);
        }

        /// <summary>Põe a sessão num fim já decidido, sem ter de jogar a rodada inteira.</summary>
        private static EventSessionController Session(bool succeeded, float elapsed, int score)
        {
            EventSessionController session = new GameObject("Sessao").AddComponent<EventSessionController>();
            Set(session, "<LastResult>k__BackingField",
                new SessionResult(succeeded, elapsed, 300f - elapsed, score));
            Set(session, "<HasResult>k__BackingField", true);
            Set(session, "<State>k__BackingField",
                succeeded ? SessionState.Success : SessionState.Failure);
            return session;
        }

        [Test]
        public void SemRodadaTerminada_ATelaFicaApagada()
        {
            SurgeryEndScreen screen = Screen(out GameObject body, out _, out _, out _);

            // Sem sessão nenhuma: o pior caso é a tela acender sozinha na sala vazia.
            screen.Refresh();

            Assert.IsFalse(screen.IsShown);
            Assert.IsFalse(body.activeSelf);
            Assert.IsEmpty(screen.Headline);
        }

        [Test]
        public void RodadaEmAndamento_NaoMostraResultado()
        {
            SurgeryEndScreen screen = Screen(out GameObject body, out _, out _, out _);
            EventSessionController session = Session(true, 100f, 400);
            Set(session, "<State>k__BackingField", SessionState.Running);
            screen.Bind(session, null);

            screen.Refresh();

            Assert.IsFalse(screen.IsShown, "Com o relógio correndo não existe resultado para mostrar.");
            Assert.IsFalse(body.activeSelf);
            Object.DestroyImmediate(session.gameObject);
        }

        [Test]
        public void CirurgiaConcluida_MostraTempoEPontos()
        {
            SurgeryEndScreen screen = Screen(out GameObject body, out TextMesh headline,
                out TextMesh detail, out TextMesh prompt);
            EventSessionController session = Session(true, 187f, 428);
            screen.Bind(session, null);

            screen.Refresh();

            Assert.IsTrue(screen.IsShown);
            Assert.IsTrue(body.activeSelf);
            Assert.AreEqual("CIRURGIA CONCLUÍDA", headline.text);
            StringAssert.Contains("3:07", detail.text, "187 s tem de sair como 3:07.");
            StringAssert.Contains("428", detail.text);
            StringAssert.Contains("visor", prompt.text);
            Object.DestroyImmediate(session.gameObject);
        }

        [Test]
        public void TempoEsgotado_NaoAnunciaPontos()
        {
            SurgeryEndScreen screen = Screen(out _, out TextMesh headline, out TextMesh detail, out _);
            EventSessionController session = Session(false, 300f, 0);
            screen.Bind(session, null);

            screen.Refresh();

            Assert.AreEqual("TEMPO ESGOTADO", headline.text);
            StringAssert.Contains("5:00", detail.text);
            StringAssert.DoesNotContain("Pontos", detail.text,
                "Quem perdeu não marca ponto; anunciar zero é esfregar na cara.");
            Object.DestroyImmediate(session.gameObject);
        }

        [Test]
        public void ORelogioSaiEmMinutosESegundos()
        {
            Assert.AreEqual("0:00", SurgeryEndScreen.Clock(0f));
            Assert.AreEqual("0:09", SurgeryEndScreen.Clock(9f));
            Assert.AreEqual("1:00", SurgeryEndScreen.Clock(60f));
            Assert.AreEqual("3:07", SurgeryEndScreen.Clock(187f));
            // Tempo negativo não existe, mas um relógio que imprime "-1:-5" num estande sim.
            Assert.AreEqual("0:00", SurgeryEndScreen.Clock(-5f));
        }
    }
}
