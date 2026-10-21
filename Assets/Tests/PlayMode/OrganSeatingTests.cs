using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Encaixar o coração novo e tirar o velho na hora certa.
    ///
    /// Os dois vieram de teste com gente de fora: encaixar o doador era quase sorte, e o coração
    /// doente saía antes do paciente estar na bomba — que é a ordem errada e, num paciente de
    /// verdade, o fim da operação.
    /// </summary>
    public class OrganSeatingTests
    {
        private GameObject _host;
        private GameObject _organ;
        private GameObject _seat;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject g in new[] { _host, _organ, _seat })
            {
                if (g != null) { Object.DestroyImmediate(g); }
            }

            SurgeryEvents.ResetAll();
        }

        private GrabbableOrgan Make(OrganRole role, out TransplantProcedure procedure,
            out SurgicalInteractable interactable, out Transform seat)
        {
            _host = new GameObject("Sistemas");
            procedure = _host.AddComponent<TransplantProcedure>();

            _seat = new GameObject("Assento");
            _seat.transform.position = new Vector3(0f, 1.2f, 0f);
            _seat.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            seat = _seat.transform;

            _organ = new GameObject("Coracao");
            interactable = _organ.AddComponent<SurgicalInteractable>();
            GrabbableOrgan organ = _organ.AddComponent<GrabbableOrgan>();
            organ.Bind(role, seat, procedure);
            return organ;
        }

        private static void Stage(TransplantProcedure procedure, TransplantStage stage)
        {
            FieldInfo field = typeof(TransplantProcedure)
                .GetField("<Stage>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(procedure, stage);
        }

        [UnityTest]
        public IEnumerator ODoadorAssentaSoltoPertoDoLugar_NaoSoEmCima()
        {
            GrabbableOrgan organ = Make(OrganRole.Donor, out TransplantProcedure procedure,
                out SurgicalInteractable interactable, out Transform seat);
            Stage(procedure, TransplantStage.PlaceDonorHeart);

            interactable.OnGrabbed(null);
            yield return null;

            // Sete centímetros fora: dentro da janela nova, fora da antiga de 3,5 cm. É a
            // diferença entre "encaixou" e "soltei e não aconteceu nada, e não sei por quê".
            _organ.transform.position = seat.position + new Vector3(0.07f, 0f, 0f);
            interactable.OnReleased();
            yield return null;
            yield return null;

            Assert.IsTrue(organ.Satisfied,
                "Soltar o coração a sete centímetros do lugar tem de contar como encaixe.");
        }

        [UnityTest]
        public IEnumerator AoAssentar_OCoracaoVaiExatamenteParaOLugar()
        {
            GrabbableOrgan organ = Make(OrganRole.Donor, out TransplantProcedure procedure,
                out SurgicalInteractable interactable, out Transform seat);
            Stage(procedure, TransplantStage.PlaceDonorHeart);

            interactable.OnGrabbed(null);
            yield return null;
            _organ.transform.position = seat.position + new Vector3(0.05f, 0.02f, 0f);
            interactable.OnReleased();
            yield return null;
            yield return null;

            Assert.AreEqual(0f, Vector3.Distance(_organ.transform.position, seat.position), 1e-3f,
                "Encaixado é encaixado: largado torto a cinco centímetros, o órgão assenta no " +
                "lugar em vez de ficar boiando onde a mão soltou.");
        }

        [UnityTest]
        public IEnumerator ForaDaJanela_NaoAssenta()
        {
            GrabbableOrgan organ = Make(OrganRole.Donor, out TransplantProcedure procedure,
                out SurgicalInteractable interactable, out Transform seat);
            Stage(procedure, TransplantStage.PlaceDonorHeart);

            interactable.OnGrabbed(null);
            yield return null;
            _organ.transform.position = seat.position + new Vector3(0.4f, 0f, 0f);
            interactable.OnReleased();
            yield return null;
            yield return null;

            Assert.IsFalse(organ.Satisfied,
                "A janela ficou generosa, não infinita: largar o coração a quarenta " +
                "centímetros não é implantar.");
        }

        [UnityTest]
        public IEnumerator AntesDaBomba_OCoracaoDoenteNaoSai()
        {
            GrabbableOrgan organ = Make(OrganRole.Native, out TransplantProcedure procedure,
                out SurgicalInteractable interactable, out Transform seat);
            Stage(procedure, TransplantStage.GoOnBypass);

            Vector3 home = _organ.transform.position;
            interactable.OnGrabbed(null);
            yield return null;

            _organ.transform.position = home + new Vector3(0.6f, 0.3f, 0f);
            interactable.OnReleased();
            yield return null;
            yield return null;

            Assert.IsFalse(organ.Satisfied,
                "Na etapa da bomba o coração não sai: o paciente ainda circula o próprio sangue.");
            Assert.AreEqual(0f, Vector3.Distance(_organ.transform.position, home), 1e-3f,
                "E volta para o lugar, em vez de ficar largado no ar fora do peito.");
            StringAssert.Contains("bomba", procedure.CurrentInstruction.ToLowerInvariant(),
                "Recusar em silêncio não ensina nada; o monitor tem de dizer o que falta.");
        }

        [UnityTest]
        public IEnumerator NaHoraCerta_OCoracaoDoenteSai()
        {
            GrabbableOrgan organ = Make(OrganRole.Native, out TransplantProcedure procedure,
                out SurgicalInteractable interactable, out Transform _);
            Stage(procedure, TransplantStage.RemoveNativeHeart);

            Vector3 home = _organ.transform.position;
            interactable.OnGrabbed(null);
            yield return null;

            _organ.transform.position = home + new Vector3(0.6f, 0.3f, 0f);
            interactable.OnReleased();
            yield return null;
            yield return null;

            Assert.IsTrue(organ.Satisfied,
                "Chegada a hora, tirar o coração e soltá-lo fora do peito conta como explante.");
        }
    }
}
