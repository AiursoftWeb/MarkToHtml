using System.Text.Json;
using Aiursoft.MarkToHtml.Models.PublicViewModels;

namespace Aiursoft.MarkToHtml.Services;

/// <summary>
/// Discovers print theme plugins from static theme folders.
/// </summary>
public class PrintThemeService(IWebHostEnvironment environment)
{
    private const string ThemeRoot = "print-themes";
    private const string ManifestFileName = "theme.json";
    private const string CssFileName = "theme.css";
    private const string DefaultThemeId = "default";

    /// <summary>
    /// Gets all valid print theme plugins.
    /// </summary>
    /// <returns>The discovered print theme plugins.</returns>
    public IReadOnlyList<PrintThemePlugin> GetThemes()
    {
        var webRootPath = GetWebRootPath();
        if (webRootPath == null)
        {
            return [];
        }

        var root = Path.Combine(webRootPath, ThemeRoot);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var themes = new List<PrintThemePlugin>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var theme = ReadTheme(directory);
            if (theme != null)
            {
                themes.Add(theme);
            }
        }

        return themes
            .OrderBy(theme => theme.Order)
            .ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Picks a theme plugin ID, falling back to the default theme when needed.
    /// </summary>
    /// <param name="requestedThemeId">The requested theme plugin ID.</param>
    /// <returns>The selected print theme plugin.</returns>
    public PrintThemePlugin PickTheme(string requestedThemeId)
    {
        var themes = GetThemes();
        var theme = themes.FirstOrDefault(item => string.Equals(item.Id, requestedThemeId, StringComparison.OrdinalIgnoreCase));
        if (theme != null)
        {
            return theme;
        }

        return themes.FirstOrDefault(item => string.Equals(item.Id, DefaultThemeId, StringComparison.OrdinalIgnoreCase))
            ?? themes.FirstOrDefault()
            ?? PrintThemePlugin.Empty;
    }

    private static PrintThemePlugin? ReadTheme(string directory)
    {
        var manifestPath = Path.Combine(directory, ManifestFileName);
        var cssPath = Path.Combine(directory, CssFileName);
        if (!File.Exists(manifestPath) || !File.Exists(cssPath))
        {
            return null;
        }

        PrintThemeManifest? manifest;
        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<PrintThemeManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        var id = Path.GetFileName(directory);
        if (manifest == null || !IsValidThemeId(id) || !string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new PrintThemePlugin
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(manifest.Name) ? id : manifest.Name,
            CssPath = $"/{ThemeRoot}/{id}/{CssFileName}",
            PageBackground = string.IsNullOrWhiteSpace(manifest.PageBackground) ? "white" : manifest.PageBackground,
            Order = manifest.Order
        };
    }

    private static bool IsValidThemeId(string value)
    {
        return value.Length is > 0 and <= 64
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    private string? GetWebRootPath()
    {
        if (!string.IsNullOrWhiteSpace(environment.WebRootPath))
        {
            return environment.WebRootPath;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "src", "Aiursoft.MarkToHtml", "wwwroot");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Creates UI models for print theme selection.
    /// </summary>
    /// <returns>The print theme UI models.</returns>
    public IReadOnlyList<PrintThemeViewModel> GetThemeViewModels()
    {
        return GetThemes()
            .Select(theme => new PrintThemeViewModel
            {
                Id = theme.Id,
                Name = theme.Name
            })
            .ToList();
    }
}

/// <summary>
/// A discovered print theme plugin.
/// </summary>
public class PrintThemePlugin
{
    /// <summary>
    /// Empty theme used when no valid plugin exists.
    /// </summary>
    public static PrintThemePlugin Empty { get; } = new();

    /// <summary>
    /// The stable theme plugin ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The display name shown to users.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The static CSS path loaded by the print page.
    /// </summary>
    public string CssPath { get; set; } = string.Empty;

    /// <summary>
    /// The page background used in the print page rule.
    /// </summary>
    public string PageBackground { get; set; } = "white";

    /// <summary>
    /// The display order in selectors.
    /// </summary>
    public int Order { get; set; }
}

internal class PrintThemeManifest
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string PageBackground { get; set; } = "white";

    public int Order { get; set; }
}
