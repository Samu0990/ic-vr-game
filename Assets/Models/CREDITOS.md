# Créditos e licenças dos modelos de terceiros

Este arquivo existe por obrigação legal, não por cortesia: a licença CC-BY exige atribuição ao
autor, indicação da licença, link para o original e aviso de que o material foi modificado.
Modelo de terceiros sem a linha correspondente aqui **não pode ser usado**.

Vale também para a defesa da IC: procedência declarada é a diferença entre "um coração que
achamos na internet" e "um modelo publicado pelo Education Resource Fund, sob CC-BY-4.0".

---

## PATIENT_Heart_Anatomico.glb

| | |
|---|---|
| **Título** | adult heart |
| **Autor** | Education Resource Fund — https://sketchfab.com/bobsmusail |
| **Fonte** | https://sketchfab.com/3d-models/adult-heart-9c6868474bc74a6ebb580b94853fd743 |
| **Licença** | CC-BY-4.0 — http://creativecommons.org/licenses/by/4.0/ |
| **Modificações** | Nenhuma até agora. Copiado como veio. Qualquer alteração futura deve ser anotada aqui |

Os metadados acima estão embutidos no próprio `.glb`, no campo `asset.extras` — não foram
inferidos. Dá para conferir abrindo o arquivo e lendo o cabeçalho JSON.

**Por que ele foi trazido:** o coração atual (`PATIENT_Heart.glb`) é gerado por IA, tem uma
malha única com um material só, e o README registra que os vasos não são segmentáveis — daí os
cinco pontos de anastomose serem transforms posicionados à mão. Este modelo vem separado por
estrutura anatômica:

| Material | Triângulos |
|---|---:|
| `Ventricles1` | 14.134 |
| `L_Atrium` | 11.166 |
| `R_Atrium` | 3.744 |
| `Aorta1` | 6.372 |
| `Artery` | 18.452 |
| `Veins` | 19.186 |
| **Total** | **73.054** |

Traz ainda 16 texturas e uma animação (`Take 001`).

**Ainda não está em uso em nenhuma cena.** Foi apenas adicionado ao projeto. Trocar o coração
atual por este mexe no construtor da cena, nos cinco pontos de anastomose e no assento
pericárdico — é tarefa à parte.

---

## Avaliados e NÃO trazidos

Registrado para ninguém repetir a busca.

| Arquivo | Onde | Por que não |
|---|---|---|
| `blood_vessel.glb` | Medical-Unity-VR | CC-BY-4.0, mas é corte didático de vaso com hemácias dentro — não é vaso anatômico |
| `charite_university_hospital_-_operating_room.glb` | Medical-Unity-VR | **CC-BY-NC** (não comercial) e 334.808 triângulos — sozinho estoura o orçamento do Quest |
| `VH_M_Liver.glb` | Medical-Unity-VR | Sem metadado de licença. Procedência desconhecida |
| `dentist_tools.glb` | Medical-Unity-VR | CC-BY-4.0, mas é odontologia |
| Malhas sob `Assets/SofaUnity/` | Medical-Unity-VR | Assets de exemplo do SofaUnity/SOFA. SOFA é LGPL-2.1 e o SofaUnity exige licença da InfinyTech3D para uso comercial; os meshes de exemplo não têm termos próprios declarados |

### Sobre o repositório Medical-Unity-VR em si

O repositório (`github.com/Ammar880121/Medical-Unity-VR`) **não tem arquivo de licença nem
menção a licença no README**. Pelo padrão legal, isso significa todos os direitos reservados, e
o código dele não pode ser reutilizado.

O que salvou os modelos foi cada `.glb` carregar a própria licença embutida, vinda do Sketchfab.
A licença é do autor original do modelo, não de quem montou aquele repositório — então a
atribuição correta é ao Education Resource Fund, e não ao dono do repositório.

**Regra prática daqui em diante:** antes de aproveitar qualquer modelo de terceiros, ler
`asset.extras` do `.glb`. Se não houver licença ali nem arquivo de licença no repositório de
origem, o modelo não entra.
