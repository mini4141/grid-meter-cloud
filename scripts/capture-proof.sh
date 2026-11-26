#!/usr/bin/env bash
# Usage: scripts/capture-proof.sh phase1    (local stack must be running: scripts/dev-up.sh)
#        scripts/capture-proof.sh phase2    (cloud stack deployed: scripts/deploy.sh)
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$PATH:/c/Program Files/dotnet:$HOME/.dotnet/tools"
PHASE="${1:?usage: capture-proof.sh phase1|phase2}"

# Strips ANSI colours and redacts account ids, keys and tokens before anything is saved.
redact() {
  sed -E 's/\x1b\[[0-9;]*[A-Za-z]//g; s/([^0-9]|^)[0-9]{12}([^0-9]|$)/\1<ACCOUNT_ID>\2/g; s/(AKIA|ASIA)[A-Z0-9]{16}/<ACCESS_KEY>/g; s/(Bearer |X-Amz-Signature=|X-Amz-Security-Token=)[A-Za-z0-9%._~+\/=-]+/\1<REDACTED>/g'
}

json() { node -pe "const d=JSON.parse(require('fs').readFileSync(0,'utf8'));$1"; }

phase1() {
  local P=proof/phase1-local API=http://localhost:8085
  mkdir -p $P/screenshots $P/logs $P/tests

  if [ -z "${SKIP_TESTS:-}" ]; then
    echo "== tests + screenshots =="
    OUT="$P/tests" PROOF_DIR="$PWD/$P/screenshots" bash scripts/test-all.sh 2>&1 | redact > $P/logs/test-all.log
  fi

  echo "== swagger screenshot =="
  ( cd web && node scripts/screenshot.mjs "$API/swagger" "../$P/screenshots/07-swagger.png" ".swagger-ui" )

  echo "== GitHub issues board screenshot =="
  REPO=$(git remote get-url origin | sed -E 's#.*github.com[/:]##; s#\.git$##')
  ( cd web && node scripts/screenshot.mjs "https://github.com/$REPO/issues" "../$P/screenshots/08-github-project-board.png" || echo "board screenshot skipped" )

  echo "== exercise the price API through Polly =="
  local token site
  token=$(curl -fs -X POST $API/api/auth/token -H 'Content-Type: application/json' -d '{"tenant":"aurora-power","user":"proof"}' | json 'd.token')
  local auth=(-H "Authorization: Bearer $token" -H "X-Tenant: aurora-power")
  curl -fs "${auth[@]}" -H 'Content-Type: application/json' -X PUT $API/api/admin/flags/tariff-estimates -d '{"enabled":true}'
  site=$(curl -fs "${auth[@]}" $API/api/sites | json 'd[0].id')
  ok=0; fail=0
  for _ in $(seq 1 20); do
    if curl -fs "${auth[@]}" $API/api/sites/$site/estimate >/dev/null; then ok=$((ok+1)); else fail=$((fail+1)); fi
  done
  echo "estimate calls: $ok succeeded, $fail failed (price mock FAIL_RATE=${FAIL_RATE:-0.5})" > $P/logs/polly-retries.log
  { docker compose logs --no-log-prefix pricemock | grep -c "Injecting failure" || true | sed 's/^/price mock injected failures: /'
    docker compose logs --no-log-prefix api | grep -E "Price API (retry|circuit)" || true; } | redact >> $P/logs/polly-retries.log

  echo "== issue a demand response and capture the outbox =="
  curl -fs "${auth[@]}" -H 'Content-Type: application/json' -X POST $API/api/sites/$site/demand-response \
    -d '{"windowStart":"2026-11-27T17:00:00Z","windowEnd":"2026-11-27T19:00:00Z","targetKw":200}' >/dev/null
  sleep 8
  {
    echo "--- outbox rows (SQL Server) ---"
    MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "${SA_PASSWORD:-Your_strong_Passw0rd}" -d GridMeter -W \
      -Q "SELECT TOP 5 Id, TenantId, Type, OccurredAt, ProcessedAt FROM OutboxMessages ORDER BY OccurredAt DESC"
    echo "--- API: dispatcher and inbox ---"
    docker compose logs --no-log-prefix api | grep -E "Outbox message|Customer notified|Ingested batch" | tail -15 || true
  } 2>&1 | redact > $P/logs/outbox-dispatch.log

  echo "== EF migrations log =="
  { docker compose logs --no-log-prefix migrate
    dotnet ef migrations list -p src/GridMeter.Infrastructure -s src/GridMeter.Api --no-build 2>&1 | tail -5; } | redact > $P/logs/ef-migrations.log

  echo "== parser + service logs =="
  docker compose logs --no-log-prefix parser | tail -40 | redact > $P/logs/parser-local-runner.log

  echo "== Nuke build =="
  nuke Compile Publish DockerBuild GenerateClient --with-integration false 2>&1 | redact > $P/logs/nuke-build.log
  echo "Phase 1 proof captured in $P"
}

case "$PHASE" in
  phase1) phase1 ;;
  phase2) bash scripts/capture-proof-cloud.sh ;;
  *) echo "unknown phase $PHASE"; exit 1 ;;
esac
