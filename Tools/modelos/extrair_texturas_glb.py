#!/usr/bin/env python3
"""Tira as imagens de dentro de um .glb e as grava como arquivos ao lado do modelo.

Por quê: o importador de glTF do Unity (glTFast) importa imagem embutida no .glb sempre
sem compressão. O coração anatômico, com 16 texturas de 2048 px, ocupava sozinho 341 MB no
APK e na memória de vídeo do Quest. Imagem em arquivo separado passa pelo importador de
texturas do Unity, que comprime (ASTC no Quest) e respeita o tamanho máximo.

O .glb é regravado apontando para os arquivos (images[i].uri) e sem os bytes das imagens,
para o repositório não guardar tudo em dobro. Malhas, materiais e animações não mudam.

Os arquivos recebem o nome <modelo>_<material>_<papel>.<ext>, e o papel diz ao Unity como
importar: baseColor (cor, sRGB), normal (normal map), metallicRoughness/occlusion (dados,
linear). Ver Assets/Editor/GlbTextureImportRules.cs.

Uso:  python3 Tools/modelos/extrair_texturas_glb.py Assets/Models/Patient/PATIENT_Heart_Anatomico.glb [...]
"""
import json
import os
import re
import struct
import sys

PAPEIS = [
    ("baseColor", lambda m: m.get("pbrMetallicRoughness", {}).get("baseColorTexture")),
    ("metallicRoughness", lambda m: m.get("pbrMetallicRoughness", {}).get("metallicRoughnessTexture")),
    ("normal", lambda m: m.get("normalTexture")),
    ("occlusion", lambda m: m.get("occlusionTexture")),
    ("emissive", lambda m: m.get("emissiveTexture")),
]
EXTENSAO = {"image/png": "png", "image/jpeg": "jpg"}


def limpo(texto):
    return re.sub(r"[^A-Za-z0-9]+", "_", texto).strip("_") or "sem_nome"


def ler_glb(caminho):
    dados = open(caminho, "rb").read()
    magia, versao, _ = struct.unpack("<4sII", dados[:12])
    if magia != b"glTF" or versao != 2:
        raise SystemExit(f"{caminho}: não é um .glb versão 2")
    pos, js, binario = 12, None, b""
    while pos < len(dados):
        tamanho, tipo = struct.unpack("<I4s", dados[pos:pos + 8])
        bloco = dados[pos + 8:pos + 8 + tamanho]
        if tipo == b"JSON":
            js = json.loads(bloco)
        elif tipo == b"BIN\x00":
            binario = bloco
        pos += 8 + tamanho
    return js, binario


def gravar_glb(caminho, js, binario):
    texto = json.dumps(js, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    texto += b" " * (-len(texto) % 4)
    binario += b"\x00" * (-len(binario) % 4)
    blocos = struct.pack("<I4s", len(texto), b"JSON") + texto
    if binario:
        blocos += struct.pack("<I4s", len(binario), b"BIN\x00") + binario
    with open(caminho, "wb") as saida:
        saida.write(struct.pack("<4sII", b"glTF", 2, 12 + len(blocos)) + blocos)


def nomes_das_imagens(js, modelo):
    papeis = {}
    for i, material in enumerate(js.get("materials", [])):
        for papel, achar in PAPEIS:
            textura = achar(material)
            if textura is not None:
                origem = js["textures"][textura["index"]]["source"]
                papeis.setdefault(origem, (limpo(material.get("name", f"material{i}")), papel))
    nomes = {}
    for i in range(len(js.get("images", []))):
        material, papel = papeis.get(i, (f"imagem{i}", "baseColor"))
        nomes[i] = f"{modelo}_{material}_{papel}"
    return nomes


def extrair(caminho):
    js, binario = ler_glb(caminho)
    imagens = js.get("images", [])
    embutidas = [i for i, im in enumerate(imagens) if "bufferView" in im]
    if not embutidas:
        print(f"{caminho}: nenhuma imagem embutida, nada a fazer")
        return

    pasta_modelo = os.path.dirname(caminho)
    modelo = os.path.splitext(os.path.basename(caminho))[0]
    relativa = f"Texturas/{modelo}"
    os.makedirs(os.path.join(pasta_modelo, relativa), exist_ok=True)
    nomes = nomes_das_imagens(js, modelo)
    views = js["bufferViews"]

    vistas_de_imagem = set()
    for i in embutidas:
        imagem = imagens[i]
        vista = views[imagem["bufferView"]]
        inicio = vista.get("byteOffset", 0)
        conteudo = binario[inicio:inicio + vista["byteLength"]]
        arquivo = f"{nomes[i]}.{EXTENSAO.get(imagem.get('mimeType'), 'png')}"
        with open(os.path.join(pasta_modelo, relativa, arquivo), "wb") as saida:
            saida.write(conteudo)
        vistas_de_imagem.add(imagem["bufferView"])
        imagens[i] = {"uri": f"{relativa}/{arquivo}", "mimeType": imagem.get("mimeType", "image/png")}
        if "name" in imagem:
            imagens[i]["name"] = imagem["name"]

    # Remonta o binário só com o que não é imagem, renumerando as bufferViews.
    novo_binario, novas_views, novo_indice = bytearray(), [], {}
    for indice, vista in enumerate(views):
        if indice in vistas_de_imagem:
            continue
        inicio = vista.get("byteOffset", 0)
        novo_binario += b"\x00" * (-len(novo_binario) % 16)
        nova = dict(vista)
        nova["byteOffset"] = len(novo_binario)
        novo_binario += binario[inicio:inicio + vista["byteLength"]]
        novo_indice[indice] = len(novas_views)
        novas_views.append(nova)

    for acessor in js.get("accessors", []):
        if "bufferView" in acessor:
            acessor["bufferView"] = novo_indice[acessor["bufferView"]]
        esparso = acessor.get("sparse")
        if esparso:
            esparso["indices"]["bufferView"] = novo_indice[esparso["indices"]["bufferView"]]
            esparso["values"]["bufferView"] = novo_indice[esparso["values"]["bufferView"]]
    for imagem in imagens:
        if "bufferView" in imagem:
            imagem["bufferView"] = novo_indice[imagem["bufferView"]]

    usadas = json.dumps(js.get("extensionsUsed", []))
    if "draco" in usadas.lower() or "meshopt" in usadas.lower():
        raise SystemExit(f"{caminho}: usa compressão de malha ({usadas}); renumerar bufferViews não é seguro aqui")

    js["bufferViews"] = novas_views
    js["buffers"][0]["byteLength"] = len(novo_binario)
    gravar_glb(caminho, js, bytes(novo_binario))
    print(f"{caminho}: {len(embutidas)} imagem(ns) em {relativa}/, "
          f"binário {len(binario) // 1024} KB -> {len(novo_binario) // 1024} KB")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    for argumento in sys.argv[1:]:
        extrair(argumento)
