using System;
using System.IO;
using NUnit.Framework;
using RevitWorkerPool.Containment;

namespace RevitWorkerPool.Tests;

[TestFixture]
public class RicaunBundleLeaseTests
{
    private string _tempAppData = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempAppData = Path.Combine(
            Path.GetTempPath(),
            "RevitWorkerPool_BundleLeaseTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAppData);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempAppData))
        {
            Directory.Delete(_tempAppData, recursive: true);
        }
    }

    [Test]
    public void OverlappingLeases_PinBundleUntilFinalLeaseDisposes()
    {
        string? adapterDll = RicaunBundleLease.FindRicaunTestAdapterDll();
        Assert.That(adapterDll, Is.Not.Null.And.Exist, "ricaun.RevitTest.TestAdapter.dll must exist in local NuGet cache.");

        string mutexName = $@"Local\Synthetic_RicaunBundleLease_Test_{Guid.NewGuid():N}";

        RicaunBundleLease lease1 = RicaunBundleLease.Acquire(
            appDataDirectory: _tempAppData,
            testAdapterDllPath: adapterDll,
            mutexName: mutexName);

        Assert.That(File.Exists(lease1.PackageContentsPath), Is.True);
        Assert.That(Directory.Exists(lease1.BundleDirectory), Is.True);

        RicaunBundleLease lease2 = RicaunBundleLease.Acquire(
            appDataDirectory: _tempAppData,
            testAdapterDllPath: adapterDll,
            mutexName: mutexName);

        Assert.That(lease2.PackageContentsPath, Is.EqualTo(lease1.PackageContentsPath));

        // Disposing the first lease must keep PackageContents.xml pinned for the second lease.
        lease1.Dispose();
        Assert.That(File.Exists(lease2.PackageContentsPath), Is.True);
        Assert.That(Directory.Exists(lease2.BundleDirectory), Is.True);

        // Disposing the final lease removes PackageContents.xml and the bundle directory cleanly.
        lease2.Dispose();
        Assert.That(File.Exists(lease2.PackageContentsPath), Is.False);
        Assert.That(Directory.Exists(lease2.BundleDirectory), Is.False);
    }

    [Test]
    public void Dispose_WhenMutexTimesOut_StillClosesFileHandleWithoutThrowing()
    {
        string? adapterDll = RicaunBundleLease.FindRicaunTestAdapterDll();
        Assert.That(adapterDll, Is.Not.Null.And.Exist, "ricaun.RevitTest.TestAdapter.dll must exist in local NuGet cache.");

        string mutexName = $@"Local\Synthetic_RicaunBundleLease_TimeoutTest_{Guid.NewGuid():N}";
        RicaunBundleLease lease = RicaunBundleLease.Acquire(
            appDataDirectory: _tempAppData,
            testAdapterDllPath: adapterDll,
            mutexName: mutexName,
            mutexTimeoutMs: 50);

        Assert.That(File.Exists(lease.PackageContentsPath), Is.True);

        using var mutexHeld = new System.Threading.ManualResetEventSlim(false);
        using var releaseMutex = new System.Threading.ManualResetEventSlim(false);
        var holderTask = System.Threading.Tasks.Task.Run(() =>
        {
            using var mutex = new System.Threading.Mutex(initiallyOwned: false, mutexName);
            bool acquired = false;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
                Assert.That(acquired, Is.True);
                mutexHeld.Set();
                releaseMutex.Wait(TimeSpan.FromSeconds(10));
            }
            finally
            {
                if (acquired)
                {
                    mutex.ReleaseMutex();
                }
            }
        });

        try
        {
            Assert.That(mutexHeld.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.DoesNotThrow(() => lease.Dispose());
        }
        finally
        {
            releaseMutex.Set();
            holderTask.GetAwaiter().GetResult();
        }

        // File handle was closed by Dispose() despite mutex timeout, so PackageContents.xml is unlocked.
        Assert.DoesNotThrow(() => File.Delete(lease.PackageContentsPath));
        Assert.That(File.Exists(lease.PackageContentsPath), Is.False);
    }

    [Test]
    public void FindRicaunTestAdapterDll_PrioritizesNet10AboveNet8Net6AndNet45()
    {
        string nugetRoot = Path.Combine(_tempAppData, "nuget");
        string libRoot = Path.Combine(nugetRoot, "ricaun.revittest.testadapter", "1.12.0", "lib");
        string[] tfms = { "net45", "net6.0", "net8.0", "net10.0" };
        var dllByTfm = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string tfm in tfms)
        {
            string tfmDir = Path.Combine(libRoot, tfm);
            Directory.CreateDirectory(tfmDir);
            string dllPath = Path.Combine(tfmDir, "ricaun.RevitTest.TestAdapter.dll");
            File.WriteAllBytes(dllPath, Array.Empty<byte>());
            dllByTfm[tfm] = dllPath;
        }

        Assert.That(
            RicaunBundleLease.FindRicaunTestAdapterDll(nugetPackagesDirectory: nugetRoot),
            Is.EqualTo(dllByTfm["net10.0"]));

        File.Delete(dllByTfm["net10.0"]);
        Assert.That(
            RicaunBundleLease.FindRicaunTestAdapterDll(nugetPackagesDirectory: nugetRoot),
            Is.EqualTo(dllByTfm["net8.0"]));

        File.Delete(dllByTfm["net8.0"]);
        Assert.That(
            RicaunBundleLease.FindRicaunTestAdapterDll(nugetPackagesDirectory: nugetRoot),
            Is.EqualTo(dllByTfm["net6.0"]));

        File.Delete(dllByTfm["net6.0"]);
        Assert.That(
            RicaunBundleLease.FindRicaunTestAdapterDll(nugetPackagesDirectory: nugetRoot),
            Is.EqualTo(dllByTfm["net45"]));
    }
}
