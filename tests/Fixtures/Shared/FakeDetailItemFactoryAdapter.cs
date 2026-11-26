using System;
using System.Collections.Generic;
using System.Threading;
using Synthetic.Features.DetailItemFactory.Adapters;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Platform.UI;

namespace SyntheticTests.Fixtures
{
    /// <summary>
    /// In-memory test double for <see cref="IDetailItemFactoryAdapter"/> supporting headless Logic Tests
    /// and WPF smoke tests.
    /// </summary>
    public sealed class FakeDetailItemFactoryAdapter : IDetailItemFactoryAdapter
    {
        public DetailItemSessionSnapshot Snapshot { get; set; } = CreateDefaultSnapshot();
        public int CaptureCallCount { get; private set; }
        public List<DetailItemBatchPlan> ExecutedPlans { get; } = new List<DetailItemBatchPlan>();
        public Exception? ExceptionToThrow { get; set; }
        public string? FailureErrorMessage { get; set; }
        public int CancelAfterItemCount { get; set; } = -1;
        public CancellationTokenSource? CancelSourceToTrigger { get; set; }
        public Func<DetailItemPlannedElement, DetailItemResultItem>? ItemResultProvider { get; set; }

        public DetailItemSessionSnapshot CaptureSessionSnapshot()
        {
            CaptureCallCount++;
            return Snapshot;
        }

        public DetailItemBatchOutcome ExecuteBatch(
            DetailItemBatchPlan plan,
            IProgress<ProgressState>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            ExecutedPlans.Add(plan);

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            if (!string.IsNullOrWhiteSpace(FailureErrorMessage))
            {
                return DetailItemBatchOutcome.Failure(plan.OutputFolder, FailureErrorMessage!);
            }

            var results = new List<DetailItemResultItem>();
            int total = plan.Items.Count;
            bool wasCancelled = false;

            for (int i = 0; i < total; i++)
            {
                if (CancelAfterItemCount >= 0 && i >= CancelAfterItemCount && CancelSourceToTrigger != null)
                {
                    CancelSourceToTrigger.Cancel();
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    wasCancelled = true;
                    for (int j = i; j < total; j++)
                    {
                        DetailItemPlannedElement cancelled = plan.Items[j];
                        results.Add(new DetailItemResultItem
                        {
                            ElementName = cancelled.DisplayName,
                            ElementId = cancelled.ElementId,
                            Status = DetailItemResultStatus.Skipped,
                            Message = "Cancelled by user."
                        });
                        Report(progress, "Cancelled by user...", cancelled.DisplayName, j + 1, total);
                    }
                    break;
                }

                DetailItemPlannedElement item = plan.Items[i];
                int progressIndex = i + 1;

                if (item.Disposition != DetailItemPlanDisposition.Execute)
                {
                    results.Add(new DetailItemResultItem
                    {
                        ElementName = $"{item.CleanFamilyName} - {item.CleanTypeName}",
                        ElementId = item.ElementId,
                        Status = DetailItemResultStatus.Skipped,
                        Message = item.SkipReason ?? "Skipped."
                    });
                    Report(progress, $"Skipped element {progressIndex} of {total}", item.DisplayName, progressIndex, total);
                    continue;
                }

                DetailItemResultItem result = ItemResultProvider != null
                    ? ItemResultProvider(item)
                    : new DetailItemResultItem
                    {
                        ElementName = $"{item.CleanFamilyName} - {item.CleanTypeName}",
                        ElementId = item.ElementId,
                        Status = DetailItemResultStatus.Success,
                        Message = "Successfully generated and loaded family."
                    };

                results.Add(result);
                Report(progress, $"Processed element {progressIndex} of {total}", item.DisplayName, progressIndex, total);
            }

            return DetailItemBatchOutcome.Completed(plan.OutputFolder, results, wasCancelled);
        }

        private static void Report(
            IProgress<ProgressState>? progress,
            string taskDescription,
            string itemName,
            int progressIndex,
            int total)
        {
            progress?.Report(new ProgressState
            {
                TaskDescription = taskDescription,
                CurrentItemName = itemName,
                ProgressIndex = progressIndex,
                MaximumBounds = Math.Max(1, total)
            });
        }

        private static DetailItemSessionSnapshot CreateDefaultSnapshot()
        {
            return new DetailItemSessionSnapshot(
                activeViewUniqueId: "view-plan-1",
                activeViewName: "Level 1",
                activeViewKind: DetailItemSourceViewKind.Plan,
                isActiveViewSupported: true,
                defaultOutputFolder: @"C:\Temp\DetailItems",
                settings: new DetailItemFactorySettings().Defaults(),
                availableSubcategories: new[] { "Detail Items", "Thin Lines", "Heavy Lines" },
                elements: new[]
                {
                    new DetailItemSessionElement(
                        elementUniqueId: "elem-1",
                        elementId: "1001",
                        categoryName: "Doors",
                        familyName: "Single-Flush",
                        typeName: "36\" x 84\""),
                    new DetailItemSessionElement(
                        elementUniqueId: "elem-2",
                        elementId: "1002",
                        categoryName: "Furniture",
                        familyName: "Desk",
                        typeName: "60\" x 30\"")
                });
        }
    }
}
