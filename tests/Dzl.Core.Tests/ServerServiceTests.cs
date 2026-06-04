using Dzl.Core.App;
using Dzl.Core.Config;
using Dzl.Core.Servers;
using FluentAssertions;
using Xunit;

namespace Dzl.Core.Tests;

public class ServerServiceTests
{
    [Fact]
    public void Creates_five_distinct_server_presets()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var configPath = Path.Combine(tmp, "config.json");
        var root = Path.Combine(tmp, "projects");

        Profiles.EnsureDefault(configPath);

        // point projects_root via base config; DayzPath=tmp means no real mission to copy — fine
        var (baseCfg, _, _) = Profiles.ResolveActive(configPath);
        ConfigStore.Save(baseCfg with { ProjectsRoot = root, DayzPath = tmp }, configPath);

        var svc = new ServerService(configPath);
        foreach (var n in new[] { "alpha", "bravo", "charlie", "delta", "echo" })
            svc.Create(n, "chernarus").Ok.Should().BeTrue();

        var presets = Profiles.List(configPath);
        foreach (var n in new[] { "alpha", "bravo", "charlie", "delta", "echo" })
            presets.Should().Contain(n);

        var ports = new[] { "alpha", "bravo", "charlie", "delta", "echo" }
            .Select(n => Profiles.Load(n, configPath).Port)
            .ToList();
        ports.Should().OnlyHaveUniqueItems();

        foreach (var n in new[] { "alpha", "bravo", "charlie", "delta", "echo" })
            File.Exists(Path.Combine(root, "servers", n, "serverDZ.cfg")).Should().BeTrue();
    }
}
