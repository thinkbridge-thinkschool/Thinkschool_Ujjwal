@description('Name of the Service Bus namespace - must be globally unique across Azure.')
param namespaceName string

@description('Azure region for the namespace.')
param location string

@description('Service Bus SKU tier. Basic does not support topics/subscriptions, so Standard is the cheapest tier that fits this design.')
@allowed(['Standard', 'Premium'])
param skuName string

@description('Messaging units for the Premium tier. Ignored on Standard.')
@allowed([1, 2, 4, 8, 16])
param premiumMessagingUnits int = 1

@description('Name of the topic quote-created events are published to.')
param topicName string = 'quote-created'

@description('Name of the "audit" subscription - one of the two competing consumers on the topic.')
param auditSubscriptionName string = 'audit'

@description('Name of the "stats" subscription - the other competing consumer on the topic.')
param statsSubscriptionName string = 'stats'

@description('Max delivery attempts before a message is dead-lettered.')
param maxDeliveryCount int = 10

@description('Tags applied to the namespace and its child resources.')
param tags object = {}

resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
    capacity: skuName == 'Premium' ? premiumMessagingUnits : null
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: namespace
  name: topicName
  properties: {}
}

// Two independent competing-consumer subscriptions on the same topic -
// audit and stats each get their own copy of every message
// (day-5/QuotesApi/Messaging/AuditSubscriptionWorker.cs and
// StatsSubscriptionWorker.cs), so a slow/failing consumer on one
// subscription never blocks the other.
resource auditSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: topic
  name: auditSubscriptionName
  properties: {
    maxDeliveryCount: maxDeliveryCount
    deadLetteringOnMessageExpiration: true
  }
}

resource statsSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: topic
  name: statsSubscriptionName
  properties: {
    maxDeliveryCount: maxDeliveryCount
    deadLetteringOnMessageExpiration: true
  }
}

// Send+Listen only, not the namespace's default RootManageSharedAccessKey
// (which also grants Manage - create/delete topics, etc.). The app only
// ever needs to publish and receive; least privilege for this
// credential the same way Key Vault Secrets User is for the JWT key.
resource sendListenRule 'Microsoft.ServiceBus/namespaces/AuthorizationRules@2022-10-01-preview' = {
  parent: namespace
  name: 'SendListen'
  properties: {
    rights: [
      'Send'
      'Listen'
    ]
  }
}

output namespaceName string = namespace.name
output serviceBusEndpoint string = namespace.properties.serviceBusEndpoint
output connectionString string = sendListenRule.listKeys().primaryConnectionString
