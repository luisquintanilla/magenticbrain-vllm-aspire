#!/usr/bin/env python3
"""Generate docs/rag-flow.svg — a sequence diagram of the grounded-RAG tool loop.
Kept in the repo so the diagram can be regenerated/tweaked deterministically."""
import os

W = 1000
Y0 = 118          # first message y
STEP = 34         # vertical spacing between messages
PAD_BOTTOM = 40

# participants: id -> (label, sublabel, x_center, half_width, fill, stroke)
P = {
    "user":  ("User",        "browser",        78,  48, "#ECEFF1", "#37474F"),
    "web":   ("Blazor Web",  "IChatClient",    250, 78, "#EEE9FC", "#512BD4"),
    "vllm":  ("MagenticBrain","vLLM · GPU",    460, 92, "#EEF7E0", "#4C8C1B"),
    "mid":   ("MarkItDown",  "MCP",            650, 66, "#FBF3DE", "#B8860B"),
    "olla":  ("Ollama",      "embeddings",     800, 62, "#E7F0FB", "#1565C0"),
    "vec":   ("SqliteVec",   "vector store",   935, 60, "#EEF1F4", "#5F6B7A"),
}

# messages: (from, to, label, kind)  kind: call|return
M = [
    ("user", "web",  "asks: “what does the water filter remove?”", "call"),
    ("web",  "vllm", "chat + tools  [ LoadDocuments , Search ]",          "call"),
    ("vllm", "web",  "① tool call:  LoadDocuments()",                     "return"),
    ("web",  "mid",  "PDF → Markdown",                                    "call"),
    ("web",  "olla", "embed chunks → 768-dim",                           "call"),
    ("web",  "vec",  "store vectors",                                     "call"),
    ("web",  "vllm", "tool result:  documents ingested",                 "call"),
    ("vllm", "web",  "② tool call:  Search(query)",                      "return"),
    ("web",  "olla", "embed query → 768-dim",                            "call"),
    ("web",  "vec",  "top-k similar chunks",                             "call"),
    ("web",  "vllm", "tool result:  retrieved context",                 "call"),
    ("vllm", "web",  "grounded answer  +  <citation>",                   "return"),
    ("web",  "user", "render answer with citation",                      "call"),
]

# shaded phase bands: (first_msg_index, last_msg_index, label, fill)
BANDS = [
    (3, 6, "①  first-run ingestion (lazy)", "#FCF6E9"),
    (8, 10, "②  retrieval", "#EAF2FC"),
]

n = len(M)
header_top, header_h = 40, 46
life_top = header_top + header_h
life_bottom = Y0 + (n - 1) * STEP + 24
height = life_bottom + PAD_BOTTOM

def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")

out = []
out.append(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {height}" '
           f'font-family="system-ui, -apple-system, \'Segoe UI\', Roboto, Arial, sans-serif">')
out.append('<defs><style>'
           '.ttl{font-size:22px;font-weight:700;fill:#141a21}'
           '.sub{font-size:13px;fill:#5a6672}'
           '.pt{font-size:13.5px;font-weight:700}'
           '.ps{font-size:10.5px;fill:#6b7480}'
           '.msg{font-size:11.5px;font-weight:600;fill:#2b3641}'
           '.band{font-size:11px;font-weight:700;fill:#8a7a3d}'
           '.band2{font-size:11px;font-weight:700;fill:#3f6ea5}'
           '</style>'
           '<marker id="a" viewBox="0 0 10 10" refX="8.5" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">'
           '<path d="M0,0 L10,5 L0,10 z" fill="#455A64"/></marker>'
           '<filter id="ds" x="-20%" y="-20%" width="140%" height="140%">'
           '<feDropShadow dx="0" dy="1.5" stdDeviation="2" flood-color="#0b1a33" flood-opacity="0.13"/></filter>'
           '</defs>')
out.append(f'<rect x="0" y="0" width="{W}" height="{height}" fill="#ffffff"/>')

# title
out.append('<text x="500" y="30" text-anchor="middle" class="ttl">How a grounded answer is produced — the RAG tool loop</text>')

# phase bands (draw first, behind)
for (a, b, label, fill) in BANDS:
    y_top = Y0 + a * STEP - 22
    y_bot = Y0 + b * STEP + 12
    out.append(f'<rect x="150" y="{y_top}" width="820" height="{y_bot - y_top}" rx="8" fill="{fill}"/>')
    cls = "band" if fill.startswith("#FCF") else "band2"
    out.append(f'<text x="163" y="{y_top + 15}" class="{cls}">{esc(label)}</text>')

# lifelines + headers
for pid, (label, sub, x, hw, fill, stroke) in P.items():
    out.append(f'<line x1="{x}" y1="{life_top}" x2="{x}" y2="{life_bottom}" stroke="#c3ccd4" stroke-width="1.3" stroke-dasharray="3 4"/>')
    out.append(f'<rect x="{x-hw}" y="{header_top}" width="{2*hw}" height="{header_h}" rx="10" fill="{fill}" stroke="{stroke}" stroke-width="1.8" filter="url(#ds)"/>')
    out.append(f'<text x="{x}" y="{header_top+20}" text-anchor="middle" class="pt" fill="{stroke}">{esc(label)}</text>')
    out.append(f'<text x="{x}" y="{header_top+35}" text-anchor="middle" class="ps">{esc(sub)}</text>')

# messages
for i, (fa, ta, label, kind) in enumerate(M):
    y = Y0 + i * STEP
    x1 = P[fa][2]
    x2 = P[ta][2]
    dash = ' stroke-dasharray="6 4"' if kind == "return" else ''
    # arrow line
    out.append(f'<line x1="{x1}" y1="{y}" x2="{x2}" y2="{y}" stroke="#455A64" stroke-width="1.7"{dash} marker-end="url(#a)"/>')
    # label centered on the segment, above the line, on a white pill
    mid = (x1 + x2) / 2
    w = len(label) * 6.15 + 14
    out.append(f'<rect x="{mid - w/2:.1f}" y="{y-19}" width="{w:.1f}" height="16" rx="8" fill="#ffffff" stroke="#d3dae1"/>')
    out.append(f'<text x="{mid:.1f}" y="{y-7}" text-anchor="middle" class="msg">{esc(label)}</text>')

out.append('</svg>')

os.makedirs("docs", exist_ok=True)
with open("docs/rag-flow.svg", "w") as f:
    f.write("\n".join(out) + "\n")
print(f"wrote docs/rag-flow.svg  ({W}x{height})")
