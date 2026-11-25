using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Synthetic.Features.MultiModelPdfExport.Models;
using Synthetic.Features.MultiModelPdfExport.Services;
using Synthetic.Platform.UI;

namespace Synthetic.Features.MultiModelPdfExport.ViewModels
{
    /// <summary>
    /// Selectable Print Set dropdown choice for a project row, including the safe first-run
    /// <c>&lt;Select Print Set...&gt;</c> placeholder sentinel.
    /// </summary>
    public sealed class MultiModelPdfPrintSetChoice
    {
        /// <summary>Display label for the unselected first-run placeholder entry.</summary>
        public const string PlaceholderDisplayName = "<Select Print Set...>";

        private MultiModelPdfPrintSetChoice(string? name, string displayName, bool isPlaceholder, MultiModelPdfSetSnapshot? snapshot)
        {
            Name = name;
            DisplayName = displayName;
            IsPlaceholder = isPlaceholder;
            Snapshot = snapshot;
        }

        /// <summary>Gets the underlying Print Set name, or <c>null</c> for the placeholder choice.</summary>
        public string? Name { get; }

        /// <summary>Gets the formatted dropdown option label.</summary>
        public string DisplayName { get; }

        /// <summary>Gets a value indicating whether this entry is the unselected placeholder.</summary>
        public bool IsPlaceholder { get; }

        /// <summary>Gets the associated Print Set snapshot, or <c>null</c> for the placeholder choice.</summary>
        public MultiModelPdfSetSnapshot? Snapshot { get; }

        /// <summary>Creates the unselected placeholder option.</summary>
        public static MultiModelPdfPrintSetChoice CreatePlaceholder() =>
            new MultiModelPdfPrintSetChoice(null, PlaceholderDisplayName, isPlaceholder: true, snapshot: null);

        /// <summary>Creates a Print Set option from a harvested snapshot.</summary>
        public static MultiModelPdfPrintSetChoice FromSnapshot(MultiModelPdfSetSnapshot setSnapshot)
        {
            if (setSnapshot == null) throw new ArgumentNullException(nameof(setSnapshot));

            string viewsLabel = setSnapshot.PrintableViewCount == 1 ? "1 view" : $"{setSnapshot.PrintableViewCount} views";
            string display = setSnapshot.SkippedViews.Count > 0
                ? $"{setSnapshot.Name} ({viewsLabel} · {setSnapshot.SkippedViews.Count} skip)"
                : $"{setSnapshot.Name} ({viewsLabel})";

            return new MultiModelPdfPrintSetChoice(setSnapshot.Name, display, isPlaceholder: false, setSnapshot);
        }
    }

    /// <summary>
    /// Row ViewModel representing one discovered Revit project inside <see cref="MultiModelPdfExportViewModel"/>.
    /// </summary>
    public sealed class MultiModelPdfExportRowViewModel : ViewModelBase
    {
        private readonly MultiModelPdfDocumentSnapshot _snapshot;
        private readonly Action? _onRowChanged;
        private readonly MultiModelPdfPrintSetChoice _placeholderChoice;
        private bool _isChecked;
        private MultiModelPdfPrintSetChoice _selectedPrintSetOption;
        private string _resolvedOutputToken;
        private bool _isCollisionDisambiguated;
        private string _collisionCueText = string.Empty;
        private string _collisionTooltipText = string.Empty;
        private bool _hasBulkMismatchAccent;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfExportRowViewModel"/> class.
        /// </summary>
        public MultiModelPdfExportRowViewModel(
            MultiModelPdfDocumentSnapshot snapshot,
            bool initialChecked = false,
            Action? onRowChanged = null)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _onRowChanged = onRowChanged;
            _placeholderChoice = MultiModelPdfPrintSetChoice.CreatePlaceholder();
            _resolvedOutputToken = MultiModelPdfPathHelper.SanitizeDocumentTitleToken(snapshot.DocumentTitle);

            var options = new List<MultiModelPdfPrintSetChoice>(snapshot.PrintSets.Count + 1)
            {
                _placeholderChoice
            };
            foreach (MultiModelPdfSetSnapshot set in snapshot.PrintSets)
            {
                options.Add(MultiModelPdfPrintSetChoice.FromSnapshot(set));
            }

            PrintSetOptions = new ReadOnlyCollection<MultiModelPdfPrintSetChoice>(options);
            _selectedPrintSetOption = _placeholderChoice;
            _isChecked = IsEligible && initialChecked;
        }

        /// <summary>Gets the deterministic document identity key.</summary>
        public string DocumentKey => _snapshot.DocumentKey;

        /// <summary>Gets the Revit document title.</summary>
        public string DocumentTitle => _snapshot.DocumentTitle;

        /// <summary>Gets the model display path.</summary>
        public string DisplayPath => _snapshot.DisplayPath;

        /// <summary>Gets the document session origin.</summary>
        public MultiModelPdfDocumentOrigin Origin => _snapshot.Origin;

        /// <summary>Gets the document backing storage kind.</summary>
        public MultiModelPdfStorageKind Storage => _snapshot.Storage;

        /// <summary>Gets a value indicating whether the row represents an unloaded link.</summary>
        public bool IsUnloadedLink => _snapshot.IsUnloadedLink;

        /// <summary>Gets all harvested Print Sets for this document.</summary>
        public IReadOnlyList<MultiModelPdfSetSnapshot> PrintSets => _snapshot.PrintSets;

        /// <summary>Gets a value indicating whether the document has at least one saved Print Set.</summary>
        public bool HasPrintSets => _snapshot.PrintSets.Count > 0;

        /// <summary>Gets a value indicating whether this project row can be selected and exported.</summary>
        public bool IsEligible => !IsUnloadedLink && HasPrintSets;

        /// <summary>Gets a value indicating whether this row is rendered in the disabled 25% opacity state.</summary>
        public bool IsDisabledRow => !IsEligible;

        /// <summary>Gets the visual row opacity (<c>0.25</c> when disabled, <c>1.0</c> otherwise).</summary>
        public double RowOpacity => IsDisabledRow ? 0.25 : 1.0;

        /// <summary>Gets the uppercase origin badge label.</summary>
        public string OriginBadgeText => Origin switch
        {
            MultiModelPdfDocumentOrigin.Active => "ACTIVE",
            MultiModelPdfDocumentOrigin.Linked => "LINKED",
            _ => "OPEN"
        };

        /// <summary>Gets whether this row is the active project.</summary>
        public bool IsActiveOrigin => Origin == MultiModelPdfDocumentOrigin.Active;

        /// <summary>Gets whether this row is a linked model.</summary>
        public bool IsLinkedOrigin => Origin == MultiModelPdfDocumentOrigin.Linked;

        /// <summary>Gets whether this row is an open standalone project.</summary>
        public bool IsOpenOrigin => Origin == MultiModelPdfDocumentOrigin.OpenStandalone;

        /// <summary>Gets the storage classification display label.</summary>
        public string StorageDisplayText => Storage switch
        {
            MultiModelPdfStorageKind.Workshared => "Workshared",
            MultiModelPdfStorageKind.CloudAcc => "Cloud (ACC)",
            MultiModelPdfStorageKind.Local => "Local",
            _ => "Unsaved"
        };

        /// <summary>Gets the status message shown in place of the Print Set dropdown when disabled.</summary>
        public string DisabledStatusText => IsUnloadedLink
            ? "Unloaded link — load in host to inspect"
            : "No saved Print Sets";

        /// <summary>Gets the hover tooltip for the project row.</summary>
        public string RowTooltipText
        {
            get
            {
                if (IsUnloadedLink)
                {
                    return "Unloaded link — load in host to inspect.";
                }

                if (!HasPrintSets)
                {
                    return $"{DocumentTitle} has no saved Print Sets. Save at least one Print Set in that model to export it.";
                }

                string baseTip = string.IsNullOrWhiteSpace(DisplayPath) ? DocumentTitle : DisplayPath;
                return IsCollisionDisambiguated && !string.IsNullOrWhiteSpace(CollisionTooltipText)
                    ? $"{baseTip}\n{CollisionTooltipText}"
                    : baseTip;
            }
        }

        /// <summary>Gets the resolved collision-free output filename token (without <c>.pdf</c> extension).</summary>
        public string ResolvedOutputToken
        {
            get => _resolvedOutputToken;
            private set => SetProperty(ref _resolvedOutputToken, value);
        }

        /// <summary>Gets whether this row's output token was disambiguated due to a title collision.</summary>
        public bool IsCollisionDisambiguated
        {
            get => _isCollisionDisambiguated;
            private set => SetProperty(ref _isCollisionDisambiguated, value);
        }

        /// <summary>Gets the muted inline collision cue text (e.g., <c>→ Tower_2.pdf</c> or <c>→ Tower_2\</c>).</summary>
        public string CollisionCueText
        {
            get => _collisionCueText;
            private set => SetProperty(ref _collisionCueText, value);
        }

        /// <summary>Gets the hover tooltip explaining title-collision disambiguation.</summary>
        public string CollisionTooltipText
        {
            get => _collisionTooltipText;
            private set => SetProperty(ref _collisionTooltipText, value);
        }

        /// <summary>Gets the dropdown options including the leading placeholder choice.</summary>
        public IReadOnlyList<MultiModelPdfPrintSetChoice> PrintSetOptions { get; }

        /// <summary>Gets whether this row currently shows the transient Sand Gold bulk mismatch left stripe.</summary>
        public bool HasBulkMismatchAccent
        {
            get => _hasBulkMismatchAccent;
            private set => SetProperty(ref _hasBulkMismatchAccent, value);
        }

        /// <summary>
        /// Gets or sets whether this project is checked for export.
        /// Ineligible rows always remain unchecked.
        /// </summary>
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                bool normalized = IsEligible && value;
                if (SetProperty(ref _isChecked, normalized))
                {
                    if (!normalized)
                    {
                        SetProperty(ref _hasBulkMismatchAccent, false, nameof(HasBulkMismatchAccent));
                    }

                    RaiseDerivedSelectionProperties();
                    _onRowChanged?.Invoke();
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected Print Set dropdown item.
        /// </summary>
        public MultiModelPdfPrintSetChoice SelectedPrintSetOption
        {
            get => _selectedPrintSetOption;
            set
            {
                if (value == null)
                {
                    return;
                }

                if (!ReferenceEquals(_selectedPrintSetOption, value))
                {
                    _selectedPrintSetOption = value;
                    SetProperty(ref _hasBulkMismatchAccent, false, nameof(HasBulkMismatchAccent));
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedPrintSetName));
                    RaiseDerivedSelectionProperties();
                    _onRowChanged?.Invoke();
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected Print Set name, or <c>null</c> when <c>&lt;Select Print Set...&gt;</c> is active.
        /// </summary>
        public string? SelectedPrintSetName
        {
            get => _selectedPrintSetOption.IsPlaceholder ? null : _selectedPrintSetOption.Name;
            set
            {
                MultiModelPdfPrintSetChoice match = _placeholderChoice;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    string target = value!.Trim();
                    foreach (MultiModelPdfPrintSetChoice option in PrintSetOptions)
                    {
                        if (!option.IsPlaceholder &&
                            string.Equals(option.Name, target, StringComparison.OrdinalIgnoreCase))
                        {
                            match = option;
                            break;
                        }
                    }
                }

                SelectedPrintSetOption = match;
            }
        }

        /// <summary>Gets the selected Print Set snapshot, or <c>null</c> if none is selected.</summary>
        public MultiModelPdfSetSnapshot? SelectedSet => _selectedPrintSetOption.Snapshot;

        /// <summary>Gets whether the row is checked and missing a Print Set selection.</summary>
        public bool HasMissingPrintSet => IsEligible && IsChecked && SelectedSet == null;

        /// <summary>Gets whether the row is checked with a Print Set yielding 0 printable views.</summary>
        public bool HasZeroPrintableViews => IsEligible && IsChecked && SelectedSet != null && SelectedSet.PrintableViewCount == 0;

        /// <summary>Gets whether the row is currently in a blocking validation error state.</summary>
        public bool HasRowError => HasMissingPrintSet || HasZeroPrintableViews;

        /// <summary>Gets whether the Views column should show the normal printable count.</summary>
        public bool HasNormalViewCount => IsEligible && SelectedSet != null && !HasRowError;

        /// <summary>Gets whether the Views column should display a neutral dash.</summary>
        public bool ShowDashViewsPlaceholder => !IsEligible || (!HasRowError && SelectedSet == null);

        /// <summary>Gets the printable view count for the currently selected Print Set.</summary>
        public int PrintableViewCount => SelectedSet?.PrintableViewCount ?? 0;

        /// <summary>Gets the skipped view count for the currently selected Print Set.</summary>
        public int SkippedViewCount => SelectedSet?.SkippedViews.Count ?? 0;

        /// <summary>Gets the formatted Views column text for valid sets.</summary>
        public string ViewsDisplayText => SelectedSet == null
            ? "—"
            : (SelectedSet.PrintableViewCount == 1 ? "1 view" : $"{SelectedSet.PrintableViewCount} views");

        /// <summary>Gets whether the selected Print Set has skipped items to warn about.</summary>
        public bool HasSkippedViews => IsEligible && SelectedSet != null && SelectedSet.SkippedViews.Count > 0 && !HasZeroPrintableViews;

        /// <summary>Gets the Sand Gold skip badge label.</summary>
        public string SkippedBadgeText => $"{SkippedViewCount} skip";

        /// <summary>Gets the hover tooltip listing up to 8 skipped views/sheets.</summary>
        public string SkippedTooltipText
        {
            get
            {
                if (SelectedSet == null || SelectedSet.SkippedViews.Count == 0)
                {
                    return string.Empty;
                }

                int total = SelectedSet.SkippedViews.Count;
                var sb = new StringBuilder();
                sb.Append(total)
                  .Append(" view/sheet")
                  .Append(total == 1 ? string.Empty : "s")
                  .Append(" will be skipped in ")
                  .Append(DocumentTitle)
                  .AppendLine(":");

                int limit = Math.Min(8, total);
                for (int i = 0; i < limit; i++)
                {
                    MultiModelPdfSkippedViewInfo item = SelectedSet.SkippedViews[i];
                    sb.Append("• ");
                    if (!string.IsNullOrWhiteSpace(item.DisplayCode))
                    {
                        sb.Append(item.DisplayCode).Append(' ');
                    }

                    sb.Append(item.ViewName);
                    if (!string.IsNullOrWhiteSpace(item.SkipReason))
                    {
                        sb.Append(" (").Append(item.SkipReason).Append(')');
                    }

                    if (i < limit - 1 || total > 8)
                    {
                        sb.AppendLine();
                    }
                }

                if (total > 8)
                {
                    sb.Append($"+ {total - 8} more");
                }

                return sb.ToString();
            }
        }

        internal void UpdateCollisionResolution(MultiModelPdfResolvedToken? resolved, MultiModelPdfFileMode fileMode)
        {
            string fallbackToken = MultiModelPdfPathHelper.SanitizeDocumentTitleToken(DocumentTitle);
            string nextToken = resolved?.FinalToken ?? fallbackToken;
            bool nextDisambiguated = resolved != null && resolved.IsCollisionDisambiguated;

            string nextCue = string.Empty;
            string nextTooltip = string.Empty;
            if (nextDisambiguated)
            {
                string targetLabel = fileMode == MultiModelPdfFileMode.CombinedPerDocument
                    ? $"{nextToken}.pdf"
                    : $"{nextToken}\\";
                nextCue = $"→ {targetLabel}";
                string pathPart = string.IsNullOrWhiteSpace(DisplayPath) ? string.Empty : $" ({DisplayPath})";
                nextTooltip = $"Disambiguated duplicate document title \"{DocumentTitle}\"{pathPart} → {targetLabel}";
            }

            bool changed = false;
            changed |= SetProperty(ref _resolvedOutputToken, nextToken, nameof(ResolvedOutputToken));
            changed |= SetProperty(ref _isCollisionDisambiguated, nextDisambiguated, nameof(IsCollisionDisambiguated));
            changed |= SetProperty(ref _collisionCueText, nextCue, nameof(CollisionCueText));
            changed |= SetProperty(ref _collisionTooltipText, nextTooltip, nameof(CollisionTooltipText));
            if (changed)
            {
                OnPropertyChanged(nameof(RowTooltipText));
            }
        }

        internal bool SetCheckedBatch(bool value)
        {
            bool normalized = IsEligible && value;
            if (_isChecked == normalized)
            {
                return false;
            }

            _isChecked = normalized;
            if (!normalized)
            {
                SetProperty(ref _hasBulkMismatchAccent, false, nameof(HasBulkMismatchAccent));
            }

            OnPropertyChanged(nameof(IsChecked));
            RaiseDerivedSelectionProperties();
            return true;
        }

        internal void ApplyBulkMatchedChoice(MultiModelPdfPrintSetChoice matchChoice)
        {
            SetProperty(ref _hasBulkMismatchAccent, false, nameof(HasBulkMismatchAccent));
            if (!ReferenceEquals(_selectedPrintSetOption, matchChoice))
            {
                _selectedPrintSetOption = matchChoice;
                OnPropertyChanged(nameof(SelectedPrintSetOption));
                OnPropertyChanged(nameof(SelectedPrintSetName));
                RaiseDerivedSelectionProperties();
            }
        }

        internal bool TryApplyMatchingPrintSet(string printSetName)
        {
            if (string.IsNullOrWhiteSpace(printSetName))
            {
                return false;
            }

            string target = printSetName.Trim();
            foreach (MultiModelPdfPrintSetChoice option in PrintSetOptions)
            {
                if (!option.IsPlaceholder &&
                    string.Equals(option.Name, target, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyBulkMatchedChoice(option);
                    return true;
                }
            }

            return false;
        }

        internal void SetBulkMismatchAccent(bool hasMismatch)
        {
            bool normalized = IsEligible && IsChecked && hasMismatch;
            SetProperty(ref _hasBulkMismatchAccent, normalized, nameof(HasBulkMismatchAccent));
        }

        private void RaiseDerivedSelectionProperties()
        {
            OnPropertyChanged(nameof(SelectedSet));
            OnPropertyChanged(nameof(HasMissingPrintSet));
            OnPropertyChanged(nameof(HasZeroPrintableViews));
            OnPropertyChanged(nameof(HasRowError));
            OnPropertyChanged(nameof(HasNormalViewCount));
            OnPropertyChanged(nameof(ShowDashViewsPlaceholder));
            OnPropertyChanged(nameof(PrintableViewCount));
            OnPropertyChanged(nameof(SkippedViewCount));
            OnPropertyChanged(nameof(ViewsDisplayText));
            OnPropertyChanged(nameof(HasSkippedViews));
            OnPropertyChanged(nameof(SkippedBadgeText));
            OnPropertyChanged(nameof(SkippedTooltipText));
        }
    }
}
