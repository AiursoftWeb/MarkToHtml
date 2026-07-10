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

    public DbSet<SearchEmbedding> SearchEmbeddings => Set<SearchEmbedding>();

    // Categories and Tags
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<DocumentCategory> DocumentCategories => Set<DocumentCategory>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MarkdownDocumentFolder>()
            .Property<int>("ParentFolderIdForUniqueness")
            .HasComputedColumnSql("COALESCE(ParentFolderId, 0)", stored: true);

        builder.Entity<MarkdownDocumentFolder>()
            .HasIndex("ParentFolderIdForUniqueness", nameof(MarkdownDocumentFolder.Name), nameof(MarkdownDocumentFolder.UserId))
            .IsUnique();

        // Apply configurations for junction tables
        builder.ApplyConfiguration(new DocumentCategoryConfiguration());
        builder.ApplyConfiguration(new DocumentTagConfiguration());
    }
}
