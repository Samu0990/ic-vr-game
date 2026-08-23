using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRSurgery.Diagnostics;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Evaluates candidate standing positions against the same reach model the ergonomics tests
    /// use, so the anchor is placed from a measurement instead of a guess. This is a targeted
    /// evaluation of a handful of stances, not the full reach sweep.
    /// </summary>
    public static class StanceSolver
    {
        private const string ScenePath = "Assets/Scenes/SurgeryMVP.unity";

        public static void SolveFromCommandLine()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            GameObject region = GameObject.Find("SurgicalRegion");
            GameObject rig = GameObject.Find("XR Origin");
            Camera head = Camera.main;

            if (region == null || rig == null || head == null)
            {
                Debug.LogError("[Stance] Scene is missing the region, the rig or the head camera.");
                EditorApplication.Exit(1);
                return;
            }

            Vector3 field = region.transform.position;
            Vector3 eyeLocal = rig.transform.InverseTransformPoint(head.transform.position);
            Debug.Log($"@@S | field={field} | eye offset inside the rig={eyeLocal} (eye height {eyeLocal.y:F3} m)");

            // Candidates: approaching along the patient's axis from the feet, and standing at
            // the side of the table. Table occupies X +/-0.25, Z +/-0.95, so any stance inside
            // that footprint would put the player inside the furniture.
            EvaluateRow("pes (Z-)", field, eyeLocal, new[] {
                new Vector3(0f, 0f, -1.30f), new Vector3(0f, 0f, -1.00f),
                new Vector3(0f, 0f, -0.70f), new Vector3(0f, 0f, -0.40f) });

            EvaluateRow("lado (X+)", field, eyeLocal, new[] {
                new Vector3(0.75f, 0f, 0.406f), new Vector3(0.65f, 0f, 0.406f),
                new Vector3(0.55f, 0f, 0.406f), new Vector3(0.50f, 0f, 0.406f),
                new Vector3(0.45f, 0f, 0.406f), new Vector3(0.40f, 0f, 0.406f) });

            EditorApplication.Exit(0);
        }

        private static void EvaluateRow(string label, Vector3 field, Vector3 eyeLocal, Vector3[] stances)
        {
            foreach (Vector3 stance in stances)
            {
                // Face the field: the shoulder model takes yaw from the head.
                Vector3 flat = field - stance;
                flat.y = 0f;
                Quaternion yaw = flat.sqrMagnitude > 1e-6f
                    ? Quaternion.LookRotation(flat.normalized, Vector3.up)
                    : Quaternion.identity;

                Vector3 eye = stance + yaw * new Vector3(eyeLocal.x, 0f, eyeLocal.z) + Vector3.up * eyeLocal.y;

                float distance = ReachEnvelope.DistanceFromNearestShoulder(eye, yaw, field);
                ReachClass reach = ReachEnvelope.Classify(distance);

                Vector3 toField = field - eye;
                float horizontal = new Vector2(toField.x, toField.z).magnitude;
                float angle = Mathf.Atan2(eye.y - field.y, horizontal) * Mathf.Rad2Deg;

                bool insideTable = Mathf.Abs(stance.x) < 0.35f && Mathf.Abs(stance.z) < 1.00f;

                Debug.Log($"@@S | {label,-10} | stance=({stance.x:F2},{stance.z:F2}) | " +
                          $"ombro->campo={distance:F3} m [{reach}] | abaixo dos olhos={angle:F1} deg" +
                          (insideTable ? " | DENTRO DA MESA" : ""));
            }
        }
    }
}
