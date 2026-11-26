#!/usr/bin/env bash
# Runs every local test layer: xUnit (unit, arch, integration via Testcontainers), Vitest, lint, Playwright e2e.
# Needs the local stack for the e2e step (scripts/dev-up.sh). OUT=<dir> collects reports (default TestResults/).
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$PATH:/c/Program Files/dotnet:$HOME/.dotnet/tools"
OUT="${OUT:-TestResults}"
mkdir -p "$OUT"

echo "== .NET: compile + unit + arch + integration =="
nuke Compile Test
cp TestResults/*.xml "$OUT"/ 2>/dev/null || true

echo "== web: lint, stylelint, build, vitest =="
( cd web
  pnpm install --frozen-lockfile
  pnpm lint
  pnpm lint:styles
  pnpm build
  npx vitest run --reporter=junit --outputFile="../$OUT/vitest.xml" )

echo "== web: playwright e2e =="
( cd web
  npx playwright install chromium >/dev/null
  PW_REPORT="../$OUT/playwright-report" PW_JUNIT="../$OUT/playwright.xml" npx playwright test )

echo "ALL TESTS PASSED"
