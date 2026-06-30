using Dzl.Core.Logs;
using FluentAssertions;

public class LogLineClassifierTests
{
    // --- per-category classification (the quick-filter buckets) ---

    [Theory]
    [InlineData("19:53:40.13 SCRIPT (E): Class 'Foo' not found")]
    [InlineData("Cannot open object dz\\foo.p3d")]
    [InlineData("ErrorMessage: something blew up")]
    [InlineData("Can't compile world script module!")]
    [InlineData("Unhandled exception in EnforceScript")]
    public void Classify_flags_errors(string line) =>
        LogLineClassifier.Classify(line).Should().HaveFlag(LogCategory.Error);

    [Theory]
    [InlineData("19:53:39.420 ENTITY     (W): Unknown object class 'building'")]
    [InlineData("Warning: deprecated config entry")]
    public void Classify_flags_warnings(string line) =>
        LogLineClassifier.Classify(line).Should().HaveFlag(LogCategory.Warning);

    [Theory]
    [InlineData("Player \"Survivor\" is connected (id=abc)")]
    [InlineData("Player \"Survivor\" has been disconnected")]
    public void Classify_flags_connections(string line) =>
        LogLineClassifier.Classify(line).Should().HaveFlag(LogCategory.Connection);

    [Theory]
    [InlineData("Loading mod @CF (id ...)")]
    [InlineData("Mission read.")]
    [InlineData("[CE][Hive] :: Initializing OFFLINE")]
    public void Classify_flags_mod_success(string line) =>
        LogLineClassifier.Classify(line).Should().HaveFlag(LogCategory.ModSuccess);

    [Fact]
    public void Classify_plain_line_has_no_category()
    {
        // A neutral data line shouldn't fall into any quick-filter bucket.
        LogLineClassifier.Classify("19:53:40.357 ENTITY      : Load entity type 'evg_barDoor_Double'")
            .Should().Be(LogCategory.None);
    }

    // --- the predicate the pane uses: filter (bucket) AND search (substring) ---

    [Fact]
    public void Matches_all_filter_keeps_every_line_when_search_is_empty()
    {
        LogLineClassifier.Matches("any old line", filter: "all", search: "").Should().BeTrue();
    }

    [Fact]
    public void Matches_errors_filter_keeps_only_error_lines()
    {
        LogLineClassifier.Matches("SCRIPT (E): boom", "errors", "").Should().BeTrue();
        LogLineClassifier.Matches("ENTITY (W): meh", "errors", "").Should().BeFalse();
    }

    [Fact]
    public void Matches_search_is_case_insensitive_substring()
    {
        LogLineClassifier.Matches("Unknown object class 'pond'", "all", "POND").Should().BeTrue();
        LogLineClassifier.Matches("Unknown object class 'pond'", "all", "tree").Should().BeFalse();
    }

    [Fact]
    public void Matches_combines_filter_and_search_with_and()
    {
        // warning bucket AND contains "pond"
        LogLineClassifier.Matches("ENTITY (W): Unknown object class 'pond'", "warnings", "pond").Should().BeTrue();
        LogLineClassifier.Matches("ENTITY (W): Unknown object class 'tree'", "warnings", "pond").Should().BeFalse();
        LogLineClassifier.Matches("SCRIPT (E): pond error", "warnings", "pond").Should().BeFalse(); // wrong bucket
    }

    [Fact]
    public void Matches_unknown_filter_is_treated_as_all()
    {
        LogLineClassifier.Matches("whatever", "bogus", "").Should().BeTrue();
    }
}
