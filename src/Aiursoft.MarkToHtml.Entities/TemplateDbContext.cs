using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Entities;

public abstract class TemplateDbContext(DbContextOptions options) : IdentityDbContext<User>(options), ICanMigrate
{
    public DbSet<GlobalSetting> GlobalSettings { get; set; }

    public virtual  Task MigrateAsync(CancellationToken cancellationToken) =>
        Database.MigrateAsync(cancellationToken);

    public virtual  Task<bool> CanConnectAsync() =>
        Database.CanConnectAsync();

    public DbSet<MarkdownDocument> MarkdownDocuments => Set<MarkdownDocument>();

    public DbSet<DocumentShare> DocumentShares => Set<DocumentShare>();

    public DbSet<MarkdownDocumentFolder> MarkdownDocumentFolders => Set<MarkdownDocumentFolder>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MarkdownDocumentFolder>()
            .HasIndex(f => new { f.ParentFolderId, f.Name, f.UserId })
            .IsUnique();
    }
}
