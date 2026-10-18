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

        /// <summary>Os cinco dedos, na ordem em que este componente os indexa.</summary>
        public const int Thumb = 0;
        public const int Index = 1;
        public const int Middle = 2;
        public const int Ring = 3;
        public const int Little = 4;
        public const int FingerCount = 5;

        private static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };

        private static readonly string[] FingerJoints = { "Proximal", "Intermediate", "Distal", "Tip" };
        private static readonly string[] ThumbJoints = { "Metacarpal", "Proximal", "Distal", "Tip" };

        [SerializeField] private bool isLeftHand;

        [Header("Bend at full squeeze (degrees): base, middle and tip joint")]
        [SerializeField] private Vector3 fingerCurl = new Vector3(68f, 92f, 58f);
        [SerializeField] private Vector3 indexCurl = new Vector3(58f, 82f, 50f);
        // The original range stopped before this rig's thumb reached a slim handle: at full
        // closure its tip was still 22.5 mm from a 7 mm scalpel. This opposition range leaves
        // a small glove clearance without making the thumb fold through the instrument.
        [SerializeField] private Vector3 thumbCurl = new Vector3(35f, 51f, 61f);

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
            public int Finger;
        }

        private readonly List<Joint> _joints = new List<Joint>();
        private readonly float[] _curl = new float[FingerCount];
        private readonly float[] _targets = new float[FingerCount];
        private readonly Transform[] _tips = new Transform[FingerCount];
        private bool _calibrated;

        public bool IsLeftHand => isLeftHand;
        public bool IsCalibrated => _calibrated;
        public int JointCount => _joints.Count;

        /// <summary>Current curl of a group, 0 open to 1 closed.</summary>
        public float Curl(int group) => _curl[RepresentativeFinger(group)];

        /// <summary>Current curl of one finger, 0 open to 1 closed.</summary>
        public float FingerCurl(int finger) => _curl[Mathf.Clamp(finger, 0, FingerCount - 1)];

        /// <summary>The finger whose curl stands for a whole group in the older three-group API.</summary>
        private static int RepresentativeFinger(int group) =>
            group == IndexGroup ? Index : group == ThumbGroup ? Thumb : Middle;

        /// <summary>World position of a fingertip, or <see cref="Vector3.zero"/> if the bone is missing.</summary>
        public Vector3 TipPosition(int finger)
        {
            Transform tip = _tips[Mathf.Clamp(finger, 0, FingerCount - 1)];
            return tip != null ? tip.position : Vector3.zero;
        }

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

            AddFinger("Index", FingerJoints, indexCurl, IndexGroup, Index, palm);
            AddFinger("Middle", FingerJoints, fingerCurl, GripGroup, Middle, palm);
            AddFinger("Ring", FingerJoints, fingerCurl, GripGroup, Ring, palm);
            AddFinger("Little", FingerJoints, fingerCurl, GripGroup, Little, palm);
            AddFinger("Thumb", ThumbJoints, thumbCurl, ThumbGroup, Thumb, ThumbClosing(index, little, palm));

            // Sem este cache, o solver procurava cada ponta pelo nome 240 vezes por pega, e cada
            // busca varria a hierarquia inteira alocando um array. No Quest isso é um engasgo de
            // GC toda vez que se pega um instrumento.
            for (int i = 0; i < FingerCount; i++) { _tips[i] = FindBone(transform, FingerNames[i] + "Tip"); }

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

        /// <summary>
        /// The way a thumb tip travels as it closes.
        ///
        /// A thumb does not curl into the palm the way the fingers do: it opposes, swinging
        /// across the palm toward them. Bending it about the finger axis sends it the other way
        /// — measured on this rig, a squeezed thumb ended up 77 mm from a held scalpel while the
        /// four fingers were sitting on it within 10 mm.
        /// </summary>
        private static Vector3 ThumbClosing(Transform indexKnuckle, Transform littleKnuckle, Vector3 palm)
        {
            // Across the palm, from the thumb's own side toward the little finger. The small
            // inward component is opposite the outward palm normal: on this rig adding the palm
            // normal sent the thumb behind the hand and made it move farther from the handle as
            // it closed (33 mm open, 47 mm half closed).
            //
            // Closing it along the palm normal, the way the four fingers close, drives it the
            // wrong way: the thumb starts out wide of the grip axis, so moving it into the palm
            // only increases that offset — measured, it went from 33 mm to 77 mm as it squeezed.
            // Running it down the hand (wrist toward the index knuckle) is no better, since that
            // is almost pure length and barely changes the offset at all.
            Vector3 across = (littleKnuckle.position - indexKnuckle.position).normalized;
            return (across - palm * 0.4f).normalized;
        }

        /// <param name="closing">Direction the tip should travel as this finger closes.</param>
        private void AddFinger(string finger, string[] joints, Vector3 degrees, int group, int fingerIndex, Vector3 closing)
        {
            for (int i = 0; i < 3; i++)
            {
                Transform bone = FindBone(transform, finger + joints[i]);
                Transform next = FindBone(transform, finger + joints[i + 1]);
                if (bone == null || next == null) { continue; }

                Vector3 along = next.position - bone.position;
                if (along.sqrMagnitude < 1e-10f) { continue; }

                Vector3 axis = Vector3.Cross(along.normalized, closing);
                if (axis.sqrMagnitude < 1e-8f) { continue; }
                axis.Normalize();

                // Whichever way round brings the tip toward the closing direction is the bending way.
                Vector3 bent = bone.position + Quaternion.AngleAxis(10f, axis) * along;
                if (Vector3.Dot(bent - next.position, closing) < 0f) { axis = -axis; }

                _joints.Add(new Joint
                {
                    Bone = bone,
                    Rest = bone.localRotation,
                    Axis = Quaternion.Inverse(bone.rotation) * axis,
                    Degrees = i == 0 ? degrees.x : i == 1 ? degrees.y : degrees.z,
                    Group = group,
                    Finger = fingerIndex,
                });
            }
        }

        /// <summary>
        /// Moves the fingers toward the pose the buttons ask for. Grip and trigger are 0..1.
        /// Public so tests and the controller hand can drive it.
        /// </summary>
        public void Tick(float deltaTime, float grip, float trigger)
        {
            grip = Mathf.Clamp01(grip);
            trigger = Mathf.Clamp01(trigger);
            float thumb = Mathf.Max(grip * 0.9f, trigger * 0.35f);

            TickTo(
                deltaTime,
                Mathf.Lerp(relaxed, 1f, trigger),
                Mathf.Lerp(relaxed, 1f, grip),
                Mathf.Lerp(relaxed, 1f, thumb));
        }

        /// <summary>Moves each finger toward its own closure. The path used while holding.</summary>
        public void TickFingers(float deltaTime, IReadOnlyList<float> closures)
        {
            if (!_calibrated || closures == null) { return; }

            float step = deltaTime <= 0f ? 1f : Mathf.Clamp01(deltaTime * speed);
            int count = Mathf.Min(closures.Count, FingerCount);
            for (int finger = 0; finger < count; finger++)
            {
                _curl[finger] = Mathf.Lerp(_curl[finger], Mathf.Clamp01(closures[finger]), step);
            }

            Apply();
        }

        /// <summary>
        /// Finds how far this finger has to close for its tip to land on the surface of a handle
        /// lying along a given axis.
        ///
        /// Solved against the actual rig rather than guessed, because how far a finger has to go
        /// depends on the model's bone lengths and on where the handle sits, and the answer is
        /// different for every finger: on this hand the middle finger meets a scalpel at about
        /// 0.8 closed while the little finger never reaches it at all.
        ///
        /// Only the approach is searched. Past its closest point a curling finger passes the
        /// handle and swings back out the other side, and that far branch is a fist closed
        /// through the steel, never a grip on it.
        /// </summary>
        public float SolveClosure(int finger, Vector3 axisOrigin, Vector3 axisDirection, float targetRadius,
            out float achievedError)
        {
            achievedError = float.MaxValue;
            if (!_calibrated) { return 0f; }

            finger = Mathf.Clamp(finger, 0, FingerCount - 1);

            // Sem o osso da ponta não há o que medir, e resolver às cegas devolveria um
            // fechamento qualquer com cara de resposta.
            if (_tips[finger] == null) { return _curl[finger]; }

            float restore = _curl[finger];

            const int steps = 48;
            float bestClosure = 0f;
            float bestError = float.MaxValue;
            float previousDistance = float.MaxValue;

            for (int i = 0; i <= steps; i++)
            {
                float closure = i / (float)steps;
                SetFingerImmediate(finger, closure);

                float distance = DistanceToAxis(TipPosition(finger), axisOrigin, axisDirection);
                float error = Mathf.Abs(distance - targetRadius);
                if (error < bestError)
                {
                    bestError = error;
                    bestClosure = closure;
                }

                // The first outside-to-inside crossing is physical contact with the handle.
                // Stop there so a later part of the curl cannot be selected after the fingertip
                // has already passed through the steel. Do not stop on a small initial rise:
                // several fingers in this rig move out before their knuckles turn them inward,
                // and the old early-out consequently returned closure zero for different fingers.
                if (i > 0 && previousDistance > targetRadius && distance <= targetRadius)
                {
                    float span = previousDistance - distance;
                    float acrossStep = span > 1e-6f
                        ? Mathf.Clamp01((previousDistance - targetRadius) / span)
                        : 1f;
                    bestClosure = (i - 1 + acrossStep) / steps;
                    bestError = 0f;
                    break;
                }

                previousDistance = distance;
            }

            SetFingerImmediate(finger, restore);
            achievedError = bestError;
            return bestClosure;
        }

        /// <summary>
        /// How far this finger's tip would sit from an axis at a given closure. Lets the caller
        /// compare candidate poses instead of trusting a fixed tolerance.
        /// </summary>
        public float DistanceAt(int finger, float closure, Vector3 axisOrigin, Vector3 axisDirection)
        {
            if (!_calibrated) { return float.MaxValue; }

            finger = Mathf.Clamp(finger, 0, FingerCount - 1);
            float restore = _curl[finger];

            SetFingerImmediate(finger, closure);
            float distance = DistanceToAxis(TipPosition(finger), axisOrigin, axisDirection);

            SetFingerImmediate(finger, restore);
            return distance;
        }

        /// <summary>Puts one finger at a closure now, with no easing. Used by the solver.</summary>
        public void SetFingerImmediate(int finger, float closure)
        {
            if (!_calibrated) { return; }

            finger = Mathf.Clamp(finger, 0, FingerCount - 1);
            _curl[finger] = Mathf.Clamp01(closure);
            Apply(finger);
        }

        /// <summary>Reaplica só um dedo. O solver mexe num de cada vez.</summary>
        private void Apply(int finger)
        {
            for (int i = 0; i < _joints.Count; i++)
            {
                Joint joint = _joints[i];
                if (joint.Bone == null || joint.Finger != finger) { continue; }
                joint.Bone.localRotation = joint.Rest * Quaternion.AngleAxis(joint.Degrees * _curl[finger], joint.Axis);
            }
        }

        private static float DistanceToAxis(Vector3 point, Vector3 origin, Vector3 direction)
        {
            Vector3 v = point - origin;
            return (v - Vector3.Project(v, direction.normalized)).magnitude;
        }

        /// <summary>
        /// Moves the fingers toward three explicit closures, 0 open to 1 closed.
        ///
        /// This is the path taken while an instrument is held. The closures then describe the
        /// handle rather than the button: a hand round a scalpel shank is not a closed fist, and
        /// squeezing harder cannot close it further, because the steel is in the way.
        /// </summary>
        public void TickTo(float deltaTime, float index, float grip, float thumb)
        {
            _targets[Thumb] = thumb;
            _targets[Index] = index;
            _targets[Middle] = grip;
            _targets[Ring] = grip;
            _targets[Little] = grip;
            TickFingers(deltaTime, _targets);
        }

        private void Apply()
        {
            for (int i = 0; i < _joints.Count; i++)
            {
                Joint joint = _joints[i];
                if (joint.Bone == null) { continue; }
                joint.Bone.localRotation = joint.Rest * Quaternion.AngleAxis(joint.Degrees * _curl[joint.Finger], joint.Axis);
            }
        }

        /// <summary>
        /// Puts every finger back where the model has it, for re-aligning or saving.
        ///
        /// The curl values go back to relaxed too. Leaving them where they were would mean the
        /// bones are at rest while the poser still believes the hand is closed, and the next tick
        /// would ease open from a clench that is not on screen.
        /// </summary>
        public void Restore()
        {
            for (int i = 0; i < _joints.Count; i++)
            {
                if (_joints[i].Bone != null) { _joints[i].Bone.localRotation = _joints[i].Rest; }
            }

            for (int i = 0; i < _curl.Length; i++) { _curl[i] = relaxed; }
        }

        /// <summary>A bone by its OpenXR joint name, whatever side prefix the model gives it.</summary>
        public static Transform FindBone(Transform root, string joint)
        {
            if (root == null) { return null; }

            // O sufixo sai do laço: montá-lo por osso alocava uma string a cada comparação.
            string suffix = "_" + joint;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == joint || n.EndsWith(suffix, System.StringComparison.Ordinal)) { return t; }
            }

            return null;
        }
    }
}
