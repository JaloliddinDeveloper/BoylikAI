#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# Manual deploy script — use when CI/CD is unavailable or for hotfixes.
# Run on the DigitalOcean server as the boylikaI user:
#   bash /opt/boylikaI/scripts/deploy.sh [image_tag]
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

APP_DIR="/opt/boylikaI"
IMAGE_TAG="${1:-latest}"
COMPOSE="docker compose -f ${APP_DIR}/docker-compose.prod.yml"

cd "$APP_DIR"

echo "==> [1/5] Pulling latest config from git..."
git fetch origin main
git reset --hard origin/main

echo "==> [2/5] Pulling Docker images (tag: ${IMAGE_TAG})..."
export IMAGE_TAG
$COMPOSE pull api bot

echo "==> [3/5] Running database migrations..."
$COMPOSE run --rm migrate

echo "==> [4/5] Restarting services..."
$COMPOSE up -d --remove-orphans --no-deps api bot nginx

echo "==> [5/5] Waiting for health check..."
TIMEOUT=120
ELAPSED=0
until docker exec boylikaI-api wget -qO- http://localhost:8080/health > /dev/null 2>&1; do
  if [ "$ELAPSED" -ge "$TIMEOUT" ]; then
    echo "❌ Health check timed out after ${TIMEOUT}s"
    echo "   Logs:"
    docker logs boylikaI-api --tail=50
    exit 1
  fi
  sleep 3
  ELAPSED=$((ELAPSED + 3))
done

docker image prune -f

echo ""
echo "✅ Deployment complete — tag: ${IMAGE_TAG}"
echo "   $(date '+%Y-%m-%d %H:%M:%S UTC')"
