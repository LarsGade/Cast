$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Installer .NET 8 SDK først.' }
New-Item -ItemType Directory -Force tools | Out-Null
if (-not (Test-Path tools/ffmpeg.exe)) {
    # Publisher build, checked against the publisher's SHA-256 manifest.
    $base = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest'
    $archive = 'ffmpeg-master-latest-win64-gpl.zip'
    Invoke-WebRequest "$base/$archive" -OutFile tools/ffmpeg.zip
    Invoke-WebRequest "$base/checksums.sha256" -OutFile tools/checksums.sha256
    $entry = Get-Content tools/checksums.sha256 | Where-Object { $_ -match "\s+\*?$([regex]::Escape($archive))$" } | Select-Object -First 1
    if (-not $entry) { throw 'Arkivet mangler i checksum-manifestet.' }
    $expected = ($entry -split '\s+')[0]
    if ((Get-FileHash tools/ffmpeg.zip -Algorithm SHA256).Hash -ne $expected) { throw 'FFmpeg checksum matcher ikke.' }
    Expand-Archive tools/ffmpeg.zip tools/unpacked -Force
    $binary = Get-ChildItem tools/unpacked -Recurse -Filter ffmpeg.exe | Select-Object -First 1
    if (-not $binary) { throw 'FFmpeg mangler i arkivet.' }
    Copy-Item $binary.FullName tools/ffmpeg.exe
    Copy-Item tools/unpacked/*/LICENSE* tools/ -ErrorAction SilentlyContinue
    Remove-Item tools/ffmpeg.zip,tools/unpacked -Recurse -Force
}
dotnet run --project tests/Cast.ProtocolTests -c Release
if ($LASTEXITCODE -ne 0) { throw 'Protokoltests fejlede.' }
dotnet publish src/Cast/Cast.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/win-x64
if ($LASTEXITCODE -ne 0) { throw 'Build fejlede.' }
Copy-Item tools/LICENSE* artifacts/win-x64/ -ErrorAction SilentlyContinue
Copy-Item THIRD-PARTY-NOTICES.md artifacts/win-x64/
Copy-Item tools/checksums.sha256 artifacts/win-x64/
Copy-Item licenses/NAudio.txt artifacts/win-x64/NAudio-LICENSE.txt
Write-Host 'Færdig: artifacts/win-x64/Cast.exe'
