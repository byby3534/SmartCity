#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
NGINX_CONF_SRC="$REPO_ROOT/deploy/nginx/local.conf"
NGINX_CONF_GEN="/tmp/smartcity-local-nginx.conf"
NGINX_PID_FILE="/tmp/smartcity-nginx.pid"
WWW_DIR="$REPO_ROOT/www"
PID_API="/tmp/smartcity-gunicorn.pid"

stop_nginx() {
  if [[ -f "$NGINX_PID_FILE" ]]; then
    nginx -s stop -c "$NGINX_CONF_GEN" 2>/dev/null \
      || kill "$(cat "$NGINX_PID_FILE")" 2>/dev/null \
      || true
    rm -f "$NGINX_PID_FILE"
  else
    nginx -s stop -c "$NGINX_CONF_GEN" 2>/dev/null || true
  fi
}

free_port_8080() {
  if ! lsof -nP -iTCP:8080 -sTCP:LISTEN >/dev/null 2>&1; then
    return 0
  fi
  echo "Port 8080 is in use — stopping previous listener..." >&2
  lsof -tiTCP:8080 -sTCP:LISTEN | xargs kill 2>/dev/null || true
  sleep 1
  if lsof -nP -iTCP:8080 -sTCP:LISTEN >/dev/null 2>&1; then
    echo "Could not free port 8080. Run: lsof -nP -iTCP:8080 -sTCP:LISTEN" >&2
    return 1
  fi
}

cleanup() {
  stop_nginx
  if [[ -f "$PID_API" ]]; then
    kill "$(cat "$PID_API")" 2>/dev/null || true
    rm -f "$PID_API"
  fi
}
trap cleanup EXIT

mkdir -p "$WWW_DIR"

# Unity index.html이 없고 www가 비어 있을 때만 placeholder 생성
if [[ ! -f "$WWW_DIR/index.html" ]] && [[ ! -d "$WWW_DIR/Build" ]]; then
  cat > "$WWW_DIR/index.html" <<'EOF'
<!doctype html>
<html lang="ko">
  <head><meta charset="utf-8"><title>SmartCity WebGL placeholder</title></head>
  <body>
    <h1>SmartCity WebGL</h1>
    <p>Unity WebGL 빌드 산출물을 <code>www/</code>에 복사하세요.</p>
    <p>API 테스트: <a href="/api/health">/api/health</a></p>
  </body>
</html>
EOF
  echo "Created placeholder $WWW_DIR/index.html"
elif [[ -f "$WWW_DIR/index.html" ]] && grep -q "SmartCity WebGL placeholder" "$WWW_DIR/index.html" 2>/dev/null; then
  echo "WARNING: www/index.html is still the placeholder — copy Unity WebGL build output here." >&2
fi

# Unity index: strip .gz from asset URLs so nginx gzip_static can serve pre-compressed files.
if [[ -f "$WWW_DIR/index.html" ]]; then
  "$REPO_ROOT/deploy/scripts/patch_unity_webgl_index.sh" "$WWW_DIR/index.html" || true
fi

if [[ -d "$WWW_DIR/Build" ]]; then
  "$REPO_ROOT/deploy/scripts/decompress_webgl_build.sh" "$WWW_DIR/Build" || true
fi

SERVER_BLOCK="$(sed "s|__REPO_ROOT__|$REPO_ROOT|g" "$NGINX_CONF_SRC")"

MIME_TYPES=""
for candidate in \
  /opt/homebrew/etc/nginx/mime.types \
  /usr/local/etc/nginx/mime.types \
  /etc/nginx/mime.types; do
  if [[ -f "$candidate" ]]; then
    MIME_TYPES="$candidate"
    break
  fi
done

if [[ -z "$MIME_TYPES" ]]; then
  echo "mime.types not found. Install nginx: brew install nginx" >&2
  exit 1
fi

cat > "$NGINX_CONF_GEN" <<EOF
worker_processes 1;
error_log /tmp/smartcity-nginx-error.log;
pid /tmp/smartcity-nginx.pid;

events {
    worker_connections 1024;
}

http {
    include       $MIME_TYPES;
    default_type  application/octet-stream;
    sendfile      on;

$SERVER_BLOCK
}
EOF

echo "Starting gunicorn..."
"$REPO_ROOT/deploy/scripts/start_api.sh" &
echo $! > "$PID_API"
sleep 3

if ! curl -sf "http://127.0.0.1:${FLASK_PORT:-5001}/health" >/dev/null; then
  echo "API health check failed on port ${FLASK_PORT:-5001}" >&2
  exit 1
fi
echo "API direct: http://127.0.0.1:${FLASK_PORT:-5001}/health OK"

if ! command -v nginx >/dev/null 2>&1; then
  echo "nginx not installed. API only mode." >&2
  echo "Install: brew install nginx" >&2
  wait
  exit 0
fi

stop_nginx
free_port_8080

echo "Starting nginx on http://localhost:8080 ..."
nginx -c "$NGINX_CONF_GEN"

if curl -sf "http://localhost:8080/api/health" >/dev/null; then
  echo "API proxy: http://localhost:8080/api/health OK"
else
  echo "API proxy check failed" >&2
  exit 1
fi

echo ""
echo "Stack ready:"
echo "  WebGL static : http://localhost:8080/"
echo "  API proxy    : http://localhost:8080/api/health"
echo ""
echo "Press Ctrl+C to stop."

wait
