"""Reproducible bitsandbytes quantization CLI for MagenticBrain.

This module supports two launch modes: local development via ``uv run quantize``
and a reproducible container path via ``python3 quantize.py`` inside a prebuilt
vLLM CUDA image. The container already provides the ML stack, while ``--help``
and idempotency checks must work even when it is absent. For that reason, only
Python standard library modules are imported at module import time; torch,
transformers, and other ML packages are imported lazily inside the functions
that need them.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from importlib import metadata
from pathlib import Path
from typing import Any


DEFAULT_MODEL_ID = "microsoft/MagenticBrain"
DEFAULT_OUTPUT_DIR = "/out/MagenticBrain-bnb-nf4"
MANIFEST_NAME = "manifest.json"


@dataclass(frozen=True)
class QuantizerConfig:
    model_id: str
    quant_method: str
    dtype: str
    double_quant: bool
    output_dir: Path
    force: bool


def parse_env_bool(name: str, default: bool) -> bool:
    value = os.environ.get(name)
    if value is None or value == "":
        return default

    normalized = value.strip().lower()
    if normalized in {"1", "true", "t", "yes", "y", "on"}:
        return True
    if normalized in {"0", "false", "f", "no", "n", "off"}:
        return False
    raise ValueError(
        f"Environment variable {name} must be a boolean "
        "(true/false, 1/0, yes/no, on/off)."
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Quantize a Hugging Face causal LM to bitsandbytes 4-bit format.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument(
        "--model-id",
        default=os.environ.get("MODEL_ID", DEFAULT_MODEL_ID),
        help="Hugging Face model ID. Env: MODEL_ID.",
    )
    parser.add_argument(
        "--quant-method",
        default=os.environ.get("QUANT_METHOD", "nf4"),
        choices=("nf4", "fp4"),
        help="bitsandbytes 4-bit quantization type. Env: QUANT_METHOD.",
    )
    parser.add_argument(
        "--dtype",
        default=os.environ.get("QUANT_DTYPE", "bfloat16"),
        choices=("bfloat16", "float16"),
        help="4-bit compute dtype. Env: QUANT_DTYPE.",
    )

    double_quant_default = parse_env_bool("DOUBLE_QUANT", True)
    double_quant_group = parser.add_mutually_exclusive_group()
    double_quant_group.add_argument(
        "--double-quant",
        dest="double_quant",
        action="store_true",
        default=double_quant_default,
        help="Enable nested/double quantization. Env: DOUBLE_QUANT.",
    )
    double_quant_group.add_argument(
        "--no-double-quant",
        dest="double_quant",
        action="store_false",
        help="Disable nested/double quantization. Env: DOUBLE_QUANT.",
    )

    parser.add_argument(
        "--output-dir",
        default=os.environ.get("OUTPUT_DIR", DEFAULT_OUTPUT_DIR),
        help="Output checkpoint directory. Env: OUTPUT_DIR.",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        default=parse_env_bool("FORCE", False),
        help="Re-quantize even if the output is already up to date. Env: FORCE.",
    )
    return parser


def build_config(args: argparse.Namespace) -> QuantizerConfig:
    return QuantizerConfig(
        model_id=args.model_id,
        quant_method=args.quant_method,
        dtype=args.dtype,
        double_quant=args.double_quant,
        output_dir=Path(args.output_dir),
        force=args.force,
    )


def get_hf_token() -> str | None:
    return os.environ.get("HF_TOKEN") or os.environ.get("HUGGING_FACE_HUB_TOKEN")


def package_version(package_name: str) -> str | None:
    try:
        return metadata.version(package_name)
    except metadata.PackageNotFoundError:
        return None


def resolve_hf_revision(model_id: str, token: str | None = None) -> str | None:
    try:
        from huggingface_hub import HfApi  # type: ignore[import-not-found]

        kwargs: dict[str, Any] = {}
        if token:
            kwargs["token"] = token
        return HfApi().model_info(model_id, **kwargs).sha
    except Exception:
        return None


def compute_signature(config: QuantizerConfig, token: str | None = None) -> dict[str, Any]:
    hf_revision = resolve_hf_revision(config.model_id, token=token)
    return {
        "hf_revision": hf_revision,
        "model_id": config.model_id,
        "quant_method": config.quant_method,
        "dtype": config.dtype,
        "double_quant": config.double_quant,
        "library_versions": {
            "torch": package_version("torch"),
            "transformers": package_version("transformers"),
            "bitsandbytes": package_version("bitsandbytes"),
            "vllm": package_version("vllm"),
        },
    }


def checkpoint_looks_complete(output_dir: Path) -> bool:
    return (output_dir / "config.json").is_file() and any(output_dir.glob("*.safetensors"))


def is_up_to_date(output_dir: Path, signature: dict[str, Any]) -> bool:
    manifest_path = output_dir / MANIFEST_NAME
    if not manifest_path.is_file() or not checkpoint_looks_complete(output_dir):
        return False

    try:
        with manifest_path.open("r", encoding="utf-8") as manifest_file:
            manifest = json.load(manifest_file)
    except (OSError, json.JSONDecodeError):
        return False

    return manifest.get("signature") == signature


def run_quantization(config: QuantizerConfig, token: str | None = None) -> None:
    print(f"Loading model {config.model_id!r} for {config.quant_method} quantization...", flush=True)

    import torch  # type: ignore[import-not-found]
    from transformers import AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig  # type: ignore[import-not-found]

    torch_dtype = {"bfloat16": torch.bfloat16, "float16": torch.float16}[config.dtype]
    quant_config = BitsAndBytesConfig(
        load_in_4bit=True,
        bnb_4bit_quant_type=config.quant_method,
        bnb_4bit_use_double_quant=config.double_quant,
        bnb_4bit_compute_dtype=torch_dtype,
    )

    pretrained_kwargs: dict[str, Any] = {}
    if token:
        pretrained_kwargs["token"] = token

    model = AutoModelForCausalLM.from_pretrained(
        config.model_id,
        quantization_config=quant_config,
        torch_dtype=torch_dtype,
        device_map={"": 0},
        low_cpu_mem_usage=True,
        **pretrained_kwargs,
    )

    config.output_dir.mkdir(parents=True, exist_ok=True)
    print(f"Saving quantized checkpoint to {config.output_dir}...", flush=True)
    model.save_pretrained(config.output_dir, safe_serialization=True)

    print("Saving tokenizer...", flush=True)
    AutoTokenizer.from_pretrained(config.model_id, **pretrained_kwargs).save_pretrained(config.output_dir)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def collect_file_hashes(output_dir: Path) -> dict[str, str]:
    files: dict[str, str] = {}
    for path in sorted(output_dir.rglob("*")):
        if not path.is_file() or path.name == MANIFEST_NAME:
            continue
        relative_path = path.relative_to(output_dir).as_posix()
        files[relative_path] = sha256_file(path)
    return files


def write_manifest(output_dir: Path, signature: dict[str, Any]) -> Path:
    output_dir.mkdir(parents=True, exist_ok=True)
    manifest = {
        "signature": signature,
        "created_utc": datetime.now(timezone.utc).replace(microsecond=0).isoformat(),
        "hf_revision": signature.get("hf_revision"),
        "files": collect_file_hashes(output_dir),
    }
    manifest_path = output_dir / MANIFEST_NAME
    with manifest_path.open("w", encoding="utf-8") as manifest_file:
        json.dump(manifest, manifest_file, indent=2, sort_keys=True)
        manifest_file.write("\n")
    return manifest_path


def main() -> int:
    try:
        parser = build_parser()
        args = parser.parse_args()
        config = build_config(args)
        token = get_hf_token()

        print("Computing reproducibility signature...", flush=True)
        signature = compute_signature(config, token=token)

        if not config.force and is_up_to_date(config.output_dir, signature):
            print(
                f"{config.output_dir} is up-to-date, skipping (use --force to override).",
                flush=True,
            )
            return 0

        run_quantization(config, token=token)
        manifest_path = write_manifest(config.output_dir, signature)
        print(f"Wrote reproducibility manifest: {manifest_path}", flush=True)
        print("Quantization complete.", flush=True)
        return 0
    except KeyboardInterrupt:
        print("Interrupted.", file=sys.stderr, flush=True)
        return 130
    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr, flush=True)
        return 1


if __name__ == "__main__":
    sys.exit(main())
