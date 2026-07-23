using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Model that acts as a wrapper for Revit ElementIds to facilitate JSON serialization.
    /// Supports resolving element instances by UniqueId, Id, Name, or Aliases.
    /// </summary>
    public class ElementIdModel : ObjectModel, IEquatable<ElementIdModel>
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets the class name of the referenced Revit element.
        /// </summary>
        public string Class { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the category name of the referenced Revit element.
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Name of the element the Id belongs too.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the list of name aliases to search if the primary Name is not found.
        /// </summary>
        public List<string>? Aliases { get; set; }

        /// <summary>
        /// Value of the Element Id as an int
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the unique string identifier of the element.
        /// </summary>
        public string? UniqueId { get; set; }

        /// <summary>
        /// If true, SerialElement is intended to be deserialized as a template for use as standards or transfer to another project.
        /// If false, SerialElement is intended to modify an element inside the project and will include ElementIds and UniqueIds.
        /// </summary>
        [JsonIgnoreAttribute]
        public bool IsTemplate { get; set; }

        #endregion
        #region Conditional Serialization Methods for Properties

        /// <summary>
        /// Conditional serialization method for the Element Id.
        /// If IsTemplate is true, excludes the ID unless it is a built-in Revit element ID (represented by negative values).
        /// This ensures built-in/system IDs (e.g. built-in categories or solid line patterns) are preserved across document transfers.
        /// </summary>
        /// <returns>True if the Id should be serialized; otherwise, false.</returns>
        public bool ShouldSerializeId()
        {
            return !IsTemplate || Id < 0;
        }

        /// <summary>
        /// If IsTemplate, don't serialize the Unqiue Id
        /// </summary>
        /// <returns>True if not a template</returns>
        public bool ShouldSerializeUniqueId()
        {
            return !IsTemplate;
        }

        #endregion
        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the ElementIdModel class.
        /// </summary>
        public ElementIdModel ()
        {
            this.IsTemplate = false;
        }

        /// <summary>
        /// Initializes a new instance of the ElementIdModel class.
        /// </summary>
        /// <param name="IsTemplate">Flag indicating if this model is a template.</param>
        public ElementIdModel(bool IsTemplate)
        {
            this.IsTemplate = IsTemplate;
        }

        /// <summary>
        /// Initializes a new instance of the ElementIdModel class.
        /// </summary>
        /// <param name="Name">Name of the element.</param>
        /// <param name="ElementId">Revit element ID integer.</param>
        /// <param name="Class">Class name of the element.</param>
        /// <param name="Category">Category name of the element.</param>
        public ElementIdModel (string Name, int ElementId, string Class, string Category)
        {
            this.Id = ElementId;
            this.Name = Name;
            this.Class = Class;
            this.Category = Category;
            this.IsTemplate = false;
        }

        #endregion
        #region Equality and Hashing

        /// <summary>
        /// Determines whether the specified object is equal to the current <see cref="ElementIdModel"/>
        /// using the 5-step fallback identity strategy.
        /// </summary>
        public override bool Equals(object? obj)
        {
            return Equals(obj as ElementIdModel);
        }

        /// <summary>
        /// Indicates whether the current <see cref="ElementIdModel"/> is equal to another <see cref="ElementIdModel"/>
        /// using the 5-step fallback identity strategy (UniqueId -> Id -> Class Guard -> Name + Class/Category -> Aliases).
        /// </summary>
        public bool Equals(ElementIdModel? other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            // Step 3: Type/Class Guard verification
            // If both specify a non-empty Class and they differ, they represent different types.
            if (!string.IsNullOrEmpty(this.Class) &&
                !string.IsNullOrEmpty(other.Class) &&
                !string.Equals(this.Class, other.Class, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Step 1: UniqueId match
            if (!string.IsNullOrEmpty(this.UniqueId) && !string.IsNullOrEmpty(other.UniqueId))
            {
                return string.Equals(this.UniqueId, other.UniqueId, StringComparison.OrdinalIgnoreCase);
            }

            // Step 2: Id integer match (preserving built-in negative IDs, e.g. -2000100)
            if (this.Id == other.Id)
            {
                if (this.Id != 0 && this.Id != -1)
                {
                    return true;
                }

                // If Id is 0 or -1 (default / invalid element ID), match only if both models are default/invalid empty models.
                bool thisIsEmpty = string.IsNullOrEmpty(this.UniqueId) && string.IsNullOrEmpty(this.Name) && (this.Aliases == null || this.Aliases.Count == 0);
                bool otherIsEmpty = string.IsNullOrEmpty(other.UniqueId) && string.IsNullOrEmpty(other.Name) && (other.Aliases == null || other.Aliases.Count == 0);

                if (thisIsEmpty && otherIsEmpty)
                {
                    return true;
                }
            }

            // Step 4: Name + Class/Category match (case-insensitive)
            if (!string.IsNullOrEmpty(this.Name) && !string.IsNullOrEmpty(other.Name))
            {
                if (string.Equals(this.Name, other.Name, StringComparison.OrdinalIgnoreCase))
                {
                    // If both specify a Category, they must match
                    if (!string.IsNullOrEmpty(this.Category) &&
                        !string.IsNullOrEmpty(other.Category) &&
                        !string.Equals(this.Category, other.Category, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    return true;
                }
            }

            // Step 5: Aliases match / Name vs Aliases cross-match (case-insensitive)
            if (!string.IsNullOrEmpty(this.Name) && other.Aliases != null && other.Aliases.Count > 0)
            {
                foreach (var alias in other.Aliases)
                {
                    if (!string.IsNullOrEmpty(alias) && string.Equals(this.Name, alias, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(other.Name) && this.Aliases != null && this.Aliases.Count > 0)
            {
                foreach (var alias in this.Aliases)
                {
                    if (!string.IsNullOrEmpty(alias) && string.Equals(other.Name, alias, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (this.Aliases != null && this.Aliases.Count > 0 && other.Aliases != null && other.Aliases.Count > 0)
            {
                foreach (var aliasA in this.Aliases)
                {
                    if (string.IsNullOrEmpty(aliasA)) continue;
                    foreach (var aliasB in other.Aliases)
                    {
                        if (!string.IsNullOrEmpty(aliasB) && string.Equals(aliasA, aliasB, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Computes a hash code using the fallback priority strategy (UniqueId -> Id -> Name -> Class)
        /// to strictly enforce a.Equals(b) => a.GetHashCode() == b.GetHashCode().
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;

                if (!string.IsNullOrWhiteSpace(UniqueId))
                {
                    hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(UniqueId.Trim());
                    return hash;
                }

                if (Id != 0)
                {
                    hash = hash * 31 + Id.GetHashCode();
                    return hash;
                }

                if (!string.IsNullOrWhiteSpace(Name))
                {
                    hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(Name.Trim());
                    return hash;
                }

                if (!string.IsNullOrWhiteSpace(Class))
                {
                    hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(Class.Trim());
                    return hash;
                }

                return hash;
            }
        }

        /// <summary>
        /// Determines whether two <see cref="ElementIdModel"/> instances are equal.
        /// </summary>
        public static bool operator ==(ElementIdModel? left, ElementIdModel? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (ReferenceEquals(left, null) || ReferenceEquals(right, null)) return false;
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two <see cref="ElementIdModel"/> instances are not equal.
        /// </summary>
        public static bool operator !=(ElementIdModel? left, ElementIdModel? right)
        {
            return !(left == right);
        }

        #endregion
        #region Public Methods

        /// <summary>
        /// Deserializes a JSON string into an ElementIdModel instance.
        /// </summary>
        /// <param name="JSON">The JSON string.</param>
        /// <returns>An ElementIdModel instance.</returns>
        public static ElementIdModel? ByJSON (string JSON)
        {
            return JsonConvert.DeserializeObject<ElementIdModel>(JSON);
        }

        /// <summary>
        /// Serializes an ElementIdModel instance to a JSON string.
        /// </summary>
        /// <param name="IdJSON">The instance to serialize.</param>
        /// <returns>A JSON string.</returns>
        public static string ToJSON (ElementIdModel IdJSON)
        {
            return JsonConvert.SerializeObject(IdJSON, Formatting.Indented);
        }

        #endregion
    }

    public static class ElementIdExtensions
    {
        public static ElementIdModel ToModel(this ElementId id, Document doc, bool isTemplate = false)
        {
            if (id == null) return null;

            long idVal;
#if REVIT2022 || REVIT2023
            idVal = id.IntegerValue;
#else
            idVal = id.Value;
#endif

            if (id == LinePatternElement.GetSolidPatternId())
            {
                return new ElementIdModel
                {
                    Name = "Solid",
                    Class = "Autodesk.Revit.DB.LinePatternElement",
                    UniqueId = "",
                    Category = "",
                    Id = idVal,
                    IsTemplate = isTemplate
                };
            }

            var model = new ElementIdModel
            {
                Id = idVal,
                IsTemplate = isTemplate
            };

            if (doc != null)
            {
                Element elem = doc.GetElement(id);
                if (elem != null)
                {
                    model.Name = elem.Name;
                    model.Class = elem.GetType().FullName ?? string.Empty;
                    model.UniqueId = elem.UniqueId;

                    Category cat = elem.Category;
                    if (cat != null)
                    {
                        model.Category = cat.Name;
                    }
                }
            }

            return model;
        }

        public static ElementId ToElementId(this ElementIdModel model)
        {
#if REVIT2022 || REVIT2023
            return new ElementId((int)model.Id);
#else
            return new ElementId(model.Id);
#endif
        }
    }
}
