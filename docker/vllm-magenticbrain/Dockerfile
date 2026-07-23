# syntax=docker/dockerfile:1
#
# vLLM + bitsandbytes for serving microsoft/MagenticBrain (14B) on a 16 GB GPU.
#
# The official vllm/vllm-openai image does NOT bundle bitsandbytes, which vLLM
# needs for in-flight 4-bit (NF4) quantization. A 14B model is ~28 GB in FP16 and
# will not fit on the RTX 3080 (16 GB); at 4-bit the weights are ~8-9 GB, leaving
# room for the KV cache at a capped context length.
#
# ENTRYPOINT is inherited from the base image: ["vllm", "serve"]. Pass the model
# id as the first positional argument followed by serve flags, e.g.:
#   docker run --rm --gpus all -p 8000:8000 <image> \
#     microsoft/MagenticBrain --quantization bitsandbytes \
#     --enable-auto-tool-choice --tool-call-parser hermes --max-model-len 16384
FROM vllm/vllm-openai:latest

RUN pip install --no-cache-dir bitsandbytes

# MagenticBrain is a Qwen3 hybrid that defaults to "thinking" mode (emits <think>...).
# Its model card calls for non-thinking inference. This template flips the default so
# the server is non-thinking unless a request explicitly sets enable_thinking=true,
# meaning OpenAI-compatible clients need no special per-request fields. Pass it with
# `--chat-template /config/chat_template_no_think.jinja`.
COPY chat_template_no_think.jinja /config/chat_template_no_think.jinja

LABEL org.opencontainers.image.description="vLLM OpenAI server with bitsandbytes for 4-bit MagenticBrain on 16GB GPUs"
