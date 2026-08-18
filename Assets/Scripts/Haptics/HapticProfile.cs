using UnityEngine;

namespace VRSurgery.Haptics
{
    public enum HapticIntensity
    {
        Soft,
        Medium,
        Strong,
        Critical,
    }

    [CreateAssetMenu(fileName = "HapticProfile", menuName = "VRSurgery/Haptics/Haptic Profile")]
    public class HapticProfile : ScriptableObject
    {
        [SerializeField] private HapticIntensity intensity = HapticIntensity.Soft;
        [SerializeField, Range(0f, 1f)] private float amplitude = 0.3f;
        [SerializeField, Range(0f, 1f)] private float duration = 0.05f;

        public HapticIntensity Intensity => intensity;
        public float Amplitude => amplitude;
        public float Duration => duration;

        /// <summary>Builds a profile in memory. Used by the scene builder and by tests.</summary>
        public static HapticProfile Create(HapticIntensity profileIntensity, float profileAmplitude, float profileDuration)
        {
            HapticProfile profile = CreateInstance<HapticProfile>();
            profile.intensity = profileIntensity;
            profile.amplitude = Mathf.Clamp01(profileAmplitude);
            profile.duration = Mathf.Clamp01(profileDuration);
            return profile;
        }
    }
}
