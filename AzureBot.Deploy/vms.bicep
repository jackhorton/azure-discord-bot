var imageBuilderSubnetName = 'azurebot-imagebuilder-subnet'

resource gallery 'Microsoft.Compute/galleries@2019-12-01' = {
  name: 'azurebotvmsig'
  location: resourceGroup().location
  properties: {
    description: 'Gallery for game server VM images'
  }
}

resource valheimImage 'Microsoft.Compute/galleries/images@2019-12-01' = {
  parent: gallery
  name: 'valheim-ws2019'
  location: resourceGroup().location
  properties: {
    description: 'A Windows Server 2019 image containing Valheim'
    osType: 'Windows'
    osState: 'Generalized'
    identifier: {
      publisher: 'AzureBot'
      offer: 'Valheim'
      sku: 'Valheim-WS2019'
    }
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2020-06-01' = {
  name: 'azurebot-vms-vnet'
  location: resourceGroup().location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.1.0.0/16'
      ]
    }
    subnets: [
      {
        name: imageBuilderSubnetName
        properties: {
          addressPrefix: '10.1.0.0/24'
          privateEndpointNetworkPolicies: 'Enabled'
          privateLinkServiceNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

resource imageBuilderIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2018-11-30' = {
  name: 'imagebuilder-identity'
  location: resourceGroup().location
}

resource contributorDefinition 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  name: 'b24988ac-6180-42a0-ab88-20f7382dd24c'
  scope: subscription()
}

resource imageBuilderGalleryContributor 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' = {
  scope: gallery
  name: guid(imageBuilderIdentity.id, contributorDefinition.id, gallery.id)
  properties: {
    roleDefinitionId: contributorDefinition.id
    principalId: imageBuilderIdentity.properties.principalId
  }
}

resource readerDefinition 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  name: 'acdd72a7-3385-48ef-bd42-f606fba81ae7'
  scope: subscription()
}

resource imageBuilderIdentityVnetReaderName 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' = {
  scope: vnet
  name: guid(imageBuilderIdentity.id, readerDefinition.id, vnet.id)
  properties: {
    roleDefinitionId: readerDefinition.id
    principalId: imageBuilderIdentity.properties.principalId
  }
}

resource valheimImageTemplateName 'Microsoft.VirtualMachineImages/imageTemplates@2020-02-14' = {
  name: '${valheimImage.name}-template'
  location: resourceGroup().location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${imageBuilderIdentity.id}': {}
    }
  }
  properties: {
    buildTimeoutInMinutes: 60
    vmProfile: {
      vmSize: 'Standard_F2s_v2'
      osDiskSizeGB: 0
      vnetConfig: {
        subnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, imageBuilderSubnetName)
      }
    }
    source: {
      type: 'PlatformImage'
      publisher: 'MicrosoftWindowsServer'
      offer: 'WindowsServer'
      sku: '2019-Datacenter-Core'
      version: 'latest'
    }
    customize: [
      {
        type: 'File'
        sourceUri: 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip'
        destination: 'C:\\steamcmd.zip'
      }
      {
        type: 'PowerShell'
        name: 'Install SteamCMD'
        inline: [
          'New-Item -ItemType Directory C:\\steamcmd'
          'Expand-Archive -Path C:\\steamcmd.zip -DestinationPath C:\\steamcmd'
        ]
      }
      {
        type: 'WindowsUpdate'
      }
    ]
    distribute: [
      {
        type: 'SharedImage'
        galleryImageId: valheimImage.id
        runOutputName: 'runOutputName'
        replicationRegions: [
          gallery.location
        ]
      }
    ]
  }
}
