using UnityEngine;
using VRSurgery.Haptics;
using VRSurgery.Interaction;

namespace VRSurgery.Diagnostics
{
    /// <summary>
    /// A hand that isn't attached to any hardware. Implements the same contract as the real
    /// XR hand so automated probes and tests can exercise the full grab/cut/feedback chain on
    /// a machine with no headset attached — which is the only way this project can be verified
    /// in CI or on a dev box between hardware sessions.
    /// </summary>
    public class ProbeHand : MonoBehaviour, IHandInteractor
    {
        public SurgicalInteractable HeldObject { get; private set; }

        /// <summary>Haptic pulses this hand was asked to play. Asserted on by tests.</summary>
        public int HapticPulseCount { get; private set; }

        public HapticProfile LastHapticProfile { get; private set; }

        public bool TryGrab(SurgicalInteractable target)
        {
            if (target == null || target.IsHeld)
            {
                return false;
            }

            HeldObject = target;
            target.OnGrabbed(this);
            return true;
        }

        public void Release()
        {
            if (HeldObject == null)
            {
                return;
            }

            SurgicalInteractable previous = HeldObject;
            HeldObject = null;
            previous.OnReleased();
        }

        public void Hover(SurgicalInteractable target) { }

        public void Activate() { }

        public void GetPose(out Vector3 position, out Quaternion rotation)
        {
            position = transform.position;
            rotation = transform.rotation;
        }

        public void SendHapticFeedback(HapticProfile profile)
        {
            HapticPulseCount++;
            LastHapticProfile = profile;
        }
    }
}
