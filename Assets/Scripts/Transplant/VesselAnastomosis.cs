using System;
using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Which vessel a connection point stands for. Named because the audience screen and the
    /// surgeon's prompt both say them out loud, and because the order they are sewn in is not
    /// arbitrary — the left atrium goes first, deep and hardest to reach, and the great arteries
    /// last.
    /// </summary>
    public enum VesselSite
    {
        LeftAtrium,
        InferiorVenaCava,
        SuperiorVenaCava,
        Aorta,
        PulmonaryArtery,
    }

    /// <summary>
    /// One anastomosis: a place on the implanted heart that has to be joined to the recipient.
    ///
    /// Held, not tapped. The instrument has to stay inside the site for a moment, the same way
    /// the gauze has to stay on a bleeding port, because a connection that completes the instant
    /// something brushes past it turns five careful joins into five accidents. Progress is exposed
    /// so the projection can show a vessel filling rather than blinking.
    ///
    /// It knows nothing about hands or XR. Something else tells it what is touching it, which is
    /// what lets a test drive it with no rig in the scene.
    /// </summary>
    public class VesselAnastomosis : MonoBehaviour
    {
        [SerializeField] private VesselSite site = VesselSite.LeftAtrium;

        [Tooltip("How close the instrument has to be to count as working on this join, in metres.")]
        [SerializeField, Min(0.005f)] private float radius = 0.025f;

        [Tooltip("Seconds of steady contact to complete the join.")]
        [SerializeField, Min(0.1f)] private float secondsToJoin = 1.4f;

        [SerializeField] private TransplantProcedure procedure;

        public VesselSite Site => site;
        public float Radius => radius;

        /// <summary>0 until started, 1 when joined. What the audience's readout should follow.</summary>
        public float Progress01 { get; private set; }

        public bool IsJoined { get; private set; }

        public event Action<VesselAnastomosis> Joined;

        private float _held;

        /// <summary>
        /// Feeds the join. <paramref name="worldPoint"/> is wherever the instrument tip is; pass
        /// nothing in a frame and the join pauses rather than resets.
        /// </summary>
        public void Work(Vector3 worldPoint, float deltaTime)
        {
            if (IsJoined || deltaTime <= 0f)
            {
                return;
            }

            if (Vector3.Distance(worldPoint, transform.position) > radius)
            {
                // Drifting off the site loses the moment's work but not the join. Resetting to
                // zero would punish a visitor whose hand wobbles, which at a stand is everyone.
                _held = Mathf.Max(0f, _held - deltaTime);
                Progress01 = Mathf.Clamp01(_held / secondsToJoin);
                return;
            }

            _held += deltaTime;
            Progress01 = Mathf.Clamp01(_held / secondsToJoin);

            if (_held >= secondsToJoin)
            {
                Complete();
            }
        }

        private void Complete()
        {
            IsJoined = true;
            Progress01 = 1f;
            Joined?.Invoke(this);

            if (procedure != null)
            {
                procedure.ConnectVessel();
            }
        }

        public void ResetJoin()
        {
            IsJoined = false;
            Progress01 = 0f;
            _held = 0f;
        }

        public void Bind(VesselSite vessel, TransplantProcedure transplant, float siteRadius)
        {
            site = vessel;
            procedure = transplant;
            radius = Mathf.Max(0.005f, siteRadius);
        }

        /// <summary>Name for the surgeon's prompt and the audience screen, in pt-BR.</summary>
        public string DisplayName => site switch
        {
            VesselSite.LeftAtrium => "átrio esquerdo",
            VesselSite.InferiorVenaCava => "veia cava inferior",
            VesselSite.SuperiorVenaCava => "veia cava superior",
            VesselSite.Aorta => "aorta",
            VesselSite.PulmonaryArtery => "artéria pulmonar",
            _ => site.ToString(),
        };
    }
}
