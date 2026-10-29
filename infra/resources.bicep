param environmentName string
param location string
param tags object

var token = toLower(uniqueString(resourceGroup().id, environmentName))

// Placeholder until `azd deploy` pushes the real image (listens on 8080 like our apps).
var placeholderImage = 'mcr.microsoft.com/dotnet/samples:aspnetapp'
var acrPullRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${token}'
  location: location
  tags: tags
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
    userAssignedIdentities: { '${identity.id}': {} }
  }
  dependsOn: [acrPull]
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
          env: [{ name: 'WEATHER_API_URL', value: 'https://${api.properties.configuration.ingress.fqdn}' }]
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 2 }
    }
  }
}

output registryEndpoint string = registry.properties.loginServer
output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output mcpUrl string = 'https://${mcp.properties.configuration.ingress.fqdn}/mcp'
