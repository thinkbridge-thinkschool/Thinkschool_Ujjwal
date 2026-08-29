namespace Quotes.Tests.Integration;

// Any test class whose factory mutates process environment variables in its
// constructor (see the comment on PolicyTestFactory) must run in this collection.
// xUnit parallelizes across collections by default, and concurrent env var writes
// from different factories race with each other's host startup.
[CollectionDefinition("EnvironmentMutatingTests", DisableParallelization = true)]
public class EnvironmentMutatingTestCollection
{
}
