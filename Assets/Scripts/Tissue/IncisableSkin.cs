using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace VRSurgery.Tissue
{
    /// <summary>
    /// Binary-state incision on a skin region: the first touch of the scalpel marks the region
    /// as incised, swaps its material, fires feedback and writes a timestamped log entry.
    ///
    /// This is deliberately NOT the swept-segment system in <see cref="IncisionSystem"/>. That one
    /// models a cut as a trajectory with depth and deviation; this one answers a single yes/no
    /// question and is what the MVP needs before any real-time mesh deformation exists. The two
    /// run side by side and do not talk to each other, so neither can double-count the other's work.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class IncisableSkin : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string regionName = "AbdominalSkin";

        [Tooltip("Tag the cutting instrument carries. Only colliders with this tag can incise.")]
        [SerializeField] private string scalpelTag = "Scalpel";

        [Header("Visual feedback")]
        [SerializeField] private Renderer surfaceRenderer;
        [SerializeField] private Material incisedMaterial;
        [SerializeField] private GameObject incisionFeedback;

        [Header("Audio feedback")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip incisionClip;

        [Header("Logging")]
        [SerializeField] private bool writeLogFile = true;
        [SerializeField] private string logFileName = "incision_log.txt";

        private Material _intactMaterial;

        /// <summary>True once the region has been cut. Latches — this is the single-fire guard.</summary>
        public bool IsIncised { get; private set; }

        /// <summary>
        /// How many times the incision actually fired. Exists so a test can prove that touching
        /// twice does not act twice; forgetting this guard is the classic version of this bug,
        /// because OnTriggerEnter can fire again on any re-entry.
        /// </summary>
        public int IncisionCount { get; private set; }

        public Vector3 IncisionPoint { get; private set; }
        public string LastLogEntry { get; private set; }
        public string RegionName => regionName;

        public string LogFilePath => Path.Combine(Application.persistentDataPath, logFileName);

        public event Action<IncisableSkin, Vector3> Incised;

        private void Awake()
        {
            if (surfaceRenderer != null)
            {
                _intactMaterial = surfaceRenderer.sharedMaterial;
            }

            if (incisionFeedback != null)
            {
                incisionFeedback.SetActive(false);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // The blade tip's own transform is the cut location — not the collider centre, and not
            // this region's centre, either of which would log a position the blade never occupied.
            TryIncise(other, other.transform.position);
        }

        /// <summary>
        /// Attempts the incision. Returns true only on the call that actually performs it, so
        /// callers can distinguish "cut now" from "already cut" and from "wrong instrument".
        /// </summary>
        public bool TryIncise(Collider other, Vector3 worldPoint)
        {
            if (IsIncised)
            {
                return false;
            }

            if (other == null || !other.CompareTag(scalpelTag))
            {
                return false;
            }

            IsIncised = true;
            IncisionCount++;
            IncisionPoint = worldPoint;

            ApplyVisual();
            PlaySound();
            WriteLog(worldPoint);

            Incised?.Invoke(this, worldPoint);
            return true;
        }

        private void ApplyVisual()
        {
            if (surfaceRenderer != null && incisedMaterial != null)
            {
                surfaceRenderer.sharedMaterial = incisedMaterial;
            }

            if (incisionFeedback != null)
            {
                incisionFeedback.SetActive(true);
            }
        }

        private void PlaySound()
        {
            if (audioSource != null && incisionClip != null)
            {
                audioSource.PlayOneShot(incisionClip);
            }
        }

        private void WriteLog(Vector3 worldPoint)
        {
            // InvariantCulture on purpose: this machine's locale writes decimal commas, which
            // would make the log ambiguous to parse and inconsistent between machines.
            LastLogEntry = string.Format(
                CultureInfo.InvariantCulture,
                "[{0:yyyy-MM-dd HH:mm:ss.fff}] {1} incised at ({2:F4}, {3:F4}, {4:F4})",
                DateTime.Now, regionName, worldPoint.x, worldPoint.y, worldPoint.z);

            Debug.Log("[IncisableSkin] " + LastLogEntry);

            if (!writeLogFile)
            {
                return;
            }

            try
            {
                File.AppendAllText(LogFilePath, LastLogEntry + Environment.NewLine);
            }
            catch (Exception exception)
            {
                // A failed write must not break the procedure; the console entry above still stands.
                Debug.LogWarning($"[IncisableSkin] Could not write {LogFilePath}: {exception.Message}");
            }
        }

        /// <summary>Returns the region to its intact state. Used by the surgery reset flow and tests.</summary>
        public void ResetSkin()
        {
            IsIncised = false;
            IncisionCount = 0;
            IncisionPoint = Vector3.zero;
            LastLogEntry = null;

            if (surfaceRenderer != null && _intactMaterial != null)
            {
                surfaceRenderer.sharedMaterial = _intactMaterial;
            }

            if (incisionFeedback != null)
            {
                incisionFeedback.SetActive(false);
            }
        }

        public void Configure(string name, Renderer renderer, Material incised, AudioSource source)
        {
            regionName = name;
            surfaceRenderer = renderer;
            incisedMaterial = incised;
            audioSource = source;
        }
    }
}
