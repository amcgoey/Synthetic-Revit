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
}
