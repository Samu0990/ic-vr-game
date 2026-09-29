using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Once the new heart beats and the pump is off, the team closes: the sternum comes back
    /// together, the retractor comes out, and the skin edges are brought to meet so the surgeon
    /// can suture them.
    ///
    /// That sequence is done for the visitor, in order — sternum first, then skin — because at a
    /// stand the closure is the lap of honour, not a second operation. What the visitor does
    /// themselves is the part a first-timer can see and feel: the stitches.
    /// </summary>
    public class ChestClosure : MonoBehaviour
    {
        [SerializeField] private TransplantProcedure procedure;
        [SerializeField] private SternotomyController sternotomy;
        [SerializeField] private ChestSkinPatch patch;

        private bool _closing;

        /// <summary>True between the start of closure and the skin being together.</summary>
        public bool IsClosing => _closing;

        private void OnEnable()
        {
            if (procedure != null) { procedure.StageChanged += HandleStage; }
        }

        private void OnDisable()
        {
            if (procedure != null) { procedure.StageChanged -= HandleStage; }
        }

        private void Update() => Tick();

        public void Tick()
        {
            if (!_closing) { return; }

            // Skin only once the bone is back. Closing both at once would draw the skin shut
            // over a sternum still standing out of the chest.
            bool boneClosed = sternotomy == null || (!sternotomy.IsMoving && sternotomy.Openness01 <= 0f);
            if (!boneClosed) { return; }

            if (patch != null && patch.Openness01 > 0f && !patch.IsMoving) { patch.Close(); }
            if (patch == null || patch.IsClosed) { _closing = false; }
        }

        private void HandleStage(TransplantStage stage)
        {
            if (stage == TransplantStage.CloseSkin)
            {
                _closing = true;
                if (sternotomy != null) { sternotomy.Close(); }
                Tick();
            }
            else if (stage == TransplantStage.Idle)
            {
                _closing = false;
            }
        }

        public void Bind(TransplantProcedure transplant, SternotomyController sternum, ChestSkinPatch skin)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { OnDisable(); }

            procedure = transplant;
            sternotomy = sternum;
            patch = skin;

            if (live) { OnEnable(); }
        }
    }
}
