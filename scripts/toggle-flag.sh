#!/usr/bin/env bash
# Flips the tariff-estimates feature flag in AWS AppConfig: scripts/toggle-flag.sh on|off
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/aws-env.sh
STATE="${1:?usage: toggle-flag.sh on|off}"
APP=$(out AppConfigApplicationId); ENV=$(out AppConfigEnvironmentId); PROFILE=$(out AppConfigProfileId); STRATEGY=$(out AppConfigStrategyId)
VALUE=$([ "$STATE" = "on" ] && echo true || echo false)

echo "{\"FeatureManagement\":{\"tariff-estimates\":$VALUE}}" > .local/flag.json
VERSION=$(aws appconfig create-hosted-configuration-version --application-id "$APP" --configuration-profile-id "$PROFILE" \
  --content-type application/json --content fileb://.local/flag.json .local/flag-out.json --query VersionNumber --output text)
echo "created hosted configuration version $VERSION: $(cat .local/flag.json)"

DEPLOYMENT=$(aws appconfig start-deployment --application-id "$APP" --environment-id "$ENV" --configuration-profile-id "$PROFILE" \
  --configuration-version "$VERSION" --deployment-strategy-id "$STRATEGY" --query DeploymentNumber --output text)
for _ in $(seq 1 30); do
  S=$(aws appconfig get-deployment --application-id "$APP" --environment-id "$ENV" --deployment-number "$DEPLOYMENT" --query State --output text)
  echo "deployment $DEPLOYMENT: $S"
  [ "$S" = "COMPLETE" ] && break
  sleep 2
done
echo "AppConfig flag tariff-estimates = $VALUE. The API picks it up on its next poll (within ~15 seconds)."
