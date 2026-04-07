using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trading.Oms.Infrastructure.Persistence;

namespace Trading.Oms.Infrastructure.Tests.Infrastructure;

internal sealed class SqliteOmsDbContext : IAsyncDisposable
{
    readonly SqliteConnection _connection;

    SqliteOmsDbContext(SqliteConnection connection, OmsDbContext context)
    {
        _connection = connection;
        Context = context;
    }

    public OmsDbContext Context { get; }

    public static async Task<SqliteOmsDbContext> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<OmsDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new OmsDbContext(options);
        await context.Database.EnsureCreatedAsync();

        return new SqliteOmsDbContext(connection, context);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
