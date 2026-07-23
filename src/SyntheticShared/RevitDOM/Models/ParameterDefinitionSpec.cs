using System;
using System.Reflection;
using Autodesk.Revit.DB;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Encapsulates parameter group and spec/type metadata for Revit parameters across multi-version APIs.
    /// </summary>
    public class ParameterDefinitionSpec
    {
        /// <summary>
        /// Gets or sets the parameter group metadata (e.g. BuiltInParameterGroup or ForgeTypeId GroupTypeId).
        /// </summary>
        public object? Group { get; set; }

        /// <summary>
        /// Gets or sets the parameter spec or data type metadata (e.g. ParameterType or ForgeTypeId SpecTypeId).
        /// </summary>
        public object? SpecType { get; set; }

        /// <summary>
        /// Alias property for Group to maintain parameter group naming compatibility.
        /// </summary>
        public object? ParameterGroup
        {
            get => Group;
            set => Group = value;
        }

        /// <summary>
        /// Alias property for SpecType to maintain parameter type naming compatibility.
        /// </summary>
        public object? ParameterType
        {
            get => SpecType;
            set => SpecType = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ParameterDefinitionSpec"/> class.
        /// </summary>
        public ParameterDefinitionSpec()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ParameterDefinitionSpec"/> class with specified group and spec type.
        /// </summary>
        /// <param name="group">The parameter group.</param>
        /// <param name="specType">The parameter spec or type.</param>
        public ParameterDefinitionSpec(object? group, object? specType)
        {
            Group = group;
            SpecType = specType;
        }

        /// <summary>
        /// Creates a <see cref="ParameterDefinitionSpec"/> from an existing Revit parameter.
        /// </summary>
        /// <param name="param">The source parameter.</param>
        /// <returns>A new <see cref="ParameterDefinitionSpec"/> populated from the source parameter.</returns>
        public static ParameterDefinitionSpec FromParameter(Parameter? param)
        {
            if (param == null) return CreateDefault();

            object? group = null;
            object? specType = null;

            Definition def = param.Definition;
            if (def != null)
            {
#if REVIT2022 || REVIT2023
                group = def.ParameterGroup;
                specType = def.ParameterType;
#else
                // Default baseline: Revit 2024+
                group = def.GetGroupTypeId();
                specType = def.GetDataType();
#endif

                // Reflection fallbacks for cross-version / mock execution contexts
                if (group == null)
                {
                    var groupProp = def.GetType().GetProperty("ParameterGroup");
                    if (groupProp != null)
                    {
                        group = groupProp.GetValue(def);
                    }
                    else
                    {
                        var groupMethod = def.GetType().GetMethod("GetGroupTypeId");
                        if (groupMethod != null)
                        {
                            group = groupMethod.Invoke(def, null);
                        }
                    }
                }

                if (specType == null)
                {
                    var typeProp = def.GetType().GetProperty("ParameterType");
                    if (typeProp != null)
                    {
                        specType = typeProp.GetValue(def);
                    }
                    else
                    {
                        var typeMethod = def.GetType().GetMethod("GetDataType");
                        if (typeMethod != null)
                        {
                            specType = typeMethod.Invoke(def, null);
                        }
                    }
                }
            }

            if (group == null || specType == null)
            {
                var defaultSpec = CreateDefault();
                group ??= defaultSpec.Group;
                specType ??= defaultSpec.SpecType;
            }

            return new ParameterDefinitionSpec(group, specType);
        }

        /// <summary>
        /// Creates a <see cref="ParameterDefinitionSpec"/> containing default parameter group and spec type settings.
        /// </summary>
        /// <returns>A default <see cref="ParameterDefinitionSpec"/> instance.</returns>
        public static ParameterDefinitionSpec CreateDefault()
        {
            object? group = null;
            object? specType = null;

#if REVIT2022 || REVIT2023
            group = BuiltInParameterGroup.PG_DATA;
            specType = ParameterType.Text;
#else
            // Default baseline: Revit 2024+
            group = GroupTypeId.Data;
            specType = SpecTypeId.String.Text;
#endif

            // Dynamic reflection fallback if direct preprocessor constants are unavailable or running in reflection wrapper
            if (group == null || specType == null)
            {
                try
                {
                    Type? groupTypeIdType = typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.GroupTypeId");
                    if (groupTypeIdType != null)
                    {
                        var dataProp = groupTypeIdType.GetProperty("Data");
                        if (dataProp != null)
                        {
                            group ??= dataProp.GetValue(null);
                        }

                        Type? specTypeIdString = typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.SpecTypeId+String");
                        if (specTypeIdString != null)
                        {
                            var textProp = specTypeIdString.GetProperty("Text");
                            if (textProp != null)
                            {
                                specType ??= textProp.GetValue(null);
                            }
                        }
                    }
                    else
                    {
                        Type? bipgType = typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.BuiltInParameterGroup");
                        if (bipgType != null)
                        {
                            group ??= Enum.Parse(bipgType, "PG_DATA");
                        }

                        Type? ptType = typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.ParameterType");
                        if (ptType != null)
                        {
                            specType ??= Enum.Parse(ptType, "Text");
                        }
                    }
                }
                catch
                {
                    // Fallback to safe string descriptors if Revit assemblies are missing
                    group ??= "PG_DATA";
                    specType ??= "Text";
                }
            }

            return new ParameterDefinitionSpec(group, specType);
        }
    }
}