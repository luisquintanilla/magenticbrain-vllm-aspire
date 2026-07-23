#!/usr/bin/env bash
# Run the full local stack with .NET Aspire:
#   vLLM (GPU, MagenticBrain 4-bit) + Ollama (CPU embeddings) + MarkItDown MCP + Blazor chat UI.
#
# This uses `dotnet run` on the AppHost with the HTTP launch profile, which is the
# reliable path on WSL2: it avoids the ASP.NET dev-certificate HTTPS bind that fails
# on many WSL2 setups. (`aspire run` also works once the Aspire CLI is >= 13.4.)
#
# Prereqs (see README): custom image built (scripts/build-image.sh) and, ideally, the
# pre-quantized checkpoint created (scripts/prequantize.sh) for ~10s model loads.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${REPO_ROOT}"

# WSL2 dev-cert HTTPS binding is unreliable; the http launch profile plus this flag
# keep the dashboard and resource endpoints on plain HTTP for local development.
export ASPIRE_ALLOW_UNSECURED_TRANSPORT="${ASPIRE_ALLOW_UNSECURED_TRANSPORT:-true}"

echo "[run] starting AppHost (http profile). Dashboard + Web URLs will print below."
exec dotnet run --project MagenticBrainRag.AppHost --launch-profile http "$@"
