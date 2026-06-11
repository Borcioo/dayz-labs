using Dzl.Core.Build;
using FluentAssertions;
using Xunit;

public class BuildDiagnosticsTests
{
    [Fact]
    public void Access_violation_maps_to_odol_diagnosis()
    {
        var d = BuildDiagnostics.Diagnose("Binarize task failed. 0xC0000005 - ACCESS_VIOLATION at ...");
        d.Should().ContainSingle();
        d[0].Cause.Should().Contain("ODOL");
    }

    [Fact]
    public void Include_failure_maps_to_include_diagnosis()
    {
        var d = BuildDiagnostics.Diagnose("File config.cpp: Cannot include file gear\\common.hpp");
        d[0].Title.Should().Contain("include");
    }

    [Fact]
    public void Multiple_distinct_symptoms_yield_multiple_diagnoses()
    {
        var log = "error 3 while parsing config\nDSSignFile failed: no .bisign produced";
        BuildDiagnostics.Diagnose(log).Should().HaveCount(2);
    }

    [Fact]
    public void Unknown_text_yields_nothing()
    {
        BuildDiagnostics.Diagnose("everything is perfectly fine").Should().BeEmpty();
        BuildDiagnostics.Diagnose("").Should().BeEmpty();
    }

    [Fact]
    public void Kick_codes_decode_to_packaging_causes()
    {
        BuildDiagnostics.DiagnoseKick("client kicked: 0x0004007E VE_MISSING_BISIGN")
            .Single().Fix.Should().Contain("--sign");
        BuildDiagnostics.DiagnoseKick("err 0x00020005 filePatching mismatch")
            .Single().Fix.Should().Contain("allowFilePatching");
        BuildDiagnostics.DiagnoseKick("VE_UM_CLIENT_UPDATED")
            .Single().Title.Should().Contain("version skew");
    }

    [Fact]
    public void Summarize_counts_error_warning_missing_lines()
    {
        var (e, w, m) = BuildDiagnostics.Summarize("Error: x\nWarning: y\nCannot open file z\nok line");
        e.Should().BeGreaterThanOrEqualTo(2);   // "Error" + "Cannot "
        w.Should().Be(1);
        m.Should().Be(1);
    }

    [Fact]
    public void Format_renders_numbered_cause_fix_block()
    {
        var text = BuildDiagnostics.Format(BuildDiagnostics.Diagnose("access violation"));
        text.Should().Contain("1.").And.Contain("why:").And.Contain("fix:");
        BuildDiagnostics.Format(new List<Diagnosis>()).Should().BeEmpty();
    }
}
