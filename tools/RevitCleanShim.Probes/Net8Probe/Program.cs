using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace RevitCleanShim.Probes.Net8Probe
{
    internal static class Program
    {
        private const uint StatusSuccess = 0x00000000;
        private const uint StatusPending = 0x00000103;
        private const uint StatusNoMoreFiles = 0x80000006;
        private const uint StatusNoSuchFile = 0xC000000F;
        private const uint StatusObjectNameNotFound = 0xC0000034;

        private const uint ObjCaseInsensitive = 0x00000040;
        private const uint FileOpen = 0x00000001;
        private const uint FileSynchronousIoNonalert = 0x00000020;
        private const uint FileNonDirectoryFile = 0x00000040;
        private const uint FileDirectoryFile = 0x00000001;
        private const uint FileListDirectory = 0x00000001;
        private const uint FileReadData = 0x00000001;
        private const uint FileReadAttributes = 0x00000080;
        private const uint Synchronize = 0x00100000;
        private const uint FileShareAll = 0x00000007;

        private const uint SlRestartScan = 0x00000001;
        private const uint SlReturnSingleEntry = 0x00000002;

        private static readonly int[] AllDirectoryInfoClasses = { 1, 2, 3, 12, 37, 38 };

        public static int Main(string[] args)
        {
            ProbeArgs parsed = ParseArgs(args);
            ProbeReport report = ExecuteProbe(parsed);

            string json = JsonSerializer.Serialize(report);
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
            string year = args.Year ?? Environment.GetEnvironmentVariable("SYNTHETIC_CLEAN_SHIM_REVIT_YEAR") ?? "2027";
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
            string programFilesAddinsYearDir = Path.Combine(programFiles, "Autodesk", "Revit", "Addins", year);

            string appDataPluginsDir = Path.Combine(appData, "Autodesk", "ApplicationPlugins");
            string programDataPluginsDir = Path.Combine(programData, "Autodesk", "ApplicationPlugins");
            string programFilesPluginsDir = Path.Combine(programFiles, "Autodesk", "ApplicationPlugins");

            string internalAddinsDir = Path.Combine(programFiles, "Autodesk", $"Revit {year}", "AddIns");
            string internalETransmitDbPath = Path.Combine(internalAddinsDir, "eTransmitForRevit", "eTransmitForRevitDB.dll");
            string legacyETransmitDbPath = Path.Combine(programFiles, "Autodesk", $"eTransmit for Revit {year}", "eTransmitForRevitDB.dll");

            string appDataSyntheticAddin = Path.Combine(appDataAddinsYearDir, $"Synthetic{year}.addin");
            string appDataThirdPartyAddin = Path.Combine(appDataAddinsYearDir, "Kinship.addin");
            string programDataSyntheticAddin = Path.Combine(programDataAddinsYearDir, $"Synthetic{year}.addin");
            string programDataThirdPartyAddin = Path.Combine(programDataAddinsYearDir, "CTC-BIM-Project-Suite.addin");
            string programFilesSyntheticAddin = Path.Combine(programFilesAddinsYearDir, $"Synthetic{year}.addin");
            string programFilesThirdPartyAddin = Path.Combine(programFilesAddinsYearDir, "Revit2027ThirdParty.addin");

            string appDataRicaunPackage = Path.Combine(appDataPluginsDir, "ricaun.RevitTest.Application.bundle", "PackageContents.xml");
            string appDataBluebeamDir = Path.Combine(appDataPluginsDir, "Bluebeam Software");
            string appDataBluebeamManifest = Path.Combine(appDataBluebeamDir, "PackageContents.xml");
            string programDataRicaunPackage = Path.Combine(programDataPluginsDir, "ricaun.RevitTest.Application.bundle", "PackageContents.xml");
            string programDataThirdPartyBundle = Path.Combine(programDataPluginsDir, "ThirdParty.bundle");
            string programDataThirdPartyBundleManifest = Path.Combine(programDataThirdPartyBundle, "PackageContents.xml");
            string programFilesRicaunPackage = Path.Combine(programFilesPluginsDir, "ricaun.RevitTest.Application.bundle", "PackageContents.xml");
            string programFilesThirdPartyBundle = Path.Combine(programFilesPluginsDir, "AutoCAD-MCP-Server-2027.bundle");
            string programFilesThirdPartyBundleManifest = Path.Combine(programFilesThirdPartyBundle, "PackageContents.xml");

            string appDataSettingsPath = Path.Combine(appData, "Autodesk", "Revit", $"Autodesk Revit {year}", "AddinsData", "AddInsSettings.json");
            string programDataAdminSettingsPath = Path.Combine(programData, "Autodesk", $"RVT {year}", "Admin", "AdminSettings.json");
            string programFilesAdminSettingsPath = Path.Combine(programFiles, "Autodesk", "Revit", "Admin", year, "AdminSettings.json");

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
                ProgramFilesAddinsDirExists = Directory.Exists(programFilesAddinsYearDir),
                AppDataPluginsDirExists = Directory.Exists(appDataPluginsDir),
                ProgramDataPluginsDirExists = Directory.Exists(programDataPluginsDir),
                ProgramFilesPluginsDirExists = Directory.Exists(programFilesPluginsDir),

                AppDataAddinFiles = SafeGetFiles(appDataAddinsYearDir, "*.addin"),
                AppDataAddinAllEntries = SafeGetFileSystemEntries(appDataAddinsYearDir),
                ProgramDataAddinFiles = SafeGetFiles(programDataAddinsYearDir, "*.addin"),
                ProgramDataAddinBundleDirs = SafeGetDirectories(programDataAddinsYearDir, "*.bundle"),
                ProgramDataAddinAllEntries = SafeGetFileSystemEntries(programDataAddinsYearDir),
                ProgramFilesAddinFiles = SafeGetFiles(programFilesAddinsYearDir, "*.addin"),
                ProgramFilesAddinBundleDirs = SafeGetDirectories(programFilesAddinsYearDir, "*.bundle"),
                ProgramFilesAddinAllEntries = SafeGetFileSystemEntries(programFilesAddinsYearDir),

                AppDataBundleDirs = SafeGetDirectories(appDataPluginsDir, "*.bundle"),
                AppDataPluginAllDirs = SafeGetDirectories(appDataPluginsDir, "*"),
                ProgramDataBundleDirs = SafeGetDirectories(programDataPluginsDir, "*.bundle"),
                ProgramDataPluginAllDirs = SafeGetDirectories(programDataPluginsDir, "*"),
                ProgramFilesBundleDirs = SafeGetDirectories(programFilesPluginsDir, "*.bundle"),
                ProgramFilesPluginAllDirs = SafeGetDirectories(programFilesPluginsDir, "*"),

                InternalAddinFiles = SafeGetFiles(internalAddinsDir, "*.addin", SearchOption.AllDirectories),
                InternalETransmitDbExists = File.Exists(internalETransmitDbPath),
                InternalETransmitDbText = TryReadAllText(internalETransmitDbPath, out _),
                LegacyETransmitDbExists = File.Exists(legacyETransmitDbPath),
                LegacyETransmitDbText = TryReadAllText(legacyETransmitDbPath, out _),

                AppDataSyntheticAddinExists = File.Exists(appDataSyntheticAddin),
                AppDataSyntheticAddinText = TryReadAllText(appDataSyntheticAddin, out _),
                AppDataThirdPartyAddinExists = File.Exists(appDataThirdPartyAddin),
                AppDataThirdPartyAddinReadError = TryReadExpectError(appDataThirdPartyAddin),

                ProgramDataSyntheticAddinExists = File.Exists(programDataSyntheticAddin),
                ProgramDataSyntheticAddinReadError = TryReadExpectError(programDataSyntheticAddin),
                ProgramDataThirdPartyAddinExists = File.Exists(programDataThirdPartyAddin),
                ProgramDataThirdPartyAddinReadError = TryReadExpectError(programDataThirdPartyAddin),

                ProgramFilesSyntheticAddinExists = File.Exists(programFilesSyntheticAddin),
                ProgramFilesSyntheticAddinReadError = TryReadExpectError(programFilesSyntheticAddin),
                ProgramFilesThirdPartyAddinExists = File.Exists(programFilesThirdPartyAddin),
                ProgramFilesThirdPartyAddinReadError = TryReadExpectError(programFilesThirdPartyAddin),

                AppDataRicaunPackageExists = File.Exists(appDataRicaunPackage),
                AppDataRicaunPackageText = TryReadAllText(appDataRicaunPackage, out _),
                AppDataBluebeamDirExists = Directory.Exists(appDataBluebeamDir),
                AppDataBluebeamManifestExists = File.Exists(appDataBluebeamManifest),
                AppDataBluebeamReadError = TryReadExpectError(appDataBluebeamManifest),

                ProgramDataRicaunPackageExists = File.Exists(programDataRicaunPackage),
                ProgramDataThirdPartyBundleExists = Directory.Exists(programDataThirdPartyBundle),
                ProgramDataThirdPartyBundleManifestExists = File.Exists(programDataThirdPartyBundleManifest),

                ProgramFilesRicaunPackageExists = File.Exists(programFilesRicaunPackage),
                ProgramFilesThirdPartyBundleExists = Directory.Exists(programFilesThirdPartyBundle),
                ProgramFilesThirdPartyBundleManifestExists = File.Exists(programFilesThirdPartyBundleManifest)
            };

            if (args.ExerciseNtApis)
            {
                ExerciseDirectNtApis(
                    report,
                    year,
                    appDataAddinsYearDir,
                    programDataAddinsYearDir,
                    programFilesAddinsYearDir,
                    appDataSyntheticAddin,
                    programDataSyntheticAddin,
                    programFilesThirdPartyAddin);
            }

            ExerciseSettingsVirtualization(
                report,
                allowWrites: args.WriteSettings,
                appDataSettingsPath,
                programDataAdminSettingsPath,
                programFilesAdminSettingsPath);

            if (args.SpawnGrandchild)
            {
                SpawnGrandchild(args, year, appData, programData, programFiles, report);
            }

            return report;
        }

        private static void ExerciseDirectNtApis(
            ProbeReport report,
            string year,
            string appDataAddinsYearDir,
            string programDataAddinsYearDir,
            string programFilesAddinsYearDir,
            string appDataSyntheticAddin,
            string programDataSyntheticAddin,
            string programFilesThirdPartyAddin)
        {
            if (Directory.Exists(appDataAddinsYearDir))
            {
                foreach (int infoClass in AllDirectoryInfoClasses)
                {
                    string key = infoClass.ToString();
                    report.NtQuerySingleEntryAppDataClasses[key] = EnumerateViaNtQueryDirectoryFile(
                        appDataAddinsYearDir,
                        "*.addin",
                        infoClass,
                        useEx: false,
                        singleEntry: true,
                        out _,
                        out uint secondStatus);

                    if (infoClass == 2)
                    {
                        report.NtQueryAppDataSecondCallAfterMatchStatus = secondStatus;
                    }

                    report.NtQueryExMultiEntryAppDataClasses[key] = EnumerateViaNtQueryDirectoryFile(
                        appDataAddinsYearDir,
                        "*.addin",
                        infoClass,
                        useEx: true,
                        singleEntry: false,
                        out _,
                        out _);

                    report.NtQueryNullPatternAllEntriesAppDataClasses[key] = EnumerateViaNtQueryDirectoryFile(
                        appDataAddinsYearDir,
                        pattern: null,
                        infoClass,
                        useEx: false,
                        singleEntry: false,
                        out _,
                        out _);
                }

                report.NtQueryAsyncAppDataEntries = EnumerateViaNtQueryDirectoryFile(
                    appDataAddinsYearDir,
                    "*.addin",
                    infoClass: 2,
                    useEx: false,
                    singleEntry: false,
                    out _,
                    out _,
                    asyncIo: true);
            }

            if (Directory.Exists(programDataAddinsYearDir))
            {
                _ = EnumerateViaNtQueryDirectoryFile(
                    programDataAddinsYearDir,
                    "*.addin",
                    infoClass: 2,
                    useEx: false,
                    singleEntry: false,
                    out uint firstEmptyStatus,
                    out _);
                report.NtQueryProgramDataEmptyFirstStatus = firstEmptyStatus;

                _ = EnumerateViaNtQueryDirectoryFile(
                    programDataAddinsYearDir,
                    "*.addin",
                    infoClass: 2,
                    useEx: false,
                    singleEntry: false,
                    out uint asyncEmptyStatus,
                    out _,
                    asyncIo: true);
                report.NtQueryAsyncProgramDataFirstStatus = asyncEmptyStatus;
            }

            if (Directory.Exists(programFilesAddinsYearDir))
            {
                _ = EnumerateViaNtQueryDirectoryFile(
                    programFilesAddinsYearDir,
                    "*.addin",
                    infoClass: 37,
                    useEx: true,
                    singleEntry: false,
                    out uint firstEmptyExStatus,
                    out _);
                report.NtQueryProgramFilesExEmptyFirstStatus = firstEmptyExStatus;
            }

            report.NtOpenAppDataSyntheticStatus = TryNtOpenFile(IntPtr.Zero, ToNtPath(appDataSyntheticAddin), isDirectory: false, out SafeFileHandle? hAllowed);
            hAllowed?.Dispose();

            report.NtOpenProgramFilesThirdPartyAddinStatus = TryNtOpenFile(IntPtr.Zero, ToNtPath(programFilesThirdPartyAddin), isDirectory: false, out SafeFileHandle? hBlockedPf);
            hBlockedPf?.Dispose();

            uint openPdDirStatus = TryNtOpenFile(IntPtr.Zero, ToNtPath(programDataAddinsYearDir), isDirectory: true, out SafeFileHandle? hPdDir);
            if (openPdDirStatus == StatusSuccess && hPdDir != null)
            {
                using (hPdDir)
                {
                    report.NtCreateRelativeProgramDataSyntheticStatus = TryNtCreateFile(
                        hPdDir.DangerousGetHandle(),
                        $"Synthetic{year}.addin",
                        isDirectory: false,
                        out SafeFileHandle? hRelBlocked);
                    hRelBlocked?.Dispose();
                }
            }

            uint openAdDirStatus = TryNtOpenFile(IntPtr.Zero, ToNtPath(appDataAddinsYearDir), isDirectory: true, out SafeFileHandle? hAdDir);
            if (openAdDirStatus == StatusSuccess && hAdDir != null)
            {
                using (hAdDir)
                {
                    report.NtOpenRelativeAppDataSyntheticStatus = TryNtOpenFile(
                        hAdDir.DangerousGetHandle(),
                        $"Synthetic{year}.addin",
                        isDirectory: false,
                        out SafeFileHandle? hRelAllowed);
                    hRelAllowed?.Dispose();
                }
            }

            _ = programDataSyntheticAddin;
        }

        private static void ExerciseSettingsVirtualization(
            ProbeReport report,
            bool allowWrites,
            string appDataSettingsPath,
            string programDataAdminSettingsPath,
            string programFilesAdminSettingsPath)
        {
            report.AppDataAddInsSettingsExistsBeforeWrite = File.Exists(appDataSettingsPath);
            report.AppDataAddInsSettingsInitialRead = TryReadAllText(appDataSettingsPath, out _);
            report.AppDataAddInsSettingsNtOpenRead = TryReadViaNtOpenFile(appDataSettingsPath);

            report.ProgramDataAdminSettingsExistsBeforeWrite = File.Exists(programDataAdminSettingsPath);
            report.ProgramDataAdminSettingsInitialRead = TryReadAllText(programDataAdminSettingsPath, out _);

            report.ProgramFilesAdminSettingsExistsBeforeWrite = File.Exists(programFilesAdminSettingsPath);
            report.ProgramFilesAdminSettingsInitialRead = TryReadAllText(programFilesAdminSettingsPath, out _);
            report.ProgramFilesAdminSettingsNtCreateRead = TryReadViaNtCreateFile(programFilesAdminSettingsPath);

            if (!allowWrites)
            {
                return;
            }

            int pid = Process.GetCurrentProcess().Id;

            string appDataWritePayload = $"{{\"DisableAllAddIns\":false,\"AddInItemSettings\":[{{\"Id\":\"net8-probe-{pid}\",\"LoadTime\":17}}]}}";
            File.WriteAllText(appDataSettingsPath, appDataWritePayload, Encoding.UTF8);
            report.AppDataAddInsSettingsAfterWriteRead = TryReadAllText(appDataSettingsPath, out _);

            string programDataAdminWritePayload = $"{{\"DisableAllAddIns\":false,\"Source\":\"programdata-{pid}\"}}";
            File.WriteAllText(programDataAdminSettingsPath, programDataAdminWritePayload, Encoding.UTF8);
            report.ProgramDataAdminSettingsAfterWriteRead = TryReadAllText(programDataAdminSettingsPath, out _);

            string programFilesAdminWritePayload = $"{{\"DisableAllAddIns\":false,\"Source\":\"programfiles-{pid}\"}}";
            File.WriteAllText(programFilesAdminSettingsPath, programFilesAdminWritePayload, Encoding.UTF8);
            report.ProgramFilesAdminSettingsAfterWriteRead = TryReadAllText(programFilesAdminSettingsPath, out _);
            report.ProgramDataAdminSettingsAfterBothWritesRead = TryReadAllText(programDataAdminSettingsPath, out _);
        }

        private static string? TryReadViaNtOpenFile(string win32Path)
        {
            uint status = TryNtOpenFile(IntPtr.Zero, ToNtPath(win32Path), isDirectory: false, out SafeFileHandle? handle);
            if (status != StatusSuccess || handle == null)
            {
                return null;
            }

            using (handle)
            using (var fs = new FileStream(handle, FileAccess.Read))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static string? TryReadViaNtCreateFile(string win32Path)
        {
            uint status = TryNtCreateFile(IntPtr.Zero, ToNtPath(win32Path), isDirectory: false, out SafeFileHandle? handle);
            if (status != StatusSuccess || handle == null)
            {
                return null;
            }

            using (handle)
            using (var fs = new FileStream(handle, FileAccess.Read))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static string[] EnumerateViaNtQueryDirectoryFile(
            string directoryPath,
            string? pattern,
            int infoClass,
            bool useEx,
            bool singleEntry,
            out uint firstCallStatus,
            out uint trailingCallStatus,
            bool asyncIo = false)
        {
            firstCallStatus = 0;
            trailingCallStatus = 0;

            uint openStatus = TryNtOpenFile(
                IntPtr.Zero,
                ToNtPath(directoryPath),
                isDirectory: true,
                out SafeFileHandle? dirHandle,
                synchronous: !asyncIo);
            if (openStatus != StatusSuccess || dirHandle == null)
            {
                firstCallStatus = openStatus;
                return Array.Empty<string>();
            }

            using (dirHandle)
            using (ManualResetEvent? ioEvent = asyncIo ? new ManualResetEvent(false) : null)
            {
                const int bufferSize = 4096;
                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                IntPtr patternStrPtr = pattern != null ? Marshal.StringToHGlobalUni(pattern) : IntPtr.Zero;
                IntPtr unicodeStringPtr = pattern != null ? Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.UnicodeString>()) : IntPtr.Zero;
                IntPtr eventHandle = ioEvent != null ? ioEvent.SafeWaitHandle.DangerousGetHandle() : IntPtr.Zero;
                try
                {
                    if (pattern != null)
                    {
                        var unicodePattern = new NativeMethods.UnicodeString
                        {
                            Length = checked((ushort)(pattern.Length * sizeof(char))),
                            MaximumLength = checked((ushort)((pattern.Length + 1) * sizeof(char))),
                            Buffer = patternStrPtr
                        };
                        Marshal.StructureToPtr(unicodePattern, unicodeStringPtr, fDeleteOld: false);
                    }

                    var names = new List<string>();
                    bool firstCall = true;
                    while (true)
                    {
                        ioEvent?.Reset();
                        uint status;
                        NativeMethods.IoStatusBlock ioStatus;
                        if (useEx)
                        {
                            uint flags = firstCall ? SlRestartScan : 0;
                            if (singleEntry)
                            {
                                flags |= SlReturnSingleEntry;
                            }

                            status = NativeMethods.NtQueryDirectoryFileEx(
                                dirHandle.DangerousGetHandle(),
                                eventHandle,
                                IntPtr.Zero,
                                IntPtr.Zero,
                                out ioStatus,
                                buffer,
                                bufferSize,
                                (uint)infoClass,
                                flags,
                                unicodeStringPtr);
                        }
                        else
                        {
                            status = NativeMethods.NtQueryDirectoryFile(
                                dirHandle.DangerousGetHandle(),
                                eventHandle,
                                IntPtr.Zero,
                                IntPtr.Zero,
                                out ioStatus,
                                buffer,
                                bufferSize,
                                (uint)infoClass,
                                singleEntry,
                                unicodeStringPtr,
                                firstCall);
                        }

                        if (status == StatusPending && ioEvent != null)
                        {
                            ioEvent.WaitOne(TimeSpan.FromSeconds(5));
                            status = unchecked((uint)ioStatus.PointerOrStatus.ToInt64());
                        }

                        if (firstCall)
                        {
                            firstCallStatus = status;
                        }

                        firstCall = false;

                        if (status == StatusNoMoreFiles || status == StatusNoSuchFile)
                        {
                            trailingCallStatus = status;
                            break;
                        }

                        if (status != StatusSuccess)
                        {
                            trailingCallStatus = status;
                            break;
                        }

                        long infoBytes = ioStatus.Information.ToInt64();
                        if (infoBytes <= 0 || infoBytes > bufferSize)
                        {
                            throw new InvalidOperationException($"Unexpected IoStatusBlock.Information={infoBytes} for infoClass={infoClass}.");
                        }

                        ParseDirectoryBuffer(buffer, (int)infoBytes, infoClass, names);
                    }

                    return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                }
                finally
                {
                    if (unicodeStringPtr != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(unicodeStringPtr);
                    }

                    if (patternStrPtr != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(patternStrPtr);
                    }

                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        private static void ParseDirectoryBuffer(IntPtr buffer, int totalBytes, int infoClass, List<string> names)
        {
            (int nameOffset, int lengthOffset) = infoClass switch
            {
                1 => (64, 60),
                2 => (68, 60),
                3 => (94, 60),
                12 => (12, 8),
                37 => (104, 60),
                38 => (80, 60),
                _ => throw new ArgumentOutOfRangeException(nameof(infoClass))
            };

            int offset = 0;
            while (true)
            {
                if (offset < 0 || offset + nameOffset > totalBytes)
                {
                    throw new InvalidOperationException($"Directory buffer entry offset {offset} exceeded totalBytes {totalBytes} for infoClass={infoClass}.");
                }

                IntPtr entryPtr = IntPtr.Add(buffer, offset);
                int nextOffset = Marshal.ReadInt32(entryPtr, 0);
                int nameBytes = Marshal.ReadInt32(entryPtr, lengthOffset);
                if (nameBytes < 0 || offset + nameOffset + nameBytes > totalBytes)
                {
                    throw new InvalidOperationException($"Directory entry name length {nameBytes} exceeded totalBytes {totalBytes} at offset {offset} for infoClass={infoClass}.");
                }

                if (nameBytes > 0)
                {
                    string name = Marshal.PtrToStringUni(IntPtr.Add(entryPtr, nameOffset), nameBytes / 2);
                    if (name != "." && name != "..")
                    {
                        names.Add(name);
                    }
                }

                if (nextOffset == 0)
                {
                    break;
                }

                if ((nextOffset & 0x7) != 0 && infoClass != 12)
                {
                    throw new InvalidOperationException($"Unaligned NextEntryOffset={nextOffset} for infoClass={infoClass}.");
                }

                offset += nextOffset;
            }
        }

        private static uint TryNtOpenFile(
            IntPtr rootDirectory,
            string objectName,
            bool isDirectory,
            out SafeFileHandle? handle,
            bool synchronous = true)
        {
            handle = null;
            IntPtr nameBuf = Marshal.StringToHGlobalUni(objectName);
            IntPtr unicodePtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.UnicodeString>());
            try
            {
                var unicode = new NativeMethods.UnicodeString
                {
                    Length = checked((ushort)(objectName.Length * sizeof(char))),
                    MaximumLength = checked((ushort)((objectName.Length + 1) * sizeof(char))),
                    Buffer = nameBuf
                };
                Marshal.StructureToPtr(unicode, unicodePtr, fDeleteOld: false);

                var oa = new NativeMethods.ObjectAttributes
                {
                    Length = Marshal.SizeOf<NativeMethods.ObjectAttributes>(),
                    RootDirectory = rootDirectory,
                    ObjectName = unicodePtr,
                    Attributes = ObjCaseInsensitive,
                    SecurityDescriptor = IntPtr.Zero,
                    SecurityQualityOfService = IntPtr.Zero
                };

                uint access = isDirectory
                    ? (synchronous ? (FileListDirectory | Synchronize) : FileListDirectory)
                    : (synchronous ? (FileReadData | FileReadAttributes | Synchronize) : (FileReadData | FileReadAttributes));
                uint options = (synchronous ? FileSynchronousIoNonalert : 0) | (isDirectory ? FileDirectoryFile : FileNonDirectoryFile);

                uint status = NativeMethods.NtOpenFile(
                    out IntPtr rawHandle,
                    access,
                    ref oa,
                    out _,
                    FileShareAll,
                    options);

                if (status == StatusSuccess && rawHandle != IntPtr.Zero)
                {
                    handle = new SafeFileHandle(rawHandle, ownsHandle: true);
                }

                return status;
            }
            finally
            {
                Marshal.FreeHGlobal(unicodePtr);
                Marshal.FreeHGlobal(nameBuf);
            }
        }

        private static uint TryNtCreateFile(IntPtr rootDirectory, string objectName, bool isDirectory, out SafeFileHandle? handle)
        {
            handle = null;
            IntPtr nameBuf = Marshal.StringToHGlobalUni(objectName);
            IntPtr unicodePtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.UnicodeString>());
            try
            {
                var unicode = new NativeMethods.UnicodeString
                {
                    Length = checked((ushort)(objectName.Length * sizeof(char))),
                    MaximumLength = checked((ushort)((objectName.Length + 1) * sizeof(char))),
                    Buffer = nameBuf
                };
                Marshal.StructureToPtr(unicode, unicodePtr, fDeleteOld: false);

                var oa = new NativeMethods.ObjectAttributes
                {
                    Length = Marshal.SizeOf<NativeMethods.ObjectAttributes>(),
                    RootDirectory = rootDirectory,
                    ObjectName = unicodePtr,
                    Attributes = ObjCaseInsensitive,
                    SecurityDescriptor = IntPtr.Zero,
                    SecurityQualityOfService = IntPtr.Zero
                };

                uint access = isDirectory
                    ? (FileListDirectory | Synchronize)
                    : (FileReadData | FileReadAttributes | Synchronize);
                uint options = FileSynchronousIoNonalert | (isDirectory ? FileDirectoryFile : FileNonDirectoryFile);

                uint status = NativeMethods.NtCreateFile(
                    out IntPtr rawHandle,
                    access,
                    ref oa,
                    out _,
                    IntPtr.Zero,
                    0,
                    FileShareAll,
                    FileOpen,
                    options,
                    IntPtr.Zero,
                    0);

                if (status == StatusSuccess && rawHandle != IntPtr.Zero)
                {
                    handle = new SafeFileHandle(rawHandle, ownsHandle: true);
                }

                return status;
            }
            finally
            {
                Marshal.FreeHGlobal(unicodePtr);
                Marshal.FreeHGlobal(nameBuf);
            }
        }

        private static string ToNtPath(string win32Path)
        {
            string full = Path.GetFullPath(win32Path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return @"\??\UNC\" + full.Substring(2);
            }

            return @"\??\" + full;
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
                ?? throw new InvalidOperationException("Cannot resolve Net8Probe executable path.");

            string childArgs =
                $"--year \"{year}\" " +
                $"--appdata \"{appData}\" " +
                $"--programdata \"{programData}\" " +
                $"--programfiles \"{programFiles}\" " +
                $"--no-settings-writes " +
                $"--no-nt-apis";

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
                report.GrandchildError = "Process.Start returned null for grandchild Net8Probe.";
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
                    ? "Grandchild Net8Probe timed out."
                    : $"Grandchild Net8Probe timed out (kill failed: {killError}).";
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
                report.Grandchild = JsonSerializer.Deserialize<ProbeReport>(stdout.Trim());
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
                    case "--no-settings-writes":
                        parsed.WriteSettings = false;
                        break;
                    case "--no-nt-apis":
                        parsed.ExerciseNtApis = false;
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
            public bool WriteSettings = true;
            public bool ExerciseNtApis = true;
        }

        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct UnicodeString
            {
                public ushort Length;
                public ushort MaximumLength;
                public IntPtr Buffer;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct ObjectAttributes
            {
                public int Length;
                public IntPtr RootDirectory;
                public IntPtr ObjectName;
                public uint Attributes;
                public IntPtr SecurityDescriptor;
                public IntPtr SecurityQualityOfService;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct IoStatusBlock
            {
                public IntPtr PointerOrStatus;
                public IntPtr Information;
            }

            [DllImport("ntdll.dll", ExactSpelling = true)]
            public static extern uint NtQueryDirectoryFile(
                IntPtr fileHandle,
                IntPtr eventHandle,
                IntPtr apcRoutine,
                IntPtr apcContext,
                out IoStatusBlock ioStatusBlock,
                IntPtr fileInformation,
                uint length,
                uint fileInformationClass,
                [MarshalAs(UnmanagedType.U1)] bool returnSingleEntry,
                IntPtr fileName,
                [MarshalAs(UnmanagedType.U1)] bool restartScan);

            [DllImport("ntdll.dll", ExactSpelling = true)]
            public static extern uint NtQueryDirectoryFileEx(
                IntPtr fileHandle,
                IntPtr eventHandle,
                IntPtr apcRoutine,
                IntPtr apcContext,
                out IoStatusBlock ioStatusBlock,
                IntPtr fileInformation,
                uint length,
                uint fileInformationClass,
                uint queryFlags,
                IntPtr fileName);

            [DllImport("ntdll.dll", ExactSpelling = true)]
            public static extern uint NtCreateFile(
                out IntPtr fileHandle,
                uint desiredAccess,
                ref ObjectAttributes objectAttributes,
                out IoStatusBlock ioStatusBlock,
                IntPtr allocationSize,
                uint fileAttributes,
                uint shareAccess,
                uint createDisposition,
                uint createOptions,
                IntPtr eaBuffer,
                uint eaLength);

            [DllImport("ntdll.dll", ExactSpelling = true)]
            public static extern uint NtOpenFile(
                out IntPtr fileHandle,
                uint desiredAccess,
                ref ObjectAttributes objectAttributes,
                out IoStatusBlock ioStatusBlock,
                uint shareAccess,
                uint openOptions);
        }
    }

    internal sealed class ProbeReport
    {
        [JsonPropertyName("processId")]
        public int ProcessId { get; set; }

        [JsonPropertyName("year")]
        public string Year { get; set; } = string.Empty;

        [JsonPropertyName("loadedModules")]
        public string[] LoadedModules { get; set; } = Array.Empty<string>();

        [JsonPropertyName("hasRevitCleanShim")]
        public bool HasRevitCleanShim { get; set; }

        [JsonPropertyName("appDataAddinsDirExists")]
        public bool AppDataAddinsDirExists { get; set; }

        [JsonPropertyName("programDataAddinsDirExists")]
        public bool ProgramDataAddinsDirExists { get; set; }

        [JsonPropertyName("programFilesAddinsDirExists")]
        public bool ProgramFilesAddinsDirExists { get; set; }

        [JsonPropertyName("appDataPluginsDirExists")]
        public bool AppDataPluginsDirExists { get; set; }

        [JsonPropertyName("programDataPluginsDirExists")]
        public bool ProgramDataPluginsDirExists { get; set; }

        [JsonPropertyName("programFilesPluginsDirExists")]
        public bool ProgramFilesPluginsDirExists { get; set; }

        [JsonPropertyName("appDataAddinFiles")]
        public string[] AppDataAddinFiles { get; set; } = Array.Empty<string>();

        [JsonPropertyName("appDataAddinAllEntries")]
        public string[] AppDataAddinAllEntries { get; set; } = Array.Empty<string>();

        [JsonPropertyName("programDataAddinFiles")]
        public string[] ProgramDataAddinFiles { get; set; } = Array.Empty<string>();

        [JsonPropertyName("programDataAddinBundleDirs")]
        public string[] ProgramDataAddinBundleDirs { get; set; } = Array.Empty<string>();

        [JsonPropertyName("programDataAddinAllEntries")]
        public string[] ProgramDataAddinAllEntries { get; set; } = Array.Empty<string>();

        [JsonPropertyName("programFilesAddinFiles")]
        public string[] ProgramFilesAddinFiles { get; set; } = Array.Empty<string>();

        [JsonPropertyName("programFilesAddinBundleDirs")]
        public string[] ProgramFilesAddinBundleDirs { get; set; } = Array.Empty<string>();

        [JsonPropertyName("programFilesAddinAllEntries")]
        public string[] ProgramFilesAddinAllEntries { get; set; } = Array.Empty<string>();

        [JsonPropertyName("appDataBundleDirs")]
        public string[] AppDataBundleDirs { get; set; } = Array.Empty<string>();

        [JsonPropertyName("appDataPluginAllDirs")]
        public string[] AppDataPluginAllDirs { get; set; } = Array.Empty<string>();

        [JsonPropertyName("programDataBundleDirs")]
        public string[] ProgramDataBundleDirs { get; set; } = Array.Empty<string>();

        [JsonPropertyName("programDataPluginAllDirs")]
        public string[] ProgramDataPluginAllDirs { get; set; } = Array.Empty<string>();

        [JsonPropertyName("programFilesBundleDirs")]
        public string[] ProgramFilesBundleDirs { get; set; } = Array.Empty<string>();

        [JsonPropertyName("programFilesPluginAllDirs")]
        public string[] ProgramFilesPluginAllDirs { get; set; } = Array.Empty<string>();

        [JsonPropertyName("internalAddinFiles")]
        public string[] InternalAddinFiles { get; set; } = Array.Empty<string>();

        [JsonPropertyName("internalETransmitDbExists")]
        public bool InternalETransmitDbExists { get; set; }

        [JsonPropertyName("internalETransmitDbText")]
        public string? InternalETransmitDbText { get; set; }

        [JsonPropertyName("legacyETransmitDbExists")]
        public bool LegacyETransmitDbExists { get; set; }

        [JsonPropertyName("legacyETransmitDbText")]
        public string? LegacyETransmitDbText { get; set; }

        [JsonPropertyName("appDataSyntheticAddinExists")]
        public bool AppDataSyntheticAddinExists { get; set; }

        [JsonPropertyName("appDataSyntheticAddinText")]
        public string? AppDataSyntheticAddinText { get; set; }

        [JsonPropertyName("appDataThirdPartyAddinExists")]
        public bool AppDataThirdPartyAddinExists { get; set; }

        [JsonPropertyName("appDataThirdPartyAddinReadError")]
        public string? AppDataThirdPartyAddinReadError { get; set; }

        [JsonPropertyName("programDataSyntheticAddinExists")]
        public bool ProgramDataSyntheticAddinExists { get; set; }

        [JsonPropertyName("programDataSyntheticAddinReadError")]
        public string? ProgramDataSyntheticAddinReadError { get; set; }

        [JsonPropertyName("programDataThirdPartyAddinExists")]
        public bool ProgramDataThirdPartyAddinExists { get; set; }

        [JsonPropertyName("programDataThirdPartyAddinReadError")]
        public string? ProgramDataThirdPartyAddinReadError { get; set; }

        [JsonPropertyName("programFilesSyntheticAddinExists")]
        public bool ProgramFilesSyntheticAddinExists { get; set; }

        [JsonPropertyName("programFilesSyntheticAddinReadError")]
        public string? ProgramFilesSyntheticAddinReadError { get; set; }

        [JsonPropertyName("programFilesThirdPartyAddinExists")]
        public bool ProgramFilesThirdPartyAddinExists { get; set; }

        [JsonPropertyName("programFilesThirdPartyAddinReadError")]
        public string? ProgramFilesThirdPartyAddinReadError { get; set; }

        [JsonPropertyName("appDataRicaunPackageExists")]
        public bool AppDataRicaunPackageExists { get; set; }

        [JsonPropertyName("appDataRicaunPackageText")]
        public string? AppDataRicaunPackageText { get; set; }

        [JsonPropertyName("appDataBluebeamDirExists")]
        public bool AppDataBluebeamDirExists { get; set; }

        [JsonPropertyName("appDataBluebeamManifestExists")]
        public bool AppDataBluebeamManifestExists { get; set; }

        [JsonPropertyName("appDataBluebeamReadError")]
        public string? AppDataBluebeamReadError { get; set; }

        [JsonPropertyName("programDataRicaunPackageExists")]
        public bool ProgramDataRicaunPackageExists { get; set; }

        [JsonPropertyName("programDataThirdPartyBundleExists")]
        public bool ProgramDataThirdPartyBundleExists { get; set; }

        [JsonPropertyName("programDataThirdPartyBundleManifestExists")]
        public bool ProgramDataThirdPartyBundleManifestExists { get; set; }

        [JsonPropertyName("programFilesRicaunPackageExists")]
        public bool ProgramFilesRicaunPackageExists { get; set; }

        [JsonPropertyName("programFilesThirdPartyBundleExists")]
        public bool ProgramFilesThirdPartyBundleExists { get; set; }

        [JsonPropertyName("programFilesThirdPartyBundleManifestExists")]
        public bool ProgramFilesThirdPartyBundleManifestExists { get; set; }

        [JsonPropertyName("ntQuerySingleEntryAppDataClasses")]
        public Dictionary<string, string[]> NtQuerySingleEntryAppDataClasses { get; set; } = new();

        [JsonPropertyName("ntQueryExMultiEntryAppDataClasses")]
        public Dictionary<string, string[]> NtQueryExMultiEntryAppDataClasses { get; set; } = new();

        [JsonPropertyName("ntQueryNullPatternAllEntriesAppDataClasses")]
        public Dictionary<string, string[]> NtQueryNullPatternAllEntriesAppDataClasses { get; set; } = new();

        [JsonPropertyName("ntQueryAppDataSecondCallAfterMatchStatus")]
        public uint NtQueryAppDataSecondCallAfterMatchStatus { get; set; }

        [JsonPropertyName("ntQueryProgramDataEmptyFirstStatus")]
        public uint NtQueryProgramDataEmptyFirstStatus { get; set; }

        [JsonPropertyName("ntQueryProgramFilesExEmptyFirstStatus")]
        public uint NtQueryProgramFilesExEmptyFirstStatus { get; set; }

        [JsonPropertyName("ntQueryAsyncAppDataEntries")]
        public string[] NtQueryAsyncAppDataEntries { get; set; } = Array.Empty<string>();

        [JsonPropertyName("ntQueryAsyncProgramDataFirstStatus")]
        public uint NtQueryAsyncProgramDataFirstStatus { get; set; }

        [JsonPropertyName("ntOpenAppDataSyntheticStatus")]
        public uint NtOpenAppDataSyntheticStatus { get; set; }

        [JsonPropertyName("ntOpenProgramFilesThirdPartyAddinStatus")]
        public uint NtOpenProgramFilesThirdPartyAddinStatus { get; set; }

        [JsonPropertyName("ntCreateRelativeProgramDataSyntheticStatus")]
        public uint NtCreateRelativeProgramDataSyntheticStatus { get; set; }

        [JsonPropertyName("ntOpenRelativeAppDataSyntheticStatus")]
        public uint NtOpenRelativeAppDataSyntheticStatus { get; set; }

        [JsonPropertyName("appDataAddInsSettingsExistsBeforeWrite")]
        public bool AppDataAddInsSettingsExistsBeforeWrite { get; set; }

        [JsonPropertyName("appDataAddInsSettingsInitialRead")]
        public string? AppDataAddInsSettingsInitialRead { get; set; }

        [JsonPropertyName("appDataAddInsSettingsNtOpenRead")]
        public string? AppDataAddInsSettingsNtOpenRead { get; set; }

        [JsonPropertyName("appDataAddInsSettingsAfterWriteRead")]
        public string? AppDataAddInsSettingsAfterWriteRead { get; set; }

        [JsonPropertyName("programDataAdminSettingsExistsBeforeWrite")]
        public bool ProgramDataAdminSettingsExistsBeforeWrite { get; set; }

        [JsonPropertyName("programDataAdminSettingsInitialRead")]
        public string? ProgramDataAdminSettingsInitialRead { get; set; }

        [JsonPropertyName("programDataAdminSettingsAfterWriteRead")]
        public string? ProgramDataAdminSettingsAfterWriteRead { get; set; }

        [JsonPropertyName("programFilesAdminSettingsExistsBeforeWrite")]
        public bool ProgramFilesAdminSettingsExistsBeforeWrite { get; set; }

        [JsonPropertyName("programFilesAdminSettingsInitialRead")]
        public string? ProgramFilesAdminSettingsInitialRead { get; set; }

        [JsonPropertyName("programFilesAdminSettingsNtCreateRead")]
        public string? ProgramFilesAdminSettingsNtCreateRead { get; set; }

        [JsonPropertyName("programFilesAdminSettingsAfterWriteRead")]
        public string? ProgramFilesAdminSettingsAfterWriteRead { get; set; }

        [JsonPropertyName("programDataAdminSettingsAfterBothWritesRead")]
        public string? ProgramDataAdminSettingsAfterBothWritesRead { get; set; }

        [JsonPropertyName("grandchildExitCode")]
        public int? GrandchildExitCode { get; set; }

        [JsonPropertyName("grandchildError")]
        public string? GrandchildError { get; set; }

        [JsonPropertyName("grandchild")]
        public ProbeReport? Grandchild { get; set; }
    }
}
