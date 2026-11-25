using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using RevitWorkerPool.Reporting;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class TrxWriterTests
{
    private string _tempRoot = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "RevitWorkerPool_TrxTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Test]
    public void Write_EmitsValidTrxSchema_AndConvertsTestModelTimeFromMilliseconds()
    {
        string trxPath = Path.Combine(_tempRoot, "nested", "results_2026.trx");
        string assemblyPath = Path.Combine(_tempRoot, "Synthetic.RevitTests.2026.dll");

        var model = new TestAssemblyModel
        {
            FileName = assemblyPath,
            Name = "Synthetic.RevitTests.2026",
            Success = false,
            Tests = new List<TestTypeModel>
            {
                new()
                {
                    Name = "SampleFixture",
                    FullName = "SyntheticTests.SampleFixture",
                    Success = false,
                    Tests = new List<TestModel>
                    {
                        new()
                        {
                            Name = "PassedTest",
                            FullName = "SyntheticTests.SampleFixture.PassedTest",
                            Success = true,
                            Time = 1500.0,
                            Console = "line 1"
                        },
                        new()
                        {
                            Name = "FailedTest",
                            FullName = "SyntheticTests.SampleFixture.FailedTest",
                            Success = false,
                            Time = 250.0,
                            Message = "Expected: True\r\n   at SyntheticTests.SampleFixture.FailedTest() in Sample.cs:line 42"
                        },
                        new()
                        {
                            Name = "SkippedTest",
                            FullName = "SyntheticTests.SampleFixture.SkippedTest",
                            Success = false,
                            Skipped = true,
                            Time = 0.0
                        }
                    }
                }
            }
        };

        TrxSummary summary = TrxWriter.Write(trxPath, assemblyPath, model);

        Assert.That(summary.TotalTests, Is.EqualTo(3));
        Assert.That(summary.PassedTests, Is.EqualTo(1));
        Assert.That(summary.FailedTests, Is.EqualTo(1));
        Assert.That(summary.SkippedTests, Is.EqualTo(1));
        Assert.That(File.Exists(trxPath), Is.True);

        XDocument doc = XDocument.Load(trxPath);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

        XElement counters = doc.Descendants(ns + "Counters").Single();
        Assert.That((string?)counters.Attribute("total"), Is.EqualTo("3"));
        Assert.That((string?)counters.Attribute("executed"), Is.EqualTo("2"));
        Assert.That((string?)counters.Attribute("passed"), Is.EqualTo("1"));
        Assert.That((string?)counters.Attribute("failed"), Is.EqualTo("1"));
        Assert.That((string?)counters.Attribute("notExecuted"), Is.EqualTo("1"));

        var results = doc.Descendants(ns + "UnitTestResult").ToList();
        Assert.That(results.Count, Is.EqualTo(3));

        XElement passedEl = results.Single(r => (string?)r.Attribute("testName") == "SyntheticTests.SampleFixture.PassedTest");
        Assert.That((string?)passedEl.Attribute("outcome"), Is.EqualTo("Passed"));
        Assert.That((string?)passedEl.Attribute("duration"), Is.EqualTo("00:00:01.5000000"));
        Assert.That((string?)passedEl.Descendants(ns + "StdOut").Single(), Is.EqualTo("line 1"));

        XElement failedEl = results.Single(r => (string?)r.Attribute("testName") == "SyntheticTests.SampleFixture.FailedTest");
        Assert.That((string?)failedEl.Attribute("outcome"), Is.EqualTo("Failed"));
        Assert.That((string?)failedEl.Attribute("duration"), Is.EqualTo("00:00:00.2500000"));
        Assert.That((string?)failedEl.Descendants(ns + "Message").Single(), Is.EqualTo("Expected: True"));
        Assert.That((string?)failedEl.Descendants(ns + "StackTrace").Single(), Does.Contain("SampleFixture.FailedTest()"));

        XElement skippedEl = results.Single(r => (string?)r.Attribute("testName") == "SyntheticTests.SampleFixture.SkippedTest");
        Assert.That((string?)skippedEl.Attribute("outcome"), Is.EqualTo("NotExecuted"));

        Assert.That(doc.Descendants(ns + "TestDefinitions").Descendants(ns + "UnitTest").Count(), Is.EqualTo(3));
        Assert.That(doc.Descendants(ns + "TestEntries").Descendants(ns + "TestEntry").Count(), Is.EqualTo(3));
    }
}
