using NUnit.Framework;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Surgery;

namespace VRSurgery.Tests
{
    /// <summary>
    /// O brilho que acende o objeto do passo. O que importa: que ele se ajuste ao tamanho do
    /// alvo, que pulse de verdade, e que não acenda uma bola no vazio quando o alvo não tem
    /// corpo nenhum para envolver.
    /// </summary>
    public class StepHighlightTests
    {
        private GameObject _host;
        private GameObject _glow;
        private GameObject _target;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject g in new[] { _host, _glow, _target })
            {
                if (g != null) { Object.DestroyImmediate(g); }
            }

            SurgeryEvents.ResetAll();
        }

        private StepHighlight Make(out NextStepArrow arrow)
        {
            _host = new GameObject("Sistemas");
            arrow = _host.AddComponent<NextStepArrow>();

            _glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(_glow.GetComponent<Collider>());
            _glow.SetActive(false);

            StepHighlight highlight = _host.AddComponent<StepHighlight>();
            highlight.Bind(null, arrow, null, _glow.transform);
            return highlight;
        }

        /// <summary>Um alvo com corpo, de tamanho conhecido.</summary>
        private Transform TargetWithBody(Vector3 size)
        {
            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(_target.GetComponent<Collider>());
            _target.transform.position = new Vector3(0.4f, 1.2f, 0.3f);
            _target.transform.localScale = size;
            return _target.transform;
        }

        private static void Aim(NextStepArrow arrow, Transform target)
        {
            typeof(NextStepArrow).GetProperty("Target")
                ?.SetValue(arrow, target);
        }

        [Test]
        public void SemAlvo_NaoAcendeNada()
        {
            StepHighlight highlight = Make(out NextStepArrow arrow);
            Aim(arrow, null);

            highlight.Tick(0.1f);

            Assert.IsNull(highlight.LitPlace);
            Assert.IsFalse(_glow.activeSelf);
        }

        [Test]
        public void ComAlvo_OBrilhoEnvolveOObjeto()
        {
            StepHighlight highlight = Make(out NextStepArrow arrow);
            Transform target = TargetWithBody(new Vector3(0.1f, 0.2f, 0.1f));
            Aim(arrow, target);

            highlight.Tick(0.1f);

            Assert.IsTrue(_glow.activeSelf);
            Assert.AreEqual(target.position.x, _glow.transform.position.x, 1e-3f);
            Assert.Greater(_glow.transform.localScale.y, 0.2f,
                "O invólucro tem de cobrir o objeto, não ficar menor que ele.");
            Assert.Less(_glow.transform.localScale.y, 0.4f,
                "E não pode virar uma bola que cobre a mesa inteira.");
        }

        [Test]
        public void AlvoSemCorpo_NaoVirabolaNoVazio()
        {
            StepHighlight highlight = Make(out NextStepArrow arrow);

            // Um ponto de referência puro, como a linha do esterno: existe, mas não tem o que
            // envolver. Acender uma esfera na origem dele seria pior do que não acender.
            _target = new GameObject("PontoSemCorpo");
            _target.transform.position = new Vector3(0f, 1.2f, 0f);
            Aim(arrow, _target.transform);

            highlight.Tick(0.1f);

            Assert.IsFalse(_glow.activeSelf);
        }

        [Test]
        public void OBrilhoPulsa_NaoFicaParado()
        {
            StepHighlight highlight = Make(out NextStepArrow arrow);
            Transform target = TargetWithBody(Vector3.one * 0.1f);
            Aim(arrow, target);

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            Renderer renderer = _glow.GetComponent<Renderer>();

            float low = float.MaxValue;
            float high = float.MinValue;
            for (int i = 0; i < 40; i++)
            {
                highlight.Tick(0.05f);
                renderer.GetPropertyBlock(block);
                float a = block.GetColor(Shader.PropertyToID("_BaseColor")).a;
                low = Mathf.Min(low, a);
                high = Mathf.Max(high, a);
            }

            Assert.Greater(high - low, 0.05f,
                "Luz parada vira cenário em segundos; é o movimento que o olho pega de canto.");
        }
    }
}
