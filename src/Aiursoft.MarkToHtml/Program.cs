using System.Diagnostics.CodeAnalysis;
using Aiursoft.DbTools;
using Aiursoft.MarkToHtml.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.MarkToHtml;

[ExcludeFromCodeCoverage]
public abstract class Program
{
    public static async Task Main(string[] args)
    {
        var app = await AppAsync<Startup>(args);
        await app.UpdateDbAsync<TemplateDbContext>();
        await app.SeedAsync();
        await app.CopyAvatarFileAsync();
        await app.RunAsync();
    }
}
