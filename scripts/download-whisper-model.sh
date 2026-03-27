#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# Whisper GGML modelini oldindan yuklab olish
# Birinchi foydalanuvchi ovoz yuborganida kutish vaqtini kamaytiradi.
#
# Ishlatish:
#   bash scripts/download-whisper-model.sh           # default: base model
#   bash scripts/download-whisper-model.sh tiny       # 75 MB, eng tez
#   bash scripts/download-whisper-model.sh base       # 142 MB, tavsiya ✅
#   bash scripts/download-whisper-model.sh small      # 466 MB, aniqroq
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

MODEL_TYPE="${1:-base}"
MODEL_DIR="/app/models"
MODEL_FILE="${MODEL_DIR}/whisper-${MODEL_TYPE}.bin"

# Hugging Face dan GGML model URL lari
declare -A MODEL_URLS=(
  ["tiny"]="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin"
  ["base"]="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"
  ["small"]="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin"
  ["medium"]="https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin"
)

if [[ -z "${MODEL_URLS[$MODEL_TYPE]+x}" ]]; then
  echo "❌ Noto'g'ri model turi: ${MODEL_TYPE}"
  echo "   Mumkin: tiny | base | small | medium"
  exit 1
fi

mkdir -p "$MODEL_DIR"

if [ -f "$MODEL_FILE" ]; then
  SIZE=$(du -sh "$MODEL_FILE" | cut -f1)
  echo "✅ Model allaqachon mavjud: ${MODEL_FILE} (${SIZE})"
  exit 0
fi

URL="${MODEL_URLS[$MODEL_TYPE]}"
echo "==> Whisper '${MODEL_TYPE}' modeli yuklanmoqda..."
echo "    Manzil : ${URL}"
echo "    Saqlash: ${MODEL_FILE}"

wget --progress=bar:force:noscroll \
     --output-document="$MODEL_FILE" \
     "$URL"

SIZE=$(du -sh "$MODEL_FILE" | cut -f1)
echo ""
echo "✅ Model tayyor: ${MODEL_FILE} (${SIZE})"
