targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the azd environment; used to name every resource.')
param environmentName string

@minLength(1)
@description('Azure region for all resources.')
param location string

var tags = { 'azd-env-name': environmentName }

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

// azd reads these outputs to push images and to show the endpoints.
output AZURE_LOCATION string = location
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.registryEndpoint
output API_URL string = resources.outputs.apiUrl
output MCP_URL string = resources.outputs.mcpUrl
