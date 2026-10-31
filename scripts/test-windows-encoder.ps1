$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
New-Item -ItemType Directory -Force tools | Out-Null
$base = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest'
$archive = 'ffmpeg-master-latest-win64-gpl.zip'
Invoke-WebRequest "$base/$archive" -OutFile tools/windows-test.zip
Invoke-WebRequest "$base/checksums.sha256" -OutFile tools/windows-test.sha256
$entry = Get-Content tools/windows-test.sha256 | Where-Object { $_ -match "\s+\*?$([regex]::Escape($archive))$" } | Select-Object -First 1
if (-not $entry) { throw 'FFmpeg checksum entry missing.' }
$expected = ($entry -split '\s+')[0]
if ((Get-FileHash tools/windows-test.zip -Algorithm SHA256).Hash -ne $expected) { throw 'FFmpeg checksum mismatch.' }
Expand-Archive tools/windows-test.zip tools/windows-test -Force
$binary = Get-ChildItem tools/windows-test -Recurse -Filter ffmpeg.exe | Select-Object -First 1
$env:PATH = "$($binary.Directory.FullName);$env:PATH"
ffmpeg -version
# Test production PCM input and HLS output with the actual Windows executable.
dotnet run --project tests/Cast.ProtocolTests -c Release -- --encoder-config-only tools/audio-test.json
if ($LASTEXITCODE -ne 0) { throw 'Encoder configuration generation failed.' }
python scripts/test-encoder.py tools/audio-test.json
if ($LASTEXITCODE -ne 0) { throw 'Windows audio/video encoder test failed.' }
# Exercise gdigrab independently of sound or a physical Chromecast.
dotnet run --project tests/Cast.ProtocolTests -c Release -- --screen-config-only tools/screen-test.json
if ($LASTEXITCODE -ne 0) { throw 'Screen configuration generation failed.' }
$inputOptions = Get-Content tools/screen-test.json -Raw | ConvertFrom-Json
& ffmpeg -hide_banner -loglevel warning @inputOptions -t 1 -f null NUL
if ($LASTEXITCODE -ne 0) { throw 'Windows desktop capture failed.' }
Write-Host 'Windows PCM audio encoding and actual gdigrab screen capture passed.'
