namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class HistoryTests : TestBase
{
    [TestMethod]
    public async Task GetHistory()
    {
        await RegisterAndLoginAsync();
        var url = "/Home/History";
        
        var response = await Http.GetAsync(url);
        
        response.EnsureSuccessStatusCode();
    }
}
