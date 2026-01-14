using Aiursoft.MarkToHtml.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.MySql;

public class MySqlContext(DbContextOptions<MySqlContext> options) : TemplateDbContext(options);
