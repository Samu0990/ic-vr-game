using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRSurgery.Interaction;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>
    /// The join between the rig's hands and the project's grab contract.
    ///
    /// This is the test that should have existed already. The scene shipped with no
    /// XRHandInteractor on it at all — the adapter that turns XRI's selectEntered/selectExited
    /// into SurgicalInteractable.OnGrabbed/OnReleased — and nothing caught it, because every
    /// other test either drives the procedure's stages directly or checks layout. The result was
    /// a transplant that could not be completed with hands: IsHeld never became true, so the
    /// native heart's Released event never fired and RemoveNativeHeart was a dead end, while the
    /// donor heart counted as implanted the moment it drifted near the seat, still in the fist
    /// holding it.
    ///
    /// So these drive the real scene through XRI's own events rather than calling the contract
    /// directly. Calling TryGrab would pass with the adapter deleted, which is precisely the
    /// failure being guarded against.
    /// </summary>
    public class HandGrabWiringTests
    {
        private const string SceneName = "TransplanteCardiaco";

        private TransplantProcedure _procedure;
        private GameObject _nativeHeart;
        private GameObject _donorHeart;
        private Transform _seat;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return HeadlessScene.Load(SceneName);

            _procedure = Object.FindFirstObjectByType<TransplantProcedure>();
            Assert.IsNotNull(_procedure, "Sem TransplantProcedure na cena.");

            // Inativos incluídos de propósito: o SternotomyController desliga o coração nativo no
            // Awake, porque ele só aparece quando o tórax abre. GameObject.Find não enxerga
            // objeto inativo, e procurar por nome aqui faria o teste falhar pelo motivo errado.
            foreach (GrabbableOrgan organ in
                     Object.FindObjectsByType<GrabbableOrgan>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (organ.name == "Heart") { _nativeHeart = organ.gameObject; }
                if (organ.name == "DonorHeart") { _donorHeart = organ.gameObject; }
            }

            _seat = GameObject.Find("PericardialSeat").transform;

            Assert.IsNotNull(_nativeHeart, "Sem coração nativo na cena.");
            Assert.IsNotNull(_donorHeart, "Sem coração doador na cena.");

            yield return null;
        }

        [TearDown]
        public void TearDown() => SurgeryEvents.ResetAll();

        /// <summary>
        /// The rig's grab interactors, which is where the adapter has to live: it requires an
        /// XRBaseInteractor on its own GameObject.
        ///
        /// Inativos incluídos, e não por comodidade: o XRInputModalityManager do rig desliga os
        /// controles quando não detecta dispositivo XR, que é sempre o caso numa suíte headless.
        /// Procurar só entre ativos devolve zero e acusa a cena de não ter o componente que ela
        /// tem — foi exatamente esse o falso negativo que este teste deu na primeira execução.
        /// </summary>
        private static XRHandInteractor[] Hands() =>
            Object.FindObjectsByType<XRHandInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        /// <summary>
        /// Pula o teste quando não há mão viva para agarrar com.
        ///
        /// O adaptador assina selectEntered/selectExited no OnEnable, e o rig desliga os controles
        /// quando não detecta dispositivo XR — que é sempre o caso numa suíte headless. Levantar
        /// o evento nesse estado não chega a ninguém, e o teste falharia dizendo que o agarre está
        /// quebrado quando o que falta é o óculos.
        ///
        /// Pular declarando o motivo é melhor que falhar por um motivo errado, e melhor que
        /// passar sem ter verificado nada. O que dá para verificar sem hardware — que o adaptador
        /// está na cena e que as regras de explante e implante funcionam — está coberto pelos
        /// outros testes deste arquivo e pelo TransplantRoundTests.
        ///
        /// NÃO tente ligar os controles à força: já foi tentado, e ativar o Near-Far Interactor
        /// sem dispositivo presente TRAVA a suíte inteira em vez de falhar.
        /// </summary>
        private static void RequireTrackedHands()
        {
            foreach (XRHandInteractor hand in Hands())
            {
                if (hand.isActiveAndEnabled) { return; }
            }

            Assert.Ignore("Sem dispositivo XR: o rig desliga os controles e o adaptador não " +
                          "chega a assinar os eventos. Este comportamento só se verifica no óculos.");
        }

        // NOTA: houve aqui um PowerUpHands() que ligava os GameObjects dos controles para que o
        // OnEnable do adaptador assinasse os eventos da XRI. Ele TRAVA a suíte inteira: ativar o
        // Near-Far Interactor sem dispositivo XR presente deixa a inicialização da toolkit
        // esperando indefinidamente, e o test runner para em
        // BothHandsCarryTheProjectsGrabContract sem nunca terminar.
        //
        // Removido porque uma suíte que trava é pior que uma que falha: a que falha ainda diz o
        // que está errado e deixa o resto rodar. Consequência assumida — os três testes de
        // comportamento abaixo falham em ambiente headless, porque os eventos são levantados e
        // não há ninguém inscrito. O que continua verificado de verdade é
        // BothHandsCarryTheProjectsGrabContract, que prova que o adaptador está na cena.
        //
        // Verificar o comportamento de agarre exige dispositivo XR presente, o que significa
        // rodar no óculos. Isso está registrado no relatório como não verificado.

        /// <summary>
        /// Raises XRI's own select event on the interactor, exactly as the toolkit does when a
        /// real hand closes on something. Going through the event is the whole point — it is the
        /// wiring under test, not the contract underneath it.
        /// </summary>
        private static void RaiseSelectEntered(XRHandInteractor hand, XRGrabInteractable target)
        {
            XRBaseInteractor interactor = hand.GetComponent<XRBaseInteractor>();
            interactor.selectEntered.Invoke(new SelectEnterEventArgs
            {
                interactorObject = interactor as IXRSelectInteractor,
                interactableObject = target,
            });
        }

        private static void RaiseSelectExited(XRHandInteractor hand, XRGrabInteractable target)
        {
            XRBaseInteractor interactor = hand.GetComponent<XRBaseInteractor>();
            interactor.selectExited.Invoke(new SelectExitEventArgs
            {
                interactorObject = interactor as IXRSelectInteractor,
                interactableObject = target,
            });
        }

        /// <summary>Walks the operation to the stage where the native heart may come out.</summary>
        /// <summary>
        /// Leva a operação até a cardiectomia, seja qual for a ordem de etapas configurada.
        ///
        /// Avança pela etapa que o procedimento diz estar em curso, em vez de repetir uma lista
        /// escrita à mão. A versão anterior assumia OpenChest → GoOnBypass e quebrou quando as
        /// etapas de pele (SkinIncision, CloseSkin) e o pericárdio entraram: o teste acusava o
        /// produto por uma mudança que era dele mesmo. Um teste de ligação não deve ter opinião
        /// sobre quantas etapas existem antes da que ele testa.
        /// </summary>
        private void AdvanceToExplant()
        {
            _procedure.Begin();

            // O tórax se abre no caminho, e é a abertura que expõe o coração. Sem isto ele segue
            // desativado e nenhuma mão o alcançaria — no jogo nem no teste.
            _nativeHeart.SetActive(true);

            for (int guard = 0; guard < 16; guard++)
            {
                if (_procedure.Stage == TransplantStage.RemoveNativeHeart) { return; }

                if (_procedure.Stage == TransplantStage.GoOnBypass)
                {
                    _procedure.Bypass.Attempt(BypassStep.Cannulate);
                    _procedure.Bypass.Attempt(BypassStep.ClampAorta);
                    _procedure.Bypass.Attempt(BypassStep.Cardioplegia);
                }

                if (!_procedure.CompleteStage(_procedure.Stage))
                {
                    Assert.Fail($"A operação travou em {_procedure.Stage} a caminho da cardiectomia.");
                }
            }

            Assert.AreEqual(TransplantStage.RemoveNativeHeart, _procedure.Stage,
                "Não foi possível chegar à retirada do coração em 16 etapas.");
        }

        [UnityTest]
        public IEnumerator BothHandsCarryTheProjectsGrabContract()
        {
            XRHandInteractor[] hands = Hands();

            Assert.AreEqual(2, hands.Length,
                "A cena precisa de um XRHandInteractor por mão. Sem ele nada fica 'segurado', " +
                "e o meio da cirurgia trava sem erro nenhum no console.");

            bool left = false, right = false;
            foreach (XRHandInteractor hand in hands)
            {
                Assert.IsNotNull(hand.GetComponent<XRBaseInteractor>(),
                    $"'{hand.name}' não tem interactor da XRI — o adaptador não recebe evento algum.");

                if (hand.IsLeftHand) { left = true; } else { right = true; }
            }

            Assert.IsTrue(left && right,
                "Uma mão está marcada como a outra. Canhotos são metade da fila.");
            yield break;
        }

        [UnityTest]
        public IEnumerator GrabbingAnOrganMarksItHeld()
        {
            RequireTrackedHands();

            // Exposto, como estaria com o tórax aberto: o adaptador resolve o interactable subindo
            // a hierarquia, e GetComponentInParent ignora objeto inativo.
            _nativeHeart.SetActive(true);

            XRHandInteractor hand = Hands()[0];
            SurgicalInteractable organ = _nativeHeart.GetComponent<SurgicalInteractable>();

            Assert.IsFalse(organ.IsHeld, "Nada deveria estar na mão antes de agarrar.");

            RaiseSelectEntered(hand, _nativeHeart.GetComponent<XRGrabInteractable>());
            yield return null;

            Assert.IsTrue(organ.IsHeld,
                "Agarrar pela XRI tem que marcar o objeto como segurado no contrato do projeto.");
            Assert.AreSame(hand, organ.CurrentHolder);

            RaiseSelectExited(hand, _nativeHeart.GetComponent<XRGrabInteractable>());
            yield return null;

            Assert.IsFalse(organ.IsHeld, "Soltar tem que desfazer isso.");
        }

        [UnityTest]
        public IEnumerator ReleasingTheNativeHeartAwayFromTheChestCompletesTheExplant()
        {
            RequireTrackedHands();

            AdvanceToExplant();

            XRHandInteractor hand = Hands()[0];
            XRGrabInteractable grab = _nativeHeart.GetComponent<XRGrabInteractable>();

            RaiseSelectEntered(hand, grab);
            yield return null;

            // Carried well clear of the pericardium — further than the explant threshold.
            _nativeHeart.transform.position = _seat.position + new Vector3(0.6f, 0.1f, 0f);
            yield return null;

            Assert.AreEqual(TransplantStage.RemoveNativeHeart, _procedure.Stage,
                "Carregar o coração não é explantar; ele ainda está na mão do cirurgião.");

            RaiseSelectExited(hand, grab);
            yield return null;

            Assert.IsTrue(_nativeHeart.GetComponent<GrabbableOrgan>().Satisfied,
                "O coração doente saiu do tórax e foi pousado: isso é a cardiectomia.");
            Assert.AreEqual(TransplantStage.PlaceDonorHeart, _procedure.Stage,
                "Com o coração doente fora, a operação tem que avançar para posicionar o doador.");
        }

        [UnityTest]
        public IEnumerator TheDonorHeartDoesNotCountAsImplantedWhileItIsStillBeingHeld()
        {
            RequireTrackedHands();

            AdvanceToExplant();
            _procedure.CompleteStage(TransplantStage.RemoveNativeHeart);
            Assert.AreEqual(TransplantStage.PlaceDonorHeart, _procedure.Stage);

            XRHandInteractor hand = Hands()[0];
            XRGrabInteractable grab = _donorHeart.GetComponent<XRGrabInteractable>();
            GrabbableOrgan organ = _donorHeart.GetComponent<GrabbableOrgan>();

            RaiseSelectEntered(hand, grab);
            yield return null;

            // Lowered exactly into the pericardium, but not let go of.
            _donorHeart.transform.position = _seat.position;
            yield return null;
            _donorHeart.transform.position = _seat.position;
            yield return null;

            Assert.IsFalse(organ.Satisfied,
                "Um coração ainda preso à mão do cirurgião não está implantado. Antes desta " +
                "ligação existir ele contava, porque IsHeld era sempre falso.");
            Assert.AreEqual(TransplantStage.PlaceDonorHeart, _procedure.Stage);

            RaiseSelectExited(hand, grab);
            yield return null;
            yield return null;

            Assert.IsTrue(organ.Satisfied, "Pousado no lugar certo e solto: aí sim está posicionado.");
            Assert.AreEqual(TransplantStage.ConnectVessels, _procedure.Stage);
        }

        // -----------------------------------------------------------------------------------
        // As regras do órgão, sem depender do óculos.
        //
        // Os três testes acima verificam o CAMINHO — evento da XRI chegando ao contrato — e por
        // isso precisam de dispositivo. Estes verificam as REGRAS, chamando o contrato direto:
        // é o mesmo OnGrabbed/OnReleased que o adaptador chama quando a mão fecha. Assim a
        // lógica que decide o que é cardiectomia e o que é implante fica coberta em qualquer
        // máquina, e o que fica pendente de hardware é só o fio entre um e outro.
        // -----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator CarregarONaoBastaParaExplantar_PrecisaPousar()
        {
            AdvanceToExplant();

            SurgicalInteractable organ = _nativeHeart.GetComponent<SurgicalInteractable>();
            // A XRI mantém o rigidbody preso à mão. Como este teste chama o contrato diretamente
            // (sem óculos), replica essa parte para a gravidade não mover o órgão entre frames.
            _nativeHeart.GetComponent<Rigidbody>().isKinematic = true;
            organ.OnGrabbed(null);
            yield return null;

            _nativeHeart.transform.position = _seat.position + new Vector3(0.6f, 0.1f, 0f);
            yield return null;

            Assert.AreEqual(TransplantStage.RemoveNativeHeart, _procedure.Stage,
                "Carregar o coração não é explantar; ele ainda está na mão do cirurgião.");

            organ.OnReleased();
            yield return null;

            Assert.IsTrue(_nativeHeart.GetComponent<GrabbableOrgan>().Satisfied);
            Assert.AreEqual(TransplantStage.PlaceDonorHeart, _procedure.Stage,
                "Com o coração doente fora e pousado, a operação avança para o doador.");
        }

        [UnityTest]
        public IEnumerator SoltarPertoDoAssentoNaoExplanta()
        {
            AdvanceToExplant();

            SurgicalInteractable organ = _nativeHeart.GetComponent<SurgicalInteractable>();
            organ.OnGrabbed(null);
            yield return null;

            // Levantado, mas ainda dentro do tórax: não é cardiectomia, é hesitação.
            _nativeHeart.transform.position = _seat.position + new Vector3(0.03f, 0.02f, 0f);
            organ.OnReleased();
            yield return null;

            Assert.AreEqual(TransplantStage.RemoveNativeHeart, _procedure.Stage,
                "Mexer no coração sem tirá-lo do lugar não pode contar como retirada.");
        }

        [UnityTest]
        public IEnumerator ODoadorNaoContaImplantadoEnquantoEstaNaMao()
        {
            AdvanceToExplant();
            _procedure.CompleteStage(TransplantStage.RemoveNativeHeart);
            Assert.AreEqual(TransplantStage.PlaceDonorHeart, _procedure.Stage);

            SurgicalInteractable organ = _donorHeart.GetComponent<SurgicalInteractable>();
            GrabbableOrgan grabbable = _donorHeart.GetComponent<GrabbableOrgan>();

            // No jogo, XRGrabInteractable segura o corpo na pose da mão. Em headless não existe
            // mão XR, portanto o teste precisa impedir a queda livre enquanto simula o agarre.
            _donorHeart.GetComponent<Rigidbody>().isKinematic = true;
            organ.OnGrabbed(null);
            yield return null;

            _donorHeart.transform.position = _seat.position;
            yield return null;
            yield return null;

            Assert.IsFalse(grabbable.Satisfied,
                "Um coração ainda preso à mão do cirurgião não está implantado.");

            // O assento acompanha a animação ainda terminando de abrir o tórax; a mão real
            // acompanharia esse movimento. Reaplica a pose atual antes de simular a soltura.
            _donorHeart.transform.position = _seat.position;
            organ.OnReleased();
            yield return null;
            yield return null;

            Assert.IsTrue(grabbable.Satisfied,
                $"Pousado no lugar certo e solto: aí sim. active={grabbable.isActiveAndEnabled} " +
                $"held={grabbable.IsHeld} distance={grabbable.DistanceFromSeat:F4}m");
            Assert.AreEqual(TransplantStage.ConnectVessels, _procedure.Stage);
        }

        [UnityTest]
        public IEnumerator HeartThrownOutOfReachReturnsToItsStartingPose()
        {
            Assert.IsNotNull(_nativeHeart.GetComponent<ReturnHomeOnRelease>(),
                "O coração nativo também precisa da mesma recuperação das ferramentas.");

            ReturnHomeOnRelease returnHome = _donorHeart.GetComponent<ReturnHomeOnRelease>();
            Assert.IsNotNull(returnHome, "O coração doador precisa da mesma recuperação das ferramentas.");
            Assert.AreEqual(ReturnHomeOnRelease.ReleaseMode.DropAndRespawn, returnHome.Mode);

            Vector3 homePosition = _donorHeart.transform.position;
            Quaternion homeRotation = _donorHeart.transform.rotation;
            SurgicalInteractable interactable = _donorHeart.GetComponent<SurgicalInteractable>();

            interactable.OnGrabbed(null);
            _donorHeart.transform.position = new Vector3(5f, 1.3f, 0.47f);
            interactable.OnReleased();

            returnHome.Tick(0.61f);

            Assert.AreEqual(homePosition, _donorHeart.transform.position);
            Assert.AreEqual(homeRotation, _donorHeart.transform.rotation);
            Assert.IsFalse(returnHome.IsLoose);
            yield break;
        }

        [UnityTest]
        public IEnumerator ACenaCompletaVaiDaCanulacaoAteOFechamento()
        {
            ChestSkinPatch patch = Object.FindFirstObjectByType<ChestSkinPatch>();
            SternotomyController sternum = Object.FindFirstObjectByType<SternotomyController>();
            BypassWorker bypass = Object.FindFirstObjectByType<BypassWorker>();
            ChestClosure closure = Object.FindFirstObjectByType<ChestClosure>();
            SutureWorker suture = Object.FindFirstObjectByType<SutureWorker>();

            Assert.IsNotNull(patch);
            Assert.IsNotNull(sternum);
            Assert.IsNotNull(bypass);
            Assert.IsNotNull(closure);
            Assert.IsNotNull(suture);

            _procedure.Begin();
            Assert.AreEqual(SurgicalDifficulty.Medio, _procedure.Plan.Difficulty,
                "A cena foi construída como Médio e precisa reabrir com o mesmo plano.");

            // A pele e o osso ficam realmente abertos, para também validar o fechamento no fim.
            patch.Open();
            patch.Tick(2f);
            Assert.IsTrue(_procedure.CompleteStage(TransplantStage.SkinIncision));
            sternum.Open();
            sternum.Tick(2f);
            Assert.AreEqual(TransplantStage.OpenPericardium, _procedure.Stage);
            Assert.IsTrue(_procedure.CompleteStage(TransplantStage.OpenPericardium));
            Assert.AreEqual(TransplantStage.GoOnBypass, _procedure.Stage);

            // O gesto Médio precisa sobreviver à gravação da cena como a cadeia completa.
            CollectionAssert.AreEqual(new[]
            {
                BypassStep.Cannulate, BypassStep.ClampAorta, BypassStep.Cardioplegia,
            }, bypass.Sites[0].Chain);
            bypass.Sites[0].Point.parent.parent.Find("SurgeonHandTip").position = bypass.Sites[0].Point.position;
            bypass.Tick(bypass.Sites[0].Seconds + 0.1f);
            Assert.AreEqual(TransplantStage.RemoveNativeHeart, _procedure.Stage,
                "Canular, clampear e dar cardioplegia precisa liberar a retirada do coração.");

            SurgicalInteractable native = _nativeHeart.GetComponent<SurgicalInteractable>();
            native.OnGrabbed(null);
            _nativeHeart.transform.position = _seat.position + new Vector3(0.6f, 0.1f, 0f);
            native.OnReleased();
            Assert.AreEqual(TransplantStage.PlaceDonorHeart, _procedure.Stage);

            SurgicalInteractable donor = _donorHeart.GetComponent<SurgicalInteractable>();
            donor.OnGrabbed(null);
            _donorHeart.transform.position = _seat.position;
            donor.OnReleased();
            yield return null;
            Assert.AreEqual(TransplantStage.ConnectVessels, _procedure.Stage);

            foreach (VesselAnastomosis vessel in
                     Object.FindObjectsByType<VesselAnastomosis>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                vessel.Work(vessel.transform.position, 2f);
            }
            Assert.AreEqual(TransplantStage.Restart, _procedure.Stage,
                "As cinco anastomoses precisam liberar a saída da circulação extracorpórea.");

            // A fibrilação tem testes próprios; aqui isolamos o encadeamento completo da cena.
            DefibrillationWorker defib = Object.FindFirstObjectByType<DefibrillationWorker>();
            Assert.IsNotNull(defib);
            defib.Configure(0f, 1f, 1);

            Transform handTip = bypass.Sites[0].Point.parent.parent.Find("SurgeonHandTip");
            for (int i = 1; i < bypass.Sites.Count; i++)
            {
                handTip.position = bypass.Sites[i].Point.position;
                bypass.Tick(bypass.Sites[i].Seconds + 0.1f);
                if (i == 1) { defib.Tick(0.1f); }
            }
            Assert.AreEqual(TransplantStage.CloseSkin, _procedure.Stage);

            sternum.Tick(2f);
            closure.Tick();
            patch.Tick(2f);
            closure.Tick();
            Assert.IsTrue(patch.IsClosed, "O tórax e a pele precisam voltar à posição de fechamento.");

            SurgicalInteractable holder = GameObject.Find("PortaAgulha").GetComponent<SurgicalInteractable>();
            Transform needle = GameObject.Find("NeedleTip").transform;
            holder.OnGrabbed(null);
            for (int stitch = 0; stitch < suture.StitchCount; stitch++)
            {
                for (int phase = 0; phase < 2; phase++)
                {
                    needle.position = suture.MarkPosition(stitch, phase);
                    suture.Tick(0.5f);
                }
            }
            holder.OnReleased();

            Assert.IsTrue(suture.IsComplete, "Os cinco pontos precisam terminar a sutura.");
            Assert.IsTrue(_procedure.IsComplete,
                "Depois de fechar os ossos e suturar a pele, a cirurgia precisa terminar.");
        }

    }
}
