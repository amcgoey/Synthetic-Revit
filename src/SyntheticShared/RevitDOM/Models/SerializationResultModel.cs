using System;
using System.Collections.Generic;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Represents the outcome of a single ObjectModel serialization attempt during a batch import.
    /// </summary>
    public class SerializationResultModel
    {
        #region Public Properties

        /// <summary>
        /// Gets the model that was processed.
        /// </summary>
        public ObjectModel Model { get; }

        /// <summary>
        /// Gets a value indicating whether the serialization/import succeeded.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Gets the error message if the import failed; otherwise, null.
        /// </summary>
        public string? ErrorMessage { get; }

        /// <summary>
        /// Gets the Exception object if the import failed; otherwise, null.
        /// </summary>
        public Exception? Exception { get; }

        /// <summary>
        /// Gets the element representation model (identity) of the created or modified element, if successful.
        /// </summary>
        public ElementIdModel? ElementIdentity { get; }

        /// <summary>
        /// Gets the warnings encountered during operation.
        /// </summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>
        /// Gets or sets the target of the operation (e.g. "Database", "File").
        /// </summary>
        public string OperationTarget { get; set; } = "Database";

        /// <summary>
        /// Gets or sets the action performed (e.g. "Merged Alias", "Alias Swap Failed").
        /// </summary>
        public string? Action { get; set; }

        /// <summary>
        /// Gets or sets a descriptive message detailing the operation's outcome.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Gets or sets the redirection result containing telemetry, counts, and warnings from an alias swap operation.
        /// </summary>
        public RedirectionResultModel? RedirectionResult { get; set; }

        #endregion

        #region Thread-Local Warning Context

        [ThreadStatic]
        private static List<string>? _currentThreadWarnings;

        public static List<string> CurrentThreadWarnings
        {
            get
            {
                if (_currentThreadWarnings == null)
                {
                    _currentThreadWarnings = new List<string>();
                }
                return _currentThreadWarnings;
            }
        }

        public static void LogWarning(string warning)
        {
            CurrentThreadWarnings.Add(warning);
        }

        public static void ClearWarnings()
        {
            CurrentThreadWarnings.Clear();
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializationResultModel"/> class for a successful operation.
        /// </summary>
        /// <param name="model">The processed model.</param>
        /// <param name="elementIdentity">Optional identity of the resolved, created, or modified element.</param>
        public SerializationResultModel(ObjectModel model, ElementIdModel? elementIdentity = null)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Success = true;
            ErrorMessage = null;
            Exception = null;
            ElementIdentity = elementIdentity;
            Warnings.AddRange(CurrentThreadWarnings);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializationResultModel"/> class for a failed operation.
        /// </summary>
        /// <param name="model">The processed model.</param>
        /// <param name="errorMessage">The error message describing the failure.</param>
        /// <param name="exception">Optional exception that caused the failure.</param>
        public SerializationResultModel(ObjectModel model, string errorMessage, Exception? exception = null)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Success = false;
            ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
            Exception = exception;
            ElementIdentity = null;
            Warnings.AddRange(CurrentThreadWarnings);
        }

        #endregion
    }
}
