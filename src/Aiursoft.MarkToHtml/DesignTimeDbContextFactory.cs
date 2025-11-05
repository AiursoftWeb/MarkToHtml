using Aiursoft.MarkToHtml.MySql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aiursoft.MarkToHtml; // 使用与你的 DbContext 相同的命名空间

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MySqlContext>
{
    public MySqlContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MySqlContext>();
        var dummyConnectionString = "Server=localhost;Database=template;Uid=template;Pwd=template_password;";
        optionsBuilder.UseMySql(dummyConnectionString, ServerVersion.AutoDetect(dummyConnectionString));
        return new MySqlContext(optionsBuilder.Options);
    }
}
