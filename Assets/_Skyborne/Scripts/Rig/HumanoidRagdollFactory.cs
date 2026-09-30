using System.Collections.Generic;
using UnityEngine;
using Skyborne.Grab;
using Skyborne.NPC;

namespace Skyborne.Rig
{
    /// <summary>
    /// Builds a complete, physically correct humanoid ragdoll from code: anatomical segment
    /// proportions, Dempster-style mass distribution, and joints limited to ranges a real
    /// shoulder, elbow, hip and knee actually have.
    ///
    /// The visual is primitive geometry, not a sculpted character. That is a deliberate
    /// stand-in: what the grab system needs from a body is correct segment lengths, masses and
    /// joint limits, and those are exact here. Swap the visual children for a skinned mesh
    /// later and none of the physics changes.
    /// </summary>
    public static class HumanoidRagdollFactory
    {
        /// <summary>Proportions and masses of one humanoid, all relative to total height/mass.</summary>
        private struct Segment
        {
            public RagdollPart part;
            public RagdollPart parent;
            public bool hasParent;
            public Vector3 proximal;
            public Vector3 distal;
            public float radius;
            public float massFraction;
            public bool isHinge;
            public float twistLow;
            public float twistHigh;
            public float swing1;
            public float swing2;
        }

        private const float ReferenceHeight = 1.75f;

        /// <summary>
        /// Creates a ragdoll humanoid under a new GameObject.
        /// </summary>
        /// <param name="name">Name of the root object.</param>
        /// <param name="position">World position of the feet.</param>
        /// <param name="rotation">Facing.</param>
        /// <param name="height">Total height in metres.</param>
        /// <param name="mass">Total mass in kilograms.</param>
        /// <param name="bodyMaterial">Material for the visual primitives. Optional.</param>
        public static GameObject Create(
            string name,
            Vector3 position,
            Quaternion rotation,
            float height = 1.75f,
            float mass = 72f,
            Material bodyMaterial = null)
        {
            GameObject root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, rotation);

            float scale = height / ReferenceHeight;
            List<Segment> segments = BuildSegmentTable();
            float fractionTotal = 0f;
            for (int i = 0; i < segments.Count; i++)
            {
                fractionTotal += segments[i].massFraction;
            }

            Dictionary<RagdollPart, Transform> transforms = new Dictionary<RagdollPart, Transform>();
            Dictionary<RagdollPart, Rigidbody> bodies = new Dictionary<RagdollPart, Rigidbody>();
            List<RagdollBone> bones = new List<RagdollBone>();

            for (int i = 0; i < segments.Count; i++)
            {
                Segment segment = segments[i];

                Vector3 proximalLocal = segment.proximal * scale;
                Vector3 distalLocal = segment.distal * scale;

                Transform parent = segment.hasParent && transforms.TryGetValue(segment.parent, out Transform p)
                    ? p
                    : root.transform;

                float segmentMass = mass * (segment.massFraction / fractionTotal);

                Transform bone = CreateBone(
                    segment.part.ToString(),
                    parent,
                    root.transform.TransformPoint(proximalLocal),
                    root.transform.TransformPoint(distalLocal),
                    segment.radius * scale,
                    segmentMass,
                    bodyMaterial);

                transforms[segment.part] = bone;
                Rigidbody body = bone.GetComponent<Rigidbody>();
                bodies[segment.part] = body;

                ConfigurableJoint joint = null;
                if (segment.hasParent && bodies.TryGetValue(segment.parent, out Rigidbody parentBody))
                {
                    joint = AttachJoint(
                        body,
                        parentBody,
                        root.transform.TransformPoint(proximalLocal),
                        segment.isHinge,
                        segment.twistLow,
                        segment.twistHigh,
                        segment.swing1,
                        segment.swing2);
                }

                bones.Add(new RagdollBone { part = segment.part, body = body, joint = joint });
            }

            RagdollRig rig = root.AddComponent<RagdollRig>();
            rig.SetBones(bones);

            root.AddComponent<NpcVictim>();
            root.AddComponent<Grabbable>();

            return root;
        }

        /// <summary>
        /// Anatomical table for a 1.75 m body. Y values are heights off the ground, so the numbers
        /// can be checked against a real skeleton rather than eyeballed.
        /// </summary>
        private static List<Segment> BuildSegmentTable()
        {
            const float shoulderX = 0.185f;
            const float elbowX = 0.21f;
            const float wristX = 0.22f;
            const float hipX = 0.09f;
            const float kneeX = 0.10f;
            const float ankleX = 0.105f;

            List<Segment> segments = new List<Segment>
            {
                // Pelvis: the root. No parent, no joint.
                new Segment
                {
                    part = RagdollPart.Hips,
                    hasParent = false,
                    proximal = new Vector3(0f, 0.93f, 0f),
                    distal = new Vector3(0f, 1.08f, 0f),
                    radius = 0.125f,
                    massFraction = 0.142f,
                },
                new Segment
                {
                    part = RagdollPart.Chest, parent = RagdollPart.Hips, hasParent = true,
                    proximal = new Vector3(0f, 1.08f, 0f),
                    distal = new Vector3(0f, 1.43f, 0f),
                    radius = 0.145f,
                    massFraction = 0.216f,
                    twistLow = -22f, twistHigh = 22f, swing1 = 28f, swing2 = 20f,
                },
                new Segment
                {
                    part = RagdollPart.Head, parent = RagdollPart.Chest, hasParent = true,
                    proximal = new Vector3(0f, 1.43f, 0f),
                    distal = new Vector3(0f, 1.65f, 0f),
                    radius = 0.098f,
                    massFraction = 0.081f,
                    twistLow = -55f, twistHigh = 55f, swing1 = 40f, swing2 = 30f,
                },
            };

            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                bool left = side == 0;

                segments.Add(new Segment
                {
                    part = left ? RagdollPart.LeftUpperArm : RagdollPart.RightUpperArm,
                    parent = RagdollPart.Chest, hasParent = true,
                    proximal = new Vector3(sign * shoulderX, 1.41f, 0f),
                    distal = new Vector3(sign * elbowX, 1.12f, 0f),
                    radius = 0.048f,
                    massFraction = 0.028f,
                    // Shoulder: the widest joint in the body.
                    twistLow = -60f, twistHigh = 60f, swing1 = 95f, swing2 = 70f,
                });

                segments.Add(new Segment
                {
                    part = left ? RagdollPart.LeftForearm : RagdollPart.RightForearm,
                    parent = left ? RagdollPart.LeftUpperArm : RagdollPart.RightUpperArm, hasParent = true,
                    proximal = new Vector3(sign * elbowX, 1.12f, 0f),
                    distal = new Vector3(sign * wristX, 0.86f, 0f),
                    radius = 0.04f,
                    // Includes the hand.
                    massFraction = 0.022f,
                    // Elbow: a hinge that bends one way only and does not hyperextend.
                    isHinge = true,
                    twistLow = -5f, twistHigh = 142f, swing1 = 4f, swing2 = 4f,
                });

                segments.Add(new Segment
                {
                    part = left ? RagdollPart.LeftThigh : RagdollPart.RightThigh,
                    parent = RagdollPart.Hips, hasParent = true,
                    proximal = new Vector3(sign * hipX, 0.93f, 0f),
                    distal = new Vector3(sign * kneeX, 0.50f, 0f),
                    radius = 0.072f,
                    massFraction = 0.100f,
                    twistLow = -35f, twistHigh = 35f, swing1 = 85f, swing2 = 40f,
                });

                segments.Add(new Segment
                {
                    part = left ? RagdollPart.LeftCalf : RagdollPart.RightCalf,
                    parent = left ? RagdollPart.LeftThigh : RagdollPart.RightThigh, hasParent = true,
                    proximal = new Vector3(sign * kneeX, 0.50f, 0f),
                    distal = new Vector3(sign * ankleX, 0.07f, 0f),
                    radius = 0.053f,
                    // Includes the foot.
                    massFraction = 0.061f,
                    // Knee: hinge, bends backwards only.
                    isHinge = true,
                    twistLow = -138f, twistHigh = 2f, swing1 = 4f, swing2 = 4f,
                });
            }

            return segments;
        }

        /// <summary>
        /// Creates one bone spanning two world points, oriented so its local +Y runs along the
        /// bone. Every joint below relies on that convention.
        /// </summary>
        private static Transform CreateBone(
            string name,
            Transform parent,
            Vector3 proximal,
            Vector3 distal,
            float radius,
            float mass,
            Material material)
        {
            Vector3 direction = distal - proximal;
            float length = direction.magnitude;
            Vector3 centre = (proximal + distal) * 0.5f;

            GameObject bone = new GameObject(name);
            bone.transform.SetParent(parent, true);
            bone.transform.position = centre;
            // direction is already world space, so this rotation alone puts local +Y along the
            // bone. Composing it with the parent's rotation would apply the facing twice.
            bone.transform.rotation = length > 1e-5f
                ? Quaternion.FromToRotation(Vector3.up, direction.normalized)
                : parent.rotation;

            CapsuleCollider collider = bone.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.radius = radius;
            collider.height = Mathf.Max(length, radius * 2f);

            Rigidbody body = bone.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            bone.AddComponent<RagdollImpactRelay>();

            AddVisual(bone.transform, radius, Mathf.Max(length, radius * 2f), material);

            return bone.transform;
        }

        /// <summary>Visual-only child. Carries no collider, so it never affects the simulation.</summary>
        private static void AddVisual(Transform bone, float radius, float length, Material material)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";

            DestroyComponent(visual.GetComponent<Collider>());

            visual.transform.SetParent(bone, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            // Unity's capsule primitive is 2 units tall and 1 unit across.
            visual.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);

            if (material != null)
            {
                visual.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        /// <summary>
        /// The visual primitive ships with a collider that must go. Which destroy call is legal
        /// depends on whether we are building at runtime or from an editor menu.
        /// </summary>
        private static void DestroyComponent(Component component)
        {
            if (component == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(component);
            }
            else
            {
                Object.DestroyImmediate(component);
            }
        }

        /// <summary>
        /// Joins a bone to its parent. Hinges twist about local X and lock their swing; ball
        /// joints twist about the bone's own axis and swing within an elliptical cone.
        /// </summary>
        private static ConfigurableJoint AttachJoint(
            Rigidbody body,
            Rigidbody parentBody,
            Vector3 anchorWorld,
            bool isHinge,
            float twistLow,
            float twistHigh,
            float swing1,
            float swing2)
        {
            ConfigurableJoint joint = body.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parentBody;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = body.transform.InverseTransformPoint(anchorWorld);
            joint.connectedAnchor = parentBody.transform.InverseTransformPoint(anchorWorld);

            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;

            // A hinge twists about the axis across the joint; a ball joint twists about the bone.
            joint.axis = isHinge ? Vector3.right : Vector3.up;
            joint.secondaryAxis = isHinge ? Vector3.up : Vector3.right;

            joint.lowAngularXLimit = new SoftJointLimit { limit = twistLow };
            joint.highAngularXLimit = new SoftJointLimit { limit = twistHigh };
            joint.angularYLimit = new SoftJointLimit { limit = swing1 };
            joint.angularZLimit = new SoftJointLimit { limit = swing2 };

            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.enablePreprocessing = false;
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.05f;
            joint.projectionAngle = 12f;

            return joint;
        }
    }
}
