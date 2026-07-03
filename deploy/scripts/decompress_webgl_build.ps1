# Decompress Unity WebGL .gz build artifacts for local nginx (no Content-Encoding issues).
# Usage: .\deploy\scripts\decompress_webgl_build.ps1 [path\to\www\Build]
param(
    [string]$BuildDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path "www\Build")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BuildDir -PathType Container)) {
    Write-Error "Build directory not found: $BuildDir"
}

$gzFiles = Get-ChildItem -Path $BuildDir -Filter "*.gz" -File
if ($gzFiles.Count -eq 0) {
    Write-Host "No .gz files in $BuildDir (already decompressed?)"
    exit 0
}

foreach ($gz in $gzFiles) {
    $out = $gz.FullName.Substring(0, $gz.FullName.Length - 3)
    if (Test-Path $out) {
        Write-Host "Skip $($gz.Name) — $([IO.Path]::GetFileName($out)) already exists"
        continue
    }
    Write-Host "Decompressing $($gz.Name) -> $([IO.Path]::GetFileName($out))"
    $inStream = [IO.File]::OpenRead($gz.FullName)
    try {
        $gzip = New-Object IO.Compression.GzipStream($inStream, [IO.Compression.CompressionMode]::Decompress)
        try {
            $outStream = [IO.File]::Create($out)
            try {
                $gzip.CopyTo($outStream)
            } finally {
                $outStream.Dispose()
            }
        } finally {
            $gzip.Dispose()
        }
    } finally {
        $inStream.Dispose()
    }
}

Write-Host "Done. www/Build/ now has uncompressed assets for local serving."
