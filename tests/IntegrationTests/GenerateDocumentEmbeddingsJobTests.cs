using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class GenerateDocumentEmbeddingsJobTests : TestBase
{
    [TestMethod]
    public async Task TrySaveEmbeddingIfDocumentUnchangedAsyncSkipsStaleEmbedding()
    {
        var sourceUpdatedAt = DateTime.UtcNow.AddMinutes(-5);
        Guid documentId;
        MarkdownDocument trackedDocument;

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var admin = await db.Users.FirstAsync();
        var document = new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Original",
            Content = "Original content",
            UserId = admin.Id,
            UpdatedAt = sourceUpdatedAt
        };
        db.MarkdownDocuments.Add(document);
        await db.SaveChangesAsync();

        documentId = document.Id;
        trackedDocument = await db.MarkdownDocuments.FirstAsync(d => d.Id == documentId);

        using (var updateScope = Server.Services.CreateScope())
        {
            var updateDb = updateScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var changedDocument = await updateDb.MarkdownDocuments.FirstAsync(d => d.Id == documentId);
            changedDocument.Content = "Changed content";
            changedDocument.UpdatedAt = sourceUpdatedAt.AddMinutes(1);
            await updateDb.SaveChangesAsync();
        }

        var saved = await GenerateDocumentEmbeddingsJob.TrySaveEmbeddingIfDocumentUnchangedAsync(
            db,
            trackedDocument,
            sourceUpdatedAt,
            [1f, 0f]);

        Assert.IsFalse(saved);

        using var verifyScope = Server.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var savedDocument = await verifyDb.MarkdownDocuments.FirstAsync(d => d.Id == documentId);
        Assert.IsNull(savedDocument.Embedding);
        Assert.AreEqual(DateTime.MinValue, savedDocument.LastEmbeddedAt);
    }

    [TestMethod]
    public async Task TrySaveEmbeddingIfDocumentUnchangedAsyncSavesCurrentEmbedding()
    {
        var sourceUpdatedAt = DateTime.UtcNow.AddMinutes(-5);

        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var admin = await db.Users.FirstAsync();
        var document = new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Title = "Current",
            Content = "Current content",
            UserId = admin.Id,
            UpdatedAt = sourceUpdatedAt
        };
        db.MarkdownDocuments.Add(document);
        await db.SaveChangesAsync();

        var saved = await GenerateDocumentEmbeddingsJob.TrySaveEmbeddingIfDocumentUnchangedAsync(
            db,
            document,
            sourceUpdatedAt,
            [1f, 0f]);

        Assert.IsTrue(saved);
        Assert.IsNotNull(document.Embedding);
        Assert.AreEqual(sourceUpdatedAt, document.LastEmbeddedAt);
    }
}
