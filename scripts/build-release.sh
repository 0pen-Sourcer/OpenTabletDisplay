#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
VERSION="$(awk -F'"' '/Version = / {print $2; exit}' internal/bridge/constants.go)"
PACKAGE="OpenTabletDisplay-$VERSION-windows-x64"
OUT="$ROOT/dist/$PACKAGE"
rm -rf "$ROOT/dist"
mkdir -p "$OUT/payload/probes" "$OUT/installer"

echo '[1/6] format + tests'
test -z "$(gofmt -l cmd internal)" || { echo 'gofmt required:'; gofmt -l cmd internal; exit 1; }
go test ./...
go vet ./...
go test -race ./...
go run ./cmd/fsdl --selftest

echo '[2/6] Windows host binary'
# Keep the 3.x runtime binary name for installed-system compatibility.
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -trimpath -ldflags='-s -w' -o "$OUT/payload/FSDL.exe" ./cmd/fsdl

echo '[3/6] Android live-test probes'
GOOS=android GOARCH=arm64 CGO_ENABLED=0 go build -trimpath -ldflags='-s -w' -o "$OUT/payload/probes/fsdl-probe-android-arm64" ./cmd/fsdl-probe

echo '[4/6] installer + docs'
cp installer/*.ps1 "$OUT/installer/"
cp install.cmd status.cmd reset-device.cmd uninstall.cmd "$OUT/"
cp README.md LICENSE THIRD_PARTY_NOTICES.md "$OUT/"
mkdir -p "$OUT/docs"
cp docs/COMPATIBILITY.md docs/MOONLIGHT.md docs/TROUBLESHOOTING.md "$OUT/docs/"

echo '[5/6] checksums'
(
  cd "$OUT"
  find . -type f ! -name SHA256SUMS.txt -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS.txt
)

echo '[6/6] zip'
( cd "$ROOT/dist" && zip -qr "$PACKAGE.zip" "$PACKAGE" )
sha256sum "$ROOT/dist/$PACKAGE.zip"
echo "Built: $ROOT/dist/$PACKAGE.zip"
