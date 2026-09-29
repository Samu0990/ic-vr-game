using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.VR;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The stand-in-place rules borrowed from Job Simulator: an instrument let go falls and stays
    /// where it can be picked up again, pops back only when it lands out of reach, and the rig
    /// fits itself to each visitor instead of the visitor walking to the table.
    /// </summary>
    public class JobSimulatorMechanicsTests
    {
        private const float Step = 1f / 60f;
        private static readonly Vector3 Home = new Vector3(0.3f, 1.2f, 0.2f);

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

        private ReturnHomeOnRelease Tool(out SurgicalInteractable grab, out Rigidbody body)
        {
            GameObject tool = Spawn("Tool");
            tool.transform.position = Home;
            body = tool.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            grab = tool.AddComponent<SurgicalInteractable>();
            ReturnHomeOnRelease release = tool.AddComponent<ReturnHomeOnRelease>();
            release.Bind(grab);
            release.UseDropAndRespawn(new Vector3(0.45f, 1.3f, 0f), 0.85f, 0.65f);
            return release;
        }

        private static void Run(ReturnHomeOnRelease release, float seconds)
        {
            for (float t = 0f; t < seconds; t += Step) { release.Tick(Step); }
        }

        [Test]
        public void LetGo_ItFallsAndStaysWhereItCanBeReached()
        {
            ReturnHomeOnRelease release = Tool(out SurgicalInteractable grab, out Rigidbody body);

            grab.OnGrabbed(null);
            grab.transform.position = new Vector3(0.1f, 1.25f, 0.1f);
            grab.OnReleased();
            Run(release, 0.1f);

            Assert.IsFalse(body.isKinematic, "Released, it is a physical object again,");
            Assert.IsTrue(body.useGravity, "and it falls.");
            Assert.IsTrue(release.IsLoose);

            // Landed on the drape, within reach: it stays there to be picked up again.
            grab.transform.position = new Vector3(0.1f, 1.15f, 0.15f);
            Run(release, 3f);
            Assert.AreEqual(new Vector3(0.1f, 1.15f, 0.15f), grab.transform.position);
        }

        [Test]
        public void OnTheFloor_ItPopsBackOntoTheTray()
        {
            ReturnHomeOnRelease release = Tool(out SurgicalInteractable grab, out Rigidbody body);
            int respawns = 0;
            release.Respawned += () => respawns++;

            grab.OnGrabbed(null);
            grab.OnReleased();
            Run(release, 0.1f);

            grab.transform.position = new Vector3(0.6f, 0.02f, 0.4f);
            Run(release, 1.2f);

            Assert.AreEqual(1, respawns, "Nothing is ever lost at a stand where nobody can walk.");
            Assert.AreEqual(Home, grab.transform.position);
            Assert.IsTrue(body.isKinematic, "Back in its place it rests, not falls.");
            Assert.IsFalse(release.IsLoose);
            Assert.AreEqual(Vector3.one, grab.transform.localScale, "The pop ends at full size.");
        }

        [Test]
        public void ThrownAcrossTheRoom_ItComesBackToo()
        {
            ReturnHomeOnRelease release = Tool(out SurgicalInteractable grab, out _);

            grab.OnGrabbed(null);
            grab.OnReleased();
            Run(release, 0.1f);
            grab.transform.position = new Vector3(-2f, 1.4f, 1.5f);
            Run(release, 1.2f);

            Assert.AreEqual(Home, grab.transform.position);
        }

        [Test]
        public void ABounceOffTheEdgeThatComesBackIsNotLost()
        {
            ReturnHomeOnRelease release = Tool(out SurgicalInteractable grab, out _);
            int respawns = 0;
            release.Respawned += () => respawns++;

            grab.OnGrabbed(null);
            grab.OnReleased();
            grab.transform.position = new Vector3(0.6f, 0.5f, 0.4f);
            Run(release, 0.3f);
            grab.transform.position = new Vector3(0.1f, 1.15f, 0.15f);
            Run(release, 2f);

            Assert.AreEqual(0, respawns, "Half a second out of reach and back is a bounce, not a loss.");
        }

        [Test]
        public void DroppedThroughTheSkinIntoThePatient_ItComesBack()
        {
            ReturnHomeOnRelease release = Tool(out SurgicalInteractable grab, out _);
            Bounds insidePatient = new Bounds(new Vector3(0f, 1.05f, 0f), new Vector3(0.2f, 0.2f, 0.3f));
            release.UseDropAndRespawn(new Vector3(0.45f, 1.3f, 0f), 0.85f, 0.65f, insidePatient);

            grab.OnGrabbed(null);
            grab.OnReleased();
            Run(release, 0.1f);

            // Within arm's reach, but inside the chest where nobody can see it.
            grab.transform.position = new Vector3(0.02f, 1.02f, 0.05f);
            Run(release, 1.2f);

            Assert.AreEqual(Home, grab.transform.position, "Unseen counts as lost.");
        }

        [Test]
        public void ANewVisitorGetsTheInstrumentBackOnTheTray()
        {
            ReturnHomeOnRelease release = Tool(out SurgicalInteractable grab, out Rigidbody body);

            grab.OnGrabbed(null);
            grab.OnReleased();
            Run(release, 0.1f);
            grab.transform.position = new Vector3(0.1f, 1.15f, 0.15f);

            release.SendHomeNow();

            Assert.AreEqual(Home, grab.transform.position);
            Assert.IsTrue(body.isKinematic);
        }

        // ------------------------------------------------------------------ grab glow

        [Test]
        public void AnInstrumentGlowsWhileAHandIsNearAndKeepsItsOwnMaterial()
        {
            GameObject tool = Spawn("Tool");
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.transform.SetParent(tool.transform, false);
            Renderer renderer = part.GetComponent<Renderer>();
            Material own = renderer.sharedMaterial;

            GrabGlow glow = tool.AddComponent<GrabGlow>();

            glow.SetGlow(true);
            Assert.IsTrue(glow.IsGlowing);
            Assert.IsTrue(renderer.HasPropertyBlock(), "Tinted through a property block,");
            Assert.AreSame(own, renderer.sharedMaterial, "not by swapping the material away.");

            glow.SetGlow(false);
            Assert.IsFalse(renderer.HasPropertyBlock(), "Gone without a trace once the hand leaves.");
        }

        // ------------------------------------------------------------------ operator

        [Test]
        public void TheOperatorsHoldRecentresAtTwoSecondsAndEndsTheTurnAtFour()
        {
            VRSurgery.Session.OperatorControls controls = Spawn("Operator").AddComponent<VRSurgery.Session.OperatorControls>();
            int recentred = 0, ended = 0;
            controls.Recentred += () => recentred++;
            controls.TurnEnded += () => ended++;

            for (float t = 0f; t < 1.5f; t += Step) { controls.Tick(Step, true); }
            controls.Tick(Step, false);
            Assert.AreEqual(0, recentred + ended, "A visitor's short press does nothing.");

            for (float t = 0f; t < 2.1f; t += Step) { controls.Tick(Step, true); }
            Assert.AreEqual(1, recentred);
            Assert.AreEqual(0, ended);

            for (float t = 0f; t < 2.1f; t += Step) { controls.Tick(Step, true); }
            Assert.AreEqual(1, recentred, "Once per hold,");
            Assert.AreEqual(1, ended, "and past four seconds the turn ends.");
        }

        // ------------------------------------------------------------------ visitor fit

        private VisitorFit Rig(float eyeHeight, out Transform rig, out Transform head, out Transform stance)
        {
            rig = Spawn("XR Origin").transform;
            head = new GameObject("Camera").transform;
            head.SetParent(rig, false);
            head.localPosition = new Vector3(0.5f, eyeHeight, -0.3f);
            head.localRotation = Quaternion.Euler(0f, 90f, 0f);

            stance = Spawn("Stance").transform;
            stance.SetPositionAndRotation(new Vector3(2f, 0f, 1f), Quaternion.LookRotation(Vector3.left));

            VisitorFit fit = rig.gameObject.AddComponent<VisitorFit>();
            fit.Bind(rig, head, stance, null);
            return fit;
        }

        [Test]
        public void TheRigBringsTheVisitorsHeadOverTheStanceFacingThePatient()
        {
            VisitorFit fit = Rig(1.7f, out _, out Transform head, out _);

            fit.Fit();

            Assert.AreEqual(2f, head.position.x, 1e-3f);
            Assert.AreEqual(1f, head.position.z, 1e-3f);
            Assert.AreEqual(1.7f, head.position.y, 1e-3f, "A tall adult keeps their own height.");
            Assert.Less(Vector3.Angle(head.forward, Vector3.left), 0.5f, "and looks at the patient.");
        }

        [Test]
        public void ShortMode_AChildIsLiftedToTheTable()
        {
            VisitorFit fit = Rig(1.2f, out _, out Transform head, out _);

            fit.Fit();

            Assert.AreEqual(0.4f, fit.Lift, 1e-3f, "Lifted as far as allowed,");
            Assert.AreEqual(1.6f, head.position.y, 1e-3f, "so their eyes are where an adult's would be.");

            fit.Fit();
            Assert.AreEqual(1.6f, head.position.y, 1e-3f, "Fitting twice does not lift twice.");
        }

        [Test]
        public void ShortModeLiftsOnlyWhoNeedsIt()
        {
            VisitorFit fit = Rig(1.7f, out _, out _, out _);

            Assert.AreEqual(0f, fit.LiftFor(1.55f));
            Assert.AreEqual(0.2f, fit.LiftFor(1.4f), 1e-4f);
            Assert.AreEqual(0.4f, fit.LiftFor(1.0f), 1e-4f, "never more than 40 cm");
        }
    }
}
