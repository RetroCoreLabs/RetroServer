# Builds the RetroServer container image: publishes RetroServer for linux-x64 into docker/publish,
# then builds docker/Dockerfile. The publish folder is deleted again afterwards.
# Usage (from src):  ./eng/build-docker.ps1 [-Tag retroserver:local]
param([string]$Tag = 'retroserver:local')
$ErrorActionPreference = 'Stop'

# $root is src: this script lives in src/eng.
$root = Split-Path -Parent $PSScriptRoot
$docker = Join-Path $root 'docker'
$publish = Join-Path $docker 'publish'

if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

try {
    dotnet publish (Join-Path $root 'RetroServer.Service') -c Release -r linux-x64 --self-contained false -nodeReuse:false -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    docker build -t $Tag $docker
    if ($LASTEXITCODE -ne 0) { throw "docker build failed ($LASTEXITCODE)" }
}
finally {
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
}

Write-Host "built $Tag"
