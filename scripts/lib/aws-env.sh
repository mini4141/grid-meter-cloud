# Sourced by the cloud scripts: pins the AWS profile and region (CLAUDE.md section 4).
export AWS_PROFILE=grid-meter-cloud-deployer
export AWS_REGION=ap-southeast-2
export AWS_DEFAULT_REGION=ap-southeast-2
export AWS_PAGER=""
export CDK_DISABLE_VERSION_CHECK=1
export MSYS_NO_PATHCONV=1
export PATH="$PATH:/c/Program Files/dotnet:$HOME/.dotnet/tools"

STACK=GridMeter
TOOLKIT=CDKToolkit-grid-meter-cloud
QUALIFIER=gridmtr

# Strips ANSI colours and redacts account ids, access keys and tokens before anything is saved.
redact() {
  sed -E 's/\x1b\[[0-9;]*[A-Za-z]//g; s/([^0-9]|^)[0-9]{12}([^0-9]|$)/\1<ACCOUNT_ID>\2/g; s/(AKIA|ASIA)[A-Z0-9]{16}/<ACCESS_KEY>/g; s/(Bearer |X-Amz-Signature=|X-Amz-Security-Token=)[A-Za-z0-9%._~+\/=-]+/\1<REDACTED>/g'
}

account_id() { aws sts get-caller-identity --query Account --output text; }

# Reads a stack output saved by deploy.sh.
out() { node -pe "JSON.parse(require('fs').readFileSync('.local/cdk-outputs.json','utf8')).$STACK['$1']"; }

# Empties a (possibly versioned) bucket. Safe to call on a missing bucket.
empty_bucket() {
  local b=$1
  aws s3api head-bucket --bucket "$b" >/dev/null 2>&1 || return 0
  aws s3 rm "s3://$b" --recursive >/dev/null 2>&1 || true
  for _ in $(seq 1 50); do
    local payload
    payload=$(aws s3api list-object-versions --bucket "$b" --output json | node -e '
      const d = JSON.parse(require("fs").readFileSync(0, "utf8"));
      const o = [...(d.Versions || []), ...(d.DeleteMarkers || [])].slice(0, 500).map(v => ({ Key: v.Key, VersionId: v.VersionId }));
      process.stdout.write(JSON.stringify({ Objects: o, Quiet: true }));')
    [ "$payload" = '{"Objects":[],"Quiet":true}' ] && return 0
    aws s3api delete-objects --bucket "$b" --delete "$payload" >/dev/null
  done
}
