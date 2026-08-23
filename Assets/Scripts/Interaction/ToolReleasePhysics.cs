using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// Lets a dropped instrument fall instead of hanging in the air.
    ///
    /// Held behaviour is deliberately untouched: the grab interactable owns the tool while it is
    /// selected. This only takes over once the tool is let go.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class ToolReleasePhysics : MonoBehaviour
    {
        [SerializeField] private Rigidbody body;
        [SerializeField] private XRGrabInteractable grab;

        /// <summary>True once the tool has been released at least once and is falling freely.</summary>
        public bool IsFalling { get; private set; }

        private bool _dropPending;

        private void Awake()
        {
            if (body == null) { body = GetComponent<Rigidbody>(); }
            if (grab == null) { grab = GetComponent<XRGrabInteractable>(); }
        }

        private void OnEnable()
        {
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }

        private void OnDisable()
        {
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnReleased);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            IsFalling = false;
            _dropPending = false;

            // Frozen in the hand: gravity would fight the grab's own positioning.
            body.useGravity = false;
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            // XRGrabInteractable restores the Rigidbody's original isKinematic during its own
            // detach, which happens after this event, so the drop has to be applied a frame
            // later or the restore undoes it.
            //
            // A flag consumed in Update does that without a coroutine. Release also fires while
            // the scene is being torn down, and StartCoroutine throws on an inactive object;
            // an object that is going away has nothing to drop, and Update simply never runs.
            _dropPending = true;
        }

        private void Update()
        {
            if (!_dropPending) { return; }

            _dropPending = false;
            ApplyDrop();
        }

        private void ApplyDrop()
        {
            body.isKinematic = false;
            body.useGravity = true;
            IsFalling = true;
        }
    }
}
