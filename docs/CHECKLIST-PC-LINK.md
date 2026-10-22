# Checklist do laboratório: Meta Health no PC Windows + Quest no cabo

Para testar a build do estande no PC Windows do laboratório, com o Quest ligado por cabo
(Meta Horizon Link), o monitor virado para a fila e o projetor sobre o manequim.

## Antes de ir

- [ ] Gerar a build no Linux: menu `Build → Meta Health — Windows (PC + Quest no cabo)`
      (ou `-executeMethod VRSurgery.EditorTools.TransplantBuildScript.BuildWindowsFromCommandLine`).
      Sai em `Builds/Windows/`. O nível é o último escolhido em `VRSurgery → Transplante — nível …`
      (padrão: Médio); o Console diz qual nível foi.
- [ ] Copiar a pasta `Builds/Windows/` **inteira** para um pendrive (o `.exe` sozinho não roda).
      A subpasta `Meta Health_BurstDebugInformation_DoNotShip` não precisa ir.
- [ ] Levar um cabo USB-C **de dados** (USB 3 de preferência). Cabo só de carga não funciona.

## No PC Windows

1. [ ] App **Meta Horizon Link** instalado e aberto, logado na conta do óculos.
2. [ ] No app: **Configurações → Geral → Runtime OpenXR**: Meta Horizon Link como runtime
       **ativo** (botão "Definir como ativo"). Sem isso o jogo abre sem nada no óculos.
3. [ ] Windows em **Estender** (tecla `Win+P` → Estender), com o projetor ligado.
       Tela principal = monitor da fila (Display 1); projetor = Display 2.
       Um terceiro monitor (opcional) mostra o relógio grande para a plateia (Display 3).
4. [ ] Teste de cabo do app (Dispositivos → o óculos → Teste de conexão USB) sem aviso.

## No óculos

5. [ ] **Quest Link ligado** (Configurações → Sistema → Quest Link), cabo conectado,
       aceitar a conexão. O óculos deve mostrar o ambiente do Link.

## Rodando

6. [ ] Abrir `MetaHealth.exe` (pasta copiada). Se o Windows perguntar, permitir.
7. [ ] Conferir as três telas:
       - óculos: a sala de cirurgia, mãos de luva;
       - monitor: a visão do cirurgião com tempo, pontos, etapa e os ±segundos;
       - projetor: só o corpo do paciente, fundo preto, sem mesa e sem texto.
8. [ ] **Calibrar o projetor** com o manequim (tecla `C`): aparece um contorno do corpo e uma grade
       de 10 cm. Setas movem, `+` e `−` mudam a escala, `[` e `]` giram, `X` e `Y` espelham,
       `Enter` **salva**. `C` sai. A calibração fica em
       `%USERPROFILE%\AppData\LocalLow\Meta Health\Meta Health\calibracao_projetor.json`
       e é lida sozinha na próxima abertura.
9. [ ] Outras teclas do operador (`H` mostra todas no monitor):
       `R` recentraliza a mesa em quem está de óculos · `N` encerra a vez ·
       `↑`/`↓` altura do visitante ±2 cm (só fora da rodada) · `P` troca o monitor entre a visão
       do cirurgião e a de cima.

## Se der errado

- **Óculos preto, jogo rodando no monitor:** o runtime OpenXR ativo não é o Link (passo 2), ou o
  Quest Link não está ligado (passo 5). Fechar o jogo, corrigir, abrir de novo.
- **Projetor mostra o mesmo que o monitor:** o Windows está em "Duplicar". `Win+P → Estender` e
  reabrir o jogo. Sem segunda tela, `P` escolhe o que aparece na única tela.
- **Imagem do projetor torta ou espelhada:** calibrar de novo com `C` e salvar com `Enter`.
- **Mãos/controle não aparecem:** conferir se os controles estão ligados e pareados no óculos.

## O que anotar para mim

- O Quest da faculdade é 2 ou 3? (Os dois perfis de controle estão ligados.)
- FPS/engasgos no óculos durante a serra e a desfibrilação.
- Se o contorno do teste de calibração bate com o manequim depois de ajustado.
