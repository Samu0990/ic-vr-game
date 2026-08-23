using UnityEngine;

namespace VRSurgery.VR
{
    /// <summary>
    /// Central switchboard for locomotion options. This game's core loop needs none of them
    /// (zero-locomotion is a design pillar, not a placeholder) but hardware/comfort needs vary,
    /// so each mode is independently toggleable rather than hardcoded off.
    /// </summary>
    public class LocomotionManager : MonoBehaviour
    {
        [SerializeField] private bool teleportEnabled = false;
        [SerializeField] private bool smoothMovementEnabled = false;
        [SerializeField] private bool snapTurnEnabled = true;
        [SerializeField] private bool smoothTurnEnabled = false;
        [SerializeField] private bool comfortModeEnabled = true;

        public bool TeleportEnabled => teleportEnabled;
        public bool SmoothMovementEnabled => smoothMovementEnabled;
        public bool SnapTurnEnabled => snapTurnEnabled;
        public bool SmoothTurnEnabled => smoothTurnEnabled;
        public bool ComfortModeEnabled => comfortModeEnabled;

        public void SetTeleportEnabled(bool value) => teleportEnabled = value;
        public void SetSmoothMovementEnabled(bool value) => smoothMovementEnabled = value;
        public void SetSnapTurnEnabled(bool value) => snapTurnEnabled = value;
        public void SetSmoothTurnEnabled(bool value) => smoothTurnEnabled = value;
        public void SetComfortModeEnabled(bool value) => comfortModeEnabled = value;
    }
}
