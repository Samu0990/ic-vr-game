using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Surgery;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The gloved hands and the light-blue grab outline.
    ///
    /// The hands are driven on a small skeleton built here with the OpenXR joint names the real
    /// model uses, laid out like a hand resting palm down with the fingers along +Z: enough to
    /// check that a squeeze closes the right fingers toward the palm, that the model is turned
    /// to hold the controller's handle, and that nothing depends on how the model was authored.
    /// </summary>
    public class HandAndOutlineTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null) { Object.DestroyImmediate(go); }
            }

            _spawned.Clear();
            SurgeryEvents.ResetAll();
        }

        private GameObject Spawn(string name)
        {
            GameObject go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        // ------------------------------------------------------------------ a hand to test on

        /// <summary>Palm down (facing -Y), fingers along +Z, thumb on the inside.</summary>
        private GameObject Skeleton(bool left, Transform parent = null)
        {
            string p = left ? "L_" : "R_";
            float side = left ? -1f : 1f; // a right hand's thumb and index are on -X
            GameObject hand = Spawn(left ? "LeftHand" : "RightHand");
            if (parent != null) { hand.transform.SetParent(parent, false); }

            Transform wrist = Joint(hand.transform, p + "Wrist", Vector3.zero);
            Joint(wrist, p + "Palm", new Vector3(0f, 0f, 0.05f));

            (string finger, float x)[] fingers =
            {
                ("Index", -0.025f), ("Middle", -0.008f), ("Ring", 0.01f), ("Little", 0.027f),
            };
            foreach ((string finger, float x) in fingers)
            {
                float fx = x * side;
                Transform meta = Joint(wrist, p + finger + "Metacarpal", new Vector3(fx * 0.4f, 0f, 0.02f));
                Transform proximal = Joint(meta, p + finger + "Proximal", new Vector3(fx, 0f, 0.09f));
                Transform middle = Joint(proximal, p + finger + "Intermediate", new Vector3(fx, 0f, 0.135f));
                Transform distal = Joint(middle, p + finger + "Distal", new Vector3(fx, 0f, 0.165f));
                Joint(distal, p + finger + "Tip", new Vector3(fx, 0f, 0.19f));
            }

            Transform tm = Joint(wrist, p + "ThumbMetacarpal", new Vector3(-0.02f * side, -0.01f, 0.03f));
            Transform tp = Joint(tm, p + "ThumbProximal", new Vector3(-0.045f * side, -0.01f, 0.06f));
            Transform td = Joint(tp, p + "ThumbDistal", new Vector3(-0.06f * side, -0.01f, 0.09f));
            Joint(td, p + "ThumbTip", new Vector3(-0.07f * side, -0.01f, 0.115f));
            return hand;
        }

        private static Transform Joint(Transform parent, string name, Vector3 worldPosition)
        {
            Transform joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.position = parent.root.TransformPoint(worldPosition);
            return joint;
        }

        private static Vector3 Local(Transform hand, string joint) =>
            hand.InverseTransformPoint(HandPoser.FindBone(hand, joint).position);

        private HandPoser Poser(bool left, out GameObject hand)
        {
            hand = Skeleton(left);
            HandPoser poser = hand.AddComponent<HandPoser>();
            poser.Bind(left);
            poser.Calibrate();
            return poser;
        }

        // ------------------------------------------------------------------ fingers

        [Test]
        public void ThePalmFacesDownOnAHandLyingPalmDown_LeftOrRight()
        {
            foreach (bool left in new[] { false, true })
            {
                GameObject hand = Skeleton(left);
                Vector3 normal = HandPoser.PalmNormal(
                    HandPoser.FindBone(hand.transform, "Wrist").position,
                    HandPoser.FindBone(hand.transform, "IndexProximal").position,
                    HandPoser.FindBone(hand.transform, "LittleProximal").position, left);

                Assert.Greater(Vector3.Dot(normal, Vector3.down), 0.99f, left ? "mão esquerda" : "mão direita");
            }
        }

        [Test]
        public void SqueezingTheGripClosesTheLastThreeFingersIntoThePalm_NotTheIndex()
        {
            HandPoser poser = Poser(false, out GameObject hand);
            Assert.IsTrue(poser.IsCalibrated);
            Assert.AreEqual(15, poser.JointCount, "Three joints on each of five digits.");

            float middleOpen = ToPalm(hand, "MiddleTip");
            Vector3 indexOpen = Local(hand.transform, "IndexTip");

            poser.Tick(0f, 1f, 0f);

            Assert.Less(ToPalm(hand, "MiddleTip"), middleOpen - 0.07f, "The middle fingertip closes into the palm,");
            Assert.Less(Local(hand.transform, "MiddleTip").y, -0.02f, "on the palm side, not the back of the hand.");
            Assert.Less(Vector3.Distance(Local(hand.transform, "IndexTip"), indexOpen), 0.001f, "The index waits for the trigger.");
        }

        /// <summary>From a fingertip to the middle of the palm.</summary>
        private static float ToPalm(GameObject hand, string tip) =>
            Vector3.Distance(Local(hand.transform, tip), Local(hand.transform, "Palm"));

        [Test]
        public void PullingTheTriggerBendsOnlyTheIndex()
        {
            HandPoser poser = Poser(false, out GameObject hand);
            Vector3 ringOpen = Local(hand.transform, "RingTip");
            float indexOpen = ToPalm(hand, "IndexTip");

            poser.Tick(0f, 0f, 1f);

            Assert.Less(ToPalm(hand, "IndexTip"), indexOpen - 0.05f);
            Assert.Less(Local(hand.transform, "IndexTip").y, -0.02f);
            Assert.Less(Vector3.Distance(Local(hand.transform, "RingTip"), ringOpen), 0.001f);
        }

        [Test]
        public void TheLeftHandCurlsTheSameWayMirrored()
        {
            HandPoser poser = Poser(true, out GameObject hand);
            Vector3 open = Local(hand.transform, "LittleTip");
            float openToPalm = ToPalm(hand, "LittleTip");

            poser.Tick(0f, 1f, 0f);

            Vector3 closed = Local(hand.transform, "LittleTip");
            Assert.Less(ToPalm(hand, "LittleTip"), openToPalm - 0.07f);
            Assert.Less(closed.y, -0.02f, "Toward the palm, not the back of the hand.");
            Assert.AreEqual(open.x, closed.x, 0.01f, "Bending, not swinging sideways.");
        }

        [Test]
        public void LettingGoTheHandRelaxesOverAFewFrames_NotInstantly()
        {
            HandPoser poser = Poser(false, out _);
            poser.Tick(0f, 1f, 1f);
            Assert.AreEqual(1f, poser.Curl(HandPoser.GripGroup), 1e-4f);

            poser.Tick(1f / 72f, 0f, 0f);
            float oneFrame = poser.Curl(HandPoser.GripGroup);
            Assert.Less(oneFrame, 1f);
            Assert.Greater(oneFrame, 0.5f, "Fingers follow the button, they do not snap.");

            for (int i = 0; i < 72; i++) { poser.Tick(1f / 72f, 0f, 0f); }
            Assert.Less(poser.Curl(HandPoser.GripGroup), 0.2f, "A second later the hand is relaxed again.");
            Assert.Greater(poser.Curl(HandPoser.GripGroup), 0.05f, "Relaxed, never flat.");
        }

        // ------------------------------------------------------------------ hand on the controller

        [Test]
        public void TheModelIsTurnedToWrapTheHandle_PalmInFingersDownPalmOffTheAxis()
        {
            foreach (bool left in new[] { false, true })
            {
                GameObject root = Spawn("Mao");
                root.transform.SetPositionAndRotation(new Vector3(0.3f, 1.1f, 0.2f), Quaternion.Euler(20f, 70f, -10f));
                GameObject model = Skeleton(left, root.transform);
                // Authored any old way: the alignment must not care.
                model.transform.localRotation = Quaternion.Euler(-90f, 35f, 12f);
                model.transform.localPosition = new Vector3(0.1f, -0.2f, 0.05f);

                ControllerHand hand = root.AddComponent<ControllerHand>();
                hand.enabled = false;
                hand.Bind(left, null, model.transform, null, null, null, null);
                hand.AlignModel();

                Vector3 w = Local(root.transform, "Wrist");
                Vector3 m = Local(root.transform, "MiddleProximal");
                Vector3 palm = HandPoser.PalmNormal(w, Local(root.transform, "IndexProximal"), Local(root.transform, "LittleProximal"), left);
                Vector3 fingers = (m - w).normalized;
                Vector3 wantPalm = left ? Vector3.right : Vector3.left;

                string which = left ? "esquerda" : "direita";
                Assert.Greater(Vector3.Dot(palm, wantPalm), 0.98f, $"Palma voltada para o cabo ({which}).");
                Assert.Greater(Vector3.Dot(fingers, Vector3.down), 0.98f, $"Dedos descendo para envolver o cabo ({which}).");

                Vector3 palmCentre = Local(root.transform, "Palm");
                Assert.Less(Vector3.Distance(palmCentre, -wantPalm * 0.025f), 0.001f, $"Palma a 2,5 cm do eixo do cabo ({which}).");

                // Doing it again changes nothing.
                hand.AlignModel();
                Assert.Less(Vector3.Distance(Local(root.transform, "Palm"), palmCentre), 1e-4f);
            }
        }

        // ------------------------------------------------------------------ outline

        private static Material OutlineMaterial()
        {
            Shader shader = Shader.Find("VRSurgery/GrabOutline");
            return new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
        }

        [Test]
        public void TheOutlineTracesTheSolidPartsOnly_AndCostsNothingUntilShown()
        {
            GameObject tool = Spawn("Instrumento");
            GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.transform.SetParent(tool.transform, false);
            Object.DestroyImmediate(blade.GetComponent<Collider>());

            GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.transform.SetParent(tool.transform, false);
            Material glass = new Material(glow.GetComponent<Renderer>().sharedMaterial) { renderQueue = 3000 };
            glow.GetComponent<Renderer>().sharedMaterial = glass;

            GameObject label = new GameObject("Rotulo");
            label.transform.SetParent(tool.transform, false);
            label.AddComponent<TextMesh>().text = "BISTURI";

            InteractableOutline outline = tool.AddComponent<InteractableOutline>();
            Assert.AreEqual(0, outline.ShellCount, "Nothing is built before anyone reaches for it.");

            Material material = OutlineMaterial();
            outline.Show(true, material);

            Assert.AreEqual(1, outline.ShellCount, "The blade, not the glow nor the label.");
            Transform shell = blade.transform.Find(InteractableOutline.ShellName);
            Assert.IsNotNull(shell);
            Renderer rim = shell.GetComponent<Renderer>();
            Assert.IsTrue(rim.enabled);
            Assert.AreSame(material, rim.sharedMaterial);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, rim.shadowCastingMode);
            Assert.AreSame(blade.GetComponent<MeshFilter>().sharedMesh, shell.GetComponent<MeshFilter>().sharedMesh);

            outline.Show(false, material);
            Assert.IsFalse(rim.enabled);

            outline.Show(true, material);
            Assert.AreEqual(1, outline.ShellCount, "Built once, then only switched.");
        }

        [Test]
        public void OnlyWhatTheHandsWouldTakeIsOutlined_AndTheRestGoesDark()
        {
            GrabOutlines outlines = Spawn("Contornos").AddComponent<GrabOutlines>();
            outlines.Bind(OutlineMaterial(), null);

            InteractableOutline Make(string name)
            {
                GameObject tool = Spawn(name);
                GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.transform.SetParent(tool.transform, false);
                return GrabOutlines.OutlineOf(tool);
            }

            InteractableOutline scalpel = Make("Bisturi");
            InteractableOutline forceps = Make("Pinca");
            InteractableOutline holder = Make("PortaAgulha");

            outlines.Apply(new[] { scalpel, forceps });
            Assert.IsTrue(scalpel.IsShown);
            Assert.IsTrue(forceps.IsShown);
            Assert.IsFalse(holder.IsShown);

            // The left hand took the forceps; the right moved on to the needle holder.
            outlines.Apply(new[] { holder });
            Assert.IsFalse(scalpel.IsShown);
            Assert.IsFalse(forceps.IsShown);
            Assert.IsTrue(holder.IsShown);
            Assert.AreEqual(1, outlines.Shown.Count);

            outlines.Apply(new InteractableOutline[0]);
            Assert.IsFalse(holder.IsShown);
            Assert.AreSame(GrabOutlines.OutlineOf(holder.gameObject), holder, "One outline per object, reused.");
        }
    }
}
