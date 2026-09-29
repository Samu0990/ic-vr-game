using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Draws a set of guides only during the stages they belong to.
    ///
    /// Every stage has its own mark — the purple incision line, the gold sternal ring, the vessel
    /// cuffs — and with all of them showing at once the chest reads as a checklist rather than a
    /// patient, and the first-timer does not know which mark is theirs right now.
    /// </summary>
    public class StageVisibility : MonoBehaviour
    {
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private List<TransplantStage> stages = new List<TransplantStage>();
        [SerializeField] private List<Renderer> renderers = new List<Renderer>();

        private int _shown = -1;

        public bool IsShowing => _shown == 1;

        private void Update() => Tick();

        public void Tick()
        {
            bool show = procedure != null && stages.Contains(procedure.Stage);
            int state = show ? 1 : 0;
            if (state == _shown) { return; }
            _shown = state;

            foreach (Renderer r in renderers)
            {
                if (r != null) { r.enabled = show; }
            }
        }

        public void Bind(TransplantProcedure transplant, IEnumerable<TransplantStage> visibleIn, IEnumerable<Renderer> targets)
        {
            procedure = transplant;
            stages = new List<TransplantStage>(visibleIn);
            renderers = new List<Renderer>(targets);
            _shown = -1;
        }
    }
}
