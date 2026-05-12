# BhenguAI

On-device AI library for the TheGeekNetwork ecosystem. Internal codename: Butler / B!.

> **NuGet note:** 2.0.0 is the last package published under the `Bhengu.AI.*` namespace.
> New packages ship as `Circle.AI.*` from **[CircleAI](https://github.com/bhengubv/CircleAI)**.
> This repo continues to receive updates alongside CircleAI.

Runs Qwen 3 14B (Alibaba, MIT licensed) locally via llama.cpp. Designed to fit on a Xiaomi 12 Pro: under 15 GB on disk, under 4 GB RAM in use, fully offline.

## Why local?
- Sovereignty: no dependency on US or EU clouds.
- Privacy: nothing leaves the device.
- Resilience: works under sanctions, blackouts, or roaming.
- Cost: zero per-token charges.

## Components (12 projects)
- **Bhengu.AI.Core** — model registry, downloader (ModelScope primary, HuggingFace fallback), local cache.
- **Bhengu.AI.Inference** — llama.cpp P/Invoke layer. Qwen chat generator.
- **Bhengu.AI.Embeddings** — local embeddings for semantic search.
- **Bhengu.AI.Search** — vector search over local data.
- **Bhengu.AI.Memory** — affect state, episodic memory, context windows.
- **Bhengu.AI.Skills** — skill dispatch and execution pipeline.
- **Bhengu.AI.Tools** — function-calling bridge to the 36 TheGeekNetwork APIs.
- **Bhengu.AI.Voice** — TTS/STT abstraction layer.
- **Bhengu.AI.Hosting** — DI wiring, scheduled tasks, proactive reasoning.
- **Bhengu.AI.Maui** — MAUI platform adapter.
- **Bhengu.AI.Aether** — Aether mesh-network telemetry contracts.
- **Bhengu.AI.Security** — AI Security Layer: trust scoring, threat detection, security directives.

## Quick start

See `SETUP.md` for native binary acquisition. See `TODO.md` for what's not yet done.

## Status
Foundational build around Qwen 3 14B. Native llama.cpp binaries to be added per platform. PowerInfer-2 expert streaming integration to follow.
