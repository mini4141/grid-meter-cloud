#!/usr/bin/env bash
# Loads demo data through the public API: one site + two meters per tenant, and a month of readings for aurora-power.
set -euo pipefail
cd "$(dirname "$0")/.."
API="${API_URL:-http://localhost:8085}"
json() { node -pe "const d=JSON.parse(require('fs').readFileSync(0,'utf8'));$1"; }

seed_tenant() {
  local tenant=$1 site=$2 region=$3
  local token
  token=$(curl -fs -X POST "$API/api/auth/token" -H 'Content-Type: application/json' -d "{\"tenant\":\"$tenant\",\"user\":\"seed\"}" | json 'd.token')
  local auth=(-H "Authorization: Bearer $token" -H "X-Tenant: $tenant")
  if [ "$(curl -fs "${auth[@]}" "$API/api/sites" | json 'd.length')" != "0" ]; then
    echo "$tenant already seeded"
    return
  fi
  local id
  id=$(curl -fs "${auth[@]}" -H 'Content-Type: application/json' -X POST "$API/api/sites" -d "{\"name\":\"$site\",\"region\":\"$region\"}" | json 'd.id')
  for m in MPAN-0001 MPAN-0002; do
    curl -fs "${auth[@]}" -H 'Content-Type: application/json' -X POST "$API/api/sites/$id/meters" -d "{\"externalId\":\"$m\",\"type\":\"Import\"}" >/dev/null
  done
  echo "$tenant seeded: $site ($id)"
  if [ "$tenant" = "aurora-power" ]; then
    local ticket url
    ticket=$(curl -fs "${auth[@]}" -H 'Content-Type: application/json' -X POST "$API/api/uploads" -d '{"fileName":"readings-2026-11.csv"}')
    url=$(echo "$ticket" | json 'd.url')
    curl -fs -X PUT -H 'Content-Type: text/csv' --data-binary @samples/readings-2026-11.csv "$url" >/dev/null
    echo "readings-2026-11.csv uploaded for $tenant"
  fi
}

seed_tenant aurora-power "Melbourne Depot" VIC
seed_tenant kita-denki "Osaka Hub" Kansai
