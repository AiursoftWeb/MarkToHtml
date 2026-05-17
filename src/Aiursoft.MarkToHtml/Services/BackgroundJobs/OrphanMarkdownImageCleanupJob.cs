using System.Text.RegularExpressions;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Services.BackgroundJobs;

/// <summary>
/// Scans the markdown-images storage directory and deletes any image file that is no longer
/// referenced by any document's markdown content in the database. Also skips files newer than
/// 24 hours to avoid deleting images that were just uploaded but not yet saved.
/// </summary>
public partial class OrphanMarkdownImageCleanupJob(
    TemplateDbContext db,
    FeatureFoldersProvider folders,
    ILogger<OrphanMarkdownImageCleanupJob> logger) : IBackgroundJob
{
    // 7h = one job cycle (6h) + 1h safety buffer, ensuring every image survives
    // at least one full cleanup pass before being eligible for deletion.
    private static readonly TimeSpan GracePeriod = TimeSpan.FromHours(7);

    public string Name => "Orphan Markdown Image Cleanup";

    public string Description =>
        "Scans the markdown-images storage directory and deletes image files " +
        "that are no longer referenced by any document, freeing disk space. " +
        "Files newer than 24 hours are always kept.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("OrphanMarkdownImageCleanupJob started.");

        // 1. Collect all image logical paths referenced in any document's markdown content.
        //    Image links in markdown look like: ![alt](/download/markdown-images/paste-xxx.png)
        //    We extract the path segment after "/download/" to get the logical path.
        var allContent = await db.MarkdownDocuments
            .Select(d => d.Content)
            .ToListAsync();

        var referencedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in allContent)
        {
            if (string.IsNullOrEmpty(content)) continue;
            foreach (Match match in MarkdownImageUrlRegex().Matches(content))
            {
                // match.Groups[1].Value = "markdown-images/paste-xxx.png"
                referencedPaths.Add(match.Groups[1].Value);
            }
        }

        logger.LogInformation(
            "OrphanMarkdownImageCleanupJob: {Count} markdown-images path(s) are referenced in the database.",
            referencedPaths.Count);

        // 2. Scan the workspace for files inside the 'markdown-images/' subdirectory.
        var workspace = folders.GetWorkspaceFolder();
        var imagesDir = Path.Combine(workspace, "markdown-images");

        if (!Directory.Exists(imagesDir))
        {
            logger.LogInformation(
                "OrphanMarkdownImageCleanupJob: markdown-images directory does not exist — nothing to clean.");
            return;
        }

        var allImageFiles = Directory
            .EnumerateFiles(imagesDir, "*", SearchOption.AllDirectories)
            .ToList();

        logger.LogInformation(
            "OrphanMarkdownImageCleanupJob: {Count} file(s) found in markdown-images directory.",
            allImageFiles.Count);

        // 3. Delete files that are not referenced AND older than the grace period.
        var cutoff = DateTime.UtcNow - GracePeriod;
        var deletedCount = 0;
        foreach (var physicalPath in allImageFiles)
        {
            var relativePath = Path
                .GetRelativePath(workspace, physicalPath)
                .Replace('\\', '/');

            if (referencedPaths.Contains(relativePath))
                continue;

            // Grace period: keep files that were recently uploaded (not yet saved to a document).
            var lastWriteTime = File.GetLastWriteTimeUtc(physicalPath);
            if (lastWriteTime >= cutoff)
            {
                logger.LogInformation(
                    "OrphanMarkdownImageCleanupJob: skipping '{RelativePath}' — within grace period (uploaded {Age:N0}h ago).",
                    relativePath, (DateTime.UtcNow - lastWriteTime).TotalHours);
                continue;
            }

            try
            {
                File.Delete(physicalPath);
                deletedCount++;
                logger.LogInformation(
                    "OrphanMarkdownImageCleanupJob: deleted orphan image '{RelativePath}'.",
                    relativePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "OrphanMarkdownImageCleanupJob: failed to delete '{RelativePath}'.",
                    relativePath);
            }
        }

        logger.LogInformation(
            "OrphanMarkdownImageCleanupJob finished. {Deleted}/{Total} orphan file(s) removed.",
            deletedCount, allImageFiles.Count);
    }

    // Matches: ![anything](/download/markdown-images/some/path.png)
    // Captures group 1: "markdown-images/some/path.png"
    // Singleline: allows alt text to span newlines.
    // Stops at ) ? # to avoid capturing query strings or anchors as part of the path.
    [GeneratedRegex(@"!\[.*?\]\(/download/(markdown-images/[^)?#]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MarkdownImageUrlRegex();
}
