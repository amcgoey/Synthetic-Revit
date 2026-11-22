using System;
using System.Collections.Generic;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Shared
{
    /// <summary>
    /// Wrapper for using enumerations
    /// </summary>
    public class EnumUtil
    {
        internal EnumUtil() { }

        /// <summary>
        /// Retrieves a enum of the given type and name.
        /// </summary>
        /// <param name="enumTypeName">The enum type as a string. Requires the full assembly path in front of the type.</param>
        /// <param name="name">Name of the enum.</param>
        /// <returns name="enum">Returns a enum.</returns>
        public static System.Object? Parse(string enumTypeName, string name)
        {
            System.Object? e = null;
            try
            {
                Type? et = GetEnumType(enumTypeName);
                if (et != null)
                {
                    e = Enum.Parse(et, name);
                }
            }
            catch (ArgumentException)
            {
                e = null;
            }
            return e;
        }

        /// <summary>
        /// Retrieves a list of the names of the constants in a specified enumeration.
        /// </summary>
        /// <param name="enumTypeName">The enum type as a string. Requires the full assembly path in front of the type.</param>
        /// <returns name="names">A string list of the names of the constants in enumType.</returns>
        public static List<string> GetNames(string enumTypeName)
        {
            List<string> e = new List<string>();
            Type? eType = GetEnumType(enumTypeName);
            if (eType != null)
            {
                foreach (string name in Enum.GetNames(eType))
                {
                    e.Add(name);
                }
            }
            return e;
        }

        /// <summary> 
        /// Retrieves the name as a string of the enumeration. 
        /// </summary> 
        /// <param name="enumeration">A enum</param> 
        /// <returns name="name">Returns the name of the enum as a string</returns> 
        public static string GetName(Enum enumeration)
        {
            var enumType = enumeration.GetType();
            return Enum.GetName(enumType, enumeration) ?? enumeration.ToString();
        }

        /// <summary>
        /// Retrieves all enums of a given type.
        /// </summary>
        /// <param name="enumTypeName">The enum type as a string. Requires the full assembly path in front of the type.</param>
        /// <returns name="enums">A list of enums.</returns>
        public static List<System.Object> GetEnums(string enumTypeName)
        {
            List<System.Object> eList = new List<System.Object>();
            Type? eType = GetEnumType(enumTypeName);
            if (eType != null)
            {
                foreach (string name in Enum.GetNames(eType))
                {
                    System.Object? e = Parse(enumTypeName, name);
                    if (e != null)
                    {
                        eList.Add(e);
                    }
                }
            }
            return eList;
        }

        /// <summary>
        /// Evaluates a enum and returns its value.
        /// </summary>
        /// <param name="enumeration">A enum</param>
        /// <returns name="value">Returns the value of the enum</returns>
        public static object GetValue(object enumeration)
        {
            //int i = Convert.ToInt32(enumeration);
            //return i;
            var enumType = enumeration.GetType();
            var underlyingType = Enum.GetUnderlyingType(enumType);
            var numericValue = System.Convert.ChangeType(enumeration, underlyingType);
            return numericValue;
        }

        /// <summary>
        /// Retrieves the Enum Type from a string name.
        /// </summary>
        /// <param name="enumTypeName">The enum type as a string. Requires the full assembly path in front of the type.</param>
        /// <returns name="enumType">Returns the enum type if it exists in the current domain.</returns>
        public static Type? GetEnumType(string enumTypeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(enumTypeName);
                if (type == null)
                    continue;
                if (type.IsEnum)
                    return type;
            }
            return null;
        }

        /// <summary>
        /// Returns an indication whether a constant with a specified value exists in a specified enumeration.
        /// </summary>
        /// <param name="enumTypeName">The enum type as a string. Requires the full assembly path in front of the type.</param>
        /// <param name="name">Name of the enum.</param>
        /// <returns name="bool">True if a constant in enumType has a value equal to value; otherwise, false.</returns>
        public static bool IsDefined(string enumTypeName, string name)
        {
            Type? eType = GetEnumType(enumTypeName);
            System.Object? e = Parse(enumTypeName, name);

            if (eType != null && e != null)
            {
                return Enum.IsDefined(eType, e);
            }
            return false;
        }
    }

    public static class EnumExtensions
    {
        public static Enum ToEnum(this EnumModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var parsed = EnumUtil.Parse(model.Type, model.Value);
            if (parsed is Enum e) return e;
            throw new InvalidOperationException($"Could not parse enum of type {model.Type} with value {model.Value}");
        }

        public static EnumModel ToModel(this Enum value)
        {
            if (value == null) return null;
            return new EnumModel(value.GetType(), value);
        }
    }
}
