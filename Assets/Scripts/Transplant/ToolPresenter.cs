using System;
using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Keeps an instrument off the tray until the step that needs it, then puts it there — the
    /// team handing it over.
    ///
    /// Made for the internal defibrillator paddles. Nothing may lie on the patient, and the
    /// tray beside the table has room for the instruments and the donor heart's basin but not
    /// for 27 cm of paddles as well. They are only wanted once the new heart fibrillates, by
    /// which time the basin is empty: so they appear laid across it then, and are taken away
    /// again for the next visitor.
    /// </summary>
    public class ToolPresenter : MonoBehaviour
    {
        [SerializeField] private GameObject tool;
        [SerializeField] private DefibrillationWorker defibrillation;
        [SerializeField] private TransplantProcedure procedure;

        private bool _subscribed;

        /// <summary>True while the instrument is out.</summary>
        public bool IsPresented => tool != null && tool.activeSelf;

        /// <summary>Raised when the instrument is put out.</summary>
        public event Action Presented;

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Start()
        {
            if (defibrillation == null || !defibrillation.IsFibrillating) { Withdraw(); }
        }

        private void Subscribe()
        {
            if (_subscribed) { return; }
            _subscribed = true;
            if (defibrillation != null) { defibrillation.FibrillationStarted += Present; }
            if (procedure != null) { procedure.StageChanged += HandleStage; }
        }

        private void Unsubscribe()
        {
            if (!_subscribed) { return; }
            _subscribed = false;
            if (defibrillation != null) { defibrillation.FibrillationStarted -= Present; }
            if (procedure != null) { procedure.StageChanged -= HandleStage; }
        }

        private void HandleStage(TransplantStage stage)
        {
            // A new visitor: back off the tray until their heart fibrillates.
            if (stage == TransplantStage.Idle) { Withdraw(); }
        }

        /// <summary>Puts the instrument out on the tray.</summary>
        public void Present()
        {
            if (tool == null || tool.activeSelf) { return; }
            tool.SetActive(true);
            Presented?.Invoke();
        }

        /// <summary>Takes the instrument away.</summary>
        public void Withdraw()
        {
            if (tool != null && tool.activeSelf) { tool.SetActive(false); }
        }

        public void Bind(GameObject instrument, DefibrillationWorker defib, TransplantProcedure transplant)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { Unsubscribe(); }

            tool = instrument;
            defibrillation = defib;
            procedure = transplant;

            if (live) { Subscribe(); }
        }
    }
}
