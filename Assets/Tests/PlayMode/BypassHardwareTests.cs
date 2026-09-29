using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The cannulas, the cardioplegia line and the cross-clamp appear with the pump step that puts
    /// them in the chest and go with the step that takes them out.
    /// </summary>
    public class BypassHardwareTests
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

        private Renderer Part(string name)
        {
            GameObject go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            _spawned.Add(go);
            return go.GetComponent<MeshRenderer>();
        }

        [Test]
        public void EachPieceShowsExactlyWhileThePumpHasItInTheChest()
        {
            GameObject host = new GameObject("Procedure");
            _spawned.Add(host);
            TransplantProcedure procedure = host.AddComponent<TransplantProcedure>();
            procedure.Begin();

            Renderer cannula = Part("Cannula"), rootLine = Part("RootLine"), clamp = Part("Clamp");
            BypassHardware hardware = host.AddComponent<BypassHardware>();
            hardware.Bind(procedure, new[] { cannula }, new[] { rootLine }, new[] { clamp });

            Assert.IsFalse(cannula.enabled || rootLine.enabled || clamp.enabled, "Nothing in the chest before the pump.");

            procedure.Bypass.Attempt(BypassStep.Cannulate);
            hardware.Tick();
            Assert.IsTrue(cannula.enabled);
            Assert.IsFalse(clamp.enabled);

            procedure.Bypass.Attempt(BypassStep.ClampAorta);
            hardware.Tick();
            Assert.IsTrue(clamp.enabled, "Clamping the aorta leaves a clamp on the aorta.");

            procedure.Bypass.Attempt(BypassStep.Cardioplegia);
            hardware.Tick();
            Assert.IsTrue(rootLine.enabled);

            procedure.Bypass.Attempt(BypassStep.Unclamp, true);
            hardware.Tick();
            Assert.IsFalse(clamp.enabled, "and taking it off takes it away.");
            Assert.IsTrue(cannula.enabled, "The cannulas stay until the chest is closed.");

            procedure.ResetProcedure();
            hardware.Tick();
            Assert.IsFalse(cannula.enabled || rootLine.enabled || clamp.enabled, "The next visitor gets an empty chest.");
        }
    }
}
