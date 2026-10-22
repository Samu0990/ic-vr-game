# Projeto: jogo de cirurgia VR para o Next
- Unity 6000.3.22f1, URP, XR Interaction Toolkit 3.5.1, OpenXR com Meta Quest
- Alvo: PC Windows com o Quest ligado por cabo (Meta Horizon Link). Uma build só, para Windows
  (menu Build → "Meta Health — Windows"). Display 1 = monitor virado para a fila, Display 2 =
  projetor no manequim, Display 3 opcional. Manter 72–90 fps estáveis no óculos
- Desenvolvo no Linux Mint. O Link só é testado no PC Windows do laboratório
  (passo a passo em docs/CHECKLIST-PC-LINK.md)
- A cena TransplanteCardiaco é gerada pelo construtor (Assets/Editor/TransplanteSceneBuilder*.cs)
  em 3 níveis: Fácil, Médio e Difícil (menu VRSurgery). A build usa o último nível escolhido;
  padrão Médio. A ajuda adaptativa (PaceAssist) vale nos três
- Procedimentos são definidos por dados (SurgeryDefinition/ObjectiveDefinition)

## Regras
- Uma tarefa por vez. Não mude nada fora do escopo pedido
- Não altere Project Settings sem eu pedir explicitamente
- Rode os testes existentes (Assets/Tests) depois de mudar código
- Todo .cs novo precisa do seu .meta
- Termine explicando em 3 linhas, em português, o que mudou
- Se algo for arriscado ou ambíguo, pergunte antes de fazer
