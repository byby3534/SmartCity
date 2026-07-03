# Start Flask API on Windows (gunicorn is not supported on Windows).
# Usage: .\deploy\scripts\start_api.ps1
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $RepoRoot

if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    Write-Error "python not found. Install Python 3 and run: pip install -r requirements.txt"
}

if (-not $env:FLASK_DEBUG) { $env:FLASK_DEBUG = "0" }
if (-not $env:FLASK_PORT) { $env:FLASK_PORT = "5001" }

Write-Host "Starting Flask API on http://127.0.0.1:$($env:FLASK_PORT) ..."
python -m python.api.flask_app
