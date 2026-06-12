using System.CommandLine.Invocation;
using Dzl.Core.Tools;

namespace Dzl.Cli;

/// <summary>Shared console-output helpers for command handlers.</summary>
internal static class CliOut
{
    /// <summary>Print an operation result message; exit code 1 when it failed.</summary>
    public static void Result(InvocationContext ctx, bool ok, string message)
    {
        Console.WriteLine(message);
        if (!ok) ctx.ExitCode = 1;
    }

    /// <summary>Print an error to stderr and set exit code 1. Returns false for guard chaining.</summary>
    public static bool Fail(InvocationContext ctx, string message)
    {
        Console.Error.WriteLine(message);
        ctx.ExitCode = 1;
        return false;
    }

    /// <summary>Print one line per item, or a placeholder when the collection is empty.</summary>
    public static void List<T>(IReadOnlyCollection<T> items, string empty, Func<T, string> line)
    {
        if (items.Count == 0)
        {
            Console.WriteLine(empty);
            return;
        }
        foreach (var item in items) Console.WriteLine(line(item));
    }

    /// <summary>
    /// Find a DayZ tool by key; returns null (and reports the error + exit code 1) when
    /// it is unknown or its exe is missing on disk.
    /// </summary>
    public static ToolEntry? RequireTool(InvocationContext ctx, string toolsPath, string key)
    {
        var tool = ToolCatalog.Find(toolsPath, key);
        if (tool is null || !tool.Exists)
        {
            Fail(ctx, $"tool not found or missing: {key}");
            return null;
        }
        return tool;
    }
}
