# Starts a built RetroServer image, waits for its /health answer, then removes the container and its
# data volume again. Fails if the answer does not come within the time limit.
# Usage (from src):  ./eng/test-docker.ps1 [-Tag retroserver:local] [-Port 15000] [-Seconds 60]
param([string]$Tag = 'retroserver:local', [int]$Port = 15000, [int]$Seconds = 60)
$ErrorActionPreference = 'Stop'

$name = "retroserver-test-$PID"
# --rm removes the container when it stops; its anonymous /data volume is removed in finally.
$id = docker run -d --rm --name $name -p "127.0.0.1:${Port}:5000" $Tag
if ($LASTEXITCODE -ne 0) { throw "docker run failed ($LASTEXITCODE)" }

try {
    $deadline = (Get-Date).AddSeconds($Seconds)
    $answer = $null
    while ((Get-Date) -lt $deadline -and $answer -ne 'OK') {
        try { $answer = (Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:$Port/health" -TimeoutSec 2).Content }
        catch { Start-Sleep -Milliseconds 500 }
    }

    if ($answer -ne 'OK') {
        docker logs $id
        throw "no /health answer from $Tag within $Seconds s"
    }

    Write-Host "$Tag answers /health with OK"
}
finally {
    $volumes = docker inspect -f '{{range .Mounts}}{{.Name}} {{end}}' $id
    docker stop -t 30 $id | Out-Null
    foreach ($v in ($volumes -split ' ')) { if ($v) { docker volume rm $v 2>$null | Out-Null } }
}
