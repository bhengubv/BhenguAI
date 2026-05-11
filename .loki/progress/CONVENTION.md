# Agent Progress Logging Convention

Every background agent MUST write progress checkpoints to:
  .loki/progress/{track-name}.log

## Format
[HH:mm:ss] STEP: {description}
[HH:mm:ss] DONE: {summary} | Tests: {n} passed

## Example
[14:03:11] STEP: Created ITtsEngine.cs
[14:03:45] STEP: Created NullTtsEngine.cs
[14:05:02] STEP: Created MauiAudioCapture.cs
[14:07:30] STEP: Running dotnet test...
[14:08:55] DONE: Track 1 complete | Tests: 38 passed

## How to write from an agent (PowerShell)
$log = "C:\Dev\Solutions\com.bhengubv\BhenguAI\.loki\progress\track-N.log"
Add-Content $log "[$(Get-Date -Format HH:mm:ss)] STEP: Created ITtsEngine.cs"

## How to write from an agent (Bash)
LOG="C:/Dev/Solutions/com.bhengubv/BhenguAI/.loki/progress/track-N.log"
echo "[$(date +%H:%M:%S)] STEP: Created ITtsEngine.cs" >> "$LOG"

## Template line to paste into every future agent prompt
---
After every major step, append one line to
`C:\Dev\Solutions\com.bhengubv\BhenguAI\.loki\progress\{track-name}.log`
using the format: `[HH:mm:ss] STEP: {what you just did}`
Write `[HH:mm:ss] DONE: {summary} | Tests: N passed` as the final line.
---
