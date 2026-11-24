using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SyntheticTests.Architecture
{
    public sealed class PreprocessorDirectiveRecord
    {
        public PreprocessorDirectiveRecord(
            string directiveKind,
            string conditionText,
            IReadOnlyList<string> identifiers,
            int lineNumber)
        {
            DirectiveKind = directiveKind;
            ConditionText = conditionText;
            Identifiers = identifiers;
            LineNumber = lineNumber;
        }

        public string DirectiveKind { get; }
        public string ConditionText { get; }
        public IReadOnlyList<string> Identifiers { get; }
        public int LineNumber { get; }
    }

    public sealed class UsingRecord
    {
        public UsingRecord(
            string normalizedTarget,
            string? alias,
            bool isStatic,
            bool isGlobalQualified,
            int lineNumber)
        {
            NormalizedTarget = normalizedTarget;
            Alias = alias;
            IsStatic = isStatic;
            IsGlobalQualified = isGlobalQualified;
            LineNumber = lineNumber;
        }

        public string NormalizedTarget { get; }
        public string? Alias { get; }
        public bool IsStatic { get; }
        public bool IsGlobalQualified { get; }
        public int LineNumber { get; }
    }

    public sealed class TypeReferenceRecord
    {
        public TypeReferenceRecord(
            string normalizedName,
            int lineNumber,
            bool isUsingDirective,
            bool isNamespaceDeclaration)
        {
            NormalizedName = normalizedName;
            LineNumber = lineNumber;
            IsUsingDirective = isUsingDirective;
            IsNamespaceDeclaration = isNamespaceDeclaration;
        }

        public string NormalizedName { get; }
        public int LineNumber { get; }
        public bool IsUsingDirective { get; }
        public bool IsNamespaceDeclaration { get; }
    }

    public sealed class ObjectCreationRecord
    {
        public ObjectCreationRecord(
            string normalizedTypeName,
            string rawTypeName,
            bool isImplicit,
            int lineNumber,
            string? enclosingTypeFullName,
            string? enclosingMethodName,
            ExpressionSyntax node)
        {
            NormalizedTypeName = normalizedTypeName;
            RawTypeName = rawTypeName;
            IsImplicit = isImplicit;
            LineNumber = lineNumber;
            EnclosingTypeFullName = enclosingTypeFullName;
            EnclosingMethodName = enclosingMethodName;
            Node = node;
        }

        public string NormalizedTypeName { get; }
        public string RawTypeName { get; }
        public bool IsImplicit { get; }
        public int LineNumber { get; }
        public string? EnclosingTypeFullName { get; }
        public string? EnclosingMethodName { get; }
        public ExpressionSyntax Node { get; }
    }

    public sealed class MemberAccessRecord
    {
        public MemberAccessRecord(string normalizedFullName, int lineNumber, MemberAccessExpressionSyntax node)
        {
            NormalizedFullName = normalizedFullName;
            LineNumber = lineNumber;
            Node = node;
        }

        public string NormalizedFullName { get; }
        public int LineNumber { get; }
        public MemberAccessExpressionSyntax Node { get; }
    }

    public sealed class InvocationRecord
    {
        public InvocationRecord(string methodName, string normalizedTarget, int lineNumber, InvocationExpressionSyntax node)
        {
            MethodName = methodName;
            NormalizedTarget = normalizedTarget;
            LineNumber = lineNumber;
            Node = node;
        }

        public string MethodName { get; }
        public string NormalizedTarget { get; }
        public int LineNumber { get; }
        public InvocationExpressionSyntax Node { get; }
    }

    public sealed class AttributeRecord
    {
        public AttributeRecord(string rawName, string normalizedName, int lineNumber)
        {
            RawName = rawName;
            NormalizedName = normalizedName;
            LineNumber = lineNumber;
        }

        public string RawName { get; }
        public string NormalizedName { get; }
        public int LineNumber { get; }
    }

    public sealed class ConstantFieldRecord
    {
        public ConstantFieldRecord(string fieldName, string? normalizedTypeName, ExpressionSyntax? initializer, bool isConst, int lineNumber)
        {
            FieldName = fieldName;
            NormalizedTypeName = normalizedTypeName;
            Initializer = initializer;
            IsConst = isConst;
            LineNumber = lineNumber;
        }

        public string FieldName { get; }
        public string? NormalizedTypeName { get; }
        public ExpressionSyntax? Initializer { get; }
        public bool IsConst { get; }
        public int LineNumber { get; }
    }

    public sealed class FieldDeclarationRecord
    {
        public FieldDeclarationRecord(
            string name,
            string normalizedTypeName,
            bool isStatic,
            bool isConst,
            bool isReadOnly,
            int lineNumber)
        {
            Name = name;
            NormalizedTypeName = normalizedTypeName;
            IsStatic = isStatic;
            IsConst = isConst;
            IsReadOnly = isReadOnly;
            LineNumber = lineNumber;
        }

        public string Name { get; }
        public string NormalizedTypeName { get; }
        public bool IsStatic { get; }
        public bool IsConst { get; }
        public bool IsReadOnly { get; }
        public int LineNumber { get; }
    }

    public sealed class PropertyDeclarationRecord
    {
        public PropertyDeclarationRecord(
            string name,
            string? normalizedTypeName,
            ExpressionSyntax? getterExpression,
            int lineNumber)
        {
            Name = name;
            NormalizedTypeName = normalizedTypeName;
            GetterExpression = getterExpression;
            LineNumber = lineNumber;
        }

        public string Name { get; }
        public string? NormalizedTypeName { get; }
        public ExpressionSyntax? GetterExpression { get; }
        public int LineNumber { get; }
    }

    public sealed class MethodDeclarationRecord
    {
        public MethodDeclarationRecord(
            string name,
            bool isAbstract,
            bool isStatic,
            IReadOnlyList<AttributeRecord> attributes,
            int lineNumber)
        {
            Name = name;
            IsAbstract = isAbstract;
            IsStatic = isStatic;
            Attributes = attributes;
            LineNumber = lineNumber;
        }

        public string Name { get; }
        public bool IsAbstract { get; }
        public bool IsStatic { get; }
        public IReadOnlyList<AttributeRecord> Attributes { get; }
        public int LineNumber { get; }
    }

    public sealed class TypeDeclarationRecord
    {
        public TypeDeclarationRecord(
            string name,
            string fullName,
            string @namespace,
            string kind,
            bool isAbstract,
            bool isStatic,
            IReadOnlyList<string> normalizedBaseTypes,
            IReadOnlyList<string> rawBaseTypes,
            IReadOnlyList<AttributeRecord> attributes,
            IReadOnlyList<ConstantFieldRecord> constantFields,
            IReadOnlyList<FieldDeclarationRecord> fields,
            IReadOnlyList<PropertyDeclarationRecord> properties,
            IReadOnlyList<MethodDeclarationRecord> methods,
            int lineNumber,
            SyntaxTree syntaxTree)
        {
            Name = name;
            FullName = fullName;
            Namespace = @namespace;
            Kind = kind;
            IsAbstract = isAbstract;
            IsStatic = isStatic;
            NormalizedBaseTypes = normalizedBaseTypes;
            RawBaseTypes = rawBaseTypes;
            Attributes = attributes;
            ConstantFields = constantFields;
            Fields = fields;
            Properties = properties;
            Methods = methods;
            LineNumber = lineNumber;
            SyntaxTree = syntaxTree;
        }

        public string Name { get; }
        public string FullName { get; }
        public string Namespace { get; }
        public string Kind { get; }
        public bool IsAbstract { get; }
        public bool IsStatic { get; }
        public IReadOnlyList<string> NormalizedBaseTypes { get; }
        public IReadOnlyList<string> RawBaseTypes { get; }
        public IReadOnlyList<AttributeRecord> Attributes { get; }
        public IReadOnlyList<ConstantFieldRecord> ConstantFields { get; }
        public IReadOnlyList<FieldDeclarationRecord> Fields { get; }
        public IReadOnlyList<PropertyDeclarationRecord> Properties { get; }
        public IReadOnlyList<MethodDeclarationRecord> Methods { get; }
        public int LineNumber { get; }
        public SyntaxTree SyntaxTree { get; }
    }

    public sealed class ParsedSyntaxVariant
    {
        public ParsedSyntaxVariant(
            IReadOnlyList<string> preprocessorSymbols,
            SyntaxTree tree,
            CompilationUnitSyntax root,
            IReadOnlyList<PreprocessorDirectiveRecord> ifDirectives,
            IReadOnlyList<UsingRecord> usings,
            IReadOnlyDictionary<string, string> usingAliases,
            IReadOnlyList<TypeReferenceRecord> typeReferences,
            IReadOnlyList<ObjectCreationRecord> objectCreations,
            IReadOnlyList<MemberAccessRecord> memberAccesses,
            IReadOnlyList<InvocationRecord> invocations,
            IReadOnlyList<TypeDeclarationRecord> types)
        {
            PreprocessorSymbols = preprocessorSymbols;
            Tree = tree;
            Root = root;
            IfDirectives = ifDirectives;
            Usings = usings;
            UsingAliases = usingAliases;
            TypeReferences = typeReferences;
            ObjectCreations = objectCreations;
            MemberAccesses = memberAccesses;
            Invocations = invocations;
            Types = types;
        }

        public IReadOnlyList<string> PreprocessorSymbols { get; }
        public SyntaxTree Tree { get; }
        public CompilationUnitSyntax Root { get; }
        public IReadOnlyList<PreprocessorDirectiveRecord> IfDirectives { get; }
        public IReadOnlyList<UsingRecord> Usings { get; }
        public IReadOnlyDictionary<string, string> UsingAliases { get; }
        public IReadOnlyList<TypeReferenceRecord> TypeReferences { get; }
        public IReadOnlyList<ObjectCreationRecord> ObjectCreations { get; }
        public IReadOnlyList<MemberAccessRecord> MemberAccesses { get; }
        public IReadOnlyList<InvocationRecord> Invocations { get; }
        public IReadOnlyList<TypeDeclarationRecord> Types { get; }
    }

    /// <summary>
    /// Single-parse Roslyn syntax parser and AST normalizer handling <c>#if</c>/<c>#elif</c> branch re-parsing,
    /// <see cref="UsingDirectiveSyntax"/> alias expansion, <see cref="AliasQualifiedNameSyntax"/> (<c>global::</c>) stripping,
    /// and C# 9 <see cref="ImplicitObjectCreationExpressionSyntax"/> (<c>new(...)</c>) target-type resolution.
    /// </summary>
    public static class RoslynSyntaxNormalizer
    {
        private static readonly Regex RevitSymbolRegex = new Regex(@"^REVIT20\d{2}$", RegexOptions.CultureInvariant);

        private static readonly CSharpParseOptions BaseParseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.CSharp9)
            .WithDocumentationMode(DocumentationMode.None);

        public static IReadOnlyList<ParsedSyntaxVariant> ParseVariants(
            string sourceText,
            string filePath,
            int currentLatestVersion)
        {
            SourceText text = SourceText.From(sourceText);
            SyntaxTree primaryTree = CSharpSyntaxTree.ParseText(text, BaseParseOptions, path: filePath);
            CompilationUnitSyntax primaryRoot = primaryTree.GetCompilationUnitRoot();

            var ifDirectives = ExtractIfDirectives(primaryRoot);
            var primaryVariant = AnalyzeTree(primaryTree, primaryRoot, Array.Empty<string>(), ifDirectives);

            if (ifDirectives.Count == 0)
            {
                return new[] { primaryVariant };
            }

            List<string[]> extraProfiles = BuildPreprocessorProfiles(ifDirectives, currentLatestVersion);
            if (extraProfiles.Count == 0)
            {
                return new[] { primaryVariant };
            }

            var variants = new List<ParsedSyntaxVariant>(1 + extraProfiles.Count) { primaryVariant };
            foreach (string[] symbols in extraProfiles)
            {
                CSharpParseOptions options = BaseParseOptions.WithPreprocessorSymbols(symbols);
                SyntaxTree variantTree = CSharpSyntaxTree.ParseText(text, options, path: filePath);
                CompilationUnitSyntax variantRoot = variantTree.GetCompilationUnitRoot();
                var variantDirectives = ExtractIfDirectives(variantRoot);
                variants.Add(AnalyzeTree(variantTree, variantRoot, symbols, variantDirectives));
            }

            return variants;
        }

        public static ParsedSyntaxVariant ParseSingle(string sourceText, string filePath = "", params string[] preprocessorSymbols)
        {
            CSharpParseOptions options = preprocessorSymbols.Length == 0
                ? BaseParseOptions
                : BaseParseOptions.WithPreprocessorSymbols(preprocessorSymbols);

            SyntaxTree tree = CSharpSyntaxTree.ParseText(SourceText.From(sourceText), options, path: filePath);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            var directives = ExtractIfDirectives(root);
            return AnalyzeTree(tree, root, preprocessorSymbols, directives);
        }

        private static IReadOnlyList<PreprocessorDirectiveRecord> ExtractIfDirectives(CompilationUnitSyntax root)
        {
            if (!root.ContainsDirectives)
            {
                return Array.Empty<PreprocessorDirectiveRecord>();
            }

            var list = new List<PreprocessorDirectiveRecord>();
            foreach (BranchingDirectiveTriviaSyntax directive in root.DescendantNodes(descendIntoTrivia: true).OfType<BranchingDirectiveTriviaSyntax>())
            {
                string? kind = null;
                ExpressionSyntax? condition = null;
                if (directive is IfDirectiveTriviaSyntax ifDir)
                {
                    kind = "if";
                    condition = ifDir.Condition;
                }
                else if (directive is ElifDirectiveTriviaSyntax elifDir)
                {
                    kind = "elif";
                    condition = elifDir.Condition;
                }

                if (kind == null || condition == null)
                {
                    continue;
                }

                var identifiers = condition
                    .DescendantNodesAndSelf()
                    .OfType<IdentifierNameSyntax>()
                    .Select(id => id.Identifier.ValueText)
                    .Where(name => !string.Equals(name, "true", StringComparison.Ordinal) &&
                                   !string.Equals(name, "false", StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                int line = directive.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                list.Add(new PreprocessorDirectiveRecord(kind, condition.ToString().Trim(), identifiers, line));
            }

            return list;
        }

        private static List<string[]> BuildPreprocessorProfiles(
            IReadOnlyList<PreprocessorDirectiveRecord> ifDirectives,
            int currentLatestVersion)
        {
            var symbolsInFile = new HashSet<string>(StringComparer.Ordinal);
            foreach (PreprocessorDirectiveRecord d in ifDirectives)
            {
                foreach (string id in d.Identifiers)
                {
                    symbolsInFile.Add(id);
                }
            }

            var profiles = new List<string[]>();
            var seenProfileKeys = new HashSet<string>(StringComparer.Ordinal) { string.Empty };

            void AddProfile(params string[] symbols)
            {
                string[] ordered = symbols
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToArray();

                string key = string.Join(",", ordered);
                if (seenProfileKeys.Add(key))
                {
                    profiles.Add(ordered);
                }
            }

            bool hasNetFramework = symbolsInFile.Contains("NETFRAMEWORK");
            bool hasDebug = symbolsInFile.Contains("DEBUG");

            for (int year = 2023; year < currentLatestVersion; year++)
            {
                string revitSym = "REVIT" + year.ToString(CultureInfo.InvariantCulture);
                if (!symbolsInFile.Contains(revitSym))
                {
                    continue;
                }

                AddProfile(revitSym);
                if (hasNetFramework)
                {
                    AddProfile(revitSym, "NETFRAMEWORK");
                }
                if (hasDebug)
                {
                    AddProfile(revitSym, "DEBUG");
                }
            }

            foreach (string sym in symbolsInFile)
            {
                if (RevitSymbolRegex.IsMatch(sym))
                {
                    AddProfile(sym);
                }
                else if (string.Equals(sym, "NETFRAMEWORK", StringComparison.Ordinal))
                {
                    AddProfile("NETFRAMEWORK");
                }
                else if (string.Equals(sym, "DEBUG", StringComparison.Ordinal))
                {
                    AddProfile("DEBUG");
                }
                else if (!sym.StartsWith("REVIT", StringComparison.Ordinal))
                {
                    AddProfile(sym);
                }
            }

            return profiles;
        }

        private static ParsedSyntaxVariant AnalyzeTree(
            SyntaxTree tree,
            CompilationUnitSyntax root,
            IReadOnlyList<string> preprocessorSymbols,
            IReadOnlyList<PreprocessorDirectiveRecord> ifDirectives)
        {
            var usingAliases = new Dictionary<string, string>(StringComparer.Ordinal);
            var usings = new List<UsingRecord>();

            foreach (UsingDirectiveSyntax u in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                if (u.Name == null)
                {
                    continue;
                }

                (string rawTarget, bool isGlobal) = FlattenName(u.Name);
                if (string.IsNullOrEmpty(rawTarget))
                {
                    continue;
                }

                int line = GetLineNumber(u);
                string? aliasName = u.Alias?.Name?.Identifier.ValueText;
                bool isStatic = u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword);

                if (!string.IsNullOrEmpty(aliasName))
                {
                    usingAliases[aliasName!] = rawTarget;
                }

                usings.Add(new UsingRecord(rawTarget, aliasName, isStatic, isGlobal, line));
            }

            var typeReferences = new List<TypeReferenceRecord>();
            var objectCreations = new List<ObjectCreationRecord>();
            var memberAccesses = new List<MemberAccessRecord>();
            var invocations = new List<InvocationRecord>();
            var types = new List<TypeDeclarationRecord>();

            foreach (SyntaxNode node in root.DescendantNodes())
            {
                switch (node)
                {
                    case UsingDirectiveSyntax u when u.Name != null:
                    {
                        (string target, _) = FlattenName(u.Name);
                        if (!string.IsNullOrEmpty(target))
                        {
                            typeReferences.Add(new TypeReferenceRecord(target, GetLineNumber(u), isUsingDirective: true, isNamespaceDeclaration: false));
                        }
                        break;
                    }
                    case QualifiedNameSyntax qn when !(qn.Parent is QualifiedNameSyntax) && !(qn.Parent is AliasQualifiedNameSyntax):
                    {
                        string normalized = NormalizeName(qn, usingAliases);
                        if (!string.IsNullOrEmpty(normalized))
                        {
                            bool isNsDecl = qn.Parent is BaseNamespaceDeclarationSyntax;
                            typeReferences.Add(new TypeReferenceRecord(normalized, GetLineNumber(qn), isUsingDirective: qn.Parent is UsingDirectiveSyntax, isNamespaceDeclaration: isNsDecl));
                        }
                        break;
                    }
                    case AliasQualifiedNameSyntax aqn when !(aqn.Parent is QualifiedNameSyntax) && !(aqn.Parent is MemberAccessExpressionSyntax):
                    {
                        string normalized = NormalizeName(aqn, usingAliases);
                        if (!string.IsNullOrEmpty(normalized))
                        {
                            typeReferences.Add(new TypeReferenceRecord(normalized, GetLineNumber(aqn), isUsingDirective: aqn.Parent is UsingDirectiveSyntax, isNamespaceDeclaration: false));
                        }
                        break;
                    }
                    case MemberAccessExpressionSyntax ma when !(ma.Parent is MemberAccessExpressionSyntax):
                    {
                        string normalized = NormalizeName(ma, usingAliases);
                        if (!string.IsNullOrEmpty(normalized))
                        {
                            int line = GetLineNumber(ma);
                            memberAccesses.Add(new MemberAccessRecord(normalized, line, ma));
                            typeReferences.Add(new TypeReferenceRecord(normalized, line, isUsingDirective: false, isNamespaceDeclaration: false));
                        }
                        break;
                    }
                    case IdentifierNameSyntax id when IsStandaloneIdentifierReference(id):
                    {
                        string text = id.Identifier.ValueText;
                        string normalized = ExpandAlias(text, isGlobalQualified: false, usingAliases);
                        bool isNsDecl = id.Parent is BaseNamespaceDeclarationSyntax;
                        typeReferences.Add(new TypeReferenceRecord(normalized, GetLineNumber(id), isUsingDirective: id.Parent is UsingDirectiveSyntax, isNamespaceDeclaration: isNsDecl));
                        break;
                    }
                    case GenericNameSyntax gn when IsStandaloneIdentifierReference(gn):
                    {
                        string text = gn.Identifier.ValueText;
                        string normalized = ExpandAlias(text, isGlobalQualified: false, usingAliases);
                        typeReferences.Add(new TypeReferenceRecord(normalized, GetLineNumber(gn), isUsingDirective: false, isNamespaceDeclaration: false));
                        break;
                    }
                    case ObjectCreationExpressionSyntax objCreate:
                    {
                        string rawType = FlattenName(objCreate.Type).Name;
                        string normalized = NormalizeTypeSyntax(objCreate.Type, usingAliases);
                        var (enclosingType, enclosingMethod) = GetEnclosingContext(objCreate);
                        objectCreations.Add(new ObjectCreationRecord(
                            normalized,
                            rawType,
                            isImplicit: false,
                            GetLineNumber(objCreate),
                            enclosingType,
                            enclosingMethod,
                            objCreate));
                        break;
                    }
                    case ImplicitObjectCreationExpressionSyntax implicitCreate:
                    {
                        (string rawType, string normalized) = ResolveImplicitObjectCreationType(implicitCreate, usingAliases);
                        var (enclosingType, enclosingMethod) = GetEnclosingContext(implicitCreate);
                        int line = GetLineNumber(implicitCreate);
                        objectCreations.Add(new ObjectCreationRecord(
                            normalized,
                            rawType,
                            isImplicit: true,
                            line,
                            enclosingType,
                            enclosingMethod,
                            implicitCreate));
                        if (!string.IsNullOrEmpty(normalized))
                        {
                            typeReferences.Add(new TypeReferenceRecord(normalized, line, isUsingDirective: false, isNamespaceDeclaration: false));
                        }
                        break;
                    }
                    case InvocationExpressionSyntax invocation:
                    {
                        InvocationRecord? invRecord = AnalyzeInvocation(invocation, usingAliases);
                        if (invRecord != null)
                        {
                            invocations.Add(invRecord);
                        }
                        break;
                    }
                    case TypeDeclarationSyntax typeDecl:
                    {
                        types.Add(AnalyzeTypeDeclaration(typeDecl, usingAliases, tree));
                        break;
                    }
                }
            }

            return new ParsedSyntaxVariant(
                preprocessorSymbols,
                tree,
                root,
                ifDirectives,
                usings,
                usingAliases,
                typeReferences,
                objectCreations,
                memberAccesses,
                invocations,
                types);
        }

        private static bool IsStandaloneIdentifierReference(SimpleNameSyntax name)
        {
            SyntaxNode? parent = name.Parent;
            if (parent is QualifiedNameSyntax ||
                parent is AliasQualifiedNameSyntax ||
                parent is NameColonSyntax)
            {
                return false;
            }

            if (parent is MemberAccessExpressionSyntax ma && ma.Name == name)
            {
                return false;
            }

            if (parent is NameEqualsSyntax ne && ne.Name == name)
            {
                return false;
            }

            return true;
        }

        private static InvocationRecord? AnalyzeInvocation(
            InvocationExpressionSyntax invocation,
            IReadOnlyDictionary<string, string> usingAliases)
        {
            string methodName = string.Empty;
            string normalizedTarget = string.Empty;

            if (invocation.Expression is IdentifierNameSyntax id)
            {
                methodName = id.Identifier.ValueText;
                normalizedTarget = ExpandAlias(methodName, isGlobalQualified: false, usingAliases);
            }
            else if (invocation.Expression is GenericNameSyntax gn)
            {
                methodName = gn.Identifier.ValueText;
                normalizedTarget = ExpandAlias(methodName, isGlobalQualified: false, usingAliases);
            }
            else if (invocation.Expression is MemberAccessExpressionSyntax ma)
            {
                methodName = ma.Name.Identifier.ValueText;
                normalizedTarget = NormalizeName(ma, usingAliases);
                if (string.IsNullOrEmpty(normalizedTarget))
                {
                    normalizedTarget = methodName;
                }
            }
            else if (invocation.Expression is MemberBindingExpressionSyntax mb)
            {
                methodName = mb.Name.Identifier.ValueText;
                normalizedTarget = methodName;
            }

            if (string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            return new InvocationRecord(methodName, normalizedTarget, GetLineNumber(invocation), invocation);
        }

        private static TypeDeclarationRecord AnalyzeTypeDeclaration(
            TypeDeclarationSyntax typeDecl,
            IReadOnlyDictionary<string, string> usingAliases,
            SyntaxTree syntaxTree)
        {
            string name = typeDecl.Identifier.ValueText;
            string ns = GetEnclosingNamespace(typeDecl);
            string fullName = GetFullTypeName(typeDecl);
            string kind = typeDecl switch
            {
                ClassDeclarationSyntax => "class",
                StructDeclarationSyntax => "struct",
                InterfaceDeclarationSyntax => "interface",
                RecordDeclarationSyntax => "record",
                _ => "type"
            };

            bool isAbstract = typeDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword));
            bool isStatic = typeDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));

            var normalizedBases = new List<string>();
            var rawBases = new List<string>();
            if (typeDecl.BaseList != null)
            {
                foreach (BaseTypeSyntax baseType in typeDecl.BaseList.Types)
                {
                    string raw = FlattenName(baseType.Type).Name;
                    string norm = NormalizeTypeSyntax(baseType.Type, usingAliases);
                    if (!string.IsNullOrEmpty(raw)) rawBases.Add(raw);
                    if (!string.IsNullOrEmpty(norm)) normalizedBases.Add(norm);
                }
            }

            List<AttributeRecord> attributes = ExtractAttributes(typeDecl.AttributeLists, usingAliases);
            var constantFields = new List<ConstantFieldRecord>();
            var fields = new List<FieldDeclarationRecord>();
            var properties = new List<PropertyDeclarationRecord>();
            var methods = new List<MethodDeclarationRecord>();

            foreach (MemberDeclarationSyntax member in typeDecl.Members)
            {
                if (member is FieldDeclarationSyntax fieldDecl)
                {
                    bool fieldStatic = fieldDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                    bool fieldConst = fieldDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));
                    bool fieldReadOnly = fieldDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword));
                    string fieldType = NormalizeTypeSyntax(fieldDecl.Declaration.Type, usingAliases);

                    foreach (VariableDeclaratorSyntax v in fieldDecl.Declaration.Variables)
                    {
                        string fieldName = v.Identifier.ValueText;
                        int line = GetLineNumber(v);
                        fields.Add(new FieldDeclarationRecord(fieldName, fieldType, fieldStatic, fieldConst, fieldReadOnly, line));

                        if (fieldConst || (fieldStatic && fieldReadOnly))
                        {
                            constantFields.Add(new ConstantFieldRecord(fieldName, fieldType, v.Initializer?.Value, fieldConst, line));
                        }
                    }
                }
                else if (member is PropertyDeclarationSyntax propDecl)
                {
                    string propType = NormalizeTypeSyntax(propDecl.Type, usingAliases);
                    ExpressionSyntax? getterExpr = ExtractPropertyGetterExpression(propDecl);
                    properties.Add(new PropertyDeclarationRecord(
                        propDecl.Identifier.ValueText,
                        propType,
                        getterExpr,
                        GetLineNumber(propDecl)));
                }
                else if (member is MethodDeclarationSyntax methodDecl)
                {
                    bool methodAbstract = methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword));
                    bool methodStatic = methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                    methods.Add(new MethodDeclarationRecord(
                        methodDecl.Identifier.ValueText,
                        methodAbstract,
                        methodStatic,
                        ExtractAttributes(methodDecl.AttributeLists, usingAliases),
                        GetLineNumber(methodDecl)));
                }
            }

            return new TypeDeclarationRecord(
                name,
                fullName,
                ns,
                kind,
                isAbstract,
                isStatic,
                normalizedBases,
                rawBases,
                attributes,
                constantFields,
                fields,
                properties,
                methods,
                GetLineNumber(typeDecl),
                syntaxTree);
        }

        private static ExpressionSyntax? ExtractPropertyGetterExpression(PropertyDeclarationSyntax propDecl)
        {
            if (propDecl.ExpressionBody != null)
            {
                return propDecl.ExpressionBody.Expression;
            }

            if (propDecl.Initializer != null)
            {
                return propDecl.Initializer.Value;
            }

            if (propDecl.AccessorList != null)
            {
                AccessorDeclarationSyntax? getAccessor = propDecl.AccessorList.Accessors
                    .FirstOrDefault(a => a.Kind() == SyntaxKind.GetAccessorDeclaration);
                if (getAccessor != null)
                {
                    if (getAccessor.ExpressionBody != null)
                    {
                        return getAccessor.ExpressionBody.Expression;
                    }

                    if (getAccessor.Body != null)
                    {
                        ReturnStatementSyntax? singleReturn = getAccessor.Body.Statements
                            .OfType<ReturnStatementSyntax>()
                            .FirstOrDefault();
                        return singleReturn?.Expression;
                    }
                }
            }

            return null;
        }

        private static List<AttributeRecord> ExtractAttributes(
            SyntaxList<AttributeListSyntax> attributeLists,
            IReadOnlyDictionary<string, string> usingAliases)
        {
            var result = new List<AttributeRecord>();
            foreach (AttributeListSyntax list in attributeLists)
            {
                foreach (AttributeSyntax attr in list.Attributes)
                {
                    string raw = FlattenName(attr.Name).Name;
                    string norm = NormalizeName(attr.Name, usingAliases);
                    result.Add(new AttributeRecord(raw, norm, GetLineNumber(attr)));
                }
            }

            return result;
        }

        public static string NormalizeTypeSyntax(TypeSyntax? typeSyntax, IReadOnlyDictionary<string, string> usingAliases)
        {
            if (typeSyntax == null)
            {
                return string.Empty;
            }

            if (typeSyntax is NullableTypeSyntax nullable)
            {
                return NormalizeTypeSyntax(nullable.ElementType, usingAliases);
            }

            if (typeSyntax is ArrayTypeSyntax array)
            {
                string elem = NormalizeTypeSyntax(array.ElementType, usingAliases);
                return string.IsNullOrEmpty(elem) ? string.Empty : elem + "[]";
            }

            if (typeSyntax is PredefinedTypeSyntax predefined)
            {
                return predefined.Keyword.ValueText;
            }

            return NormalizeName(typeSyntax, usingAliases);
        }

        public static string NormalizeName(SyntaxNode node, IReadOnlyDictionary<string, string> usingAliases)
        {
            (string raw, bool isGlobal) = FlattenName(node);
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            return ExpandAlias(raw, isGlobal, usingAliases);
        }

        public static string ExpandAlias(
            string rawName,
            bool isGlobalQualified,
            IReadOnlyDictionary<string, string> usingAliases)
        {
            if (string.IsNullOrEmpty(rawName) || isGlobalQualified || usingAliases.Count == 0)
            {
                return rawName;
            }

            if (usingAliases.TryGetValue(rawName, out string? directTarget))
            {
                return directTarget;
            }

            int firstDot = rawName.IndexOf('.');
            if (firstDot > 0)
            {
                string head = rawName.Substring(0, firstDot);
                if (usingAliases.TryGetValue(head, out string? prefixTarget))
                {
                    return prefixTarget + rawName.Substring(firstDot);
                }
            }

            return rawName;
        }

        public static (string Name, bool IsGlobalQualified) FlattenName(SyntaxNode? node)
        {
            if (node == null)
            {
                return (string.Empty, false);
            }

            switch (node)
            {
                case IdentifierNameSyntax id:
                    return (id.Identifier.ValueText, false);

                case GenericNameSyntax gn:
                    return (gn.Identifier.ValueText, false);

                case AliasQualifiedNameSyntax aqn:
                {
                    (string inner, _) = FlattenName(aqn.Name);
                    if (string.Equals(aqn.Alias.Identifier.ValueText, "global", StringComparison.Ordinal))
                    {
                        return (inner, true);
                    }

                    return (string.IsNullOrEmpty(inner)
                        ? aqn.Alias.Identifier.ValueText
                        : aqn.Alias.Identifier.ValueText + "." + inner, false);
                }

                case QualifiedNameSyntax qn:
                {
                    (string left, bool isGlobal) = FlattenName(qn.Left);
                    (string right, _) = FlattenName(qn.Right);
                    if (string.IsNullOrEmpty(left))
                    {
                        return (right, isGlobal);
                    }
                    if (string.IsNullOrEmpty(right))
                    {
                        return (left, isGlobal);
                    }
                    return (left + "." + right, isGlobal);
                }

                case MemberAccessExpressionSyntax ma:
                {
                    if (ma.Expression is ThisExpressionSyntax || ma.Expression is BaseExpressionSyntax)
                    {
                        return (ma.Name.Identifier.ValueText, false);
                    }

                    (string left, bool isGlobal) = FlattenName(ma.Expression);
                    (string right, _) = FlattenName(ma.Name);
                    if (string.IsNullOrEmpty(left))
                    {
                        return (string.Empty, false);
                    }
                    if (string.IsNullOrEmpty(right))
                    {
                        return (left, isGlobal);
                    }
                    return (left + "." + right, isGlobal);
                }

                case NullableTypeSyntax nullable:
                    return FlattenName(nullable.ElementType);

                case PredefinedTypeSyntax predefined:
                    return (predefined.Keyword.ValueText, false);

                default:
                    return (string.Empty, false);
            }
        }

        private static (string RawType, string NormalizedType) ResolveImplicitObjectCreationType(
            ImplicitObjectCreationExpressionSyntax node,
            IReadOnlyDictionary<string, string> usingAliases)
        {
            TypeSyntax? resolvedTypeSyntax = FindTargetTypeSyntax(node);
            if (resolvedTypeSyntax != null)
            {
                string raw = FlattenName(resolvedTypeSyntax).Name;
                string normalized = NormalizeTypeSyntax(resolvedTypeSyntax, usingAliases);
                return (raw, normalized);
            }

            if (node.Parent is AssignmentExpressionSyntax assign && assign.Right == node)
            {
                string? targetName = ExtractSimpleIdentifierName(assign.Left);
                if (!string.IsNullOrEmpty(targetName))
                {
                    TypeSyntax? declType = FindDeclaredTypeForIdentifier(node, targetName!);
                    if (declType != null)
                    {
                        string raw = FlattenName(declType).Name;
                        string normalized = NormalizeTypeSyntax(declType, usingAliases);
                        return (raw, normalized);
                    }
                }
            }

            return (string.Empty, string.Empty);
        }

        private static TypeSyntax? FindTargetTypeSyntax(SyntaxNode expression)
        {
            SyntaxNode? current = expression;
            while (current?.Parent != null)
            {
                SyntaxNode parent = current.Parent;

                if (parent is ParenthesizedExpressionSyntax)
                {
                    current = parent;
                    continue;
                }

                if (parent is BinaryExpressionSyntax bin &&
                    bin.IsKind(SyntaxKind.CoalesceExpression))
                {
                    if (bin.Left == current)
                    {
                        current = parent;
                        continue;
                    }

                    string? leftId = ExtractSimpleIdentifierName(bin.Left);
                    if (!string.IsNullOrEmpty(leftId))
                    {
                        TypeSyntax? leftType = FindDeclaredTypeForIdentifier(bin, leftId!);
                        if (leftType != null)
                        {
                            return leftType;
                        }
                    }

                    current = parent;
                    continue;
                }

                if (parent is ConditionalExpressionSyntax cond &&
                    (cond.WhenTrue == current || cond.WhenFalse == current))
                {
                    ExpressionSyntax sibling = cond.WhenTrue == current ? cond.WhenFalse : cond.WhenTrue;
                    if (sibling is ObjectCreationExpressionSyntax sibObj)
                    {
                        return sibObj.Type;
                    }

                    string? sibId = ExtractSimpleIdentifierName(sibling);
                    if (!string.IsNullOrEmpty(sibId))
                    {
                        TypeSyntax? sibType = FindDeclaredTypeForIdentifier(cond, sibId!);
                        if (sibType != null)
                        {
                            return sibType;
                        }
                    }

                    current = parent;
                    continue;
                }

                if (parent is CastExpressionSyntax cast)
                {
                    return cast.Type;
                }

                if (parent is EqualsValueClauseSyntax equalsClause)
                {
                    if (equalsClause.Parent is VariableDeclaratorSyntax varDecl &&
                        varDecl.Parent is VariableDeclarationSyntax varTypeDecl &&
                        !varTypeDecl.Type.IsVar)
                    {
                        return varTypeDecl.Type;
                    }

                    if (equalsClause.Parent is PropertyDeclarationSyntax propDecl)
                    {
                        return propDecl.Type;
                    }

                    if (equalsClause.Parent is ParameterSyntax paramDecl)
                    {
                        return paramDecl.Type;
                    }
                }

                if (parent is ReturnStatementSyntax)
                {
                    return FindEnclosingReturnTypeSyntax(parent);
                }

                if (parent is ArrowExpressionClauseSyntax arrow)
                {
                    if (arrow.Parent is MethodDeclarationSyntax m) return m.ReturnType;
                    if (arrow.Parent is LocalFunctionStatementSyntax lf) return lf.ReturnType;
                    if (arrow.Parent is PropertyDeclarationSyntax p) return p.Type;
                    if (arrow.Parent is IndexerDeclarationSyntax idx) return idx.Type;
                    if (arrow.Parent is AccessorDeclarationSyntax acc && acc.Parent?.Parent is PropertyDeclarationSyntax ap) return ap.Type;
                }

                if (parent is InitializerExpressionSyntax initExpr)
                {
                    TypeSyntax? elemType = TryGetCollectionElementType(initExpr);
                    if (elemType != null)
                    {
                        return elemType;
                    }
                }

                break;
            }

            return null;
        }

        private static TypeSyntax? TryGetCollectionElementType(InitializerExpressionSyntax initExpr)
        {
            TypeSyntax? containerType = null;
            if (initExpr.Parent is ObjectCreationExpressionSyntax objCreate)
            {
                containerType = objCreate.Type;
            }
            else if (initExpr.Parent is ArrayCreationExpressionSyntax arrayCreate)
            {
                return arrayCreate.Type.ElementType;
            }
            else if (initExpr.Parent is EqualsValueClauseSyntax eq &&
                     eq.Parent is VariableDeclaratorSyntax vd &&
                     vd.Parent is VariableDeclarationSyntax vdecl)
            {
                containerType = vdecl.Type;
            }

            if (containerType is ArrayTypeSyntax arr)
            {
                return arr.ElementType;
            }

            if (containerType is GenericNameSyntax gn && gn.TypeArgumentList.Arguments.Count == 1)
            {
                return gn.TypeArgumentList.Arguments[0];
            }

            if (containerType is QualifiedNameSyntax qn && qn.Right is GenericNameSyntax qgn && qgn.TypeArgumentList.Arguments.Count == 1)
            {
                return qgn.TypeArgumentList.Arguments[0];
            }

            return null;
        }

        private static TypeSyntax? FindEnclosingReturnTypeSyntax(SyntaxNode node)
        {
            foreach (SyntaxNode ancestor in node.Ancestors())
            {
                switch (ancestor)
                {
                    case LocalFunctionStatementSyntax lf:
                        return lf.ReturnType;
                    case MethodDeclarationSyntax m:
                        return m.ReturnType;
                    case PropertyDeclarationSyntax p:
                        return p.Type;
                    case IndexerDeclarationSyntax idx:
                        return idx.Type;
                    case LambdaExpressionSyntax:
                        return null;
                }
            }

            return null;
        }

        private static string? ExtractSimpleIdentifierName(ExpressionSyntax expr)
        {
            if (expr is IdentifierNameSyntax id)
            {
                return id.Identifier.ValueText;
            }

            if (expr is MemberAccessExpressionSyntax ma && ma.Expression is ThisExpressionSyntax)
            {
                return ma.Name.Identifier.ValueText;
            }

            return null;
        }

        private static TypeSyntax? FindDeclaredTypeForIdentifier(SyntaxNode origin, string identifier)
        {
            foreach (SyntaxNode ancestor in origin.Ancestors())
            {
                if (ancestor is BlockSyntax block)
                {
                    foreach (LocalDeclarationStatementSyntax local in block.Statements.OfType<LocalDeclarationStatementSyntax>())
                    {
                        if (local.Declaration.Type.IsVar)
                        {
                            continue;
                        }

                        if (local.Declaration.Variables.Any(v => string.Equals(v.Identifier.ValueText, identifier, StringComparison.Ordinal)))
                        {
                            return local.Declaration.Type;
                        }
                    }
                }
                else if (ancestor is BaseMethodDeclarationSyntax method)
                {
                    ParameterSyntax? param = method.ParameterList?.Parameters
                        .FirstOrDefault(p => string.Equals(p.Identifier.ValueText, identifier, StringComparison.Ordinal));
                    if (param?.Type != null)
                    {
                        return param.Type;
                    }
                }
                else if (ancestor is LocalFunctionStatementSyntax lf)
                {
                    ParameterSyntax? param = lf.ParameterList?.Parameters
                        .FirstOrDefault(p => string.Equals(p.Identifier.ValueText, identifier, StringComparison.Ordinal));
                    if (param?.Type != null)
                    {
                        return param.Type;
                    }
                }
                else if (ancestor is TypeDeclarationSyntax typeDecl)
                {
                    foreach (FieldDeclarationSyntax field in typeDecl.Members.OfType<FieldDeclarationSyntax>())
                    {
                        if (field.Declaration.Variables.Any(v => string.Equals(v.Identifier.ValueText, identifier, StringComparison.Ordinal)))
                        {
                            return field.Declaration.Type;
                        }
                    }

                    PropertyDeclarationSyntax? prop = typeDecl.Members.OfType<PropertyDeclarationSyntax>()
                        .FirstOrDefault(p => string.Equals(p.Identifier.ValueText, identifier, StringComparison.Ordinal));
                    if (prop != null)
                    {
                        return prop.Type;
                    }
                }
            }

            return null;
        }

        private static (string? EnclosingTypeFullName, string? EnclosingMethodName) GetEnclosingContext(SyntaxNode node)
        {
            string? methodName = null;
            TypeDeclarationSyntax? enclosingType = null;

            foreach (SyntaxNode ancestor in node.Ancestors())
            {
                if (methodName == null)
                {
                    if (ancestor is MethodDeclarationSyntax m) methodName = m.Identifier.ValueText;
                    else if (ancestor is ConstructorDeclarationSyntax c) methodName = c.Identifier.ValueText;
                    else if (ancestor is PropertyDeclarationSyntax p) methodName = p.Identifier.ValueText;
                }

                if (enclosingType == null && ancestor is TypeDeclarationSyntax td)
                {
                    enclosingType = td;
                }

                if (methodName != null && enclosingType != null)
                {
                    break;
                }
            }

            return (enclosingType != null ? GetFullTypeName(enclosingType) : null, methodName);
        }

        private static string GetEnclosingNamespace(SyntaxNode node)
        {
            var nsParts = node.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(n => FlattenName(n.Name).Name)
                .Where(s => !string.IsNullOrEmpty(s));

            return string.Join(".", nsParts);
        }

        private static string GetFullTypeName(TypeDeclarationSyntax typeDecl)
        {
            string ns = GetEnclosingNamespace(typeDecl);
            var typeChain = typeDecl.AncestorsAndSelf()
                .OfType<TypeDeclarationSyntax>()
                .Reverse()
                .Select(t => t.Identifier.ValueText);

            string nested = string.Join("+", typeChain);
            return string.IsNullOrEmpty(ns) ? nested : ns + "." + nested;
        }

        private static int GetLineNumber(SyntaxNode node)
        {
            return node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        }
    }
}
