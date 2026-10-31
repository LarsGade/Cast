#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1
if ! command -v dotnet >/dev/null 2>&1 && [ ! -x /workspace/.dotnet/dotnet ]; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/cast-dotnet-install.sh
  bash /tmp/cast-dotnet-install.sh --version 8.0.419 --install-dir /workspace/.dotnet --no-path
fi
export PATH="/workspace/.dotnet:$PATH"
mkdir -p tools
if [ ! -f tools/ffmpeg.exe ]; then
  base=https://github.com/BtbN/FFmpeg-Builds/releases/download/latest
  [ -f tools/ffmpeg.zip ] || curl -fL "$base/ffmpeg-master-latest-win64-gpl.zip" -o tools/ffmpeg.zip
  [ -f tools/checksums.sha256 ] || curl -fL "$base/checksums.sha256" -o tools/checksums.sha256
  python3 - <<'PY'
import hashlib, pathlib, zipfile
root = pathlib.Path('tools')
archive = root / 'ffmpeg.zip'
expected = next(line.split()[0] for line in (root/'checksums.sha256').read_text().splitlines() if line.split()[-1] == 'ffmpeg-master-latest-win64-gpl.zip')
actual = hashlib.file_digest(archive.open('rb'), 'sha256').hexdigest()
if actual != expected:
    raise SystemExit('FFmpeg archive checksum mismatch')
with zipfile.ZipFile(archive) as z:
    binary = next(name for name in z.namelist() if name.endswith('/bin/ffmpeg.exe'))
    (root/'ffmpeg.exe').write_bytes(z.read(binary))
    for name in z.namelist():
        if pathlib.PurePosixPath(name).name.startswith('LICENSE') and not name.endswith('/'):
            (root/pathlib.PurePosixPath(name).name).write_bytes(z.read(name))
PY
fi
encoder_options="$(mktemp)"
trap 'rm -f "$encoder_options"' EXIT
dotnet run --project tests/Cast.ProtocolTests -c Release -- --write-encoder-options "$encoder_options"
if command -v ffmpeg >/dev/null && command -v ffprobe >/dev/null; then
  python3 scripts/test-encoder.py "$encoder_options"
fi
dotnet publish src/Cast/Cast.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/win-x64
cp THIRD-PARTY-NOTICES.md tools/checksums.sha256 artifacts/win-x64/
find tools -maxdepth 1 -name 'LICENSE*' -exec cp {} artifacts/win-x64/ \;

cp licenses/NAudio.txt artifacts/win-x64/NAudio-LICENSE.txt
