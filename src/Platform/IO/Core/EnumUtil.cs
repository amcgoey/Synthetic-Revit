using System;
using System.Collections.Generic;

namespace Synthetic.Platform.IO
{
    /// <summary>
    /// Reflection utilities for working with CLR enumerations.
    /// </summary>
    public class EnumUtil
    {
        internal EnumUtil() { }

        /// <summary>
        /// Retrieves an enum value of the given type name and member name.
        /// </summary>
        /// <param name="enumTypeName">The full type name of the enum.</param>
        /// <param name="name">Name of the enum member.</param>
        /// <returns>The parsed enum value, or null if not found.</returns>
        public static object? Parse(string enumTypeName, string name)
        {
            object? e = null;
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
        /// <param name="enumTypeName">The full type name of the enum.</param>
        /// <returns>A list of constant names in the enum.</returns>
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
        /// Retrieves the name as a string of the enumeration value.
        /// </summary>
        /// <param name="enumeration">An enum value.</param>
        /// <returns>The name of the enum value as a string.</returns>
        public static string GetName(Enum enumeration)
        {
            var enumType = enumeration.GetType();
            return Enum.GetName(enumType, enumeration) ?? enumeration.ToString();
        }

        /// <summary>
        /// Retrieves all enum values of a given type.
        /// </summary>
        /// <param name="enumTypeName">The full type name of the enum.</param>
        /// <returns>A list of boxed enum values.</returns>
        public static List<object> GetEnums(string enumTypeName)
        {
            List<object> eList = new List<object>();
            Type? eType = GetEnumType(enumTypeName);
            if (eType != null)
            {
                foreach (string name in Enum.GetNames(eType))
                {
                    object? e = Parse(enumTypeName, name);
                    if (e != null)
                    {
                        eList.Add(e);
                    }
                }
            }
            return eList;
        }

        /// <summary>
        /// Evaluates an enum and returns its underlying numeric value.
        /// </summary>
        /// <param name="enumeration">An enum value.</param>
        /// <returns>The underlying numeric value of the enum.</returns>
        public static object GetValue(object enumeration)
        {
            var enumType = enumeration.GetType();
            var underlyingType = Enum.GetUnderlyingType(enumType);
            var numericValue = Convert.ChangeType(enumeration, underlyingType);
            return numericValue;
        }

        /// <summary>
        /// Retrieves the Enum Type from a full type name across loaded assemblies.
        /// </summary>
        /// <param name="enumTypeName">The full type name of the enum.</param>
        /// <returns>The enum Type if found in the current AppDomain; otherwise null.</returns>
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
        /// Returns an indication whether a constant with a specified name exists in a specified enumeration.
        /// </summary>
        /// <param name="enumTypeName">The full type name of the enum.</param>
        /// <param name="name">Name of the enum member.</param>
        /// <returns>True if defined; otherwise false.</returns>
        public static bool IsDefined(string enumTypeName, string name)
        {
            Type? eType = GetEnumType(enumTypeName);
            object? e = Parse(enumTypeName, name);

            if (eType != null && e != null)
            {
                return Enum.IsDefined(eType, e);
            }
            return false;
        }
    }
}
