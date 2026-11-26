# Proof index

Every artifact below is produced by `scripts/capture-proof.sh <phase>` (Playwright, CLI and test runners), not by hand. Account IDs, tokens and keys are redacted.

## Phase 1: local (Docker Compose, moto stand-ins for S3/SQS/Lambda)

| Artifact | What it proves |
|---|---|
| `phase1-local/screenshots/01-sites-carbon-table.png` | Tenant-aware sites listed in a Carbon `DataTable` (F1) |
| `phase1-local/screenshots/02-upload.png` | CSV uploaded through a presigned S3 URL, parser running (F2) |
| `phase1-local/screenshots/03-consumption-chart.png` | The uploaded month rendered as Carbon line/bar charts and a peak-demand tile (F3, F4) |
| `phase1-local/screenshots/04-estimate-flag-on.png` | Bill estimate visible with the `tariff-estimates` flag on, priced through the flaky mock API (F5) |
| `phase1-local/screenshots/05-dr-event.png` | Demand-response event shown as published via the outbox (F6) |
| `phase1-local/screenshots/06-tenant-switch-isolation.png` | After switching to `kita-denki`, none of `aurora-power`'s data is visible |
| `phase1-local/screenshots/07-swagger.png` | OpenAPI document served by FastEndpoints.Swagger |
| `phase1-local/screenshots/08-github-project-board.png` | GitHub issues F1-F8 (the Projects board needs a token scope that could not be granted autonomously, see `docs/DECISIONS.md`) |
| `phase1-local/tests/*.xml`, `tests/playwright-report/` | JUnit reports: xUnit unit, architecture and integration; Vitest; Playwright e2e |
| `phase1-local/logs/test-all.log` | Full output of `scripts/test-all.sh` |
| `phase1-local/logs/polly-retries.log` | 20 estimate calls all succeeded while the price mock failed ~50% of requests; retry warnings from the Polly pipeline |
| `phase1-local/logs/outbox-dispatch.log` | Outbox rows with `ProcessedAt` set, dispatcher log, and the notifier's "Customer notified" line; inbox ingestion log |
| `phase1-local/logs/nuke-build.log` | Nuke targets Compile, GenerateClient, DockerBuild, Publish |
| `phase1-local/logs/ef-migrations.log` | EF Core migration applied to SQL Server |
| `phase1-local/logs/parser-local-runner.log` | The parser (same code as the Lambda) turning the file into SQS batches |

## Phase 2: AWS

Added by `scripts/capture-proof.sh phase2`; see `phase2-cloud/`.

## Phase 3: destroy

Added by `scripts/verify-destroyed.sh`; see `phase3-destroy/verify-destroyed.txt`.
