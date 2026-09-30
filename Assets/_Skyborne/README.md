# Skyborne — voo estilo Superman + agarrão de NPC

Projeto **separado** do jogo de cirurgia VR. Não é VR, é primeira pessoa com teclado/mouse.
Vive inteiro dentro de `Assets/_Skyborne/` e não referencia nada do `VRSurgery` nem do XR —
são assemblies próprios (`Skyborne.Runtime`, `Skyborne.Editor`, `Skyborne.Tests`), então dá pra
apagar a pasta inteira sem quebrar a cirurgia.

## Como rodar

1. Menu do Unity: **Skyborne → Construir cena de teste**
2. Aperte **Play**

A cena é gerada por código (`SkybornePlaygroundBuilder`), então é reproduzível e nada precisa
ser ligado na mão no Inspector.

## Controles

| Tecla | Ação |
|---|---|
| Mouse | olhar / mirar |
| W | voar pra frente (na direção que você olha) |
| Shift | turbo |
| S | freio aéreo |
| A / D | desviar (ou transladar, pairando) |
| Espaço / Ctrl | subir / descer |
| Clique esq. (ou E) | agarrar / soltar |
| Clique dir. (ou Q) | segurar pra carregar força, soltar pra arremessar |

Soltar o W = pairar. Pairar é o estado de descanso, não cair.

## O que faz o voo parecer voo

`FlightModel` é função pura (sem MonoBehaviour), então dá pra testar sem entrar em Play:

- **Arrasto anisotrópico**: quadrático e diferente por eixo do corpo. Baixo no eixo de frente
  (aerodinâmico, você mantém velocidade), alto de lado (mata a derrapagem, a curva "pega").
- **O corpo atrasa em relação à câmera**: a taxa de giro cai com a velocidade. Isso gera
  derrapagem real, que o arrasto lateral depois consome — é isso que faz uma curva parecer curva
  em vez de um giro instantâneo.
- **Curva coordenada**: o corpo inclina (roll) proporcional ao empuxo lateral.
- **Velocidade terminal emerge da física**: empuxo = arrasto. Não tem clamp de velocidade fazendo
  o trabalho (o `maxSpeed` é só rede de segurança).

Câmera (`FlightCameraRig`): FOV abre com a velocidade, a cabeça atrasa sob aceleração, e acima de
90 m/s entra o tremor do vento (Perlin, contínuo — não é jitter aleatório).

## O que faz o agarrão parecer agarrão

O ponto central: **agarrar é uma restrição física, não um re-parent.** O corpo agarrado pendura
num `ConfigurableJoint` ligado ao Rigidbody do jogador, então as forças vão nos dois sentidos.

- **O braço realmente viaja** até o osso escolhido (`GrabState.Reaching`). Nada é teleportado
  pra sua mão.
- **Escolha de pegada** por osso, com viés anatômico: o tórax ganha de um punho que está mais
  perto, porque é por ali que uma pessoa pega outra. Pegada no tórax quase não balança; pegada no
  punho deixa o corpo inteiro pendular embaixo de você.
- **Absorção de impacto**: a mola da junta sobe de `catchSpring` pra `holdSpring` em ~0,25 s. É
  isso que impede que pegar alguém a 40 m/s estale o corpo — funciona como braço de verdade.
- **A massa entra no voo**: `loadFactor = 1 + (massa/massa) * acoplamento`. O empuxo é dividido
  por isso, então carregar um adulto realmente deixa você lento pra acelerar e pra subir.
- **A vítima briga de verdade**: `NpcVictim` dirige os `targetRotation` das juntas com ruído
  Perlin (cada membro com fase própria, então se contorce em vez de fazer polichinelo) e chuta
  impulsos reais no ragdoll. O solver passa isso pra você — carregar alguém que se debate sacode.
- **Estamina**: 7 s de briga em força máxima e o corpo se esgota, o tônus muscular cai e ele
  amolece. Recupera no chão. Bater a pessoa na parede também custa estamina; a 14 m/s apaga.
- **A pegada arrebenta**: `breakForce` escalado pela qualidade da pegada. Bata o corpo numa torre
  e ele sai da sua mão.
- **Momento se conserva na soltura**: soltar não escreve velocidade nenhuma — a junta já entregou
  o momento do carregador, então o corpo continua exatamente como estava. O arremesso só soma
  impulso em cima (`impulso = massa * delta-v`, então as constantes se leem em m/s).

## O humanoide

`HumanoidRagdollFactory` monta o corpo por código: proporções de um corpo de 1,75 m (alturas em
metros, conferíveis contra um esqueleto real), distribuição de massa estilo Dempster, e limites de
junta que um ombro, cotovelo, quadril e joelho de verdade têm — cotovelo e joelho são dobradiça e
não hiperextendem.

**O visual é geometria primitiva, não um personagem esculpido.** Isso é um stand-in consciente: o
que o sistema de agarrão precisa de um corpo é comprimento de segmento, massa e limite de junta
corretos, e esses estão exatos. Troque os filhos `Visual` por uma malha com skin e nada da física
muda.

### Por que não veio malha gerada por IA

Os providers `tripo` e `meshy` do plugin MCP for Unity estão com `configured: false` — sem API key
no Editor não dá pra gerar. E vale saber: esses geradores entregam malha estática **sem rig**, que
não ragdolla nem agarra sem riggar depois. O uso melhor deles aqui seria cenário/prédios, onde não
ter rig não importa.

## Testes

`Assets/_Skyborne/Tests/FlightModelTests.cs` — 15 testes EditMode no solver: empuxo, divisão por
carga, anisotropia do arrasto, freio aéreo, hover cancelando gravidade, velocidade terminal,
clamp do banking, e proteção contra NaN em direção degenerada.
