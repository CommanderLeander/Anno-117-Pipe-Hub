$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$published = Join-Path $root 'publish\win-x64\Anno117PipeHub.exe'
if (Test-Path $published) {
    $process = Start-Process -FilePath $published -WorkingDirectory $root -PassThru
} else {
    $env:DOTNET_CLI_HOME = Join-Path $root '.local\dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $root '.local\nuget\packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $root '.local\nuget\http-cache'
    $env:TEMP = Join-Path $root '.local\tmp'
    $env:TMP = $env:TEMP
    $process = Start-Process dotnet -ArgumentList 'run','--project','src\AnnoPipeHub\AnnoPipeHub.csproj','--no-launch-profile' -WorkingDirectory $root -PassThru
}
Start-Process 'http://127.0.0.1:8765/'
Write-Host "Anno 117 Pipe Hub gestartet (PID $($process.Id)). Dashboard: http://127.0.0.1:8765/"