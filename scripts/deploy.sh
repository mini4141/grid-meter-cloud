#!/usr/bin/env bash
# Phase 2: builds, bootstraps (qualifier gridmtr), deploys the CDK stack, migrates the DB with a one-off ECS task
# and uploads the SPA. Credentials come only from the grid-meter-cloud-deployer profile.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/aws-env.sh
LOG=proof/phase2-cloud/logs
mkdir -p "$LOG" .local

aws sts get-caller-identity >/dev/null || { echo "AWS identity check failed for $AWS_PROFILE"; exit 1; }
ACCOUNT=$(account_id)
echo "Deploying to account <ACCOUNT_ID>, region $AWS_REGION"

echo "== 1. nuke Publish DockerBuild =="
nuke Compile Publish DockerBuild --with-integration false
( cd web && pnpm install --frozen-lockfile && pnpm build )

echo "== 2. cdk bootstrap =="
( cd infra
  cdk bootstrap "aws://$ACCOUNT/$AWS_REGION" --qualifier "$QUALIFIER" --toolkit-stack-name "$TOOLKIT" \
    --tags Project=grid-meter-cloud --tags Ephemeral=true --tags ManagedBy=cdk --tags Owner=alvieirvansyah ) 2>&1 | redact | tee "$LOG/cdk-bootstrap.log"

echo "== 3. cdk deploy =="
( cd infra
  cdk deploy --all --require-approval never --outputs-file ../.local/cdk-outputs.json ) 2>&1 | redact | tee "$LOG/cdk-deploy.log"

echo "== 4. EF migrations through a one-off ECS task =="
TASK=$(aws ecs run-task --cluster "$(out ClusterName)" --launch-type FARGATE --task-definition "$(out MigrateTaskDefinition)" \
  --network-configuration "awsvpcConfiguration={subnets=[$(out PublicSubnets)],securityGroups=[$(out MigrateSecurityGroup)],assignPublicIp=ENABLED}" \
  --tags key=Project,value=grid-meter-cloud key=Ephemeral,value=true key=ManagedBy,value=cdk key=Owner,value=alvieirvansyah \
  --query 'tasks[0].taskArn' --output text)
aws ecs wait tasks-stopped --cluster "$(out ClusterName)" --tasks "$TASK"
EXIT=$(aws ecs describe-tasks --cluster "$(out ClusterName)" --tasks "$TASK" --query 'tasks[0].containers[0].exitCode' --output text)
sleep 5
aws logs tail "$(out MigrateLogGroup)" --since 30m 2>&1 | redact > "$LOG/ef-migrations-cloud.log" || true
[ "$EXIT" = "0" ] || { echo "migration task failed (exit $EXIT)"; cat "$LOG/ef-migrations-cloud.log"; exit 1; }
echo "migrations applied"

echo "== 5. upload the SPA =="
aws s3 sync web/dist "s3://$(out SpaBucket)" --delete
aws cloudfront create-invalidation --distribution-id "$(out DistributionId)" --paths '/*' >/dev/null

echo "CLOUD_URL=$(out CloudFrontUrl)" > .local/cloud.env
echo "Deployed: $(out CloudFrontUrl)"
echo "Waiting for the API behind CloudFront..."
for _ in $(seq 1 60); do
  curl -fs "$(out CloudFrontUrl)/api/admin/info" >/dev/null && break
  sleep 5
done
echo "Next: scripts/capture-proof.sh phase2   then   scripts/destroy.sh"
