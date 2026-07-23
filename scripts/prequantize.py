#!/usr/bin/env python3
"""Pre-quantize microsoft/MagenticBrain to a bitsandbytes NF4 checkpoint.

vLLM can quantize to 4-bit "in-flight", but on a laptop CPU that takes ~28 min
*every* time the server starts. Doing it once and saving the quantized weights
lets vLLM load a ready-made 4-bit checkpoint in ~1-2 min on every subsequent run.

The saved checkpoint embeds a `quantization_config` in config.json plus the NF4
quant_state in the safetensors, so vLLM (with `--quantization bitsandbytes`)
detects it as pre-quantized and skips the slow runtime quantization.

Run it inside the custom vLLM image (which already has torch + transformers +
bitsandbytes); see scripts/prequantize.sh.
"""
import os
import sys

import torch
from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig

MODEL_ID = os.environ.get("MODEL_ID", "microsoft/MagenticBrain")
OUT_DIR = os.environ.get("OUT_DIR", "/out/MagenticBrain-bnb-nf4")

quant_config = BitsAndBytesConfig(
    load_in_4bit=True,
    bnb_4bit_quant_type="nf4",
    bnb_4bit_use_double_quant=True,
    bnb_4bit_compute_dtype=torch.bfloat16,
)

print(f"[prequantize] loading + NF4-quantizing {MODEL_ID} (this is the slow part)...", flush=True)
model = AutoModelForCausalLM.from_pretrained(
    MODEL_ID,
    quantization_config=quant_config,
    torch_dtype=torch.bfloat16,
    device_map={"": 0},
    low_cpu_mem_usage=True,
)

print(f"[prequantize] saving pre-quantized checkpoint to {OUT_DIR} ...", flush=True)
os.makedirs(OUT_DIR, exist_ok=True)
model.save_pretrained(OUT_DIR, safe_serialization=True)
AutoTokenizer.from_pretrained(MODEL_ID).save_pretrained(OUT_DIR)

print("[prequantize] DONE", flush=True)
sys.exit(0)
