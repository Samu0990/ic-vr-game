using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRSurgery.Feedback;
using VRSurgery.Interaction;
using VRSurgery.Session;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// O que um leigo precisa ver: a pele do corpo igual à da janela, sem emenda; o coração doente
    /// maior, mais redondo e apagado; o do doador vermelho vivo; as bacias com nome; e uma frase
    /// na primeira vez que a pessoa pega cada coração, vinda do arquivo de textos.
    /// </summary>
    public class HeartsAndSkinLookTests
    {
        private const string SceneName = "TransplanteCardiaco";

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null) { Object.DestroyImmediate(go); }
            }

            _spawned.Clear();
            SurgeryEvents.ResetAll();
        }

        private static GameObject FindIncludingInactive(string name)
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t.name == name) { return t.gameObject; }
            }

            return null;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) { b.Encapsulate(r.bounds); }
            return b;
        }

        private static float MeanBrightness(GameObject go, out float redness)
        {
            float sum = 0f, red = 0f;
            int n = 0;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || !m.HasProperty("_BaseColor")) { continue; }
                    Color c = m.GetColor("_BaseColor");
                    sum += (c.r + c.g + c.b) / 3f;
                    red += c.r - (c.g + c.b) * 0.5f;
                    n++;
                }
            }

            redness = n > 0 ? red / n : 0f;
            return n > 0 ? sum / n : 0f;
        }

        [UnityTest]
        public IEnumerator APeleDoCorpo_EAMesmaDaJanela_SemEmenda()
        {
            yield return HeadlessScene.Load(SceneName);

            Material window = FindIncludingInactive("Pele_E").GetComponent<Renderer>().sharedMaterial;
            Assert.IsNotNull(window);
            foreach (Renderer r in FindIncludingInactive("Body_Skin").GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material m in r.sharedMaterials)
                {
                    Assert.AreSame(window, m, $"'{r.name}' usa '{m?.name}', não o material da janela: a borda aparece como emenda.");
                }
            }
        }

        [UnityTest]
        public IEnumerator OCoracaoDoente_EMaiorEApagado_ODoadorVermelhoVivo_EOGradilAindaOContem()
        {
            yield return HeadlessScene.Load(SceneName);

            GameObject sick = FindIncludingInactive("HeartModel");
            GameObject donor = FindIncludingInactive("DonorHeartModel");
            Assert.IsNotNull(sick);
            Assert.IsNotNull(donor);

            // By scale, not by world bounds: the two hearts lie at different angles, and an
            // axis-aligned box around a turned heart grows with the turn, not with the heart.
            Vector3 sickScale = sick.transform.lossyScale;
            Vector3 donorScale = donor.transform.lossyScale;
            float sickVolume = Mathf.Abs(sickScale.x * sickScale.y * sickScale.z);
            float donorVolume = Mathf.Abs(donorScale.x * donorScale.y * donorScale.z);
            Assert.Greater(sickVolume, donorVolume * 1.35f, "Dilatado: o doente tem de ser visivelmente maior que o saudável.");

            float sickLight = MeanBrightness(sick, out float sickRed);
            float donorLight = MeanBrightness(donor, out float donorRed);
            Assert.Less(sickLight, donorLight * 0.85f, "Apagado e escuro, ao lado de um coração saudável.");
            Assert.Greater(donorRed, sickRed, "O do doador é o vermelho vivo.");

            Bounds cage = RendererBounds(GameObject.Find("Ribcage"));
            Bounds heart = RendererBounds(sick);
            Assert.IsTrue(cage.Contains(heart.min) && cage.Contains(heart.max),
                $"O coração dilatado ainda cabe no gradil (gradil {cage.size * 100f} cm, coração {heart.size * 100f} cm).");
        }

        [UnityTest]
        public IEnumerator AsBaciasTemNome()
        {
            yield return HeadlessScene.Load(SceneName);

            List<string> labels = new List<string>();
            foreach (TextMesh text in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                labels.Add(text.text);
            }

            CollectionAssert.Contains(labels, StandTexts.Get("rotulo_bacia_doador", "DOADOR"));
            CollectionAssert.Contains(labels, StandTexts.Get("rotulo_cuba_retirada", "RETIRADO"));
        }

        [Test]
        public void OsTextosVemDeUmArquivoSo_ParaOOrientadorRevisar()
        {
            StandTexts.Load();
            Assert.AreEqual("Coração doente\ndilatado e fraco, não bombeia direito",
                StandTexts.Get(OrganInfoPopups.SickKey, "reserva"));
            Assert.AreEqual("Coração do doador\nsaudável", StandTexts.Get(OrganInfoPopups.DonorKey, "reserva"));
            Assert.AreEqual("DOADOR", StandTexts.Get("rotulo_bacia_doador", "reserva"));
            Assert.AreEqual("RETIRADO", StandTexts.Get("rotulo_cuba_retirada", "reserva"));
            Assert.AreEqual("reserva", StandTexts.Get("chave_que_nao_existe", "reserva"),
                "Chave fora do arquivo não deixa o pop-up em branco.");
        }

        [Test]
        public void NaPrimeiraVezQuePegaCadaCoracao_UmaFrase_EVisitanteNovoVeDeNovo()
        {
            GameObject sickObject = new GameObject("Heart");
            _spawned.Add(sickObject);
            SurgicalInteractable sickHandle = sickObject.AddComponent<SurgicalInteractable>();
            GrabbableOrgan sick = sickObject.AddComponent<GrabbableOrgan>();
            sick.Bind(OrganRole.Native, null, null);

            GameObject donorObject = new GameObject("DonorHeart");
            _spawned.Add(donorObject);
            SurgicalInteractable donorHandle = donorObject.AddComponent<SurgicalInteractable>();
            GrabbableOrgan donor = donorObject.AddComponent<GrabbableOrgan>();
            donor.Bind(OrganRole.Donor, null, null);

            GameObject host = new GameObject("Info");
            _spawned.Add(host);
            OrganInfoPopups popups = host.AddComponent<OrganInfoPopups>();
            popups.Bind(new[] { sick, donor }, null, -1);

            sickHandle.OnGrabbed(null);
            StringAssert.Contains("dilatado e fraco", popups.Showing);
            sickHandle.OnReleased();

            popups.Tick(5f);
            Assert.IsEmpty(popups.Showing, "Curto: some em alguns segundos.");

            sickHandle.OnGrabbed(null);
            Assert.IsEmpty(popups.Showing, "Só na primeira vez de cada coração.");
            sickHandle.OnReleased();

            donorHandle.OnGrabbed(null);
            StringAssert.Contains("saudável", popups.Showing);
            donorHandle.OnReleased();
            popups.Tick(5f);

            popups.ResetForVisitor();
            sickHandle.OnGrabbed(null);
            StringAssert.Contains("dilatado e fraco", popups.Showing, "Visitante novo vê de novo.");
        }
    }
}
