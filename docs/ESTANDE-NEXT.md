# Estande do NEXT — como montar e operar

O jogo roda **sozinho no Meta Quest**. O **notebook** ao lado do manequim roda a
**central do estande**, que recebe tudo do Quest pelo Wi-Fi:

```
  Meta Quest (jogo)  ──Wi-Fi──►  Notebook (central do estande)
                                   ├─ HDMI ──► Projetor, em cima do manequim: vista de cima do paciente
                                   ├─ DP/USB-C ► Monitor: ranking do dia + o que está acontecendo
                                   └─ teclado: nome de quem terminou a cirurgia
```

O nome pode ser digitado **no teclado do notebook** ou **no teclado do próprio
óculos**; os dois funcionam ao mesmo tempo. O que o operador digita no notebook vale
mais e fecha o teclado do óculos, para o visitante não digitar de novo.

---

## 1. Equipamento

- Meta Quest carregado, com o jogo instalado (APK).
- Notebook com a central (`Builds/CentralEstande_Linux/`) e carregador.
- Projetor + cabo HDMI. Suporte que deixe o projetor **acima** do manequim, apontando para baixo.
- Monitor para o ranking + cabo (DisplayPort ou USB-C, porque o HDMI vai para o projetor).
- Manequim do tronco para cima, deitado com a **cabeça à esquerda** de quem olha a imagem do projetor
  (a calibração gira a imagem se precisar).
- Extensão elétrica.

## 2. Rede

O Quest e o notebook precisam estar na **mesma rede**. Wi-Fi de evento (inclusive o da FIAP)
costuma isolar os aparelhos entre si, então use uma rede só do estande:

- **Hotspot do notebook** (Linux Mint): Configurações de Rede → Wi-Fi → menu ⋮ → *Ligar ponto
  de acesso Wi-Fi*. Ou no terminal, com a senha que vocês escolherem:
  ```bash
  nmcli dev wifi hotspot ifname wlp2s0 ssid EstandeVR password SUA_SENHA
  ```
  O notebook perde a internet enquanto o hotspot estiver ligado. A central não precisa de internet.
- **Ou o hotspot de um celular**, com o Quest e o notebook conectados nele.

No Quest: Configurações → Wi-Fi → conectar na rede do estande.

> A central acha o Quest sozinha: ela ouve o aviso que o jogo manda pela rede e, se não ouvir
> (o firewall do Mint bloqueia), varre a rede procurando o Quest. Não precisa digitar IP.
> Se mesmo assim não achar, veja o IP do Quest (Configurações → Wi-Fi → a rede → detalhes) e
> abra a central com `./iniciar_estande.sh -quest 10.42.0.23`.

## 3. Ligar

1. No Quest, abra o jogo.
2. No notebook:
   ```bash
   cd Builds/CentralEstande_Linux
   ./iniciar_estande.sh
   ```
   Abre duas janelas: **projetor** no monitor 2 e **ranking** no monitor 3 (o 1 é a tela do notebook).
   Se só houver um monitor extra: `MONITOR_PROJETOR=2 MONITOR_RANKING=1 ./iniciar_estande.sh`.
3. Em cada janela: **F9/F10** muda de monitor, **F11** tela cheia, **F1** ajuda.

O ranking mostra **• Óculos conectado** (verde) quando achou o Quest, e **• Procurando o óculos…** (laranja) enquanto não acha.

## 4. Calibrar o projetor (uma vez, na montagem)

Na janela do projetor:

| Tecla | Faz |
|---|---|
| **G** | padrão de teste: contorno da imagem, cruz no centro e a palavra CABEÇA |
| Setas | move a imagem (Shift: mais rápido) |
| **+ / −** | zoom (Shift: mais rápido) |
| **R** | gira 90° |
| **H / V** | espelha na horizontal / vertical |
| **0** | volta ao ajuste inicial |

A imagem mostra só o paciente do jogo, visto de cima, do peito à cabeça. Em volta fica preto,
que no projetor é luz apagada: a bancada não acende. Focos e arco de anestesia não aparecem.

Encaixe o contorno no manequim com a cabeça no lugar da cabeça. O ajuste é **salvo sozinho**
(`dados_estande/calibracao_projetor.json`) e volta na próxima vez que a central abrir.

## 5. Durante o evento

- **Uma dificuldade só**, com ajuda automática: quem fica para trás recebe ajuda,
  avisada na tela; quem vai bem não recebe nenhuma.
- Rodada de até **3 min 37 s**. Com instruções e troca de óculos, conte **10 a 15 pessoas por hora**.
- Ao fim de uma cirurgia **concluída** aparece o campo de nome no monitor do ranking:
  digite e **Enter**. **Esc** pula (fica o nome digitado no óculos, ou "Anônimo").
- Quem não termina a tempo não entra no ranking, mas a rodada fica nos dados da pesquisa.
- **F2** edita o ranking: ↑↓ escolhe, **R** renomeia, **Delete** duas vezes remove
  (para nome que não pode ficar numa tela pública).

## 6. Fim do dia e prêmio

O ranking **zera sozinho a cada dia**: o primeiro resultado depois da meia-noite começa um dia
novo. Fechar e abrir a central no meio do dia não apaga nada.

Cada dia fica em `dados_estande/ranking_AAAA-MM-DD.json`, com uma planilha `.csv` ao lado:
todas as rodadas (concluídas ou não), tempo, pontos, nome e etapa alcançada. É o dado da
pesquisa do IC e a referência para o prêmio do melhor tempo do dia.

## 7. Problemas comuns

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| "Procurando o óculos…" não sai | Quest e notebook em redes diferentes | Conectar os dois na rede do estande |
| Continua procurando na mesma rede | Rede de evento isolando aparelhos | Usar o hotspot do notebook ou do celular |
| Conectado, mas o projetor sem imagem | O jogo não está aberto no Quest | Abrir o jogo; a imagem só existe com ele rodando |
| Imagem de cabeça para baixo | Diferença do vídeo do Quest | **V** na janela do projetor |
| Janela no monitor errado | Ordem dos monitores do sistema | **F9/F10**, depois **F11** |

## 8. Gerar de novo

- **Cena do jogo:** Unity → menu `VRSurgery` → `Transplante — construir cena (nível único)`.
- **Central do notebook:** menu `Build` → `Central do estande — Linux 64` (ou Windows 64).
- **APK do Quest:** menu `Build` → `Transplante — APK do Quest`.
