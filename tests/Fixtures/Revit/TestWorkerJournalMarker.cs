using System;
using System.Threading;
using Autodesk.Revit.UI;

namespace SyntheticTests.Helpers
{
    /// <summary>
    /// Writes once-per-process <c>SYNTHETIC_TEST_RUN:&lt;RunId&gt;</c> and
    /// <c>SYNTHETIC_WORKER_ID:&lt;WorkerId&gt;</c> comments into the active Revit journal
    /// as an idempotent fallback when bootstrap manifests are not deployed.
    /// </summary>
    public static class TestWorkerJournalMarker
    {
        private static int _runIdWritten;
        private static int _workerIdWritten;

        /// <summary>
        /// Writes <c>SYNTHETIC_TEST_RUN:&lt;RunId&gt;</c> and
        /// <c>SYNTHETIC_WORKER_ID:&lt;WorkerId&gt;</c> to the Revit journal once per process
        /// when the corresponding environment variables are set and <paramref name="uiapp"/> is available.
        /// </summary>
        public static void EnsureWritten(UIApplication? uiapp)
        {
            if (uiapp?.Application == null)
            {
                return;
            }

            TryWriteMarkerOnce(uiapp, "SYNTHETIC_TEST_RUN_ID", "SYNTHETIC_TEST_RUN", ref _runIdWritten);
            TryWriteMarkerOnce(uiapp, "SYNTHETIC_WORKER_ID", "SYNTHETIC_WORKER_ID", ref _workerIdWritten);
        }

        private static void TryWriteMarkerOnce(
            UIApplication uiapp,
            string envVarName,
            string markerPrefix,
            ref int writtenFlag)
        {
            string? value = Environment.GetEnvironmentVariable(envVarName);
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (Interlocked.Exchange(ref writtenFlag, 1) != 0)
            {
                return;
            }

            try
            {
                uiapp.Application.WriteJournalComment($"{markerPrefix}:{value!.Trim()}", true);
            }
            catch
            {
                // Best-effort fallback marker; never fail test fixture setup on journal I/O
            }
        }
    }
}
