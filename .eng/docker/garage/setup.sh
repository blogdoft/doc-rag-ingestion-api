#!/bin/sh
# Idempotent one-shot Garage bootstrap: layout, access key and the (private) bucket.
# Production buckets are created by the platform team; the service never creates them itself.
set -eu

GARAGE="/garage -c /etc/garage.toml"
: "${GARAGE_ACCESS_KEY:?}" "${GARAGE_SECRET_KEY:?}" "${GARAGE_BUCKET:?}"

NODE_ID=$($GARAGE status 2>/dev/null | awk '/^[0-9a-f]{16} /{print $1; exit}')
if $GARAGE layout show 2>/dev/null | grep -q "No nodes have a role"; then
  $GARAGE layout assign -z dc1 -c 1G "$NODE_ID"
  $GARAGE layout apply --version 1
  sleep 2
fi

$GARAGE key info "$GARAGE_ACCESS_KEY" >/dev/null 2>&1 || $GARAGE key import --yes "$GARAGE_ACCESS_KEY" "$GARAGE_SECRET_KEY" -n docrag
$GARAGE bucket info "$GARAGE_BUCKET" >/dev/null 2>&1 || $GARAGE bucket create "$GARAGE_BUCKET"
$GARAGE bucket allow --read --write --owner "$GARAGE_BUCKET" --key docrag
echo "Garage ready: bucket '$GARAGE_BUCKET' for key '$GARAGE_ACCESS_KEY'"
