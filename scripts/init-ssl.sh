#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# Initialize Let's Encrypt SSL certificate for the first time.
# Run ONCE after setup-server.sh and BEFORE starting the full stack.
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

source /opt/boylikaI/.env

DOMAIN="${DOMAIN:?DOMAIN not set in .env}"
EMAIL="${LETSENCRYPT_EMAIL:?LETSENCRYPT_EMAIL not set in .env}"
APP_DIR="/opt/boylikaI"

cd "$APP_DIR"

echo "==> Starting temporary Nginx for ACME challenge..."
docker compose -f docker-compose.prod.yml up -d nginx

echo "==> Requesting certificate for ${DOMAIN}..."
docker run --rm \
  -v "${APP_DIR}/docker/certbot/www:/var/www/certbot" \
  -v "${APP_DIR}/docker/certbot/conf:/etc/letsencrypt" \
  certbot/certbot certonly \
    --webroot \
    --webroot-path /var/www/certbot \
    --email "$EMAIL" \
    --agree-tos \
    --no-eff-email \
    --force-renewal \
    -d "$DOMAIN"

echo "==> Reloading Nginx with SSL..."
docker compose -f docker-compose.prod.yml exec nginx nginx -s reload

echo "==> Setting up certificate auto-renewal cron job..."
(crontab -l 2>/dev/null; echo "0 3 * * * cd $APP_DIR && docker compose -f docker-compose.prod.yml exec certbot certbot renew --quiet && docker compose -f docker-compose.prod.yml exec nginx nginx -s reload") | crontab -

echo ""
echo "✅ SSL certificate installed for ${DOMAIN}"
echo "   Auto-renewal: daily at 03:00 via cron"
