using Dzl.Core.Workshop;
using FluentAssertions;
using Xunit;

public class WorkshopTests
{
    [Fact]
    public void SearchUrl_includes_key_appid_query_and_text_search_type()
    {
        var url = WorkshopApi.SearchUrl("ABC123", "trader mod", 10);
        url.Should().Contain("key=ABC123");
        url.Should().Contain("appid=221100");
        url.Should().Contain("query_type=12");
        url.Should().Contain("search_text=trader%20mod");
        url.Should().Contain("numperpage=10");
    }

    [Fact]
    public void ParseSearch_reads_id_title_desc_and_tolerates_missing_fields()
    {
        const string json = """
        {"response":{"total":2,"publishedfiledetails":[
          {"publishedfileid":"1559212036","title":"Community Framework","short_description":"CF","time_updated":1700000000},
          {"publishedfileid":"2545327648","title":"GameLabs"}
        ]}}
        """;
        var items = WorkshopApi.ParseSearch(json);
        items.Should().HaveCount(2);
        items[0].Id.Should().Be("1559212036");
        items[0].Title.Should().Be("Community Framework");
        items[0].Updated.Should().Be(1700000000);
        items[1].Title.Should().Be("GameLabs");
        items[1].Updated.Should().Be(0);   // missing time_updated
    }

    [Fact]
    public void ParseSearch_empty_when_no_results()
    {
        WorkshopApi.ParseSearch("""{"response":{"total":0}}""").Should().BeEmpty();
    }

    [Fact]
    public void SteamCmd_command_line_uses_anonymous_or_login()
    {
        WorkshopCmd.CommandLine(null, "123").Should().Be("+login anonymous +workshop_download_item 221100 123 +quit");
        WorkshopCmd.CommandLine("macie", "123").Should().Be("+login macie +workshop_download_item 221100 123 +quit");
    }

    [Fact]
    public void SteamCmd_content_dir_is_under_steamapps_workshop_content()
    {
        WorkshopCmd.ContentDir(@"C:\steamcmd\steamcmd.exe", "123")
            .Should().Be(@"C:\steamcmd\steamapps\workshop\content\221100\123");
    }
}
