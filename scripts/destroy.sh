#!/usr/bin/env bash
# Phase 3: removes everything Phase 2 created. Idempotent: safe to re-run, and works even if the deploy only half-finished.
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/aws-env.sh
mkdir -p .local

aws sts get-caller-identity >/dev/null || { echo "AWS identity check failed for $AWS_PROFILE"; exit 1; }
ACCOUNT=$(account_id)

stack_status() { aws cloudformation describe-stacks --stack-name "$1" --query 'Stacks[0].StackStatus' --output text 2>/dev/null || echo GONE; }

echo "== 1. empty buckets and delete container images (these block the stack delete) =="
for b in $(aws s3api list-buckets --query "Buckets[?starts_with(Name, 'gridmeter-')].Name" --output text); do
  echo "emptying $b"; empty_bucket "$b"
done
REPO="cdk-$QUALIFIER-container-assets-$ACCOUNT-$AWS_REGION"
if aws ecr describe-repositories --repository-names "$REPO" >/dev/null 2>&1; then
  echo "deleting images in $REPO"
  IDS=$(aws ecr list-images --repository-name "$REPO" --query 'imageIds[*]' --output json)
  [ "$IDS" != "[]" ] && aws ecr batch-delete-image --repository-name "$REPO" --image-ids "$IDS" >/dev/null
fi

echo "== 2. cdk destroy =="
if [ "$(stack_status $STACK)" != "GONE" ]; then
  mkdir -p artifacts/parser-lambda   # the synth needs the asset folder to exist
  ( cd infra && cdk destroy --all --force ) 2>&1 | redact
  # fall back to CloudFormation directly if the CDK app could not synth
  if [ "$(stack_status $STACK)" != "GONE" ]; then
    aws cloudformation delete-stack --stack-name "$STACK"
    aws cloudformation wait stack-delete-complete --stack-name "$STACK"
  fi
else
  echo "stack $STACK already gone"
fi

echo "== 3. secrets (force delete, no recovery window) =="
for arn in $(aws secretsmanager list-secrets --include-planned-deletion --query "SecretList[?Tags[?Key=='Project' && Value=='grid-meter-cloud']].ARN" --output text); do
  aws secretsmanager delete-secret --secret-id "$arn" --force-delete-without-recovery >/dev/null && echo "deleted secret"
done

echo "== 4. bootstrap stack and staging bucket =="
STAGING="cdk-$QUALIFIER-assets-$ACCOUNT-$AWS_REGION"
empty_bucket "$STAGING"
if [ "$(stack_status $TOOLKIT)" != "GONE" ]; then
  aws cloudformation delete-stack --stack-name "$TOOLKIT"
  aws cloudformation wait stack-delete-complete --stack-name "$TOOLKIT"
fi
# the staging bucket is retained by the bootstrap template: remove it explicitly
if aws s3api head-bucket --bucket "$STAGING" >/dev/null 2>&1; then
  empty_bucket "$STAGING"
  aws s3api delete-bucket --bucket "$STAGING" && echo "deleted $STAGING"
fi
if aws ecr describe-repositories --repository-names "$REPO" >/dev/null 2>&1; then
  aws ecr delete-repository --repository-name "$REPO" --force >/dev/null && echo "deleted $REPO"
fi
echo "destroy finished. Run scripts/verify-destroyed.sh"
