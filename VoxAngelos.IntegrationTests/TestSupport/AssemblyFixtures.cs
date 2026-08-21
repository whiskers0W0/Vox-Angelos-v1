using VoxAngelos.IntegrationTests.TestSupport;

// All tests share one running app instance and one local database — running test
// classes in parallel would make DB-state assertions (counts, "only one pending
// application", etc.) race against each other. Keep the whole suite sequential.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace VoxAngelos.IntegrationTests.TestSupport;

[CollectionDefinition("VoxAngelos App")]
public class AppCollection : ICollectionFixture<IdentityTestServices>
{
}
