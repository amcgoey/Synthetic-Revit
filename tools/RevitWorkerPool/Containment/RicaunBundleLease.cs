using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace RevitWorkerPool.Containment;

/// <summary>
/// Coordinates concurrent worker access to
/// <c>%APPDATA%\Autodesk\ApplicationPlugins\ricaun.RevitTest.Application.bundle</c>
/// via the shared Win32 mutex <c>Local\Synthetic_RicaunBundleLease</c> and a
/// shared-read file handle (without <see cref="FileShare.Delete"/>) on <c>PackageContents.xml</c>.
/// </summary>
public sealed class RicaunBundleLease : IDisposable
{
    public const string DefaultMutexName = @"Local\Synthetic_RicaunBundleLease";
    public const string BundleFolderName = "ricaun.RevitTest.Application.bundle";
    private const int DefaultMutexTimeoutMs = 30000;

    private readonly string _mutexName;
    private readonly int _mutexTimeoutMs;
    private FileStream? _leaseStream;
    private bool _disposed;

    private RicaunBundleLease(
        string bundleDirectory,
        string packageContentsPath,
        string mutexName,
        int mutexTimeoutMs,
        FileStream leaseStream)
    {
        BundleDirectory = bundleDirectory;
        PackageContentsPath = packageContentsPath;
        _mutexName = mutexName;
        _mutexTimeoutMs = mutexTimeoutMs;
        _leaseStream = leaseStream;
    }

    public string BundleDirectory { get; }

    public string PackageContentsPath { get; }

    /// <summary>
    /// Seeds <c>ricaun.RevitTest.Application.bundle</c> if absent and acquires a shared-read
    /// (no delete) lease handle on <c>PackageContents.xml</c>.
    /// </summary>
    public static RicaunBundleLease Acquire(
        string? appDataDirectory = null,
        IEnumerable<string>? testAdapterSearchDirectories = null,
        string? testAdapterDllPath = null,
        string mutexName = DefaultMutexName,
        string? nugetPackagesDirectory = null,
        int mutexTimeoutMs = DefaultMutexTimeoutMs)
    {
        string resolvedAppData = !string.IsNullOrWhiteSpace(appDataDirectory)
            ? appDataDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        string bundleDir = Path.Combine(
            resolvedAppData,
            "Autodesk",
            "ApplicationPlugins",
            BundleFolderName);
        string packageContentsPath = Path.Combine(bundleDir, "PackageContents.xml");

        using var mutex = new Mutex(initiallyOwned: false, mutexName);
        AcquireMutex(mutex, mutexName, mutexTimeoutMs);
        try
        {
            if (!File.Exists(packageContentsPath))
            {
                string? adapterDll = !string.IsNullOrWhiteSpace(testAdapterDllPath)
                    ? testAdapterDllPath
                    : FindRicaunTestAdapterDll(testAdapterSearchDirectories, nugetPackagesDirectory);

                if (string.IsNullOrWhiteSpace(adapterDll))
                {
                    throw new FileNotFoundException(
                        "Could not locate ricaun.RevitTest.TestAdapter.dll in search directories or NuGet cache.");
                }

                SeedBundleFromTestAdapter(adapterDll, bundleDir);
            }

            // CRITICAL: FileShare.ReadWrite allows concurrent readers/writers while excluding
            // FileShare.Delete so no sibling can delete PackageContents.xml mid-boot.
            var stream = new FileStream(
                packageContentsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            return new RicaunBundleLease(bundleDir, packageContentsPath, mutexName, mutexTimeoutMs, stream);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Locates <c>ricaun.RevitTest.TestAdapter.dll</c> in explicit directories or the local NuGet cache.
    /// </summary>
    public static string? FindRicaunTestAdapterDll(
        IEnumerable<string>? searchDirectories = null,
        string? nugetPackagesDirectory = null)
    {
        if (searchDirectories is not null)
        {
            foreach (string dir in searchDirectories)
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                string candidate = Path.Combine(dir, "ricaun.RevitTest.TestAdapter.dll");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        string resolvedNugetRoot = !string.IsNullOrWhiteSpace(nugetPackagesDirectory)
            ? nugetPackagesDirectory
            : Environment.GetEnvironmentVariable("NUGET_PACKAGES")
              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        string pkgDir = Path.Combine(resolvedNugetRoot, "ricaun.revittest.testadapter");
        if (!Directory.Exists(pkgDir))
        {
            return null;
        }

        var tfmPriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["net8.0"] = 3,
            ["net6.0"] = 2,
            ["net45"] = 1
        };

        var candidates = new List<(int Priority, string Root, string Path)>();
        foreach (string dllPath in Directory.EnumerateFiles(pkgDir, "ricaun.RevitTest.TestAdapter.dll", SearchOption.AllDirectories))
        {
            string parentDir = Path.GetDirectoryName(dllPath) ?? string.Empty;
            string tfm = Path.GetFileName(parentDir);
            int priority = tfmPriority.TryGetValue(tfm, out int p) ? p : 0;
            candidates.Add((priority, parentDir, dllPath));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderByDescending(c => c.Priority)
            .ThenByDescending(c => c.Root, StringComparer.OrdinalIgnoreCase)
            .First()
            .Path;
    }

    /// <summary>
    /// Extracts the nested <c>ricaun.RevitTest.Application.bundle</c> archive embedded in
    /// <c>ricaun.RevitTest.TestAdapter.dll</c> into <paramref name="bundleDirectory"/>.
    /// </summary>
    public static string SeedBundleFromTestAdapter(string testAdapterDllPath, string bundleDirectory)
    {
        if (string.IsNullOrWhiteSpace(testAdapterDllPath) || !File.Exists(testAdapterDllPath))
        {
            throw new FileNotFoundException(
                $"Could not locate ricaun.RevitTest.TestAdapter.dll: '{testAdapterDllPath}'.",
                testAdapterDllPath);
        }

        byte[] adapterBytes = File.ReadAllBytes(testAdapterDllPath);
        using ZipArchive outerZip = ExtractTrailingZip(adapterBytes)
            ?? throw new InvalidDataException($"Embedded ricaun.RevitTest.Console archive not found in '{testAdapterDllPath}'.");

        byte[]? consoleBytes = null;
        foreach (string entryName in new[] { "ricaun.RevitTest.Console.dll", "ricaun.RevitTest.Console.exe" })
        {
            ZipArchiveEntry? entry = outerZip.GetEntry(entryName);
            if (entry is null)
            {
                continue;
            }

            using Stream entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            consoleBytes = ms.ToArray();
            break;
        }

        if (consoleBytes is null)
        {
            throw new InvalidDataException(
                $"Embedded ricaun.RevitTest.Console(.dll/.exe) not found in '{testAdapterDllPath}'.");
        }

        using ZipArchive bundleZip = ExtractTrailingZip(consoleBytes)
            ?? throw new InvalidDataException(
                $"Embedded bundle archive not found inside ricaun.RevitTest.Console from '{testAdapterDllPath}'.");

        string bundleRootAbs = Path.GetFullPath(bundleDirectory);
        string bundleRootWithSep = bundleRootAbs.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(bundleRootAbs);

        string prefix = $"{BundleFolderName}/";
        int extracted = 0;

        foreach (ZipArchiveEntry member in bundleZip.Entries)
        {
            if (string.IsNullOrEmpty(member.Name))
            {
                continue;
            }

            string normalizedName = member.FullName.Replace('\\', '/');
            if (!normalizedName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string relPath = normalizedName[prefix.Length..].TrimStart('/');
            if (string.IsNullOrEmpty(relPath))
            {
                continue;
            }

            string destPath = Path.GetFullPath(Path.Combine(bundleRootAbs, relPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!destPath.StartsWith(bundleRootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            using Stream src = member.Open();
            using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            src.CopyTo(dst);
            extracted++;
        }

        string pkgXml = Path.Combine(bundleRootAbs, "PackageContents.xml");
        if (extracted == 0 || !File.Exists(pkgXml))
        {
            throw new InvalidDataException($"Failed to seed '{pkgXml}' from '{testAdapterDllPath}'.");
        }

        return pkgXml;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        bool mutexAcquired = false;
        Mutex? mutex = null;
        try
        {
            try
            {
                mutex = new Mutex(initiallyOwned: false, _mutexName);
                try
                {
                    mutexAcquired = mutex.WaitOne(_mutexTimeoutMs);
                }
                catch (AbandonedMutexException)
                {
                    // Previous holder exited unexpectedly; ownership is now granted to this thread.
                    mutexAcquired = true;
                }
                catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
                {
                    mutexAcquired = false;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
            {
                mutexAcquired = false;
            }

            _leaseStream?.Dispose();
            _leaseStream = null;

            if (!mutexAcquired)
            {
                return;
            }

            if (File.Exists(PackageContentsPath))
            {
                try
                {
                    File.Delete(PackageContentsPath);
                }
                catch (UnauthorizedAccessException)
                {
                    // Another active worker still holds a shared-read lease without FILE_SHARE_DELETE.
                    return;
                }
                catch (IOException)
                {
                    // Sharing violation from another active lease holder.
                    return;
                }
            }

            if (Directory.Exists(BundleDirectory))
            {
                try
                {
                    Directory.Delete(BundleDirectory, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort directory cleanup.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best-effort directory cleanup.
                }
            }
        }
        finally
        {
            _leaseStream?.Dispose();
            _leaseStream = null;

            if (mutexAcquired && mutex is not null)
            {
                mutex.ReleaseMutex();
            }

            mutex?.Dispose();
        }
    }

    private static void AcquireMutex(Mutex mutex, string mutexName, int timeoutMs)
    {
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(timeoutMs);
        }
        catch (AbandonedMutexException)
        {
            // Previous holder exited unexpectedly; ownership is now granted to this thread.
            acquired = true;
        }

        if (!acquired)
        {
            throw new TimeoutException($"Timed out waiting for bundle lease mutex '{mutexName}'.");
        }
    }

    private static ZipArchive? ExtractTrailingZip(byte[] data)
    {
        if (data.Length < 22)
        {
            return null;
        }

        int searchEnd = data.Length - 22;
        for (int i = searchEnd; i >= 0; i--)
        {
            if (data[i] != (byte)'P' || data[i + 1] != (byte)'K' || data[i + 2] != 0x05 || data[i + 3] != 0x06)
            {
                continue;
            }

            uint cdirSize = BitConverter.ToUInt32(data, i + 12);
            uint cdirRelOffset = BitConverter.ToUInt32(data, i + 16);
            ushort commentLen = BitConverter.ToUInt16(data, i + 20);

            long end = i + 22L + commentLen;
            long start = (i - (long)cdirSize) - cdirRelOffset;

            if (start < 0 || start >= i || end > data.Length)
            {
                continue;
            }

            int startIdx = (int)start;
            if (data[startIdx] != (byte)'P' ||
                data[startIdx + 1] != (byte)'K' ||
                data[startIdx + 2] != 0x03 ||
                data[startIdx + 3] != 0x04)
            {
                continue;
            }

            try
            {
                int length = (int)(end - start);
                var ms = new MemoryStream(data, startIdx, length, writable: false);
                return new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
            }
            catch (InvalidDataException)
            {
                // Keep scanning backwards in case of a false signature match.
            }
        }

        return null;
    }
}
