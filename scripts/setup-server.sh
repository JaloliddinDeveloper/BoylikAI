#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# BoylikAI — DigitalOcean Ubuntu Server Initial Setup
# Run once as root on a fresh Ubuntu 22.04/24.04 droplet:
#   curl -fsSL https://raw.githubusercontent.com/JaloliddinDeveloper/BoylikAI/master/scripts/setup-server.sh | bash
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

DOMAIN="${DOMAIN:-api.fastergo.uz}"
APP_USER="boylikaI"
APP_DIR="/opt/boylikaI"
GITHUB_REPO="${GITHUB_REPO:-JaloliddinDeveloper/BoylikAI}"

echo "==> [1/8] System update"
apt-get update -qq && apt-get upgrade -y -qq

echo "==> [2/8] Install prerequisites"
apt-get install -y -qq \
  curl wget git ufw fail2ban \
  ca-certificates gnupg lsb-release

echo "==> [3/8] Install Docker Engine"
# Use the official convenience script — supports all Ubuntu versions including 25.x
curl -fsSL https://get.docker.com | sh
systemctl enable docker
systemctl start docker

echo "==> [4/8] Configure UFW firewall"
ufw --force reset
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp    comment "SSH"
ufw allow 80/tcp    comment "HTTP"
ufw allow 443/tcp   comment "HTTPS"
ufw --force enable
ufw status verbose

echo "==> [5/8] Configure fail2ban (SSH brute-force protection)"
cat > /etc/fail2ban/jail.local << 'EOF'
[sshd]
enabled  = true
port     = ssh
filter   = sshd
logpath  = /var/log/auth.log
maxretry = 5
bantime  = 1h
EOF
systemctl enable fail2ban
systemctl restart fail2ban

echo "==> [6/8] Create app user and directory"
useradd -r -s /bin/bash -d "$APP_DIR" "$APP_USER" 2>/dev/null || true
usermod -aG docker "$APP_USER"
mkdir -p "$APP_DIR"
cd "$APP_DIR"

echo "==> [7/8] Clone repository"
if [ ! -d ".git" ]; then
  git clone "https://github.com/${GITHUB_REPO}.git" .
fi
chown -R "$APP_USER:$APP_USER" "$APP_DIR"

echo "==> [8/8] Create .env from template"
if [ ! -f .env ]; then
  cp .env.production.example .env
  echo ""
  echo "⚠️  Edit /opt/boylikaI/.env with your secrets before starting:"
  echo "   nano /opt/boylikaI/.env"
fi

echo ""
echo "══════════════════════════════════════════════════════════════"
echo " Server setup complete!"
echo ""
echo " Next steps:"
echo " 1. Edit secrets:    nano /opt/boylikaI/.env"
echo " 2. Init SSL:        bash /opt/boylikaI/scripts/init-ssl.sh"
echo " 3. Start services:  cd /opt/boylikaI && docker compose -f docker-compose.prod.yml up -d"
echo "══════════════════════════════════════════════════════════════"
