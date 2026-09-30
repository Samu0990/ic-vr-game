using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skyborne.NPC
{
    /// <summary>The physical bones this project builds a ragdoll out of.</summary>
    public enum RagdollPart
    {
        Hips,
        Chest,
        Head,
        LeftUpperArm,
        LeftForearm,
        RightUpperArm,
        RightForearm,
        LeftThigh,
        LeftCalf,
        RightThigh,
        RightCalf,
    }

    /// <summary>One simulated bone: its body and its joint to the parent.</summary>
    [Serializable]
    public struct RagdollBone
    {
        public RagdollPart part;
        public Rigidbody body;
        public ConfigurableJoint joint;
    }

    /// <summary>
    /// Owns a humanoid ragdoll and its muscle tone. Tone is the single dial between a person
    /// standing under their own power (1) and a body that has gone completely limp (0);
    /// everything in between is someone losing the fight, which is what a struggle looks like.
    /// </summary>
    public class RagdollRig : MonoBehaviour
    {
        [SerializeField] private List<RagdollBone> bones = new List<RagdollBone>();

        [Tooltip("Joint drive spring at full muscle tone.")]
        [SerializeField] private float maxMuscleSpring = 900f;

        [Tooltip("Joint drive damper at full muscle tone.")]
        [SerializeField] private float maxMuscleDamper = 55f;

        [Tooltip("Damper kept even when fully limp, so a limp body still swings rather than rattles.")]
        [SerializeField] private float limpDamper = 6f;

        private readonly Dictionary<RagdollPart, RagdollBone> _lookup = new Dictionary<RagdollPart, RagdollBone>();
        private float _muscleTone = 1f;
        private float _totalMass;

        /// <summary>0 = fully limp, 1 = holding its own pose.</summary>
        public float MuscleTone => _muscleTone;

        /// <summary>Sum of every bone's mass. This is what the flyer actually has to haul.</summary>
        public float TotalMass => _totalMass;

        public IReadOnlyList<RagdollBone> Bones => bones;

        public Rigidbody Root => TryGet(RagdollPart.Hips, out RagdollBone hips) ? hips.body : null;

        public Rigidbody Chest => TryGet(RagdollPart.Chest, out RagdollBone chest) ? chest.body : null;

        private void Awake()
        {
            Rebuild();
        }

        /// <summary>
        /// Re-reads the bone list. Called on Awake, and by the factory after it assembles a rig.
        /// </summary>
        public void Rebuild()
        {
            _lookup.Clear();
            _totalMass = 0f;

            for (int i = 0; i < bones.Count; i++)
            {
                RagdollBone bone = bones[i];
                if (bone.body == null)
                {
                    continue;
                }

                _lookup[bone.part] = bone;
                _totalMass += bone.body.mass;

                if (bone.joint != null)
                {
                    bone.joint.rotationDriveMode = RotationDriveMode.Slerp;
                }
            }

            SetMuscleTone(_muscleTone);
        }

        public void SetBones(List<RagdollBone> newBones)
        {
            bones = newBones ?? new List<RagdollBone>();
            Rebuild();
        }

        public bool TryGet(RagdollPart part, out RagdollBone bone)
        {
            return _lookup.TryGetValue(part, out bone);
        }

        /// <summary>
        /// Sets how hard the body holds its pose. Dropping this to 0 is what makes a knocked-out
        /// person go heavy and fold, instead of staying rigid while they fall.
        /// </summary>
        public void SetMuscleTone(float tone)
        {
            _muscleTone = Mathf.Clamp01(tone);

            JointDrive drive = new JointDrive
            {
                positionSpring = maxMuscleSpring * _muscleTone,
                positionDamper = Mathf.Lerp(limpDamper, maxMuscleDamper, _muscleTone),
                maximumForce = float.MaxValue,
            };

            for (int i = 0; i < bones.Count; i++)
            {
                ConfigurableJoint joint = bones[i].joint;
                if (joint != null)
                {
                    joint.slerpDrive = drive;
                }
            }
        }

        /// <summary>
        /// Offsets a single joint away from its rest pose. This is the primitive the struggle
        /// behaviour writes through: real torque through a real joint, not an animation.
        /// </summary>
        public void SetJointTarget(RagdollPart part, Quaternion offsetFromRest)
        {
            if (_lookup.TryGetValue(part, out RagdollBone bone) && bone.joint != null)
            {
                bone.joint.targetRotation = offsetFromRest;
            }
        }

        /// <summary>Clears every joint offset back to the rest pose.</summary>
        public void ResetJointTargets()
        {
            for (int i = 0; i < bones.Count; i++)
            {
                ConfigurableJoint joint = bones[i].joint;
                if (joint != null)
                {
                    joint.targetRotation = Quaternion.identity;
                }
            }
        }

        /// <summary>Bone whose centre of mass is closest to a world point. Used to pick a grip.</summary>
        public Rigidbody FindNearestBody(Vector3 worldPoint)
        {
            Rigidbody best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < bones.Count; i++)
            {
                Rigidbody body = bones[i].body;
                if (body == null)
                {
                    continue;
                }

                float sqr = (body.worldCenterOfMass - worldPoint).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = body;
                }
            }

            return best;
        }

        /// <summary>Average velocity across every bone, i.e. how fast the body as a whole is moving.</summary>
        public Vector3 AverageVelocity()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;

            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].body != null)
                {
                    sum += bones[i].body.linearVelocity;
                    count++;
                }
            }

            return count > 0 ? sum / count : Vector3.zero;
        }

        /// <summary>Sets velocity on every bone at once. Used by the playground reset.</summary>
        public void SetVelocity(Vector3 velocity)
        {
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].body != null)
                {
                    bones[i].body.linearVelocity = velocity;
                }
            }
        }

        /// <summary>Impulse spread across the whole body, scaled per bone by its own mass.</summary>
        public void AddImpulse(Vector3 impulse)
        {
            if (_totalMass <= 0f)
            {
                return;
            }

            for (int i = 0; i < bones.Count; i++)
            {
                Rigidbody body = bones[i].body;
                if (body != null)
                {
                    body.AddForce(impulse * (body.mass / _totalMass), ForceMode.Impulse);
                }
            }
        }
    }
}
