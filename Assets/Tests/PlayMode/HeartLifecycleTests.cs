using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Os dois corações na cena que o jogo carrega: quando cada um pode ser pego, onde fica
    /// depois, e para onde volta quando cai ou some.
    ///
    /// Os defeitos aqui eram todos de "fora de hora": o coração doente saía do peito na mão
    /// em qualquer etapa, o doador encaixava antes de existir lugar para ele e depois nunca mais
    /// contava, o doador encaixado caía do peito, e um coração solto sobre o corpo fechado sumia
    /// lá dentro para sempre. Nenhum deles aparece montando o procedimento à mão, porque todos
    /// dependem do que a cena liga junto: física de soltar, recuperação, assento e bacia.
    /// </summary>
    public class HeartLifecycleTests
    {
        private const string SceneName = "TransplanteCardiaco";

        private TransplantProcedure _procedure;
        private EventSessionController _session;
        private GameObject _native;
        private GameObject _donor;
        private Transform _seat;
        private Vector3 _donorHome;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return HeadlessScene.Load(SceneName);

            _procedure = Object.FindFirstObjectByType<TransplantProcedure>();
            _session = Object.FindFirstObjectByType<EventSessionController>();
            Assert.IsNotNull(_procedure);
            Assert.IsNotNull(_session);

            foreach (GrabbableOrgan organ in
                     Object.FindObjectsByType<GrabbableOrgan>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (organ.name == "Heart") { _native = organ.gameObject; }
                if (organ.name == "DonorHeart") { _donor = organ.gameObject; }
            }

            _seat = GameObject.Find("PericardialSeat").transform;
            Assert.IsNotNull(_native);
            Assert.IsNotNull(_donor);
            _donorHome = _donor.transform.position;

            // A rodada de verdade: a vez começa (o paciente é preparado) e o relógio corre. Sem
            // isso a cena fica em Atração e, seis segundos depois, reinicia o paciente sozinha.
            _session.BeginSession();
            _session.StartRound();
            yield return null;
        }

        [TearDown]
        public void TearDown() => SurgeryEvents.ResetAll();

        /// <summary>
        /// Moves an organ the way a hand does: the body as well as the transform. An organ is an
        /// interpolated rigidbody, and moving only its transform is undone by the interpolation
        /// on the next frame — the test would be measuring a heart that never moved.
        /// </summary>
        private static void Place(GameObject organ, Vector3 position)
        {
            Rigidbody body = organ.GetComponent<Rigidbody>();
            if (body != null)
            {
                // A hand moves it by velocity; a test can only teleport it, and a teleport on an
                // interpolated body is written over by the next frame's interpolation.
                body.interpolation = RigidbodyInterpolation.None;
                body.position = position;
            }

            organ.transform.position = position;
            Physics.SyncTransforms();
        }

        private void AdvanceTo(TransplantStage target)
        {
            for (int guard = 0; guard < 20 && _procedure.Stage != target; guard++)
            {
                if (_procedure.Stage == TransplantStage.GoOnBypass)
                {
                    _procedure.Bypass.Attempt(BypassStep.Cannulate);
                    _procedure.Bypass.Attempt(BypassStep.ClampAorta);
                    _procedure.Bypass.Attempt(BypassStep.Cardioplegia);
                }

                Assert.IsTrue(_procedure.CompleteStage(_procedure.Stage),
                    $"A operação travou em {_procedure.Stage} a caminho de {target}.");

                // O esterno aberto revela o coração; aqui a etapa é concluída sem a serra.
                if (_procedure.Stage == TransplantStage.OpenPericardium) { _native.SetActive(true); }
            }

            Assert.AreEqual(target, _procedure.Stage);
        }

        private static bool FiltersAllowSelection(GameObject organ)
        {
            XRGrabInteractable grab = organ.GetComponent<XRGrabInteractable>();
            Assert.IsNotNull(grab, $"{organ.name} sem XRGrabInteractable.");

            List<IXRSelectFilter> filters = new List<IXRSelectFilter>();
            grab.selectFilters.GetAll(filters);
            foreach (IXRSelectFilter filter in filters)
            {
                if (filter.canProcess && !filter.Process(null, grab)) { return false; }
            }

            return true;
        }

        // ------------------------------------------------------------------ 1.3 coração doente

        [UnityTest]
        public IEnumerator OCoracaoDoente_SoPodeSerPegoNaHoraDeRetirar()
        {
            AdvanceTo(TransplantStage.GoOnBypass);
            yield return null;

            Assert.IsFalse(FiltersAllowSelection(_native),
                "Antes da bomba o coração doente fica preso no lugar: a mão não pode pegá-lo.");

            AdvanceTo(TransplantStage.RemoveNativeHeart);
            yield return null;
            Assert.IsTrue(FiltersAllowSelection(_native), "Na hora de retirar, ele tem de sair na mão.");
        }

        // ------------------------------------------------------------------ 1.4 aviso preso

        [UnityTest]
        public IEnumerator OAvisoDoCoracaoAntesDaHora_SaiNaTrocaDeEtapa()
        {
            AdvanceTo(TransplantStage.GoOnBypass);
            SurgicalInteractable heart = _native.GetComponent<SurgicalInteractable>();
            heart.OnGrabbed(null);
            yield return null;
            heart.OnReleased();
            yield return null;

            StringAssert.Contains("ainda não pode sair", _procedure.CurrentInstruction);

            AdvanceTo(TransplantStage.RemoveNativeHeart);
            Assert.AreEqual("Retire o coração doente", _procedure.CurrentInstruction,
                "Na etapa seguinte o monitor tem de dizer o que fazer agora, não o aviso velho.");
        }

        [UnityTest]
        public IEnumerator OAvisoDoCoracaoAntesDaHora_SomeSozinho()
        {
            AdvanceTo(TransplantStage.GoOnBypass);
            string instruction = _procedure.CurrentInstruction;
            SurgicalInteractable heart = _native.GetComponent<SurgicalInteractable>();
            heart.OnGrabbed(null);
            yield return null;
            heart.OnReleased();
            yield return null;
            StringAssert.Contains("ainda não pode sair", _procedure.CurrentInstruction);

            yield return new WaitForSeconds(4.6f);

            Assert.AreEqual(instruction, _procedure.CurrentInstruction,
                "O aviso some em alguns segundos; a instrução da etapa volta ao monitor e ao cartão.");
        }

        // ------------------------------------------------------------------ 1.6 doador antes da hora

        [UnityTest]
        public IEnumerator ODoadorSoltoNoPeitoAntesDaHora_VoltaParaABacia_EDepoisAindaEncaixa()
        {
            AdvanceTo(TransplantStage.GoOnBypass);
            SurgicalInteractable donor = _donor.GetComponent<SurgicalInteractable>();
            GrabbableOrgan organ = _donor.GetComponent<GrabbableOrgan>();

            donor.OnGrabbed(null);
            yield return null;
            Place(_donor, _seat.position);
            yield return null;
            donor.OnReleased();
            for (int i = 0; i < 30; i++) { yield return null; }

            Assert.IsFalse(organ.Satisfied, "Antes de o doente sair não há onde encaixar o novo.");
            Assert.Less(Vector3.Distance(_donor.transform.position, _donorHome), 0.03f,
                "Solto no peito antes da hora, o doador volta para a bacia.");

            AdvanceTo(TransplantStage.PlaceDonorHeart);
            donor.OnGrabbed(null);
            yield return null;
            Place(_donor, _seat.position + new Vector3(0.02f, 0.01f, 0f));
            yield return null;
            donor.OnReleased();
            yield return null;
            yield return null;

            Assert.AreEqual(TransplantStage.ConnectVessels, _procedure.Stage,
                "Na hora certa o doador tem de encaixar e contar — a tentativa de antes não pode travar o jogo.");
        }

        // ------------------------------------------------------------------ 1.7 doador encaixado fica

        [UnityTest]
        public IEnumerator ODoadorEncaixado_FicaPresoNoLugar()
        {
            AdvanceTo(TransplantStage.PlaceDonorHeart);
            SurgicalInteractable donor = _donor.GetComponent<SurgicalInteractable>();

            donor.OnGrabbed(null);
            yield return null;
            Place(_donor, _seat.position + new Vector3(0.03f, 0.02f, 0f));
            yield return null;
            donor.OnReleased();

            // A física de soltar liga a gravidade dois quadros depois; meio segundo é tempo de
            // sobra para um coração solto cair do lugar.
            yield return new WaitForSeconds(0.6f);

            Assert.AreEqual(TransplantStage.ConnectVessels, _procedure.Stage);
            Assert.Less(Vector3.Distance(_donor.transform.position, _seat.position), 0.01f,
                "Encaixado, o coração novo fica no assento — não cai do peito.");
            Assert.IsTrue(_donor.GetComponent<Rigidbody>().isKinematic, "Preso no lugar, sem física.");
            Assert.IsFalse(FiltersAllowSelection(_donor),
                "E não sai mais na mão: o jogo segue como se ele estivesse implantado, porque está.");
        }

        // ------------------------------------------------------------------ 1.8 cuba e perdidos

        [UnityTest]
        public IEnumerator OCoracaoRetirado_VaiParaACuba_EFicaLa()
        {
            GameObject cuba = GameObject.Find("CubaRetirada");
            Assert.IsNotNull(cuba, "Precisa haver uma cuba de retirada visível ao lado da mesa.");
            Assert.IsNotNull(cuba.GetComponentInChildren<Renderer>(), "A cuba tem de aparecer.");

            AdvanceTo(TransplantStage.RemoveNativeHeart);
            SurgicalInteractable heart = _native.GetComponent<SurgicalInteractable>();
            heart.OnGrabbed(null);
            yield return null;
            Place(_native, _seat.position + new Vector3(0.6f, 0.1f, 0f));
            yield return null;
            heart.OnReleased();
            yield return new WaitForSeconds(1.2f);

            Assert.AreEqual(TransplantStage.PlaceDonorHeart, _procedure.Stage);
            Assert.Less(Vector3.Distance(_native.transform.position, cuba.transform.position), 0.12f,
                "O coração retirado vai para a cuba de retirada.");
            Assert.IsTrue(_native.GetComponent<Rigidbody>().isKinematic, "E fica lá, parado.");
            Assert.IsFalse(FiltersAllowSelection(_native), "Retirado, ele não volta para a mão.");
        }

        [UnityTest]
        public IEnumerator UmCoracaoQueCaiNoCorpoFechado_VoltaParaABacia()
        {
            // Sobre os campos ele para em cima deles (têm colisor). Mas a pele não tem: pela
            // janela, com a pele ainda fechada, ou ao lado do fundo pequeno do tórax aberto, o
            // coração atravessa e vai parar na mesa, DENTRO do paciente — ao alcance pela conta
            // antiga, e invisível. Aqui ele é largado já lá dentro, sob a pele do abdome e sob a
            // janela, como fica depois de atravessar.
            AdvanceTo(TransplantStage.GoOnBypass);
            SurgicalInteractable donor = _donor.GetComponent<SurgicalInteractable>();

            foreach (Vector3 inside in new[]
                     {
                         new Vector3(_seat.position.x, 1.0f, _seat.position.z - 0.40f),
                         new Vector3(_seat.position.x + 0.03f, 1.0f, _seat.position.z + 0.05f),
                     })
            {
                donor.OnGrabbed(null);
                yield return null;
                Place(_donor, inside);
                yield return null;
                donor.OnReleased();

                yield return new WaitForSeconds(2f);

                Assert.Less(Vector3.Distance(_donor.transform.position, _donorHome), 0.03f,
                    $"Perdido dentro do corpo fechado ({inside}), o coração volta para a bacia com o pop.");
            }
        }
    }
}
