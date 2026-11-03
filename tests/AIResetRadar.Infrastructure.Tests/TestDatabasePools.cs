using Microsoft.Data.Sqlite;

namespace AIResetRadar.Infrastructure.Tests;

internal static class TestDatabasePools
{
    public static void Clear(string path)
    {
        // Fixtures own separate databases. Never dispose other parallel tests' pools.
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        SqliteConnection.ClearPool(connection);
    }
}
