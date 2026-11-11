#!/bin/sh
set -eu
url=${BACKEND_URL:-http://localhost:8080}
escaped=$(printf '%s' "$url" | sed 's/[\\"]/\\&/g')
printf 'window.__CONFIG__ = { API_URL: "%s" };\n' "$escaped" > /usr/share/nginx/html/config.js
