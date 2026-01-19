using System.Diagnostics.CodeAnalysis;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.MySql;

[ExcludeFromCodeCoverage]

public class MySqlContext(DbContextOptions<MySqlContext> options) : TemplateDbContext(options);
