@description('KSeF Gateway - Azure Container Apps deployment')

param location string = resourceGroup().location
param envName string = 'ksef-gateway'

@secure()
param ksefToken string = ''
param ksefNip string
param ksefEnv string = 'TEST'

// Certificate-based auth (alternative to ksefToken) - PEM content, not a file path,
// since Container Apps has no convenient way to mount an arbitrary file. Leave all
// three empty if using ksefToken instead.
@secure()
param ksefCertContent string = ''
@secure()
param ksefKeyContent string = ''
@secure()
param ksefKeyPassword string = ''

param apiImage string = 'ghcr.io/jurczykpawel/ksef-gateway-api:latest'
param pdfImage string = 'ghcr.io/jurczykpawel/ksef-gateway-pdf:latest'

// Log Analytics workspace (required by Container Apps)
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${envName}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// Container Apps Environment
resource containerEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: envName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// PDF Service (internal only)
resource ksefPdf 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'ksef-pdf'
  location: location
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      ingress: {
        external: false
        targetPort: 3000
      }
    }
    template: {
      containers: [
        {
          name: 'ksef-pdf'
          image: pdfImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
      }
    }
  }
}

// KSeF API (public)
resource ksefApi 'Microsoft.App/containerApps@2023-05-01' = {
  name: 'ksef-api'
  location: location
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
      secrets: [
        { name: 'ksef-token', value: ksefToken }
        { name: 'ksef-cert-content', value: ksefCertContent }
        { name: 'ksef-key-content', value: ksefKeyContent }
        { name: 'ksef-key-password', value: ksefKeyPassword }
      ]
    }
    template: {
      containers: [
        {
          name: 'ksef-api'
          image: apiImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'KSEF_TOKEN', secretRef: 'ksef-token' }
            { name: 'KSEF_CERT_CONTENT', secretRef: 'ksef-cert-content' }
            { name: 'KSEF_KEY_CONTENT', secretRef: 'ksef-key-content' }
            { name: 'KSEF_KEY_PASSWORD', secretRef: 'ksef-key-password' }
            { name: 'KSEF_NIP', value: ksefNip }
            { name: 'KSEF_ENV', value: ksefEnv }
            { name: 'PDF_SERVICE_URL', value: 'http://ksef-pdf' }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
    }
  }
}

output apiUrl string = 'https://${ksefApi.properties.configuration.ingress.fqdn}'
