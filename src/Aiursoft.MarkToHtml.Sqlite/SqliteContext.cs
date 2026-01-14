using Aiursoft.MarkToHtml.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Sqlite;

public class SqliteContext(DbContextOptions<SqliteContext> options) : TemplateDbContext(options)
{
    public override Task<bool> CanConnectAsync()
    {
        return Task.FromResult(true);
    }
}
