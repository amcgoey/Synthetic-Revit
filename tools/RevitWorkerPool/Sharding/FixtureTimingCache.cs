using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using RevitWorkerPool.Discovery;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Sharding;

/// <summary>
/// Persists rolling per-fixture test execution durations (in milliseconds) per Revit version
/// to <c>%LOCALAPPDATA%\Synthetic\WorkerPool\fixture_timings_&lt;Year&gt;.json</c> using
/// Exponential Moving Average (EMA) updates and atomic temp-file replacement.
/// </summary>
public sealed class FixtureTimingCache
{
    public const double DefaultEmaAlpha = 0.6;
    private const int MutexTimeoutMs = 15000;
    private const int IoRetryCount = 5;

    private readonly double _emaAlpha;
    private readonly Action<string>? _diagnosticLogger;

    public FixtureTimingCache(
        string? cacheRootDirectory = null,
        double emaAlpha = DefaultEmaAlpha,
        Action<string>? diagnosticLogger = null)
    {
        if (!double.IsFinite(emaAlpha) || emaAlpha <= 0.0 || emaAlpha > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(emaAlpha),
                emaAlpha,
                "EMA alpha must be finite and within (0.0, 1.0].");
        }

        CacheRootDirectory = ResolveRootDirectory(cacheRootDirectory);
        _emaAlpha = emaAlpha;
        _diagnosticLogger = diagnosticLogger;
    }

    public string CacheRootDirectory { get; }

    public string GetCacheFilePath(int revitVersion)
    {
        return Path.Combine(CacheRootDirectory, $"fixture_timings_{revitVersion}.json");
    }

    /// <summary>
    /// Loads historical per-fixture durations (ms) for <paramref name="revitVersion"/>.
    /// Returns an empty dictionary when no cache exists yet or the cache file cannot be parsed.
    /// </summary>
    public IReadOnlyDictionary<string, double> LoadFixtureDurations(int revitVersion)
    {
        string filePath = GetCacheFilePath(revitVersion);
        return WithFileMutex(filePath, () =>
        {
            FixtureTimingCacheDocument doc = ReadDocumentUnlocked(filePath, revitVersion);
            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach ((string fixtureName, FixtureTimingEntry? entry) in doc.Fixtures)
            {
                if (string.IsNullOrWhiteSpace(fixtureName) || entry is null)
                {
                    continue;
                }

                if (double.IsFinite(entry.EmaMs) && entry.EmaMs >= 0.0)
                {
                    result[fixtureName] = entry.EmaMs;
                }
            }

            return (IReadOnlyDictionary<string, double>)result;
        });
    }

    /// <summary>
    /// Loads the full structured cache snapshot for <paramref name="revitVersion"/>.
    /// </summary>
    public FixtureTimingCacheDocument LoadDocument(int revitVersion)
    {
        string filePath = GetCacheFilePath(revitVersion);
        return WithFileMutex(filePath, () => ReadDocumentUnlocked(filePath, revitVersion));
    }

    /// <summary>
    /// Updates cached fixture durations using EMA (<c>alpha * observed + (1 - alpha) * previous</c>,
    /// or <c>observed</c> on first observation) and atomically persists the updated cache file.
    /// </summary>
    public IReadOnlyDictionary<string, double> RecordObservedDurations(
        int revitVersion,
        IReadOnlyDictionary<string, double> observedFixtureDurationsMs)
    {
        ArgumentNullException.ThrowIfNull(observedFixtureDurationsMs);

        string filePath = GetCacheFilePath(revitVersion);
        return WithFileMutex(filePath, () =>
        {
            FixtureTimingCacheDocument doc = ReadDocumentUnlocked(filePath, revitVersion);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            bool anyUpdated = false;
            foreach ((string rawName, double rawObservedMs) in observedFixtureDurationsMs)
            {
                if (string.IsNullOrWhiteSpace(rawName) || !double.IsFinite(rawObservedMs))
                {
                    continue;
                }

                string fixtureName = rawName.Trim();
                double observedMs = Math.Max(0.0, rawObservedMs);

                if (doc.Fixtures.TryGetValue(fixtureName, out FixtureTimingEntry? existing) &&
                    existing is not null &&
                    double.IsFinite(existing.EmaMs) &&
                    existing.EmaMs >= 0.0)
                {
                    double updatedEma = (_emaAlpha * observedMs) + ((1.0 - _emaAlpha) * existing.EmaMs);
                    doc.Fixtures[fixtureName] = new FixtureTimingEntry
                    {
                        EmaMs = updatedEma,
                        LastObservedMs = observedMs,
                        SampleCount = existing.SampleCount + 1,
                        LastUpdatedUtc = now
                    };
                }
                else
                {
                    doc.Fixtures[fixtureName] = new FixtureTimingEntry
                    {
                        EmaMs = observedMs,
                        LastObservedMs = observedMs,
                        SampleCount = 1,
                        LastUpdatedUtc = now
                    };
                }

                anyUpdated = true;
            }

            if (anyUpdated)
            {
                doc.RevitVersion = revitVersion;
                doc.UpdatedUtc = now;
                WriteDocumentAtomicallyUnlocked(filePath, doc);
            }

            var updatedMap = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach ((string fixtureName, FixtureTimingEntry entry) in doc.Fixtures)
            {
                if (double.IsFinite(entry.EmaMs) && entry.EmaMs >= 0.0)
                {
                    updatedMap[fixtureName] = entry.EmaMs;
                }
            }

            return (IReadOnlyDictionary<string, double>)updatedMap;
        });
    }

    /// <summary>
    /// Extracts observed per-fixture durations in milliseconds from a completed shard's
    /// <see cref="TestAssemblyModel"/> (falling back to <paramref name="streamedTests"/> when absent).
    /// Note that <c>ricaun.NUnit.Models.TestModel.Time</c> is already in milliseconds.
    /// </summary>
    public static IReadOnlyDictionary<string, double> ExtractFixtureDurationsMs(
        TestAssemblyModel? assemblyModel,
        IReadOnlyList<TestModel>? streamedTests = null,
        IReadOnlyList<DiscoveredFixtureTests>? shardFixtures = null)
    {
        var totals = new Dictionary<string, double>(StringComparer.Ordinal);

        if (assemblyModel?.Tests is not null)
        {
            foreach (TestTypeModel typeModel in assemblyModel.Tests)
            {
                if (typeModel?.Tests is null || typeModel.Tests.Count == 0)
                {
                    continue;
                }

                bool hasExecutedTest = false;
                double sumMs = 0.0;
                foreach (TestModel test in typeModel.Tests)
                {
                    if (test is null)
                    {
                        continue;
                    }

                    double ms = double.IsFinite(test.Time) && test.Time > 0.0 ? test.Time : 0.0;
                    if (!test.Skipped || ms > 0.0)
                    {
                        hasExecutedTest = true;
                    }

                    sumMs += ms;
                }

                if (!hasExecutedTest)
                {
                    continue;
                }

                string? fixtureName = ResolveFixtureNameFromTypeModel(typeModel, shardFixtures);
                if (string.IsNullOrWhiteSpace(fixtureName))
                {
                    continue;
                }

                totals[fixtureName] = totals.GetValueOrDefault(fixtureName) + sumMs;
            }
        }

        if (totals.Count == 0 && streamedTests is { Count: > 0 })
        {
            foreach (TestModel test in streamedTests)
            {
                if (test is null)
                {
                    continue;
                }

                double ms = double.IsFinite(test.Time) && test.Time > 0.0 ? test.Time : 0.0;
                if (test.Skipped && ms <= 0.0)
                {
                    continue;
                }

                string? fixtureName = ResolveFixtureNameFromTestModel(test, shardFixtures);
                if (string.IsNullOrWhiteSpace(fixtureName))
                {
                    continue;
                }

                totals[fixtureName] = totals.GetValueOrDefault(fixtureName) + ms;
            }
        }

        return totals;
    }

    /// <summary>
    /// Extracts durations across multiple completed shard results and updates the version's rolling cache.
    /// </summary>
    public IReadOnlyDictionary<string, double> UpdateFromCompletedShards(
        int revitVersion,
        IEnumerable<(TestAssemblyModel? AssemblyModel, IReadOnlyList<TestModel>? StreamedTests, IReadOnlyList<DiscoveredFixtureTests>? ShardFixtures)> completedShards)
    {
        ArgumentNullException.ThrowIfNull(completedShards);

        var combinedObserved = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach ((TestAssemblyModel? asm, IReadOnlyList<TestModel>? streamed, IReadOnlyList<DiscoveredFixtureTests>? fixtures) in completedShards)
        {
            IReadOnlyDictionary<string, double> shardDurations = ExtractFixtureDurationsMs(asm, streamed, fixtures);
            foreach ((string fixtureName, double ms) in shardDurations)
            {
                combinedObserved[fixtureName] = combinedObserved.GetValueOrDefault(fixtureName) + ms;
            }
        }

        if (combinedObserved.Count == 0)
        {
            return LoadFixtureDurations(revitVersion);
        }

        return RecordObservedDurations(revitVersion, combinedObserved);
    }

    private static string? ResolveFixtureNameFromTypeModel(
        TestTypeModel typeModel,
        IReadOnlyList<DiscoveredFixtureTests>? shardFixtures)
    {
        if (!string.IsNullOrWhiteSpace(typeModel.FullName))
        {
            return typeModel.FullName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(typeModel.Name))
        {
            string shortName = typeModel.Name.Trim();
            if (shardFixtures is not null)
            {
                DiscoveredFixtureTests? matched = shardFixtures.FirstOrDefault(f =>
                    string.Equals(f.FixtureFullName, shortName, StringComparison.Ordinal) ||
                    f.FixtureFullName.EndsWith("." + shortName, StringComparison.Ordinal) ||
                    f.FixtureFullName.EndsWith("+" + shortName, StringComparison.Ordinal));
                if (matched is not null)
                {
                    return matched.FixtureFullName;
                }
            }

            return shortName;
        }

        return null;
    }

    private static string? ResolveFixtureNameFromTestModel(
        TestModel test,
        IReadOnlyList<DiscoveredFixtureTests>? shardFixtures)
    {
        string? fullName = test.FullName;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        if (shardFixtures is { Count: > 0 })
        {
            string? bestFixture = null;
            int bestMatchLength = -1;

            foreach (DiscoveredFixtureTests fixture in shardFixtures)
            {
                foreach (string methodFullName in fixture.TestFullNames)
                {
                    if (IsTestInMethodScope(fullName, methodFullName) && methodFullName.Length > bestMatchLength)
                    {
                        bestMatchLength = methodFullName.Length;
                        bestFixture = fixture.FixtureFullName;
                    }
                }

                string prefix = fixture.FixtureFullName + ".";
                if (fullName.StartsWith(prefix, StringComparison.Ordinal) &&
                    fixture.FixtureFullName.Length > bestMatchLength)
                {
                    bestMatchLength = fixture.FixtureFullName.Length;
                    bestFixture = fixture.FixtureFullName;
                }
            }

            if (bestFixture is not null)
            {
                return bestFixture;
            }
        }

        int parenIdx = fullName.IndexOf('(');
        string head = parenIdx >= 0 ? fullName[..parenIdx] : fullName;
        int lastDot = head.LastIndexOf('.');
        return lastDot > 0 ? head[..lastDot] : null;
    }

    private static bool IsTestInMethodScope(string testFullName, string methodFullName)
    {
        if (string.Equals(testFullName, methodFullName, StringComparison.Ordinal))
        {
            return true;
        }

        if (!testFullName.StartsWith(methodFullName, StringComparison.Ordinal) ||
            testFullName.Length <= methodFullName.Length)
        {
            return false;
        }

        char next = testFullName[methodFullName.Length];
        return next is '.' or '(';
    }

    private FixtureTimingCacheDocument ReadDocumentUnlocked(string filePath, int revitVersion)
    {
        if (!File.Exists(filePath))
        {
            return new FixtureTimingCacheDocument { RevitVersion = revitVersion };
        }

        for (int attempt = 0; attempt < IoRetryCount; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string json = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new FixtureTimingCacheDocument { RevitVersion = revitVersion };
                }

                FixtureTimingCacheDocument? parsed =
                    JsonConvert.DeserializeObject<FixtureTimingCacheDocument>(json);
                if (parsed is null)
                {
                    return new FixtureTimingCacheDocument { RevitVersion = revitVersion };
                }

                parsed.Fixtures ??= new Dictionary<string, FixtureTimingEntry>(StringComparer.Ordinal);
                return new FixtureTimingCacheDocument
                {
                    RevitVersion = parsed.RevitVersion == 0 ? revitVersion : parsed.RevitVersion,
                    UpdatedUtc = parsed.UpdatedUtc,
                    Fixtures = new Dictionary<string, FixtureTimingEntry>(parsed.Fixtures, StringComparer.Ordinal)
                };
            }
            catch (FileNotFoundException)
            {
                return new FixtureTimingCacheDocument { RevitVersion = revitVersion };
            }
            catch (DirectoryNotFoundException)
            {
                return new FixtureTimingCacheDocument { RevitVersion = revitVersion };
            }
            catch (JsonException ex)
            {
                _diagnosticLogger?.Invoke(
                    $"[sharding] Ignoring malformed timing cache '{filePath}': {ex.Message}");
                return new FixtureTimingCacheDocument { RevitVersion = revitVersion };
            }
            catch (IOException ex) when (attempt < IoRetryCount - 1)
            {
                _diagnosticLogger?.Invoke(
                    $"[sharding] Retrying read of '{filePath}' after transient I/O error: {ex.Message}");
                Thread.Sleep(10 * (attempt + 1));
            }
        }

        return new FixtureTimingCacheDocument { RevitVersion = revitVersion };
    }

    private void WriteDocumentAtomicallyUnlocked(string filePath, FixtureTimingCacheDocument doc)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = Path.Combine(
            directory ?? CacheRootDirectory,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            string json = JsonConvert.SerializeObject(doc, Formatting.Indented);
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            for (int attempt = 0; attempt < IoRetryCount; attempt++)
            {
                try
                {
                    File.Move(tempPath, filePath, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < IoRetryCount - 1)
                {
                    Thread.Sleep(10 * (attempt + 1));
                }
                catch (UnauthorizedAccessException) when (attempt < IoRetryCount - 1)
                {
                    Thread.Sleep(10 * (attempt + 1));
                }
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static T WithFileMutex<T>(string filePath, Func<T> action)
    {
        string normalized = Path.GetFullPath(filePath).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        string mutexName = $@"Local\Synthetic_FixtureTimingCache_{Convert.ToHexString(hash.AsSpan(0, 8))}";

        using var mutex = new Mutex(initiallyOwned: false, mutexName);
        bool acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(MutexTimeoutMs);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException(
                    $"Timed out waiting for fixture timing cache mutex '{mutexName}'.");
            }

            return action();
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static string ResolveRootDirectory(string? cacheRootDirectory)
    {
        if (!string.IsNullOrWhiteSpace(cacheRootDirectory))
        {
            return Path.GetFullPath(cacheRootDirectory);
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Path.GetTempPath();
        }

        return Path.Combine(localAppData, "Synthetic", "WorkerPool");
    }
}

/// <summary>
/// Persisted JSON document for a single Revit version's rolling fixture durations.
/// </summary>
public sealed class FixtureTimingCacheDocument
{
    [JsonProperty("revitVersion")]
    public int RevitVersion { get; set; }

    [JsonProperty("updatedUtc")]
    public DateTimeOffset UpdatedUtc { get; set; }

    [JsonProperty("fixtures")]
    public Dictionary<string, FixtureTimingEntry> Fixtures { get; set; } =
        new(StringComparer.Ordinal);
}

/// <summary>
/// Rolling EMA timing record for a single test fixture.
/// </summary>
public sealed class FixtureTimingEntry
{
    [JsonProperty("emaMs")]
    public double EmaMs { get; set; }

    [JsonProperty("lastObservedMs")]
    public double LastObservedMs { get; set; }

    [JsonProperty("sampleCount")]
    public int SampleCount { get; set; }

    [JsonProperty("lastUpdatedUtc")]
    public DateTimeOffset LastUpdatedUtc { get; set; }
}
