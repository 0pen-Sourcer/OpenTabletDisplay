#!/usr/bin/env bash
# Cross-build checks only. The real WPF desktop package MUST be built on Windows.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
test -z "$(gofmt -l cmd internal)"
go test ./...
go vet ./...
go test -race ./...
go run ./cmd/fsdl --selftest
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -o "$TMP/FSDL.exe" ./cmd/fsdl
GOOS=android GOARCH=arm64 CGO_ENABLED=0 go build -o "$TMP/fsdl-probe-android-arm64" ./cmd/fsdl-probe
echo 'Go cross-build checks succeeded. No public archive created on Linux.'
