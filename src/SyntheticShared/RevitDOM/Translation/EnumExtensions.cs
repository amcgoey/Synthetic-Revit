using System;
using Synthetic.Platform.IO;
using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Translation
{
    /// <summary>
    /// Extension methods bridging <see cref="EnumModel"/> and CLR <see cref="Enum"/> values.
    /// </summary>
    public static class EnumExtensions
    {
        /// <summary>
        /// Reconstructs the CLR <see cref="Enum"/> value represented by an <see cref="EnumModel"/>.
        /// </summary>
        public static Enum ToEnum(this EnumModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var parsed = EnumUtil.Parse(model.Type, model.Value);
            if (parsed is Enum e) return e;
            throw new InvalidOperationException($"Could not parse enum of type {model.Type} with value {model.Value}");
        }

        /// <summary>
        /// Converts a CLR <see cref="Enum"/> value into a serializable <see cref="EnumModel"/>.
        /// </summary>
        public static EnumModel? ToModel(this Enum? value)
        {
            if (value == null) return null;
            return new EnumModel(value.GetType(), value);
        }
    }
}
