using System.Net;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class LargeContentTests : TestBase
{
    private const int TargetLength = 262_144; // 256 * 1024

    /// <summary>
    /// Generates exactly <paramref name="length"/> characters of markdown content
    /// by repeating a template pattern that includes various markdown elements.
    /// </summary>
    private static string GenerateMarkdownContent(int length)
    {
        const string template = """
            ## Section {0}

            Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore.

            > This is a blockquote for section {0}. It demonstrates that quotes work correctly.

            | Column A | Column B | Column C |
            |----------|----------|----------|
            | Value {0}a | Value {0}b | Value {0}c |
            | Data {0}x  | Data {0}y  | Data {0}z  |

            ```csharp
            // Code block for section {0}
            public class TestClass{0}
            {{
                public string GetValue() => "value-{0}";
            }}
            ```

            Here is some `inline code` and a [link](https://example.com/{0}) for reference.

            - Item A-{0}: This is a list item with enough text to fill space naturally.
            - Item B-{0}: Another list item that adds more content to the document.

            ---

            """;

        var sb = new System.Text.StringBuilder(length);
        var counter = 0;

        while (sb.Length < length)
        {
            var block = template.Replace("{0}", counter.ToString());
            sb.Append(block);
            counter++;
        }

        // Trim to exact length
        if (sb.Length > length)
        {
            sb.Length = length;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts a Guid from a redirect Location header like "/Home/Edit/{guid}?saved=True"
    /// </summary>
    private static Guid ExtractIdFromRedirect(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        // Pattern: /Home/Edit/{guid}?saved=...
        var match = System.Text.RegularExpressions.Regex.Match(
            location, @"/Home/Edit/([0-9a-fA-F-]{36})");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not extract document ID from redirect location: {location}");
        }
        return Guid.Parse(match.Groups[1].Value);
    }

    [TestMethod]
    public async Task SaveNew_With256KBContent_SavesCompletelyWithoutTruncation()
    {
        // Arrange
        var markdown = GenerateMarkdownContent(TargetLength);
        Assert.AreEqual(TargetLength, markdown.Length, "Generated content should be exactly the target length.");

        await RegisterAndLoginAsync();

        // Act: Save via POST. Note: SaveNew replaces the DocumentId with a new Guid,
        // so we extract the real ID from the redirect Location header.
        var response = await PostForm("/Home/SaveNew", new Dictionary<string, string>
        {
            { "DocumentId", Guid.NewGuid().ToString() },
            { "Title", "Large Document Test - SaveNew" },
            { "InputMarkdown", markdown }
        });

        // Assert: Should redirect to Edit page
        System.Console.WriteLine($"Response status: {response.StatusCode}");
        System.Console.WriteLine($"Location: {response.Headers.Location}");
        if (response.StatusCode != HttpStatusCode.Found)
        {
            var body = await response.Content.ReadAsStringAsync();
            System.Console.WriteLine($"Response body (first 500 chars): {body[..Math.Min(500, body.Length)]}");
        }
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode,
            $"Expected 302 Found but got {response.StatusCode}. The large form body may have been rejected.");

        var actualDocumentId = ExtractIdFromRedirect(response);

        // Verify: Read from database directly to confirm no truncation
        using var scope = GetService<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var savedDocument = await db.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == actualDocumentId);

        Assert.IsNotNull(savedDocument, $"Document {actualDocumentId} should exist in the database.");
        Assert.AreEqual(TargetLength, savedDocument!.Content!.Length,
            $"Content length should be {TargetLength} but was {savedDocument.Content!.Length}. " +
            "Truncation is still happening — SafeSubstring limit may not have been raised.");
        Assert.AreEqual(markdown[..100], savedDocument.Content[..100],
            "First 100 chars should match (not truncated at the beginning).");
        Assert.AreEqual(markdown[^100..], savedDocument.Content[^100..],
            "Last 100 chars should match (not truncated at the end).");
    }

    [TestMethod]
    public async Task SaveUpdate_With256KBContent_SavesCompletelyWithoutTruncation()
    {
        // Arrange: Register, login, and create a document via SaveNew first
        await RegisterAndLoginAsync();

        // Create an initial document via SaveNew with small content
        var createResponse = await PostForm("/Home/SaveNew", new Dictionary<string, string>
        {
            { "DocumentId", Guid.NewGuid().ToString() },
            { "Title", "Initial small document" },
            { "InputMarkdown", "# Small initial content" }
        });
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var documentId = ExtractIdFromRedirect(createResponse);

        // Act: Update with 256KB content via SaveUpdate
        var markdown = GenerateMarkdownContent(TargetLength);
        Assert.AreEqual(TargetLength, markdown.Length);

        var response = await PostForm("/Home/SaveUpdate", new Dictionary<string, string>
        {
            { "DocumentId", documentId.ToString() },
            { "Title", "Large Document Test - SaveUpdate" },
            { "InputMarkdown", markdown }
        });

        // Assert: SaveUpdate returns 200 OK with JSON on success
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        // Verify: Read from database directly
        using var scope = GetService<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var updatedDocument = await db.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId);

        Assert.IsNotNull(updatedDocument, "Document should exist in the database.");
        Assert.AreEqual(TargetLength, updatedDocument!.Content!.Length,
            $"Content length should be {TargetLength} but was {updatedDocument.Content!.Length}.");
        Assert.AreEqual(markdown[..100], updatedDocument.Content[..100],
            "First 100 chars should match.");
        Assert.AreEqual(markdown[^100..], updatedDocument.Content[^100..],
            "Last 100 chars should match (full content was saved).");
    }
}
