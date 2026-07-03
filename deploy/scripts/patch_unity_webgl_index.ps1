# Strip .gz from Unity WebGL index.html asset URLs for local nginx serving.
# Usage: .\deploy\scripts\patch_unity_webgl_index.ps1 [path\to\www\index.html]
param(
    [string]$Index = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "www\index.html")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Index -PathType Leaf)) {
    Write-Error "index.html not found: $Index"
}

$content = Get-Content -Path $Index -Raw -Encoding UTF8
$patched = $content `
    -replace '(dataUrl: buildUrl \+ "/[^"]*)\.data\.gz"', '$1.data"' `
    -replace '(frameworkUrl: buildUrl \+ "/[^"]*)\.framework\.js\.gz"', '$1.framework.js"' `
    -replace '(workerUrl: buildUrl \+ "/[^"]*)\.worker\.js\.gz"', '$1.worker.js"' `
    -replace '(codeUrl: buildUrl \+ "/[^"]*)\.wasm\.gz"', '$1.wasm"'

if ($patched -eq $content) {
    Write-Host "No .gz URLs to patch in $Index"
} else {
    Set-Content -Path $Index -Value $patched -Encoding UTF8 -NoNewline
    Write-Host "Patched $Index (removed .gz from Unity asset URLs)"
}
