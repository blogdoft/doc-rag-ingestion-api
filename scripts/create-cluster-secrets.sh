#!/usr/bin/env bash
# Creates (or updates) the two Secrets the Deployments in .eng/k8s expect. Idempotent.
# Usage: POSTGRES_CONNECTION_STRING=... GARAGE_ACCESS_KEY=... GARAGE_SECRET_KEY=... \
#          scripts/create-cluster-secrets.sh [namespace]
set -euo pipefail

namespace="${1:-code-brain}"
: "${POSTGRES_CONNECTION_STRING:?Npgsql connection string is required}"
: "${GARAGE_ACCESS_KEY:?Garage access key id is required}"
: "${GARAGE_SECRET_KEY:?Garage secret key is required}"

apply_secret() {
  kubectl create secret generic "$@" --namespace "$namespace" --dry-run=client -o yaml | kubectl apply -f -
}

apply_secret doc-rag-ingestion-secrets \
  --from-literal=connection-string="$POSTGRES_CONNECTION_STRING"

apply_secret doc-rag-ingestion-garage-secrets \
  --from-literal=access-key="$GARAGE_ACCESS_KEY" \
  --from-literal=secret-key="$GARAGE_SECRET_KEY"
