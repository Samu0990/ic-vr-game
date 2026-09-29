using System.Collections.Generic;
using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// Blood that runs from a fresh incision down the side of the chest.
    ///
    /// The beads along the cut are the incision's own business; this is what happens next on a
    /// real chest: some of them overflow and trickle downhill, following the curve of the skin
    /// toward the flank. The path is not scripted — each trickle steps down the slope of the
    /// sampled skin surface, so it bends where the chest bends and slows where it flattens.
    ///
    /// A cut that went too deep always starts one; an ordinary stretch of cut only sometimes.
    /// Pooled: a fixed handful of objects, reused, so nothing is allocated while the visitor cuts.
    /// </summary>
    public class IncisionBleeding : MonoBehaviour
    {
        [SerializeField] private SkinIncisionWorker incision;
        [SerializeField] private ChestSkinPatch patch;

        [Tooltip("Pooled trickle objects: stretched drops, hidden until used.")]
        [SerializeField] private List<Transform> trickles = new List<Transform>();

        [Tooltip("Chance an ordinary stretch of cut starts a trickle. A deep one always does.")]
        [SerializeField, Range(0f, 1f)] private float chance = 0.25f;

        [Tooltip("Speed of a trickle down a steep slope, in metres per second.")]
        [SerializeField, Min(0.001f)] private float speed = 0.012f;

        [Tooltip("How far a trickle runs before it dries in place, in metres.")]
        [SerializeField, Min(0.005f)] private float maxRun = 0.05f;

        [SerializeField, Min(0.0005f)] private float thickness = 0.0022f;

        private readonly List<Trickle> _live = new List<Trickle>();
        private readonly System.Random _random = new System.Random(19);
        private int _seenCuts;

        /// <summary>Stretches of the line already given their chance to bleed, so none is rolled twice.</summary>
        private bool[] _considered = new bool[0];

        private sealed class Trickle
        {
            public Transform Body;
            public Vector3 Start;
            public Vector3 Head;
            public float Run;
            public bool Flowing;
        }

        /// <summary>Trickles currently shown. Exposed for tests.</summary>
        public int ActiveCount => _live.Count;

        private void Awake() => HideAll();

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (incision == null || patch == null) { return; }

            // A new visitor, or a chest opened up: the old trickles go.
            if (incision.CutCount < _seenCuts || patch.Openness01 > 0.02f)
            {
                if (_live.Count > 0) { HideAll(); }
                if (incision.CutCount < _seenCuts) { _considered = new bool[incision.Bins]; }
                _seenCuts = incision.CutCount;
                return;
            }

            if (incision.CutCount > _seenCuts)
            {
                SpawnForNewCuts();
                _seenCuts = incision.CutCount;
            }

            if (deltaTime <= 0f) { return; }

            foreach (Trickle trickle in _live)
            {
                if (trickle.Flowing) { Flow(trickle, deltaTime); }
                Draw(trickle);
            }
        }

        private void SpawnForNewCuts()
        {
            if (_considered == null || _considered.Length != incision.Bins) { _considered = new bool[incision.Bins]; }

            for (int bin = 0; bin < incision.Bins; bin++)
            {
                if (!incision.IsBinCut(bin) || _considered[bin]) { continue; }
                _considered[bin] = true;

                bool deep = incision.IsBinDeep(bin);
                if (!deep && _random.NextDouble() > chance) { continue; }

                Transform body = FreeBody();
                if (body == null) { return; }

                float along = (bin + 0.5f) / incision.Bins;
                float side = _random.NextDouble() < 0.5 ? -1f : 1f;
                Vector3 start = patch.IncisionPoint(along, side * 0.003f, 0.0008f);

                _live.Add(new Trickle { Body = body, Start = start, Head = start, Run = 0f, Flowing = true });
                body.gameObject.SetActive(true);
            }
        }

        private Transform FreeBody()
        {
            foreach (Transform body in trickles)
            {
                if (body == null) { continue; }
                bool used = false;
                foreach (Trickle t in _live) { if (t.Body == body) { used = true; break; } }
                if (!used) { return body; }
            }

            return null;
        }

        /// <summary>One step downhill over the skin, slower where the skin is flatter.</summary>
        private void Flow(Trickle trickle, float deltaTime)
        {
            const float probe = 0.004f;
            Vector3 p = trickle.Head;
            float hx = patch.SurfaceHeightUnder(p + Vector3.right * probe) - patch.SurfaceHeightUnder(p - Vector3.right * probe);
            float hz = patch.SurfaceHeightUnder(p + Vector3.forward * probe) - patch.SurfaceHeightUnder(p - Vector3.forward * probe);
            Vector2 downhill = -new Vector2(hx, hz) / (2f * probe);

            float slope = downhill.magnitude;
            if (slope < 0.05f)
            {
                // Flat enough that surface tension wins: the trickle stops and dries.
                trickle.Flowing = false;
                return;
            }

            Vector2 dir = downhill / slope;
            float step = speed * Mathf.Clamp01(slope) * deltaTime;
            Vector3 next = p + new Vector3(dir.x, 0f, dir.y) * step;
            next.y = patch.SurfaceHeightUnder(next) + 0.0008f;

            trickle.Run += Vector3.Distance(p, next);
            trickle.Head = next;
            if (trickle.Run >= maxRun) { trickle.Flowing = false; }
        }

        /// <summary>A drop stretched from where it started to where its head is now.</summary>
        private void Draw(Trickle trickle)
        {
            Vector3 along = trickle.Head - trickle.Start;
            float length = Mathf.Max(thickness, along.magnitude + thickness);
            trickle.Body.position = (trickle.Start + trickle.Head) * 0.5f;
            trickle.Body.rotation = along.sqrMagnitude > 1e-10f
                ? Quaternion.FromToRotation(Vector3.up, along.normalized)
                : Quaternion.identity;

            // A capsule is two units tall at unit scale.
            trickle.Body.localScale = new Vector3(thickness, length * 0.5f, thickness);
        }

        private void HideAll()
        {
            _live.Clear();
            foreach (Transform body in trickles)
            {
                if (body != null) { body.gameObject.SetActive(false); }
            }
        }

        public void Bind(SkinIncisionWorker cut, ChestSkinPatch skin, IEnumerable<Transform> pool)
        {
            incision = cut;
            patch = skin;
            trickles = pool != null ? new List<Transform>(pool) : new List<Transform>();
        }
    }
}
