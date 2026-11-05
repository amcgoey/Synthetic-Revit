namespace Synthetic.Modules.SharedCoordinates.Models
{
    /// <summary>
    /// Represents an individual linked model instance and its alignment status with its assigned Shared Site.
    /// </summary>
    public class LinkRepositionInstanceItem
    {
        /// <summary>
        /// Gets or sets the unique identifier of the RevitLinkInstance element.
        /// </summary>
        public string InstanceId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the RevitLinkInstance element.
        /// </summary>
        public string InstanceName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the unique identifier of the parent RevitLinkType.
        /// </summary>
        public string TypeId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the parent RevitLinkType.
        /// </summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the assigned Level-Based Shared Site name (e.g. "Floor 3" or "&lt;Not Shared&gt;").
        /// </summary>
        public string AssignedSiteName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether a valid shared site is assigned to this instance.
        /// False when the parameter is empty, unassigned, or "&lt;Not Shared&gt;".
        /// </summary>
        public bool IsAssigned { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this link instance is pinned.
        /// </summary>
        public bool IsPinned { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this link instance's current placement transform is out of sync with its expected Shared Site transform.
        /// </summary>
        public bool IsOutOfSync { get; set; }

        /// <summary>
        /// Gets or sets the rotation delta around the Z axis in radians.
        /// </summary>
        public double DeltaAngle { get; set; }

        /// <summary>
        /// Gets or sets the translation delta along the X axis in feet.
        /// </summary>
        public double DeltaX { get; set; }

        /// <summary>
        /// Gets or sets the translation delta along the Y axis in feet.
        /// </summary>
        public double DeltaY { get; set; }

        /// <summary>
        /// Gets or sets the translation delta along the Z axis in feet.
        /// </summary>
        public double DeltaZ { get; set; }

        /// <summary>
        /// Gets or sets the Euclidean length of the 3D translation delta vector in feet.
        /// </summary>
        public double DeltaTranslationLength { get; set; }
    }
}
