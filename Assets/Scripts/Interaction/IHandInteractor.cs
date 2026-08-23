using UnityEngine;
using VRSurgery.Haptics;

namespace VRSurgery.Interaction
{
    public interface IHandInteractor
    {
        bool TryGrab(SurgicalInteractable target);
        void Release();
        void Hover(SurgicalInteractable target);
        void Activate();
        void GetPose(out Vector3 position, out Quaternion rotation);
        void SendHapticFeedback(HapticProfile profile);
    }
}
