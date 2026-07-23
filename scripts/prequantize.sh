#!/usr/bin/env bash
# Pre-quantize microsoft/MagenticBrain to a bitsandbytes NF4 checkpoint under ./models
# so that vLLM loads a ready-made 4-bit checkpoint (~1-2 min) instead of quantizing
# in-flight (~28 min) on every startup.
#
# One-time step. Requires the custom vLLM image built by docker/vllm-magenticbrain
# (tagged magenticbrain-vllm:local) and a GPU. Free the GPU first (stop any running
# vLLM container) — quantization needs the full 16 GB.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${VLLM_IMAGE:-magenticbrain-vllm:local}"
OUT_HOST="${REPO_ROOT}/models"
HF_CACHE="${HF_HOME:-$HOME/.cache/huggingface}"

mkdir -p "${OUT_HOST}"

echo "[prequantize] image=${IMAGE}  out=${OUT_HOST}/MagenticBrain-bnb-nf4"
docker run --rm --gpus all --ipc host \
  -e VLLM_WSL2_ENABLE_PIN_MEMORY=1 \
  -e MODEL_ID="microsoft/MagenticBrain" \
  -e OUT_DIR="/out/MagenticBrain-bnb-nf4" \
  -v "${HF_CACHE}:/root/.cache/huggingface" \
  -v "${OUT_HOST}:/out" \
  -v "${REPO_ROOT}/scripts:/scripts:ro" \
  --entrypoint python3 \
  "${IMAGE}" /scripts/prequantize.py

echo "[prequantize] checkpoint ready at ${OUT_HOST}/MagenticBrain-bnb-nf4"
