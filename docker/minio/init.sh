#!/bin/sh
set -eu

: "${MINIO_ENDPOINT:?MINIO_ENDPOINT is required}"
: "${MINIO_ROOT_USER:?MINIO_ROOT_USER is required}"
: "${MINIO_ROOT_PASSWORD:?MINIO_ROOT_PASSWORD is required}"
: "${MINIO_BUCKET:?MINIO_BUCKET is required}"
: "${MINIO_APP_ACCESS_KEY:?MINIO_APP_ACCESS_KEY is required}"
: "${MINIO_APP_SECRET_KEY:?MINIO_APP_SECRET_KEY is required}"

# Bucket names are interpolated into the IAM policy below.
case "$MINIO_BUCKET" in
  *[!a-z0-9-]*|-*|*-)
    printf '%s\n' 'MINIO_BUCKET must contain only lowercase letters, digits and internal hyphens.' >&2
    exit 1
    ;;
esac

if [ "${#MINIO_BUCKET}" -lt 3 ] || [ "${#MINIO_BUCKET}" -gt 63 ]; then
  printf '%s\n' 'MINIO_BUCKET must contain between 3 and 63 characters.' >&2
  exit 1
fi

if [ "$MINIO_APP_ACCESS_KEY" = "$MINIO_ROOT_USER" ]; then
  printf '%s\n' 'The application must use a separate MinIO account.' >&2
  exit 1
fi

umask 077
bootstrap_dir="$(mktemp -d)"
trap 'rm -rf "$bootstrap_dir"' EXIT
export MC_CONFIG_DIR="$bootstrap_dir/mc"

mc alias set ngb "$MINIO_ENDPOINT" "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
mc mb --ignore-existing "ngb/$MINIO_BUCKET" >/dev/null
mc anonymous set none "ngb/$MINIO_BUCKET" >/dev/null

# Retain every uploaded object indefinitely. Remove only the former NGB expiry rule.
if lifecycle_result="$(mc --json ilm rule ls "ngb/$MINIO_BUCKET" 2>&1)"; then
  case "$lifecycle_result" in
    *ngb-upload-staging*)
      mc ilm rule rm --id ngb-upload-staging "ngb/$MINIO_BUCKET" >/dev/null
      ;;
  esac
else
  case "$lifecycle_result" in
    *'"Code": "NoSuchLifecycleConfiguration"'*|*'"Code":"NoSuchLifecycleConfiguration"'*)
      ;;
    *)
      printf '%s\n' "$lifecycle_result" >&2
      exit 1
      ;;
  esac
fi

cat > "$bootstrap_dir/attachment-policy.json" <<POLICY
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:GetBucketLocation",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::$MINIO_BUCKET"
      ]
    },
    {
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:PutObject"
      ],
      "Resource": [
        "arn:aws:s3:::$MINIO_BUCKET/uploads/*",
        "arn:aws:s3:::$MINIO_BUCKET/attachments/*"
      ]
    }
  ]
}
POLICY

policy_name="ngb-attachments-$MINIO_BUCKET"
mc admin policy create ngb "$policy_name" "$bootstrap_dir/attachment-policy.json" >/dev/null
mc admin user add ngb "$MINIO_APP_ACCESS_KEY" "$MINIO_APP_SECRET_KEY" >/dev/null
mc admin policy attach ngb "$policy_name" --user "$MINIO_APP_ACCESS_KEY" >/dev/null
