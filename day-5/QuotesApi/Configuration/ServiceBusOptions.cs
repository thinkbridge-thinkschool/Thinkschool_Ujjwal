namespace QuotesApi.Configuration;

public record ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    // day-26: [Required] + ValidateOnStart() used to live here, and it
    // was a live landmine - production has no ServiceBus__ConnectionString
    // app setting (Service Bus was never actually deployed; see
    // day-23/README.md's deployServiceBus flag), so any redeploy of this
    // code would crash the entire app at startup over a background
    // messaging feature nothing needs to serve a single HTTP request.
    // Found by a failing integration test (SeedBehaviorTests), not by
    // inspection - it reproduced the exact crash a real deploy would have
    // hit.
    //
    // These defaults are syntactically valid but non-functional: enough
    // for ServiceBusClient's constructor and CreateSender to succeed
    // without throwing, so OutboxRelay still reaches its per-row
    // processing loop and genuinely attempts (and fails, and logs, and
    // retries) a publish - the honest behavior for an environment where
    // Service Bus really isn't there, rather than a hard crash over it.
    public string ConnectionString { get; init; } =
        "Endpoint=sb://not-configured.servicebus.windows.net/;SharedAccessKeyName=none;SharedAccessKey=none=";

    public string TopicName { get; init; } = "not-configured";
}
