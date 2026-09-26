$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$env:TEMP = Join-Path $root '.local\tmp'
$env:TMP = $env:TEMP
$output = Join-Path $root 'publish\win-x64'
$staging = Join-Path $root 'publish\.staging-win-x64'
$backup = Join-Path $root 'publish\.previous-win-x64'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null
$env:CGO_ENABLED = '0'
$env:GOOS = 'windows'
$env:GOARCH = 'amd64'
go build -trimpath -ldflags '-s -w' -o (Join-Path $staging 'Anno117PipeHub.exe') (Join-Path $root 'main.go')
$publishExitCode = $LASTEXITCODE
if ($publishExitCode -ne 0) {
	Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
	throw "go build ist mit Exitcode $publishExitCode fehlgeschlagen. Die bisherige Ausgabe wurde erhalten."
}
$stagingExe = Join-Path $staging 'Anno117PipeHub.exe'
if (-not (Test-Path $stagingExe -PathType Leaf) -or (Get-Item $stagingExe).Length -le 0) {
	Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
	throw "Die erwartete Staging-EXE fehlt oder ist leer: $stagingExe. Die bisherige Ausgabe wurde erhalten."
}
Get-ChildItem $staging -File | Where-Object { $_.Extension -eq '.pdb' -or $_.Name -eq 'web.config' } | Remove-Item -Force
$publishedExe = Join-Path $output 'Anno117PipeHub.exe'
if (Test-Path $publishedExe) {
	try {
		$lockProbe = [System.IO.File]::Open($publishedExe, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
		$lockProbe.Dispose()
	} catch {
		Remove-Item $staging -Recurse -Force
		throw "Die bestehende EXE ist geöffnet oder gesperrt. Die vorhandene Veröffentlichung wurde erhalten: $publishedExe"
	}
}
if (Test-Path $backup) { Remove-Item $backup -Recurse -Force }
if (Test-Path $output) { Move-Item $output $backup }
try {
	Move-Item $staging $output
	if (Test-Path $backup) { Remove-Item $backup -Recurse -Force }
} catch {
	if (Test-Path $output) { Remove-Item $output -Recurse -Force }
	if (Test-Path $backup) { Move-Item $backup $output }
	if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
	throw
}
Write-Host "Publish-Ausgabe: $output"
Get-ChildItem $output -File | Select-Object Name,Length