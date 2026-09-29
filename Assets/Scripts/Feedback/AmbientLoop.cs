using UnityEngine;

namespace VRSurgery.Feedback
{
    /// <summary>
    /// The room tone of a theatre: ventilation and equipment hum, low enough to go unnoticed and
    /// missed the moment it stops. Synthesised, so there is no audio asset to ship.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AmbientLoop : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float volume = 0.18f;

        private void Start()
        {
            AudioSource source = GetComponent<AudioSource>();
            source.clip = ProceduralTones.RoomHum;
            source.loop = true;
            source.volume = volume;
            source.spatialBlend = 0f;
            source.Play();
        }
    }
}
