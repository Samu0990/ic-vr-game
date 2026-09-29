using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// Curls the fingers of a rigged hand from two numbers: how hard the grip button is squeezed
    /// and how far the trigger is pulled. Grip closes the middle, ring and little fingers and
    /// brings the thumb over; the trigger bends the index finger. Let go and the hand relaxes.
    ///
    /// No animation clips and no Animator: each finger joint turns about its own knuckle axis,
    /// found once from the model's rest pose (the axis across the bone that brings the tip toward
    /// the palm). So it works on any hand whose bones are named like the OpenXR joints — the XR
    /// Hands sample hands this project uses ("L_IndexProximal", "R_ThumbDistal"...) — without
    /// knowing how the model was authored. On a Quest this is a few dozen quaternion multiplies
    /// per frame, far cheaper than an Animator.
    /// </summary>
    public class HandPoser : MonoBehaviour
    {
        public const int IndexGroup = 0;
        public const int GripGroup = 1;
        public const int ThumbGroup = 2;

        private static readonly string[] GripFingers = { "Middle", "Ring", "Little" };
        private static readonly string[] FingerJoints = { "Proximal", "Intermediate", "Distal", "Tip" };
        private static readonly string[] ThumbJoints = { "Metacarpal", "Proximal", "Distal", "Tip" };

        [SerializeField] private bool isLeftHand;

        [Header("Bend at full squeeze (degrees): base, middle and tip joint")]
        [SerializeField] private Vector3 fingerCurl = new Vector3(68f, 92f, 58f);
        [SerializeField] private Vector3 indexCurl = new Vector3(58f, 82f, 50f);
        [SerializeField] private Vector3 thumbCurl = new Vector3(22f, 32f, 38f);

        [Tooltip("How curled a hand with nothing pressed stays: a relaxed hand is never flat.")]
        [SerializeField, Range(0f, 0.4f)] private float relaxed = 0.14f;

        [Tooltip("How quickly the fingers follow the buttons, per second.")]
        [SerializeField, Min(1f)] private float speed = 16f;

        private struct Joint
        {
            public Transform Bone;
            public Quaternion Rest;
            public Vector3 Axis;
            public float Degrees;
            public int Group;
        }

        private readonly List<Joint> _joints = new List<Joint>();
        private readonly float[] _curl = new float[3];
        private bool _calibrated;

        public bool IsLeftHand => isLeftHand;
        public bool IsCalibrated => _calibrated;
        public int JointCount => _joints.Count;

        /// <summary>Current curl of a group, 0 open to 1 closed.</summary>
        public float Curl(int group) => _curl[Mathf.Clamp(group, 0, 2)];

        private void Awake() => Calibrate();

        public void Bind(bool leftHand)
        {
            // Back to the rest pose first: calibrating reads the axes from it.
            Restore();
            isLeftHand = leftHand;
            _calibrated = false;
        }

        /// <summary>
        /// Reads the joint axes from the pose the hand is in now, which must be its rest pose.
        /// Called once; calling again is harmless as long as the fingers have been put back.
        /// </summary>
        public void Calibrate()
        {
            if (_calibrated) { return; }
            _joints.Clear();

            Transform wrist = FindBone(transform, "Wrist");
            Transform index = FindBone(transform, "IndexProximal");
            Transform little = FindBone(transform, "LittleProximal");
            if (wrist == null || index == null || little == null)
            {
                Debug.LogWarning($"[Mão] '{name}': sem ossos Wrist/IndexProximal/LittleProximal; os dedos ficam parados.");
                return;
            }

            Vector3 palm = PalmNormal(wrist.position, index.position, little.position, isLeftHand);

            AddFinger("Index", FingerJoints, indexCurl, IndexGroup, palm);
            foreach (string finger in GripFingers) { AddFinger(finger, FingerJoints, fingerCurl, GripGroup, palm); }
            AddFinger("Thumb", ThumbJoints, thumbCurl, ThumbGroup, palm);

            for (int i = 0; i < _curl.Length; i++) { _curl[i] = relaxed; }
            _calibrated = _joints.Count > 0;
            Apply();
        }

        /// <summary>
        /// The way the palm faces, out of the palm, from where the wrist and the two outer
        /// knuckles are. The cross product of the knuckle directions points out of the back of a
        /// right hand and out of the palm of a left one.
        /// </summary>
        public static Vector3 PalmNormal(Vector3 wrist, Vector3 indexKnuckle, Vector3 littleKnuckle, bool leftHand)
        {
            Vector3 across = Vector3.Cross(indexKnuckle - wrist, littleKnuckle - wrist).normalized;
            return leftHand ? across : -across;
        }

        private void AddFinger(string finger, string[] joints, Vector3 degrees, int group, Vector3 palm)
        {
            for (int i = 0; i < 3; i++)
            {
                Transform bone = FindBone(transform, finger + joints[i]);
                Transform next = FindBone(transform, finger + joints[i + 1]);
                if (bone == null || next == null) { continue; }

                Vector3 along = next.position - bone.position;
                if (along.sqrMagnitude < 1e-10f) { continue; }

                Vector3 axis = Vector3.Cross(along.normalized, palm);
                if (axis.sqrMagnitude < 1e-8f) { continue; }
                axis.Normalize();

                // Whichever way round brings the tip toward the palm side is the bending way.
                Vector3 bent = bone.position + Quaternion.AngleAxis(10f, axis) * along;
                if (Vector3.Dot(bent - next.position, palm) < 0f) { axis = -axis; }

                _joints.Add(new Joint
                {
                    Bone = bone,
                    Rest = bone.localRotation,
                    Axis = Quaternion.Inverse(bone.rotation) * axis,
                    Degrees = i == 0 ? degrees.x : i == 1 ? degrees.y : degrees.z,
                    Group = group,
                });
            }
        }

        /// <summary>
        /// Moves the fingers toward the pose the buttons ask for. Grip and trigger are 0..1.
        /// Public so tests and the controller hand can drive it.
        /// </summary>
        public void Tick(float deltaTime, float grip, float trigger)
        {
            if (!_calibrated) { return; }

            grip = Mathf.Clamp01(grip);
            trigger = Mathf.Clamp01(trigger);
            float step = deltaTime <= 0f ? 1f : Mathf.Clamp01(deltaTime * speed);

            float thumb = Mathf.Max(grip * 0.9f, trigger * 0.35f);
            Approach(IndexGroup, Mathf.Lerp(relaxed, 1f, trigger), step);
            Approach(GripGroup, Mathf.Lerp(relaxed, 1f, grip), step);
            Approach(ThumbGroup, Mathf.Lerp(relaxed, 1f, thumb), step);
            Apply();
        }

        private void Approach(int group, float target, float step) =>
            _curl[group] = Mathf.Lerp(_curl[group], target, step);

        private void Apply()
        {
            for (int i = 0; i < _joints.Count; i++)
            {
                Joint joint = _joints[i];
                if (joint.Bone == null) { continue; }
                joint.Bone.localRotation = joint.Rest * Quaternion.AngleAxis(joint.Degrees * _curl[joint.Group], joint.Axis);
            }
        }

        /// <summary>Puts every finger back where the model has it, for re-aligning or saving.</summary>
        public void Restore()
        {
            for (int i = 0; i < _joints.Count; i++)
            {
                if (_joints[i].Bone != null) { _joints[i].Bone.localRotation = _joints[i].Rest; }
            }
        }

        /// <summary>A bone by its OpenXR joint name, whatever side prefix the model gives it.</summary>
        public static Transform FindBone(Transform root, string joint)
        {
            if (root == null) { return null; }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == joint || n.EndsWith("_" + joint, System.StringComparison.Ordinal)) { return t; }
            }

            return null;
        }
    }
}
