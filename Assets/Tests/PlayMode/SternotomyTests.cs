using NUnit.Framework;
using UnityEngine;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Opening the chest. The pose it returns to is whatever the scene was dressed with, so what
    /// these check is that the motion is reversible and lands exactly where it started — the
    /// failure that matters is a sternum that drifts a little further open every visitor.
    /// </summary>
    public class SternotomyTests
    {
        private GameObject _host;
        private SternotomyController _sternotomy;
        private Vector3 _closedPosition;
        private Quaternion _closedRotation;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("Sternum");
            _host.SetActive(false);

            // An arbitrary authored pose, the way a hand-placed model would sit.
            _host.transform.localPosition = new Vector3(0.013f, 1.31f, 0.24f);
            _host.transform.localRotation = Quaternion.Euler(7f, 191f, 3f);
            _closedPosition = _host.transform.localPosition;
            _closedRotation = _host.transform.localRotation;

            _sternotomy = _host.AddComponent<SternotomyController>();
            _host.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) { Object.DestroyImmediate(_host); }
        }

        private void Step(float seconds, float step = 1f / 60f)
        {
            for (float t = 0f; t < seconds; t += step) { _sternotomy.Tick(step); }
        }

        [Test]
        public void TheChestStartsClosedAndStaysPutUntilAsked()
        {
            Assert.AreEqual(0f, _sternotomy.Openness01);
            Step(1f);
            Assert.AreEqual(0f, _sternotomy.Openness01, "Nothing moves on its own.");
            Assert.AreEqual(_closedPosition, _host.transform.localPosition);
        }

        [Test]
        public void OpeningMovesTheSternumAndReportsWhenItIsDone()
        {
            int opened = 0;
            _sternotomy.Opened += () => opened++;

            _sternotomy.Open();
            Assert.IsTrue(_sternotomy.IsMoving);

            Step(3f);

            Assert.IsTrue(_sternotomy.IsOpen);
            Assert.IsFalse(_sternotomy.IsMoving);
            Assert.AreEqual(1, opened, "Opened fires once, not once per frame at the end.");
            Assert.AreNotEqual(_closedPosition, _host.transform.localPosition,
                "An open chest is not in the same place as a closed one.");
        }

        [Test]
        public void ClosingReturnsExactlyToTheAuthoredPose()
        {
            _sternotomy.Open();
            Step(3f);
            _sternotomy.Close();
            Step(3f);

            Assert.AreEqual(0f, _sternotomy.Openness01);

            // The regression this guards: a sternum that creeps a little further open every
            // visitor because closing returns to a remembered pose rather than the authored one.
            Assert.That(Vector3.Distance(_host.transform.localPosition, _closedPosition),
                Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(_host.transform.localRotation, _closedRotation),
                Is.LessThan(0.01f));
        }

        [Test]
        public void OpeningTwiceDoesNotOpenTwiceAsFar()
        {
            _sternotomy.Open();
            Step(3f);
            Vector3 openPosition = _host.transform.localPosition;

            _sternotomy.Open();
            Step(3f);

            Assert.AreEqual(openPosition, _host.transform.localPosition,
                "A second call on an already-open chest must be a no-op, not a second push.");
        }

        [Test]
        public void ResetSnapsShutWithoutWaitingForTheAnimation()
        {
            _sternotomy.Open();
            Step(0.5f);
            Assert.Greater(_sternotomy.Openness01, 0f);

            _sternotomy.ResetClosed();

            Assert.AreEqual(0f, _sternotomy.Openness01);
            Assert.IsFalse(_sternotomy.IsMoving);
            Assert.That(Vector3.Distance(_host.transform.localPosition, _closedPosition),
                Is.LessThan(0.0001f),
                "A round reset mid-operation has to leave the next visitor a closed chest.");
        }

        [Test]
        public void TheHeartIsRevealedPartwayThroughRatherThanAtTheStart()
        {
            GameObject heart = new GameObject("Heart");
            _sternotomy.Bind(_host.transform, new[] { heart });
            heart.SetActive(false);

            _sternotomy.Open();
            _sternotomy.Tick(0.05f);
            Assert.IsFalse(heart.activeSelf,
                "The heart must not appear while the chest is still effectively shut.");

            Step(3f);
            Assert.IsTrue(heart.activeSelf);

            Object.DestroyImmediate(heart);
        }
    }
}
