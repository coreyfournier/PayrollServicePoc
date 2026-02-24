#!/bin/sh
# Generate runtime environment config from environment variables
cat <<EOF > /usr/share/nginx/html/env-config.js
window.__ENV__ = {
  VITE_AUTH_ENABLED: "${VITE_AUTH_ENABLED:-true}"
};
EOF

exec nginx -g 'daemon off;'
