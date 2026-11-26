using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml;
using System.Xml.Linq;
using RevitWorkerPool.Ipc;

namespace RevitWorkerPool.Launching;

/// <summary>
/// Abstraction over Windows Task Scheduler (<c>Schedule.Service</c> COM) for registering,
/// updating, and running the per-user interactive supervisor task.
/// </summary>
internal interface IScheduledTaskService
{
    string? GetTaskXml(string folderPath, string taskName);

    void RegisterOrUpdateTask(string folderPath, string taskName, string taskXml);

    int? RunTask(string folderPath, string taskName, string arguments);
}

/// <summary>
/// Primary supervisor launch adapter for the default production pipe, launching the supervisor
/// in the current user's interactive session (Session 1, no elevation) via a hidden
/// <c>conhost.exe --headless</c> bootstrap under Scheduled Task <c>Synthetic\RevitWorkerPoolSupervisor</c>.
/// </summary>
internal sealed class ScheduledTaskSupervisorLauncher : ISupervisorLauncher
{
    public const string TaskFolderPath = @"\Synthetic";
    public const string TaskName = "RevitWorkerPoolSupervisor";
    public const string FullTaskPath = @"Synthetic\RevitWorkerPoolSupervisor";

    private static readonly XNamespace TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly IScheduledTaskService _taskService;
    private readonly string? _conhostPathOverride;
    private readonly string? _processPathOverride;
    private readonly string? _entryAssemblyPathOverride;

    [SupportedOSPlatform("windows")]
    public ScheduledTaskSupervisorLauncher()
        : this(new Win32TaskSchedulerService())
    {
    }

    internal ScheduledTaskSupervisorLauncher(
        IScheduledTaskService taskService,
        string? conhostPathOverride = null,
        string? processPathOverride = null,
        string? entryAssemblyPathOverride = null)
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _conhostPathOverride = conhostPathOverride;
        _processPathOverride = processPathOverride;
        _entryAssemblyPathOverride = entryAssemblyPathOverride;
    }

    public SupervisorLaunchResult Launch(
        SupervisorClientLaunchOptions options,
        Action<string>? diagnosticLogger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            SupervisorLaunchCommand command = SupervisorLaunchCommandLine.Resolve(
                options,
                _processPathOverride,
                _entryAssemblyPathOverride);

            string conhostPath = !string.IsNullOrWhiteSpace(_conhostPathOverride)
                ? _conhostPathOverride
                : Path.Combine(Environment.SystemDirectory, "conhost.exe");

            string? existingXml = _taskService.GetTaskXml(TaskFolderPath, TaskName);
            if (TaskNeedsRegistrationOrUpdate(
                    existingXml,
                    conhostPath,
                    command.ExecutablePath,
                    command.WorkingDirectory))
            {
                string desiredXml = BuildTaskXml(
                    conhostPath,
                    command.ExecutablePath,
                    command.WorkingDirectory);

                diagnosticLogger?.Invoke(
                    existingXml is null
                        ? $"[launcher] Registering per-user Scheduled Task '{FullTaskPath}'..."
                        : $"[launcher] Updating per-user Scheduled Task '{FullTaskPath}'...");

                _taskService.RegisterOrUpdateTask(TaskFolderPath, TaskName, desiredXml);
            }

            int? enginePid = _taskService.RunTask(TaskFolderPath, TaskName, command.Arguments);
            string detail = enginePid is int pid
                ? $"task='{FullTaskPath}', conhostPid={pid}"
                : $"task='{FullTaskPath}'";

            diagnosticLogger?.Invoke($"[launcher] Started supervisor via Scheduled Task ({detail}).");
            return new SupervisorLaunchResult(SupervisorLaunchMethod.ScheduledTask, enginePid, detail);
        }
        catch (SupervisorLaunchException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SupervisorLaunchException(
                $"Scheduled Task '{FullTaskPath}' launch failed: {ex.Message}",
                ex);
        }
    }

    internal static string BuildTaskActionArguments(string supervisorExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(supervisorExecutablePath))
        {
            throw new ArgumentException("Supervisor executable path must not be empty.", nameof(supervisorExecutablePath));
        }

        return $"--headless {Win32ProcessLaunchUtilities.QuoteArg(supervisorExecutablePath)} $(Arg0)";
    }

    internal static string BuildTaskXml(
        string conhostPath,
        string supervisorExecutablePath,
        string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(conhostPath))
        {
            throw new ArgumentException("conhost.exe path must not be empty.", nameof(conhostPath));
        }

        if (string.IsNullOrWhiteSpace(supervisorExecutablePath))
        {
            throw new ArgumentException("Supervisor executable path must not be empty.", nameof(supervisorExecutablePath));
        }

        string effectiveWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? (Path.GetDirectoryName(supervisorExecutablePath) ?? Directory.GetCurrentDirectory())
            : workingDirectory;

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-16", null),
            new XElement(
                TaskNs + "Task",
                new XAttribute("version", "1.3"),
                new XElement(
                    TaskNs + "RegistrationInfo",
                    new XElement(TaskNs + "Description", "Detached per-user Synthetic Worker Pool supervisor host")),
                new XElement(TaskNs + "Triggers"),
                new XElement(
                    TaskNs + "Principals",
                    new XElement(
                        TaskNs + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(TaskNs + "LogonType", "InteractiveToken"),
                        new XElement(TaskNs + "RunLevel", "LeastPrivilege"))),
                new XElement(
                    TaskNs + "Settings",
                    new XElement(TaskNs + "MultipleInstancesPolicy", "Parallel"),
                    new XElement(TaskNs + "DisallowStartIfOnBatteries", "false"),
                    new XElement(TaskNs + "StopIfGoingOnBatteries", "false"),
                    new XElement(TaskNs + "AllowHardTerminate", "true"),
                    new XElement(TaskNs + "StartWhenAvailable", "false"),
                    new XElement(TaskNs + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(
                        TaskNs + "IdleSettings",
                        new XElement(TaskNs + "StopOnIdleEnd", "false"),
                        new XElement(TaskNs + "RestartOnIdle", "false")),
                    new XElement(TaskNs + "AllowStartOnDemand", "true"),
                    new XElement(TaskNs + "Enabled", "true"),
                    new XElement(TaskNs + "Hidden", "true"),
                    new XElement(TaskNs + "RunOnlyIfIdle", "false"),
                    new XElement(TaskNs + "DisallowStartOnRemoteAppSession", "false"),
                    new XElement(TaskNs + "UseUnifiedSchedulingEngine", "true"),
                    new XElement(TaskNs + "WakeToRun", "false"),
                    new XElement(TaskNs + "ExecutionTimeLimit", "PT0S"),
                    new XElement(TaskNs + "Priority", "7")),
                new XElement(
                    TaskNs + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(
                        TaskNs + "Exec",
                        new XElement(TaskNs + "Command", conhostPath),
                        new XElement(TaskNs + "Arguments", BuildTaskActionArguments(supervisorExecutablePath)),
                        new XElement(TaskNs + "WorkingDirectory", effectiveWorkingDirectory)))));

        using var writer = new StringWriter();
        doc.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }

    internal static bool TaskNeedsRegistrationOrUpdate(
        string? existingXml,
        string desiredConhostPath,
        string desiredSupervisorExecutablePath,
        string desiredWorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(existingXml))
        {
            return true;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(existingXml);
        }
        catch (XmlException)
        {
            return true;
        }

        string? ElementValue(string name) =>
            doc.Descendants(TaskNs + name).FirstOrDefault()?.Value.Trim();

        if (!string.Equals(ElementValue("LogonType"), "InteractiveToken", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ElementValue("RunLevel"), "LeastPrivilege", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ElementValue("MultipleInstancesPolicy"), "Parallel", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ElementValue("DisallowStartIfOnBatteries"), "false", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ElementValue("StopIfGoingOnBatteries"), "false", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ElementValue("AllowStartOnDemand"), "true", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ElementValue("Enabled"), "true", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ElementValue("Hidden"), "true", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ElementValue("ExecutionTimeLimit"), "PT0S", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        XElement? exec = doc.Descendants(TaskNs + "Exec").FirstOrDefault();
        if (exec is null)
        {
            return true;
        }

        string? actualCommand = exec.Element(TaskNs + "Command")?.Value.Trim();
        string? actualArguments = exec.Element(TaskNs + "Arguments")?.Value.Trim();
        string? actualWorkingDir = exec.Element(TaskNs + "WorkingDirectory")?.Value.Trim();

        string expectedArguments = BuildTaskActionArguments(desiredSupervisorExecutablePath);

        return !string.Equals(actualCommand, desiredConhostPath, StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(actualArguments, expectedArguments, StringComparison.Ordinal) ||
               !string.Equals(actualWorkingDir, desiredWorkingDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("windows")]
    private sealed class Win32TaskSchedulerService : IScheduledTaskService
    {
        private const int TaskCreateOrUpdate = 6;
        private const int TaskLogonInteractiveToken = 3;

        public string? GetTaskXml(string folderPath, string taskName)
        {
            dynamic service = ConnectService();
            dynamic? folder = null;
            dynamic? task = null;
            try
            {
                try
                {
                    folder = service.GetFolder(folderPath);
                }
                catch (Exception ex) when (IsNotFoundException(ex))
                {
                    return null;
                }

                try
                {
                    task = folder.GetTask(taskName);
                    return (string?)task.Xml;
                }
                catch (Exception ex) when (IsNotFoundException(ex))
                {
                    return null;
                }
            }
            finally
            {
                ReleaseComObject(task);
                ReleaseComObject(folder);
                ReleaseComObject(service);
            }
        }

        public void RegisterOrUpdateTask(string folderPath, string taskName, string taskXml)
        {
            dynamic service = ConnectService();
            dynamic? folder = null;
            dynamic? registeredTask = null;
            try
            {
                folder = GetOrCreateFolder(service, folderPath);
                registeredTask = folder.RegisterTask(
                    taskName,
                    taskXml,
                    TaskCreateOrUpdate,
                    null,
                    null,
                    TaskLogonInteractiveToken,
                    null);
            }
            finally
            {
                ReleaseComObject(registeredTask);
                ReleaseComObject(folder);
                ReleaseComObject(service);
            }
        }

        public int? RunTask(string folderPath, string taskName, string arguments)
        {
            dynamic service = ConnectService();
            dynamic? folder = null;
            dynamic? task = null;
            dynamic? runningTask = null;
            try
            {
                folder = service.GetFolder(folderPath);
                task = folder.GetTask(taskName);
                runningTask = task.Run(arguments);

                try
                {
                    uint enginePid = (uint)runningTask.EnginePID;
                    return enginePid > 0 ? unchecked((int)enginePid) : null;
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    System.Diagnostics.Trace.WriteLine($"[ScheduledTaskSupervisorLauncher] Failed to read EnginePID: {ex.Message}");
                    return null;
                }
            }
            finally
            {
                ReleaseComObject(runningTask);
                ReleaseComObject(task);
                ReleaseComObject(folder);
                ReleaseComObject(service);
            }
        }

        private static dynamic ConnectService()
        {
            Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType is null)
            {
                throw new InvalidOperationException("Windows Task Scheduler COM class 'Schedule.Service' is unavailable.");
            }

            dynamic service = Activator.CreateInstance(serviceType)
                ?? throw new InvalidOperationException("Failed to instantiate 'Schedule.Service' COM object.");
            service.Connect();
            return service;
        }

        private static dynamic GetOrCreateFolder(dynamic service, string folderPath)
        {
            try
            {
                return service.GetFolder(folderPath);
            }
            catch (Exception ex) when (IsNotFoundException(ex))
            {
                dynamic? root = null;
                try
                {
                    root = service.GetFolder(@"\");
                    string relativeName = folderPath.TrimStart('\\');
                    return root.CreateFolder(relativeName, null);
                }
                finally
                {
                    ReleaseComObject(root);
                }
            }
        }

        private static bool IsNotFoundException(Exception ex)
        {
            if (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }

            const int hrFileNotFound = unchecked((int)0x80070002);
            const int hrPathNotFound = unchecked((int)0x80070003);
            return ex is COMException comEx &&
                   (comEx.HResult == hrFileNotFound || comEx.HResult == hrPathNotFound);
        }

        private static void ReleaseComObject(object? obj)
        {
            if (obj is not null && Marshal.IsComObject(obj))
            {
                try
                {
                    Marshal.FinalReleaseComObject(obj);
                }
                catch (Exception ex) when (ex is COMException or InvalidComObjectException or ArgumentException)
                {
                    System.Diagnostics.Trace.WriteLine($"[ScheduledTaskSupervisorLauncher] Failed to release COM object: {ex.Message}");
                }
            }
        }
    }
}
