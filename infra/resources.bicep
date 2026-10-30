param environmentName string
param location string
param tags object

@description('Entra tenant that issues tokens for the MCP server.')
param entraTenantId string

@description('Client (application) ID of the Entra app registration that represents the MCP API.')
param entraApiClientId string

@description('Object ID of the person who may manage secrets in the Key Vault (azd sets AZURE_PRINCIPAL_ID). Empty = nobody.')
param adminPrincipalId string = ''

var token = toLower(uniqueString(resourceGroup().id, environmentName))

// Placeholder until `azd deploy` pushes the real image (listens on 8080 like our apps).
var placeholderImage = 'mcr.microsoft.com/dotnet/samples:aspnetapp'
var kvSecretsUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var kvSecretsOfficerRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
var acrPullRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${token}'
  location: location
  tags: tags
}

// Dedicated identity for the MCP server: the only workload that may read the Key Vault.
resource mcpIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-mcp-${token}'
  location: location
  tags: tags
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${token}'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

// The secret itself (OPENROUTER-API-KEY) is added manually, never in source control.
resource mcpReadsVault 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, mcpIdentity.id, kvSecretsUserRole)
  scope: vault
  properties: {
    roleDefinitionId: kvSecretsUserRole
    principalId: mcpIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource adminManagesVault 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(adminPrincipalId)) {
  name: guid(vault.id, adminPrincipalId, kvSecretsOfficerRole)
  scope: vault
  properties: {
    roleDefinitionId: kvSecretsOfficerRole
    principalId: adminPrincipalId
    principalType: 'User'
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'cr${token}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, acrPullRole)
  scope: registry
  properties: {
    roleDefinitionId: acrPullRole
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${token}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${token}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// The `azd-service-name` tag must match the service name in azure.yaml.
resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-api-${token}'
  location: location
  tags: union(tags, { 'azd-service-name': 'api' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  dependsOn: [acrPull]
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      // Internal ingress: reachable only from inside the environment.
      ingress: { external: false, targetPort: 8080, transport: 'auto' }
      registries: [{ server: registry.properties.loginServer, identity: identity.id }]
    }
    template: {
      containers: [{ name: 'api', image: placeholderImage, resources: { cpu: json('0.25'), memory: '0.5Gi' } }]
      scale: { minReplicas: 0, maxReplicas: 2 }
    }
  }
}

resource mcp 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-mcp-${token}'
  location: location
  tags: union(tags, { 'azd-service-name': 'mcp' })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {}, '${mcpIdentity.id}': {} }
  }
  dependsOn: [acrPull, mcpReadsVault]
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: { external: true, targetPort: 8080, transport: 'auto' }
      registries: [{ server: registry.properties.loginServer, identity: identity.id }]
    }
    template: {
      containers: [
        {
          name: 'mcp'
          image: placeholderImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'WEATHER_API_URL', value: 'https://${api.properties.configuration.ingress.fqdn}' }
            { name: 'AzureAd__TenantId', value: entraTenantId }
            { name: 'AzureAd__ClientId', value: entraApiClientId }
            { name: 'KEYVAULT_URI', value: vault.properties.vaultUri }
            // Picks the Key Vault identity (the app has two user-assigned identities).
            { name: 'AZURE_CLIENT_ID', value: mcpIdentity.properties.clientId }
          ]
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 2 }
    }
  }
}

output registryEndpoint string = registry.properties.loginServer
output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output keyVaultName string = vault.name
output keyVaultUri string = vault.properties.vaultUri
output mcpUrl string = 'https://${mcp.properties.configuration.ingress.fqdn}/mcp'
