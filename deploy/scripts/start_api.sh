#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

export FLASK_DEBUG="${FLASK_DEBUG:-0}"

if ! command -v gunicorn >/dev/null 2>&1; then
  echo "gunicorn not found. Run: pip install -r requirements.txt" >&2
  exit 1
fi

exec gunicorn -c "$REPO_ROOT/deploy/gunicorn/gunicorn.conf.py"
