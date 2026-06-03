using Dzl.Core.Env;
using FluentAssertions;
using Xunit;

public class EnvTests
{
    [Fact]
    public void ParseLibraryFolders_extracts_all_paths_unescaped()
    {
        var vdf = "\"libraryfolders\"\n{\n  \"0\"\n  {\n    \"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n  }\n  \"1\"\n  {\n    \"path\"\t\t\"D:\\\\SteamLibrary\"\n  }\n}";
        var libs = EnvDetect.ParseLibraryFolders(vdf);
        libs.Should().Contain(@"C:\Program Files (x86)\Steam").And.Contain(@"D:\SteamLibrary");
    }

    [Fact]
    public void FindApp_returns_first_existing_common_folder()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var lib = Path.Combine(root, "lib1");
        Directory.CreateDirectory(Path.Combine(lib, "steamapps", "common", "DayZ"));
        EnvDetect.FindApp(new[] { Path.Combine(root, "nope"), lib }, "DayZ")
            .Should().Be(Path.Combine(lib, "steamapps", "common", "DayZ"));
        EnvDetect.FindApp(new[] { lib }, "DayZServer").Should().BeNull();
    }

    [Fact]
    public void DefaultServerCfg_is_dev_friendly_and_has_mission()
    {
        var cfg = ServerScaffold.DefaultServerCfg("dayzOffline.chernarusplus");
        cfg.Should().Contain("hostname").And.Contain("verifySignatures = 0");
        cfg.Should().Contain("allowFilePatching = 1");                 // dev clients use -filePatching
        cfg.Should().Contain("template = \"dayzOffline.chernarusplus\"");  // exact folder name, no doubled .map
        cfg.Should().NotContain("chernarusplus.chernarusplus");        // regression: no double suffix
    }

    [Fact]
    public void SteamCmd_script_targets_app_223350_with_validate()
    {
        var s = SteamCmd.DownloadServerScript(@"D:\dzserver");
        s.Should().Contain("223350").And.Contain("force_install_dir").And.Contain(@"D:\dzserver").And.Contain("validate");
    }

    [Fact]
    public void ParseWorkDir_reads_quoted_value() =>
        EnvDetect.ParseWorkDir("Generic\n  WorkDirPath=\"C:\\Users\\m\\DayZ Projects\"\n").Should().Be(@"C:\Users\m\DayZ Projects");

    [Fact]
    public void ParseWorkDir_reads_projectdrive_section() =>
        EnvDetect.ParseWorkDir("[Game]\nuser=0\npath=D:\\SteamLibrary\\steamapps\\common\\DayZ\n\n[ProjectDrive]\nuser=0\npath=D:\\DayZ\\Workdrive_1\n\n[Tools]\nMount=0")
            .Should().Be(@"D:\DayZ\Workdrive_1");

    [Fact]
    public void ParseGamePath_reads_game_section() =>
        EnvDetect.ParseGamePath("[Game]\nuser=0\npath=D:\\SteamLibrary\\steamapps\\common\\DayZ\n\n[ProjectDrive]\npath=D:\\DayZ\\Workdrive_1")
            .Should().Be(@"D:\SteamLibrary\steamapps\common\DayZ");

    [Fact]
    public void InstallUri_builds_steam_protocol()
    {
        Dzl.Core.Env.SteamInstall.InstallUri(830640).Should().Be("steam://install/830640");
        Dzl.Core.Env.SteamInstall.DayZServer.Should().Be(223350);
    }

    [Fact]
    public void ValidateUri_builds_steam_validate_protocol() =>
        Dzl.Core.Env.SteamInstall.ValidateUri(830640).Should().Be("steam://validate/830640");

    [Fact]
    public void RunUri_builds_steam_run_protocol() =>
        Dzl.Core.Env.SteamInstall.RunUri(830640).Should().Be("steam://run/830640");
}
