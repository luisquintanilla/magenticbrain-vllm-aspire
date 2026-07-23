# magenticbrain-vllm-aspire

Run [`microsoft/MagenticBrain`](https://huggingface.co/microsoft/MagenticBrain) (14B, Qwen3-14B
based tool-orchestration model) **locally on GPU** with **vLLM (Docker)**, orchestrated by
**.NET Aspire**, behind the **.NET AI Chat Web App** (`aichatweb`) template with **Full RAG**.

> Staging experiment — unproven by default. See `~/dev/experiments/README.md`.

## Architecture

- **vLLM (GPU)** serves MagenticBrain at an OpenAI-compatible `/v1` endpoint (4-bit, tool-calling).
- **Ollama (CPU)** serves `nomic-embed-text` embeddings for RAG (keeps the GPU free for the 14B).
- **MarkItDown MCP** container ingests PDFs → markdown.
- **SqliteVec** local vector store; **Blazor** chat UI with citations.
- **.NET Aspire AppHost** orchestrates all of the above.

## Status

🚧 Work in progress — see the implementation plan and `docs/` (added as the build progresses).

## License

MIT
