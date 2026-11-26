#!/usr/bin/env bash
# Phase 2 proof: runs the Playwright e2e against CloudFront and collects CLI evidence. Called by capture-proof.sh phase2.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/aws-env.sh
source .local/cloud.env
P=proof/phase2-cloud
mkdir -p $P/screenshots $P/logs $P/tests/playwright-cloud
json() { node -pe "const d=JSON.parse(require('fs').readFileSync(0,'utf8'));$1"; }

echo "== feature flag toggle through AppConfig =="
{
  echo "SPA: $CLOUD_URL  API: $API_URL"; echo "before: $(curl -fs "$API_URL/api/admin/info")"
  bash scripts/toggle-flag.sh on
  for _ in $(seq 1 30); do
    [ "$(curl -fs "$API_URL/api/admin/info" | json 'd.tariffEstimates')" = "true" ] && break
    sleep 3
  done
  echo "after (flag on):  $(curl -fs "$API_URL/api/admin/info")"
} 2>&1 | redact | tee $P/logs/appconfig-flag-toggle.log

echo "== Playwright against CloudFront =="
( cd web
  npx playwright install chromium >/dev/null
  CLOUD=1 BASE_URL="$CLOUD_URL" PROOF_DIR="../$P/screenshots" \
    PW_REPORT="../$P/tests/playwright-cloud/report" PW_JUNIT="../$P/tests/playwright-cloud/junit.xml" \
    npx playwright test ) 2>&1 | redact | tee $P/logs/playwright-cloud.log

echo "== API-level checks through CloudFront =="
{
  for t in aurora-power kita-denki; do
    TOKEN=$(curl -fs -X POST "$API_URL/api/auth/token" -H 'Content-Type: application/json' -d "{\"tenant\":\"$t\",\"user\":\"proof\"}" | json 'd.token')
    echo "$t sees $(curl -fs "$API_URL/api/sites" -H "Authorization: Bearer $TOKEN" -H "X-Tenant: $t" | json 'd.length + " site(s): " + d.map(s => s.name).join(", ")')"
  done
  curl -fs "$API_URL/readyz" | sed 's/^/readyz: /' || true
} 2>&1 | redact > $P/logs/tenant-isolation-api.log

echo "== flag off hides the estimate =="
{
  bash scripts/toggle-flag.sh off
  for _ in $(seq 1 30); do
    [ "$(curl -fs "$API_URL/api/admin/info" | json 'd.tariffEstimates')" = "false" ] && break
    sleep 3
  done
  echo "after (flag off): $(curl -fs "$API_URL/api/admin/info")"
} 2>&1 | redact | tee -a $P/logs/appconfig-flag-toggle.log

echo "== Lambda and service logs from CloudWatch =="
aws logs filter-log-events --log-group-name "$(out LambdaLogGroup)" --start-time $(($(date +%s%3N)-480000)) --query 'events[].message' --output text 2>&1 | redact > $P/logs/lambda-parser.log
{ aws logs tail "$(out ApiLogGroup)" --since 2h --format short 2>&1 | grep -E "Ingested batch|Outbox message|Customer notified|Price API|Database migrations" | tail -60 || true; } | redact > $P/logs/ecs-api.log
{ aws logs tail "$(out PriceLogGroup)" --since 2h --format short 2>&1 | tail -15 || true; } | redact > $P/logs/price-mock.log

echo "== CloudWatch metric graph =="
FN=$(out LambdaName)
WIDGET="{\"metrics\":[[\"AWS/Lambda\",\"Invocations\",\"FunctionName\",\"$FN\",{\"stat\":\"Sum\"}],[\"AWS/SQS\",\"NumberOfMessagesSent\",\"QueueName\",\"$(out ReadingsQueueName)\",{\"stat\":\"Sum\"}]],\"view\":\"timeSeries\",\"region\":\"$AWS_REGION\",\"period\":60,\"stat\":\"Sum\",\"start\":\"-PT2H\",\"width\":900,\"height\":400,\"title\":\"Parser Lambda invocations and reading batches sent to SQS\"}"
aws cloudwatch get-metric-widget-image --metric-widget "$WIDGET" --output json > .local/widget.json
node -e 'const fs = require("fs"); const d = JSON.parse(fs.readFileSync(".local/widget.json", "utf8")); fs.writeFileSync(process.argv[1], Buffer.from(d.MetricWidgetImage, "base64"));' "$P/screenshots/04-cloudwatch-lambda-sqs.png"

echo "== resources.txt =="
{
  echo "Deployed AWS resources (ap-southeast-2), from stack outputs and describe calls"
  echo
  echo "--- CloudFormation outputs ---"
  node -pe "const o=JSON.parse(require('fs').readFileSync('.local/cdk-outputs.json','utf8')).$STACK; Object.entries(o).map(([k,v])=>k+' = '+v).join('\n')"
  echo
  echo "--- RDS ---"
  aws rds describe-db-instances --query 'DBInstances[].{Id:DBInstanceIdentifier,Engine:Engine,Class:DBInstanceClass,MultiAZ:MultiAZ,Storage:AllocatedStorage,Public:PubliclyAccessible,Status:DBInstanceStatus}' --output table
  echo "--- ECS service ---"
  aws ecs describe-services --cluster "$(out ClusterName)" --services "$(out ServiceName)" --query 'services[].{Name:serviceName,Desired:desiredCount,Running:runningCount,LaunchType:launchType,Status:status}' --output table
  echo "--- Lambda ---"
  aws lambda get-function-configuration --function-name "$FN" --query '{Name:FunctionName,Runtime:Runtime,Memory:MemorySize,Timeout:Timeout,Handler:Handler}' --output table
  echo "--- SQS ---"
  aws sqs list-queues --queue-name-prefix gridmeter --query 'QueueUrls' --output text | tr '\t' '\n'
  echo "--- S3 ---"
  aws s3api list-buckets --query "Buckets[?starts_with(Name, 'gridmeter-')].Name" --output text | tr '\t' '\n'
  echo "--- AppConfig ---"
  aws appconfig list-applications --query "Items[?Name=='grid-meter-cloud'].{Id:Id,Name:Name}" --output table
  echo "--- Secrets Manager ---"
  aws secretsmanager list-secrets --query "SecretList[?Tags[?Key=='Project' && Value=='grid-meter-cloud']].Name" --output text | tr '\t' '\n'
  echo "--- SPA hosting ---"
  DIST=$(out DistributionId 2>/dev/null || true)
  if [ -n "$DIST" ] && [ "$DIST" != "undefined" ]; then
    aws cloudfront get-distribution --id "$DIST" --query 'Distribution.{Id:Id,Status:Status,Domain:DomainName,PriceClass:DistributionConfig.PriceClass}' --output table
  else
    echo "S3 static website: $(out SpaUrl) (CloudFront not used, see docs/DECISIONS.md)"
  fi
  echo "--- Tagged resources (Project=grid-meter-cloud) ---"
  aws resourcegroupstaggingapi get-resources --tag-filters Key=Project,Values=grid-meter-cloud --query 'ResourceTagMappingList[].ResourceARN' --output text | tr '\t' '\n'
} 2>&1 | redact > $P/resources.txt
echo "Phase 2 proof captured in $P"
