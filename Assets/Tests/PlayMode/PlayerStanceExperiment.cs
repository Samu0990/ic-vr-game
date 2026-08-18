using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Diagnostics;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Tissue;

namespace VRSurgery.Tests
{
    /// <summary>
    /// Non-destructive experiment: re-measures the reach envelope with the player standing in
    /// different places, without editing the scene asset, the patient pose, or the trays.
    ///
    /// Applies exactly the same ReachEnvelope thresholds the workspace tests assert against, so
    /// the numbers are comparable to the ones already reported. Nothing here is committed to the
    /// build — moving the XR Origin happens on the loaded instance only.
    /// </summary>
    public class PlayerStanceExperiment
    {
        private const string SceneName = "VerticalSlice";

        private struct Stance
        {
            public string Label;
            public Vector3 OriginPosition;
            public float Yaw;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return HeadlessScene.Load(SceneName);
        }

        [TearDown]
        public void TearDown() => SurgeryEvents.ResetAll();

        [UnityTest]
        public IEnumerator CompareStances()
        {
            GameObject origin = GameObject.Find("XR Origin");
            Assert.IsNotNull(origin, "No XR Origin in the scene.");

            TissueSurface tissue = Object.FindFirstObjectByType<TissueSurface>();
            Assert.IsNotNull(tissue);
            Vector3 field = tissue.transform.position;

            Vector3 scalpel = Vector3.zero;
            Vector3 forceps = Vector3.zero;
            foreach (SurgicalInteractable tool in Object.FindObjectsByType<SurgicalInteractable>(FindObjectsSortMode.None))
            {
                Vector3 grip = tool.GripPoint != null ? tool.GripPoint.position : tool.transform.position;
                if (tool.name.ToLowerInvariant().Contains("scalpel")) scalpel = grip;
                if (tool.name.ToLowerInvariant().Contains("forceps")) forceps = grip;
            }

            SurgeryHUD hud = Object.FindFirstObjectByType<SurgeryHUD>();
            Vector3 monitor = hud != null ? hud.transform.position : Vector3.zero;

            // The abdomen sits at z = 0.42, so a surgeon standing at the patient's side lines up
            // with it. Offsets are measured from the table edge at x = +/-0.375.
            Stance[] stances =
            {
                new Stance { Label = "CURRENT (at the feet)", OriginPosition = new Vector3(0f, 0f, 0f), Yaw = 0f },
                new Stance { Label = "SIDE -X @0.55", OriginPosition = new Vector3(-0.55f, 0f, 0.42f), Yaw = 90f },
                new Stance { Label = "SIDE -X @0.62", OriginPosition = new Vector3(-0.62f, 0f, 0.42f), Yaw = 90f },
                new Stance { Label = "SIDE -X @0.70", OriginPosition = new Vector3(-0.70f, 0f, 0.42f), Yaw = 90f },
                new Stance { Label = "SIDE +X @0.62", OriginPosition = new Vector3(0.62f, 0f, 0.42f), Yaw = -90f },
            };

            StringBuilder table = new StringBuilder();
            table.AppendLine();
            table.AppendLine("STANCE                 | FIELD          | SCALPEL        | FORCEPS        | PITCH | MON.YAW");
            table.AppendLine("-----------------------+----------------+----------------+----------------+-------+--------");

            foreach (Stance s in stances)
            {
                origin.transform.SetPositionAndRotation(s.OriginPosition, Quaternion.Euler(0f, s.Yaw, 0f));
                yield return null;

                Camera cam = Camera.main;
                Vector3 eye = cam.transform.position;
                Quaternion head = cam.transform.rotation;

                string Cell(Vector3 target)
                {
                    float d = ReachEnvelope.DistanceFromNearestShoulder(eye, head, target);
                    return $"{d:F3}m {ReachEnvelope.Classify(d),-11}";
                }

                float pitch = ReachEnvelope.GazePitchDegrees(eye, head, field);
                float monYaw = ReachEnvelope.GazeYawDegrees(eye, head, monitor);

                table.AppendLine($"{s.Label,-22} | {Cell(field)} | {Cell(scalpel)} | {Cell(forceps)} | " +
                                 $"{pitch,5:F1} | {monYaw,6:F1}");
            }

            Debug.Log("[Stance]" + table);

            // Restore, so nothing leaks into a later test in the same run.
            origin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            yield return null;
        }

        /// <summary>
        /// The tray/patient collision was reported from axis-aligned bounds, which span the body's
        /// widest point over its entire length. This measures the real silhouette slice by slice
        /// so the required tray shift is a measured number rather than a worst case.
        /// </summary>
        [UnityTest]
        public IEnumerator MeasureArmEnvelopeInTrayRegion()
        {
            GameObject bodyObject = GameObject.Find("Patient").transform.Find("ExternalBody").gameObject;
            MeshFilter filter = bodyObject.GetComponentInChildren<MeshFilter>();
            Mesh mesh = filter.sharedMesh;
            Transform t = filter.transform;

            Vector3[] verts = mesh.vertices;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("Z BAND        | Y BAND        | max |x| of skin | tray inner edge 0.210 -> clear?");
            sb.AppendLine("--------------+---------------+-----------------+--------------------------------");

            (float zLo, float zHi)[] zBands = { (0.19f, 0.41f), (0.19f, 0.30f), (0.30f, 0.41f) };
            (float yLo, float yHi)[] yBands = { (1.014f, 1.042f), (1.014f, 1.10f), (1.014f, 1.36f) };

            foreach ((float zLo, float zHi) in zBands)
            {
                foreach ((float yLo, float yHi) in yBands)
                {
                    float maxAbsX = 0f;
                    int n = 0;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 w = t.TransformPoint(verts[i]);
                        if (w.z >= zLo && w.z <= zHi && w.y >= yLo && w.y <= yHi)
                        {
                            n++;
                            maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(w.x));
                        }
                    }

                    string verdict = n == 0 ? "no skin here"
                        : (maxAbsX <= 0.210f ? "CLEAR" : $"OVERLAP by {(maxAbsX - 0.210f) * 100f:F1} cm");
                    sb.AppendLine($"{zLo:F2}..{zHi:F2}   | {yLo:F3}..{yHi:F3} | {maxAbsX:F3} m (n={n,5}) | {verdict}");
                }
            }

            Debug.Log("[ArmEnvelope]" + sb);
            yield break;
        }

        /// <summary>
        /// Every geometric quantity that varies with tray position, alongside the resulting
        /// reach class. ReachEnvelope.Classify takes a single float — the shoulder distance —
        /// so this exists to show what else moves, and to confirm nothing else feeds the verdict.
        /// </summary>
        [UnityTest]
        public IEnumerator TrayPosition_ClassificationInputs()
        {
            Camera cam = Camera.main;
            Vector3 eye = cam.transform.position;
            Quaternion head = cam.transform.rotation;

            Vector3 grip = Vector3.zero;
            foreach (SurgicalInteractable tool in Object.FindObjectsByType<SurgicalInteractable>(FindObjectsSortMode.None))
            {
                if (tool.name.ToLowerInvariant().Contains("scalpel"))
                {
                    grip = tool.GripPoint != null ? tool.GripPoint.position : tool.transform.position;
                }
            }

            Vector3 shoulder = ReachEnvelope.ShoulderPosition(eye, head, true);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"left shoulder = {shoulder.x:F3},{shoulder.y:F3},{shoulder.z:F3}   " +
                          $"tool y={grip.y:F3} z={grip.z:F3} (constant across all rows)");
            sb.AppendLine();
            sb.AppendLine("tool |x| | dx     | dy     | dz     | distance | CLASS       | pitch  | yaw");
            sb.AppendLine("---------+--------+--------+--------+----------+-------------+--------+-------");

            foreach (float x in new[] { 0.34f, 0.42f, 0.45f, 0.48f })
            {
                Vector3 pos = new Vector3(-x, grip.y, grip.z);
                Vector3 d = pos - shoulder;
                float dist = ReachEnvelope.DistanceFromNearestShoulder(eye, head, pos);
                sb.AppendLine($"  {x:F2}   | {d.x,6:F3} | {d.y,6:F3} | {d.z,6:F3} | {dist,8:F3} | " +
                              $"{ReachEnvelope.Classify(dist),-11} | " +
                              $"{ReachEnvelope.GazePitchDegrees(eye, head, pos),6:F1} | " +
                              $"{ReachEnvelope.GazeYawDegrees(eye, head, pos),5:F1}");
            }

            Debug.Log("[TrayInputs]" + sb);
            yield break;
        }

        /// <summary>
        /// Real 3D clearance between a tool's grip point and the patient's skin, sampled against
        /// the mesh. This is the number that decides whether a hand can actually get to the tool,
        /// which the reach class says nothing about.
        /// </summary>
        [UnityTest]
        public IEnumerator ToolClearanceAgainstSkin()
        {
            GameObject bodyObject = GameObject.Find("Patient").transform.Find("ExternalBody").gameObject;
            MeshFilter filter = bodyObject.GetComponentInChildren<MeshFilter>();
            Vector3[] verts = filter.sharedMesh.vertices;
            Transform t = filter.transform;

            Vector3 grip = Vector3.zero;
            foreach (SurgicalInteractable tool in Object.FindObjectsByType<SurgicalInteractable>(FindObjectsSortMode.None))
            {
                if (tool.name.ToLowerInvariant().Contains("scalpel"))
                {
                    grip = tool.GripPoint != null ? tool.GripPoint.position : tool.transform.position;
                }
            }

            // The direct interactor's grab sphere is 0.06 m; the hand needs at least that much
            // room around the grip point before it starts pushing into the patient.
            const float handRadius = 0.06f;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"ACTUAL scalpel grip in scene: {grip.x:F3}, {grip.y:F3}, {grip.z:F3}");
            sb.AppendLine();
            sb.AppendLine("tool |x| | nearest skin | gap vs hand r=0.06 | nearest vertex (x,y,z) | arm top y nearby");
            sb.AppendLine("---------+--------------+--------------------+------------------------+-----------------");

            // Mathf.Abs(grip.x) first: that is the position actually shipped in the scene, the rest
            // is the sweep it was chosen from.
            foreach (float x in new[] { Mathf.Abs(grip.x), 0.34f, 0.42f, 0.44f, 0.45f, 0.48f })
            {
                Vector3 pos = new Vector3(-x, grip.y, grip.z);

                float nearest = float.MaxValue;
                Vector3 nearestVert = Vector3.zero;
                float armTop = float.MinValue;

                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 w = t.TransformPoint(verts[i]);
                    float dist = Vector3.Distance(w, pos);
                    if (dist < nearest)
                    {
                        nearest = dist;
                        nearestVert = w;
                    }

                    // Height of the arm in the corridor the hand descends through.
                    if (Mathf.Abs(w.z - pos.z) <= 0.11f && w.x <= -0.20f && w.x >= pos.x)
                    {
                        armTop = Mathf.Max(armTop, w.y);
                    }
                }

                float margin = nearest - handRadius;
                string verdict = margin >= 0.02f ? "OK" : (margin >= 0f ? "TIGHT" : "BLOCKED");
                sb.AppendLine($"  {x:F2}   |   {nearest:F3} m    |  {margin,+6:F3} m {verdict,-7} | " +
                              $"{nearestVert.x,6:F3},{nearestVert.y:F3},{nearestVert.z:F3} | " +
                              $"{(armTop > float.MinValue ? armTop.ToString("F3") : "n/a")}");
            }

            Debug.Log("[Clearance]" + sb);
            yield break;
        }
    }
}
