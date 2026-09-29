using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Ready-made models in place of the generated furniture, when someone has put them in the
    /// project.
    ///
    /// The room is built from code so that it works with nothing downloaded. But a real artist's
    /// anaesthesia machine will always look better than one made of rounded boxes, and the
    /// machine that builds this scene may have no network. So each large piece of furniture is a
    /// slot: drop a model file named after the slot into Assets/Models/Sala (GLB, glTF, FBX or
    /// OBJ) and the next build puts it where the generated one stood, scaled to the same height,
    /// standing on the same floor (or hanging from the same ceiling), and hides the generated one.
    /// No file, no change.
    ///
    /// Models point wherever their author pointed them. A suffix on the file name turns them:
    /// "carrinho_parada@90.glb" is rotated 90 degrees about the vertical. The Console says which
    /// slots were filled and warns about any model too heavy for the Quest.
    /// </summary>
    public static partial class TransplanteSceneBuilder
    {
        private const string SlotFolder = "Assets/Models/Sala";

        /// <summary>Above this many triangles a single prop starts to cost the Quest real frame time.</summary>
        private const int HeavyModelTriangles = 20000;

        private struct ModelSlot
        {
            public string File;
            public string Group;
            public bool Hangs;
            public string[] Keep;

            public ModelSlot(string file, string group, bool hangs = false, params string[] keep)
            {
                File = file;
                Group = group;
                Hangs = hangs;
                Keep = keep;
            }
        }

        /// <summary>
        /// The slots, by file name, and the generated group each one replaces. "Keep" names parts
        /// that are not furniture — the pump lines to the patient, the echo probe — and stay.
        /// </summary>
        private static readonly ModelSlot[] ModelSlots =
        {
            new ModelSlot("maquina_anestesia", "Anestesia"),
            new ModelSlot("maquina_cec", "MaquinaCEC", false, "ArterialLine", "VenousLine"),
            new ModelSlot("carrinho_parada", "CarrinhoDeParada"),
            new ModelSlot("mesa_auxiliar", "MesaAuxiliar"),
            new ModelSlot("suporte_soro", "SuporteSoro"),
            new ModelSlot("armario", "SupplyCabinet"),
            new ModelSlot("aspirador", "Aspirador"),
            new ModelSlot("bisturi_eletrico", "BisturiEletrico"),
            new ModelSlot("banqueta", "Stool"),
            new ModelSlot("eco", "EcoTransesofagico", false, "Sonda"),
            new ModelSlot("termorregulador", "TermorreguladorCEC", false, "Mangueira_Azul", "Mangueira_Vermelha"),
            new ModelSlot("cell_saver", "RecuperadorCelular"),
            new ModelSlot("caixa_termica", "CaixaTermicaOrgao"),
            new ModelSlot("porta_compressas", "PortaCompressas"),
            new ModelSlot("pia_escovacao", "ScrubSink"),
            new ModelSlot("foco_cirurgico", "SurgicalLights", true),
        };

        private static readonly string[] SlotExtensions = { ".glb", ".gltf", ".fbx", ".obj" };

        /// <summary>Fills every slot that has a model in the folder. Called once the room is built.</summary>
        private static void ApplyModelSlots(Transform room)
        {
            EnsureAssetFolder(SlotFolder);
            List<string> filled = new List<string>();

            foreach (ModelSlot slot in ModelSlots)
            {
                string path = FindSlotModel(slot.File, out float yaw);
                if (path == null) { continue; }

                Transform group = FindChildDeep(room, slot.Group);
                if (group == null)
                {
                    Debug.LogWarning($"[Transplante] modelo '{path}' encontrado, mas a sala não tem '{slot.Group}' para substituir.");
                    continue;
                }

                if (FillSlot(slot, group, path, yaw)) { filled.Add(slot.File); }
            }

            Debug.Log(filled.Count == 0
                ? $"[Transplante] nenhum modelo pronto em {SlotFolder}; sala toda gerada. Encaixes disponíveis: " +
                  string.Join(", ", System.Array.ConvertAll(ModelSlots, s => s.File))
                : $"[Transplante] modelos prontos usados: {string.Join(", ", filled)}");
        }

        private static bool FillSlot(ModelSlot slot, Transform group, string path, float yaw)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                Debug.LogWarning($"[Transplante] '{path}' não importou como modelo; o encaixe '{slot.File}' ficou gerado.");
                return false;
            }

            if (!BoundsOf(group, slot.Keep, null, out Bounds target))
            {
                Debug.LogWarning($"[Transplante] '{slot.Group}' não tem nada visível para medir; '{slot.File}' ignorado.");
                return false;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (model == null) { model = Object.Instantiate(asset); }
            model.name = "Modelo_" + slot.File;
            model.transform.SetParent(group, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.transform.localScale = Vector3.one;

            if (!BoundsOf(model.transform, null, null, out Bounds made) || made.size.y < 1e-4f)
            {
                Object.DestroyImmediate(model);
                Debug.LogWarning($"[Transplante] '{path}' não tem malha visível; '{slot.File}' ignorado.");
                return false;
            }

            // Same height as what it replaces, but never a footprint much wider than the space
            // left for it: a model exported flat would otherwise be scaled up into the table.
            float scale = target.size.y / made.size.y;
            float room = Mathf.Max(target.size.x, target.size.z);
            float wide = Mathf.Max(made.size.x, made.size.z);
            if (wide > 1e-4f) { scale = Mathf.Min(scale, room * 1.3f / wide); }
            model.transform.localScale = Vector3.one * scale;

            BoundsOf(model.transform, null, null, out made);
            Vector3 want = slot.Hangs
                ? new Vector3(target.center.x, target.max.y, target.center.z)
                : new Vector3(target.center.x, target.min.y, target.center.z);
            Vector3 have = slot.Hangs
                ? new Vector3(made.center.x, made.max.y, made.center.z)
                : new Vector3(made.center.x, made.min.y, made.center.z);
            model.transform.position += want - have;

            // The generated piece goes, except the parts that were never furniture. Lights,
            // sounds and scripts on the group stay: only what is drawn is swapped.
            foreach (Renderer renderer in group.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.transform.IsChildOf(model.transform) || IsKept(renderer.transform, group, slot.Keep)) { continue; }
                renderer.enabled = false;
            }

            int triangles = 0;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) { continue; }
                for (int i = 0; i < filter.sharedMesh.subMeshCount; i++) { triangles += (int)filter.sharedMesh.GetIndexCount(i) / 3; }
            }

            if (triangles > HeavyModelTriangles)
            {
                Debug.LogWarning($"[Transplante] '{slot.File}' tem {triangles} triângulos: pesado para o Quest " +
                                 $"(ideal abaixo de {HeavyModelTriangles}). Reduza no Blender (Decimate) se faltar quadro.");
            }

            Debug.Log($"[Transplante] encaixe '{slot.File}': {Path.GetFileName(path)} em '{slot.Group}', " +
                      $"escala {scale:F3}, giro {yaw:F0}°, {triangles} triângulos");
            return true;
        }

        /// <summary>The slot's model file, if any: "name.ext" or "name@yaw.ext".</summary>
        private static string FindSlotModel(string slot, out float yaw)
        {
            yaw = 0f;
            if (!Directory.Exists(SlotFolder)) { return null; }

            foreach (string file in Directory.GetFiles(SlotFolder))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (System.Array.IndexOf(SlotExtensions, extension) < 0) { continue; }

                string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                string wanted = slot.ToLowerInvariant();
                if (name == wanted)
                {
                    return file.Replace('\\', '/');
                }

                if (name.StartsWith(wanted + "@") && float.TryParse(name.Substring(wanted.Length + 1),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float turn))
                {
                    yaw = turn;
                    return file.Replace('\\', '/');
                }
            }

            return null;
        }

        private static bool IsKept(Transform part, Transform group, string[] keep)
        {
            if (keep == null || keep.Length == 0) { return false; }
            for (Transform t = part; t != null && t != group; t = t.parent)
            {
                if (System.Array.IndexOf(keep, t.name) >= 0) { return true; }
            }

            return false;
        }

        /// <summary>World bounds of everything drawn under a transform, leaving out the kept parts.</summary>
        private static bool BoundsOf(Transform root, string[] keep, Transform skip, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || renderer is LineRenderer) { continue; }
                if (skip != null && renderer.transform.IsChildOf(skip)) { continue; }
                if (IsKept(renderer.transform, root, keep)) { continue; }
                if (renderer.GetComponent<TextMesh>() != null) { continue; }

                if (any) { bounds.Encapsulate(renderer.bounds); }
                else { bounds = renderer.bounds; any = true; }
            }

            return any;
        }

        private static Transform FindChildDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) { return t; }
            }

            return null;
        }
    }
}
