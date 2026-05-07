# Loki Mode Working Memory
Last Updated: 2026-05-07
Current Phase: development
Current Iteration: 1

## Active Goal
Pivot BhenguAI from Phi-3 (US/Microsoft) to Qwen 3 14B (China, sanctions-safe) for use as Butler/B! AI in TheGeekNetwork ecosystem. Reuse existing scaffolding (downloader, registry, search), replace stub inference layer with llama.cpp wiring, add tool-use bridge for 36 APIs.

## Current Task
- ID: butler-pivot-001
- Description: Restructure repo for Qwen + llama.cpp + ModelScope
- Status: in-progress
- Started: 2026-05-07
- Branch: butler-pivot

## Work Groups (Parallel)
- A: Registry + Downloader + Source abstraction
- B: Inference project + Qwen text generator + llama.cpp interop
- C: Tools project (function-calling bridge to 36 APIs)
- D: Engine fix + docs + sample + build props

## Constraints
- No US/EU/ally model references
- No Phi-3, Microsoft, Google, Meta, IBM, Apple model deps
- No Google Play, no Apple MLX runtime
- Net9.0 target (matches TheGeekNetwork stack)
- Don't push to remote
- Don't download multi-GB model files

## Key Decisions
- Public name: BhenguAI. Internal: Butler/B!.
- Primary model: Qwen 3 14B (Q4_K_M GGUF, ~8 GB)
- Upgrade path: Qwen 3.6 35B-A3B at Q3 (~14 GB)
- Runtime: llama.cpp (foundation), PowerInfer-2 to layer in later
- Primary source: ModelScope (Alibaba). Fallback: HuggingFace
- Distribution: APK-direct
