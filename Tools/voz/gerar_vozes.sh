#!/usr/bin/env bash
# Gera as falas da equipe (Assets/Resources/Voz/<id>.wav) a partir de roteiro.tsv,
# com o Piper (voz neural gratuita, roda offline depois de baixar a voz).
#
# Uso, na raiz do projeto:
#   bash Tools/voz/gerar_vozes.sh                 # voz padrão pt_BR-faber-medium
#   VOZ=pt_BR-cadu-medium bash Tools/voz/gerar_vozes.sh
#
# Precisa de Python 3 e internet na primeira vez (baixa o Piper e a voz, ~60 MB).
# Confira a licença da voz escolhida no cartão dela (huggingface.co/rhasspy/piper-voices).
set -euo pipefail

RAIZ="$(cd "$(dirname "$0")/../.." && pwd)"
VOZ="${VOZ:-pt_BR-faber-medium}"
MODELOS="$RAIZ/Tools/voz/modelos"
SAIDA="$RAIZ/Assets/Resources/Voz"
AMBIENTE="$RAIZ/Tools/voz/.venv"

mkdir -p "$MODELOS" "$SAIDA"

if [ ! -x "$AMBIENTE/bin/python" ]; then
  python3 -m venv "$AMBIENTE"
  "$AMBIENTE/bin/pip" install --quiet "piper-tts>=1.3"
fi
PY="$AMBIENTE/bin/python"

if [ ! -f "$MODELOS/$VOZ.onnx" ]; then
  "$PY" -m piper.download_voices "$VOZ" --download-dir "$MODELOS"
fi

while IFS=$'\t' read -r id texto; do
  [ -z "$id" ] && continue
  echo "  $id"
  printf '%s\n' "$texto" | "$PY" -m piper -m "$MODELOS/$VOZ.onnx" -f "$SAIDA/$id.wav" --sentence-silence 0.1
done < "$RAIZ/Tools/voz/roteiro.tsv"

echo "Pronto: $(ls "$SAIDA"/*.wav | wc -l) falas em $SAIDA."
echo "Volte para a Unity (ela importa sozinha) e faça commit dos .wav e dos .meta gerados."
