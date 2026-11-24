using System;
using System.Text;

namespace RevitWorkerPool.Ipc;

/// <summary>
/// Shared constants for local Worker Pool control IPC and singleton startup coordination.
/// </summary>
public static class SupervisorIpcConstants
{
    public const string DefaultPipeName = "Synthetic_RevitWorkerPool_Control";
    public const string SingletonMutexPrefix = @"Local\Synthetic_RevitWorkerPool_Singleton_";

    public const int DefaultIdleTtlMinutes = 25;
    public const int MinIdleTtlMinutes = 20;
    public const int MaxIdleTtlMinutes = 30;

    /// <summary>
    /// Builds the Win32 local mutex name used to serialize supervisor singleton startup
    /// per control pipe name.
    /// </summary>
    public static string BuildSingletonMutexName(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name must not be empty.", nameof(pipeName));
        }

        var sanitized = new StringBuilder(pipeName.Length);
        foreach (char ch in pipeName.Trim())
        {
            sanitized.Append(ch is '\\' or '/' or ' ' or ':' ? '_' : ch);
        }

        return SingletonMutexPrefix + sanitized;
    }
}
