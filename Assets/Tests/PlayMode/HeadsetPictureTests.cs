using NUnit.Framework;
using UnityEngine;
using VRSurgery.VR;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The headset shows the same picture as the laptop mirror and the Quest, and when it does
    /// not, the Console says why.
    /// </summary>
    public class HeadsetPictureTests
    {
        [Test]
        public void TheHeadsetDrawsWithoutPostProcessing_OnThePcAsOnTheQuest()
        {
            GameObject head = new GameObject("Cabeca");
            try
            {
                HeadsetPostProcessing post = head.AddComponent<HeadsetPostProcessing>();

                post.Apply(mobile: false);
                Assert.IsFalse(post.Applied, "PC com óculos: sem a vinheta e a exposição menor que escureciam só o óculos.");

                post.Apply(mobile: true);
                Assert.IsFalse(post.Applied, "Quest sozinho: sem passe de tela cheia.");
            }
            finally
            {
                Object.DestroyImmediate(head);
            }
        }

        [Test]
        public void TheConsoleLineNamesTheRuntime_OrSaysNoHeadsetIsDrawing()
        {
            string running = HeadsetDiagnostics.Describe(true, "Open XR Loader", "Oculus", "1.1.49", "Direct3D11",
                "Linear", "Low", "Performance URP Config", false, false, true);
            StringAssert.Contains("rodando pelo runtime 'Oculus' 1.1.49", running);
            StringAssert.Contains("luzes extras DESLIGADAS", running);
            StringAssert.Contains("pós-processamento desligado", running);

            string none = HeadsetDiagnostics.Describe(false, "", "", "", "Vulkan", "Linear", "Low",
                "Performance URP Config", false, false, true);
            StringAssert.Contains("NENHUM óculos rodando", none);
            StringAssert.Contains("o Unity não está desenhando nele", none);

            string build = HeadsetDiagnostics.Describe(false, "", "", "", "Vulkan", "Linear", "Low",
                "Performance URP Config", false, false, false);
            StringAssert.DoesNotContain("notebook", build, "Fora do Editor não há notebook para culpar.");
        }
    }
}
