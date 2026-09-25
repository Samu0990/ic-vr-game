using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    /// <summary>Reports what shader and textures a model's materials actually came in with.</summary>
    public static class MaterialProbe
    {
        public static void RunFromCommandLine()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SurgeryMVP.unity", OpenSceneMode.Single);

            GameObject table = GameObject.Find("OperatingTable");
            if (table == null) { Debug.Log("@@P sem mesa"); EditorApplication.Exit(0); return; }

            foreach (Renderer r in table.GetComponentsInChildren<Renderer>())
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) { Debug.Log($"@@P {r.name}: material NULO"); continue; }

                    string baseMap = m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null
                        ? m.GetTexture("_BaseMap").name : "(nenhuma)";
                    string mainTex = m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null
                        ? m.GetTexture("_MainTex").name : "(nenhuma)";
                    Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                            : (m.HasProperty("_Color") ? m.GetColor("_Color") : Color.magenta);

                    Debug.Log($"@@P {r.name} | shader='{m.shader.name}'");
                    int count = UnityEditor.ShaderUtil.GetPropertyCount(m.shader);
                    for (int i = 0; i < count; i++)
                    {
                        string prop = UnityEditor.ShaderUtil.GetPropertyName(m.shader, i);
                        var kind = UnityEditor.ShaderUtil.GetPropertyType(m.shader, i);
                        if (kind == UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv)
                        {
                            Texture t = m.GetTexture(prop);
                            if (t != null) Debug.Log($"@@T   {prop} = {t.name} ({t.width}px)");
                        }
                        else if (kind == UnityEditor.ShaderUtil.ShaderPropertyType.Color)
                        {
                            Debug.Log($"@@C   {prop} = {m.GetColor(prop)}");
                        }
                    }
                }
            }

            Debug.Log("@@P_FIM");
            EditorApplication.Exit(0);
        }
    }
}
