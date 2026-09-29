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
Incisão com bisturi  →  Abrir o esterno  →  Abrir o pericárdio  →  Entrar em bomba  →  Retirar o coração doente
                     →  Posicionar o doador  →  Conectar 5 vasos
                     →  Retirar o clampe  →  o coração novo FIBRILA  →  desfibrilar com as pás internas
                     →  Desarejar  →  Sair de bomba
                     →  Fechar: esterno e pele voltam sozinhos, sutura com porta-agulha (5 pontos)
```

A ordem é imposta, não sugerida, e cada recusa explica a consequência clínica —
cardioplegia sem clampe é lavada pela circulação, sair de bomba sem desarejar manda
êmbolo gasoso para o cérebro. A razão é o conteúdo didático; um gesto que apenas
falha não ensina nada.

A incisão e a sutura são etapas opcionais do `TransplantProcedure`
(`SetSkinStages(true)`), ligadas pela cena. Sem elas o procedimento é exatamente o de
antes, que é o que todos os testes antigos montam à mão. Com elas a rodada ganha 45 s
(`SkinStageSeconds`) sobre o tempo do nível, mais 10 s do pericárdio (`PericardiumSeconds`), e mais 12 s (`DefibrillationSeconds`) nos
níveis em que retirar o clampe é um gesto próprio (Médio e Difícil), que são os que têm
a fibrilação.

### O que o visitante vê e faz

| Etapa | Gesto | Guia visual |
|---|---|---|
| Incisão | Pegar o **bisturi** na mesa de Mayo, **apontá-lo ao longo da linha roxa com a lâmina em pé** e **puxar** encostando na pele (até 4 mm acima conta; mais de 2,2 cm abaixo é "profunda demais"). Lâmina parada só afunda a pele; deitada ou empurrada de lado só raspa, e o monitor diz como segurar. **Ajuda adaptativa:** depois de ~3 s tentando com a lâmina errada, a regra cai para aquele visitante | Linha roxa de marcador cirúrgico; a pele afunda sob a lâmina e **abre onde a lâmina passou** (até 4 mm da linha), mostrando as **camadas** na parede do corte (pele, derme, gordura amarela, fáscia e, com a ferida afastada, músculo); corte fora da linha deixa **arranhão** na pele; a lâmina sai **suja de sangue**; gotas crescem e escorrem. Ao terminar, **cartão com 1–3 estrelas** sobre o tórax (passadas, desvio em mm, profundidade, arranhões) |
| Cautério (opcional) | Pegar o **bisturi elétrico** no coldre sobre o campo e encostar a ponta nos 3 pontos que sangram na borda da ferida | Fumaça, chiado e vibração; o ponto para de sangrar e fica a marca de cauterização |
| Esterno | Pegar a **serra esternal** na bandeja e apoiar a lâmina na marca dourada por 3 s | Anel dourado (só nesta etapa); zumbido e vibração forte enquanto serra; o esterno se parte em duas metades presas no afastador de Finochietto |
| Pericárdio | Com o **bisturi elétrico**, passar a ponta sobre a linha tracejada no meio da membrana (1,3 cm de tolerância) | Membrana brilhante sobre o coração, que bate por baixo; fumaça e chiado; as duas metades **dobram para os lados** e ficam abertas (o pericárdio fica aberto no fim do transplante) |
| CEC | Mão no ponto por alguns segundos | Anel âmbar pulsando **só no próximo passo** da bomba. Cada passo **aparece no tórax**: cânula na aorta e nas duas cavas (Y até a linha venosa) ao canular, **clampe aórtico** enquanto a aorta está clampeada, linha de cardioplegia/vent na raiz da aorta |
| Desfibrilação | Ao retirar o clampe, o coração novo **fibrila** (acontece de 10% a 80% das vezes na vida real; aqui, sempre, para ensinar). Pegar as **pás internas** sobre o campo e segurar o coração entre as colheres ~1 s: carrega e dispara. 1º choque 10 J (reverte ~60%), 2º 20 J (sempre reverte, para a rodada não depender de sorte). A bomba **recusa desarejar/sair** com o coração fibrilando | Coração tremendo; monitor com traçado de FV, "FC FV", alarme; anel vermelho "CARREGANDO 10 J", zumbido de carga, pancada e espículo no ECG a cada choque; cartão "CORAÇÃO BATENDO!" ao reverter |
| Vasos | **Porta-agulha** com a ponta da agulha no anel (1,5× mais rápido e mais firme) ou a mão firme no anel por 1,4 s; mão trêmula faz sangrar, pressão estanca | Anéis vermelho/azul (só nesta etapa); vermelho pulsante enquanto sangra |
| Sutura | **Porta-agulha**: ponta da agulha no ponto azul de entrada, depois no de saída; 5 pontos | Pontos azuis do ponto atual; a pele afunda sob a agulha; cada nó fica na pele e fecha a abertura do corte no seu trecho; entre a entrada e a saída o **fio** aparece indo do ponto de entrada até a agulha. No último nó, **cartão com estrelas** da sutura (mm do alvo, furos fora do ponto, tempo); passar a agulha por baixo da pele da entrada até a saída não conta como furo. Ao fechar, **fios de aço** aparecem no esterno enquanto o osso se junta e **dois drenos mediastinais** saem abaixo da ferida até o frasco de drenagem na grade da mesa |
| Fim | — | **Curativo** (filme e compressa com uma mancha) sobre a sutura enquanto o placar é mostrado; cartão **"TRANSPLANTE CONCLUÍDO!"** com as estrelas da incisão e da sutura e os choques. Cartões que chegam juntos entram em fila, um depois do outro |

Com o tórax aberto, uma poça de sangue fica no fundo da cavidade e **sobe enquanto um vaso
vaza** (`CavityBloodPool`), baixando devagar quando o vazamento é estancado.

O **coração nativo bate** fraco e irregular (fibrilação atrial) até a cardioplegia; o doador
volta a bater ao sair de bomba. Os dois monitores (anestesia e a **tela grande de batimentos
na parede**) seguem o coração visível: FC, ECG e bipe batem junto com o órgão.

### Física da pele

A pele da janela (`ChestSkinPatch`) reage como nos jogos de cirurgia:
- **Afunda** sob a lâmina e a agulha, e é **arrastada** alguns milímetros junto com a lâmina
  que corta.
- Cada trecho recém-cortado **estremece** ao abrir e assenta na abertura (pele sob tensão).
- **Pinça de dissecção** na bandeja (`SkinForceps`): segure, encoste as pontas na pele e
  **aperte o gatilho** — a pele fica presa e acompanha a mão, esticando até 3 cm; do outro lado
  do corte a pele não vem junto (é outro pedaço). Solte o gatilho e ela **volta com balanço
  elástico**. As pontas da pinça fecham enquanto o gatilho está apertado.
- **Costurar segurando a borda**: com a borda presa pela pinça perto do ponto, a agulha passa
  **2× mais rápido** — a técnica de duas mãos de verdade, recompensada, não obrigatória.

### Ajuda adaptativa (nível único)

Não há níveis de dificuldade para o visitante escolher; o jogo ajuda quem precisa:

- **Ritmo** (`PaceAssist`): cada etapa tem um orçamento de tempo (escalado para caber em 85% da
  rodada). Quem passa **15 s** atrás do orçamento tem os gestos de segurar (CEC e vasos)
  contando **1,6×** mais rápido; **35 s** atrás, **2,5×**. O visitante continua fazendo tudo,
  é avisado no monitor. Não diminui durante a vez;
  zera para o próximo visitante.
- **Lâmina**: depois de ~3 s tentando cortar com a lâmina errada, a regra do fio cai.
- **Seta** sobre o instrumento da etapa e **contorno azul-claro** no que a mão vai pegar.

### Mecânica estilo Job Simulator (ninguém anda no estande)

- **Tudo ao alcance de um ponto só**: instrumentos a no máximo ~0,6 m dos ombros de quem está
  na marca dos pés (teste `TransplantErgonomicsTests` cobra 0,70 m pelos dois ombros).
- **Soltar = cair com física** (`ReturnHomeOnRelease`, modo `DropAndRespawn`): o instrumento
  fica onde caiu, no campo ou na bandeja de Mayo (agora sólida), e pode ser pego de novo;
  **arremessar** também funciona. Só se ele parar **fora do alcance** (chão, outro lado da
  mesa, atirado longe) por 0,6 s é que some e **reaparece com um "pop"** no lugar dele. A cada
  novo visitante, tudo volta para a bandeja. Instrumento que cai **dentro do paciente** (atravessa
  a pele da janela, que não tem colisor) também conta como perdido e reaparece.
- **A mesa vai até o visitante** (`VisitorFit` no XR Origin): no início de cada briefing (e de
  novo 3 s depois) e **quando o sensor de presença do óculos detecta que alguém o colocou**, nunca
  com a rodada correndo o rig é movido para a cabeça ficar exatamente
  sobre a marca e virada para o paciente. **Modo baixinho**: olhos abaixo de 1,48 m são
  levantados até 40 cm, para uma criança alcançar a mesa como um adulto. Tecla **R** no PC
  recentraliza a qualquer momento (para o operador).
- **Controles do operador sem teclado** (`OperatorControls`), porque o Quest roda sozinho no
  evento: segurar o **botão de menu do controle esquerdo** por **2 s** recentraliza a mesa em quem
  está com o óculos, por **4 s** encerra a vez e prepara o próximo visitante (um clique na mão
  marca cada ponto). No PC/Link: **R** recentraliza, **N** encerra a vez.
- **Marca dos pés** verde no chão onde o visitante fica.
- **Mãos de luva no lugar dos controles** (`ControllerHand` + `HandPoser`): a mão oficial da
  Unity que já vem no projeto (sample *HandVisualizer* do pacote XR Hands, a mesma do rastreio de
  mãos), com a luva de nitrila azul. Ela segue a **pose de empunhadura** do controle (a palma
  em volta do cabo, definição "grip" do OpenXR), não a pose de mira que o rig do template usa —
  essa fica na ponta do controle, uns centímetros à frente da mão. O modelo é girado a partir
  dos próprios ossos (palma para o cabo, dedos envolvendo), então qualquer mão com ossos no
  padrão OpenXR serve. **Os dedos mexem**: apertar o grip fecha médio, anelar e mínimo e traz o
  polegar; o gatilho dobra o indicador; soltando, a mão relaxa (nunca fica chapada). Sem
  Animator nem clipes: cada articulação gira no próprio eixo, achado na pose de repouso.
- **Pega de perto, pela palma** (Job Simulator): a esfera que acha instrumentos (10 cm) e o ponto
  onde eles ficam presos saem da ponta do controle e vão para **dentro do punho**; entre dois
  instrumentos próximos vale o de **superfície mais perto** (`ClosestPointOnCollider`); o **raio
  de longe está desligado** (não dá mais para puxar instrumento do outro lado da mesa). O
  **ponto de toque** (`Poke Point`, que aperta teclas e faz os gestos com a mão) vai para a
  **ponta do indicador** da luva. Ajuste fino no óculos, no `ControllerHand` de cada mão:
  `palmDepth`, `positionOffset`, `rotationOffset`, `fistOffset`.
- **Contorno azul-claro** (`GrabOutlines` + `InteractableOutline`, shader
  `VRSurgery/GrabOutline`): só **o que cada mão vazia pegaria agora** fica contornado — não todos
  os instrumentos ao alcance. Some ao pegar. É um casco invertido de 2,5 mm (um desenho a mais
  por objeto contornado, nada quando não há contorno); o construtor grava normais suavizadas no
  UV3 das malhas geradas para o contorno não abrir nas quinas. Brilho, vidro, texto e partículas
  não recebem contorno. O antigo brilho ciano (`GrabGlow`) saiu da cena porque acendia todos os
  instrumentos perto da mão ao mesmo tempo; o script continua no projeto.
- **Seta amarela pulando** sobre o instrumento que a etapa pede (`NextToolHint`): bisturi na
  incisão, bisturi elétrico enquanto houver sangramento e no pericárdio, serra no esterno, pás
  na fibrilação, porta-agulha no fechamento. Some quando o instrumento está na mão; não aparece
  nas etapas feitas com as mãos.

### Física

- **Campos cirúrgicos são tecido simulado** (`DrapeCloth`, Unity Cloth): presos em volta da
  janela, cedem ~1,5 cm sob a mão ou um instrumento e balançam nas bordas que caem da mesa. Um
  vigia volta para o campo estático se a simulação sair do lugar. Desligar: `UseDrapeCloth`.
- **O pano parece pano**: algodão de trama simples em duas escalas — de longe, rugas de pano
  deitado sobre o corpo, um vinco de lavanderia em cada sentido e o tom desigual de tecido
  lavado (`DrapeFolds`); de perto, os fios passando por cima e por baixo, torcidos, com
  engrossamentos (`DrapeWeave`, camada de detalhe do URP/Lit, ~1,5 mm por fio). Nas laterais e
  nos pés o pano **cai em pé** logo depois da borda da mesa (antes era uma rampa que virava uma
  "prateleira" azul na altura do joelho do visitante), com dobras verticais que aprofundam
  perto da barra e textura sem esticar na queda. Em volta da janela, um **painel absorvente**
  de trama mais fechada e tom mais escuro, como nos campos fenestrados de verdade.
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

Tudo gerado pelo construtor, sem asset novo. Toda caixa com mais de 1,5 cm de espessura
tem **bordas arredondadas** com sombreamento suave (108 triângulos, malha compartilhada por
tamanho), no estilo "gordinho" dos objetos do Job Simulator, em vez dos cubos de primitiva: paredes de azulejo, piso epóxi,
fluxo laminar e luminárias no teto, **foco cirúrgico** de duas cúpulas (o spot de luz fica
dentro dele), **campos cirúrgicos azuis** moldados ao corpo com janela sobre o esterno,
campo de anestesia no pescoço, máquina de anestesia com circuito até a via aérea,
**monitor de sinais vitais** com ECG e pletismografia animados e bipe no QRS,
**máquina de CEC** com 5 bombas de rolete e as linhas arterial/venosa até o campo,
mesa auxiliar, suporte de soro, negatoscópio com raio-X, relógio de parede com a hora real,
gases medicinais, armário, portas com visor, pia de escovação e lixeiras.

Do transplante em particular: **caixa térmica do órgão** aberta com gelo e o rótulo
"ÓRGÃO HUMANO PARA TRANSPLANTE", quadro de **Cirurgia Segura (OMS)** ao lado das portas,
**quadro branco** com a contagem de compressas/agulhas e o tempo de isquemia do enxerto,
**ecocardiógrafo transesofágico** na cabeceira com a sonda até a boca, **termorregulador**
com as mangueiras até o oxigenador e **recuperador celular** ao lado da bomba, e o
**porta-compressas** com as usadas em vermelho. Tudo estático e com materiais
compartilhados, para o static batching juntar no Quest. Textos 3D (`TextMesh`) ficam fora
do static batching: ele congelaria a malha antes do texto existir.

O monitor mostra também a **fibrilação ventricular** ao retirar o clampe (traçado
caótico, "FC FV", alarme) e um espículo a cada choque das pás.

**Modelos prontos no lugar dos gerados**: qualquer `.glb/.gltf/.fbx/.obj` colocado em
`Assets/Models/Sala/` com o nome de um encaixe (`maquina_anestesia`, `carrinho_parada`,
`foco_cirurgico`… lista completa em `Assets/Models/Sala/LEIA-ME.txt`) substitui a peça
gerada no próximo rebuild, na mesma altura e posição; `nome@90.glb` gira o modelo. O ambiente
em nuvem onde este código foi escrito não alcança sites de modelos (Sketchfab, Kenney,
Poly Haven, itch.io bloqueados), então os modelos têm de ser baixados no PC.

O monitor de sinais vitais conta a história da cirurgia: coração doente taquicárdico e
hipotenso → linha reta e "CEC — BOMBA LIGADA" em bomba → ritmo sinusal quando o doador
bate. Um vaso vazando derruba a pressão e dispara o alarme.

Todos os sons (bipe, alarme, bisturi, agulha, sucesso, ruído da sala) são sintetizados
em tempo de execução (`ProceduralTones`); não há arquivo de áudio.

## Narração da equipe (voz)

`VoiceGuide` fala com o visitante em cada etapa e em cada emergência (24 falas: boas-vindas,
cada etapa, "fique na linha roxa", "está vazando! pressione", "fibrilação! pegue as pás",
"carregando... afasta!", "voltou, ritmo sinusal", "trinta segundos"...). Uma voz por vez; as
urgentes interrompem; cada fala tem intervalo mínimo para não repetir.

Os áudios **ainda não existem** (o ambiente onde isso foi escrito não alcança os modelos de voz).
Para gerar, no PC, na raiz do projeto:

```
bash Tools/voz/gerar_vozes.sh           # Piper, voz pt_BR-faber-medium, offline depois do 1º download
VOZ=pt_BR-cadu-medium bash Tools/voz/gerar_vozes.sh   # outra voz
```

O texto de cada fala está em `Tools/voz/roteiro.tsv` (edite e rode de novo). Também dá para
gravar com a própria voz: um `.wav` por id em `Assets/Resources/Voz/`. Sem arquivo, a fala é
pulada e o estande funciona em silêncio.


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

**121 testes passando na última execução registrada neste README**, antes dos testes do PR do orientador (`AnastomosisTests`, `NameEntryTests`, `VesselAnastomosisVisualTests`) e dos 44 de `SkinStagesTests` (incisão, técnica do bisturi, nota, sutura, pele, etapas novas, correções do PR). Esses ainda precisam ser rodados no Editor. O que os testes cobrem e por quê:

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
- **Lâmina em pé** (sessão seguinte): o construtor descobre para que lado a lâmina do modelo
  está virada pela parte `filo` (eixo mais fino) e gira o modelo para ela ficar de pé na mão.
  O Console diz o ângulo, ou avisa que não conseguiu — aí a regra do fio fica desligada. Se no
  óculos a lâmina aparecer de cabeça para baixo, é só estética; se o corte recusar com o
  bisturi bem segurado, desligar `requireEdge` no `SkinIncisionWorker`. Os ~3 s até a ajuda
  adaptativa (`edgeHelpSeconds`) e os ~50° de tolerância são chutes a calibrar com visitantes.
- **Custo no Quest ainda não medido** do que mais pesa: tecido simulado (~1,7 mil vértices por
  quadro na CPU; o pano ainda lê 3 texturas a mais por pixel com a camada de detalhe — se a GPU
  apertar, tirar a linha `DressDetail` do "drapeBlue"), pele da janela recalculada enquanto o bisturi encosta, fumaça, e o foco com
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
