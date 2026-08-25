using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRSurgery.Tissue;
using VRSurgery.Tools;
using VRSurgery.VR;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Checks the built scene against the concrete deliverables in the project's VR development
    /// guide, so "does it have everything" is answered by the scene itself rather than by memory.
    /// Each check names the guide item it comes from.
    /// </summary>
    public static class GuideComplianceCheck
    {
        private const string ScenePath = "Assets/Scenes/SurgeryMVP.unity";

        private static readonly List<string> Results = new List<string>();
        private static int _failed;

        [MenuItem("VRSurgery/Check Guide Compliance")]
        public static void CheckMenu() => Check();

        public static void CheckFromCommandLine()
        {
            Check();
            EditorApplication.Exit(_failed > 0 ? 1 : 0);
        }

        private static T GetPrivate<T>(object target, string fieldName) where T : Object
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(target) as T;
        }

        private static void Require(string item, bool ok, string detail)
        {
            if (!ok) { _failed++; }
            Results.Add($"@@G | {(ok ? "OK  " : "FALTA")} | {item} | {detail}");
        }

        public static void Check()
        {
            Results.Clear();
            _failed = 0;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // --- 1. Setup -------------------------------------------------
            Require("XR Origin na cena", GameObject.Find("XR Origin") != null, "rig do jogador");

            // --- 3. Hierarquia + bisturi pegavel --------------------------
            GameObject scalpel = GameObject.Find("Scalpel");
            Require("GameObject Scalpel", scalpel != null, scalpel != null ? "presente" : "ausente");

            bool tagExists = false;
            foreach (string t in UnityEditorInternal.InternalEditorUtility.tags)
            {
                if (t == "Scalpel") { tagExists = true; break; }
            }
            Require("Tag \"Scalpel\" criada", tagExists, tagExists ? "no TagManager" : "nao existe");

            if (scalpel != null)
            {
                BoxCollider box = scalpel.GetComponent<BoxCollider>();
                Require("Box Collider no bisturi", box != null,
                    box != null ? $"size={box.size}" : "ausente");
                Require("Box Collider com Is Trigger = FALSE", box != null && !box.isTrigger,
                    box != null ? $"isTrigger={box.isTrigger}" : "sem collider");

                Rigidbody rb = scalpel.GetComponent<Rigidbody>();
                Require("Rigidbody no bisturi", rb != null, rb != null ? $"massa={rb.mass} kg" : "ausente");
                Require("Is Kinematic = TRUE", rb != null && rb.isKinematic,
                    rb != null ? $"isKinematic={rb.isKinematic}" : "sem rigidbody");
                Require("Use Gravity = FALSE", rb != null && !rb.useGravity,
                    rb != null ? $"useGravity={rb.useGravity}" : "sem rigidbody");

                XRGrabInteractable grab = scalpel.GetComponent<XRGrabInteractable>();
                Require("XR Grab Interactable", grab != null, grab != null ? "presente" : "ausente");
                Require("Movement Type = Velocity Tracking",
                    grab != null && grab.movementType == XRBaseInteractable.MovementType.VelocityTracking,
                    grab != null ? grab.movementType.ToString() : "sem grab");
                Require("Throw On Detach = false", grab != null && !grab.throwOnDetach,
                    grab != null ? $"throwOnDetach={grab.throwOnDetach}" : "sem grab");

                // Tag: the guide tags the scalpel; here the blade tip carries it so contact
                // means the blade is really at the skin.
                bool tagged = scalpel.CompareTag("Scalpel");
                foreach (Transform t in scalpel.GetComponentsInChildren<Transform>())
                {
                    if (t.CompareTag("Scalpel")) { tagged = true; break; }
                }
                Require("Tag aplicada no bisturi", tagged, tagged ? "na BladeTip" : "nao aplicada");
            }

            // --- Orgao ----------------------------------------------------
            GameObject region = GameObject.Find("SurgicalRegion");
            Require("Orgao/regiao com script de corte", region != null,
                region != null ? "SurgicalRegion" : "ausente");

            if (region != null)
            {
                Collider organ = region.GetComponent<Collider>();
                Require("Collider do orgao com Is Trigger = TRUE", organ != null && organ.isTrigger,
                    organ != null ? $"{organ.GetType().Name} isTrigger={organ.isTrigger}" : "ausente");
                Require("Nao usar Mesh Collider no orgao", !(organ is MeshCollider),
                    organ != null ? organ.GetType().Name : "ausente");

                IncisableSkin skin = region.GetComponent<IncisableSkin>();
                Require("Script de corte por colisao", skin != null,
                    skin != null ? "IncisableSkin" : "ausente");
                Require("Log em arquivo", skin != null, skin != null ? skin.LogFilePath : "sem script");

                AudioSource audio = region.GetComponent<AudioSource>();
                Require("AudioSource na regiao", audio != null,
                    audio != null ? $"spatialBlend={audio.spatialBlend}" : "ausente");

                // An AudioSource with no clip passes a naive presence check and plays nothing.
                AudioClip clip = skin != null ? GetPrivate<AudioClip>(skin, "incisionClip") : null;
                Require("AudioClip do corte atribuido", clip != null,
                    clip != null ? $"{clip.name} ({clip.length:F2}s)" : "NULO - o corte seria silencioso");

                GameObject feedback = skin != null ? GetPrivate<GameObject>(skin, "incisionFeedback") : null;
                Require("GameObject de feedback atribuido", feedback != null,
                    feedback != null ? feedback.name : "NULO - nenhum highlight ativaria");

                bool insideTable = region.GetComponentInParent<Transform>() != null &&
                                   region.transform.root.name == "OperatingTable";
                Require("Orgao dentro de OperatingTable", insideTable,
                    "raiz=" + region.transform.root.name);
            }

            // --- 4. Segunda camera ----------------------------------------
            Camera projection = null;
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (c.name == "ProjectionCamera") { projection = c; break; }
            }

            Require("Segunda camera (projecao)", projection != null,
                projection != null ? "ProjectionCamera" : "ausente");
            Require("Target Display = Display 2", projection != null && projection.targetDisplay == 1,
                projection != null ? $"Display {projection.targetDisplay + 1}" : "sem camera");
            Require("Clear Flags = Solid Color", projection != null &&
                projection.clearFlags == CameraClearFlags.SolidColor,
                projection != null ? projection.clearFlags.ToString() : "sem camera");
            Require("Fundo preto", projection != null && projection.backgroundColor.r < 0.02f &&
                projection.backgroundColor.g < 0.02f && projection.backgroundColor.b < 0.02f,
                projection != null ? projection.backgroundColor.ToString() : "sem camera");
            Require("Projection = Orthographic", projection != null && projection.orthographic,
                projection != null
                    ? (projection.orthographic ? $"ortho, size {projection.orthographicSize:F3}" : "Perspective")
                    : "sem camera");
            // This check exists because a naive "is a culling mask set" reading passed while the
            // projector was showing both controllers, every affordance tooltip and the teleport
            // marker. It now asserts against what the camera actually renders.
            int operatorLayer = LayerMask.NameToLayer("OperatorOnly");
            Require("Layer do operador existe", operatorLayer >= 0,
                operatorLayer >= 0 ? $"OperatorOnly (indice {operatorLayer})" : "nao criada");
            Require("Culling Mask exclui o operador",
                projection != null && operatorLayer >= 0 &&
                (projection.cullingMask & (1 << operatorLayer)) == 0,
                projection != null ? $"mask 0x{projection.cullingMask:X}" : "sem camera");

            if (projection != null)
            {
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(projection);
                List<string> leaks = new List<string>();

                foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) { continue; }
                    if ((projection.cullingMask & (1 << r.gameObject.layer)) == 0) { continue; }
                    if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) { continue; }

                    Transform root = r.transform.root;
                    if (root.name == "XR Origin" || root.name == "Teleport Area Setup")
                    {
                        leaks.Add(r.name);
                    }
                }

                foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (!c.isActiveAndEnabled || c.renderMode != RenderMode.WorldSpace) { continue; }
                    if ((projection.cullingMask & (1 << c.gameObject.layer)) == 0) { continue; }

                    Transform root = c.transform.root;
                    if (root.name == "XR Origin" || root.name == "Teleport Area Setup")
                    {
                        leaks.Add("[canvas] " + c.name);
                    }
                }

                Require("Nada do operador no quadro projetado", leaks.Count == 0,
                    leaks.Count == 0 ? "limpo" : $"{leaks.Count} vazando: {string.Join(", ", leaks.GetRange(0, Mathf.Min(3, leaks.Count)))}");
            }
            Require("Ativacao do display em runtime",
                projection != null && projection.GetComponent<ProjectionDisplay>() != null,
                projection != null ? "ProjectionDisplay" : "sem camera");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[GuideCheck] BEGIN");
            foreach (string r in Results) { sb.AppendLine(r); }
            sb.AppendLine($"@@G | RESUMO | {Results.Count - _failed}/{Results.Count} itens conformes");
            sb.AppendLine("[GuideCheck] END");
            Debug.Log(sb.ToString());
        }
    }
}
