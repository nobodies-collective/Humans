#!/usr/bin/env bash
# QA deployment script for the NUC.
# Sets SOURCE_COMMIT so the footer shows the git hash, then rebuilds and starts.
#
# Usage:
#   ./deploy-qa.sh

set -euo pipefail
cd "$(dirname "$0")"

git pull --ff-only

export SOURCE_COMMIT
SOURCE_COMMIT=$(git rev-parse --short HEAD)

docker compose up --build -d
echo "Deployed $SOURCE_COMMIT to QA"
