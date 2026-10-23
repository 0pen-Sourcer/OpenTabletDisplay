# Contributing

OpenTabletDisplay (formerly FSDL) is an integration project. Prefer fixing the orchestration boundary instead of reimplementing Sunshine/Moonlight features.

The current 3.x codebase intentionally retains `fsdl` package/module/runtime names for compatibility. Do not perform a mechanical internal rename unless the change includes an explicit upgrade, rollback, installer, and state-migration plan.

Before a PR:

1. `gofmt -w ./cmd ./internal`
2. `go test ./...`
3. `go vet ./...`
4. `go test -race ./...` on a supported development platform
5. Ensure installer/uninstaller changes are transactional and ownership-scoped.
6. Do not add wildcard driver/package/process deletion.
7. Do not commit third-party binaries or credentials.
8. Update `THIRD_PARTY_NOTICES.md` when dependency pins change.

Hardware reports should include Windows version, GPU, Android model/Android version, Moonlight version, and the sanitized output of `status.cmd`.
