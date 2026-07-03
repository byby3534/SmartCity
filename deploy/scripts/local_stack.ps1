# Windows local stack: Flask API + nginx (WebGL + /api proxy).
# Prerequisites: Python 3, pip install -r requirements.txt, nginx for Windows.
#
# Usage:
#   .\deploy\scripts\local_stack.ps1
#   .\deploy\scripts\local_stack.ps1 -NginxRoot "D:\nginx"
param(
    [string]$NginxRoot = "C:\nginx"
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$WwwDir = Join-Path $RepoRoot "www"
$NginxConfSrc = Join-Path $RepoRoot "deploy\nginx\local.windows.conf"
$NginxConfGen = Join-Path $env:TEMP "smartcity-local-nginx.conf"
$NginxErrorLog = Join-Path $env:TEMP "smartcity-nginx-error.log"
$NginxPidFile = Join-Path $env:TEMP "smartcity-nginx.pid"
$ApiJob = $null
$NginxExe = $null

function Convert-ToNginxPath([string]$Path) {
    return ($Path -replace '\\', '/')
}

function Find-NginxExe {
    param([string]$Root)
    $candidates = @(
        (Join-Path $Root "nginx.exe"),
        "nginx.exe"
    )
    foreach ($c in $candidates) {
        if ($c -eq "nginx.exe") {
            $cmd = Get-Command nginx -ErrorAction SilentlyContinue
            if ($cmd) { return $cmd.Source }
        } elseif (Test-Path $c) {
            return (Resolve-Path $c).Path
        }
    }
    return $null
}

function Stop-NginxStack {
    param([string]$Exe, [string]$Conf)
    if (-not $Exe) { return }
    if (Test-Path $Conf) {
        & $Exe -s stop -c $Conf 2>$null
    }
    if (Test-Path $NginxPidFile) {
        $pidText = Get-Content $NginxPidFile -ErrorAction SilentlyContinue
        if ($pidText -match '^\d+$') {
            Stop-Process -Id ([int]$pidText) -Force -ErrorAction SilentlyContinue
        }
        Remove-Item $NginxPidFile -Force -ErrorAction SilentlyContinue
    }
}

function Free-Port8080 {
    try {
        $listeners = Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue
        foreach ($l in $listeners) {
            Write-Host "Port 8080 in use (PID $($l.OwningProcess)) — stopping..." -ForegroundColor Yellow
            Stop-Process -Id $l.OwningProcess -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Seconds 1
    } catch {
        # Get-NetTCPConnection may require admin; ignore and let nginx -t report bind errors.
    }
}

function Stop-ApiJob {
    if ($script:ApiJob) {
        Stop-Job $script:ApiJob -ErrorAction SilentlyContinue
        Remove-Job $script:ApiJob -Force -ErrorAction SilentlyContinue
        $script:ApiJob = $null
    }
}

try {
    New-Item -ItemType Directory -Force -Path $WwwDir | Out-Null

    if (-not (Test-Path (Join-Path $WwwDir "index.html"))) {
        if (-not (Test-Path (Join-Path $WwwDir "Build"))) {
            @"
<!doctype html>
<html lang="ko">
  <head><meta charset="utf-8"><title>SmartCity WebGL placeholder</title></head>
  <body>
    <h1>SmartCity WebGL</h1>
    <p>Unity WebGL 빌드 산출물을 <code>www/</code>에 복사하세요.</p>
    <p>API 테스트: <a href="/api/health">/api/health</a></p>
  </body>
</html>
"@ | Set-Content -Path (Join-Path $WwwDir "index.html") -Encoding UTF8
            Write-Host "Created placeholder www/index.html"
        }
    }

    & (Join-Path $PSScriptRoot "patch_unity_webgl_index.ps1") -Index (Join-Path $WwwDir "index.html")
    if (Test-Path (Join-Path $WwwDir "Build")) {
        & (Join-Path $PSScriptRoot "decompress_webgl_build.ps1") -BuildDir (Join-Path $WwwDir "Build")
    }

    $repoNginxPath = Convert-ToNginxPath $RepoRoot
    $serverBlock = (Get-Content $NginxConfSrc -Raw) -replace '__REPO_ROOT__', $repoNginxPath

    $script:NginxExe = Find-NginxExe -Root $NginxRoot
    if (-not $NginxExe) {
        Write-Error @"
nginx.exe not found.
Download: https://nginx.org/en/download.html
Extract to C:\nginx (or pass -NginxRoot).
"@
    }

    $mimeTypes = Join-Path (Split-Path $NginxExe -Parent) "conf\mime.types"
    if (-not (Test-Path $mimeTypes)) {
        $mimeTypes = Join-Path $NginxRoot "conf\mime.types"
    }
    if (-not (Test-Path $mimeTypes)) {
        Write-Error "mime.types not found next to nginx. Expected: $mimeTypes"
    }
    $mimeTypesNginx = Convert-ToNginxPath $mimeTypes

    $fullConf = @"
worker_processes 1;
error_log "$((Convert-ToNginxPath $NginxErrorLog))";
pid "$((Convert-ToNginxPath $NginxPidFile))";

events {
    worker_connections 1024;
}

http {
    include       $mimeTypesNginx;
    default_type  application/octet-stream;
    sendfile      on;

$serverBlock
}
"@

    Set-Content -Path $NginxConfGen -Value $fullConf -Encoding ASCII

    $flaskPort = if ($env:FLASK_PORT) { $env:FLASK_PORT } else { "5001" }
    if (-not $env:FLASK_DEBUG) { $env:FLASK_DEBUG = "0" }

    Write-Host "Starting Flask API..."
    $script:ApiJob = Start-Job -ScriptBlock {
        param($Root, $Port, $Debug)
        Set-Location $Root
        $env:FLASK_PORT = $Port
        $env:FLASK_DEBUG = $Debug
        python -m python.api.flask_app
    } -ArgumentList $RepoRoot, $flaskPort, $env:FLASK_DEBUG

    Start-Sleep -Seconds 3

    try {
        $health = Invoke-WebRequest -Uri "http://127.0.0.1:$flaskPort/health" -UseBasicParsing -TimeoutSec 5
        if ($health.StatusCode -ne 200) { throw "unexpected status $($health.StatusCode)" }
        Write-Host "API direct: http://127.0.0.1:$flaskPort/health OK"
    } catch {
        Write-Error "API health check failed on port $flaskPort. Check: pip install -r requirements.txt, .env"
    }

    Stop-NginxStack -Exe $NginxExe -Conf $NginxConfGen
    Free-Port8080

    Write-Host "Starting nginx on http://localhost:8080 ..."
    & $NginxExe -c $NginxConfGen
    if ($LASTEXITCODE -ne 0) {
        Write-Error "nginx failed to start. Run: `"$NginxExe`" -t -c `"$NginxConfGen`""
    }

    try {
        $proxyHealth = Invoke-WebRequest -Uri "http://localhost:8080/api/health" -UseBasicParsing -TimeoutSec 5
        if ($proxyHealth.StatusCode -ne 200) { throw "unexpected status $($proxyHealth.StatusCode)" }
        Write-Host "API proxy: http://localhost:8080/api/health OK"
    } catch {
        Write-Error "API proxy check failed. nginx config: $NginxConfGen"
    }

    Write-Host ""
    Write-Host "Stack ready:"
    Write-Host "  WebGL static : http://localhost:8080/"
    Write-Host "  API proxy    : http://localhost:8080/api/health"
    Write-Host ""
    Write-Host "Browser check: open DevTools Console and run: crossOriginIsolated"
    Write-Host "  (must be true for Unity WebGL multithreading)"
    Write-Host ""
    Write-Host "Press Ctrl+C to stop."

    while ($true) {
        if ($script:ApiJob.State -eq "Failed") {
            Receive-Job $script:ApiJob
            Write-Error "Flask API job exited."
        }
        Start-Sleep -Seconds 2
    }
} finally {
    if ($NginxExe) {
        Stop-NginxStack -Exe $NginxExe -Conf $NginxConfGen
    }
    Stop-ApiJob
}
