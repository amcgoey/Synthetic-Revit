using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RevitWorkerPool.Launching;
using ricaun.NUnit.Models;

namespace RevitWorkerPool.Pipe;

/// <summary>
/// Result returned from a single named-pipe test execution session.
/// </summary>
public sealed record PipeTestExecutionResult(
    TestAssemblyModel? AssemblyResult,
    IReadOnlyList<TestModel> StreamedTests,
    string? Info);

/// <summary>
/// Contract for connecting to a Revit worker named pipe, gating readiness on
/// <c>ServerMessage.IsBusy == false</c>, and executing a test request.
/// </summary>
public interface IPipeTestClient : IAsyncDisposable, IDisposable
{
    bool IsConnected { get; }

    event Action? Disconnected;

    Task ConnectAndWaitForReadyAsync(
        LaunchedWorkerProcess worker,
        TimeSpan bootTimeout,
        CancellationToken cancellationToken = default);

    Task<PipeTestExecutionResult> ExecuteTestsAsync(
        TestRequest request,
        LaunchedWorkerProcess worker,
        TimeSpan executionTimeout,
        Action<TestModel>? onTestCompleted = null,
        CancellationToken cancellationToken = default);
}
