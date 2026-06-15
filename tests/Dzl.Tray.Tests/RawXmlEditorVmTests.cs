using Dzl.Tray.Controls;
using FluentAssertions;

/// <summary>
/// Tests for <see cref="RawXmlEditorVm"/>, the raw-file-snapshot undo/redo engine shared by every CE editor
/// tab (RandomPresets / SpawnableTypes / Globals / Events / PlayerSpawns). A bug here regresses all five, so
/// it's worth locking directly. The base takes Func delegates for read/write/path, so a fake in-memory store
/// exercises the whole engine with no filesystem.
/// </summary>
public class RawXmlEditorVmTests
{
    /// <summary>Concrete RawXmlEditorVm over an in-memory "file" string. Edit() mimics a real edit:
    /// snapshot-for-undo (PushUndo) then write, exactly as the subclasses do around each service call.</summary>
    private sealed class FakeEditor : RawXmlEditorVm
    {
        private sealed class Store { public string? Raw; }

        private readonly Store _store;
        public int ReloadViewCount { get; private set; }

        public FakeEditor(string? initial, Func<string, bool>? confirm = null)
            : this(new Store { Raw = initial }, confirm) { }

        private FakeEditor(Store store, Func<string, bool>? confirm)
            : base(() => store.Raw,
                   raw => { store.Raw = raw; return (true, "saved"); },
                   () => "fake.xml",
                   "(no file)",
                   confirm)
            => _store = store;

        public string? Current => _store.Raw;

        /// <summary>Snapshot the current state then write the new one — the PushUndo-then-write pattern.</summary>
        public void Edit(string newRaw) { PushUndo(); _store.Raw = newRaw; }

        protected override void ReloadView() => ReloadViewCount++;

        /// <summary>Stand-in for "the selected row" so the selection-token hooks can be exercised.</summary>
        public string? Selected { get; set; }
        protected override string? CaptureSelectionToken() => Selected;
        protected override void RestoreSelectionToken(string? token) => Selected = token;
    }

    [Fact]
    public void Undo_then_redo_round_trips_the_content()
    {
        var e = new FakeEditor("v0");
        e.CanUndo.Should().BeFalse();
        e.CanRedo.Should().BeFalse();

        e.Edit("v1");
        e.CanUndo.Should().BeTrue();

        e.UndoCommand.Execute(null);
        e.Current.Should().Be("v0");
        e.CanRedo.Should().BeTrue();

        e.RedoCommand.Execute(null);
        e.Current.Should().Be("v1");
        e.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Undo_and_redo_restore_the_selection_captured_around_the_edit()
    {
        var e = new FakeEditor("v0") { Selected = "rowA" };
        e.Edit("v1");            // PushUndo captures Selected = "rowA"
        e.Selected = "rowB";     // the action moved the selection (e.g. a rename re-sorted the list)

        e.UndoCommand.Execute(null);
        e.Selected.Should().Be("rowA", "undo reselects what was selected before the edited action");

        e.RedoCommand.Execute(null);
        e.Selected.Should().Be("rowB", "redo restores the post-edit selection");
    }

    [Fact]
    public void A_fresh_edit_clears_the_redo_stack()
    {
        var e = new FakeEditor("v0");
        e.Edit("v1");
        e.UndoCommand.Execute(null);     // back to v0, redo now holds v1
        e.CanRedo.Should().BeTrue();

        e.Edit("v2");                    // a new edit must drop the redo branch
        e.CanRedo.Should().BeFalse();
        e.Current.Should().Be("v2");
    }

    [Fact]
    public void Undo_history_is_capped_at_50_steps()
    {
        var e = new FakeEditor("s0");
        for (var i = 1; i <= 60; i++) e.Edit($"s{i}");   // 60 edits, cap is 50

        var undos = 0;
        while (e.CanUndo) { e.UndoCommand.Execute(null); undos++; }

        undos.Should().Be(50, "the undo stack is capped at 50 entries (oldest dropped)");
    }

    [Fact]
    public void Reload_keeps_undo_history_when_the_confirm_prompt_is_declined()
    {
        var e = new FakeEditor("v0", confirm: _ => false);
        e.Edit("v1");

        e.Reload();   // there is history to lose → confirm asked → declined → no-op

        e.CanUndo.Should().BeTrue("a declined reload must not discard the undo history");
        e.ReloadViewCount.Should().Be(0, "a declined reload must not re-read the view");
    }

    [Fact]
    public void EnsureLoaded_loads_once_and_re_activation_is_a_silent_noop()
    {
        var prompts = 0;
        var e = new FakeEditor("v0", confirm: _ => { prompts++; return true; });

        e.EnsureLoaded();
        e.ReloadViewCount.Should().Be(1, "first tab activation loads the view");

        e.Edit("v1");   // build up undo history, as the user would by editing
        e.EnsureLoaded();   // switching away and back must NOT re-read or prompt

        e.ReloadViewCount.Should().Be(1, "re-activating an already-loaded tab does not reload");
        e.CanUndo.Should().BeTrue("re-activation preserves the in-progress undo history");
        prompts.Should().Be(0, "tab re-activation never shows the discard-undo confirm");
    }

    [Fact]
    public void Reload_clears_undo_history_when_confirmed()
    {
        var e = new FakeEditor("v0", confirm: _ => true);
        e.Edit("v1");

        e.Reload();

        e.CanUndo.Should().BeFalse("a confirmed reload drops the undo history");
        e.CanRedo.Should().BeFalse();
        e.ReloadViewCount.Should().Be(1, "reload re-reads the view once");
    }
}
