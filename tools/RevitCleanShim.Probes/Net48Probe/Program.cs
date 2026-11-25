using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace RevitCleanShim.Probes.Net48Probe
{
    internal static class Program
    {
        private const uint InvalidFileAttributes = 0xFFFFFFFF;
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        public static int Main(string[] args)
        {
            ProbeArgs parsed = ParseArgs(args);
            ProbeReport report = ExecuteProbe(parsed);

            string json = SerializeJson(report);
            if (!string.IsNullOrWhiteSpace(parsed.OutputPath))
            {
                string? outDir = Path.GetDirectoryName(Path.GetFullPath(parsed.OutputPath));
                if (!string.IsNullOrEmpty(outDir))
                {
                    Directory.CreateDirectory(outDir);
                }

                File.WriteAllText(parsed.OutputPath, json, Encoding.UTF8);
            }

            Console.WriteLine(json);
            return 0;
        }

        private static ProbeReport ExecuteProbe(ProbeArgs args)
        {
            string year = args.Year ?? Environment.GetEnvironmentVariable("SYNTHETIC_CLEAN_SHIM_REVIT_YEAR") ?? "2024";
            string appData = args.AppDataRoot
                ?? Environment.GetEnvironmentVariable("SYNTHETIC_CLEAN_SHIM_APPDATA_ROOT")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string programData = args.ProgramDataRoot
                ?? Environment.GetEnvironmentVariable("SYNTHETIC_CLEAN_SHIM_PROGRAMDATA_ROOT")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string programFiles = args.ProgramFilesRoot
                ?? Environment.GetEnvironmentVariable("SYNTHETIC_CLEAN_SHIM_PROGRAMFILES_ROOT")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            string appDataAddinsYearDir = Path.Combine(appData, "Autodesk", "Revit", "Addins", year);
            string programDataAddinsYearDir = Path.Combine(programData, "Autodesk", "Revit", "Addins", year);
            string appDataPluginsDir = Path.Combine(appData, "Autodesk", "ApplicationPlugins");
            string programDataPluginsDir = Path.Combine(programData, "Autodesk", "ApplicationPlugins");
            string internalAddinsDir = Path.Combine(programFiles, "Autodesk", $"Revit {year}", "AddIns");
            string eTransmitDbPath = Path.Combine(programFiles, "Autodesk", $"eTransmit for Revit {year}", "eTransmitForRevitDB.dll");

            string appDataSyntheticAddin = Path.Combine(appDataAddinsYearDir, $"Synthetic{year}.addin");
            string appDataThirdPartyAddin = Path.Combine(appDataAddinsYearDir, "Kinship.addin");
            string programDataSyntheticAddin = Path.Combine(programDataAddinsYearDir, $"Synthetic{year}.addin");
            string programDataThirdPartyAddin = Path.Combine(programDataAddinsYearDir, "CTC-BIM-Project-Suite.addin");
            string appDataRicaunPackage = Path.Combine(appDataPluginsDir, "ricaun.RevitTest.Application.bundle", "PackageContents.xml");
            string appDataBluebeamDir = Path.Combine(appDataPluginsDir, "Bluebeam Software");
            string appDataBluebeamManifest = Path.Combine(appDataBluebeamDir, "PackageContents.xml");
            string programDataThirdPartyBundle = Path.Combine(programDataPluginsDir, "ThirdParty.bundle");
            string programDataThirdPartyBundleManifest = Path.Combine(programDataThirdPartyBundle, "PackageContents.xml");

            string[] loadedModules = CollectLoadedModules();
            bool hasShim = loadedModules.Any(m => string.Equals(m, "RevitCleanShim.dll", StringComparison.OrdinalIgnoreCase));

            var report = new ProbeReport
            {
                ProcessId = Process.GetCurrentProcess().Id,
                Year = year,
                LoadedModules = loadedModules,
                HasRevitCleanShim = hasShim,
                AppDataAddinsDirExists = Directory.Exists(appDataAddinsYearDir),
                ProgramDataAddinsDirExists = Directory.Exists(programDataAddinsYearDir),
                AppDataPluginsDirExists = Directory.Exists(appDataPluginsDir),
                ProgramDataPluginsDirExists = Directory.Exists(programDataPluginsDir),
                AppDataAddinFiles = SafeGetFiles(appDataAddinsYearDir, "*.addin"),
                AppDataAddinAllEntries = SafeGetFileSystemEntries(appDataAddinsYearDir),
                ProgramDataAddinFiles = SafeGetFiles(programDataAddinsYearDir, "*.addin"),
                ProgramDataAddinBundleDirs = SafeGetDirectories(programDataAddinsYearDir, "*.bundle"),
                ProgramDataAddinAllEntries = SafeGetFileSystemEntries(programDataAddinsYearDir),
                AppDataBundleDirs = SafeGetDirectories(appDataPluginsDir, "*.bundle"),
                AppDataPluginAllDirs = SafeGetDirectories(appDataPluginsDir, "*"),
                ProgramDataBundleDirs = SafeGetDirectories(programDataPluginsDir, "*.bundle"),
                ProgramDataPluginAllDirs = SafeGetDirectories(programDataPluginsDir, "*"),
                InternalAddinFiles = SafeGetFiles(internalAddinsDir, "*.addin", SearchOption.AllDirectories),
                ETransmitDbExists = File.Exists(eTransmitDbPath),
                ETransmitDbText = TryReadAllText(eTransmitDbPath, out _),
                AppDataSyntheticAddinExists = File.Exists(appDataSyntheticAddin),
                AppDataSyntheticAddinText = TryReadAllText(appDataSyntheticAddin, out _),
                AppDataThirdPartyAddinExists = File.Exists(appDataThirdPartyAddin),
                AppDataThirdPartyAddinReadError = TryReadExpectError(appDataThirdPartyAddin),
                ProgramDataSyntheticAddinExists = File.Exists(programDataSyntheticAddin),
                ProgramDataSyntheticAddinReadError = TryReadExpectError(programDataSyntheticAddin),
                ProgramDataThirdPartyAddinExists = File.Exists(programDataThirdPartyAddin),
                ProgramDataThirdPartyAddinReadError = TryReadExpectError(programDataThirdPartyAddin),
                AppDataRicaunPackageExists = File.Exists(appDataRicaunPackage),
                AppDataRicaunPackageText = TryReadAllText(appDataRicaunPackage, out _),
                AppDataBluebeamDirExists = Directory.Exists(appDataBluebeamDir),
                AppDataBluebeamManifestExists = File.Exists(appDataBluebeamManifest),
                AppDataBluebeamReadError = TryReadExpectError(appDataBluebeamManifest),
                ProgramDataThirdPartyBundleExists = Directory.Exists(programDataThirdPartyBundle),
                ProgramDataThirdPartyBundleManifestExists = File.Exists(programDataThirdPartyBundleManifest),
                FindFirstFileExAppDataAddins = EnumerateViaFindFirstFileEx(Path.Combine(appDataAddinsYearDir, "*.addin")),
                GetFileAttributesProgramDataSyntheticHidden = NativeMethods.GetFileAttributesW(@"\\?\" + programDataSyntheticAddin.Replace('\\', '/')) == InvalidFileAttributes
            };

            if (args.SpawnGrandchild)
            {
                SpawnGrandchild(args, year, appData, programData, programFiles, report);
            }

            return report;
        }

        private static void SpawnGrandchild(
            ProbeArgs args,
            string year,
            string appData,
            string programData,
            string programFiles,
            ProbeReport report)
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Cannot resolve Net48Probe executable path.");

            string childArgs =
                $"--year \"{year}\" " +
                $"--appdata \"{appData}\" " +
                $"--programdata \"{programData}\" " +
                $"--programfiles \"{programFiles}\"";

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = childArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using Process? child = Process.Start(psi);
            if (child == null)
            {
                report.GrandchildError = "Process.Start returned null for grandchild Net48Probe.";
                return;
            }

            string stdout = child.StandardOutput.ReadToEnd();
            string stderr = child.StandardError.ReadToEnd();
            if (!child.WaitForExit(15000))
            {
                string? killError = null;
                try
                {
                    child.Kill();
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    killError = ex.Message;
                }

                report.GrandchildError = killError is null
                    ? "Grandchild Net48Probe timed out."
                    : $"Grandchild Net48Probe timed out (kill failed: {killError}).";
                return;
            }

            report.GrandchildExitCode = child.ExitCode;
            if (child.ExitCode != 0)
            {
                report.GrandchildError = $"Grandchild exited with {child.ExitCode}: {stderr}";
                return;
            }

            try
            {
                report.Grandchild = DeserializeJson(stdout.Trim());
            }
            catch (Exception ex)
            {
                report.GrandchildError = $"Failed to parse grandchild JSON ({ex.Message}): {stdout} {stderr}";
            }
        }

        private static string[] CollectLoadedModules()
        {
            var names = new List<string>();
            Process current = Process.GetCurrentProcess();
            foreach (ProcessModule module in current.Modules)
            {
                if (!string.IsNullOrWhiteSpace(module.ModuleName))
                {
                    names.Add(module.ModuleName);
                }
            }

            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string[] SafeGetFiles(string directory, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
        {
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(directory, pattern, option)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] SafeGetDirectories(string directory, string pattern)
        {
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            return Directory.GetDirectories(directory, pattern, SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] SafeGetFileSystemEntries(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFileSystemEntries(directory)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string? TryReadAllText(string path, out string? error)
        {
            try
            {
                error = null;
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        private static string? TryReadExpectError(string path)
        {
            _ = TryReadAllText(path, out string? error);
            return error;
        }

        private static string[] EnumerateViaFindFirstFileEx(string pattern)
        {
            var results = new List<string>();
            IntPtr handle = NativeMethods.FindFirstFileExW(
                pattern,
                NativeMethods.FindExInfoBasic,
                out NativeMethods.Win32FindDataW data,
                NativeMethods.FindExSearchNameMatch,
                IntPtr.Zero,
                0);

            if (handle == InvalidHandleValue)
            {
                return Array.Empty<string>();
            }

            try
            {
                do
                {
                    if (!string.IsNullOrEmpty(data.cFileName) &&
                        data.cFileName != "." &&
                        data.cFileName != "..")
                    {
                        results.Add(data.cFileName);
                    }
                }
                while (NativeMethods.FindNextFileW(handle, out data));
            }
            finally
            {
                NativeMethods.FindClose(handle);
            }

            return results.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string SerializeJson(ProbeReport report)
        {
            var serializer = new DataContractJsonSerializer(typeof(ProbeReport));
            using var ms = new MemoryStream();
            serializer.WriteObject(ms, report);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static ProbeReport DeserializeJson(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(ProbeReport));
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return (ProbeReport)(serializer.ReadObject(ms)
                ?? throw new InvalidOperationException("Deserialized ProbeReport was null."));
        }

        private static ProbeArgs ParseArgs(string[] args)
        {
            var parsed = new ProbeArgs();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--year" when i + 1 < args.Length:
                        parsed.Year = args[++i];
                        break;
                    case "--appdata" when i + 1 < args.Length:
                        parsed.AppDataRoot = args[++i];
                        break;
                    case "--programdata" when i + 1 < args.Length:
                        parsed.ProgramDataRoot = args[++i];
                        break;
                    case "--programfiles" when i + 1 < args.Length:
                        parsed.ProgramFilesRoot = args[++i];
                        break;
                    case "--out" when i + 1 < args.Length:
                        parsed.OutputPath = args[++i];
                        break;
                    case "--spawn-grandchild":
                        parsed.SpawnGrandchild = true;
                        break;
                }
            }

            return parsed;
        }

        private sealed class ProbeArgs
        {
            public string? Year;
            public string? AppDataRoot;
            public string? ProgramDataRoot;
            public string? ProgramFilesRoot;
            public string? OutputPath;
            public bool SpawnGrandchild;
        }

        private static class NativeMethods
        {
            public const int FindExInfoBasic = 1;
            public const int FindExSearchNameMatch = 0;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct FileTime
            {
                public uint dwLowDateTime;
                public uint dwHighDateTime;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct Win32FindDataW
            {
                public uint dwFileAttributes;
                public FileTime ftCreationTime;
                public FileTime ftLastAccessTime;
                public FileTime ftLastWriteTime;
                public uint nFileSizeHigh;
                public uint nFileSizeLow;
                public uint dwReserved0;
                public uint dwReserved1;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                public string cFileName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
                public string cAlternateFileName;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr FindFirstFileExW(
                string lpFileName,
                int fInfoLevelId,
                out Win32FindDataW lpFindFileData,
                int fSearchOp,
                IntPtr lpSearchFilter,
                uint dwAdditionalFlags);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool FindNextFileW(IntPtr hFindFile, out Win32FindDataW lpFindFileData);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool FindClose(IntPtr hFindFile);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern uint GetFileAttributesW(string lpFileName);
        }
    }

    [DataContract]
    internal sealed class ProbeReport
    {
        [DataMember(Name = "processId")]
        public int ProcessId { get; set; }

        [DataMember(Name = "year")]
        public string Year { get; set; } = string.Empty;

        [DataMember(Name = "loadedModules")]
        public string[] LoadedModules { get; set; } = Array.Empty<string>();

        [DataMember(Name = "hasRevitCleanShim")]
        public bool HasRevitCleanShim { get; set; }

        [DataMember(Name = "appDataAddinsDirExists")]
        public bool AppDataAddinsDirExists { get; set; }

        [DataMember(Name = "programDataAddinsDirExists")]
        public bool ProgramDataAddinsDirExists { get; set; }

        [DataMember(Name = "appDataPluginsDirExists")]
        public bool AppDataPluginsDirExists { get; set; }

        [DataMember(Name = "programDataPluginsDirExists")]
        public bool ProgramDataPluginsDirExists { get; set; }

        [DataMember(Name = "appDataAddinFiles")]
        public string[] AppDataAddinFiles { get; set; } = Array.Empty<string>();

        [DataMember(Name = "appDataAddinAllEntries")]
        public string[] AppDataAddinAllEntries { get; set; } = Array.Empty<string>();

        [DataMember(Name = "programDataAddinFiles")]
        public string[] ProgramDataAddinFiles { get; set; } = Array.Empty<string>();

        [DataMember(Name = "programDataAddinBundleDirs")]
        public string[] ProgramDataAddinBundleDirs { get; set; } = Array.Empty<string>();

        [DataMember(Name = "programDataAddinAllEntries")]
        public string[] ProgramDataAddinAllEntries { get; set; } = Array.Empty<string>();

        [DataMember(Name = "appDataBundleDirs")]
        public string[] AppDataBundleDirs { get; set; } = Array.Empty<string>();

        [DataMember(Name = "appDataPluginAllDirs")]
        public string[] AppDataPluginAllDirs { get; set; } = Array.Empty<string>();

        [DataMember(Name = "programDataBundleDirs")]
        public string[] ProgramDataBundleDirs { get; set; } = Array.Empty<string>();

        [DataMember(Name = "programDataPluginAllDirs")]
        public string[] ProgramDataPluginAllDirs { get; set; } = Array.Empty<string>();

        [DataMember(Name = "internalAddinFiles")]
        public string[] InternalAddinFiles { get; set; } = Array.Empty<string>();

        [DataMember(Name = "eTransmitDbExists")]
        public bool ETransmitDbExists { get; set; }

        [DataMember(Name = "eTransmitDbText")]
        public string? ETransmitDbText { get; set; }

        [DataMember(Name = "appDataSyntheticAddinExists")]
        public bool AppDataSyntheticAddinExists { get; set; }

        [DataMember(Name = "appDataSyntheticAddinText")]
        public string? AppDataSyntheticAddinText { get; set; }

        [DataMember(Name = "appDataThirdPartyAddinExists")]
        public bool AppDataThirdPartyAddinExists { get; set; }

        [DataMember(Name = "appDataThirdPartyAddinReadError")]
        public string? AppDataThirdPartyAddinReadError { get; set; }

        [DataMember(Name = "programDataSyntheticAddinExists")]
        public bool ProgramDataSyntheticAddinExists { get; set; }

        [DataMember(Name = "programDataSyntheticAddinReadError")]
        public string? ProgramDataSyntheticAddinReadError { get; set; }

        [DataMember(Name = "programDataThirdPartyAddinExists")]
        public bool ProgramDataThirdPartyAddinExists { get; set; }

        [DataMember(Name = "programDataThirdPartyAddinReadError")]
        public string? ProgramDataThirdPartyAddinReadError { get; set; }

        [DataMember(Name = "appDataRicaunPackageExists")]
        public bool AppDataRicaunPackageExists { get; set; }

        [DataMember(Name = "appDataRicaunPackageText")]
        public string? AppDataRicaunPackageText { get; set; }

        [DataMember(Name = "appDataBluebeamDirExists")]
        public bool AppDataBluebeamDirExists { get; set; }

        [DataMember(Name = "appDataBluebeamManifestExists")]
        public bool AppDataBluebeamManifestExists { get; set; }

        [DataMember(Name = "appDataBluebeamReadError")]
        public string? AppDataBluebeamReadError { get; set; }

        [DataMember(Name = "programDataThirdPartyBundleExists")]
        public bool ProgramDataThirdPartyBundleExists { get; set; }

        [DataMember(Name = "programDataThirdPartyBundleManifestExists")]
        public bool ProgramDataThirdPartyBundleManifestExists { get; set; }

        [DataMember(Name = "findFirstFileExAppDataAddins")]
        public string[] FindFirstFileExAppDataAddins { get; set; } = Array.Empty<string>();

        [DataMember(Name = "getFileAttributesProgramDataSyntheticHidden")]
        public bool GetFileAttributesProgramDataSyntheticHidden { get; set; }

        [DataMember(Name = "grandchildExitCode")]
        public int? GrandchildExitCode { get; set; }

        [DataMember(Name = "grandchildError")]
        public string? GrandchildError { get; set; }

        [DataMember(Name = "grandchild")]
        public ProbeReport? Grandchild { get; set; }
    }
}
