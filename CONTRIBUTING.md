# Contributing

Thank you for contributing to Anno 117 Pipe Hub. Keep changes focused and preserve the read-only pipe and WebSocket contracts documented in `docs/`.

## Before opening a pull request

- Explain the behavior change and its scope.
- Do not include credentials, local settings, logs, binaries, or private paths.
- Update documentation and schemas when a public message or configuration contract changes.
- Run the available tests from the repository root:

```powershell
go test ./...
node --check src\AnnoPipeHub\wwwroot\app.js
```

- For Windows release changes, verify `Publish-Windows.ps1` locally when appropriate. Do not commit its generated output.

The Go implementation uses the standard `testing` package. Keep new tests focused on the public pipe, HTTP, and WebSocket contracts.
