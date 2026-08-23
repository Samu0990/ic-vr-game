using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSurgery.Interaction;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Reproduces the reported symptom: the tool is grabbed but does not follow the hand.
    /// Driving a real XRGrabInteractable through the interaction manager is the only way to
    /// see this — checking the Inspector values only says how it is configured, not what it does.
    /// </summary>
    public class GrabMovementTests
    {
        private const string SceneName = "SurgeryMVP";

        private XRInteractionManager _manager;
        private XRDirectInteractor _interactor;
        private XRGrabInteractable _scalpel;

        /// <summary>Objects this fixture creates, so teardown can take them away again.</summary>
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return HeadlessScene.Load(SceneName);

            _scalpel = Object.FindFirstObjectByType<XRGrabInteractable>();
            Assert.IsNotNull(_scalpel, "No XRGrabInteractable in the scene.");

            _manager = Object.FindFirstObjectByType<XRInteractionManager>();
            if (_manager == null)
            {
                _manager = new GameObject("TestInteractionManager").AddComponent<XRInteractionManager>();
            }

            GameObject hand = new GameObject("TestInteractor");
            hand.transform.position = _scalpel.transform.position;

            SphereCollider reach = hand.AddComponent<SphereCollider>();
            reach.radius = 0.15f;
            reach.isTrigger = true;

            _interactor = hand.AddComponent<XRDirectInteractor>();
            _interactor.interactionManager = _manager;
            _spawned.Add(hand);
            yield return null;
        }

        /// <summary>
        /// Interactors left selecting at teardown make XRI cancel the selection while the scene
        /// is already being torn down, and its attach controller then touches a destroyed
        /// Transform. That exception surfaces in the NEXT test's SetUp, so one dirty fixture
        /// fails the whole run. Release first, then destroy.
        /// </summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_manager != null && _scalpel != null && _scalpel.isSelected)
            {
                List<IXRSelectInteractor> holders = new List<IXRSelectInteractor>(_scalpel.interactorsSelecting);
                foreach (IXRSelectInteractor holder in holders)
                {
                    _manager.SelectExit(holder, _scalpel);
                }
            }

            yield return null;

            foreach (GameObject spawned in _spawned)
            {
                if (spawned != null) { Object.Destroy(spawned); }
            }

            _spawned.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator GrabbedTool_FollowsTheHand()
        {
            Rigidbody body = _scalpel.GetComponent<Rigidbody>();
            Assert.IsNotNull(body, "Scalpel has no Rigidbody.");

            Debug.Log($"[Grab] before: movementType={_scalpel.movementType} " +
                      $"isKinematic={body.isKinematic} useGravity={body.useGravity} " +
                      $"parent={(_scalpel.transform.parent == null ? "none" : _scalpel.transform.parent.name)}");

            _manager.SelectEnter((IXRSelectInteractor)_interactor, (IXRSelectInteractable)_scalpel);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsTrue(_scalpel.isSelected, "The scalpel did not register as selected.");
            Debug.Log($"[Grab] held: isKinematic={body.isKinematic} " +
                      $"constraints={body.constraints} joints={_scalpel.GetComponents<Joint>().Length}");

            Vector3 start = _scalpel.transform.position;

            // Move the hand a clear, unambiguous distance and let physics run.
            Vector3 target = start + new Vector3(0.30f, 0.20f, 0f);
            _interactor.transform.position = target;

            for (int i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Vector3 end = _scalpel.transform.position;
            float travelled = Vector3.Distance(start, end);
            float handTravel = Vector3.Distance(start, target);

            Debug.Log($"[Grab] hand moved {handTravel:F3} m | tool moved {travelled:F3} m | " +
                      $"start={start} end={end} velocity={body.linearVelocity}");

            Assert.Greater(travelled, handTravel * 0.5f,
                $"The tool moved only {travelled * 100f:F1} cm while the hand moved {handTravel * 100f:F1} cm — " +
                "it is grabbed but not following.");
        }

        /// <summary>
        /// The template rig grabs with a NearFarInteractor, not a direct one, and the scalpel
        /// currently sits 1.19 m away — out of direct reach. So the ray is the path the player
        /// actually uses, and it is the one worth proving.
        /// </summary>
        [UnityTest]
        public IEnumerator GrabbedTool_FollowsTheHand_ViaRayInteractor()
        {
            GameObject rig = new GameObject("TestNearFar");
            rig.transform.position = _scalpel.transform.position + new Vector3(0f, 0.5f, -0.6f);

            NearFarInteractor far = rig.AddComponent<NearFarInteractor>();
            far.interactionManager = _manager;
            _spawned.Add(rig);
            yield return null;

            Rigidbody body = _scalpel.GetComponent<Rigidbody>();
            _manager.SelectEnter((IXRSelectInteractor)far, (IXRSelectInteractable)_scalpel);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsTrue(_scalpel.isSelected, "The scalpel did not register as selected by the ray.");
            Debug.Log($"[Ray] held: isKinematic={body.isKinematic} movementType={_scalpel.movementType}");

            Vector3 start = _scalpel.transform.position;
            Vector3 target = rig.transform.position + new Vector3(0.30f, 0.20f, 0f);
            rig.transform.position = target;

            for (int i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            float travelled = Vector3.Distance(start, _scalpel.transform.position);
            Debug.Log($"[Ray] hand moved 0,361 m | tool moved {travelled:F3} m | " +
                      $"start={start} end={_scalpel.transform.position}");

            Assert.Greater(travelled, 0.10f,
                $"Grabbed by the ray, the tool moved only {travelled * 100f:F1} cm — it is held but not following.");
        }

        [UnityTest]
        public IEnumerator ReleasedTool_FallsInsteadOfFloating()
        {
            Rigidbody body = _scalpel.GetComponent<Rigidbody>();
            ToolReleasePhysics drop = _scalpel.GetComponent<ToolReleasePhysics>();
            Assert.IsNotNull(drop, "Scalpel has no ToolReleasePhysics.");

            _manager.SelectEnter((IXRSelectInteractor)_interactor, (IXRSelectInteractable)_scalpel);
            yield return new WaitForFixedUpdate();

            // Lift it well clear of the table so the fall is unambiguous.
            _interactor.transform.position = _scalpel.transform.position + new Vector3(0f, 0.45f, 0f);
            for (int i = 0; i < 40; i++) { yield return new WaitForFixedUpdate(); }

            float heldY = _scalpel.transform.position.y;
            Assert.IsFalse(body.useGravity, "Gravity is on while the tool is held; it would fight the grab.");

            _manager.SelectExit((IXRSelectInteractor)_interactor, (IXRSelectInteractable)_scalpel);
            yield return null;
            yield return null;

            Assert.IsTrue(body.useGravity, "Gravity was not enabled on release.");
            Assert.IsFalse(body.isKinematic, "The Rigidbody is still kinematic; it cannot fall.");

            for (int i = 0; i < 90; i++) { yield return new WaitForFixedUpdate(); }

            float restY = _scalpel.transform.position.y;
            Debug.Log($"[Drop] held at Y {heldY:F3} | after release Y {restY:F3} | fell {(heldY - restY) * 100f:F1} cm");

            Assert.Less(restY, heldY - 0.05f,
                $"The tool only fell {(heldY - restY) * 100f:F1} cm — it is still floating.");
        }

        [UnityTest]
        public IEnumerator HoveredTool_ShowsTheHighlight()
        {
            ToolHoverHighlight highlight = _scalpel.GetComponent<ToolHoverHighlight>();
            Assert.IsNotNull(highlight, "Scalpel has no ToolHoverHighlight.");

            // Driven through the interactor's real proximity detection rather than by calling the
            // hover events directly, so this exercises what actually happens in play. Moving a
            // transform does not refresh trigger overlaps until physics steps, hence the waits.
            yield return MoveInteractorTo(_scalpel.transform.position + new Vector3(0f, 3f, 0f));
            Assert.IsFalse(highlight.IsHighlighted, "The tool is highlighted with no hand near it.");

            yield return MoveInteractorTo(_scalpel.transform.position);
            Assert.IsTrue(highlight.IsHighlighted, "Approaching the tool did not light the outline.");

            yield return MoveInteractorTo(_scalpel.transform.position + new Vector3(0f, 3f, 0f));
            Assert.IsFalse(highlight.IsHighlighted, "The outline stayed lit after the hand left.");

            Debug.Log("[Hover] highlight follows real proximity: off -> on -> off");
        }

        private IEnumerator MoveInteractorTo(Vector3 position)
        {
            _interactor.transform.position = position;
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            yield return null;
        }
    }
}
