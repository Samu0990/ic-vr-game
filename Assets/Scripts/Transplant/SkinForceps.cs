using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRSurgery.Interaction;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Tissue forceps for the skin: squeeze the trigger with the tips on the skin and it is held;
    /// move the hand and the skin comes with it, stretching; let go and it springs back.
    ///
    /// This is how every surgery game (and every surgeon) handles a wound edge: lift it with the
    /// forceps in one hand, pass the needle with the other. Closing the pinch in the air takes
    /// nothing, like real forceps; closing it on the skin takes the skin. The tines visibly
    /// close while the trigger is held.
    /// </summary>
    public class SkinForceps : MonoBehaviour
    {
        [SerializeField] private Transform tip;
        [SerializeField] private SurgicalInteractable tool;
        [SerializeField] private XRGrabInteractable grab;
        [SerializeField] private ChestSkinPatch patch;

        [Tooltip("The two tines, pivoted at the heel; they swing together while pinching.")]
        [SerializeField] private Transform[] tines = new Transform[0];
        [SerializeField] private float openDegrees = 1.2f;
        [SerializeField] private float closedDegrees = -1.5f;

        private int _handle = -1;
        private bool _pinching;
        private float _closed;
        private bool _subscribed;

        /// <summary>True while the forceps hold skin.</summary>
        public bool IsHoldingSkin => _handle >= 0;

        /// <summary>Where the held skin is being pulled from: the forceps tip.</summary>
        public Vector3 HoldPoint => tip != null ? tip.position : transform.position;

        public event Action Pinched;
        public event Action LetGo;

        private void OnEnable() => Subscribe();

        private void OnDisable()
        {
            Unsubscribe();
            SetPinch(false);
        }

        private void Subscribe()
        {
            if (_subscribed || grab == null) { return; }
            _subscribed = true;
            grab.activated.AddListener(HandleActivated);
            grab.deactivated.AddListener(HandleDeactivated);
            grab.selectExited.AddListener(HandleReleased);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || grab == null) { return; }
            _subscribed = false;
            grab.activated.RemoveListener(HandleActivated);
            grab.deactivated.RemoveListener(HandleDeactivated);
            grab.selectExited.RemoveListener(HandleReleased);
        }

        private void HandleActivated(ActivateEventArgs args) => SetPinch(true);
        private void HandleDeactivated(DeactivateEventArgs args) => SetPinch(false);
        private void HandleReleased(SelectExitEventArgs args) => SetPinch(false);

        /// <summary>Squeezes or opens the forceps. Squeezing on the skin takes hold of it.</summary>
        public void SetPinch(bool on)
        {
            if (on == _pinching) { return; }
            _pinching = on;

            if (on)
            {
                if (patch != null && tip != null && (tool == null || tool.IsHeld))
                {
                    _handle = patch.Grab(tip.position);
                    if (_handle >= 0) { Pinched?.Invoke(); }
                }

                return;
            }

            if (_handle >= 0)
            {
                if (patch != null) { patch.Release(_handle); }
                _handle = -1;
                LetGo?.Invoke();
            }
        }

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            // Put down, the forceps let go of whatever they held.
            if (_handle >= 0 && tool != null && !tool.IsHeld) { SetPinch(false); }

            if (_handle >= 0 && patch != null && tip != null) { patch.Drag(_handle, tip.position); }

            _closed = Mathf.MoveTowards(_closed, _pinching ? 1f : 0f, deltaTime * 12f);
            float angle = Mathf.Lerp(openDegrees, closedDegrees, _closed);
            for (int i = 0; i < tines.Length; i++)
            {
                if (tines[i] == null) { continue; }
                float side = i % 2 == 0 ? -1f : 1f;
                tines[i].localRotation = Quaternion.Euler(0f, side * angle, 0f);
            }
        }

        public void Bind(Transform forcepsTip, SurgicalInteractable forcepsTool, XRGrabInteractable forcepsGrab,
            ChestSkinPatch skin, Transform[] forcepsTines)
        {
            bool live = Application.isPlaying && isActiveAndEnabled;
            if (live) { Unsubscribe(); }

            tip = forcepsTip;
            tool = forcepsTool;
            grab = forcepsGrab;
            patch = skin;
            tines = forcepsTines ?? new Transform[0];

            if (live) { Subscribe(); }
        }
    }
}
