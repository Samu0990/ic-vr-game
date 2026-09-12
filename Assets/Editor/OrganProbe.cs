using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRSurgery.Transplant;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Reports the two heart instances: same mesh, same scale, different roles.
    /// </summary>
    public static class OrganProbe
    {
        public static void RunFromCommandLine()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/TransplanteCardiaco.unity", OpenSceneMode.Single);

            StringBuilder report = new StringBuilder();
            foreach (GrabbableOrgan organ in Object.FindObjectsByType<GrabbableOrgan>(FindObjectsSortMode.None))
            {
                Bounds bounds = new Bounds();
                bool any = false;
                string mesh = "(nenhuma)";

                foreach (MeshFilter filter in organ.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null) { continue; }
                    mesh = filter.sharedMesh.name;
                    if (!any) { bounds = filter.GetComponent<Renderer>().bounds; any = true; }
                    else { bounds.Encapsulate(filter.GetComponent<Renderer>().bounds); }
                }

                SerializedObject so = new SerializedObject(organ);
                string role = so.FindProperty("role").enumDisplayNames[so.FindProperty("role").enumValueIndex];

                report.AppendLine(
                    $"@@ORGAO '{organ.name}' papel={role} malha='{mesh}' " +
                    $"escala={organ.transform.lossyScale} " +
                    $"tamanho={bounds.size.x * 100f:F1}x{bounds.size.y * 100f:F1}x{bounds.size.z * 100f:F1}cm " +
                    $"posição={organ.transform.position}");
            }

            Debug.Log(report.ToString());
            Debug.Log("@@ORGAO_FIM");
            EditorApplication.Exit(0);
        }
    }
}
