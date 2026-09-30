using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Fotografa a cena do transplante enquadrando o que existe, não coordenadas escritas à mão.
    ///
    /// As ferramentas de captura que já existiam — SceneShots, ThoraxProbe — têm posição de
    /// câmera fixa, calibrada para o layout de quando foram escritas. A cena mudou várias vezes
    /// desde então (mesa de Mayo, pericárdio, campos cirúrgicos) e hoje as duas devolvem fotos de
    /// azulejo e de chão. Uma foto de diagnóstico que não aponta para o alvo é pior que nenhuma:
    /// custa o mesmo tempo e ainda dá a impressão de que foi verificado.
    ///
    /// Aqui a câmera sai das bounds do objeto, então mover o paciente move o enquadramento junto.
    ///
    /// Uso:
    ///   -executeMethod VRSurgery.EditorTools.TransplantShots.RunFromCommandLine -out PASTA
    /// </summary>
    public static class TransplantShots
    {
        private const string ScenePath = "Assets/Scenes/TransplanteCardiaco.unity";

        public static void RunFromCommandLine()
        {
            string output = "TransplantShots";
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-out") { output = args[i + 1]; }
            }

            try
            {
                Capture(output);
                Debug.Log("[Shots] TRANSPLANT_SHOTS_OK");
                EditorApplication.Exit(0);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[Shots] TRANSPLANT_SHOTS_FAILED " + exception);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("VRSurgery/Fotos do transplante")]
        public static void CaptureMenu() => Capture("TransplantShots");

        public static void Capture(string folder)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(folder);

            GameObject heart = Find("Heart");
            GameObject donor = Find("DonorHeart");
            GameObject drapes = Find("CamposCirurgicos") ?? Find("Campo");
            GameObject skin = Find("Body_Skin");

            // O coração nativo nasce desligado — o esterno só o revela ao abrir. Para fotografar
            // é preciso ligá-lo, senão a foto do órgão principal sai vazia.
            bool heartWasOff = heart != null && !heart.activeSelf;
            if (heartWasOff) { heart.SetActive(true); }

            Shoot(folder, "1_paciente_inteiro", skin, 1.15f, 45f, above: true);
            Shoot(folder, "2_torax_de_cima", skin, 1.30f, 45f, above: true, focusChest: true);
            Shoot(folder, "3_campos_de_lado", drapes, 1.10f, 45f, above: false);
            Shoot(folder, "4_coracao_nativo", heart, 1.60f, 40f, above: true);
            Shoot(folder, "5_coracao_doador", donor, 1.60f, 40f, above: true);

            // Com a pele escondida dá para ver o coração dentro do gradil, que é o que o
            // ReportFit afirma por número mas ninguém nunca conferiu com o olho.
            if (skin != null) { skin.SetActive(false); }
            Shoot(folder, "6_coracao_no_gradil", heart, 1.70f, 40f, above: true);
            if (skin != null) { skin.SetActive(true); }

            if (heartWasOff) { heart.SetActive(false); }

            Debug.Log($"[Shots] {folder}: fotos geradas");
        }

        /// <summary>Acha por nome incluindo objetos desligados, que GameObject.Find ignora.</summary>
        private static GameObject Find(string name)
        {
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t.name == name && t.gameObject.scene.IsValid()) { return t.gameObject; }
            }

            return null;
        }

        private static Bounds WorldBounds(GameObject go)
        {
            bool any = false;
            Bounds bounds = new Bounds();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = r.bounds; any = true; } else { bounds.Encapsulate(r.bounds); }
            }

            return bounds;
        }

        /// <summary>
        /// Enquadra <paramref name="target"/> a partir das bounds dele.
        ///
        /// A distância sai da trigonometria do campo de visão, não de um múltiplo do tamanho. Com
        /// múltiplo, a primeira versão pôs a câmera dentro do gradil ao fotografar o coração (21cm
        /// x 0,42 = 9cm, ou seja, dentro do peito) e acima do forro ao fotografar o corpo inteiro
        /// (2m x 1,9 = 3,8m de altura). Com a trigonometria, um órgão e um corpo ficam enquadrados
        /// pela mesma conta.
        ///
        /// <paramref name="padding"/> é folga em volta do alvo: 1,0 encosta nas bordas, 1,6 deixa
        /// contexto em volta.
        /// </summary>
        private static void Shoot(string folder, string name, GameObject target, float padding,
            float fov, bool above, bool focusChest = false)
        {
            if (target == null)
            {
                Debug.LogWarning($"[Shots] '{name}': alvo ausente na cena, foto pulada");
                return;
            }

            Bounds b = WorldBounds(target);
            if (b.size.sqrMagnitude < 1e-8f)
            {
                Debug.LogWarning($"[Shots] '{name}': alvo sem volume, foto pulada");
                return;
            }

            Vector3 look = b.center;
            float span = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));

            // O tórax fica a cerca de 3/4 da altura do corpo, contando dos pés — o paciente está
            // deitado com a cabeça em +Z, então é ao longo de Z que se procura. Aqui o alvo é a
            // janela no peito, não o corpo, então o vão é a largura e não a estatura.
            if (focusChest)
            {
                look = new Vector3(b.center.x, b.max.y, Mathf.Lerp(b.min.z, b.max.z, 0.74f));
                span = Mathf.Max(b.size.x, b.size.y);
            }

            // Distância que põe um objeto de `span` metros dentro do campo de visão.
            float reach = (span * 0.5f) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * padding;

            Vector3 direction = above
                ? new Vector3(0.45f, 0.82f, 0.35f)
                : new Vector3(0.92f, 0.30f, 0.25f);

            Vector3 from = look + direction.normalized * reach;

            Render(Path.Combine(folder, name + ".png"), from, look, fov);
            Debug.Log($"[Shots] {name}: alvo {b.size.x * 100f:F0}x{b.size.y * 100f:F0}x{b.size.z * 100f:F0}cm");
        }

        private static void Render(string path, Vector3 from, Vector3 look, float fov)
        {
            GameObject holder = new GameObject("ShotCamera");
            Camera cam = holder.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 60f;
            cam.transform.position = from;
            cam.transform.rotation = Quaternion.LookRotation((look - from).normalized, Vector3.up);

            RenderTexture target = new RenderTexture(1400, 900, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };

            cam.targetTexture = target;
            cam.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D shot = new Texture2D(1400, 900, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 1400, 900), 0, 0);
            shot.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(path, shot.EncodeToPNG());

            cam.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(shot);
            Object.DestroyImmediate(holder);
        }
    }
}
