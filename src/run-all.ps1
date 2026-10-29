<#
.SYNOPSIS
  Starts the whole Weather stack in the right order: API -> MCP server -> chatbot.
.PARAMETER NoClient
  Only start the API and MCP server; keep them running until Ctrl+C.
#>
param(
    [switch]$NoClient,
    [int]$ApiPort = 5080,
    [int]$McpPort = 5000
)

$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$procs = @()

function Wait-Until([string]$What, [scriptblock]$Ready, [int]$TimeoutSec = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try { if (& $Ready) { Write-Host "  $What is up"; return } } catch { }
        Start-Sleep -Milliseconds 500
    }
    throw "$What did not start within $TimeoutSec s"
}

function Start-Project([string]$Project, [string[]]$ExtraArgs, [hashtable]$Env = @{}) {
    foreach ($k in $Env.Keys) { Set-Item "Env:$k" $Env[$k] }
    Start-Process dotnet -PassThru -NoNewWindow `
        -ArgumentList (@('run', '--no-launch-profile', '--project', (Join-Path $src $Project)) + $ExtraArgs)
}

if (-not $NoClient -and -not $env:OPENROUTER_API_KEY) {
    throw 'Set OPENROUTER_API_KEY first (or use -NoClient).'
}

try {
    Write-Host "1/3 Weather.Api on :$ApiPort"
    $procs += Start-Project 'Weather.Api' @('--urls', "http://localhost:$ApiPort")
    Wait-Until 'Weather.Api' {
        (Invoke-WebRequest "http://localhost:$ApiPort/api/locations?query=Brussels" -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200
    }

    Write-Host "2/3 Weather.McpServer on :$McpPort"
    $procs += Start-Project 'Weather.McpServer' @('--urls', "http://localhost:$McpPort") @{ WEATHER_API_URL = "http://localhost:$ApiPort" }
    Wait-Until 'Weather.McpServer' {
        (New-Object Net.Sockets.TcpClient).ConnectAsync('localhost', $McpPort).Wait(1000)
    }

    if ($NoClient) {
        Write-Host "API: http://localhost:$ApiPort  MCP: http://localhost:$McpPort/mcp  (Ctrl+C to stop)"
        Wait-Process -Id $procs[-1].Id
    }
    else {
        Write-Host '3/3 Weather.McpClient'
        $env:WEATHER_MCP_URL = "http://localhost:$McpPort/mcp"
        dotnet run --no-launch-profile --project (Join-Path $src 'Weather.McpClient')
    }
}
finally {
    foreach ($p in $procs) {
        if ($p -and -not $p.HasExited) { & taskkill /PID $p.Id /T /F | Out-Null }
    }
    Write-Host 'Stopped.'
}
