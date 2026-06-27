using Dzl.Core.Env;
using FluentAssertions;

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
    public void EnsureAbsoluteTemplate_rewrites_relative_template_to_the_instance_mission_path()
    {
        // DayZ forces $currentdir to the exe dir, so a bare template name resolves to the INSTALL's
        // mpmissions — never the instance's. An absolute template path (verified live) makes the server
        // load the instance's own mission (where dzl's CE edits live).
        var dir = Directory.CreateTempSubdirectory().FullName;
        var cfgPath = Path.Combine(dir, "serverDZ.cfg");
        File.WriteAllText(cfgPath, "class Missions{class DayZ{template = \"dayzOffline.chernarusplus\";};};");
        var missionDir = Path.Combine(dir, "mpmissions", "dayzOffline.chernarusplus");

        ServerScaffold.EnsureAbsoluteTemplate(cfgPath, missionDir);

        File.ReadAllText(cfgPath).Should().Contain($"template = \"{missionDir}\"")
            .And.NotContain("template = \"dayzOffline.chernarusplus\"");
    }

    [Fact]
    public void EnsureAbsoluteTemplate_is_noop_when_template_already_points_at_the_mission()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var cfgPath = Path.Combine(dir, "serverDZ.cfg");
        var missionDir = Path.Combine(dir, "mpmissions", "dayzOffline.chernarusplus");
        File.WriteAllText(cfgPath, $"class Missions{{class DayZ{{template = \"{missionDir}\";}};}};");
        var before = File.GetLastWriteTimeUtc(cfgPath);

        ServerScaffold.EnsureAbsoluteTemplate(cfgPath, missionDir);

        File.GetLastWriteTimeUtc(cfgPath).Should().Be(before);   // no rewrite, no mtime churn
    }

    [Theory]
    [InlineData("storage_1", true)]
    [InlineData("storage_42", true)]
    [InlineData("STORAGE_1", true)]
    [InlineData("db", false)]
    [InlineData("env", false)]
    [InlineData("cfgplayerspawnpoints.xml", false)]
    public void Mission_copy_skips_live_storage_dirs(string name, bool skip)
        => ServerScaffold.IsRuntimeDir(name).Should().Be(skip);

    [Fact]
    public void WipePersistence_removes_storage_dirs_only()
    {
        var inst = Directory.CreateTempSubdirectory().FullName;
        var mission = Path.Combine(inst, "mpmissions", "dayzOffline.chernarusplus");
        Directory.CreateDirectory(Path.Combine(mission, "storage_1"));
        Directory.CreateDirectory(Path.Combine(mission, "storage_2"));
        Directory.CreateDirectory(Path.Combine(mission, "db"));
        File.WriteAllText(Path.Combine(mission, "init.c"), "");

        ServerScaffold.WipePersistence(inst).Should().Be(2);
        Directory.Exists(Path.Combine(mission, "storage_1")).Should().BeFalse();
        Directory.Exists(Path.Combine(mission, "db")).Should().BeTrue();        // real content kept
        File.Exists(Path.Combine(mission, "init.c")).Should().BeTrue();
        ServerScaffold.WipePersistence(inst).Should().Be(0);                    // idempotent
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
