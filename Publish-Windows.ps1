$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$originalLocation = Get-Location
$originalTemp = $env:TEMP
$originalTmp = $env:TMP
$originalCgoEnabled = $env:CGO_ENABLED
$originalGoos = $env:GOOS
$originalGoarch = $env:GOARCH
$locationChanged = $false
$output = Join-Path $root 'publish\win-x64'
$staging = Join-Path $root 'publish\.staging-win-x64'
$backup = Join-Path $root 'publish\.previous-win-x64'
$zipOutput = Join-Path $root 'publish\Anno117PipeHub-windows-x64.zip'
$zipStaging = Join-Path $root 'publish\.staging-Anno117PipeHub-windows-x64.zip'
$zipBackup = Join-Path $root 'publish\.previous-Anno117PipeHub-windows-x64.zip'
try {
    Push-Location -LiteralPath $root
    $locationChanged = $true
    $tempDirectory = Join-Path $root '.local\tmp'
    New-Item -ItemType Directory -Force -Path $tempDirectory | Out-Null
    $env:TEMP = $tempDirectory
    $env:TMP = $tempDirectory
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    $env:CGO_ENABLED = '0'
    $env:GOOS = 'windows'
    $env:GOARCH = 'amd64'
    go build -trimpath -ldflags '-s -w' -o (Join-Path $staging 'Anno117PipeHub.exe') .
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
        Copy-Item (Join-Path $root 'LICENSE') (Join-Path $staging 'LICENSE')
        Copy-Item (Join-Path $root 'THIRD_PARTY_NOTICES.md') (Join-Path $staging 'THIRD_PARTY_NOTICES.md')
        if (Test-Path $zipStaging) { Remove-Item $zipStaging -Force }
        try {
            Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipStaging -CompressionLevel Optimal
        } catch {
            Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item $zipStaging -Force -ErrorAction SilentlyContinue
            throw "Die Release-ZIP konnte nicht erstellt werden. Die bisherige Ausgabe wurde erhalten: $zipOutput"
        }
        if (-not (Test-Path $zipStaging -PathType Leaf) -or (Get-Item $zipStaging).Length -le 0) {
            Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item $zipStaging -Force -ErrorAction SilentlyContinue
            throw "Die erwartete Release-ZIP fehlt oder ist leer: $zipStaging. Die bisherige Ausgabe wurde erhalten."
        }
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
        if (Test-Path $zipBackup) { Remove-Item $zipBackup -Force }
        $outputBackedUp = $false
        $zipBackedUp = $false
        $outputInstalled = $false
        $zipInstalled = $false
    try {
            if (Test-Path $output) { Move-Item $output $backup; $outputBackedUp = $true }
	    Move-Item $staging $output
            $outputInstalled = $true
            if (Test-Path $zipOutput) { Move-Item $zipOutput $zipBackup; $zipBackedUp = $true }
            Move-Item $zipStaging $zipOutput
            $zipInstalled = $true
            if (Test-Path $backup) { Remove-Item $backup -Recurse -Force }
            if (Test-Path $zipBackup) { Remove-Item $zipBackup -Force }
    } catch {
            if ($zipInstalled -and (Test-Path $zipOutput)) { Remove-Item $zipOutput -Force }
            if ($zipBackedUp -and (Test-Path $zipBackup)) { Move-Item $zipBackup $zipOutput }
            if ($outputInstalled -and (Test-Path $output)) { Remove-Item $output -Recurse -Force }
            if ($outputBackedUp -and (Test-Path $backup)) { Move-Item $backup $output }
	    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
            if (Test-Path $zipStaging) { Remove-Item $zipStaging -Force }
	    throw
    }
    Write-Host "Publish-Ausgabe: $output"
    Get-ChildItem $output -File | Select-Object Name,Length
        Write-Host "Release-ZIP: $zipOutput"
        Get-Item $zipOutput | Select-Object FullName,Length
} finally {
    if ($locationChanged) { Pop-Location }
    if ($null -eq $originalTemp) { Remove-Item Env:TEMP -ErrorAction SilentlyContinue } else { $env:TEMP = $originalTemp }
    if ($null -eq $originalTmp) { Remove-Item Env:TMP -ErrorAction SilentlyContinue } else { $env:TMP = $originalTmp }
    if ($null -eq $originalCgoEnabled) { Remove-Item Env:CGO_ENABLED -ErrorAction SilentlyContinue } else { $env:CGO_ENABLED = $originalCgoEnabled }
    if ($null -eq $originalGoos) { Remove-Item Env:GOOS -ErrorAction SilentlyContinue } else { $env:GOOS = $originalGoos }
    if ($null -eq $originalGoarch) { Remove-Item Env:GOARCH -ErrorAction SilentlyContinue } else { $env:GOARCH = $originalGoarch }
}