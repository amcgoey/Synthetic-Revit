using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Reporting;

/// <summary>
/// Summary counters produced when writing a TRX file.
/// </summary>
public sealed record TrxSummary(
    string TrxPath,
    int TotalTests,
    int PassedTests,
    int FailedTests,
    int SkippedTests);

/// <summary>
/// Writes Visual Studio 2010 TRX XML reports from <c>ricaun.NUnit</c> execution results
/// compatible with <c>test_executor.py</c>.
/// </summary>
public static class TrxWriter
{
    public static readonly XNamespace TrxNamespace =
        "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private static readonly Guid UnitTestTypeId =
        Guid.Parse("13cdc9d9-ddb5-4fa4-a97d-d965ccfc6d4b");

    private static readonly Guid DefaultTestListId =
        Guid.Parse("8c84fa94-04c1-424b-9868-57a2d4851a1d");

    internal readonly record struct CollectedTrxTest(
        string ClassName,
        TestModel Test,
        int ShardIndex = 0,
        DateTimeOffset? ShardStartTime = null);

    public static TrxSummary Write(
        string trxPath,
        string assemblyPath,
        TestAssemblyModel? assemblyModel,
        IReadOnlyList<TestModel>? streamedTests = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? finishTime = null)
    {
        DateTimeOffset runStart = startTime ?? DateTimeOffset.UtcNow;
        DateTimeOffset runFinish = finishTime ?? DateTimeOffset.UtcNow;
        if (runFinish < runStart)
        {
            runFinish = runStart;
        }

        List<(string ClassName, TestModel Test)> rawTests = CollectTests(assemblyModel, streamedTests);
        var collected = new List<CollectedTrxTest>(rawTests.Count);
        foreach ((string className, TestModel test) in rawTests)
        {
            collected.Add(new CollectedTrxTest(className, test, ShardIndex: 0, ShardStartTime: runStart));
        }

        return WriteCollectedTests(trxPath, assemblyPath, collected, runStart, runFinish);
    }

    internal static TrxSummary WriteCollectedTests(
        string trxPath,
        string assemblyPath,
        IReadOnlyList<CollectedTrxTest> collectedTests,
        DateTimeOffset runStart,
        DateTimeOffset runFinish)
    {
        if (string.IsNullOrWhiteSpace(trxPath))
        {
            throw new ArgumentException("TRX output path must not be empty.", nameof(trxPath));
        }

        ArgumentNullException.ThrowIfNull(collectedTests);

        string fullTrxPath = Path.GetFullPath(trxPath);
        string? trxDir = Path.GetDirectoryName(fullTrxPath);
        if (!string.IsNullOrEmpty(trxDir))
        {
            Directory.CreateDirectory(trxDir);
        }

        string normalizedAssemblyPath = string.IsNullOrWhiteSpace(assemblyPath)
            ? string.Empty
            : Path.GetFullPath(assemblyPath);

        if (runFinish < runStart)
        {
            runFinish = runStart;
        }

        int passed = 0;
        int failed = 0;
        int skipped = 0;

        var resultsElement = new XElement(TrxNamespace + "Results");
        var testDefinitionsElement = new XElement(TrxNamespace + "TestDefinitions");
        var testEntriesElement = new XElement(TrxNamespace + "TestEntries");
        var seenTestDefinitionIds = new HashSet<Guid>();
        var shardCursors = new Dictionary<int, DateTimeOffset>();

        string computerName = Environment.MachineName;

        foreach (CollectedTrxTest entry in collectedTests)
        {
            string className = entry.ClassName;
            TestModel test = entry.Test;

            string fullName = !string.IsNullOrWhiteSpace(test.FullName)
                ? test.FullName
                : (!string.IsNullOrWhiteSpace(test.Name) ? test.Name : "UnnamedTest");
            string methodName = !string.IsNullOrWhiteSpace(test.Name)
                ? test.Name
                : ExtractMethodName(fullName);
            string resolvedClassName = !string.IsNullOrWhiteSpace(className)
                ? className
                : ExtractClassName(fullName);

            Guid testId = CreateDeterministicGuid($"{normalizedAssemblyPath}::{fullName}");
            Guid executionId = Guid.NewGuid();

            if (!shardCursors.TryGetValue(entry.ShardIndex, out DateTimeOffset cursor))
            {
                cursor = entry.ShardStartTime ?? runStart;
            }

            // CRITICAL: TestModel.Time in ricaun.NUnit 1.5.0 is in milliseconds, NOT seconds.
            double clampedMs = double.IsFinite(test.Time) && test.Time > 0 ? test.Time : 0d;
            TimeSpan duration = TimeSpan.FromMilliseconds(clampedMs);
            DateTimeOffset testStart = cursor;
            DateTimeOffset testEnd = testStart + duration;
            shardCursors[entry.ShardIndex] = testEnd;

            string outcome;
            if (test.Skipped)
            {
                outcome = "NotExecuted";
                skipped++;
            }
            else if (test.Success)
            {
                outcome = "Passed";
                passed++;
            }
            else
            {
                outcome = "Failed";
                failed++;
            }

            var unitTestResult = new XElement(
                TrxNamespace + "UnitTestResult",
                new XAttribute("executionId", executionId.ToString("D")),
                new XAttribute("testId", testId.ToString("D")),
                new XAttribute("testName", SanitizeXmlString(fullName)),
                new XAttribute("computerName", SanitizeXmlString(computerName)),
                new XAttribute("duration", FormatTrxDuration(duration)),
                new XAttribute("startTime", testStart.ToString("O", CultureInfo.InvariantCulture)),
                new XAttribute("endTime", testEnd.ToString("O", CultureInfo.InvariantCulture)),
                new XAttribute("testType", UnitTestTypeId.ToString("D")),
                new XAttribute("outcome", outcome),
                new XAttribute("testListId", DefaultTestListId.ToString("D")),
                new XAttribute("relativeResultsDirectory", executionId.ToString("D")));

            XElement? outputElement = BuildOutputElement(test);
            if (outputElement is not null)
            {
                unitTestResult.Add(outputElement);
            }

            resultsElement.Add(unitTestResult);

            if (seenTestDefinitionIds.Add(testId))
            {
                var unitTestDefinition = new XElement(
                    TrxNamespace + "UnitTest",
                    new XAttribute("name", SanitizeXmlString(fullName)),
                    new XAttribute("storage", SanitizeXmlString(normalizedAssemblyPath)),
                    new XAttribute("id", testId.ToString("D")),
                    new XElement(
                        TrxNamespace + "Execution",
                        new XAttribute("id", executionId.ToString("D"))),
                    new XElement(
                        TrxNamespace + "TestMethod",
                        new XAttribute("codeBase", SanitizeXmlString(normalizedAssemblyPath)),
                        new XAttribute("adapterTypeName", "executor://ricaun.RevitTest.TestAdapter"),
                        new XAttribute("className", SanitizeXmlString(resolvedClassName)),
                        new XAttribute("name", SanitizeXmlString(methodName))));
                testDefinitionsElement.Add(unitTestDefinition);
            }

            testEntriesElement.Add(new XElement(
                TrxNamespace + "TestEntry",
                new XAttribute("testId", testId.ToString("D")),
                new XAttribute("executionId", executionId.ToString("D")),
                new XAttribute("testListId", DefaultTestListId.ToString("D"))));
        }

        int total = passed + failed + skipped;
        int executed = passed + failed;
        string summaryOutcome = failed > 0 ? "Failed" : "Completed";

        string runName = $"{Environment.UserName}@{computerName} {runStart:yyyy-MM-dd HH:mm:ss}";
        string runUser = string.IsNullOrWhiteSpace(Environment.UserDomainName)
            ? Environment.UserName
            : $"{Environment.UserDomainName}\\{Environment.UserName}";

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                TrxNamespace + "TestRun",
                new XAttribute("id", Guid.NewGuid().ToString("D")),
                new XAttribute("name", SanitizeXmlString(runName)),
                new XAttribute("runUser", SanitizeXmlString(runUser)),
                new XElement(
                    TrxNamespace + "Times",
                    new XAttribute("creation", runStart.ToString("O", CultureInfo.InvariantCulture)),
                    new XAttribute("queuing", runStart.ToString("O", CultureInfo.InvariantCulture)),
                    new XAttribute("start", runStart.ToString("O", CultureInfo.InvariantCulture)),
                    new XAttribute("finish", runFinish.ToString("O", CultureInfo.InvariantCulture))),
                new XElement(
                    TrxNamespace + "TestSettings",
                    new XAttribute("name", "default"),
                    new XAttribute("id", Guid.NewGuid().ToString("D"))),
                resultsElement,
                testDefinitionsElement,
                new XElement(
                    TrxNamespace + "TestLists",
                    new XElement(
                        TrxNamespace + "TestList",
                        new XAttribute("name", "Results Not in a List"),
                        new XAttribute("id", DefaultTestListId.ToString("D")))),
                testEntriesElement,
                new XElement(
                    TrxNamespace + "ResultSummary",
                    new XAttribute("outcome", summaryOutcome),
                    new XElement(
                        TrxNamespace + "Counters",
                        new XAttribute("total", total),
                        new XAttribute("executed", executed),
                        new XAttribute("passed", passed),
                        new XAttribute("failed", failed),
                        new XAttribute("error", 0),
                        new XAttribute("timeout", 0),
                        new XAttribute("aborted", 0),
                        new XAttribute("inconclusive", 0),
                        new XAttribute("passedButRunAborted", 0),
                        new XAttribute("notRunnable", 0),
                        new XAttribute("notExecuted", skipped),
                        new XAttribute("disconnected", 0),
                        new XAttribute("warning", 0),
                        new XAttribute("completed", executed),
                        new XAttribute("inProgress", 0),
                        new XAttribute("pending", 0)))));

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true
        };
        using (XmlWriter writer = XmlWriter.Create(fullTrxPath, settings))
        {
            document.Save(writer);
        }

        return new TrxSummary(fullTrxPath, total, passed, failed, skipped);
    }

    internal static string FormatTrxDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        int totalHours = (int)duration.TotalHours;
        long subSecondTicks = duration.Ticks % TimeSpan.TicksPerSecond;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2}.{3:D7}",
            totalHours,
            duration.Minutes,
            duration.Seconds,
            subSecondTicks);
    }

    internal static List<(string ClassName, TestModel Test)> CollectTests(
        TestAssemblyModel? assemblyModel,
        IReadOnlyList<TestModel>? streamedTests)
    {
        var collected = new List<(string ClassName, TestModel Test)>();

        if (assemblyModel?.Tests is not null)
        {
            foreach (TestTypeModel typeModel in assemblyModel.Tests)
            {
                if (typeModel?.Tests is null)
                {
                    continue;
                }

                string className = !string.IsNullOrWhiteSpace(typeModel.FullName)
                    ? typeModel.FullName
                    : (typeModel.Name ?? string.Empty);

                foreach (TestModel test in typeModel.Tests)
                {
                    if (test is not null)
                    {
                        collected.Add((className, test));
                    }
                }
            }
        }

        if (collected.Count == 0 && streamedTests is not null)
        {
            foreach (TestModel test in streamedTests)
            {
                if (test is not null)
                {
                    collected.Add((ExtractClassName(test.FullName), test));
                }
            }
        }

        return collected;
    }

    private static XElement? BuildOutputElement(TestModel test)
    {
        bool hasConsole = !string.IsNullOrEmpty(test.Console);
        bool isFailure = !test.Success && !test.Skipped;

        if (!hasConsole && !isFailure)
        {
            return null;
        }

        var output = new XElement(TrxNamespace + "Output");
        if (hasConsole)
        {
            output.Add(new XElement(TrxNamespace + "StdOut", SanitizeXmlString(test.Console)));
        }

        if (isFailure)
        {
            (string errorMessage, string stackTrace) = SplitMessageAndStackTrace(test.Message);
            var errorInfo = new XElement(
                TrxNamespace + "ErrorInfo",
                new XElement(TrxNamespace + "Message", SanitizeXmlString(errorMessage)));

            if (!string.IsNullOrWhiteSpace(stackTrace))
            {
                errorInfo.Add(new XElement(TrxNamespace + "StackTrace", SanitizeXmlString(stackTrace)));
            }

            output.Add(errorInfo);
        }

        return output;
    }

    private static (string Message, string StackTrace) SplitMessageAndStackTrace(string? rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return ("Test failed.", string.Empty);
        }

        string[] markers = { "\r\n   at ", "\n   at ", "\r\nat ", "\nat ", "\r\n--- ", "\n--- " };
        int earliest = -1;
        foreach (string marker in markers)
        {
            int idx = rawMessage.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0 && (earliest < 0 || idx < earliest))
            {
                earliest = idx;
            }
        }

        if (earliest <= 0)
        {
            return (rawMessage.Trim(), string.Empty);
        }

        string msg = rawMessage[..earliest].Trim();
        string stack = rawMessage[earliest..].Trim();
        return (string.IsNullOrEmpty(msg) ? "Test failed." : msg, stack);
    }

    private static string ExtractClassName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return string.Empty;
        }

        string trimmed = fullName;
        int parenIdx = trimmed.IndexOf('(');
        if (parenIdx >= 0)
        {
            trimmed = trimmed[..parenIdx];
        }

        int lastDot = trimmed.LastIndexOf('.');
        return lastDot > 0 ? trimmed[..lastDot] : string.Empty;
    }

    private static string ExtractMethodName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "UnnamedTest";
        }

        string trimmed = fullName;
        int parenIdx = trimmed.IndexOf('(');
        string head = parenIdx >= 0 ? trimmed[..parenIdx] : trimmed;
        int lastDot = head.LastIndexOf('.');
        return lastDot >= 0 && lastDot + 1 < trimmed.Length
            ? trimmed[(lastDot + 1)..]
            : trimmed;
    }

    private static Guid CreateDeterministicGuid(string seed)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static string SanitizeXmlString(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            char ch = input[i];
            if (XmlConvert.IsXmlChar(ch))
            {
                sb.Append(ch);
            }
            else if (char.IsHighSurrogate(ch) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
            {
                sb.Append(ch);
                sb.Append(input[i + 1]);
                i++;
            }
        }

        return sb.ToString();
    }
}
