using Dzl.Tray.ViewModels;
using FluentAssertions;

/// <summary>
/// Tests for <see cref="TypesEditorVm"/> — the densest VM logic in the app: checkbox batch operations,
/// snapshot undo/redo, and duplication with collision-safe naming. All in-memory: rows are seeded via
/// AddType (which tolerates a mission-less config — SourceFile just resolves to ""), so no types.xml is
/// needed. Run on STA ([WpfFact]) because the ctor builds a CollectionViewSource over the rows.
/// </summary>
public class TypesEditorVmTests
{
    private static TypesEditorVm Vm() => new(CeScaffold.NoMission());

    private static TypeRowVm AddRow(TypesEditorVm vm, string name)
    {
        vm.AddType(name);
        return vm.Types.Last();
    }

    // ── batch numeric ────────────────────────────────────────────────────
    [WpfFact]
    public void BatchApply_set_assigns_and_clamps_negative_to_zero()
    {
        var vm = Vm();
        var a = AddRow(vm, "A"); a.Nominal = 10;

        vm.BatchApply(new[] { a }, "nominal", 50, multiply: false);
        a.Nominal.Should().Be(50);

        vm.BatchApply(new[] { a }, "nominal", -5, multiply: false);
        a.Nominal.Should().Be(0, "set clamps below zero");
    }

    [WpfFact]
    public void BatchApply_multiply_rounds_and_clamps()
    {
        var vm = Vm();
        var a = AddRow(vm, "A"); a.Nominal = 10;

        vm.BatchApply(new[] { a }, "nominal", 2.5, multiply: true);
        a.Nominal.Should().Be(25, "10 × 2.5 = 25");
    }

    [WpfFact]
    public void BatchApply_quant_fields_allow_negative()
    {
        var vm = Vm();
        var a = AddRow(vm, "A");

        vm.BatchApply(new[] { a }, "quantmin", -1, multiply: false);
        a.QuantMin.Should().Be(-1, "quantmin/quantmax keep the -1 'not set' sentinel");
    }

    [WpfFact]
    public void BatchDivide_zero_is_a_no_op()
    {
        var vm = Vm();
        var a = AddRow(vm, "A"); a.Nominal = 100;

        vm.BatchDivide(new[] { a }, "nominal", 0);
        a.Nominal.Should().Be(100, "divide-by-zero must never zero the column");

        vm.BatchDivide(new[] { a }, "nominal", 4);
        a.Nominal.Should().Be(25, "100 / 4 = 25");
    }

    // ── batch flags ──────────────────────────────────────────────────────
    [WpfFact]
    public void BatchFlag_set_clear_toggle()
    {
        var vm = Vm();
        var a = AddRow(vm, "A"); a.CountInMap = false;

        vm.BatchFlag(new[] { a }, "map", "set");
        a.CountInMap.Should().BeTrue();

        vm.BatchFlag(new[] { a }, "map", "toggle");
        a.CountInMap.Should().BeFalse();

        vm.BatchFlag(new[] { a }, "map", "clear");
        a.CountInMap.Should().BeFalse();
    }

    // ── batch lists ──────────────────────────────────────────────────────
    [WpfFact]
    public void BatchList_add_dedups_case_insensitively_then_remove()
    {
        var vm = Vm();
        var a = AddRow(vm, "A");

        vm.BatchList(new[] { a }, "usage", "Military", add: true);
        vm.BatchList(new[] { a }, "usage", "military", add: true);   // case-insensitive duplicate → ignored
        a.Usage.Should().ContainSingle().Which.Should().Be("Military");

        vm.BatchList(new[] { a }, "usage", "MILITARY", add: false);  // remove is case-insensitive too
        a.Usage.Should().BeEmpty();
    }

    [WpfFact]
    public void BatchCategory_sets_trimmed_category_on_all_rows()
    {
        var vm = Vm();
        var a = AddRow(vm, "A");
        var b = AddRow(vm, "B");

        vm.BatchCategory(new[] { a, b }, "  weapons ");
        a.Category.Should().Be("weapons");
        b.Category.Should().Be("weapons");
    }

    // ── duplicate ────────────────────────────────────────────────────────
    [WpfFact]
    public void DuplicateTypes_uses_collision_safe_copy_names()
    {
        var vm = Vm();
        var gun = AddRow(vm, "Gun");

        vm.DuplicateTypes(new[] { gun });
        vm.DuplicateTypes(vm.Types.Where(t => t.Name == "Gun").ToList());

        vm.Types.Select(t => t.Name).Should().Contain(new[] { "Gun", "Gun_Copy", "Gun_Copy2" });
    }

    // ── undo / redo ──────────────────────────────────────────────────────
    [WpfFact]
    public void Batch_op_is_one_undo_step_and_redo_reapplies()
    {
        var vm = Vm();
        AddRow(vm, "A");
        vm.Types.Single().Nominal = 10;

        vm.BatchApply(vm.Types.ToList(), "nominal", 99, multiply: false);
        vm.Types.Single().Nominal.Should().Be(99);

        vm.UndoTypesCommand.Execute(null);
        vm.Types.Single().Nominal.Should().Be(10, "undo restores the pre-batch value");

        vm.RedoTypesCommand.Execute(null);
        vm.Types.Single().Nominal.Should().Be(99, "redo re-applies the batch");
    }

    [WpfFact]
    public void A_new_op_clears_the_redo_stack()
    {
        var vm = Vm();
        AddRow(vm, "A");
        vm.Types.Single().Nominal = 10;

        vm.BatchApply(vm.Types.ToList(), "nominal", 99, multiply: false);
        vm.UndoTypesCommand.Execute(null);
        vm.CanRedoTypes.Should().BeTrue();

        vm.BatchApply(vm.Types.ToList(), "nominal", 5, multiply: false);   // new op drops the redo branch
        vm.CanRedoTypes.Should().BeFalse();
        vm.Types.Single().Nominal.Should().Be(5);
    }

    // ── add / remove / checked state ─────────────────────────────────────
    [WpfFact]
    public void AddType_marks_unsaved_and_RemoveTypes_drops_rows()
    {
        var vm = Vm();
        vm.HasUnsavedChanges.Should().BeFalse();

        var a = AddRow(vm, "A");
        vm.HasUnsavedChanges.Should().BeTrue();
        vm.Types.Should().ContainSingle();

        vm.RemoveTypes(new[] { a });
        vm.Types.Should().BeEmpty();
    }

    [WpfFact]
    public void BatchMode_turns_on_at_two_checked_rows()
    {
        var vm = Vm();
        var a = AddRow(vm, "A");
        var b = AddRow(vm, "B");
        vm.BatchMode.Should().BeFalse();

        a.IsSelected = true;
        vm.CheckedTypeCount.Should().Be(1);
        vm.BatchMode.Should().BeFalse();

        b.IsSelected = true;
        vm.CheckedTypeCount.Should().Be(2);
        vm.BatchMode.Should().BeTrue("the batch panel shows once 2+ rows are checked");
    }
}
