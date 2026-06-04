using System.IO;
using Dzl.Core.Config;
using FluentAssertions;
using Xunit;

public class ConfigSplitTests
{
    [Fact]
    public void Compose_of_extracted_parts_round_trips_every_field()
    {
        // A config with every field nudged off its default — guards GlobalPart/InstancePart/Compose
        // against forgetting to map a field.
        var d = DzlConfig.Default() with
        {
            DayzPath = "X:/dz", DayzToolsPath = "X:/tools", ProjectsRoot = "X:/proj",
            ExeDebug = "a", ExeNormal = "b", ClientExeDebug = "c", ClientExeNormal = "d",
            ScanRoots = new() { "r1", "r2" }, LogsShown = new() { "script" }, ModWidthIdx = 3,
            EnableAutomationServer = true, AutoLaunchTray = false,
            ProfilesPath = "X:/prof", ClientProfilesPath = "X:/cprof", Port = 1234,
            Mission = "m", PlayerName = "p", ConfigName = "s.cfg", ConnectIp = "1.2.3.4",
            Mods = new() { new ModEntry { Path = @"P:\@CF", Enabled = true, Side = "server" } },
            Mode = "normal",
            ServerParamsDebug = new() { "-sd" }, ServerParamsNormal = new() { "-sn" },
            ClientParamsDebug = new() { "-cd" }, ClientParamsNormal = new() { "-cn" },
        };

        var composed = DzlConfig.Compose(d.GlobalPart("act"), d.InstancePart());
        composed.Should().BeEquivalentTo(d);
    }

    [Fact]
    public void Migrates_legacy_config_and_presets_into_instances()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "config.json");
        // Legacy full config.json (global + per-server keys at root + active_preset pointer).
        File.WriteAllText(path, "{ \"dayz_path\": \"D:/DZ\", \"port\": 2500, \"mods\": [], \"active_preset\": \"pvp\" }");
        var presets = Path.Combine(dir, "presets");
        Directory.CreateDirectory(presets);
        File.WriteAllText(Path.Combine(presets, "pvp.json"), "{ \"port\": 2600 }");

        // ResolveActive triggers the one-time migration.
        var (cfg, _, active) = Profiles.ResolveActive(path);

        active.Should().Be("pvp");
        cfg.Port.Should().Be(2600);          // per-server, from the migrated pvp instance
        cfg.DayzPath.Should().Be("D:/DZ");   // global, preserved from the legacy base config
        Profiles.List(path).Should().Contain("pvp").And.Contain("default");
        File.Exists(Profiles.PresetFile("pvp", path)).Should().BeTrue();
        Directory.Exists(presets).Should().BeTrue();  // non-destructive: legacy presets/ left in place
    }

    [Fact]
    public void Global_and_instance_edits_persist_to_separate_files()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "config.json");
        Profiles.EnsureDefault(path);

        // global edit
        GlobalStore.Save(GlobalStore.Load(path) with { DayzPath = "G:/dz" }, path);
        // per-server edit on the active instance
        var (cfg, _, active) = Profiles.ResolveActive(path);
        Profiles.Save(cfg with { Port = 2999 }, active, path);

        var (reloaded, _, _) = Profiles.ResolveActive(path);
        reloaded.DayzPath.Should().Be("G:/dz");   // from config.json
        reloaded.Port.Should().Be(2999);          // from instances/default.json
        // config.json must NOT carry the per-server port (it's instance-only now)
        File.ReadAllText(path).Should().NotContain("\"port\"");
    }
}
