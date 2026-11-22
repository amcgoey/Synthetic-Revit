using System;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// Wrapper ViewModel for deep-copied elements staged in the Staging Queue.
    /// </summary>
    public class QueueItemModel : ViewModelBase
    {
        private bool _willEnforce;
        private bool _willSave;
        private bool _isEdited;
        private bool _isDiffed;

        /// <summary>
        /// Gets the immutable historical snapshot at the moment of queuing.
        /// </summary>
        public ObjectModel BaselineModel { get; }

        /// <summary>
        /// Gets the active, mutable state of the staged element.
        /// </summary>
        public ObjectModel TargetModel { get; private set; }

        /// <summary>
        /// Gets the underlying cloned standard object (redirects to TargetModel).
        /// </summary>
        public ObjectModel Model => TargetModel;

        /// <summary>
        /// Gets the display name of the staged element.
        /// </summary>
        public string Name
        {
            get
            {
                if (Model is ElementModel elementModel)
                {
                    return elementModel.Name ?? string.Empty;
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets the class name / category of the element used for UI grouping.
        /// </summary>
        public string ClassName
        {
            get
            {
                if (Model is ElementModel elementModel)
                {
                    return elementModel.Class ?? "Unknown Class";
                }
                return Model.GetType().Name;
            }
        }

        /// <summary>
        /// Gets the category of the element.
        /// </summary>
        public string Category
        {
            get
            {
                if (Model is ElementModel elementModel)
                {
                    return elementModel.Category ?? string.Empty;
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets or sets whether this item will be enforced in the Revit database.
        /// </summary>
        public bool WillEnforce
        {
            get => _willEnforce;
            set => SetProperty(ref _willEnforce, value);
        }

        /// <summary>
        /// Gets or sets whether this item will be saved to a JSON standards file.
        /// </summary>
        public bool WillSave
        {
            get => _willSave;
            set => SetProperty(ref _willSave, value);
        }

        /// <summary>
        /// Gets or sets whether this item has been edited.
        /// </summary>
        public bool IsEdited
        {
            get => _isEdited;
            set => SetProperty(ref _isEdited, value);
        }

        /// <summary>
        /// Gets or sets whether this item has been diffed.
        /// </summary>
        public bool IsDiffed
        {
            get => _isDiffed;
            set => SetProperty(ref _isDiffed, value);
        }

        private string? _errorMessage;

        /// <summary>
        /// Gets or sets the execution error message if this item failed database or file operations.
        /// </summary>
        public string? ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether this item has an execution error.
        /// </summary>
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        private string? _dependencyOrigin;

        /// <summary>
        /// Gets or sets the name of the parent element that triggered the harvesting of this dependency.
        /// </summary>
        public string? DependencyOrigin
        {
            get => _dependencyOrigin;
            set
            {
                if (SetProperty(ref _dependencyOrigin, value))
                {
                    OnPropertyChanged(nameof(IsDependency));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether this item was harvested as a dependency.
        /// </summary>
        public bool IsDependency => !string.IsNullOrEmpty(DependencyOrigin);

        /// <summary>
        /// Initializes a new instance of the <see cref="QueueItemModel"/> class with explicit execution flags.
        /// </summary>
        /// <param name="model">The original model POCO.</param>
        /// <param name="willEnforce">True to enforce this item.</param>
        /// <param name="willSave">True to save this item.</param>
        public QueueItemModel(ObjectModel model, bool willEnforce, bool willSave)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            // Deep clone using the runtime type of the model to preserve subclasses
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(model, Newtonsoft.Json.Formatting.None);

            BaselineModel = (ObjectModel)Newtonsoft.Json.JsonConvert.DeserializeObject(json, model.GetType())!;
            TargetModel = (ObjectModel)Newtonsoft.Json.JsonConvert.DeserializeObject(json, model.GetType())!;

            WillEnforce = willEnforce;
            WillSave = willSave;
        }

        /// <summary>
        /// Generates a fresh ElementTypeWrapperVM on-the-fly.
        /// </summary>
        public ElementTypeWrapperVM GetWrapper()
        {
            if (TargetModel is ElementModel targetElement)
            {
                var baselineElement = BaselineModel as ElementModel;
                return new ElementTypeWrapperVM(targetElement, baselineElement);
            }
            throw new InvalidOperationException("Model is not an ElementModel.");
        }

        /// <summary>
        /// Reverts the mutable TargetModel to match the immutable BaselineModel.
        /// </summary>
        public void Revert()
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(BaselineModel, Newtonsoft.Json.Formatting.None);
            TargetModel = (ObjectModel)Newtonsoft.Json.JsonConvert.DeserializeObject(json, BaselineModel.GetType())!;
            OnPropertyChanged(nameof(TargetModel));
            OnPropertyChanged(nameof(Model));
            OnPropertyChanged(nameof(Name));
        }

        /// <summary>
        /// Raises a property changed notification for a given property.
        /// </summary>
        public void RaisePropertyChanged(string propertyName)
        {
            OnPropertyChanged(propertyName);
        }
    }
}
