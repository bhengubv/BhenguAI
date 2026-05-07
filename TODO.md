# BhenguAI TODO

## Native binaries
- [ ] Provide a build pipeline for llama.cpp natives (Win x64, Linux x64, macOS arm64, Android arm64, iOS arm64).
- [ ] Set up automated CI to refresh binaries on llama.cpp releases.

## Runtime upgrades
- [ ] Layer in PowerInfer-2 hot/cold expert scheduling for the Qwen 3.6 35B-A3B sparse MoE path.
- [ ] Implement expert streaming from UFS storage with predictive prefetch.
- [ ] Add KV-cache compression for long contexts.
- [ ] NPU offload for Snapdragon Hexagon (the 12 Pro's chip).

## Compression
- [ ] Document the path to a 13 GB target via REAP-style expert pruning + structured sparsity for the 35B-A3B model.
- [ ] Add a tooling project that consumes a full-precision GGUF and outputs a pruned + sparsified variant.

## Tooling
- [ ] Wire HttpToolBridge to actual TheGeekNetwork API clients.
- [ ] Generate tool manifests from API OpenAPI schemas automatically.
- [ ] Add per-tool authorisation guard (Butler shouldn't be able to invoke arbitrary endpoints — must respect user roles).

## Distribution
- [ ] Build APK-direct delivery for Android (no Google Play dependency).
- [ ] Add side-channel update mechanism for model file refresh.

## Sanctions resilience
- [ ] Mirror Qwen GGUF files on a sovereign-controlled CDN as additional fallback.
- [ ] Add Aether mesh network as a final tier ("download from a peer").

## Quality
- [ ] **PRODUCTION BLOCKER** — Real checksums on registry entries (currently `sha256:TBD` in `src/Bhengu.AI.Core/registry.json`).
      Both `Qwen3-14B-Q4` and `Qwen3.6-35B-A3B-Q3` need verified SHA-256 hashes before deploying to end users.
      `LocalModelLoader` emits a `Trace.TraceWarning` on every load until these are populated.
      Steps: download each GGUF, run `sha256sum <file>`, prefix with `sha256:`, update registry.json.
- [ ] Sovereign-origin embedding model — wire a BGE-zh or Qwen-Embedding backend into `TextEmbedder.cs`
      (currently throws `NotSupportedException`; see `src/Bhengu.AI.Embeddings/TextEmbedder.cs`).
- [ ] Verify that ModelScope download URLs match Alibaba's actual API surface.
- [ ] Resume support for partial downloads on slow/unreliable connections.
- [ ] Integration tests against a tiny local GGUF (so CI doesn't move 8 GB).
