using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.Surgery;
using VRSurgery.Transplant;
using VRSurgery.VR;

namespace VRSurgery.EditorTools
{
    public static partial class TransplanteSceneBuilder
    {
        private const string MenuTextureFolder = "Assets/Textures/Menu/";

        /// <summary>
        /// Where the menu room stands: far enough from the theatre that nothing of it can be seen
        /// from inside the dome, and nothing of the dome from the theatre.
        /// </summary>
        private static readonly Vector3 MenuOrigin = new Vector3(400f, 0f, 0f);

        /// <summary>The logo's purple.</summary>
        private static readonly Color MenuPurple = new Color(0.52f, 0.09f, 0.98f);

        private static readonly Vector2 MenuButtonSize = new Vector2(0.34f, 0.106f);

        /// <summary>
        /// The start menu: a dark domed room of its own with a starry sky, a floor with a lit ring
        /// to stand in, the Meta Health logo floating ahead and one button, INICIAR, within reach.
        /// <see cref="StartMenu"/> sends the visitor there whenever no turn is being played and
        /// back to the surgeon's place when the button is pressed. The theatre is not touched.
        /// </summary>
        private static void BuildStartMenu(GameObject systems)
        {
            EventSessionController session = systems.GetComponent<EventSessionController>();
            GameObject rig = GameObject.Find("XR Origin");
            Camera head = rig != null ? rig.GetComponentInChildren<Camera>(true) : null;
            VisitorFit fit = rig != null ? rig.GetComponent<VisitorFit>() : null;
            if (session == null || head == null || fit == null)
            {
                Debug.LogWarning("[Transplante] sem sessão, câmera ou ajuste ao visitante: o menu inicial não foi montado.");
                return;
            }

            GameObject root = new GameObject("MenuInicial");
            root.transform.position = MenuOrigin;

            GameObject room = new GameObject("SalaDoMenu");
            room.transform.SetParent(root.transform, false);

            Transform stance = new GameObject("PosicaoMenu").transform;
            stance.SetParent(root.transform, false);

            // The sky: a sphere seen from inside, dark at the feet, a violet band at the horizon,
            // stars overhead.
            GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "CeuDoMenu";
            Object.DestroyImmediate(dome.GetComponent<Collider>());
            dome.transform.SetParent(room.transform, false);
            dome.transform.localScale = Vector3.one * 20f;
            Material sky = MakeOpaqueUnlit(Color.white);
            sky.SetTexture("_BaseMap", MenuTexture("MenuCeu.png", false));
            sky.SetFloat("_Cull", (float)CullMode.Front);
            Quiet(dome.GetComponent<MeshRenderer>(), sky);

            // The floor, with the ring the visitor stands in.
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "ChaoDoMenu";
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.SetParent(room.transform, false);
            floor.transform.localPosition = new Vector3(0f, 0.001f, 0f);
            floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(9f, 9f, 1f);
            Material ground = MakeOpaqueUnlit(Color.white);
            ground.SetTexture("_BaseMap", MenuTexture("MenuChao.png", true));
            Quiet(floor.GetComponent<MeshRenderer>(), ground);

            // Logo and button, set at the visitor's eyes by the menu at runtime.
            Transform layout = new GameObject("LogoEBotao").transform;
            layout.SetParent(room.transform, false);
            layout.localPosition = new Vector3(0f, 1.6f, 0f);

            Texture2D glowTexture = MenuTexture("MenuBrilho.png", true);
            Renderer logoGlow = MenuQuad("BrilhoDaLogo", layout, glowTexture, new Vector3(0f, 0.15f, 2.46f),
                new Vector2(3.2f, 1.5f), new Color(MenuPurple.r, MenuPurple.g, MenuPurple.b, 0.35f), 3000);

            Texture2D logoTexture = MenuTexture("MenuLogo.png", true);
            const float logoWidth = 1.7f;
            float logoHeight = logoTexture != null ? logoWidth * logoTexture.height / logoTexture.width : 0.5f;
            Renderer logo = MenuQuad("Logo", layout, logoTexture, new Vector3(0f, 0.15f, 2.4f),
                new Vector2(logoWidth, logoHeight), Color.white, 3001);

            // Within reach below the eyes, tilted to face them: touched like a real button.
            Vector3 buttonAt = new Vector3(0f, -0.40f, 0.46f);
            Transform button = new GameObject("BotaoIniciar").transform;
            button.SetParent(layout, false);
            button.localPosition = buttonAt;
            button.localRotation = Quaternion.LookRotation(buttonAt.normalized, Vector3.up);

            Renderer buttonGlow = MenuQuad("BrilhoDoBotao", button, glowTexture, new Vector3(0f, 0f, 0.006f),
                new Vector2(0.62f, 0.30f), new Color(MenuPurple.r, MenuPurple.g, MenuPurple.b, 0.4f), 3000);
            Renderer face = MenuQuad("Botao", button, MenuTexture("MenuBotao.png", true), Vector3.zero,
                MenuButtonSize, Color.white, 3001);
            MenuQuad("Rotulo", button, MenuTexture("MenuRotuloIniciar.png", true), new Vector3(0f, 0f, -0.002f),
                new Vector2(0.235f, 0.0588f), Color.white, 3002);

            // The charge, a thin line under the button filling from the left.
            const float barWidth = 0.28f;
            MenuQuad("BarraFundo", button, null, new Vector3(0f, -0.078f, -0.001f), new Vector2(barWidth, 0.007f),
                new Color(1f, 1f, 1f, 0.16f), 3001);
            Transform bar = new GameObject("BarraCarga").transform;
            bar.SetParent(button, false);
            bar.localPosition = new Vector3(-barWidth * 0.5f, -0.078f, -0.0015f);
            MenuQuad("Cheia", bar, null, new Vector3(barWidth * 0.5f, 0f, 0f), new Vector2(barWidth, 0.007f),
                new Color(1f, 1f, 1f, 0.95f), 3002);
            bar.localScale = new Vector3(0.0001f, 1f, 1f);
            bar.gameObject.SetActive(false);

            // The fade: black just in front of the eyes, drawn over everything else.
            GameObject fadeQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fadeQuad.name = "EscurecerVisao";
            Object.DestroyImmediate(fadeQuad.GetComponent<Collider>());
            fadeQuad.transform.SetParent(head.transform, false);
            fadeQuad.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            fadeQuad.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
            Material black = MakeUnlit(new Color(0f, 0f, 0f, 0f));
            black.renderQueue = 4000;
            MeshRenderer fade = fadeQuad.GetComponent<MeshRenderer>();
            Quiet(fade, black);
            fade.enabled = false;

            int layer = EnsureLayer(OperatorLayer);
            SetLayerRecursively(root, layer);
            fadeQuad.layer = layer;

            StartMenu menu = systems.AddComponent<StartMenu>();
            menu.Bind(session, fit, head.transform, Object.FindObjectsByType<ControllerHand>(FindObjectsSortMode.None));
            menu.BindRoom(room, stance, layout, logo.transform, logoGlow);
            menu.BindButton(button, MenuButtonSize, face, buttonGlow, bar);
            menu.BindFade(fade);

            // The menu offers the turn now: the stand no longer starts one by itself after a pause.
            TransplantRoundBridge bridge = systems.GetComponent<TransplantRoundBridge>();
            if (bridge != null) { SetPrivateField(bridge, "autoAdvance", false); }

            room.SetActive(false);
            Debug.Log($"[Transplante] menu inicial em {MenuOrigin}: logo, botão INICIAR e transição para o preto");
        }

        private static Renderer MenuQuad(string name, Transform parent, Texture2D texture, Vector3 localPosition,
            Vector2 size, Color colour, int queue)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPosition;
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);

            Material m = MakeUnlit(colour);
            if (texture != null) { m.SetTexture("_BaseMap", texture); }
            m.renderQueue = queue;

            MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
            Quiet(renderer, m);
            return renderer;
        }

        /// <summary>Unlit, so no light to cast or catch a shadow: the menu room has no lamps.</summary>
        private static void Quiet(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// A menu picture, imported for being seen up close in a headset: clamped at the edges
        /// (no bleeding of the opposite edge into a transparent border), mipmapped, high-quality
        /// compression and alpha that blends cleanly.
        /// </summary>
        private static Texture2D MenuTexture(string file, bool clamp)
        {
            string path = MenuTextureFolder + file;
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                TextureWrapMode wrap = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                if (importer.wrapMode != wrap || !importer.alphaIsTransparency || !importer.mipmapEnabled ||
                    importer.textureCompression != TextureImporterCompression.CompressedHQ || importer.anisoLevel != 4)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.wrapMode = wrap;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = true;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.anisoLevel = 4;
                    importer.SaveAndReimport();
                }
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) { Debug.LogError($"[Transplante] textura do menu ausente: {path}"); }
            return texture;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) { SetLayerRecursively(child.gameObject, layer); }
        }
    }
}
