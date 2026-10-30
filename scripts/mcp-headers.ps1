<#
.SYNOPSIS
  Prints the Authorization header for the Weather MCP server as JSON, for MCP clients' `headersHelper`.
  Needs `az login` as a user assigned to the Weather.User role.
#>
param([string]$ApiClientId = '845ba887-173a-4032-b02f-fea08a82b3df')

$token = az account get-access-token --scope "api://$ApiClientId/Weather.Read" --query accessToken -o tsv --only-show-errors
if (-not $token) { [Console]::Error.WriteLine('No token: run `az login` first.'); exit 1 }
@{ Authorization = "Bearer $token" } | ConvertTo-Json -Compress
