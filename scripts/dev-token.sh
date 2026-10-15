#!/usr/bin/env bash
# Mints an HS256 JWT for LOCAL DEVELOPMENT ONLY, matching appsettings.Development.json.
# Usage: scripts/dev-token.sh [scopes] [ttl-seconds]
set -euo pipefail

scopes="${1:-ingestion:read ingestion:write}"
ttl="${2:-3600}"
key="${AUTH_SIGNING_KEY:-dev-only-signing-key-change-me-0123456789}"
issuer="${AUTH_ISSUER:-doc-rag-ingestion-dev}"
audience="${AUTH_AUDIENCE:-doc-rag-ingestion}"

b64() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }
now=$(date +%s)
header=$(printf '{"alg":"HS256","typ":"JWT"}' | b64)
payload=$(printf '{"iss":"%s","aud":"%s","sub":"dev-user","preferred_username":"dev-user","scope":"%s","iat":%s,"exp":%s}' \
  "$issuer" "$audience" "$scopes" "$now" "$((now + ttl))" | b64)
signature=$(printf '%s.%s' "$header" "$payload" | openssl dgst -sha256 -hmac "$key" -binary | b64)
printf '%s.%s.%s\n' "$header" "$payload" "$signature"
