using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Surgery;
using VRSurgery.Tissue;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Validates the real patient mesh in the built scene: that it imported at 1:1 metres, that
    /// it rests on the table, that the spine sits inside the torso, and — the one that actually
    /// matters for gameplay — that the surgical region is seated on the skin rather than buried
    /// under it. The previous placeholder box was 5.7 cm shallower than the real body, so this
    /// is exactly the failure that would otherwise reach a headset unnoticed.
    /// </summary>
    public class PatientMeshTests
    {
        private const string SceneName = "VerticalSlice";

        private GameObject _body;
        private GameObject _spine;
        private Bounds _bodyBounds;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return HeadlessScene.Load(SceneName);

            GameObject patient = GameObject.Find("Patient");
            Assert.IsNotNull(patient, "No 'Patient' object in the scene.");

            Transform bodyTransform = patient.transform.Find("ExternalBody");
            Transform spineTransform = patient.transform.Find("Vertebrae");
            Assert.IsNotNull(bodyTransform, "Patient has no 'ExternalBody' child — the FBX was not attached.");
            Assert.IsNotNull(spineTransform, "Patient has no 'Vertebrae' child.");

            _body = bodyTransform.gameObject;
            _spine = spineTransform.gameObject;
            _bodyBounds = WorldBounds(_body);
        }

        [TearDown]
        public void TearDown() => SurgeryEvents.ResetAll();

        private static Bounds WorldBounds(GameObject root)
        {
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>();
            Assert.Greater(renderers.Length, 0, $"'{root.name}' has no MeshRenderer.");

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                b.Encapsulate(renderers[i].bounds);
            }

            return b;
        }

        [UnityTest]
        public IEnumerator Body_ImportedAtOneToOneMetres()
        {
            Vector3 size = _bodyBounds.size;
            Debug.Log($"[Patient] body world bounds size={size} centre={_bodyBounds.center}");

            // Head-to-toe runs along Z. 1.7678 m was measured on the mesh before export; a
            // mismatch here means the FBX import scale is wrong, not that the model changed.
            Assert.AreEqual(1.7678f, size.z, 0.04f,
                $"Body length is {size.z:F3} m, expected ~1.768 m. FBX import scale is off.");
            Assert.AreEqual(0.3384f, size.y, 0.03f,
                $"Body thickness is {size.y:F3} m, expected ~0.338 m.");
            Assert.AreEqual(0.6854f, size.x, 0.04f,
                $"Body width is {size.x:F3} m, expected ~0.685 m (arms alongside).");

            yield break;
        }

        [UnityTest]
        public IEnumerator Body_RestsOnTheTableSurface()
        {
            // The mesh is authored with the patient's back at local Y = 0.
            Assert.AreEqual(1.02f, _bodyBounds.min.y, 0.02f,
                $"The patient's back is at y={_bodyBounds.min.y:F3}, not on the 1.02 m table surface.");

            yield break;
        }

        [UnityTest]
        public IEnumerator Body_IsStaticGeometryNotSkinned()
        {
            Assert.AreEqual(0, _body.GetComponentsInChildren<SkinnedMeshRenderer>().Length,
                "The patient body imported as a SkinnedMeshRenderer. The pose was supposed to be " +
                "baked into a static mesh — a decorative body should not cost per-frame skinning.");
            Assert.AreEqual(0, _body.GetComponentsInChildren<Animator>().Length,
                "The patient body carries an Animator; the rig should not have been exported.");

            yield break;
        }

        [UnityTest]
        public IEnumerator Spine_SitsInsideTheTorso()
        {
            Bounds spine = WorldBounds(_spine);
            Debug.Log($"[Patient] spine world bounds min={spine.min} max={spine.max}");

            Assert.IsTrue(_bodyBounds.Contains(spine.min) && _bodyBounds.Contains(spine.max),
                $"Spine bounds {spine.min}..{spine.max} escape the body {_bodyBounds.min}..{_bodyBounds.max}.");

            // Posterior: the spine belongs near the patient's back, i.e. the lower half of the
            // body's thickness, not floating up under the belly.
            float midThickness = _bodyBounds.min.y + _bodyBounds.size.y * 0.5f;
            Assert.Less(spine.center.y, midThickness,
                "The spine sits in the anterior half of the torso; it should be near the back.");

            yield break;
        }

        [UnityTest]
        public IEnumerator SurgicalRegion_IsSeatedOnTheSkinNotInsideIt()
        {
            TissueSurface tissue = Object.FindFirstObjectByType<TissueSurface>();
            Assert.IsNotNull(tissue, "No tissue surface in the scene.");

            Vector3 region = tissue.transform.position;

            // Find the actual skin height directly beneath the region by sampling the mesh,
            // which needs no collider and cannot be fooled by a stale placeholder constant.
            MeshFilter filter = _body.GetComponentInChildren<MeshFilter>();
            Assert.IsNotNull(filter, "Body has no MeshFilter to sample.");

            Mesh mesh = filter.sharedMesh;
            Transform meshTransform = filter.transform;
            const float window = 0.05f;

            float skinTop = float.MinValue;
            int samples = 0;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = meshTransform.TransformPoint(vertices[i]);
                if (Mathf.Abs(world.x - region.x) <= window && Mathf.Abs(world.z - region.z) <= window)
                {
                    samples++;
                    if (world.y > skinTop)
                    {
                        skinTop = world.y;
                    }
                }
            }

            Assert.Greater(samples, 20,
                $"Only {samples} skin vertices found beneath the surgical region — the region is " +
                "not over the body at all.");

            Debug.Log($"[Patient] region y={region.y:F4}  skin surface y={skinTop:F4}  " +
                      $"delta={(region.y - skinTop) * 1000f:F1} mm  (samples={samples})");

            Assert.GreaterOrEqual(region.y, skinTop - 0.015f,
                $"The surgical region sits {(skinTop - region.y) * 1000f:F0} mm BELOW the skin " +
                "surface — incisions would start inside the body.");
            Assert.LessOrEqual(region.y, skinTop + 0.02f,
                $"The surgical region floats {(region.y - skinTop) * 1000f:F0} mm above the skin — " +
                "the blade would cut empty air before reaching the patient.");

            yield break;
        }

        [UnityTest]
        public IEnumerator Patient_FitsOnTheTable()
        {
            GameObject tableTop = GameObject.Find("TableTop");
            Assert.IsNotNull(tableTop, "No TableTop in the scene.");

            Bounds table = WorldBounds(tableTop);
            Debug.Log($"[Fit] table z {table.min.z:F3}..{table.max.z:F3} | " +
                      $"body z {_bodyBounds.min.z:F3}..{_bodyBounds.max.z:F3} | " +
                      $"table x {table.min.x:F3}..{table.max.x:F3} | " +
                      $"body x {_bodyBounds.min.x:F3}..{_bodyBounds.max.x:F3}");

            Assert.GreaterOrEqual(_bodyBounds.min.z, table.min.z,
                $"The patient's feet reach z={_bodyBounds.min.z:F3} but the table ends at " +
                $"z={table.min.z:F3} — the body overhangs the foot end.");
            Assert.LessOrEqual(_bodyBounds.max.z, table.max.z,
                $"The patient's head reaches z={_bodyBounds.max.z:F3} but the table ends at " +
                $"z={table.max.z:F3}.");

            yield break;
        }

        /// <summary>
        /// Tray/patient overlap measured against the skin mesh, not bounding boxes. The AABB of a
        /// supine body spans its widest point over the whole length, so it reports a collision
        /// wherever a tray is merely alongside the patient — which is how this test first claimed
        /// a hard intersection that the mesh showed was really an arm overhanging from above.
        /// </summary>
        [UnityTest]
        public IEnumerator Trays_DoNotIntersectThePatientMesh()
        {
            MeshFilter filter = _body.GetComponentInChildren<MeshFilter>();
            Vector3[] verts = filter.sharedMesh.vertices;
            Transform t = filter.transform;

            foreach (string trayName in new[] { "Tray_Left", "Tray_Right" })
            {
                GameObject tray = GameObject.Find(trayName);
                Assert.IsNotNull(tray, $"No {trayName} in the scene.");

                Bounds b = WorldBounds(tray);
                int inside = 0;
                for (int i = 0; i < verts.Length; i++)
                {
                    if (b.Contains(t.TransformPoint(verts[i])))
                    {
                        inside++;
                    }
                }

                Debug.Log($"[Fit] {trayName} x {b.min.x:F3}..{b.max.x:F3} y {b.min.y:F3}..{b.max.y:F3} " +
                          $"z {b.min.z:F3}..{b.max.z:F3} | skin vertices inside tray volume = {inside}");

                Assert.AreEqual(0, inside,
                    $"{inside} skin vertices are inside {trayName}'s volume — the tray is embedded " +
                    "in the patient.");
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator Patient_NoPlaceholderBoxesRemain()
        {
            foreach (string stale in new[] { "Torso", "Drape_Head", "Drape_Legs" })
            {
                Assert.IsNull(GameObject.Find(stale),
                    $"Placeholder '{stale}' is still in the scene alongside the real body.");
            }

            yield break;
        }
    }
}
