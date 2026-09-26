$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $root '.local\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.local\nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $root '.local\nuget\http-cache'
$env:TEMP = Join-Path $root '.local\tmp'
$env:TMP = $env:TEMP
$output = Join-Path $root 'publish\win-x64'
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $output | Out-Null
dotnet publish (Join-Path $root 'src\AnnoPipeHub\AnnoPipeHub.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $output
Get-ChildItem $output -File | Where-Object { $_.Extension -eq '.pdb' -or $_.Name -eq 'web.config' } | Remove-Item -Force
Write-Host "Publish-Ausgabe: $output"
Get-ChildItem $output -File | Select-Object Name,Length