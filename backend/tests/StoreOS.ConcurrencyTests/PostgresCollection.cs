using StoreOS.IntegrationTests;

namespace StoreOS.ConcurrencyTests;

[CollectionDefinition("postgres")]
public class ConcurrencyPostgresCollection : ICollectionFixture<PostgresTestFixture>
{
}
