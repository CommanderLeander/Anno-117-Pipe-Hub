param(
    [switch]$UsePublished
)
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$published = Join-Path $root 'publish\win-x64\Anno117PipeHub.exe'
if ($UsePublished) {
    if (-not (Test-Path $published -PathType Leaf)) {
        throw "Die veröffentlichte EXE wurde nicht gefunden: $published"
    }
    $process = Start-Process -FilePath $published -WorkingDirectory $root -PassThru
} else {
    $env:TEMP = Join-Path $root '.local\tmp'
    $env:TMP = $env:TEMP
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
    throw "Das Dashboard antwortet nach 30 Sekunden nicht unter $dashboard. Der Hub-Prozess läuft möglicherweise noch (PID $($process.Id))."
}
Start-Process $dashboard
Write-Host "Anno 117 Pipe Hub gestartet (PID $($process.Id)). Dashboard: http://127.0.0.1:8765/"