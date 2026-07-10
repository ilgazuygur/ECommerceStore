using ECommerceStore.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ECommerceStore.Tests.TestInfrastructure;

/// <summary>
/// A temporary on-disk SQLite database. Unlike a shared in-memory connection, a file database lets
/// multiple contexts open independent connections with real write locking, so genuine concurrency
/// (last-unit races, duplicate submissions) can be exercised deterministically.
/// </summary>
internal sealed class SqliteFileTestDatabase : IAsyncDisposable
{
    private readonly string _path;
    private readonly string _connectionString;

    private SqliteFileTestDatabase(string path)
    {
        _path = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            // Wait for a held write lock instead of failing fast, matching real request behavior.
            DefaultTimeout = 30
        }.ToString();
    }

    public static async Task<SqliteFileTestDatabase> CreateAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ecommerce-test-{Guid.NewGuid():N}.db");
        var database = new SqliteFileTestDatabase(path);
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return database;
    }

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await ValueTask.CompletedTask;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }

                return;
            }
            catch (IOException)
            {
                await Task.Delay(25);
            }
        }
    }
}
