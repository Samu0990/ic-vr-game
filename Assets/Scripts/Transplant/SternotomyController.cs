using System;
using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Opens the chest: swings the sternum clear of the ribcage to expose the heart.
    ///
    /// A real median sternotomy saws the bone down the midline and a retractor spreads the two
    /// halves apart. With the sternum modelled as one piece, the honest approximation is to lift
    /// it forward and rotate it up on its superior edge — the motion a surgeon sees as the
    /// retractor cranks open, without pretending to split geometry that has no split in it.
    ///
    /// The pose it returns to is whatever the sternum was authored at, captured on Awake. Nothing
    /// here assumes a position, because these models were placed by hand and any number written
    /// into this script would be wrong the moment the scene is re-dressed.
    /// </summary>
    public class SternotomyController : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The sternum. Defaults to this transform if left empty.")]
        [SerializeField] private Transform sternum;

        [Header("Opened pose, relative to the closed one")]
        [Tooltip("How far the sternum travels, in the sternum's own local axes, in metres.")]
        [SerializeField] private Vector3 localOffset = new Vector3(0f, 0.06f, -0.09f);

        [Tooltip("How far it tilts as it opens, in degrees. A little rotation reads as the " +
                 "retractor hinging it rather than the bone sliding off on rails.")]
        [SerializeField] private Vector3 localRotation = new Vector3(-38f, 0f, 6f);

        [Header("Timing")]
        [Tooltip("Seconds to open. At a stand this is time the visitor is not operating, so it " +
                 "wants to read as effortful without being something they wait through.")]
        [SerializeField, Min(0.1f)] private float openSeconds = 1.6f;

        [Tooltip("Closing is faster: it happens between visitors, when nobody is watching for it.")]
        [SerializeField, Min(0.1f)] private float closeSeconds = 0.6f;

        [Tooltip("Eased rather than linear. Bone under a retractor gives way slowly, then goes.")]
        [SerializeField]
        private AnimationCurve ease = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

        [Header("Reveal")]
        [Tooltip("Optional. Switched on as the chest opens — the heart, the pericardium, " +
                 "anything that should not be visible through a closed chest.")]
        [SerializeField] private GameObject[] revealOnOpen;

        [Tooltip("Fraction of the opening at which the reveal happens, so the heart appears once " +
                 "there is a gap to see it through rather than popping in behind solid bone.")]
        [SerializeField, Range(0f, 1f)] private float revealAt = 0.35f;

        /// <summary>0 closed, 1 fully open. The single value everything else reads.</summary>
        public float Openness01 { get; private set; }

        public bool IsOpen => Openness01 >= 1f;
        public bool IsMoving { get; private set; }

        public event Action Opened;
        public event Action Closed;

        private Vector3 _closedPosition;
        private Quaternion _closedRotation;
        private int _direction;

        private void Awake()
        {
            if (sternum == null)
            {
                sternum = transform;
            }

            _closedPosition = sternum.localPosition;
            _closedRotation = sternum.localRotation;

            SetReveal(false);
            Apply(0f);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>
        /// Advances the motion. Separated from Update so the pacing can be stepped exactly in a
        /// test instead of waited through.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_direction == 0 || deltaTime <= 0f)
            {
                return;
            }

            float duration = _direction > 0 ? openSeconds : closeSeconds;
            Openness01 = Mathf.Clamp01(Openness01 + _direction * (deltaTime / duration));

            Apply(Openness01);

            if (Openness01 >= 1f && _direction > 0)
            {
                _direction = 0;
                IsMoving = false;
                Opened?.Invoke();
            }
            else if (Openness01 <= 0f && _direction < 0)
            {
                _direction = 0;
                IsMoving = false;
                SetReveal(false);
                Closed?.Invoke();
            }
        }

        /// <summary>Starts the sternotomy. Safe to call again while it is already opening.</summary>
        public void Open()
        {
            if (IsOpen)
            {
                return;
            }

            _direction = 1;
            IsMoving = true;
        }

        /// <summary>Returns the chest to closed, for the next visitor.</summary>
        public void Close()
        {
            if (Openness01 <= 0f)
            {
                return;
            }

            _direction = -1;
            IsMoving = true;
        }

        /// <summary>Snaps shut with no animation. Used when a round is reset mid-operation.</summary>
        public void ResetClosed()
        {
            _direction = 0;
            IsMoving = false;
            Openness01 = 0f;
            SetReveal(false);
            Apply(0f);
        }

        private void Apply(float amount)
        {
            float eased = ease.Evaluate(amount);

            sternum.localPosition = _closedPosition + _closedRotation * (localOffset * eased);
            sternum.localRotation = _closedRotation * Quaternion.Euler(localRotation * eased);

            SetReveal(amount >= revealAt);
        }

        private void SetReveal(bool visible)
        {
            if (revealOnOpen == null)
            {
                return;
            }

            for (int i = 0; i < revealOnOpen.Length; i++)
            {
                if (revealOnOpen[i] != null && revealOnOpen[i].activeSelf != visible)
                {
                    revealOnOpen[i].SetActive(visible);
                }
            }
        }

        /// <summary>Wires the controller up from code, for a scene built by a builder script.</summary>
        public void Bind(Transform target, GameObject[] reveal)
        {
            sternum = target != null ? target : transform;
            revealOnOpen = reveal;
            _closedPosition = sternum.localPosition;
            _closedRotation = sternum.localRotation;
        }
    }
}
