#!/usr/bin/env bash
# Keeps only the most recent vX.Y.Z tags (highest versions; KEEP, default 10) and deletes the
# rest from the remote. Only the git tags are removed: releases and their notes are not touched
# (that is why this pushes the deletion instead of calling the tags API, which refuses to delete
# a tag that still has a release). Tags that aren't vX.Y.Z are left alone.
#
# Env: RELEASE_TOKEN (Forgejo token with write access to the repo), GITHUB_SERVER_URL,
# GITHUB_REPOSITORY, KEEP. With --dry-run it only lists what would be deleted.
set -euo pipefail

dry_run=false
[[ "${1:-}" == "--dry-run" ]] && dry_run=true

keep=${KEEP:-10}

mapfile -t tags < <(git tag --list 'v[0-9]*.[0-9]*.[0-9]*' | sort -rV)

if (( ${#tags[@]} <= keep )); then
  echo "Nothing to prune: ${#tags[@]} tag(s), keeping up to ${keep}."
  exit 0
fi

echo "Keeping: ${tags[*]:0:keep}"
echo "Pruning $(( ${#tags[@]} - keep )) tag(s): ${tags[*]:keep}"

$dry_run && exit 0

remote="${GITHUB_SERVER_URL/\/\//\/\/x-access-token:${RELEASE_TOKEN}@}/${GITHUB_REPOSITORY}.git"

refs=()
for tag in "${tags[@]:keep}"; do refs+=("refs/tags/${tag}"); done

# The token is part of the remote URL: keep it out of the log.
git push "$remote" --delete "${refs[@]}" 2>&1 | sed "s|${RELEASE_TOKEN}|***|g"
