#!/usr/bin/env bash
#
# Put a Chromium build in front of the Edge Add-ons reviewers.
#
# Two calls, both asynchronous. The first uploads the package into the draft; the second submits
# that draft for review. Only the second one ever reaches users, and the store refuses it while an
# earlier submission is still being reviewed - which is nothing wrong with this build, so that case
# is reported and left alone rather than failing.
#
# Usage: publish-edge.sh <zip> <version>
# Needs PRODUCT_ID, CLIENT_ID and API_KEY from Partner Center.

set -euo pipefail

zip=${1:?usage: publish-edge.sh <zip> <version>}
version=${2:?usage: publish-edge.sh <zip> <version>}

if [ -z "${PRODUCT_ID:-}" ] || [ -z "${CLIENT_ID:-}" ]; then
  echo "::notice::Edge Add-ons credentials not set - skipping"
  exit 0
fi

api="https://api.addons.microsoftedge.microsoft.com/v1/products/$PRODUCT_ID/submissions"
auth=(-H "Authorization: ApiKey ${API_KEY:-}" -H "X-ClientID: $CLIENT_ID")

# The upload answers 202 with an operation id in a Location header, and the package is not part of
# the draft until that operation succeeds. Submitting immediately afterwards races the store.
operation=$(curl -fsS -D - -o /dev/null -X POST "$api/draft/package" "${auth[@]}" \
  -H "Content-Type: application/zip" \
  --data-binary "@$zip" \
  | tr -d '\r' | sed -n 's/^[Ll]ocation: //p')

if [ -z "$operation" ]; then
  echo "::error::the Edge upload returned no operation to wait on"
  exit 1
fi

# Five minutes is generous for a package this size, and finite - a stuck operation should fail
# rather than hold the release open.
for _ in $(seq 1 60); do
  status=$(curl -fsS "$api/draft/package/operations/$operation" "${auth[@]}")
  echo "$status"

  case "$status" in
    *'"status":"Succeeded"'*) break ;;
    *'"status":"Failed"'*) echo "::error::Edge rejected the package"; exit 1 ;;
  esac

  sleep 5
done

# Submitting is not publishing: the store shows the reviewed version, and a review takes hours or
# days. Saying which of the two happened is the whole point - a submission the store refused looked
# exactly like one it accepted.
submission=$(curl -fsS -D - -o /dev/null -X POST "$api" "${auth[@]}" \
  -H "Content-Type: application/json" \
  -d "{\"notes\":\"QuickRun $version\"}" \
  | tr -d '\r' | sed -n 's/^[Ll]ocation: //p')

if [ -z "$submission" ]; then
  echo "::warning::the store took the submission but named no operation to check"
  exit 0
fi

for _ in $(seq 1 24); do
  state=$(curl -fsS "$api/operations/$submission" "${auth[@]}")

  case "$state" in
    *'"status":"Succeeded"'*)
      echo "::notice::QuickRun $version is submitted to the Edge store - it goes live when review passes"
      exit 0 ;;

    # One submission at a time. The store is still reviewing an earlier one, so this build cannot
    # go in yet; edge-catchup.yml comes back for it once that review is over.
    *'"errorCode":"InProgressSubmission"'*)
      echo "::warning::the Edge store is still reviewing an earlier submission - QuickRun $version will be submitted again by the daily catch-up"
      exit 0 ;;

    *'"status":"Failed"'*)
      echo "::error::the Edge store refused the submission: $state"
      exit 1 ;;
  esac

  sleep 5
done

echo "::warning::the submission was still processing after two minutes: $state"
