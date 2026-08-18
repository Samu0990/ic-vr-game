using System;
using UnityEngine;
using VRSurgery.Data;

namespace VRSurgery.Interaction
{
    /// <summary>
    /// Reusable grab component for surgical tools. Holds identity and grip data;
    /// tool-specific behavior (cutting, pinching, etc.) lives in separate components
    /// on the same GameObject, not here.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SurgicalInteractable : MonoBehaviour
    {
        [SerializeField] private ToolDefinition toolDefinition;
        [SerializeField] private Transform gripPoint;

        public ToolDefinition ToolDefinition => toolDefinition;
        public Transform GripPoint => gripPoint;
        public bool IsHeld { get; private set; }
        public IHandInteractor CurrentHolder { get; private set; }

        public event Action<SurgicalInteractable, IHandInteractor> Grabbed;
        public event Action<SurgicalInteractable, IHandInteractor> Released;

        public void OnGrabbed(IHandInteractor holder)
        {
            IsHeld = true;
            CurrentHolder = holder;
            Grabbed?.Invoke(this, holder);
        }

        public void OnReleased()
        {
            IHandInteractor previousHolder = CurrentHolder;
            IsHeld = false;
            CurrentHolder = null;
            Released?.Invoke(this, previousHolder);
        }
    }
}
