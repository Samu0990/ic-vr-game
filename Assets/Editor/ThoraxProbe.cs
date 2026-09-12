using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Renders the assembled thorax with the skin switched off, which is the only way to see
    /// whether the ribcage, sternum and heart actually landed inside each other.
    /// </summary>
    public static class ThoraxProbe
    {
        public static void RunFromCommandLine()
        {
            string output = "thorax";
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-out") { output = args[i + 1]; }
            }

            Directory.CreateDirectory(output);
            EditorSceneManager.OpenScene("Assets/Scenes/TransplanteCardiaco.unity", OpenSceneMode.Single);

            GameObject skin = GameObject.Find("Body_Skin");
            GameObject ribcage = GameObject.Find("Ribcage");
            GameObject sternum = GameObject.Find("Sternum");
            GameObject heart = GameObject.Find("Heart");

            Vector3 focus = heart != null ? heart.transform.position : new Vector3(0f, 1.15f, 0.47f);

            if (skin != null) { skin.SetActive(false); }

            Shot(Path.Combine(output, "a_torax_sem_pele.png"), focus + new Vector3(0.45f, 0.30f, -0.30f), focus, 950, 700);
            Shot(Path.Combine(output, "b_torax_lateral.png"), focus + new Vector3(0.60f, 0.06f, 0.02f), focus, 950, 700);

            if (sternum != null) { sternum.SetActive(false); }
            Shot(Path.Combine(output, "c_sem_esterno.png"), focus + new Vector3(0.30f, 0.40f, -0.15f), focus, 950, 700);

            if (ribcage != null) { ribcage.SetActive(false); }
            Shot(Path.Combine(output, "d_so_coracao.png"), focus + new Vector3(0.22f, 0.14f, -0.16f), focus, 950, 700);

            Debug.Log("@@THORAX_OK");
            EditorApplication.Exit(0);
        }

        private static void Shot(string path, Vector3 from, Vector3 look, int w, int h)
        {
            GameObject holder = new GameObject("ProbeCam");
            Camera cam = holder.AddComponent<Camera>();
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.005f;
            cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.11f, 0.14f);
            holder.transform.position = from;
            holder.transform.rotation = Quaternion.LookRotation((look - from).normalized, Vector3.up);

            RenderTexture rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D img = new Texture2D(w, h, TextureFormat.RGB24, false);
            img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            img.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, img.EncodeToPNG());

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(img);
            Object.DestroyImmediate(holder);
        }
    }
}
