#!/usr/bin/env bash
# Creates the next release of the commit being built: derives the SemVer bump from the
# conventional commits since the last v*.*.* tag, then creates the tag and a release with
# generated notes through the Forgejo API (the API creates the tag at the commit).
#
# Bump rules (highest wins): `type!:` or a `BREAKING CHANGE:` footer -> major; `feat` -> minor;
# anything else (fix, perf, docs, chore, ...) -> patch. Every commit on main is a release.
#
# Outputs `tag` (vX.Y.Z) and `version` (X.Y.Z) to $GITHUB_OUTPUT. A commit that already carries
# a version tag (a re-run) reuses it instead of creating another.
#
# Env: RELEASE_TOKEN (Forgejo token that can write releases), GITHUB_SERVER_URL,
# GITHUB_REPOSITORY. With --dry-run it only prints the tag and notes.
set -euo pipefail

dry_run=false
[[ "${1:-}" == "--dry-run" ]] && dry_run=true

readonly tag_glob='v[0-9]*.[0-9]*.[0-9]*'

emit() {
  echo "tag=$1" "version=${1#v}"
  [[ -n "${GITHUB_OUTPUT:-}" ]] && printf 'tag=%s\nversion=%s\n' "$1" "${1#v}" >> "$GITHUB_OUTPUT"
  return 0
}

sha=$(git rev-parse HEAD)

existing=$(git tag --points-at "$sha" --list "$tag_glob" | sort -V | tail -1)
if [[ -n "$existing" ]]; then
  echo "HEAD already carries $existing; reusing it."
  emit "$existing"
  exit 0
fi

previous=$(git describe --tags --abbrev=0 --match "$tag_glob" 2>/dev/null || true)
if [[ -n "$previous" ]]; then
  range="${previous}..HEAD"
  base="${previous#v}"
else
  range="HEAD"
  base="0.0.0"
fi

mapfile -t commits < <(git log --no-merges --format=%H "$range")
if (( ${#commits[@]} == 0 )); then
  echo "No commits since ${previous:-the start of history}." >&2
  exit 1
fi

bump=patch
breaking="" features="" fixes="" perfs="" others=""

for commit in "${commits[@]}"; do
  subject=$(git log -1 --format=%s "$commit")
  short=$(git rev-parse --short "$commit")
  type=other scope="" bang="" desc="$subject"

  if [[ "$subject" =~ ^([A-Za-z]+)(\(([^\)]*)\))?(!)?:[[:space:]]*(.+)$ ]]; then
    type="${BASH_REMATCH[1],,}"
    scope="${BASH_REMATCH[3]}"
    bang="${BASH_REMATCH[4]}"
    desc="${BASH_REMATCH[5]}"
  fi

  entry="- "
  [[ -n "$scope" ]] && entry+="**${scope}:** "
  entry+="${desc} (${short})"$'\n'

  is_breaking=false
  [[ -n "$bang" ]] && is_breaking=true
  git log -1 --format=%B "$commit" | grep -qE '^BREAKING[ -]CHANGE:' && is_breaking=true

  if $is_breaking; then
    breaking+="$entry"
    bump=major
  fi

  case "$type" in
    feat) features+="$entry"; [[ "$bump" == patch ]] && bump=minor ;;
    fix) fixes+="$entry" ;;
    perf) perfs+="$entry" ;;
    *) $is_breaking || others+="$entry" ;;
  esac
done

IFS=. read -r major minor patch <<< "$base"
case "$bump" in
  major) major=$((major + 1)); minor=0; patch=0 ;;
  minor) minor=$((minor + 1)); patch=0 ;;
  patch) patch=$((patch + 1)) ;;
esac
tag="v${major}.${minor}.${patch}"

notes=""
section() { [[ -n "$2" ]] && notes+="### $1"$'\n\n'"$2"$'\n'; return 0; }
section "Breaking changes" "$breaking"
section "Features" "$features"
section "Bug fixes" "$fixes"
section "Performance" "$perfs"
section "Other changes" "$others"
if [[ -n "$previous" ]]; then
  notes+="**Full changelog:** ${GITHUB_SERVER_URL:-}/${GITHUB_REPOSITORY:-}/compare/${previous}...${tag}"$'\n'
fi

echo "Releasing ${tag} (${bump} bump from ${previous:-nothing})"

if $dry_run; then
  printf '\n%s\n' "$notes"
  exit 0
fi

payload=$(jq -n --arg tag "$tag" --arg sha "$sha" --arg body "$notes" \
  '{tag_name: $tag, target_commitish: $sha, name: $tag, body: $body}')

curl -fsS --fail-with-body -X POST \
  -H "Authorization: token ${RELEASE_TOKEN}" \
  -H "Content-Type: application/json" \
  -d "$payload" \
  "${GITHUB_SERVER_URL}/api/v1/repos/${GITHUB_REPOSITORY}/releases" > /dev/null

emit "$tag"
