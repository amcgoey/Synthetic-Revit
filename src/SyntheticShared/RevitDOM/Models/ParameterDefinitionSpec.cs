using System;

namespace Synthetic.RevitDOM.Models
{
    public class ParameterDefinitionSpec
    {
        public object? Group { get; set; }
        public object? SpecType { get; set; }

        public object? ParameterGroup
        {
            get => Group;
            set => Group = value;
        }

        public object? ParameterType
        {
            get => SpecType;
            set => SpecType = value;
        }

        public ParameterDefinitionSpec()
        {
        }

        public ParameterDefinitionSpec(object? group, object? specType)
        {
            Group = group;
            SpecType = specType;
        }

        public static ParameterDefinitionSpec CreateDefault()
        {
            return new ParameterDefinitionSpec("PG_DATA", "Text");
        }
    }
}
