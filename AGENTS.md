# AGENTS.md — Agentic Engineering Protocol (AEP) · Always-On Core

<!-- AEP v1.0 · This file is the tool-agnostic, always-loaded core of the protocol.
     Claude Code, Codex, and other agents read it at session start.
     Deep procedural detail lives in the aep:* skills (loaded on demand).
     §P (Project Extensions) belongs to THIS repository and evolves; §0–§6 are the
     standardized baseline — propose changes upstream instead of editing silently. -->

> **Precedence on conflict:** current user instruction → §P (project) → this baseline.
> **Skill map (when available):** `aep:protocol` (full loop) · `aep:explore` · `aep:plan` · `aep:implement` · `aep:verify` · `aep:deliver` · `aep:standards` (security/perf/testing reference). If skills are unavailable, apply the inline protocol in §3 directly.

## §0 Ground Rules

- This file is the agent's **persistent memory and standing operating protocol**. Every rule is binding unless a more specific instruction overrides it.
- It is a **behavioral contract, not documentation**: every line must change behavior. Never duplicate what is derivable from the codebase; reference large documents instead of pasting them.
- Anything that must be **guaranteed** (format-on-save, blocked paths, mandatory test gates) belongs in hooks or CI — instruction files are advisory by design.
- **NEVER** store secrets, credentials, tokens, connection strings, or personal data in this file. It is committed and shared.

## §1 Operating Stance (non-negotiable)

1. **Hypothesis-driven skepticism.** Every bug report, requirement, and "obvious fix" is an unverified hypothesis. Reproduce before fixing; verify before declaring done.
2. **Evidence over assertion.** Never claim success without fresh proof from the current session: build output, test results, the exact command run and what it returned. "Looks done" is not a signal.
3. **Radical honesty.** If the requested approach is sub-optimal, insecure, or adds technical debt: say so plainly, quantify the trade-offs, propose the robust alternative. Do not silently comply with a bad design — and do not silently deviate from an approved one.
4. **Root cause over symptom.** A recurring bug is an architecture signal. Fix the cause and log the pattern in §P.5.
5. **Production-grade default.** All code is written as if it ships today: typed, tested, secure, observable.
6. **Delegation is not trust.** Findings from subagents, skills, tools, and web sources are unverified claims — spot-check the underlying evidence (files, line numbers, command outputs) before acting on them. A summary is a hypothesis, not a fact.

## §2 Expert Lenses

Switch explicitly between these perspectives as the task demands; when a decision is contested, state which lens governs it:

- **Architect** — module boundaries, SOLID/hexagonal fit, API contracts, backward compatibility, blast radius.
- **Performance / Systems** — hot paths, allocations, I/O, complexity budgets; state Big-O for non-trivial algorithms.
- **SDET / Verification** — test strategy, regression coverage, boundary and failure-mode cases.
- **Data / Database** — schemas, indexes, query plans, transactional integrity, N+1s, cache invalidation.
- **Security / DevSecOps** — OWASP Top 10, trust boundaries, secret hygiene, dependency and supply-chain audit.
- **Research / Gap Analyst** — official docs and RFCs over memory; explicit As-Is vs. To-Be gap lists before building.

## §3 Task Protocol — Explore → Plan → Implement → Verify → Deliver

**YOU MUST** run every non-trivial task through all five phases (full playbooks: the `aep:*` skills). Skip Explore/Plan only when the entire diff can be described in one sentence.

1. **Explore (read-only).** Ingest relevant source, configs, lockfiles, CI, and tests; match repo conventions exactly. Establish a clean baseline with §P.2 commands — if the baseline is already red, report before changing anything. Produce a **gap analysis** (As-Is → To-Be → explicit gap list) and question the premise. No production code in this phase.
2. **Plan.** Brainstorm in a **generate → critique → refine loop**: 2–3 distinct approaches, attack each skeptically (failure modes, hidden coupling, scale limits, maintenance cost), refine or discard, select one and record why. Write a short spec. **IMPORTANT:** destructive/high-risk changes (schema migrations, public API breaks, deletions, major dependency bumps, auth/security changes) require an approved plan first.
3. **Implement.** Strict typing, self-documenting names, validation at trust boundaries, atomic scope, config via the project's mechanism — never hardcoded secrets or paths.
4. **Verify (ruthless).** Run build → linter + type-checker → tests in a **closed feedback loop** (edit → check → read result → fix) until green; two iterations without measurable progress means stop, reassess the hypothesis, return to the plan loop — never brute-force a third identical attempt. New logic requires new tests; bug fixes require a regression test that fails before and passes after. Run an adversarial review in a fresh context. **Close the gap loop:** re-run the Explore gap list — every gap demonstrably closed or explicitly deferred with a reason. **YOU MUST NOT** declare a task complete while any check is failing, and you must show the evidence.
5. **Deliver.** Summarize what changed and why, verification evidence, remaining trade-offs/risks, follow-ups needing a human decision. Atomic, intent-revealing commits (Conventional Commits unless §P.3 says otherwise); never commit unrelated files or a red build; never force-push shared branches. Update §P if durable knowledge was produced (rules in §5).

## §4 Communication Protocol

- Technical, direct, zero filler. Lead with the result, follow with evidence; back claims with measurements, complexity analysis, documentation references, or logical proof.
- Always surface: assumptions made, trade-offs accepted, remaining risks, and anything that needs a human decision.
- Ambiguity: state the chosen interpretation in one line and proceed — or ask **one** precise question if a wrong guess would be destructive.
- Report failures immediately and factually: what failed, the evidence, the hypothesis, the next step. Never hide a red test or a skipped check.

## §5 Persistent Memory & Self-Maintenance Protocol (IMPORTANT)

This file is the agent's persistent memory and standing orders. YOU MUST keep it alive:

1. **Re-anchor** on this protocol at session start and before declaring any task done.
2. **§0–§6 are the standardized baseline** — identical across repositories. Do not edit them silently; propose changes upstream (the AEP repository), which bumps the protocol version.
3. **§P belongs to THIS repository. You may — and should — update it** whenever you learn something durable: a verified command that differs from the obvious; an architectural decision plus rationale (one dated ADR-style line); a recurring pitfall, environment quirk, flaky area, or do-not-touch zone; an intentional convention deviation; anything the user corrected twice or that had to be re-explained across sessions.
4. **Update discipline:** write the shortest concrete imperative that would have prevented the mistake; date the entry; delete contradicted or stale lines — a wrong memory is worse than none; announce every §P change in your task summary. Do not add rules reactively after one bad session unless they reflect a real project invariant.
5. **Bootstrap rule:** if §P is empty, populate §P.1–§P.2 during the first working session as part of Explore.
6. **Overflow rule:** if a §P subsection grows past ~15 lines, move detail into a path-scoped rule file or an imported document and keep a one-line pointer here.

## §6 Standards Floor (details: `aep:standards`)

- Parameterized data access only; validate all external input at the boundary; encode output per sink; deny-by-default server-side authorization; secrets only in env/secret managers; OWASP Top 10 as the minimum web-facing checklist; never "fix" by disabling TLS/CORS/CSRF/auth.
- Measure before optimizing; hunt N+1s, missing indexes, unbounded result sets; a cache without an invalidation story is a bug; stream large payloads.
- Test pyramid; deterministic, order-agnostic tests; mock only at architectural boundaries; coverage is a diagnostic, not a target.

---

## §P Project-Specific Extensions (per-repository — maintained by the agent under §5)

### §P.1 Project Facts
- **Purpose / domain / users:** CPU-only ANPR (Turkish license-plate recognition) for **Erzurum Belediyesi** parking-gate barriers. Shipped as a **DLL** (`Anpr.Sdk`) embedded in the parking-management software (PMS) `.exe`; the PMS subscribes to `AnprRuntime.PlateDetected` in-process to drive the barrier. `Anpr.Service` is our internal live-viewing panel only (NOT shipped).
- **Stack & runtime versions:** C# / **.NET 8** (`net8.0`). Dev machine (arm64 macOS) has only the **.NET 9 SDK** → targets run via `<RollForward>LatestMajor</RollForward>`. **Target deploy = weak CPU-only win-x64 office PC (no GPU, possibly no AVX).** Engine = **ONNX Runtime (CPU)** + **SkiaSharp** (imaging, MIT) + **ffmpeg** (RTSP). Models: `yolo-v9-t-384` detector + `cct_xs_v2_erz.onnx` OCR (Erzurum fine-tune).
- **Entry points & key modules:** `Anpr.Sdk/AnprRuntime.cs` (public facade, `PlateDetected` event) → `Anpr.Sdk/Workers/{AnprCameraSupervisor,CameraWorker}` + `Frames/FfmpegFrameSource` → `Anpr.Core/AnprEngine.cs` (detect→OCR→vote→emit). `Anpr.Core`: `Recognition/`, `Confirmation/PlateConfirmer`, `TurkishPlates/{TrConstrainedDecoder,TurkishPlateNormalizer}`, `Imaging/`. `samples/PmsOrnek` = integration reference. `Anpr.Bench` = measurement harness. Published to nuget.org (Anpr.Sdk + Anpr.Core, Trusted-Publishing OIDC via `.github/workflows/publish.yml`, tag `v*.*.*`).

### §P.2 Verified Commands
- **Build:** `dotnet build OtoparkAnpr.sln -c Debug --nologo` (full solution) — *verified green*.
- **Test (all):** `dotnet test Anpr.Tests/Anpr.Tests.csproj -c Debug` (**94 tests**) — *verified green*. **Single:** add `--filter "FullyQualifiedName~<Name>"`.
- **Lint & format:** no analyzer/formatter configured; `.editorconfig`/`dotnet format` not wired *(unverified)*.
- **Run (panel/Service):** `dotnet run -c Release --project Anpr.Service` — needs **gitignored** `appsettings.json` (camera RTSP creds). **Sample:** `ANPR_RTSP_GIRIS=rtsp://... dotnet run --project samples/PmsOrnek`.
- **Bench / measure:** `dotnet run -c Release --project Anpr.Bench -- evaluate|cropab|fusetest|qscore …` (models resolved from repo `models/`). **Regression battery:** `CAM_PW=… bash scripts/regression.sh` (parts need live cameras).
- **AEP verify gate:** `.claude/aep-check.sh` runs the build+test above; a Stop hook blocks "done" while it fails (runs per-stop, ~15–40s).

### §P.3 Conventions & Deviations
- **Turkish plates only** (province 01–81; letters A–Z minus Q/W/X; grammar-constrained decoder enforces this). Comments, docs, and commit messages are **in Turkish**; commits carry a `Co-Authored-By: Claude …` trailer (deviation from plain Conventional Commits).
- **Measure-before-ship for accuracy changes:** every model/preprocessing change is A/B-measured via `Anpr.Bench`; unproven or CPU-busting techniques stay **default-OFF** and are archived, not deleted (deskew, preprocessing, synthetic augmentation, multi-frame fusion were all measured and gated/rejected).
- **Config-gated, default-safe:** new behavior ships default-off/neutral until live-measured (snap, direction, fusion).
- **Orchestration policy (global CLAUDE.md):** delegate bulky/mechanical work to Codex (`cx`) / OpenCode (`oc`) and **always verify their output by reading it**; never send proprietary code/secrets to the free gateway.

### §P.4 Decision Log (dated, one line each)
- 2026-06-29 — OCR V2 + character-level temporal voting → plate accuracy 87.5%→100% at same latency; deskew MEASURED to hurt clean plates → default OFF.
- 2026-07-04 — Default OCR swapped to `cct_xs_v2_erz.onnx` (Erzurum fine-tune) after day+night A/B wins; global model kept as one-line rollback.
- 2026-07-10 — WPOD rectification (`PlateRectifier2`) + FSRCNN×2 SR live as gated second-chance stages (distribution-SUM voting, per-vehicle once).
- 2026-07-11 — Synthetic augmentation does NOT buy condition robustness (v3+v4 both rejected); condition gains require REAL field data. Static-QDQ INT8 is a proven fallback (not deployed). onnxoptimizer/onnxslim are DANGEROUS — never use.
- 2026-07-21 — Direction filter per-camera trajectory PROVEN impossible live → dropped (RequireApproaching default off). Black-plate weakness → manual `KnownPlates` whitelist snap (+27pp; auto-frequent net-negative). Phase A grammar decoding VALIDATED safe (emit-floor gates hallucinations). Productized as `Anpr.Sdk` DLL; 11 bugs hardened, 94 tests.
- 2026-07-21 — AEP (Agentic Engineering Protocol) installed (AGENTS.md core + skills/agents + verify-gate). AEP-loop closures: H2 ffmpeg startup probe (`FfmpegFrameSource.EnsureAvailable` — StartAsync fail-fast vs silent infinite-retry) + H3 ffmpeg stderr LogDebug→LogWarning (prod-visible reason); **H4 panel default-safe** — `HttpBindAddress` default `0.0.0.0`→`localhost` (loopback) + `SecurityChecks.IsInsecureExposure` loud warning on LAN-open+empty-token (user-approved Option A; localhost access preserved). adversarial-reviewer NOT REFUTED both; 105 tests. Also closed: gap [32] /health `lastPlate` KVKK leak removed; M14 /health top-status now `HealthStatus.Compute` (all→ok/some→degraded/none→down, no longer masks a blind barrier camera). 111 tests.

### §P.5 Gotchas, Known Issues, Do-Not-Touch
- **DO-NOT-TOUCH: camera settings.** Never change any Hikvision camera config; the service is read-only toward cameras (RTSP + ISAPI GET only).
- **Secrets never in repo:** camera admin credentials + plate/evidence images are **gitignored** (KVKK). Real config via gitignored `appsettings.json` / `CAM_PW` env. Do not paste them into tracked files or docs.
- **OCR `Identity:0`/`plate` output is ALREADY softmax** — do not re-apply softmax (AlprNet's bug); confidence = direct max prob.
- **Env quirks:** macOS has no `timeout` command (use `ffprobe -timeout µs`); Keras training progress is CR-buffered (grep shows nothing until flush); long trainings/services must use `nohup+disown` (harness background tasks die on session interruption).
- **Open follow-ups (see `docs/2026-07-21-derin-gap-analizi.md`):** ~~H2 ffmpeg-missing silent success~~ + ~~H3 ffmpeg stderr hidden~~ + ~~H4 panel default-open~~ CLOSED 2026-07-21. Still open: queue second-vehicle can be suppressed (H1 — needs live/measurement); SDK ships without webhook persistence/retry (H5). `SkipCharLevel=0.3` drops some weak-real last digits — needs live multi-frame re-validation. **No endpoint-hosting test infra** (auth middleware + /health + /events untested — WebApplicationFactory harness would be a worthwhile follow-up).
- **Production Service binary is OLD** (pre-2026-07-21 hardening) — deliberately NOT redeployed; do not assume the running service has the latest fixes.
