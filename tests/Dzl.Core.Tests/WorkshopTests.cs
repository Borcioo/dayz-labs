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
    public void ParseSearch_reads_browse_metadata_preview_subs_tags()
    {
        const string json = """
        {"response":{"publishedfiledetails":[
          {"publishedfileid":"42","title":"Trader","preview_url":"https://img/x.jpg","subscriptions":12345,
           "time_created":1600000000,"tags":[{"tag":"Mechanic","display_name":"Mechanic"},{"display_name":"Economy"}]}
        ]}}
        """;
        var i = WorkshopApi.ParseSearch(json).Should().ContainSingle().Subject;
        i.PreviewUrl.Should().Be("https://img/x.jpg");
        i.Subscriptions.Should().Be(12345);
        i.Created.Should().Be(1600000000);
        i.Tags.Should().Be("Mechanic, Economy");
    }

    [Theory]
    [InlineData("top", 0)]
    [InlineData("recent", 1)]
    [InlineData("trending", 3)]
    [InlineData("search", 12)]
    [InlineData("whatever", 0)]
    public void QueryType_maps_modes(string mode, int qt)
        => WorkshopApi.QueryType(mode).Should().Be(qt);

    // --- keyless browse (HTML scrape) ---

    [Theory]
    [InlineData("top", "totaluniquesubscribers")]
    [InlineData("recent", "mostrecent")]
    [InlineData("trending", "trend")]
    [InlineData("search", "textsearch")]
    public void Web_sort_maps_modes(string mode, string sort) => WorkshopWeb.Sort(mode).Should().Be(sort);

    [Fact]
    public void Web_browse_url_has_appid_sort_and_searchtext()
    {
        var url = WorkshopWeb.BrowseUrl("search", "trader mod", 1);
        url.Should().Contain("appid=221100");
        url.Should().Contain("browsesort=textsearch");
        url.Should().Contain("searchtext=trader%20mod");
    }

    [Fact]
    public void Web_parse_extracts_id_title_preview_from_hashed_markup_and_dedupes()
    {
        // Mirrors current Steam markup: hashed classes, title in the img alt, dup links per item.
        const string html =
            @"<a href=""https://steamcommunity.com/sharedfiles/filedetails/?id=3737385977"" class=""rKsVn"">" +
            @"<img src=""https://images.steamusercontent.com/ugc/x/?imw=288"" alt=""TP_Apoc_M1025"" loading=""lazy""/></a>" +
            @"<a href=""https://steamcommunity.com/sharedfiles/filedetails/?id=3737385977"" class=""q""></a>" +
            @"<a href=""https://steamcommunity.com/sharedfiles/filedetails/?id=111"" class=""z""><img src=""p2"" alt=""Second &amp; Co""/></a>";
        var items = WorkshopWeb.ParseBrowse(html);
        items.Should().HaveCount(2);                       // deduped (id 3737385977 once)
        items[0].Id.Should().Be("3737385977");
        items[0].Title.Should().Be("TP_Apoc_M1025");
        items[0].PreviewUrl.Should().Contain("imw=288");
        items[1].Title.Should().Be("Second & Co");         // HTML-decoded
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
