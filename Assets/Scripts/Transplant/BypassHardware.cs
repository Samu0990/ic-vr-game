using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// What the pump puts into the chest, shown as each step puts it there: the aortic and caval
    /// cannulas from cannulation until the chest is closed, the cardioplegia line (which later
    /// vents the air out of the aortic root) from the arrest onwards, and the cross-clamp on the
    /// aorta exactly while the aorta is clamped.
    ///
    /// Before this the pump steps changed only numbers on a monitor; the chest looked the same on
    /// bypass as off it, and "clamp the aorta" left no clamp. Visual only: it polls the pump and
    /// owns no rules.
    /// </summary>
    public class BypassHardware : MonoBehaviour
    {
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private Renderer[] cannulas = new Renderer[0];
        [SerializeField] private Renderer[] rootLine = new Renderer[0];
        [SerializeField] private Renderer[] crossClamp = new Renderer[0];

        public bool CannulasShown { get; private set; }
        public bool RootLineShown { get; private set; }
        public bool ClampShown { get; private set; }

        private void Awake() => Tick();

        private void LateUpdate() => Tick();

        /// <summary>Reads the pump and shows what it has in the chest. Called by hand in tests.</summary>
        public void Tick()
        {
            int done = procedure != null ? procedure.Bypass.CompletedSteps : 0;
            TransplantStage stage = procedure != null ? procedure.Stage : TransplantStage.Idle;

            // Decannulation happens once the heart has taken over and before the chest closes.
            bool inChest = stage != TransplantStage.Idle && stage != TransplantStage.CloseSkin &&
                           stage != TransplantStage.Complete;

            CannulasShown = inChest && done >= 1;
            ClampShown = inChest && done >= 2 && done < 4;
            RootLineShown = inChest && done >= 3;

            Set(cannulas, CannulasShown);
            Set(crossClamp, ClampShown);
            Set(rootLine, RootLineShown);
        }

        private static void Set(Renderer[] renderers, bool on)
        {
            if (renderers == null) { return; }
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].enabled != on) { renderers[i].enabled = on; }
            }
        }

        public void Bind(TransplantProcedure transplant, Renderer[] cannulaParts, Renderer[] rootParts, Renderer[] clampParts)
        {
            procedure = transplant;
            cannulas = cannulaParts ?? new Renderer[0];
            rootLine = rootParts ?? new Renderer[0];
            crossClamp = clampParts ?? new Renderer[0];
            Tick();
        }
    }
}
