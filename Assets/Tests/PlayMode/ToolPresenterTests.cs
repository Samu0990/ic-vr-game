using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The paddles handed over when they are needed: off the tray until the new heart
    /// fibrillates, on it then, and off again for the next visitor.
    /// </summary>
    public class ToolPresenterTests
    {
        private GameObject _host;
        private GameObject _tool;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) { Object.DestroyImmediate(_host); }
            if (_tool != null) { Object.DestroyImmediate(_tool); }
            SurgeryEvents.ResetAll();
        }

        [UnityTest]
        public IEnumerator TheInstrumentIsOffTheTrayUntilPresented_ThenStaysUntilTheNextVisitor()
        {
            _tool = new GameObject("Pas");
            _host = new GameObject("Sistemas");
            TransplantProcedure procedure = _host.AddComponent<TransplantProcedure>();
            ToolPresenter presenter = _host.AddComponent<ToolPresenter>();
            presenter.Bind(_tool, null, procedure);
            int presented = 0;
            presenter.Presented += () => presented++;

            yield return null; // Start

            Assert.IsFalse(_tool.activeSelf, "Nothing fibrillating yet: the paddles are not on the tray.");
            Assert.IsFalse(presenter.IsPresented);

            presenter.Present();
            Assert.IsTrue(_tool.activeSelf, "Handed over when the heart fibrillates.");
            Assert.AreEqual(1, presented);

            presenter.Present();
            Assert.AreEqual(1, presented, "Handing over twice is still once.");

            procedure.Begin();
            Assert.IsTrue(_tool.activeSelf, "The operation going on does not take them away.");

            procedure.ResetProcedure();
            Assert.IsFalse(_tool.activeSelf, "A new visitor starts without them.");
        }
    }
}
