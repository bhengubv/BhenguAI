# Parallel Agent Rules

## The two things that kill parallel agents

### 1. vstest timeout
dotnet test hangs silently if VSTEST_CONNECTION_TIMEOUT isn't set.
FIXED: bhengu.runsettings now sets TestSessionTimeout=180000 automatically.
Agents just run `dotnet test` — no env var needed.

### 2. Parallel file conflicts
Two agents editing the same file simultaneously causes "file modified since read"
errors. The agent retries, the other agent retries, and both stall.

---

## File ownership map (one agent per file at a time)

| File / Area                          | Who owns it            |
|--------------------------------------|------------------------|
| ButlerOptions.cs                     | ONE agent only         |
| ButlerService.cs                     | ONE agent only         |
| Bhengu.AI.Hosting.csproj             | ONE agent only         |
| BhenguAI.sln                         | ONE agent only         |
| Bhengu.AI.Tests.csproj               | ONE agent only         |

These are SHARED files. Never assign two parallel agents work that both
require editing the same shared file.

---

## How to launch parallel agents safely

### Safe: non-overlapping file ownership
  Agent A → src/Bhengu.AI.Voice/     (new files only)
  Agent B → src/Bhengu.AI.Memory/    (new files only)
  Agent C → src/Bhengu.AI.Tools/     (new files only)

### Unsafe: shared file conflict
  Agent A → modifies ButlerOptions.cs
  Agent B → also modifies ButlerOptions.cs   ← COLLISION

### Rule
When multiple agents need to modify a shared file (ButlerOptions, ButlerService,
*.csproj, *.sln), either:
  a) Give ONE agent ownership of the shared file and have others skip it, OR
  b) Sequence the agents (A finishes → B starts)

---

## Progress log convention (mandatory)
Every agent MUST write to .loki/progress/{track-name}.log after each major step.
Format: [HH:mm:ss] STEP: description
Final:  [HH:mm:ss] DONE: summary | Tests: N passed
