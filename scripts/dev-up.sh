#!/usr/bin/env bash
# Starts the whole local stack (SQL Server, moto, price mock, migrations, API, parser runner, web) and seeds demo data.
set -euo pipefail
cd "$(dirname "$0")/.."
[ -f .env ] || cp .env.example .env
docker compose up -d --build
echo "Waiting for the API..."
for _ in $(seq 1 60); do
  curl -fs http://localhost:8085/readyz >/dev/null && break
  sleep 2
done
curl -fs http://localhost:8085/readyz >/dev/null || { echo "API did not become ready"; exit 1; }
bash scripts/seed.sh
echo "Web: http://localhost:5173   API: http://localhost:8085/swagger"
