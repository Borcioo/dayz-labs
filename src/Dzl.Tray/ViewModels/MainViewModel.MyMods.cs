using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dzl.Core.App;
using Dzl.Core.Build;
using Dzl.Core.Config;
using Dzl.Core.Build.Preflight;
using Dzl.Core.Projects;
using Dzl.Core.Vcs;

namespace Dzl.Tray.ViewModels;

public partial class MainViewModel
{
    // === My Mods (source projects) page ===================================

    /// <summary>Mod source projects discovered under the ProjectsRoot (drives the My Mods page).</summary>
    public ObservableCollection<ModProjectVm> ModProjects { get; } = new();

    private ICollectionView? _modProjectsView;
    /// <summary>Filtered view over <see cref="ModProjects"/> (bound by the My Mods list), driven by
    /// <see cref="ProjectFilter"/>. Built lazily on first bind (UI thread).</summary>
    public ICollectionView ModProjectsView =>
        _modProjectsView ??= BuildView(CollectionViewSource.GetDefaultView(ModProjects), FilterModProject);

    private static ICollectionView BuildView(ICollectionView view, Predicate<object> filter)
    {
        view.Filter = filter;
        return view;
    }

    [ObservableProperty] private string _projectFilter = "";
    partial void OnProjectFilterChanged(string value) => ModProjectsView.Refresh();

    private bool FilterModProject(object obj)
    {
        if (string.IsNullOrEmpty(ProjectFilter)) return true;
        if (obj is not ModProjectVm p) return true;
        bool Matches(ModProjectVm x) =>
            x.Name.Contains(ProjectFilter, StringComparison.OrdinalIgnoreCase)
            || x.Path.Contains(ProjectFilter, StringComparison.OrdinalIgnoreCase);
        // A pack stays visible when its name OR any inner mod matches.
        return Matches(p) || p.Children.Any(Matches);
    }

    /// <summary>Resolved ProjectsRoot (configured value or the %USERPROFILE% fallback). Shown on
    /// the My Mods / Servers pages so the user knows where dzl creates things.</summary>
    public string ProjectsRoot => ProjectPaths.Root(_cfg);

    /// <summary>The config directory (author cache lives here, next to config.json).</summary>
    private string ConfigDir => Path.GetDirectoryName(_configPath) ?? ".";

    /// <summary>Cached author handle (for prefilling the New mod form), or "".</summary>
    public string CachedAuthor => ModScaffold.CachedAuthor(ConfigDir) ?? "";

    // Persisted, view-only UI state (collapsed pack groups). Loaded once; survives the frequent
    // ModProjects rebuilds AND app restarts (ui-state.json, separate from config/presets).
    private UiState? _uiStateBacking;
    private UiState UiState => _uiStateBacking ??= UiState.Load(_configPath);

    /// <summary>Re-enumerate mod source projects. Called on My Mods page show + after create/import/link.</summary>
    [RelayCommand]
    public void RefreshModProjects()
    {
        ModProjects.Clear();
        foreach (var p in Dzl.Core.Projects.ModProjects.Discover(ProjectsRoot, WorkDriveSource))
        {
            var expanded = !p.IsPack || !UiState.IsPackCollapsed(p.Name);
            var vm = new ModProjectVm(p, expanded);
            if (p.IsPack) vm.PropertyChanged += OnPackExpandedChanged;   // remember collapse/expand
            ModProjects.Add(vm);
        }
        OnPropertyChanged(nameof(ProjectsRoot));
        _ = LoadGitStatusesAsync();   // fire-and-forget; fills each card's git badge off the UI thread
    }

    private void OnPackExpandedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ModProjectVm.IsExpanded) || sender is not ModProjectVm vm) return;
        UiState.SetPackCollapsed(vm.Name, !vm.IsExpanded);
        UiState.Save(_configPath);
    }

    // Bumped per refresh so an older in-flight badge pass can't overwrite a newer one's results.
    private int _gitGen;

    /// <summary>Fill each project card's git summary off the UI thread (git status shells out).</summary>
    private async Task LoadGitStatusesAsync()
    {
        var gen = ++_gitGen;
        var root = ProjectsRoot;
        foreach (var vm in ModProjects.ToList())
        {
            var dir = ProjectPaths.ModDir(root, vm.Name);
            var s = await Task.Run(() => Git.Status(dir));
            if (gen != _gitGen) return;   // a newer refresh owns the badges now
            if (!s.IsRepo) { vm.Git = "no repo"; vm.RepoUrl = null; continue; }
            var ab = (s.Ahead > 0 || s.Behind > 0) ? $" ↑{s.Ahead}↓{s.Behind}" : "";
            var local = s.HasRemote ? "" : " (local)";
            vm.Git = $"{s.Branch} • {s.Detail}{ab}{local}";
            var url = s.HasRemote ? await Task.Run(() => Git.RemoteUrl(dir)) : null;
            if (gen != _gitGen) return;
            vm.RepoUrl = url;
        }
    }

    /// <summary>Scaffold a new mod project + link P:\&lt;Mod&gt;. Caches the author. Optionally initialises a
    /// local git repo with a first commit. The scaffold + junction + git I/O runs off the UI thread; the
    /// project-list refresh hops back. Returns a status line.</summary>
    public async Task<string> CreateModProjectAsync(string name, string author, bool initGit = false)
    {
        var root = ProjectsRoot;
        var source = WorkDriveSource;
        var dir = ProjectPaths.ModDir(root, name);
        var msg = await Task.Run(() =>
        {
            var res = ModScaffold.Scaffold(root, name, author);
            if (!res.Ok) return $"✗ {res.Message}";
            if (!string.IsNullOrWhiteSpace(author)) ModScaffold.SaveAuthor(ConfigDir, author);
            var link = Junction.Ensure(ProjectPaths.JunctionPath(source, name), dir);
            var m = link.Ok ? $"✓ created {name} + linked P:\\{name}" : $"✓ created {name}  (⚠ P:\\ link: {link.Detail})";
            if (initGit)
            {
                var gi = Dzl.Core.Vcs.Git.Init(dir);
                if (gi.ok) Dzl.Core.Vcs.Git.CommitAll(dir, "Initial commit (dzl scaffold)");
                m += gi.ok ? "  + git repo" : $"  (⚠ git: {gi.msg})";
            }
            return m;
        });
        RefreshModProjects();
        return msg;
    }

    /// <summary>How a GitHub import treats version control. <see cref="Clone"/> keeps the repo's
    /// .git (your own mod); <see cref="Snapshot"/> strips it (samples/templates you just want the
    /// files of); <see cref="Fresh"/> strips it and starts a new local repo with an initial commit
    /// (sample as the starting point of YOUR mod — publish later from the Git window).</summary>
    public enum GitHubImportMode { Clone, Snapshot, Fresh }

    /// <summary>Clone a GitHub repo into the projects tree as a mod (folder named <paramref name="name"/> or
    /// derived from the repo), then link P:\&lt;Mod&gt;. Needs gh installed + logged in. Returns a status line.</summary>
    public async Task<string> ImportFromGitHubAsync(string repo, string? name, GitHubImportMode mode = GitHubImportMode.Clone)
    {
        repo = repo?.Trim() ?? "";
        if (repo.Length == 0) return "✗ enter a GitHub repo (owner/name or URL)";
        var modName = SanitizeModName(string.IsNullOrWhiteSpace(name) ? DeriveRepoName(repo) : name!.Trim());
        if (!ProjectPaths.IsValidName(modName)) return $"✗ couldn't derive a valid mod name — type one (letters, digits, _)";
        var root = ProjectsRoot;
        var source = WorkDriveSource;
        var dest = ProjectPaths.ModDir(root, modName);
        if (Directory.Exists(dest)) return $"✗ {modName} already exists";

        // Clone (network) + optional .git rewrite + junction all run off the UI thread; the
        // project-list refresh always hops back afterward (even on a clone failure).
        var msg = await Task.Run(() =>
        {
            var clone = Dzl.Core.Vcs.GitHub.Clone(repo, dest);
            if (!clone.ok) return $"✗ clone failed: {clone.msg}";

            var vcsNote = "";
            if (mode != GitHubImportMode.Clone)
            {
                var (ok, m) = DeleteGitDir(dest);
                if (!ok) vcsNote = $"  (⚠ couldn't remove .git: {m})";
                else if (mode == GitHubImportMode.Fresh)
                {
                    var init = Dzl.Core.Vcs.Git.Init(dest);
                    var commit = init.ok ? Dzl.Core.Vcs.Git.CommitAll(dest, $"Initial commit — imported from {repo} (dzl)") : init;
                    vcsNote = commit.ok ? "  (fresh repo, initial commit)" : $"  (⚠ re-init: {commit.msg})";
                }
                else vcsNote = "  (no .git — plain files)";
            }

            var link = Junction.Ensure(ProjectPaths.JunctionPath(source, modName), dest);
            return link.Ok
                ? $"✓ imported {modName} from GitHub + linked P:\\{modName}{vcsNote}"
                : $"✓ imported {modName}  (⚠ link: {link.Detail}){vcsNote}";
        });
        RefreshModProjects();
        return msg;
    }

    /// <summary>Delete a clone's .git folder (read-only-safe via <see cref="FileOps.ForceDeleteDirectory"/>).</summary>
    private static (bool ok, string msg) DeleteGitDir(string dir)
    {
        try { FileOps.ForceDeleteDirectory(Path.Combine(dir, ".git")); return (true, ""); }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>True when gh is installed + logged in (drives the "From GitHub" tab availability).
    /// Set off the UI thread by <see cref="RefreshGitHubAuthAsync"/> — never shell out during a binding read.</summary>
    [ObservableProperty] private bool _gitHubReady;

    /// <summary>Suggested mod-folder name for a repo URL/slug (sanitized repo name; "" when the
    /// input doesn't look like a repo yet). Drives the name auto-fill on the From GitHub tab.</summary>
    public static string SuggestModName(string repo)
    {
        repo = repo?.Trim() ?? "";
        if (repo.Length == 0) return "";
        var name = SanitizeModName(DeriveRepoName(repo));
        return ProjectPaths.IsValidName(name) ? name : "";
    }

    /// <summary>The source folder for a mod project (for the per-mod git window).</summary>
    public string ModDirOf(string name) => ProjectPaths.ModDir(ProjectsRoot, name);

    /// <summary>Publish a project to GitHub (init + commit + gh repo create) for the git window. Returns the result.</summary>
    public async Task<(bool ok, string msg)> PublishForGitAsync(string name)
    {
        var cp = _configPath;
        var r = await Task.Run(() => new RepoService(cp).Publish(name));
        RefreshModProjects();
        return (r.Ok, r.Message);
    }

    /// <summary>Cut a GitHub release for a project from the git window (full options + optional built-PBO assets).</summary>
    public async Task<(bool ok, string msg)> ReleaseForGitAsync(string name, Dzl.Core.Vcs.ReleaseOptions opts, bool attachBuiltAddons)
    {
        var cp = _configPath;
        var r = await Task.Run(() => new RepoService(cp).Release(name, opts, attachBuiltAddons));
        return (r.Ok, r.Message);
    }

    private static string DeriveRepoName(string repo)
    {
        var s = repo.TrimEnd('/');
        var slash = s.LastIndexOf('/');
        if (slash >= 0) s = s[(slash + 1)..];
        return s.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }

    private static string SanitizeModName(string s)
    {
        var chars = s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var name = new string(chars).Trim('_');
        if (name.Length > 0 && !char.IsLetter(name[0])) name = "Mod_" + name;
        return name;
    }

    /// <summary>Import an external mod source folder as a project (non-invasive link). Returns a status line.</summary>
    public string ImportModProject(string source, string? name)
    {
        var res = ModImport.Import(ProjectsRoot, source, string.IsNullOrWhiteSpace(name) ? null : name.Trim(), WorkDriveSource);
        RefreshModProjects();
        return res.Ok ? $"✓ imported → {res.ModDir}" : $"✗ {res.Message}";
    }

    /// <summary>(Re)create the P:\&lt;Mod&gt; junction for a project. Returns a status line.</summary>
    public string QuickJunction(string name)
    {
        var link = Junction.Ensure(ProjectPaths.JunctionPath(WorkDriveSource, name), ProjectPaths.ModDir(ProjectsRoot, name));
        RefreshModProjects();
        return link.Ok ? $"✓ {name}: {link.Detail}" : $"✗ {name}: {link.Detail}";
    }

    /// <summary>Delete a mod project: remove its P: source junction (link only), delete the source folder, and
    /// optionally the build output. Destructive — the caller confirms first. Returns a status line.</summary>
    public async Task<string> DeleteModProjectAsync(string name, bool alsoBuild)
    {
        var root = ProjectsRoot;
        var source = WorkDriveSource;
        // The recursive force-delete (read-only git files) can take a while; run it off the UI thread.
        var error = await Task.Run(() =>
        {
            try
            {
                Junction.Remove(ProjectPaths.JunctionPath(source, name));   // drop the link, never the target
                // Force-delete: cloned projects contain read-only git files that kill a plain delete.
                FileOps.ForceDeleteDirectory(ProjectPaths.ModDir(root, name));
                if (alsoBuild)
                    FileOps.ForceDeleteDirectory(ProjectPaths.BuildDir(root, name));
                return (string?)null;
            }
            catch (Exception ex) { return ex.Message; }
        });
        if (error is not null) { RefreshModProjects(); return $"✗ {name}: {error}"; }
        RefreshModProjects();
        Reload();   // the mod also drops out of the library / run-list discovery
        return $"✓ deleted {name}" + (alsoBuild ? " (source + build)" : " (source)");
    }

    /// <summary>Remove a mod's work-drive junction (leaves the source folder untouched). Returns a status line.</summary>
    public string UnlinkMod(string name)
    {
        var link = ProjectPaths.JunctionPath(WorkDriveSource, name);
        try { if (Junction.IsLink(link)) Junction.Remove(link); }
        catch (Exception ex) { RefreshModProjects(); return $"✗ {name}: {ex.Message}"; }
        RefreshModProjects();
        return $"✓ unlinked {name}";
    }

    // === Build → deploy (SP2) ============================================

    /// <summary>Live AddonBuilder log for the most recent build (shown on the My Mods page).</summary>
    [ObservableProperty] private string _buildLog = "";

    /// <summary>True while a build is running — used to disable the build buttons.</summary>
    [ObservableProperty] private bool _building;

    /// <summary>Build a mod project into a PBO off the UI thread, streaming the AddonBuilder log; on
    /// success register the <c>@&lt;Mod&gt;</c> into the active server's run-list and refresh.
    /// Returns the result (incl. the gate's preflight view) so callers can render findings;
    /// null when a build is already running.</summary>
    public async Task<BuildResult?> BuildModAsync(string name, bool clean = false, bool binarize = true, bool sign = false, bool force = false, string? keyName = null)
    {
        if (Building) return null;
        Building = true;
        BuildLog = $"▸ Building {name} (clean={clean}, binarize={binarize}, sign={sign}, force={force}) …\n";
        var configPath = _configPath;
        var result = await Task.Run(() =>
            new BuildService(configPath).Build(name, clean: clean, binarize: binarize, sign: sign,
                onLine: line => _dispatcher.BeginInvoke(() => BuildLog += line + "\n"), force: force,
                keyName: keyName));
        BuildLog += (result.Ok ? "\n✓ " : "\n✗ ") + result.Message + "\n";
        if (!result.Ok && result.Diagnostics.Length > 0)
            BuildLog += "\n" + result.Diagnostics + "\n";
        Building = false;
        if (result.Ok) { Reload(); RefreshModProjects(); }
        return result;
    }

    /// <summary>Build a PACK off the UI thread: its selected inner mods are packed into one shared
    /// <c>@&lt;pack&gt;</c> (Addons with many PBOs + keys) and registered as a single mod. Streams the log;
    /// null when a build is already running.</summary>
    public async Task<BuildService.PackBuildResult?> BuildPackAsync(
        string packName, IReadOnlyList<string> selected, bool binarize = true, bool sign = false,
        string? keyName = null, bool ignorePreflightErrors = false)
    {
        if (Building) return null;
        Building = true;
        BuildLog = $"▸ Building pack {packName} ({selected.Count} mod(s), binarize={binarize}, sign={sign}" +
                   (ignorePreflightErrors ? ", build-anyway" : "") + ") …\n";
        var configPath = _configPath;
        var result = await Task.Run(() =>
            new BuildService(configPath).BuildPack(packName, selected, binarize: binarize, sign: sign,
                onLine: line => _dispatcher.BeginInvoke(() => BuildLog += line + "\n"), keyName: keyName,
                ignorePreflightErrors: ignorePreflightErrors));
        BuildLog += (result.Ok ? "\n✓ " : "\n✗ ") + result.Message + "\n";
        Building = false;
        if (result.Ok) { Reload(); RefreshModProjects(); }
        return result;
    }

    /// <summary>Preflight a pack's selected inner mods off the UI thread (same checks the build gates on).</summary>
    public Task<IReadOnlyList<BuildService.PackPreflight>> PreflightPackAsync(string packName, IReadOnlyList<string> selected)
    {
        var configPath = _configPath;
        return Task.Run(() => new BuildService(configPath).PreflightPack(packName, selected));
    }

    /// <summary>Preflight a mod project off the UI thread (configs, references, paths, scripts).</summary>
    public Task<PreflightView> PreflightAsync(string name)
    {
        var configPath = _configPath;
        return Task.Run(() => new BuildService(configPath).Preflight(name));
    }

    /// <summary>Resolved path/tool preview for the Build options dialog (no side effects).</summary>
    public BuildService.BuildPlanView BuildPlan(string name) => new BuildService(_configPath).Plan(name);

    /// <summary>Signing keys present in the keys folder (for the Settings/Build pickers).</summary>
    public IReadOnlyList<BuildService.SigningKeyInfo> ListSigningKeys() =>
        new BuildService(_configPath).ListKeys();

    /// <summary>Create the creator's signing key (DSCreateKey). Returns a status line.</summary>
    public string GenerateSigningKey()
    {
        var r = new BuildService(_configPath).GenerateKey();
        return r.Ok ? $"✓ key ready: {r.PrivateKey}" : $"✗ {r.Output}";
    }
}
