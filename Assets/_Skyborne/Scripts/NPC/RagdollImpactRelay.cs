using System;
using UnityEngine;

namespace Skyborne.NPC
{
    /// <summary>
    /// Sits on each ragdoll bone and forwards its collisions up to the owner. Bones collide
    /// individually, so without this the body has no idea a knee just hit a wall at 40 m/s.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RagdollImpactRelay : MonoBehaviour
    {
        /// <summary>Raised with the relative impact speed in m/s and the contact point.</summary>
        public event Action<float, Vector3> Impacted;

        [Tooltip("Impacts slower than this are footsteps and brushing, not collisions worth reporting.")]
        [SerializeField] private float minimumReportedSpeed = 3.5f;

        private void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (speed < minimumReportedSpeed)
            {
                return;
            }

            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            Impacted?.Invoke(speed, point);
        }
    }
}
