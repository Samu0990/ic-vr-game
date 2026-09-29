using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.VR;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The headset view in the Editor and at the stand: nothing but the visitor's own head moves
    /// it, and no second camera draws over it.
    /// </summary>
    public class StandInPlaceCameraTests
    {
        private const string SceneName = "TransplanteCardiaco";

        [Test]
        public void TheSpectatorMirrorDrawsOnlyForAHeadsetRunningFromAPc()
        {
            Assert.IsTrue(HeadsetFollowCamera.ShouldDraw(headsetRunning: true, standaloneHeadset: false), "PC com óculos: espelho na tela.");
            Assert.IsFalse(HeadsetFollowCamera.ShouldDraw(headsetRunning: false, standaloneHeadset: false),
                "Editor com o simulador: a câmera do VR já desenha no Game view; o espelho por cima era o 'bug'.");
            Assert.IsFalse(HeadsetFollowCamera.ShouldDraw(headsetRunning: true, standaloneHeadset: true),
                "Quest sozinho: não há tela para o espelho, seria uma renderização a mais por quadro.");
        }

        [UnityTest]
        public IEnumerator WithoutAHeadsetTheMirrorStaysOff_ButStillFollowsTheHead()
        {
            GameObject head = new GameObject("Cabeca");
            GameObject mirror = new GameObject("Espelho");
            try
            {
                mirror.SetActive(false);
                mirror.AddComponent<Camera>();
                HeadsetFollowCamera follow = mirror.AddComponent<HeadsetFollowCamera>();
                follow.Headset = head.transform;
                mirror.SetActive(true);

                Assert.IsFalse(follow.IsDrawing, "No headset running here: the mirror must not draw over the Game view.");

                head.transform.SetPositionAndRotation(new Vector3(0.3f, 1.5f, -0.2f), Quaternion.Euler(10f, 35f, 0f));
                yield return null;
                yield return null;

                Assert.AreEqual(0f, Vector3.Distance(mirror.transform.position, head.transform.position), 0.001f);
                Assert.AreEqual(0f, Quaternion.Angle(mirror.transform.rotation, head.transform.rotation), 0.5f);
            }
            finally
            {
                Object.DestroyImmediate(mirror);
                Object.DestroyImmediate(head);
            }
        }

        [UnityTest]
        public IEnumerator InTheTransplantSceneNothingButTheHeadMovesTheView()
        {
            yield return HeadlessScene.Load(SceneName);

            GameObject rig = GameObject.Find("XR Origin");
            Assert.IsNotNull(rig, "Sem XR Origin na cena.");

            Transform locomotion = rig.transform.Find("Locomotion");
            Assert.IsTrue(locomotion == null || !locomotion.gameObject.activeInHierarchy,
                "Locomoção ligada: o polegar no analógico desliza ou gira a sala, o botão pula, a gravidade puxa.");

            CharacterController body = rig.GetComponent<CharacterController>();
            Assert.IsTrue(body == null || !body.enabled,
                "Colisor do corpo ligado: ao se inclinar sobre a mesa ele bate no campo e empurra a visão para trás.");

            HeadsetFollowCamera mirror = Object.FindFirstObjectByType<HeadsetFollowCamera>();
            Assert.IsNotNull(mirror, "Sem câmera de espectador.");
            Assert.IsFalse(mirror.IsDrawing, "Sem óculos rodando, o espelho não pode desenhar por cima da câmera do VR.");
        }
    }
}
