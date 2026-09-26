param(
    [switch]$UsePublished
)
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$tempDirectory = Join-Path $root '.local\tmp'
$originalTemp = $env:TEMP
$originalTmp = $env:TMP
$published = Join-Path $root 'publish\win-x64\Anno117PipeHub.exe'
try {
    New-Item -ItemType Directory -Force -Path $tempDirectory | Out-Null
    $env:TEMP = $tempDirectory
    $env:TMP = $tempDirectory
    if ($UsePublished) {
        if (-not (Test-Path $published -PathType Leaf)) {
            throw "Die verÃ¶ffentlichte EXE wurde nicht gefunden: $published"
        }
        $process = Start-Process -FilePath $published -WorkingDirectory $root -PassThru
    } else {
        $process = Start-Process go -ArgumentList 'run','.' -WorkingDirectory $root -PassThru
    }
    $dashboard = 'http://127.0.0.1:8765/'
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) {
            throw "Der Hub-Prozess wurde vor dem Start des Dashboards beendet (Exitcode $($process.ExitCode))."
        }
        try {
            $response = Invoke-WebRequest -Uri $dashboard -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) {
                $ready = $true
                break
            }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) {
        if ($process.HasExited) {
            throw "Der Hub-Prozess wurde vor dem Start des Dashboards beendet (Exitcode $($process.ExitCode))."
        }
        throw "Das Dashboard antwortet nach 30 Sekunden nicht unter $dashboard. Der Hub-Prozess lÃ¤uft mÃ¶glicherweise noch (PID $($process.Id))."
    }
    Start-Process $dashboard
    Write-Host "Anno 117 Pipe Hub gestartet (PID $($process.Id)). Dashboard: http://127.0.0.1:8765/"
} finally {
    if ($null -eq $originalTemp) { Remove-Item Env:TEMP -ErrorAction SilentlyContinue } else { $env:TEMP = $originalTemp }
    if ($null -eq $originalTmp) { Remove-Item Env:TMP -ErrorAction SilentlyContinue } else { $env:TMP = $originalTmp }
}