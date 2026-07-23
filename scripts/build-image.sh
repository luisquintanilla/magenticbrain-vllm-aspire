#!/usr/bin/env bash
# Build the custom vLLM image used to serve microsoft/MagenticBrain.
#
# It layers two things onto the official vllm/vllm-openai image:
#   1. bitsandbytes  – enables 4-bit (NF4) quantization so the 14B model fits in 16 GB VRAM.
#   2. a non-thinking Qwen3 chat template – makes MagenticBrain answer directly instead of
#      emitting <think> ... </think> blocks by default.
#
# One-time step (re-run only if the Dockerfile or template change).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="${VLLM_IMAGE:-magenticbrain-vllm:local}"

echo "[build-image] building ${IMAGE} (this pulls the ~10 GB vllm-openai base on first run) ..."
docker build -t "${IMAGE}" "${REPO_ROOT}/docker/vllm-magenticbrain"
echo "[build-image] done: ${IMAGE}"
