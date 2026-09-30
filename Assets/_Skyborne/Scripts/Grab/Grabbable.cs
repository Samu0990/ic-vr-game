using System.Collections.Generic;
using UnityEngine;
using Skyborne.NPC;

namespace Skyborne.Grab
{
    /// <summary>
    /// Marks a ragdoll as something the flyer can pick up, and decides which bone a given hand
    /// position should actually close around.
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    public class Grabbable : MonoBehaviour
    {
        /// <summary>
        /// How well a given bone can be held. A torso grip is controllable; a wrist grip means
        /// the whole body hangs off one joint and swings, which is harder to fly with.
        /// </summary>
        [System.Serializable]
        public struct GripProfile
        {
            public RagdollPart part;

            [Tooltip("0..1. High = a secure hold that barely swings and is hard to tear loose.")]
            [Range(0f, 1f)] public float stability;

            [Tooltip("Metres of bias subtracted from this bone's distance when choosing a grip. " +
                     "Lets the torso win over a fingertip-close wrist.")]
            public float preferenceBias;
        }

        [SerializeField]
        private List<GripProfile> gripProfiles = new List<GripProfile>
        {
            new GripProfile { part = RagdollPart.Chest, stability = 0.95f, preferenceBias = 0.35f },
            new GripProfile { part = RagdollPart.Hips, stability = 0.85f, preferenceBias = 0.2f },
            new GripProfile { part = RagdollPart.LeftUpperArm, stability = 0.5f, preferenceBias = 0f },
            new GripProfile { part = RagdollPart.RightUpperArm, stability = 0.5f, preferenceBias = 0f },
            new GripProfile { part = RagdollPart.LeftForearm, stability = 0.3f, preferenceBias = 0f },
            new GripProfile { part = RagdollPart.RightForearm, stability = 0.3f, preferenceBias = 0f },
            new GripProfile { part = RagdollPart.LeftThigh, stability = 0.45f, preferenceBias = 0f },
            new GripProfile { part = RagdollPart.RightThigh, stability = 0.45f, preferenceBias = 0f },
            new GripProfile { part = RagdollPart.LeftCalf, stability = 0.25f, preferenceBias = 0f },
            new GripProfile { part = RagdollPart.RightCalf, stability = 0.25f, preferenceBias = 0f },
        };

        private RagdollRig _rig;
        private NpcVictim _victim;

        public RagdollRig Rig => _rig != null ? _rig : _rig = GetComponent<RagdollRig>();

        public NpcVictim Victim => _victim != null ? _victim : _victim = GetComponent<NpcVictim>();

        /// <summary>Total mass the flyer has to haul if it picks this body up.</summary>
        public float Mass => Rig.TotalMass;

        /// <summary>True while something is holding this body.</summary>
        public bool IsHeld { get; private set; }

        /// <summary>
        /// Picks the bone a hand at <paramref name="handPosition"/> should close around, biased
        /// toward the parts a person would actually grab someone by.
        /// </summary>
        public bool TryPickGrip(Vector3 handPosition, out Rigidbody bone, out float stability)
        {
            bone = null;
            stability = 0f;

            float bestScore = float.MaxValue;

            for (int i = 0; i < gripProfiles.Count; i++)
            {
                GripProfile profile = gripProfiles[i];
                if (!Rig.TryGet(profile.part, out RagdollBone candidate) || candidate.body == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(candidate.body.worldCenterOfMass, handPosition);
                float score = distance - profile.preferenceBias;

                if (score < bestScore)
                {
                    bestScore = score;
                    bone = candidate.body;
                    stability = profile.stability;
                }
            }

            // Nothing profiled resolved: fall back to whatever bone is physically nearest, so a
            // rig assembled by hand still works without being fully configured.
            if (bone == null)
            {
                bone = Rig.FindNearestBody(handPosition);
                stability = 0.4f;
            }

            return bone != null;
        }

        /// <summary>Every collider on the ragdoll, so the carrier can stop colliding with it.</summary>
        public void CollectColliders(List<Collider> into)
        {
            into.Clear();
            GetComponentsInChildren(true, into);
        }

        internal void MarkHeld(bool held)
        {
            IsHeld = held;
            if (Victim == null)
            {
                return;
            }

            if (held)
            {
                Victim.OnGrabbed();
            }
            else
            {
                Victim.OnReleased();
            }
        }
    }
}
