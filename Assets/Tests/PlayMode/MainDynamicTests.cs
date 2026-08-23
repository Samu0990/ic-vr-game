using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Diagnostics;
using VRSurgery.Interaction;
using VRSurgery.Tissue;
using VRSurgery.Tools;
using VRSurgery.VR;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The first-version target: two cameras working, a scalpel that ends up in the hand, a
    /// patient on the table and a target organ present. These are deliberately coarse — they ask
    /// "does the loop run at all", not "is it good".
    /// </summary>
    public class MainDynamicTests
    {
        private const string SceneName = "SurgeryMVP";

        [UnitySetUp]
        public IEnumerator SetUp() => HeadlessScene.Load(SceneName);

        [UnityTest]
        public IEnumerator BothCameras_ExistOnSeparateDisplays()
        {
            Camera spectator = Find("SpectatorCamera");
            Camera projection = Find("ProjectionCamera");

            Assert.IsNotNull(spectator, "No SpectatorCamera — the headset view has nothing to mirror to.");
            Assert.IsNotNull(projection, "No ProjectionCamera — nothing to send to the projector.");
            Assert.AreNotEqual(spectator.targetDisplay, projection.targetDisplay,
                "Both cameras target the same display; one would hide the other.");

            Debug.Log($"[Dynamic] spectator -> Display {spectator.targetDisplay + 1} | " +
                      $"projection -> Display {projection.targetDisplay + 1}");
            yield break;
        }

        [UnityTest]
        public IEnumerator SpectatorCamera_TracksTheHeadset()
        {
            HeadsetFollowCamera follow = Object.FindFirstObjectByType<HeadsetFollowCamera>();
            Assert.IsNotNull(follow, "SpectatorCamera has no HeadsetFollowCamera.");
            Assert.IsTrue(follow.IsFollowing, "The spectator camera is not bound to a headset transform.");

            // Move the head and confirm the mirror lands on the same pose next frame.
            Transform head = follow.Headset;
            head.position += new Vector3(0.25f, 0.10f, -0.15f);
            head.rotation = Quaternion.Euler(12f, 40f, 0f);
            yield return null;
            yield return null;

            Assert.AreEqual(0f, Vector3.Distance(follow.transform.position, head.position), 0.001f,
                "Spectator camera did not follow the headset position.");
            Assert.AreEqual(0f, Quaternion.Angle(follow.transform.rotation, head.rotation), 0.5f,
                "Spectator camera did not follow the headset rotation.");
        }

        [UnityTest]
        public IEnumerator ProjectionCamera_FramesTheWholePatient()
        {
            Camera cam = Find("ProjectionCamera");
            Assert.IsNotNull(cam, "No ProjectionCamera.");

            GameObject patient = GameObject.Find("Patient");
            Assert.IsNotNull(patient, "No patient in the scene.");

            Bounds b = default;
            bool any = false;
            foreach (Renderer r in patient.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else { b.Encapsulate(r.bounds); }
            }

            // Every corner of the patient's bounds must fall inside the viewport.
            int outside = 0;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 vp = cam.WorldToViewportPoint(corner);
                if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) { outside++; }
            }

            Debug.Log($"[Dynamic] patient bounds size={b.size} | corners outside the projected view: {outside}/8");
            Assert.AreEqual(0, outside,
                $"{outside} of 8 patient bounds corners fall outside the projected view — the projector crops the patient.");
            yield break;
        }

        [UnityTest]
        public IEnumerator Scalpel_EndsUpInTheHand()
        {
            ScalpelTool scalpel = Object.FindFirstObjectByType<ScalpelTool>();
            Assert.IsNotNull(scalpel, "No scalpel in the scene.");

            SurgicalInteractable interactable = scalpel.GetComponent<SurgicalInteractable>();
            Assert.IsNotNull(interactable, "Scalpel has no SurgicalInteractable.");
            Assert.IsFalse(interactable.IsHeld, "Scalpel starts already held.");

            GameObject handObject = new GameObject("TestHand");
            handObject.transform.position = new Vector3(0.2f, 1.1f, -0.5f);
            ProbeHand hand = handObject.AddComponent<ProbeHand>();

            Assert.IsTrue(hand.TryGrab(interactable), "The hand could not grab the scalpel.");
            yield return null;

            Assert.IsTrue(interactable.IsHeld, "Scalpel does not report being held.");
            Assert.AreSame(interactable, hand.HeldObject, "The hand is not holding the scalpel.");

            Debug.Log($"[Dynamic] scalpel held; grip at {interactable.GripPoint.position}");
        }

        [UnityTest]
        public IEnumerator Patient_AndTarget_AreOnTheTable()
        {
            GameObject table = GameObject.Find("OperatingTable");
            Assert.IsNotNull(table, "No OperatingTable.");

            GameObject patient = GameObject.Find("Patient");
            Assert.IsNotNull(patient, "No patient.");
            Assert.AreSame(table.transform, patient.transform.parent,
                "The patient is not on the operating table.");

            // The organ the surgery targets.
            Transform vertebrae = null;
            foreach (Transform t in patient.GetComponentsInChildren<Transform>())
            {
                if (t.name.Contains("Vertebrae")) { vertebrae = t; break; }
            }

            Assert.IsNotNull(vertebrae, "No target organ (vertebrae) under the patient.");

            Renderer organ = vertebrae.GetComponentInChildren<Renderer>();
            Assert.IsNotNull(organ, "The target organ has no renderer.");
            Debug.Log($"[Dynamic] organ '{vertebrae.name}' bounds={organ.bounds.size} enabled={organ.enabled}");
            yield break;
        }

        [UnityTest]
        public IEnumerator CutCycle_MarksTheSkinAndWritesTheLog()
        {
            IncisableSkin skin = Object.FindFirstObjectByType<IncisableSkin>();
            Assert.IsNotNull(skin, "No incisable region in the scene.");
            Assert.IsFalse(skin.IsIncised, "The region starts already cut.");

            string logPath = skin.LogFilePath;
            if (File.Exists(logPath)) { File.Delete(logPath); }

            SurgeryProbe probe = Object.FindFirstObjectByType<SurgeryProbe>();
            Assert.IsNotNull(probe, "No SurgeryProbe to drive the cut without hardware.");

            yield return probe.RunIncision();

            Assert.IsTrue(skin.IsIncised, "The sweep did not register as a cut.");
            Assert.AreEqual(1, skin.IncisionCount, "The cut fired more than once.");
            Assert.IsTrue(File.Exists(logPath), "No log file was written at " + logPath);

            string contents = File.ReadAllText(logPath);
            Assert.IsNotEmpty(contents, "The log file is empty.");
            Debug.Log($"[Dynamic] cut logged at {logPath}: {contents.Trim().Split('\n')[0]}");
        }

        private static Camera Find(string cameraName)
        {
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (c.name == cameraName) { return c; }
            }

            return null;
        }
    }
}
