using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Tools;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Verifies the converted Sketchfab scalpel replaced the proxy without moving anything the
    /// reach measurements depend on, and that cut detection now tracks the real blade tip rather
    /// than the proxy's assumed one.
    /// </summary>
    public class ScalpelMeshTests
    {
        private const string SceneName = "SurgeryMVP";
        private ScalpelTool _scalpel;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return HeadlessScene.Load(SceneName);
            _scalpel = Object.FindFirstObjectByType<ScalpelTool>();
            Assert.IsNotNull(_scalpel, "No scalpel in the scene.");
        }

        [TearDown]
        public void TearDown() => SurgeryEvents.ResetAll();

        [UnityTest]
        public IEnumerator GripPoint_StaysAtTheValidatedWorldPosition()
        {
            SurgicalInteractable interactable = _scalpel.GetComponent<SurgicalInteractable>();
            Vector3 grip = interactable.GripPoint.position;

            Debug.Log($"[Scalpel] grip world = {grip.x:F4}, {grip.y:F4}, {grip.z:F4}");

            // Swapping the proxy for real geometry must not shift the grip: every reach number
            // already reported is measured from this exact point.
            Assert.AreEqual(-0.450f, grip.x, 0.0005f);
            Assert.AreEqual(1.080f, grip.y, 0.0005f);
            Assert.AreEqual(0.300f, grip.z, 0.0005f);

            yield break;
        }

        [UnityTest]
        public IEnumerator BladeTip_MatchesTheRealMeshTip()
        {
            BladeTip tip = _scalpel.BladeTip;
            Assert.IsNotNull(tip, "Scalpel has no BladeTip.");

            // Furthest point of the actual geometry along the tool's forward axis.
            float furthest = float.MinValue;
            Vector3 furthestWorld = Vector3.zero;
            foreach (MeshFilter filter in _scalpel.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                Assert.IsTrue(mesh.isReadable, $"{filter.name} mesh is not readable.");

                foreach (Vector3 v in mesh.vertices)
                {
                    Vector3 world = filter.transform.TransformPoint(v);
                    float along = _scalpel.transform.InverseTransformPoint(world).z;
                    if (along > furthest)
                    {
                        furthest = along;
                        furthestWorld = world;
                    }
                }
            }

            float tipAlong = _scalpel.transform.InverseTransformPoint(tip.transform.position).z;
            Debug.Log($"[Scalpel] mesh tip local z = {furthest:F5} | BladeTip local z = {tipAlong:F5} " +
                      $"| delta = {(tipAlong - furthest) * 1000f:F2} mm | tip world = {furthestWorld}");

            Assert.AreEqual(furthest, tipAlong, 0.002f,
                $"BladeTip sits {(tipAlong - furthest) * 1000f:F1} mm from the real blade tip. " +
                "Cut detection tracks this transform, so the blade would cut from the wrong point.");

            yield break;
        }

        [UnityTest]
        public IEnumerator ScalpelMesh_HasTheThreeConvertedParts()
        {
            // Unity collapses the FBX's root empty into the model-prefab root, so the parts land
            // one level up from where the Blender hierarchy puts them. Search by name rather than
            // asserting a fixed path.
            foreach (string part in new[] { "mango", "filo", "tope" })
            {
                bool found = false;
                foreach (Transform t in _scalpel.GetComponentsInChildren<Transform>())
                {
                    if (t.name == part)
                    {
                        found = true;
                        break;
                    }
                }

                Assert.IsTrue(found,
                    $"Converted model is missing '{part}'. Expected mango, filo and tope under one root.");
            }

            // Non-uniform parent scales from the Sketchfab export would skew the mesh on rotation.
            foreach (Transform t in _scalpel.GetComponentsInChildren<Transform>())
            {
                Vector3 s = t.localScale;
                Assert.AreEqual(s.x, s.y, 0.001f, $"'{t.name}' has a non-uniform scale {s}.");
                Assert.AreEqual(s.y, s.z, 0.001f, $"'{t.name}' has a non-uniform scale {s}.");
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator ScalpelIsRealisticallySized()
        {
            Bounds b = default;
            bool first = true;
            foreach (MeshRenderer r in _scalpel.GetComponentsInChildren<MeshRenderer>())
            {
                if (first)
                {
                    b = r.bounds;
                    first = false;
                }
                else
                {
                    b.Encapsulate(r.bounds);
                }
            }

            float length = Mathf.Max(b.size.x, b.size.y, b.size.z);
            Debug.Log($"[Scalpel] world bounds size = {b.size} (length {length:F4} m)");

            Assert.AreEqual(0.1208f, length, 0.004f,
                $"Scalpel is {length:F3} m long; the converted mesh measures 0.121 m. Import scale is off.");

            yield break;
        }

        [UnityTest]
        public IEnumerator NoProxyBoxesRemain()
        {
            Assert.IsNull(_scalpel.transform.Find("Handle"), "Proxy 'Handle' box still present.");
            Assert.IsNull(_scalpel.transform.Find("Blade"), "Proxy 'Blade' box still present.");
            yield break;
        }
    }
}
