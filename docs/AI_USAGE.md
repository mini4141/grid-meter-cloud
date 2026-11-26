# AI usage

This repository was built end to end with Claude Code, working from `SPECS.md` (what to build) and `CLAUDE.md` (engineering rules, phases and proof requirements).

**How it was used**
- Claude Code scaffolded the solution, wrote the features phase by phase and tracked progress in `PROGRESS.md`.
- Decisions that were ambiguous were logged in `docs/DECISIONS.md` instead of being left implicit.

**How the output was reviewed and tested (the harness is the guardrail)**
- Every layer has a shallow automated test: xUnit unit tests, `WebApplicationFactory` + SQL Server (Testcontainers) integration tests for tenant isolation, inbox dedupe and outbox dispatch, a NetArchTest layering test, Vitest + React Testing Library per page, and a Playwright end-to-end flow.
- `scripts/test-all.sh` runs all of it and must be green before a phase counts as done. GitHub Actions runs the cloud-free layers on every push.
- Proof (screenshots, logs, test reports) is captured by script (`scripts/capture-proof.sh`) so claims can be re-checked, not just asserted.
- Failures found along the way (for example an EF query that did not translate, a moto/SDK checksum incompatibility, entity key tracking) were caught by those tests and fixed in code, not worked around in tests.
- Nothing runs against AWS until the local phase is green, and cloud resources are destroyed and verified gone before the work is called finished.
