#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  ./scripts/run-k6.sh --env-file <path> --test <path> [--output local|cloud|prometheus-remote-write] [--summary-export <path>]

Options:
  --env-file, -e          Path to a local env file.
  --test, -t              k6 test file to run.
  --output, -o            Output mode. Defaults to local. Cloud mode uses local execution.
  --summary-export, -s    Optional k6 summary JSON export path.
  --help, -h              Show this help.
USAGE
}

ENV_FILE=""
TEST_FILE=""
OUTPUT_MODE="local"
SUMMARY_EXPORT=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env-file|-e)
      ENV_FILE="${2:-}"
      shift 2
      ;;
    --test|-t)
      TEST_FILE="${2:-}"
      shift 2
      ;;
    --output|-o)
      OUTPUT_MODE="${2:-}"
      shift 2
      ;;
    --summary-export|-s)
      SUMMARY_EXPORT="${2:-}"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ -z "$ENV_FILE" || -z "$TEST_FILE" ]]; then
  usage >&2
  exit 2
fi

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Env file not found: $ENV_FILE" >&2
  exit 1
fi

if [[ ! -f "$TEST_FILE" ]]; then
  echo "Test file not found: $TEST_FILE" >&2
  exit 1
fi

if ! command -v k6 >/dev/null 2>&1; then
  echo "k6 is not installed or is not on PATH." >&2
  exit 1
fi

case "$OUTPUT_MODE" in
  local|cloud|prometheus-remote-write)
    ;;
  *)
    echo "Unsupported output mode: $OUTPUT_MODE" >&2
    exit 2
    ;;
esac

load_env_file() {
  local file="$1"
  while IFS= read -r line || [[ -n "$line" ]]; do
    [[ -z "$line" || "$line" =~ ^[[:space:]]*# ]] && continue
    [[ "$line" != *"="* ]] && continue

    local key="${line%%=*}"
    local value="${line#*=}"
    key="$(printf '%s' "$key" | xargs)"

    if [[ ! "$key" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]]; then
      echo "Invalid env key in $file: $key" >&2
      exit 1
    fi

    if [[ -n "${!key+x}" ]]; then
      continue
    fi

    value="${value%$'\r'}"
    if [[ "$value" =~ ^\".*\"$ || "$value" =~ ^\'.*\'$ ]]; then
      value="${value:1:${#value}-2}"
    fi

    export "$key=$value"
  done < "$file"
}

load_env_file "$ENV_FILE"

if [[ -n "$SUMMARY_EXPORT" ]]; then
  export NGB_K6_SUMMARY_EXPORT="$SUMMARY_EXPORT"
elif [[ -z "${NGB_K6_SUMMARY_EXPORT:-}" || "${NGB_K6_SUMMARY_EXPORT}" == "artifacts/k6-summary.json" ]]; then
  TEST_PACKAGE="${TEST_FILE%%/*}"
  TEST_NAME="$(basename "$TEST_FILE" .ts)"
  RUN_TIMESTAMP="$(date -u +%Y%m%dT%H%M%SZ)"
  export NGB_K6_SUMMARY_EXPORT="artifacts/${TEST_PACKAGE}-${TEST_NAME}-${RUN_TIMESTAMP}.summary.json"
fi

if [[ -n "${NGB_K6_SUMMARY_EXPORT:-}" ]]; then
  SUMMARY_DIR="$(dirname "$NGB_K6_SUMMARY_EXPORT")"
  if [[ "$SUMMARY_DIR" != "." ]]; then
    mkdir -p "$SUMMARY_DIR"
  fi
fi

echo "Starting k6: test=$TEST_FILE output=$OUTPUT_MODE env_file=$ENV_FILE summary_export=${NGB_K6_SUMMARY_EXPORT:-none}"

case "$OUTPUT_MODE" in
  local)
    k6 run "$TEST_FILE"
    ;;
  cloud)
    k6 cloud run --local-execution --include-system-env-vars "$TEST_FILE"
    ;;
  prometheus-remote-write)
    if [[ -z "${K6_PROMETHEUS_RW_SERVER_URL:-}" ]]; then
      echo "K6_PROMETHEUS_RW_SERVER_URL must be set for prometheus-remote-write output." >&2
      exit 1
    fi
    k6 run -o experimental-prometheus-rw "$TEST_FILE"
    ;;
esac
