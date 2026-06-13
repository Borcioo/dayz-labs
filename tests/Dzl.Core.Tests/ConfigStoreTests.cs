using Dzl.Core.Config;
using FluentAssertions;

public class ConfigStoreTests
{
    [Fact]
    public void Default_has_expected_scalars()
    {
        var c = DzlConfig.Default();
        c.Port.Should().Be(2302);
        c.Mode.Should().Be("debug");
        c.ConnectIp.Should().Be("127.0.0.1");
        c.ServerParamsDebug.Should().Contain("-filePatching");
        c.ClientParamsDebug.Should().Contain("-window");
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "config.json");
        var c = DzlConfig.Default() with { Port = 2345, Mods = new() { new ModEntry { Path = @"P:\@CF", Enabled = true } } };
        ConfigStore.Save(c, path);
        var loaded = ConfigStore.Load(path);
        loaded.Port.Should().Be(2345);
        loaded.Mods.Should().ContainSingle().Which.Path.Should().Be(@"P:\@CF");
    }

    [Fact]
    public void Saved_json_uses_python_snake_case_keys()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "config.json");
        ConfigStore.Save(DzlConfig.Default(), path);
        var json = File.ReadAllText(path);
        json.Should().Contain("\"dayz_path\"").And.Contain("\"server_params_debug\"");
    }

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        ConfigStore.Load(path).Port.Should().Be(2302);
    }

    [Fact]
    public void Migrates_pre_per_mode_params_to_debug()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "config.json");
        // simulate an old config: has server_params/client_params, no *_debug keys
        File.WriteAllText(path, "{ \"server_params\": [\"-legacyS\"], \"client_params\": [\"-legacyC\"] }");
        var cfg = ConfigStore.Load(path);
        cfg.ServerParamsDebug.Should().ContainSingle().Which.Should().Be("-legacyS");
        cfg.ClientParamsDebug.Should().ContainSingle().Which.Should().Be("-legacyC");
        cfg.ServerParamsNormal.Should().BeEquivalentTo(DzlConfig.Default().ServerParamsNormal); // untouched
    }
}
