using Dzl.Core.Workshop;
using FluentAssertions;
using Xunit;

public class WorkshopTests
{
    // --- keyless browse (HTML scrape) ---

    [Fact]
    public void Web_browse_url_has_sort_days_searchtext_and_required_tags()
    {
        var url = WorkshopWeb.BrowseUrl("trend", 7, "trader mod", 2, new[] { "Vehicle", "Weapon" });
        url.Should().Contain("appid=221100");
        url.Should().Contain("browsesort=trend");
        url.Should().Contain("days=7");
        url.Should().Contain("p=2");
        url.Should().Contain("searchtext=trader%20mod");
        url.Should().Contain("requiredtags%5B%5D=Vehicle");
        url.Should().Contain("requiredtags%5B%5D=Weapon");
    }

    [Fact]
    public void Web_browse_url_omits_days_for_non_trend_sorts()
        => WorkshopWeb.BrowseUrl("mostrecent", 7, "", 1, null).Should().NotContain("days=");

    [Fact]
    public void Web_parse_details_reads_subs_description_and_tags()
    {
        const string json = """
        {"response":{"publishedfiledetails":[
          {"publishedfileid":"42","title":"Trader","file_description":"A trader mod","subscriptions":999,
           "preview_url":"https://p","time_updated":123,"tags":[{"tag":"Economy"},{"tag":"Mechanics"}]}
        ]}}
        """;
        var i = WorkshopWeb.ParseDetails(json, "42")!;
        i.Subscriptions.Should().Be(999);
        i.Description.Should().Be("A trader mod");
        i.Tags.Should().Be("Economy, Mechanics");
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
    public void Subscribe_payload_matches_a_captured_request()
        // protobuf: id=3737385977, field2=1, appid=221100, field4=1 — verified against a real Subscribe POST.
        => WorkshopWeb.SubscribePayload("3737385977").Should().Be("CPn3j/YNEAEYrL8NIAE=");

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
