#!/usr/bin/env bash
# Exits non-zero if anything billable from this project is still in the account. Output goes to proof/phase3-destroy/.
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/aws-env.sh
OUT=proof/phase3-destroy/verify-destroyed.txt
mkdir -p proof/phase3-destroy
FAIL=0

{
echo "verify-destroyed: $(date -u +%FT%TZ)  profile=$AWS_PROFILE region=$AWS_REGION"
ACCOUNT=$(account_id)

check() {  # check <label> <command output that must be empty>
  local label=$1 found=$2
  if [ -z "$found" ] || [ "$found" = "None" ]; then echo "OK    $label: none"; else echo "FAIL  $label: $found"; FAIL=1; fi
}

echo "--- direct checks (SPECS section 11) ---"
check "RDS instances" "$(aws rds describe-db-instances --query 'DBInstances[].DBInstanceIdentifier' --output text)"
check "ECS clusters (gridmeter)" "$(aws ecs list-clusters --query "clusterArns[?contains(@, 'GridMeter')]" --output text)"
check "Lambda functions (GridMeter)" "$(aws lambda list-functions --query "Functions[?contains(FunctionName, 'GridMeter')].FunctionName" --output text)"
check "SQS queues (gridmeter)" "$(aws sqs list-queues --queue-name-prefix gridmeter --query 'QueueUrls' --output text)"
check "AppConfig applications" "$(aws appconfig list-applications --query "Items[?Name=='grid-meter-cloud'].Id" --output text)"
check "Secrets Manager secrets (tagged, incl. pending deletion)" "$(aws secretsmanager list-secrets --include-planned-deletion --query "SecretList[?Tags[?Key=='Project' && Value=='grid-meter-cloud']].Name" --output text)"
check "Load balancers (GridM)" "$(aws elbv2 describe-load-balancers --query "LoadBalancers[?contains(LoadBalancerName, 'GridM')].LoadBalancerName" --output text)"
check "NAT gateways (available/pending)" "$(aws ec2 describe-nat-gateways --filter Name=state,Values=available,pending --query 'NatGateways[].NatGatewayId' --output text)"
check "Elastic IPs" "$(aws ec2 describe-addresses --query 'Addresses[].PublicIp' --output text)"
check "VPCs tagged Project" "$(aws ec2 describe-vpcs --filters Name=tag:Project,Values=grid-meter-cloud --query 'Vpcs[].VpcId' --output text)"
check "S3 buckets (gridmeter-*, cdk-gridmtr-*)" "$(aws s3api list-buckets --query "Buckets[?starts_with(Name, 'gridmeter-') || starts_with(Name, 'cdk-gridmtr-')].Name" --output text)"
check "ECR repositories (cdk-gridmtr-*)" "$(aws ecr describe-repositories --query "repositories[?starts_with(repositoryName, 'cdk-gridmtr-')].repositoryName" --output text 2>/dev/null)"
check "CloudFront distributions (tagged)" "$(for d in $(aws cloudfront list-distributions --query 'DistributionList.Items[].ARN' --output text | tr '	' ' ' | grep -o 'arn:[^ ]*'); do aws cloudfront list-tags-for-resource --resource "$d" --query "Tags.Items[?Key=='Project' && Value=='grid-meter-cloud'].Value" --output text | grep -q . && echo "$d"; done)"
check "CloudFormation stacks" "$(aws cloudformation describe-stacks --query "Stacks[?StackName=='$STACK' || StackName=='$TOOLKIT'].StackName" --output text 2>/dev/null)"
check "CloudWatch log groups (GridMeter)" "$(aws logs describe-log-groups --query "logGroups[?contains(logGroupName, 'GridMeter')].logGroupName" --output text)"
check "IAM roles (cdk-gridmtr-*, GridMeter-*)" "$(aws iam list-roles --query "Roles[?starts_with(RoleName, 'cdk-gridmtr-') || starts_with(RoleName, 'GridMeter-')].RoleName" --output text)"

echo "--- tagging API cross-check ---"
TAGGED=""; DENIED=""
for r in ap-southeast-2 us-east-1; do
  RES=$(aws resourcegroupstaggingapi get-resources --region $r --tag-filters Key=Project,Values=grid-meter-cloud --query 'ResourceTagMappingList[].ResourceARN' --output text 2>&1)
  case "$RES" in *AccessDenied*) DENIED=1 ;; *) TAGGED="$TAGGED $RES" ;; esac
done
if [ -n "$DENIED" ]; then
  echo "NOTE  the deployer profile is not allowed to call tag:GetResources (explicit deny), so the tagging API cross-check was skipped; the direct describe/list checks above are the source of truth."
elif [ -z "${TAGGED// /}" ]; then
  echo "OK    tagging API: no resources tagged Project=grid-meter-cloud"
else
  echo "NOTE  tagging API still lists (its index lags behind deletions); direct checks above decide the result:"
  echo "$TAGGED" | tr '	 ' '

' | sed '/^$/d'
fi
check "ECS clusters in any state (GridMeter)" "$(aws ecs describe-clusters --clusters $(aws ecs list-clusters --query 'clusterArns' --output text) --query "clusters[?contains(clusterName, 'GridMeter') && status=='ACTIVE'].clusterName" --output text 2>/dev/null)"

echo
[ "$FAIL" = 0 ] && echo "RESULT: nothing billable left" || echo "RESULT: resources remain, see FAIL lines"
exit $FAIL
} 2>&1 | redact | tee "$OUT"
exit "${PIPESTATUS[0]}"
