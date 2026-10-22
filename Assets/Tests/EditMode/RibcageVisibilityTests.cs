using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// A caixa torácica tem de continuar aparecendo depois que o esterno abre.
    ///
    /// O defeito era de imagem, não de estado: as duas metades das costelas estavam ativas, no
    /// lugar certo e com a malha certa, e mesmo assim sumiam da tela — a "cavidade" vermelha que
    /// liga quando a pele abre era uma tigela opaca mais estreita que o gradil, e as metades,
    /// afastadas pelo afastador, ficavam do lado de fora da parede dela. O quanto sobrava
    /// visível dependia de onde estava a cabeça, por isso "às vezes".
    ///
    /// Por isso o teste renderiza: pinta as costelas de magenta sem luz e conta os pixels magenta,
    /// do ponto de vista de quem opera, com a malha da cavidade ligada e desligada. (Só a malha:
    /// o pericárdio e os fios do esterno são filhos da cavidade e cobrem o coração de verdade.)
    /// Sem placa de vídeo (-nographics) não há o que medir e o teste se declara ignorado; rode a
    /// suíte EditMode sem -nographics para ele valer.
    /// </summary>
    public class RibcageVisibilityTests
    {
        private const string ScenePath = "Assets/Scenes/TransplanteCardiaco.unity";
        private const int Width = 480;
        private const int Height = 360;

        [TearDown]
        public void TearDown() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [Test]
        public void ComOToraxAberto_AsCostelasContinuamVisiveis()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Sem placa de vídeo (-nographics): este teste mede pixels e precisa renderizar.");
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            GameObject sternum = GameObject.Find("Sternum");
            GameObject heart = FindIncludingInactive("Heart");
            Assert.IsNotNull(sternum);
            SternotomyController opening = sternum.GetComponent<SternotomyController>();
            opening.Bind(sternum.transform, heart != null ? new[] { heart } : new GameObject[0]);
            opening.ResetClosed();

            ChestSkinPatch skin = Object.FindFirstObjectByType<ChestSkinPatch>();
            Assert.IsNotNull(skin);
            skin.Open();
            skin.Tick(10f);

            opening.Open();
            opening.Tick(10f);
            Object.FindFirstObjectByType<SternalRetractor>()?.Apply(1f);
            RibcageSpreader spreader = Object.FindFirstObjectByType<RibcageSpreader>();
            Assert.IsNotNull(spreader);
            spreader.Apply(1f);

            Renderer[] ribs = GameObject.Find("Ribcage").GetComponentsInChildren<Renderer>(true);
            GameObject cavity = FindIncludingInactive("CavidadeToracica");
            Assert.IsNotNull(cavity, "A cena perdeu a cavidade torácica.");
            Assert.IsTrue(cavity.activeSelf, "Com a pele aberta a cavidade aparece.");
            Renderer liner = cavity.GetComponent<Renderer>();
            Assert.IsNotNull(liner);

            Vector3 look = Bounds(sternum).center;
            foreach (Vector3 eye in new[] { new Vector3(0.45f, 1.62f, 0.47f), new Vector3(0.25f, 1.48f, 0.47f) })
            {
                int withLiner = RibPixels(eye, look, ribs);
                liner.enabled = false;
                int withoutLiner = RibPixels(eye, look, ribs);
                liner.enabled = true;

                Assert.Greater(withoutLiner, 50, $"De {eye} as costelas nem estão no quadro; o teste mira errado.");
                Assert.GreaterOrEqual(withLiner, withoutLiner / 2,
                    $"De {eye}, a cavidade esconde as costelas: {withLiner} pixel(s) com ela, " +
                    $"{withoutLiner} sem. Ela tem de ficar atrás do gradil aberto, não na frente.");
            }
        }

        /// <summary>Ribs painted flat magenta, counted on screen: how much of them is in sight.</summary>
        private static int RibPixels(Vector3 eye, Vector3 look, Renderer[] ribs)
        {
            Material mask = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mask.SetColor("_BaseColor", Color.magenta);

            Material[][] saved = new Material[ribs.Length][];
            for (int i = 0; i < ribs.Length; i++)
            {
                saved[i] = ribs[i].sharedMaterials;
                Material[] flat = new Material[saved[i].Length];
                for (int m = 0; m < flat.Length; m++) { flat[m] = mask; }
                ribs[i].sharedMaterials = flat;
            }

            Color32[] pixels = Render(eye, look);
            for (int i = 0; i < ribs.Length; i++) { ribs[i].sharedMaterials = saved[i]; }
            Object.DestroyImmediate(mask);

            int visible = 0;
            foreach (Color32 c in pixels)
            {
                if (c.r > 200 && c.g < 60 && c.b > 200) { visible++; }
            }

            return visible;
        }

        private static Color32[] Render(Vector3 eye, Vector3 look)
        {
            GameObject holder = new GameObject("RibcageTestCamera");
            Camera cam = holder.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
            holder.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((look - eye).normalized, Vector3.up));

            RenderTexture rt = new RenderTexture(Width, Height, 24);
            cam.targetTexture = rt;
            AmbientMode oldMode = RenderSettings.ambientMode;
            Color oldLight = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.75f, 0.75f, 0.78f);
            cam.Render();
            RenderSettings.ambientMode = oldMode;
            RenderSettings.ambientLight = oldLight;

            RenderTexture.active = rt;
            Texture2D image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            Color32[] pixels = image.GetPixels32();

            cam.targetTexture = null;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(holder);
            return pixels;
        }

        private static GameObject FindIncludingInactive(string name)
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name == name) { return t.gameObject; }
            }

            return null;
        }

        private static Bounds Bounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) { b.Encapsulate(renderers[i].bounds); }
            return b;
        }
    }
}
