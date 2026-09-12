using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// The stages of a heart transplant, in the order a transplant actually happens.
    ///
    /// A real transplant runs four to six hours; a stand gives each visitor ninety seconds. So
    /// this is the procedure's arc rather than its full length — the five beats an audience can
    /// follow and a first-timer can perform, each one a real step of the operation and not a
    /// mini-game bolted on.
    /// </summary>
    public enum TransplantStage
    {
        /// <summary>Nothing started. The chest is closed.</summary>
        Idle,

        /// <summary>Sternotomy: the chest has to be opened before anything else is possible.</summary>
        OpenChest,

        /// <summary>Cardiectomy: the failing heart comes out.</summary>
        RemoveNativeHeart,

        /// <summary>The donor heart is placed in the empty pericardium.</summary>
        PlaceDonorHeart,

        /// <summary>The connections that make it a transplant rather than a placement.</summary>
        ConnectVessels,

        /// <summary>The heart is restarted and takes over the circulation.</summary>
        Restart,

        /// <summary>Every stage cleared.</summary>
        Complete,
    }

    /// <summary>
    /// Owns the order of the operation and nothing else.
    ///
    /// Stages advance strictly in sequence, because the sequence is the teaching: you cannot lift
    /// a heart out of a closed chest, and a heart that is in place but unconnected is not a
    /// transplant. Each stage reports its own completion; this only decides what comes next and
    /// when the whole thing is done.
    ///
    /// It deliberately owns no clock. The booth's session owns that, the same way it does for
    /// every other procedure in this project, so the scoreboard and the projection keep reading
    /// one source of truth.
    /// </summary>
    public class TransplantProcedure : MonoBehaviour
    {
        [Tooltip("How many vessel connections the implant stage requires. Five is the real " +
                 "count: left atrium, inferior and superior vena cava, aorta and pulmonary artery.")]
        [SerializeField, Range(2, 5)] private int vesselCount = 5;

        private readonly List<TransplantStage> _order = new List<TransplantStage>
        {
            TransplantStage.OpenChest,
            TransplantStage.RemoveNativeHeart,
            TransplantStage.PlaceDonorHeart,
            TransplantStage.ConnectVessels,
            TransplantStage.Restart,
        };

        public TransplantStage Stage { get; private set; } = TransplantStage.Idle;

        /// <summary>Vessels connected so far. Only meaningful during ConnectVessels.</summary>
        public int VesselsConnected { get; private set; }

        public int VesselCount => vesselCount;

        public bool IsComplete => Stage == TransplantStage.Complete;

        /// <summary>0..1 across the whole operation, for the audience's progress readout.</summary>
        public float Progress01
        {
            get
            {
                if (Stage == TransplantStage.Idle) { return 0f; }
                if (Stage == TransplantStage.Complete) { return 1f; }

                int index = _order.IndexOf(Stage);
                float within = Stage == TransplantStage.ConnectVessels && vesselCount > 0
                    ? VesselsConnected / (float)vesselCount
                    : 0f;

                return Mathf.Clamp01((index + within) / _order.Count);
            }
        }

        public event Action<TransplantStage> StageChanged;
        public event Action<int, int> VesselConnected;
        public event Action ProcedureCompleted;

        public void Begin()
        {
            VesselsConnected = 0;
            SetStage(_order[0]);
        }

        public void ResetProcedure()
        {
            VesselsConnected = 0;
            SetStage(TransplantStage.Idle);
        }

        /// <summary>
        /// Reports a stage finished. Ignored unless it is the stage actually in progress, so an
        /// instrument that fires twice, or fires for a step the visitor has not reached, cannot
        /// skip the operation forward.
        /// </summary>
        public bool CompleteStage(TransplantStage stage)
        {
            if (Stage != stage || Stage == TransplantStage.Complete)
            {
                return false;
            }

            int index = _order.IndexOf(stage);
            if (index < 0)
            {
                return false;
            }

            SetStage(index + 1 < _order.Count ? _order[index + 1] : TransplantStage.Complete);
            return true;
        }

        /// <summary>One vessel anastomosed. Advances the stage once all of them are done.</summary>
        public bool ConnectVessel()
        {
            if (Stage != TransplantStage.ConnectVessels || VesselsConnected >= vesselCount)
            {
                return false;
            }

            VesselsConnected++;
            VesselConnected?.Invoke(VesselsConnected, vesselCount);

            if (VesselsConnected >= vesselCount)
            {
                CompleteStage(TransplantStage.ConnectVessels);
            }

            return true;
        }

        /// <summary>What the room should be telling the visitor to do right now, in pt-BR.</summary>
        public string CurrentInstruction => Stage switch
        {
            TransplantStage.OpenChest => "Abra o tórax com a serra esternal",
            TransplantStage.RemoveNativeHeart => "Retire o coração doente",
            TransplantStage.PlaceDonorHeart => "Posicione o coração do doador",
            TransplantStage.ConnectVessels => $"Conecte os vasos — {VesselsConnected}/{vesselCount}",
            TransplantStage.Restart => "Aplique o choque para religar o coração",
            TransplantStage.Complete => "Coração batendo",
            _ => "Aguardando",
        };

        private void SetStage(TransplantStage next)
        {
            if (next == Stage)
            {
                return;
            }

            Stage = next;
            StageChanged?.Invoke(Stage);

            if (Stage == TransplantStage.Complete)
            {
                ProcedureCompleted?.Invoke();
            }
        }
    }
}
