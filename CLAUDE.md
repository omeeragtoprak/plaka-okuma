# CLAUDE.md — AEP Adapter

The canonical, tool-agnostic protocol for this repository lives in @AGENTS.md — it is binding in every session. The global operator policy (`~/.claude/CLAUDE.md`, orchestration/delegation) still applies and composes with it.

## Claude Code specifics

- **Skills** (invoked with `/` or auto-matched). Installed in this repo under `.claude/skills/`: `protocol` orchestrates the full loop; `explore`, `plan`, `implement`, `verify`, `deliver` are the phase playbooks; `standards` is the security/performance/testing reference. Prefer invoking the phase skill over improvising the phase.
  - *Official plugin (optional, recommended):* the same skills as `aep:*` (namespaced) + auto-update via marketplace — install with `/plugin marketplace add omeeragtoprak/agentic-engineering-protocol` then `/plugin install aep@agentic-engineering`. If you install the plugin, the bare-named copies in `.claude/skills/` become redundant and may be removed.
- **Subagents** (`.claude/agents/`): delegate wide investigations to protect the main context. For the adversarial review step in §3.4 use the **`adversarial-reviewer`** agent; for gap-closure audits use **`gap-auditor`**. The author never grades its own work — verification runs in fresh context.
- **Verify gate (hook):** `.claude/aep-check.sh` runs this project's build+test (`dotnet build OtoparkAnpr.sln` + `dotnet test`). A Stop hook (`.claude/settings.json` → `.claude/aep-verify-gate.sh`) runs it and blocks task completion while it fails (per-stop, ~15–40s; 8-block safety override). Keep the check pointing at §P.2's real commands; to speed it up, trim to build-only.
- **Context discipline:** `/clear` between unrelated tasks; after two failed corrections on the same issue, prefer a fresh session with a sharper prompt over a third attempt.
