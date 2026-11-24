using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace RevitWorkerPool.Execution;

/// <summary>
/// Creates isolated per-shard staging directories for concurrent multi-shard runs so
/// <c>ricaun.RevitTest.Application</c>'s <c>%TEMP%\RevitTest\&lt;ParentDirName&gt;.zip</c>
/// archiving never collides across workers while preserving <c>&lt;RunId&gt;</c> prefixes.
/// </summary>
internal static class ShardStagingHelper
{
    public sealed record StagedShardDirectory(string StagingDirectory, string StagedAssemblyPath);

    /// <summary>
    /// Creates an isolated staging directory for <paramref name="shardIndex"/> at
    /// <c>&lt;AssemblyParent&gt;\&lt;AssemblyDirName&gt;_s&lt;shardIndex&gt;_&lt;shortGuid&gt;\</c>,
    /// hardlinking (or copying on fallback) all files from the assembly's directory tree.
    /// </summary>
    public static StagedShardDirectory CreateStagingDirectory(
        string fullAssemblyPath,
        int shardIndex,
        Action<string>? logger = null)
    {
        string normalizedAssemblyPath = Path.GetFullPath(fullAssemblyPath);
        string? sourceDir = Path.GetDirectoryName(normalizedAssemblyPath);
        string assemblyFileName = Path.GetFileName(normalizedAssemblyPath);

        if (string.IsNullOrWhiteSpace(sourceDir) || string.IsNullOrWhiteSpace(assemblyFileName))
        {
            throw new ArgumentException($"Invalid test assembly path '{fullAssemblyPath}'.", nameof(fullAssemblyPath));
        }

        string sourceDirName = Path.GetFileName(sourceDir);
        string? parentDir = Path.GetDirectoryName(sourceDir);
        if (string.IsNullOrWhiteSpace(parentDir) || string.IsNullOrWhiteSpace(sourceDirName))
        {
            parentDir = Path.GetTempPath();
            sourceDirName = Path.GetFileNameWithoutExtension(assemblyFileName);
        }

        string shortGuid = Guid.NewGuid().ToString("N")[..8];
        string stagingDirName = $"{sourceDirName}_s{shardIndex}_{shortGuid}";
        string stagingDir = Path.Combine(parentDir, stagingDirName);

        try
        {
            MirrorDirectoryWithHardlinks(
                sourceDir,
                stagingDir,
                skipSiblingPrefix: $"{sourceDirName}_s");

            string stagedAssemblyPath = Path.Combine(stagingDir, assemblyFileName);
            if (!File.Exists(stagedAssemblyPath))
            {
                throw new FileNotFoundException(
                    $"Staged test assembly was not created at '{stagedAssemblyPath}'.",
                    stagedAssemblyPath);
            }

            logger?.Invoke(
                $"[sharding] Staged shard {shardIndex} assembly at '{stagedAssemblyPath}'.");
            return new StagedShardDirectory(stagingDir, stagedAssemblyPath);
        }
        catch
        {
            CleanupStagingDirectories(new[] { stagingDir }, logger);
            throw;
        }
    }

    public static void CleanupStagingDirectories(
        IEnumerable<string> stagingDirectories,
        Action<string>? logger = null)
    {
        foreach (string dir in stagingDirectories)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                continue;
            }

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    break;
                }
                catch (DirectoryNotFoundException)
                {
                    break;
                }
                catch (IOException ex) when (attempt < 2)
                {
                    logger?.Invoke($"[sharding] Retrying staging cleanup for '{dir}': {ex.Message}");
                    Thread.Sleep(25);
                }
                catch (UnauthorizedAccessException ex) when (attempt < 2)
                {
                    logger?.Invoke($"[sharding] Retrying staging cleanup for '{dir}': {ex.Message}");
                    Thread.Sleep(25);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger?.Invoke($"[sharding] Warning: failed to remove staging directory '{dir}': {ex.Message}");
                    break;
                }
            }
        }
    }

    private static void MirrorDirectoryWithHardlinks(
        string sourceRoot,
        string destRoot,
        string skipSiblingPrefix)
    {
        Directory.CreateDirectory(destRoot);

        foreach (string filePath in Directory.EnumerateFiles(sourceRoot))
        {
            string fileName = Path.GetFileName(filePath);
            string destFile = Path.Combine(destRoot, fileName);
            LinkOrCopyFile(filePath, destFile);
        }

        foreach (string subDir in Directory.EnumerateDirectories(sourceRoot))
        {
            string dirName = Path.GetFileName(subDir);
            if (dirName.StartsWith(skipSiblingPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destSubDir = Path.Combine(destRoot, dirName);
            MirrorDirectoryWithHardlinks(subDir, destSubDir, skipSiblingPrefix);
        }
    }

    private static void LinkOrCopyFile(string sourceFile, string destFile)
    {
        if (OperatingSystem.IsWindows())
        {
            if (NativeMethods.CreateHardLinkW(destFile, sourceFile, IntPtr.Zero))
            {
                return;
            }
        }

        File.Copy(sourceFile, destFile, overwrite: true);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateHardLinkW(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes);
    }
}
