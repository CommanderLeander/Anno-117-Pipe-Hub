# Contributing

Thank you for contributing to Anno 117 Pipe Hub. Keep changes focused and preserve the read-only pipe and WebSocket contracts documented in `docs/`.

## Before opening a pull request

- Explain the behavior change and its scope.
- Do not include tokens, local settings, logs, binaries, or private paths.
- Update documentation and schemas when a public message or configuration contract changes.
- Run the available tests from the repository root:

```powershell
dotnet run --project tests\AnnoPipeHub.Tests\AnnoPipeHub.Tests.csproj
node --check src\AnnoPipeHub\wwwroot\app.js
```

- For Windows release changes, verify `Publish-Windows.ps1` locally when appropriate. Do not commit its generated output.

The project currently uses a small custom test runner rather than a test framework. Keep new tests consistent with the existing test project unless there is a clear reason to change it.
