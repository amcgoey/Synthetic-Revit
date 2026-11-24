using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace RevitWorkerPool.Discovery;

/// <summary>
/// Metadata-only representation of a CLR type encountered inside custom attributes or signatures,
/// resolved strictly from ECMA-335 tables without loading any assemblies.
/// </summary>
public sealed record DecodedMetadataType(
    string FullName,
    bool IsArray = false,
    bool IsSystemType = false,
    PrimitiveTypeCode? UnderlyingEnumType = null,
    TypeDefinitionHandle? DefinitionHandle = null)
{
    public override string ToString() => FullName;
}

/// <summary>
/// Safe metadata-only <see cref="ICustomAttributeTypeProvider{TType}"/> and signature decoder
/// that decodes string constructor arguments, named properties/fields, enums, and <c>typeof(...)</c>
/// arguments directly from ECMA-335 blobs without invoking <see cref="Assembly.Load(string)"/>.
/// </summary>
internal sealed class MetadataCustomAttributeDecoder :
    ICustomAttributeTypeProvider<DecodedMetadataType>,
    ISignatureTypeProvider<DecodedMetadataType, object?>
{
    private readonly MetadataReader _reader;
    private readonly Dictionary<TypeDefinitionHandle, DecodedMetadataType> _typeDefCache = new();

    public MetadataCustomAttributeDecoder(MetadataReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public static string GetTypeDefinitionFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition td = reader.GetTypeDefinition(handle);
        string name = reader.GetString(td.Name);

        if (td.IsNested)
        {
            TypeDefinitionHandle declaringHandle = td.GetDeclaringType();
            if (!declaringHandle.IsNil)
            {
                return $"{GetTypeDefinitionFullName(reader, declaringHandle)}+{name}";
            }
        }

        string ns = reader.GetString(td.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    public static string GetTypeReferenceFullName(MetadataReader reader, TypeReferenceHandle handle)
    {
        TypeReference tr = reader.GetTypeReference(handle);
        string name = reader.GetString(tr.Name);

        if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return $"{GetTypeReferenceFullName(reader, (TypeReferenceHandle)tr.ResolutionScope)}+{name}";
        }

        string ns = reader.GetString(tr.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    public string? TryGetAttributeTypeFullName(
        CustomAttribute attribute,
        out TypeDefinitionHandle? sameAssemblyTypeHandle)
    {
        sameAssemblyTypeHandle = null;
        EntityHandle ctorHandle = attribute.Constructor;

        if (ctorHandle.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition methodDef = _reader.GetMethodDefinition((MethodDefinitionHandle)ctorHandle);
            TypeDefinitionHandle declaringType = methodDef.GetDeclaringType();
            if (declaringType.IsNil)
            {
                return null;
            }

            sameAssemblyTypeHandle = declaringType;
            return GetTypeDefinitionFullName(_reader, declaringType);
        }

        if (ctorHandle.Kind == HandleKind.MemberReference)
        {
            MemberReference memberRef = _reader.GetMemberReference((MemberReferenceHandle)ctorHandle);
            return ResolveEntityFullName(memberRef.Parent, out sameAssemblyTypeHandle);
        }

        return null;
    }

    public string? ResolveEntityFullName(EntityHandle handle, out TypeDefinitionHandle? sameAssemblyTypeHandle)
    {
        sameAssemblyTypeHandle = null;
        if (handle.IsNil)
        {
            return null;
        }

        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                sameAssemblyTypeHandle = (TypeDefinitionHandle)handle;
                return GetTypeDefinitionFullName(_reader, (TypeDefinitionHandle)handle);

            case HandleKind.TypeReference:
                return GetTypeReferenceFullName(_reader, (TypeReferenceHandle)handle);

            case HandleKind.TypeSpecification:
                try
                {
                    TypeSpecification spec = _reader.GetTypeSpecification((TypeSpecificationHandle)handle);
                    DecodedMetadataType decodedSpec = spec.DecodeSignature(this, genericContext: null);
                    sameAssemblyTypeHandle = decodedSpec.DefinitionHandle;
                    return decodedSpec.FullName;
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    return null;
                }

            default:
                return null;
        }
    }

    public bool TryDecodeAttribute(CustomAttribute attribute, out DecodedCustomAttribute? decoded)
    {
        decoded = null;
        string? attrTypeName = TryGetAttributeTypeFullName(attribute, out _);
        if (string.IsNullOrEmpty(attrTypeName))
        {
            return false;
        }

        try
        {
            CustomAttributeValue<DecodedMetadataType> rawValue = attribute.DecodeValue(this);
            var fixedArgs = new List<object?>(rawValue.FixedArguments.Length);
            foreach (CustomAttributeTypedArgument<DecodedMetadataType> arg in rawValue.FixedArguments)
            {
                fixedArgs.Add(UnwrapArgumentValue(arg.Value));
            }

            var namedArgs = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (CustomAttributeNamedArgument<DecodedMetadataType> named in rawValue.NamedArguments)
            {
                if (!string.IsNullOrEmpty(named.Name))
                {
                    namedArgs[named.Name] = UnwrapArgumentValue(named.Value);
                }
            }

            decoded = new DecodedCustomAttribute(attrTypeName, fixedArgs, namedArgs);
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    public PrimitiveTypeCode? TryGetFieldPrimitiveType(FieldDefinition fieldDef)
    {
        try
        {
            DecodedMetadataType decoded = fieldDef.DecodeSignature(this, genericContext: null);
            return decoded.UnderlyingEnumType;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static object? UnwrapArgumentValue(object? raw)
    {
        if (raw is ImmutableArray<CustomAttributeTypedArgument<DecodedMetadataType>> typedArray)
        {
            if (typedArray.IsDefault)
            {
                return null;
            }

            var elements = new object?[typedArray.Length];
            for (int i = 0; i < typedArray.Length; i++)
            {
                elements[i] = UnwrapArgumentValue(typedArray[i].Value);
            }

            return elements;
        }

        return raw;
    }

    public DecodedMetadataType GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        string fullName = typeCode switch
        {
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.Void => "System.Void",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            _ => $"System.{typeCode}"
        };

        return new DecodedMetadataType(fullName, UnderlyingEnumType: typeCode);
    }

    public DecodedMetadataType GetSystemType()
        => new("System.Type", IsSystemType: true);

    public bool IsSystemType(DecodedMetadataType type)
        => !type.IsArray && type.IsSystemType;

    public DecodedMetadataType GetSZArrayType(DecodedMetadataType elementType)
        => new($"{elementType.FullName}[]", IsArray: true);

    public DecodedMetadataType GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
    {
        if (_typeDefCache.TryGetValue(handle, out DecodedMetadataType? cached))
        {
            return cached;
        }

        string fullName = GetTypeDefinitionFullName(reader, handle);
        PrimitiveTypeCode? enumUnderlying = TryResolveSameAssemblyEnumUnderlyingType(reader, handle);
        var info = new DecodedMetadataType(
            fullName,
            IsSystemType: string.Equals(fullName, "System.Type", StringComparison.Ordinal),
            UnderlyingEnumType: enumUnderlying,
            DefinitionHandle: handle);

        _typeDefCache[handle] = info;
        return info;
    }

    public DecodedMetadataType GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
    {
        string fullName = GetTypeReferenceFullName(reader, handle);
        return new DecodedMetadataType(
            fullName,
            IsSystemType: string.Equals(fullName, "System.Type", StringComparison.Ordinal));
    }

    public DecodedMetadataType GetTypeFromSerializedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new DecodedMetadataType(string.Empty);
        }

        // Extract CLR type name from assembly-qualified name without loading any assembly.
        int commaIdx = name.IndexOf(',');
        string typeName = (commaIdx >= 0 ? name[..commaIdx] : name).Trim();
        return new DecodedMetadataType(
            typeName,
            IsArray: typeName.EndsWith("[]", StringComparison.Ordinal),
            IsSystemType: string.Equals(typeName, "System.Type", StringComparison.Ordinal));
    }

    public PrimitiveTypeCode GetUnderlyingEnumType(DecodedMetadataType type)
    {
        // Same-assembly enums have exact backing type resolved from their value__ field.
        // External enums fall back safely to Int32 without loading external assemblies.
        return type.UnderlyingEnumType ?? PrimitiveTypeCode.Int32;
    }

    private PrimitiveTypeCode? TryResolveSameAssemblyEnumUnderlyingType(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        TypeDefinition td = reader.GetTypeDefinition(handle);
        string? baseName = ResolveEntityFullName(td.BaseType, out _);
        if (!string.Equals(baseName, "System.Enum", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (FieldDefinitionHandle fieldHandle in td.GetFields())
        {
            FieldDefinition fieldDef = reader.GetFieldDefinition(fieldHandle);
            if ((fieldDef.Attributes & FieldAttributes.Static) == 0 &&
                string.Equals(reader.GetString(fieldDef.Name), "value__", StringComparison.Ordinal))
            {
                return TryGetFieldPrimitiveType(fieldDef) ?? PrimitiveTypeCode.Int32;
            }
        }

        return PrimitiveTypeCode.Int32;
    }

    #region ISignatureTypeProvider<DecodedMetadataType, object?>

    public DecodedMetadataType GetArrayType(DecodedMetadataType elementType, ArrayShape shape)
    {
        string rankSuffix = shape.Rank <= 1 ? "[]" : $"[{new string(',', shape.Rank - 1)}]";
        return new DecodedMetadataType($"{elementType.FullName}{rankSuffix}", IsArray: true);
    }

    public DecodedMetadataType GetByReferenceType(DecodedMetadataType elementType)
        => new($"{elementType.FullName}&");

    public DecodedMetadataType GetFunctionPointerType(MethodSignature<DecodedMetadataType> signature)
        => new("System.IntPtr");

    public DecodedMetadataType GetGenericInstantiation(
        DecodedMetadataType genericType,
        ImmutableArray<DecodedMetadataType> typeArguments)
    {
        string args = string.Join(", ", typeArguments);
        return genericType with { FullName = $"{genericType.FullName}<{args}>" };
    }

    public DecodedMetadataType GetGenericMethodParameter(object? genericContext, int index)
        => new($"!!{index}");

    public DecodedMetadataType GetGenericTypeParameter(object? genericContext, int index)
        => new($"!{index}");

    public DecodedMetadataType GetModifiedType(
        DecodedMetadataType modifier,
        DecodedMetadataType unmodifiedType,
        bool isRequired)
        => unmodifiedType;

    public DecodedMetadataType GetPinnedType(DecodedMetadataType elementType)
        => elementType;

    public DecodedMetadataType GetPointerType(DecodedMetadataType elementType)
        => new($"{elementType.FullName}*");

    public DecodedMetadataType GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
    {
        TypeSpecification spec = reader.GetTypeSpecification(handle);
        return spec.DecodeSignature(this, genericContext);
    }

    #endregion
}
