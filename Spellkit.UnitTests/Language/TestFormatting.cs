using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Spellkit.UnitTesting;

internal static class TestFormatter
{
    private const string Header = "Test session from {0:dd/MM/yyyy HH:mm}";
    private const string FileHeader1 = "{0} test file(s):";
    private const string FileHeader2 = "Test file(s):";
    private const string Warnings = "Warnings:";
    private const string Report = "Report:";
    private const string SummaryHeader = "Summary:";
    private const string Summary = "{0} passed, {1} failed in {2} file(s), {3:F3} s total";

    public static string Format(TestReport report, TestFormatFlags flags)
    {
        var builder = new StringBuilder();

        if ((flags & TestFormatFlags.Markdown) == TestFormatFlags.Markdown)
        {
            FormatMd(builder, report, flags);
        }
        else
        {
            FormatText(builder, report, flags);
        }

        return builder.ToString();
    }

    private static void FormatMd(StringBuilder sb, TestReport report, TestFormatFlags flags)
    {
        sb.AppendLine("# " + string.Format(Header, DateTime.Now));
        sb.AppendLine();

        sb.AppendLine("## " + SummaryHeader);
        sb.AppendLine(string.Format(Summary,
            report.Results.Count(r => r.Error is null),
            report.Results.Count(r => r.Error is not null),
            report.TestFiles.Length,
            report.Results.Sum(r => r.Duration.TotalSeconds)
        ));
        sb.AppendLine();

        sb.AppendLine("## " + FileHeader2);
        sb.AppendLine(string.Join(", ", report.TestFiles
            .Select(f => "[" + Path.GetFileName(f) + "](" + Path.GetFullPath(f) + ")")));
        sb.AppendLine();

        if (report.BuildWarnings != null && report.BuildWarnings.Count > 0)
        {
            sb.AppendLine("## " + Warnings);

            foreach (var w in report.BuildWarnings)
            {
                sb.Append("* " + w);
            }

            sb.AppendLine();
        }

        IEnumerable<TestResult> results;
        var onlyFailed = (flags & TestFormatFlags.OnlyFailed) == TestFormatFlags.OnlyFailed;

        if (onlyFailed)
        {
            results = report.Results.Where(r => r.Error is not null);
        }
        else
        {
            results = report.Results;
        }

        if (!onlyFailed || results.Any())
        {
            sb.AppendLine("## " + Report);
            sb.AppendLine();

            foreach (var group in results.GroupBy(r => r.FileName))
            {
                sb.AppendLine("### " + GetShortFileName(group.Key));

                foreach (var f in group)
                {
                    if (f.Error is null)
                    {
                        sb.AppendLine($"* &#9745; **{f.Name}** ({FormatDuration(f.Duration)})");
                    }
                    else
                    {
                        FormatMdFailure(sb, f);
                    }
                }

                sb.AppendLine();
            }
        }
    }

    private static void FormatText(StringBuilder sb, TestReport report, TestFormatFlags flags)
    {
        sb.AppendLine(string.Format(Header, DateTime.Now));
        sb.AppendLine();
        sb.AppendLine(string.Format(FileHeader1, report.TestFiles.Length));
        sb.AppendLine(string.Join(", ", report.TestFiles.Select(Path.GetFileName)));
        sb.AppendLine();

        if (report.BuildWarnings != null && report.BuildWarnings.Count > 0)
        {
            sb.AppendLine(Warnings);

            foreach (var w in report.BuildWarnings)
            {
                sb.Append(w);
            }

            sb.AppendLine();
        }

        IEnumerable<TestResult> results;
        var onlyFailed = (flags & TestFormatFlags.OnlyFailed) == TestFormatFlags.OnlyFailed;

        if (onlyFailed)
        {
            results = report.Results.Where(r => r.Error is not null);
        }
        else
        {
            results = report.Results;
        }

        if (!onlyFailed || results.Any())
        {
            sb.AppendLine(Report);
            sb.AppendLine();

            foreach (var group in results.GroupBy(r => r.FileName))
            {
                sb.AppendLine(GetShortFileName(group.Key));

                foreach (var f in group)
                {
                    if (f.Error is null)
                    {
                        sb.AppendLine($"[+] \"{f.Name}\" ({FormatDuration(f.Duration)})");
                    }
                    else
                    {
                        FormatTextFailure(sb, f);
                    }
                }

                sb.AppendLine();
            }
        }

        sb.AppendLine(SummaryHeader);
        sb.AppendLine(string.Format(Summary,
            report.Results.Count(r => r.Error is null),
            report.Results.Count(r => r.Error is not null),
            report.TestFiles.Length,
            report.Results.Sum(r => r.Duration.TotalSeconds)
        ));
    }

    private static void FormatMdFailure(StringBuilder sb, TestResult result)
    {
        var name = result.Name is null ? "Unidentified test" : $"**{result.Name}**";
        sb.AppendLine($"* &#9746; {name}: {result.Error}");
        sb.AppendLine($"  * File: `{result.FileName}`");
        if (result.Name is not null)
        {
            sb.AppendLine($"  * Region: `{result.Name}`");
        }

        sb.AppendLine($"  * Duration: {FormatDuration(result.Duration)}");
        if (result.TimedOut)
        {
            sb.AppendLine("  * Timed out: yes");
        }

        if (result.Expected is not null)
        {
            sb.AppendLine($"  * Expected: `{result.Expected}`");
        }

        if (result.Actual is not null)
        {
            sb.AppendLine($"  * Actual: `{result.Actual}`");
        }

        if (result.ReproductionCommand is not null)
        {
            sb.AppendLine($"  * Reproduce: `{result.ReproductionCommand.Replace("`", "``")}`");
        }

        if (result.StackTrace is not null)
        {
            sb.AppendLine();
            sb.AppendLine("  <details><summary>Stack trace</summary>");
            sb.AppendLine();
            sb.AppendLine("  <pre>" + System.Net.WebUtility.HtmlEncode(result.StackTrace) + "</pre>");
            sb.AppendLine("  </details>");
        }
    }

    private static void FormatTextFailure(StringBuilder sb, TestResult result)
    {
        var name = result.Name is null ? string.Empty : $" \"{result.Name}\"";
        sb.AppendLine($"[ ]{name}: {result.Error}");
        sb.AppendLine($"    File: {result.FileName}");
        if (result.Name is not null)
        {
            sb.AppendLine($"    Region: {result.Name}");
        }

        sb.AppendLine($"    Duration: {FormatDuration(result.Duration)}");
        if (result.TimedOut)
        {
            sb.AppendLine("    Timed out: yes");
        }

        if (result.Expected is not null)
        {
            sb.AppendLine($"    Expected: {result.Expected}");
        }

        if (result.Actual is not null)
        {
            sb.AppendLine($"    Actual: {result.Actual}");
        }

        if (result.ReproductionCommand is not null)
        {
            sb.AppendLine($"    Reproduce: {result.ReproductionCommand}");
        }

        if (result.StackTrace is not null)
        {
            sb.AppendLine("    Stack trace:");
            foreach (var line in result.StackTrace.Replace("\r\n", "\n").Split('\n'))
            {
                sb.AppendLine("      " + line);
            }
        }
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds >= 1
            ? $"{duration.TotalSeconds:F3} s"
            : $"{duration.TotalMilliseconds:F1} ms";

    private static string GetShortFileName(string fileName)
    {
        var fi = new FileInfo(fileName);
        return $"{fi.Directory?.Name}/{fi.Name}:";
    }
}
