using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Skyborne.Flight;
using Skyborne.Grab;
using Skyborne.NPC;
using Skyborne.Rig;
using Skyborne.UI;

namespace Skyborne.EditorTools
{
    /// <summary>
    /// Builds the whole sandbox from a menu item, so the scene is reproducible from code and
    /// nothing has to be wired by hand in the Inspector.
    /// </summary>
    public static class SkybornePlaygroundBuilder
    {
        private const string SceneFolder = "Assets/_Skyborne/Scenes";
        private const string MaterialFolder = "Assets/_Skyborne/Materials";
        private const string ScenePath = SceneFolder + "/Skyborne_Playground.unity";

        [MenuItem("Skyborne/Construir cena de teste", false, 0)]
        public static void Build()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material concrete = CreateMaterial("SKY_Concrete", new Color(0.42f, 0.43f, 0.45f), 0.75f);
            Material ground = CreateMaterial("SKY_Ground", new Color(0.24f, 0.27f, 0.24f), 0.9f);
            Material body = CreateMaterial("SKY_Body", new Color(0.78f, 0.58f, 0.47f), 0.55f);

            CreateLighting();
            CreateGround(ground);
            CreateCity(concrete);

            FlyerController flyer = CreateFlyer();
            CreateVictims(body);

            Selection.activeGameObject = flyer.gameObject;

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Skyborne] Cena criada em {ScenePath}. Aperte Play e use W + mouse.");
        }

        private static void CreateLighting()
        {
            GameObject sun = new GameObject("Sun");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            light.color = new Color(1f, 0.96f, 0.89f);
            sun.transform.rotation = Quaternion.Euler(48f, 35f, 0f);
        }

        private static void CreateGround(Material material)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -2f, 0f);
            ground.transform.localScale = new Vector3(600f, 4f, 600f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            ground.isStatic = true;
        }

        /// <summary>
        /// A grid of towers: something to weave between at speed, and hard surfaces to find out
        /// what happens when you slam a carried body into one.
        /// </summary>
        private static void CreateCity(Material material)
        {
            GameObject city = new GameObject("City");
            Random.InitState(20260929);

            for (int x = -3; x <= 3; x++)
            {
                for (int z = -3; z <= 3; z++)
                {
                    // Leave the middle open as a landing/launch area.
                    if (Mathf.Abs(x) <= 1 && Mathf.Abs(z) <= 1)
                    {
                        continue;
                    }

                    float height = Random.Range(18f, 95f);
                    float width = Random.Range(12f, 24f);
                    float depth = Random.Range(12f, 24f);

                    GameObject tower = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tower.name = $"Tower_{x}_{z}";
                    tower.transform.SetParent(city.transform, false);
                    tower.transform.position = new Vector3(
                        x * 46f + Random.Range(-6f, 6f),
                        height * 0.5f,
                        z * 46f + Random.Range(-6f, 6f));
                    tower.transform.localScale = new Vector3(width, height, depth);
                    tower.GetComponent<MeshRenderer>().sharedMaterial = material;
                    tower.isStatic = true;
                }
            }
        }

        private static FlyerController CreateFlyer()
        {
            GameObject flyerObject = new GameObject("Flyer");
            flyerObject.transform.position = new Vector3(0f, 32f, -40f);

            Rigidbody rigidbody = flyerObject.AddComponent<Rigidbody>();
            rigidbody.mass = 95f;

            CapsuleCollider collider = flyerObject.AddComponent<CapsuleCollider>();
            collider.height = 1.85f;
            collider.radius = 0.34f;
            collider.center = new Vector3(0f, 0.1f, 0f);

            FlyerController flyer = flyerObject.AddComponent<FlyerController>();
            GrabController grab = flyerObject.AddComponent<GrabController>();

            GameObject hand = new GameObject("HandAnchor");
            hand.transform.SetParent(flyerObject.transform, false);
            hand.transform.localPosition = new Vector3(0.32f, 0.2f, 0.7f);

            GameObject cameraObject = new GameObject("Eye");
            cameraObject.transform.SetParent(flyerObject.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0.72f, 0.1f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 2500f;
            cameraObject.AddComponent<AudioListener>();

            FlightCameraRig rig = cameraObject.AddComponent<FlightCameraRig>();
            SkyborneHud hud = cameraObject.AddComponent<SkyborneHud>();

            // Wire the private serialized references the same way the Inspector would.
            SetReference(flyer, "handAnchor", hand.transform);
            SetReference(rig, "flyer", flyer);
            SetReference(grab, "aimCamera", camera);
            SetReference(hud, "flyer", flyer);
            SetReference(hud, "grab", grab);

            return flyer;
        }

        private static void CreateVictims(Material material)
        {
            GameObject group = new GameObject("Victims");

            // On the ground, in the open middle of the map.
            Vector3[] groundSpots =
            {
                new Vector3(-6f, 0f, -6f),
                new Vector3(4f, 0f, -10f),
                new Vector3(10f, 0f, 2f),
                new Vector3(-12f, 0f, 6f),
            };

            for (int i = 0; i < groundSpots.Length; i++)
            {
                GameObject victim = HumanoidRagdollFactory.Create(
                    $"Victim_Ground_{i}",
                    groundSpots[i],
                    Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
                    Random.Range(1.66f, 1.86f),
                    Random.Range(62f, 88f),
                    material);
                victim.transform.SetParent(group.transform, true);
            }

            // One on a rooftop, so there is something to dive for.
            GameObject rooftop = HumanoidRagdollFactory.Create(
                "Victim_Rooftop",
                new Vector3(92f, 46f, 92f),
                Quaternion.Euler(0f, 210f, 0f),
                1.78f,
                76f,
                material);
            rooftop.transform.SetParent(group.transform, true);
        }

        private static Material CreateMaterial(string name, Color color, float smoothnessInverse)
        {
            Directory.CreateDirectory(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader);
            material.color = color;

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 1f - smoothnessInverse);
            }
            else if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 1f - smoothnessInverse);
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Assigns a private [SerializeField] reference through SerializedObject, which is what
        /// the Inspector does, so the value is serialised into the scene properly.
        /// </summary>
        private static void SetReference(Object target, string field, Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);

            if (property == null)
            {
                Debug.LogWarning($"[Skyborne] Campo '{field}' nao encontrado em {target.GetType().Name}.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
