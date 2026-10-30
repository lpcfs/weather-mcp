<#
.SYNOPSIS
  Creates (idempotently) the Entra ID app registrations that protect the Weather MCP server.
.DESCRIPTION
  weather-mcp-api    the protected resource. Exposes the delegated scope `Weather.Read` (signed-in users)
                     and the application role `Weather.ReadAll` (daemons / apps, client credentials).
  weather-mcp-client the caller (Weather.McpClient). Public client for interactive sign-in; optionally a
                     client secret for app-only access. Pre-authorized for the scope (no consent prompt);
                     the Azure CLI is pre-authorized too so `az account get-access-token` works.
  Needs `az login` as someone allowed to create app registrations and grant app roles.
  Stores ENTRA_TENANT_ID / ENTRA_API_CLIENT_ID / ENTRA_CLIENT_ID in the azd environment when azd is available.
.PARAMETER CreateClientSecret
  Also create a client secret for the client app (printed once; store it as ENTRA_CLIENT_SECRET).
#>
param(
    [string]$ApiName = 'weather-mcp-api',
    [string]$ClientName = 'weather-mcp-client',
    [switch]$CreateClientSecret,
    [string[]]$AssignUsers = @()   # extra users (UPN or object id) allowed to use the MCP server; you are always added
)

$ErrorActionPreference = 'Stop'
$ScopeId = '5b1d7a70-2c36-4c5e-9f6e-0a7d1a2f4b01'   # fixed so reruns are idempotent
$RoleId = '9c3e2f48-6d1b-4b7a-8e55-3f0c8a6d7b02'
$UserRoleId = '2d8f6a31-7b94-4e0c-a1d5-6c9e3b7f4a03'
$AzureCliAppId = '04b07795-8ddb-461a-bbee-02f9e1bf7b46'
$Graph = 'https://graph.microsoft.com/v1.0'

function Invoke-Graph([string]$Method, [string]$Path, $Body) {
    $azArgs = @('rest', '--method', $Method, '--url', "$Graph$Path", '--headers', 'Content-Type=application/json')
    if ($Body) {
        $file = New-TemporaryFile
        try {
            $Body | ConvertTo-Json -Depth 10 | Set-Content $file -Encoding utf8
            $azArgs += @('--body', "@$file")
            az @azArgs --only-show-errors
        } finally { Remove-Item $file -ErrorAction SilentlyContinue }
    } else { az @azArgs --only-show-errors }
    if ($LASTEXITCODE -ne 0) { throw "Graph $Method $Path failed" }
}

function Get-OrCreateApp([string]$Name) {
    $existing = az ad app list --display-name $Name --query '[0]' -o json --only-show-errors | ConvertFrom-Json
    if ($existing) { Write-Host "  found $Name ($($existing.appId))"; return $existing }
    Write-Host "  creating $Name"
    az ad app create --display-name $Name --sign-in-audience AzureADMyOrg -o json --only-show-errors | ConvertFrom-Json
}

function Get-OrCreateSp([string]$AppId) {
    $sp = az ad sp list --filter "appId eq '$AppId'" --query '[0]' -o json --only-show-errors | ConvertFrom-Json
    if ($sp) { return $sp }
    az ad sp create --id $AppId -o json --only-show-errors | ConvertFrom-Json
}

$tenantId = az account show --query tenantId -o tsv
Write-Host "Tenant $tenantId"

Write-Host 'API app'
$api = Get-OrCreateApp $ApiName
Invoke-Graph PATCH "/applications/$($api.id)" @{
    identifierUris = @("api://$($api.appId)")
    api            = @{
        requestedAccessTokenVersion = 2
        oauth2PermissionScopes      = @(@{
                id                      = $ScopeId
                value                   = 'Weather.Read'
                type                    = 'User'
                isEnabled               = $true
                adminConsentDisplayName = 'Read weather data'
                adminConsentDescription = 'Allows the app to call the Weather MCP server on behalf of the signed-in user.'
                userConsentDisplayName  = 'Read weather data'
                userConsentDescription  = 'Allows the app to call the Weather MCP server on your behalf.'
            })
    }
    appRoles       = @(@{
            id                 = $RoleId
            value              = 'Weather.ReadAll'
            displayName        = 'Weather.ReadAll'
            description        = 'Allows an application to call the Weather MCP server.'
            allowedMemberTypes = @('Application')
            isEnabled          = $true
        },
        @{
            id                 = $UserRoleId
            value              = 'Weather.User'
            displayName        = 'Weather.User'
            description        = 'Users allowed to use the Weather MCP server (and download its OpenRouter key).'
            allowedMemberTypes = @('User')
            isEnabled          = $true
        })
}
$apiSp = Get-OrCreateSp $api.appId

# Only assigned users/apps can get a token for the API (everyone else is refused by Entra at sign-in).
Write-Host 'Requiring assignment and assigning users'
Invoke-Graph PATCH "/servicePrincipals/$($apiSp.id)" @{ appRoleAssignmentRequired = $true }
$me = az ad signed-in-user show --query id -o tsv --only-show-errors
$userIds = @($me) + @($AssignUsers | ForEach-Object { az ad user show --id $_ --query id -o tsv --only-show-errors })
foreach ($uid in ($userIds | Select-Object -Unique)) {
    $has = az rest --method GET --url "$Graph/servicePrincipals/$($apiSp.id)/appRoleAssignedTo" --query "value[?principalId=='$uid' && appRoleId=='$UserRoleId'] | [0].id" -o tsv --only-show-errors
    if (-not $has) {
        Invoke-Graph POST "/servicePrincipals/$($apiSp.id)/appRoleAssignedTo" @{ principalId = $uid; resourceId = $apiSp.id; appRoleId = $UserRoleId } | Out-Null
        Write-Host "  assigned $uid"
    }
}

Write-Host 'Client app'
$client = Get-OrCreateApp $ClientName
Invoke-Graph PATCH "/applications/$($client.id)" @{
    isFallbackPublicClient = $true
    publicClient           = @{ redirectUris = @('http://localhost') }
}
$clientSp = Get-OrCreateSp $client.appId

Write-Host 'Pre-authorizing client + Azure CLI for the Weather.Read scope'
Invoke-Graph PATCH "/applications/$($api.id)" @{
    api = @{
        preAuthorizedApplications = @(
            @{ appId = $client.appId; delegatedPermissionIds = @($ScopeId) },
            @{ appId = $AzureCliAppId; delegatedPermissionIds = @($ScopeId) })
    }
}

Write-Host 'Granting the client the Weather.ReadAll app role (app-only access)'
$assigned = az rest --method GET --url "$Graph/servicePrincipals/$($clientSp.id)/appRoleAssignments" --query "value[?appRoleId=='$RoleId'] | [0].id" -o tsv --only-show-errors
if (-not $assigned) {
    Invoke-Graph POST "/servicePrincipals/$($clientSp.id)/appRoleAssignments" @{
        principalId = $clientSp.id
        resourceId  = $apiSp.id
        appRoleId   = $RoleId
    } | Out-Null
}

if ($CreateClientSecret) {
    Write-Host 'Creating client secret (shown once, 6 months)'
    $secret = az ad app credential reset --id $client.appId --append --display-name 'weather-mcp' --years 0.5 --query password -o tsv --only-show-errors
    Write-Host "  ENTRA_CLIENT_SECRET=$secret"
}

if (Get-Command azd -ErrorAction SilentlyContinue) {
    azd env set ENTRA_TENANT_ID $tenantId
    azd env set ENTRA_API_CLIENT_ID $api.appId
    azd env set ENTRA_CLIENT_ID $client.appId
}

Write-Host ''
Write-Host "ENTRA_TENANT_ID     = $tenantId"
Write-Host "ENTRA_API_CLIENT_ID = $($api.appId)   (MCP server: AzureAd__ClientId)"
Write-Host "ENTRA_CLIENT_ID     = $($client.appId)   (Weather.McpClient)"
Write-Host 'Next: azd up   (re-deploys the MCP server with AzureAd__* settings)'
