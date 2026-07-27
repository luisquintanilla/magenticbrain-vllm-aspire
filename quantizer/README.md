# MagenticBrain Quantizer

Reproducible CLI for producing a bitsandbytes 4-bit MagenticBrain checkpoint that vLLM can load with `--quantization bitsandbytes`.

## Launch modes

- Development: `uv run quantize`
- Reproducible container: `python3 quantize.py` inside a prebuilt vLLM CUDA container that already includes `torch`, `transformers`, `bitsandbytes`, `accelerate`, and `huggingface_hub`

The CLI uses `argparse` and keeps ML imports lazy, so `python3 quantize.py --help` and up-to-date checks work without importing `torch`.

## Configuration

Each flag defaults from its environment variable; explicit CLI flags override env values.

| Flag | Env var | Default | Notes |
| --- | --- | --- | --- |
| `--model-id` | `MODEL_ID` | `microsoft/MagenticBrain` | Hugging Face model ID |
| `--quant-method` | `QUANT_METHOD` | `nf4` | `nf4` or `fp4` |
| `--dtype` | `QUANT_DTYPE` | `bfloat16` | `bfloat16` or `float16` |
| `--double-quant` / `--no-double-quant` | `DOUBLE_QUANT` | `true` | Enables/disables nested quantization |
| `--output-dir` | `OUTPUT_DIR` | `/out/MagenticBrain-bnb-nf4` | Output checkpoint directory |
| `--force` | `FORCE` | `false` | Rebuild even when output is current |

If `HF_TOKEN` or `HUGGING_FACE_HUB_TOKEN` is set, it is passed to Hugging Face model/tokenizer loading and revision resolution. The token is never printed.

## Manifest and idempotency

Before quantizing, the CLI computes a reproducibility signature from the model ID, resolved Hugging Face revision, quantization settings, and installed versions of `torch`, `transformers`, `bitsandbytes`, and `vllm` when available.

After quantization, it writes `manifest.json` containing the signature, creation timestamp, resolved revision, and SHA-256 checksums for produced files. On later runs, if `manifest.json` has the same signature and the checkpoint contains `config.json` plus at least one `*.safetensors` file, the CLI exits successfully without loading the model. Use `--force` to override.

## Example

```bash
uv run quantize \
  --model-id microsoft/MagenticBrain \
  --quant-method nf4 \
  --dtype bfloat16 \
  --output-dir /out/MagenticBrain-bnb-nf4
```
