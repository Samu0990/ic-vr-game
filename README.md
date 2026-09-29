# Simulador de transplante cardíaco em VR

Projeto de iniciação científica. Experiência de realidade virtual em que o visitante
executa um transplante de coração, pensada para estande de evento com fila.

Unity 6000.3.22f1 · URP · OpenXR · Meta Quest (standalone)

---

## Qual cena abrir

O repositório tem três cenas jogáveis, resultado de duas mudanças de conceito.
**Só uma está em desenvolvimento:**

| Cena | Estado | O que é |
|---|---|---|
| **`TransplanteCardiaco`** | **ATUAL** | Transplante de coração com circulação extracorpórea |
| `PortaDeEntrada` | Legado | Inserção de trocáteres em laparoscopia |
| `SurgeryMVP` | Legado | Incisão com bisturi e controle de hemorragia |

As duas legadas continuam no repositório porque funcionam e têm testes que passam —
apagá-las descartaria trabalho verificado. Mas nenhuma recebe desenvolvimento novo,
e a `SurgeryMVP` é a única que ainda usa bisturi e pinça.

## Construir a cena

> **Depois de puxar alterações do construtor, reconstrua a cena** (`VRSurgery` →
> `Transplante — nível Médio`). O arquivo `TransplanteCardiaco.unity` no repositório é
> sempre a última cena gerada por alguém; código novo do construtor só aparece depois do
> rebuild. A cena **não** foi regenerada na sessão que adicionou bisturi, sutura e sala
> (ela rodou sem Unity) — o primeiro rebuild é seu.

As cenas são **geradas por código**, não editadas à mão. Arrastar objetos na
Hierarchy funciona até o próximo rebuild, que desfaz tudo. Para mudar a cena,
mude o construtor.

No Editor, menu `VRSurgery`:

- `Transplante — nível Fácil` / `Médio` / `Difícil`
- `Build 'A Porta de Entrada'` (legado)
- `Rebuild MVP Scene` (legado)

Por linha de comando, sem abrir o Editor:

```bash
~/Unity/Hub/Editor/6000.3.22f1/Editor/Unity -batchmode -quit \
  -projectPath . -executeMethod VRSurgery.EditorTools.TransplanteSceneBuilder.BuildMedium
```

## Níveis de dificuldade

O nível decide quanto da circulação extracorpórea o visitante executa. **As regras
clínicas não mudam** — o que muda é em que ponto da cirurgia ele entra.

| Nível | CEC | Gestos | Rodada |
|---|---|---|---|
| Fácil | Equipe já colocou em bomba; saída em um gesto | 7 s | 90 s |
| Médio | Entrada encadeada em um gesto; saída completa | 26 s | 150 s |
| Difícil | Os seis gestos, um a um | 37 s | 240 s |

A duração da rodada vem do nível (`BypassPlan`), não de uma constante. Um nível mais
fácil é um visitante que chega mais tarde na cirurgia, nunca um paciente para quem as
regras deixaram de valer: o preparo da equipe passa pelas mesmas validações
(`ApplyTeamPreparation`), então nenhum nível produz um paciente que o procedimento não
poderia ter produzido.

## A cirurgia

```
Incisão com bisturi  →  Abrir o esterno  →  Entrar em bomba  →  Retirar o coração doente
                     →  Posicionar o doador  →  Conectar 5 vasos
                     →  Sair de bomba (o coração volta a bater)
                     →  Fechar: esterno e pele voltam sozinhos, sutura com porta-agulha (5 pontos)
```

A ordem é imposta, não sugerida, e cada recusa explica a consequência clínica —
cardioplegia sem clampe é lavada pela circulação, sair de bomba sem desarejar manda
êmbolo gasoso para o cérebro. A razão é o conteúdo didático; um gesto que apenas
falha não ensina nada.

A incisão e a sutura são etapas opcionais do `TransplantProcedure`
(`SetSkinStages(true)`), ligadas pela cena. Sem elas o procedimento é exatamente o de
antes, que é o que todos os testes antigos montam à mão. Com elas a rodada ganha 45 s
(`SkinStageSeconds`) sobre o tempo do nível.

### O que o visitante vê e faz

| Etapa | Gesto | Guia visual |
|---|---|---|
| Incisão | Pegar o **bisturi** na mesa de Mayo e passar a lâmina sobre a linha roxa, **encostando** na pele (até 4 mm acima conta; mais de 2,2 cm abaixo é "profunda demais") | Linha roxa de marcador cirúrgico; a pele afunda sob a lâmina, o corte abre ~4 mm atrás dela pela tensão da pele, as gotas de sangue crescem e às vezes escorrem pela lateral do tórax |
| Cautério (opcional) | Pegar o **bisturi elétrico** no coldre sobre o campo e encostar a ponta nos 3 pontos que sangram na borda da ferida | Fumaça, chiado e vibração; o ponto para de sangrar e fica a marca de cauterização |
| Esterno | Pegar a **serra esternal** na bandeja e apoiar a lâmina na marca dourada por 3 s | Anel dourado (só nesta etapa); zumbido e vibração forte enquanto serra; o esterno se parte em duas metades presas no afastador de Finochietto |
| CEC | Mão no ponto por alguns segundos | Anel âmbar pulsando **só no próximo passo** da bomba |
| Vasos | Mão firme no anel por 1,4 s; mão trêmula faz sangrar, pressão estanca | Anéis vermelho/azul (só nesta etapa); vermelho pulsante enquanto sangra |
| Sutura | **Porta-agulha**: ponta da agulha no ponto azul de entrada, depois no de saída; 5 pontos | Pontos azuis do ponto atual; a pele afunda sob a agulha; cada nó fica na pele e fecha a abertura do corte no seu trecho |

Com o tórax aberto, uma poça de sangue fica no fundo da cavidade e **sobe enquanto um vaso
vaza** (`CavityBloodPool`), baixando devagar quando o vazamento é estancado.

O **coração nativo bate** fraco e irregular (fibrilação atrial) até a cardioplegia; o doador
volta a bater ao sair de bomba. Os dois monitores (anestesia e a **tela grande de batimentos
na parede**) seguem o coração visível: FC, ECG e bipe batem junto com o órgão.

### Física

- **Campos cirúrgicos são tecido simulado** (`DrapeCloth`, Unity Cloth): presos em volta da
  janela, cedem ~1,5 cm sob a mão ou um instrumento e balançam nas bordas que caem da mesa. Um
  vigia volta para o campo estático se a simulação sair do lugar. Desligar: `UseDrapeCloth`.
- **Coisas não atravessam o paciente**: colisor na superfície dos campos e no fundo da cavidade;
  órgão ou instrumento solto para em cima deles. Instrumentos soltos voltam sozinhos para o
  lugar (`ReturnHomeOnRelease`). As mãos rastreadas continuam atravessando (não há mãos físicas).

Ao abrir o Editor com uma cena mais velha que o construtor (`BuildVersion`), o Unity pergunta se
quer reconstruir (`TransplantSceneFreshness`).

Todo gesto de "segurar" mostra um **anel de progresso flutuante** sobre o local
(`WorkProgressIndicator`), com rótulo e porcentagem, e vibra de leve na mão enquanto avança.
Erros (bisturi fora da linha, anastomose vazando, passo da bomba fora de ordem) aparecem
no monitor do cirurgião, fazem som e vibram forte.

### Sala de cirurgia

Tudo gerado pelo construtor, sem asset novo: paredes de azulejo, piso epóxi,
fluxo laminar e luminárias no teto, **foco cirúrgico** de duas cúpulas (o spot de luz fica
dentro dele), **campos cirúrgicos azuis** moldados ao corpo com janela sobre o esterno,
campo de anestesia no pescoço, máquina de anestesia com circuito até a via aérea,
**monitor de sinais vitais** com ECG e pletismografia animados e bipe no QRS,
**máquina de CEC** com 5 bombas de rolete e as linhas arterial/venosa até o campo,
mesa auxiliar, suporte de soro, negatoscópio com raio-X, relógio de parede com a hora real,
gases medicinais, armário, portas com visor, pia de escovação e lixeiras.

O monitor de sinais vitais conta a história da cirurgia: coração doente taquicárdico e
hipotenso → linha reta e "CEC — BOMBA LIGADA" em bomba → ritmo sinusal quando o doador
bate. Um vaso vazando derruba a pressão e dispara o alarme.

Todos os sons (bipe, alarme, bisturi, agulha, sucesso, ruído da sala) são sintetizados
em tempo de execução (`ProceduralTones`); não há arquivo de áudio.

## Arquitetura

Regra geral: **a lógica clínica não depende do Unity**, e o que sabe onde estão as
mãos é um MonoBehaviour separado. Isso deixa as regras testáveis sem cena, sem rig e
sem frame.

| Onde | O quê |
|---|---|
| `Scripts/Transplant/BypassProcedure.cs` | CEC. C# puro, sem `UnityEngine` |
| `Scripts/Transplant/BypassPlan.cs` | Os três níveis. C# puro |
| `Scripts/Transplant/TransplantProcedure.cs` | Ordem das etapas |
| `Scripts/Transplant/*Worker.cs` | Onde está a mão do cirurgião |
| `Scripts/Interaction/` | Contrato de agarre, usado por todos os instrumentos |
| `Scripts/Session/` | Cronômetro, placar, estados do estande |
| `Scripts/Cutting/MeshIncision.cs` | Corte topológico de malha (ver Limitações) |

### Incisão local experimental em pele curva

O primeiro incremento de incisão curva usa uma única cadeia de execução:

`BladeTip → CuttingInteractor → IncisionSystem → CuttableTissue → MeshIncision`

- `MeshIncision.Curved.cs` recebe uma polyline 3D local, divide somente os triângulos
  interceptados, duplica as duas bordas e preserva UV, normal, tangente e submesh.
- `CuttableTissue` filtra/espaça a trajetória e só reconstrói a mesh e o `MeshCollider`
  em `END CUT`, não a cada frame. As faces internas usam um material separado.
- A orientação exposta por `BladeTip` rejeita contato superficial, movimento fora do fio e
  a face da lâmina deitada sobre a pele.
- `CurvedSkinProbe` chama a mesma API do runtime e extrai sua fixture da malha humana
  `PATIENT_BodySkin.glb`; não usa cubo, cápsula, plano ou o paciente da cena final.

Para tornar um objeto cortável, coloque no mesmo GameObject `MeshFilter`, `MeshRenderer`,
`MeshCollider`, `TissueSurface`, `IncisionSystem` e `CuttableTissue`. Configure profundidade,
abertura, espaçamento, resistência e material interno no `CuttableTissue`. No bisturi,
`BladeTip.localEdgeDirection` deve acompanhar o fio e `localFaceNormal` a face larga da lâmina.

Arquivos deste incremento: `MeshIncision.cs`, `MeshIncision.Curved.cs`, `CuttableTissue.cs`,
`BladeTip.cs`, `CuttingInteractor.cs`, `IncisionSystem.cs` e `CurvedSkinProbe.cs`.

## Verificação sem headset

Ferramentas de editor que renderizam o estado da cena para PNG, porque uma classe
inteira de defeito passa em toda checagem programática e só aparece olhando —
marcadores quadrados onde o teste é radial, um trocáter em pé feito torneira, uma
vinheta cobrindo a visão inteira. Todos esses passaram nos testes.

```bash
# a cena inteira, de vários ângulos
-executeMethod VRSurgery.EditorTools.SceneShots.CaptureFromCommandLine -shotOut /tmp/shots

# o tórax com pele e esterno desligados
-executeMethod VRSurgery.EditorTools.ThoraxProbe.RunFromCommandLine -out /tmp/torax
```

## Testes

```bash
~/Unity/Hub/Editor/6000.3.22f1/Editor/Unity -batchmode -nographics \
  -projectPath . -runTests -testPlatform PlayMode -testResults /tmp/res.xml
```

**121 testes passando na última execução registrada neste README**, antes dos testes do PR do orientador (`AnastomosisTests`, `NameEntryTests`, `VesselAnastomosisVisualTests`) e dos 17 de `SkinStagesTests` (incisão, sutura, pele, etapas novas, correções do PR). Esses ainda precisam ser rodados no Editor. O que os testes cobrem e por quê:

- **A volta do estande**: a rodada começa no primeiro corte, o transplante concluído vence a
  rodada, e o visitante seguinte recebe tórax fechado e coração doente de volta. Essas regras
  já existiam todas, e nenhuma estava ligada — a classe de defeito que um teste de cada lado
  da junção não enxerga
- **Ordem da CEC** nos três níveis, incluindo cada erro clínico nomeado
- **Ergonomia**: todo instrumento alcançável pelos dois ombros — canhoto e pessoa de
  braço curto precisam pegar qualquer instrumento com qualquer mão
- **Anastomose**: encostar não costura; só segurar costura
- **Batimento**: a sístole ocupa menos de metade do ciclo, senão lê como respiração

## Limitações conhecidas

**Sessão de 29/09 (bisturi, sutura, sala, correções do PR do orientador) — escrita sem Unity.**
O código compila contra as DLLs de referência do Unity e stubs dos pacotes, mas nada foi
renderizado nem jogado. Primeiro rebuild: olhar o Console, depois olhar a cena. Pontos a
conferir com o óculos:
- Posição e escala da **mesa de Mayo** (sobre o abdome), do bisturi e do porta-agulha;
  o alcance foi calculado para ombro entre 1,10 m e 1,35 m.
- A **janela de pele** (`ChestSkinPatch`) e os **campos** são amostrados da malha do corpo;
  se a pele original aparecer por baixo da janela aberta, aumentar a margem em
  `CutSkinUnderPatch`.
- Orientação do texto do **monitor de sinais vitais** e o sentido da varredura do ECG.
- O **pós-processamento** agora liga no PC (`HeadsetPostProcessing`) e continua desligado
  no Quest standalone. Medir o frame time antes de ligar no Quest.
- A **tolerância do bisturi** (1,4 cm da linha média, 4 mm acima e 3 cm abaixo da pele)
  e o raio da agulha (1,3 cm) são estimativas.
- **Custo no Quest ainda não medido** do que mais pesa: tecido simulado (~1,6 mil vértices por
  quadro na CPU), pele da janela recalculada enquanto o bisturi encosta, fumaça, e o foco com
  sombra suave. Se faltar quadro: `UseDrapeCloth = false` primeiro.
- O `Cloth` não é documentado para colliders de trigger, por isso as esferas das mãos são
  sólidas e ignoram instrumentos e órgãos. Se algo for empurrado pela mão, é aí que olhar.

Nada aqui foi jogado com o óculos. Os tempos de gesto, os raios de 2,2 cm e a
legibilidade dos anéis a 30 cm do olho são estimativas informadas, não medições.

- **Modelos são sopa de cascas.** O coração tem 40.236 arestas abertas em 776 laços;
  o conjunto de vasos gerado separadamente sai como 350 fragmentos. Consequência
  prática: não dá para derivar geometricamente a boca de cada vaso, então os pontos de
  anastomose são transforms posicionados à mão, com anéis visíveis no lugar do modelo
  de vasos.
- **A incisão curva ainda é experimental.** A fixture orgânica aceita duas incisões locais
  consecutivas e preserva os canais da mesh, mas a captura atual reprovou visualmente: o
  albedo embutido da pele não apareceu no render batch e há descontinuidades escuras entre
  trechos das faces internas. Portanto `CurvedSkinTest` ainda não é apresentação final.
- **Custo ainda não medido no Quest.** Na fixture Editor de 6.608 vértices, duas reconstruções
  afetaram 219 triângulos e levaram 326,7 ms no total. O collider é recocido somente no fim
  da trajetória, mas ainda precisa de perfil no hardware alvo.
- **Contato curvo usa um eixo externo dominante.** Funciona para uma pequena região torácica;
  superfícies que dobram mais de 90 graus exigirão outra estratégia de projeção.
- `WoundRenderer` (faixa cosmética) e `IncisableSkin` (troca binária de estado) continuam no
  legado e não foram removidos neste incremento.
- **A projeção exige Display 2**, que não existe em Quest standalone.
- **Projeção do `TransplanteCardiaco` é nova e não testada em sala, e usa 3 displays.** Display 1 espelha o cirurgião (fila de espera), Display 2 é o projetor apontado pro manequim físico (só a cena 3D: paciente, coração, luz avermelhando com o relógio, nenhum texto), Display 3 é um monitor à parte, ao lado do manequim, com relógio, placar e a instrução do momento (`ProjectionHUD`, como um monitor de sinais vitais). A separação existe porque texto projetado em cima de um corpo físico não dá pra ler. A barra de sangramento no Display 3 lê a fração de anastomoses vazando em vez de um `BleedingSystem`. Posição da câmera do Display 2 e enquadramento herdaram os números validados no MVP, não foram remedidos para este paciente nem para o manequim real.
- **O gradil costal** é proporcionalmente estreito: 23,8 × 16,0 × 30,0 cm contra
  28 × 20 × 30 reais. Escala uniforme, sem distorção, mas um tórax magro.
- **Atmosfera visual nova e não testada no headset.** `TuneRoomLighting` reforça a luz direcional e pendura um foco cirúrgico sobre o tórax; `BuildClinicalPostProcessing` liga um Volume global (bloom leve, saturação -6, vinheta sutil) só na câmera do headset — espectador e projetor continuam com `renderPostProcessing = false`. `ApplyGloveMaterialToHands` procura por qualquer renderer com "Hand" no nome sob "XR Origin" para trocar pelo material `GLOVE_NitrileBlue`; se a malha de mão do VR Template usada no projeto tiver outro nome, o Console avisa e nada é trocado — confirme no primeiro build. Custo de GPU do Volume no Quest ainda não foi medido.
- **Teclado de nome no placar não validado.** `NameEntryController` + `NameEntryWorker` existem e têm testes de lógica. O painel agora fica à frente e à direita do cirurgião, na borda da mesa, inclinado para o olho, e só aparece quando há um nome para digitar (`NameEntryPanel`), com a tecla sob o dedo acendendo. Ninguém tentou digitar um nome com o óculos posto.

Próximo passo recomendado para a incisão: ordenar a borda por conectividade topológica (não
apenas por distância ao longo da trajetória), gerar uma parede interna contínua e corrigir a
ligação do albedo/normal da pele no URP. Só depois repetir a captura orgânica e perfilar no Quest.

## Orçamento de Quest

182 mil triângulos para a anatomia toda, contra 5,98 milhões como os modelos chegaram.
O alvo é 72 Hz (13,9 ms por frame), não 60 FPS — num headset, 60 FPS é desconforto.
