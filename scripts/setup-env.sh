#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ -e .env ]]; then
    echo '.env already exists. Edit it directly to preserve existing credentials.' >&2
    exit 1
fi

umask 077
database_password=$(openssl rand -hex 32)
signing_key=$(openssl rand -hex 32)
cat > .env <<EOF
POSTGRES_PASSWORD=$database_password
CONNECTIONSTRINGS__PORTAL="Host=localhost;Port=5433;Database=portal;Username=portal;Password=\${POSTGRES_PASSWORD}"
JWT__SIGNINGKEY=$signing_key
SITE_ADDRESS=localhost
HTTP_PORT=80
HTTPS_PORT=443
EOF
echo 'Created .env with random development secrets.'
