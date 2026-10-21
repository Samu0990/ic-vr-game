using UnityEngine;
using VRSurgery.Interaction;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// Acende e faz pulsar a coisa que o passo atual pede: o instrumento a pegar e o lugar a
    /// trabalhar.
    ///
    /// As setas dizem onde olhar, mas uma seta é uma informação a mais para processar enquanto
    /// o visitante já está tentando entender a cena inteira. O objeto aceso é a informação mais
    /// barata que existe: não precisa ser lido, não precisa ser seguido, não depende de ângulo.
    /// Brilhou, é esse.
    ///
    /// Pulsa em vez de ficar aceso fixo porque uma luz parada vira parte do cenário em segundos,
    /// e porque movimento é o que o olho pega na periferia — que é justamente onde o objeto está
    /// quando o visitante ainda não sabe para onde olhar.
    ///
    /// Não usa o contorno de <see cref="GrabOutlines"/> de propósito: aquele é do hover e é
    /// apagado todo quadro em tudo que não está sob a mão, então os dois se anulariam. Este é um
    /// invólucro próprio, que funciona por cima de qualquer material do alvo.
    /// </summary>
    public class StepHighlight : MonoBehaviour
    {
        [Header("Fontes")]
        [SerializeField] private NextToolHint toolHint;
        [SerializeField] private NextStepArrow stepArrow;

        [Header("Invólucros")]
        [Tooltip("O brilho sobre o instrumento a pegar. Mesma cor da seta de instrumento.")]
        [SerializeField] private Transform toolGlow;

        [Tooltip("O brilho sobre o lugar a trabalhar. Mesma cor da seta de passo.")]
        [SerializeField] private Transform placeGlow;

        [Header("Pulso")]
        [SerializeField, Min(0.1f)] private float pulseHz = 1.1f;

        [SerializeField, Range(0f, 1f)] private float minAlpha = 0.14f;

        [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.42f;

        [Tooltip("Quanto o invólucro é maior que o próprio objeto. Pouco: um halo grande demais " +
                 "deixa de mostrar qual é a peça e passa a cobrir a mesa toda.")]
        [SerializeField, Min(1f)] private float margin = 1.18f;

        private static readonly int BaseColour = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _block;
        private float _clock;

        /// <summary>O instrumento aceso agora, ou null.</summary>
        public Transform LitTool { get; private set; }

        /// <summary>O lugar aceso agora, ou null.</summary>
        public Transform LitPlace { get; private set; }

        private void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>Recalcula os dois brilhos. Recebe o delta para os testes poderem conduzi-lo.</summary>
        public void Tick(float deltaTime)
        {
            _clock += Mathf.Max(0f, deltaTime);
            float alpha = Mathf.Lerp(minAlpha, maxAlpha,
                0.5f + 0.5f * Mathf.Sin(_clock * pulseHz * Mathf.PI * 2f));

            SurgicalInteractable tool = toolHint != null ? toolHint.Target : null;
            LitTool = tool != null && !tool.IsHeld ? tool.transform : null;
            LitPlace = stepArrow != null ? stepArrow.Target : null;

            Wrap(toolGlow, LitTool, alpha);
            Wrap(placeGlow, LitPlace, alpha);
        }

        /// <summary>
        /// Põe um invólucro em volta do alvo, do tamanho dele. Sem renderizador não há tamanho
        /// que faça sentido — é o caso de um ponto de referência vazio, como a linha do esterno —
        /// e aí só a seta aponta, sem brilho nenhum, em vez de uma bola no lugar errado.
        /// </summary>
        private void Wrap(Transform glow, Transform target, float alpha)
        {
            if (glow == null) { return; }

            Bounds? size = target != null ? BoundsOf(target.gameObject) : null;
            bool show = size.HasValue;

            if (glow.gameObject.activeSelf != show) { glow.gameObject.SetActive(show); }
            if (!show) { return; }

            Bounds bounds = size.Value;
            glow.position = bounds.center;
            glow.localScale = Vector3.Max(bounds.size * margin, Vector3.one * 0.02f);

            Renderer renderer = glow.GetComponent<Renderer>();
            if (renderer == null) { return; }

            _block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            Color colour = renderer.sharedMaterial != null
                ? renderer.sharedMaterial.color
                : Color.white;
            colour.a = alpha;
            _block.SetColor(BaseColour, colour);
            renderer.SetPropertyBlock(_block);
        }

        private static Bounds? BoundsOf(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            Bounds bounds = default;
            bool any = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                // Sem pegar o próprio invólucro, que viveria crescendo em cima de si mesmo.
                if (r == null || !r.enabled || r.transform.IsChildOf(target.transform) == false) { continue; }

                if (!any) { bounds = r.bounds; any = true; }
                else { bounds.Encapsulate(r.bounds); }
            }

            return any ? bounds : (Bounds?)null;
        }

        /// <summary>Liga os brilhos às duas fontes e aos dois invólucros.</summary>
        public void Bind(NextToolHint hint, NextStepArrow arrow, Transform tool, Transform place)
        {
            toolHint = hint;
            stepArrow = arrow;
            toolGlow = tool;
            placeGlow = place;
        }
    }
}
