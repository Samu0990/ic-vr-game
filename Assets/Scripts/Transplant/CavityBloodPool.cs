using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// The blood lying in the bottom of the open chest.
    ///
    /// An open chest is never dry. A thin pool sits at the bottom from the moment it is opened;
    /// while a vessel join leaks it rises, visibly, toward the heart — the physical consequence of
    /// a bad anastomosis that the monitor can only describe — and once the leak is stopped the
    /// team's suction takes it back down, more slowly than it came up.
    ///
    /// The pool is a flat disc inside an ellipsoidal cavity, so its outline is resized to the
    /// cavity's cross-section at whatever height the blood has reached.
    /// </summary>
    public class CavityBloodPool : MonoBehaviour
    {
        [SerializeField] private AnastomosisWorker vessels;

        [Header("Cavity shape (local to the cavity's rim)")]
        [SerializeField] private float radiusX = 0.07f;
        [SerializeField] private float radiusZ = 0.12f;
        [SerializeField] private float depth = 0.15f;

        [Header("Level, as a fraction of the depth measured up from the bottom")]
        [SerializeField, Range(0f, 1f)] private float restLevel = 0.12f;
        [SerializeField, Range(0f, 1f)] private float leakLevel = 0.55f;

        [Tooltip("Fraction of the depth the blood rises per second while a vessel leaks.")]
        [SerializeField, Min(0.001f)] private float riseRate = 0.08f;

        [Tooltip("Fraction of the depth suction removes per second once the leak is stopped.")]
        [SerializeField, Min(0.001f)] private float drainRate = 0.03f;

        /// <summary>Current level, 0 at the bottom of the cavity and 1 at its rim.</summary>
        public float Level01 { get; private set; }

        private void OnEnable()
        {
            if (Level01 <= 0f) { Level01 = restLevel; }
            Apply();
        }

        // The cavity is hidden when the chest closes, which is also when this visitor's blood is
        // done with: the next chest to be opened starts from a thin film again.
        private void OnDisable() => Level01 = restLevel;

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) { return; }

            bool leaking = vessels != null && vessels.BleedingSite != null;
            float target = leaking ? leakLevel : restLevel;
            float rate = leaking ? riseRate : drainRate;
            Level01 = Mathf.MoveTowards(Level01, target, rate * deltaTime);
            Apply();
        }

        private void Apply()
        {
            // Height below the rim, and the ellipse the cavity wall makes at that height.
            float below = depth * (1f - Level01);
            float ratio = depth > 0f ? Mathf.Clamp01(below / depth) : 0f;
            float fit = Mathf.Sqrt(Mathf.Max(0f, 1f - ratio * ratio)) * 0.97f;

            transform.localPosition = new Vector3(0f, -below, 0f);

            // Unity's cylinder is one unit across at unit scale.
            transform.localScale = new Vector3(radiusX * 2f * fit, 0.0008f, radiusZ * 2f * fit);
        }

        /// <summary>Back to a thin film for the next visitor.</summary>
        public void ResetPool()
        {
            Level01 = restLevel;
            Apply();
        }

        /// <summary>Which vessel joins can make the pool rise. Set once the joins exist.</summary>
        public void BindVessels(AnastomosisWorker anastomosis) => vessels = anastomosis;

        public void Bind(AnastomosisWorker anastomosis, float rx, float rz, float cavityDepth)
        {
            vessels = anastomosis;
            radiusX = rx;
            radiusZ = rz;
            depth = cavityDepth;
            Level01 = restLevel;
            Apply();
        }
    }
}
