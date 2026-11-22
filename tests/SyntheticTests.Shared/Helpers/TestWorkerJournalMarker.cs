using System;
using System.Threading;
using Autodesk.Revit.UI;

namespace SyntheticTests.Helpers
{
    /// <summary>
    /// Writes a once-per-process <c>SYNTHETIC_TEST_RUN:&lt;RunId&gt;</c> comment into the active
    /// Revit journal as an idempotent fallback when bootstrap manifests are not deployed.
    /// </summary>
    public static class TestWorkerJournalMarker
    {
        private static int _written;

        /// <summary>
        /// Writes <c>SYNTHETIC_TEST_RUN:&lt;RunId&gt;</c> to the Revit journal once per process when
        /// <c>SYNTHETIC_TEST_RUN_ID</c> is set and <paramref name="uiapp"/> is available.
        /// </summary>
        public static void EnsureWritten(UIApplication? uiapp)
        {
            if (uiapp?.Application == null)
            {
                return;
            }

            string? runId = Environment.GetEnvironmentVariable("SYNTHETIC_TEST_RUN_ID");
            if (string.IsNullOrWhiteSpace(runId))
            {
                return;
            }

            if (Interlocked.Exchange(ref _written, 1) != 0)
            {
                return;
            }

            try
            {
                uiapp.Application.WriteJournalComment($"SYNTHETIC_TEST_RUN:{runId!.Trim()}", true);
            }
            catch
            {
                // Best-effort fallback marker; never fail test fixture setup on journal I/O
            }
        }
    }
}
