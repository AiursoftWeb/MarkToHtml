using Aiursoft.MarkToHtml.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Tests;

[TestClass]
public class MigrationTests
{
    [TestMethod]
    public void ModelAndMigrationShouldBeConsistent()
    {
        var services = new ServiceCollection();
        var dbPath = Path.GetFullPath($"test-db-{Guid.NewGuid()}.db");
        services.AddDbContext<SqliteContext>(options =>
        {
            options.UseSqlite($"Data Source={dbPath}");
        });
        services.AddEntityFrameworkSqlite();
        var serviceProvider = services.BuildServiceProvider();
        try
        {
            using var context = serviceProvider.GetRequiredService<SqliteContext>();
            context.Database.Migrate();
            var hasPendingChanges = context.Database.HasPendingModelChanges();
            Assert.IsFalse(hasPendingChanges,
                "❌ Detected model changes that are not reflected in a new migration. " +
                "Please run 'dotnet ef migrations add YourMigrationName' to create a new migration file.");
            Console.WriteLine(@"Migrations are consistent with the current model.");
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}
