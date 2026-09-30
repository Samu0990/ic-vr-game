using UnityEngine;
using Skyborne.Flight;
using Skyborne.Grab;

namespace Skyborne.UI
{
    /// <summary>
    /// Bare IMGUI readout for the sandbox: speed, what you are carrying and what it is costing
    /// you. Deliberately IMGUI so the playground needs no Canvas, no fonts and no UI assets.
    /// </summary>
    public class SkyborneHud : MonoBehaviour
    {
        [SerializeField] private FlyerController flyer;
        [SerializeField] private GrabController grab;
        [SerializeField] private bool showControls = true;

        private GUIStyle _style;
        private GUIStyle _crosshair;

        private void Awake()
        {
            if (flyer == null)
            {
                flyer = GetComponentInParent<FlyerController>();
            }

            if (grab == null && flyer != null)
            {
                grab = flyer.GetComponent<GrabController>();
            }
        }

        private void OnGUI()
        {
            if (flyer == null)
            {
                return;
            }

            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 15, richText = false };
            _crosshair ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };

            DrawCrosshair();
            DrawReadout();

            if (showControls)
            {
                DrawControls();
            }
        }

        private void DrawCrosshair()
        {
            bool holding = grab != null && grab.State == GrabState.Holding;
            GUI.color = holding ? new Color(1f, 0.55f, 0.2f) : new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(Screen.width * 0.5f - 15f, Screen.height * 0.5f - 15f, 30f, 30f),
                holding ? "◉" : "·", _crosshair);
            GUI.color = Color.white;
        }

        private void DrawReadout()
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 340f, 200f));

            GUILayout.Label($"Velocidade  {flyer.Speed * 3.6f,6:0} km/h   ({flyer.Speed,5:0.0} m/s)", _style);
            GUILayout.Label(flyer.IsHovering ? "Estado      pairando" : "Estado      em propulsao", _style);

            if (grab != null)
            {
                GUILayout.Label($"Maos        {DescribeGrab()}", _style);

                if (grab.State == GrabState.Holding)
                {
                    GUILayout.Label($"Carga       {grab.CarriedMass,5:0.0} kg  (empuxo /{flyer.LoadFactor:0.00})", _style);

                    if (grab.ThrowCharge > 0f)
                    {
                        GUILayout.Label($"Arremesso   {new string('=', Mathf.RoundToInt(grab.ThrowCharge * 20f))}", _style);
                    }
                }
            }

            GUILayout.EndArea();
        }

        private string DescribeGrab()
        {
            switch (grab.State)
            {
                case GrabState.Reaching: return "esticando o braco";
                case GrabState.Holding: return "segurando";
                default: return "livres";
            }
        }

        private void DrawControls()
        {
            GUILayout.BeginArea(new Rect(16f, Screen.height - 132f, 420f, 124f));
            GUILayout.Label("Mouse  olhar          W  voar pra frente", _style);
            GUILayout.Label("Shift  turbo          S  freio aereo", _style);
            GUILayout.Label("A / D  desviar        Espaco / Ctrl  subir / descer", _style);
            GUILayout.Label("Clique esq. (ou E)  agarrar / soltar", _style);
            GUILayout.Label("Clique dir. (ou Q)  segurar p/ carregar, soltar p/ arremessar", _style);
            GUILayout.EndArea();
        }
    }
}
