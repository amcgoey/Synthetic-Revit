using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;

namespace SyntheticTests.Architecture
{
    public enum SourceArea
    {
        Src,
        Tests
    }

    public enum PartitionKind
    {
        None,
        Core,
        Addin,
        Logic,
        Revit,
        Shared,
        ProjectHead,
        Mock
    }

    public sealed class WorkspaceSourceFile
    {
        public WorkspaceSourceFile(
            SourceArea area,
            string fullPath,
            string relativeToRepoPath,
            string relativeToAreaPath,
            string topFolder,
            string? sliceName,
            string? roleFolder,
            PartitionKind partition,
            bool isInLegacyMockTests,
            IReadOnlyList<ParsedSyntaxVariant> variants)
        {
            Area = area;
            FullPath = fullPath;
            RelativeToRepoPath = relativeToRepoPath;
            RelativeToAreaPath = relativeToAreaPath;
            TopFolder = topFolder;
            SliceName = sliceName;
            RoleFolder = roleFolder;
            Partition = partition;
            IsInLegacyMockTests = isInLegacyMockTests;
            Variants = variants;
        }

        public SourceArea Area { get; }
        public string FullPath { get; }
        public string RelativeToRepoPath { get; }
        public string RelativeToAreaPath { get; }
        public string TopFolder { get; }
        public string? SliceName { get; }
        public string? RoleFolder { get; }
        public PartitionKind Partition { get; }
        public bool IsInLegacyMockTests { get; }
        public IReadOnlyList<ParsedSyntaxVariant> Variants { get; }
        public ParsedSyntaxVariant PrimaryVariant => Variants[0];
    }

    public sealed class XamlNamespaceBinding
    {
        public XamlNamespaceBinding(string prefix, string rawValue, string? clrNamespace, string? assemblyName)
        {
            Prefix = prefix;
            RawValue = rawValue;
            ClrNamespace = clrNamespace;
            AssemblyName = assemblyName;
        }

        public string Prefix { get; }
        public string RawValue { get; }
        public string? ClrNamespace { get; }
        public string? AssemblyName { get; }
    }

    public sealed class WorkspaceXamlFile
    {
        public WorkspaceXamlFile(
            SourceArea area,
            string fullPath,
            string relativeToRepoPath,
            string relativeToAreaPath,
            string topFolder,
            string? sliceName,
            PartitionKind partition,
            string rawText,
            XDocument document,
            IReadOnlyList<XamlNamespaceBinding> xmlnsBindings,
            bool mergesSyntheticThemeDictionary,
            bool referencesRelativeSyntheticThemeXaml,
            bool referencesSyntheticSharedUi)
        {
            Area = area;
            FullPath = fullPath;
            RelativeToRepoPath = relativeToRepoPath;
            RelativeToAreaPath = relativeToAreaPath;
            TopFolder = topFolder;
            SliceName = sliceName;
            Partition = partition;
            RawText = rawText;
            Document = document;
            XmlnsBindings = xmlnsBindings;
            MergesSyntheticThemeDictionary = mergesSyntheticThemeDictionary;
            ReferencesRelativeSyntheticThemeXaml = referencesRelativeSyntheticThemeXaml;
            ReferencesSyntheticSharedUi = referencesSyntheticSharedUi;
        }

        public SourceArea Area { get; }
        public string FullPath { get; }
        public string RelativeToRepoPath { get; }
        public string RelativeToAreaPath { get; }
        public string TopFolder { get; }
        public string? SliceName { get; }
        public PartitionKind Partition { get; }
        public string RawText { get; }
        public XDocument Document { get; }
        public IReadOnlyList<XamlNamespaceBinding> XmlnsBindings { get; }
        public bool MergesSyntheticThemeDictionary { get; }
        public bool ReferencesRelativeSyntheticThemeXaml { get; }
        public bool ReferencesSyntheticSharedUi { get; }
    }

    public sealed class WorkspaceXmlFile
    {
        public WorkspaceXmlFile(
            SourceArea area,
            string fullPath,
            string relativeToRepoPath,
            string relativeToAreaPath,
            string extension,
            string rawText,
            XDocument? document)
        {
            Area = area;
            FullPath = fullPath;
            RelativeToRepoPath = relativeToRepoPath;
            RelativeToAreaPath = relativeToAreaPath;
            Extension = extension;
            RawText = rawText;
            Document = document;
        }

        public SourceArea Area { get; }
        public string FullPath { get; }
        public string RelativeToRepoPath { get; }
        public string RelativeToAreaPath { get; }
        public string Extension { get; }
        public string RawText { get; }
        public XDocument? Document { get; }
    }

    public sealed class RibbonEntryRecord
    {
        public RibbonEntryRecord(
            string? type,
            string? name,
            string? className,
            string? availabilityClass,
            string? image,
            string? largeImage)
        {
            Type = type;
            Name = name;
            ClassName = className;
            AvailabilityClass = availabilityClass;
            Image = image;
            LargeImage = largeImage;
        }

        public string? Type { get; }
        public string? Name { get; }
        public string? ClassName { get; }
        public string? AvailabilityClass { get; }
        public string? Image { get; }
        public string? LargeImage { get; }
    }

    public sealed class RibbonConfigSnapshot
    {
        public RibbonConfigSnapshot(
            string configPath,
            string rawJson,
            JObject root,
            IReadOnlyList<RibbonEntryRecord> entries,
            IReadOnlyCollection<string> assetRelativePaths)
        {
            ConfigPath = configPath;
            RawJson = rawJson;
            Root = root;
            Entries = entries;
            AssetRelativePaths = assetRelativePaths;
        }

        public string ConfigPath { get; }
        public string RawJson { get; }
        public JObject Root { get; }
        public IReadOnlyList<RibbonEntryRecord> Entries { get; }
        public IReadOnlyCollection<string> AssetRelativePaths { get; }
    }

    public sealed class SettingsTemplateSnapshot
    {
        public SettingsTemplateSnapshot(
            string templatePath,
            string rawJson,
            JObject root,
            IReadOnlyList<string> settingsKeys)
        {
            TemplatePath = templatePath;
            RawJson = rawJson;
            Root = root;
            SettingsKeys = settingsKeys;
        }

        public string TemplatePath { get; }
        public string RawJson { get; }
        public JObject Root { get; }
        public IReadOnlyList<string> SettingsKeys { get; }
    }

    public sealed class TypeEntry
    {
        public TypeEntry(WorkspaceSourceFile file, ParsedSyntaxVariant variant, TypeDeclarationRecord type)
        {
            File = file;
            Variant = variant;
            Type = type;
        }

        public WorkspaceSourceFile File { get; }
        public ParsedSyntaxVariant Variant { get; }
        public TypeDeclarationRecord Type { get; }
    }

    /// <summary>
    /// Resolves Roslyn constant and simple getter string expressions across files and partitions.
    /// </summary>
    public sealed class RoslynConstantResolver
    {
        private readonly IReadOnlyDictionary<string, List<TypeEntry>> _byFullName;
        private readonly IReadOnlyDictionary<string, List<TypeEntry>> _bySimpleName;

        public RoslynConstantResolver(
            IReadOnlyDictionary<string, List<TypeEntry>> byFullName,
            IReadOnlyDictionary<string, List<TypeEntry>> bySimpleName)
        {
            _byFullName = byFullName;
            _bySimpleName = bySimpleName;
        }

        public string? TryResolveString(
            ExpressionSyntax? expression,
            TypeEntry? contextType,
            ParsedSyntaxVariant contextVariant)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            return ResolveInternal(expression, contextType, contextVariant, visited);
        }

        private string? ResolveInternal(
            ExpressionSyntax? expression,
            TypeEntry? contextType,
            ParsedSyntaxVariant contextVariant,
            HashSet<string> visited)
        {
            if (expression == null)
            {
                return null;
            }

            if (expression is ParenthesizedExpressionSyntax paren)
            {
                return ResolveInternal(paren.Expression, contextType, contextVariant, visited);
            }

            if (expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }

            if (expression is BinaryExpressionSyntax bin &&
                bin.IsKind(SyntaxKind.AddExpression))
            {
                string? left = ResolveInternal(bin.Left, contextType, contextVariant, visited);
                string? right = ResolveInternal(bin.Right, contextType, contextVariant, visited);
                if (left != null && right != null)
                {
                    return left + right;
                }

                return null;
            }

            if (expression is IdentifierNameSyntax id)
            {
                string memberName = id.Identifier.ValueText;
                if (contextType != null)
                {
                    return ResolveTypeMember(contextType, memberName, visited);
                }

                return null;
            }

            if (expression is MemberAccessExpressionSyntax ma)
            {
                string memberName = ma.Name.Identifier.ValueText;
                string normalizedReceiver = RoslynSyntaxNormalizer.NormalizeName(ma.Expression, contextVariant.UsingAliases);
                if (string.IsNullOrEmpty(normalizedReceiver))
                {
                    return null;
                }

                List<TypeEntry> candidates = FindCandidateTypes(normalizedReceiver, contextType?.Type.Namespace, contextVariant);
                foreach (TypeEntry candidate in candidates)
                {
                    string? resolved = ResolveTypeMember(candidate, memberName, visited);
                    if (resolved != null)
                    {
                        return resolved;
                    }
                }

                return null;
            }

            return null;
        }

        private string? ResolveTypeMember(TypeEntry typeEntry, string memberName, HashSet<string> visited)
        {
            string visitKey = typeEntry.Type.FullName + "::" + memberName;
            if (!visited.Add(visitKey))
            {
                return null;
            }

            try
            {
                ConstantFieldRecord? field = typeEntry.Type.ConstantFields
                    .FirstOrDefault(f => string.Equals(f.FieldName, memberName, StringComparison.Ordinal));
                if (field?.Initializer != null)
                {
                    string? fromField = ResolveInternal(field.Initializer, typeEntry, typeEntry.Variant, visited);
                    if (fromField != null)
                    {
                        return fromField;
                    }
                }

                PropertyDeclarationRecord? prop = typeEntry.Type.Properties
                    .FirstOrDefault(p => string.Equals(p.Name, memberName, StringComparison.Ordinal));
                if (prop?.GetterExpression != null)
                {
                    string? fromProp = ResolveInternal(prop.GetterExpression, typeEntry, typeEntry.Variant, visited);
                    if (fromProp != null)
                    {
                        return fromProp;
                    }
                }

                return null;
            }
            finally
            {
                visited.Remove(visitKey);
            }
        }

        public List<TypeEntry> FindCandidateTypes(
            string typeReference,
            string? currentNamespace,
            ParsedSyntaxVariant contextVariant)
        {
            string normalized = RoslynSyntaxNormalizer.ExpandAlias(typeReference, isGlobalQualified: false, contextVariant.UsingAliases);

            if (_byFullName.TryGetValue(normalized, out List<TypeEntry>? exact))
            {
                return exact;
            }

            if (!string.IsNullOrEmpty(currentNamespace))
            {
                string sameNs = currentNamespace + "." + normalized;
                if (_byFullName.TryGetValue(sameNs, out List<TypeEntry>? inCurrentNs))
                {
                    return inCurrentNs;
                }
            }

            foreach (UsingRecord u in contextVariant.Usings)
            {
                if (!string.IsNullOrEmpty(u.Alias))
                {
                    continue;
                }

                string candidate = u.NormalizedTarget + "." + normalized;
                if (_byFullName.TryGetValue(candidate, out List<TypeEntry>? viaUsing))
                {
                    return viaUsing;
                }
            }

            if (_bySimpleName.TryGetValue(normalized, out List<TypeEntry>? bySimple))
            {
                return bySimple;
            }

            return new List<TypeEntry>();
        }
    }

    /// <summary>
    /// Immutable single-parse Roslyn + XML/JSON workspace snapshot shared across architecture tests.
    /// </summary>
    public sealed class ArchitectureWorkspaceSnapshot
    {
        private static readonly ConcurrentDictionary<string, Lazy<ArchitectureWorkspaceSnapshot>> SharedCache =
            new ConcurrentDictionary<string, Lazy<ArchitectureWorkspaceSnapshot>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex LatestVersionRegex = new Regex(
            @"\bCURRENT_LATEST_VERSION\b[^\r\n]*?\b(20\d{2})\b",
            RegexOptions.CultureInvariant);

        private static readonly HashSet<string> XmlBuildExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".csproj",
            ".targets",
            ".props",
            ".shproj",
            ".projitems"
        };

        private ArchitectureWorkspaceSnapshot(
            string projectRoot,
            string srcRoot,
            string testsRoot,
            int currentLatestVersion,
            IReadOnlyList<WorkspaceSourceFile> sourceFiles,
            IReadOnlyList<WorkspaceXamlFile> xamlFiles,
            IReadOnlyList<WorkspaceXmlFile> projectAndBuildFiles,
            RibbonConfigSnapshot ribbonConfig,
            SettingsTemplateSnapshot settingsTemplate,
            IReadOnlyList<TypeEntry> allSrcTypes,
            IReadOnlyList<TypeEntry> allTestTypes,
            IReadOnlyDictionary<string, List<TypeEntry>> srcTypesByFullName,
            IReadOnlyDictionary<string, List<TypeEntry>> testTypesByFullName,
            RoslynConstantResolver constantResolver,
            TimeSpan loadDuration)
        {
            ProjectRoot = projectRoot;
            SrcRoot = srcRoot;
            TestsRoot = testsRoot;
            CurrentLatestVersion = currentLatestVersion;
            SourceFiles = sourceFiles;
            XamlFiles = xamlFiles;
            ProjectAndBuildFiles = projectAndBuildFiles;
            RibbonConfig = ribbonConfig;
            SettingsTemplate = settingsTemplate;
            AllSrcTypes = allSrcTypes;
            AllTestTypes = allTestTypes;
            SrcTypesByFullName = srcTypesByFullName;
            TestTypesByFullName = testTypesByFullName;
            ConstantResolver = constantResolver;
            LoadDuration = loadDuration;
        }

        public string ProjectRoot { get; }
        public string SrcRoot { get; }
        public string TestsRoot { get; }
        public int CurrentLatestVersion { get; }
        public IReadOnlyList<WorkspaceSourceFile> SourceFiles { get; }
        public IReadOnlyList<WorkspaceXamlFile> XamlFiles { get; }
        public IReadOnlyList<WorkspaceXmlFile> ProjectAndBuildFiles { get; }
        public RibbonConfigSnapshot RibbonConfig { get; }
        public SettingsTemplateSnapshot SettingsTemplate { get; }
        public IReadOnlyList<TypeEntry> AllSrcTypes { get; }
        public IReadOnlyList<TypeEntry> AllTestTypes { get; }
        public IReadOnlyDictionary<string, List<TypeEntry>> SrcTypesByFullName { get; }
        public IReadOnlyDictionary<string, List<TypeEntry>> TestTypesByFullName { get; }
        public RoslynConstantResolver ConstantResolver { get; }
        public TimeSpan LoadDuration { get; }

        public static ArchitectureWorkspaceSnapshot GetShared(string projectRoot)
        {
            string normalized = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Lazy<ArchitectureWorkspaceSnapshot> lazy = SharedCache.GetOrAdd(
                normalized,
                root => new Lazy<ArchitectureWorkspaceSnapshot>(() => Load(root)));
            return lazy.Value;
        }

        public static ArchitectureWorkspaceSnapshot Load(string projectRoot)
        {
            var stopwatch = Stopwatch.StartNew();

            string normalizedRoot = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string srcRoot = Path.Combine(normalizedRoot, "src");
            string testsRoot = Path.Combine(normalizedRoot, "tests");

            int currentLatestVersion = ParseCurrentLatestVersion(normalizedRoot);

            var csJobs = new List<(SourceArea Area, string FullPath, string AreaRoot)>();
            var xamlJobs = new List<(SourceArea Area, string FullPath, string AreaRoot)>();
            var xmlJobs = new List<(SourceArea Area, string FullPath, string AreaRoot, string Ext)>();

            CollectFiles(srcRoot, SourceArea.Src, csJobs, xamlJobs, xmlJobs);
            CollectFiles(testsRoot, SourceArea.Tests, csJobs, xamlJobs, xmlJobs);

            var sourceResults = new WorkspaceSourceFile[csJobs.Count];
            Parallel.For(0, csJobs.Count, i =>
            {
                var job = csJobs[i];
                string text = File.ReadAllText(job.FullPath);
                string relRepo = GetRelativeNormalizedPath(normalizedRoot, job.FullPath);
                string relArea = GetRelativeNormalizedPath(job.AreaRoot, job.FullPath);
                var (topFolder, sliceName, roleFolder, partition, isLegacyMock) = ClassifyPath(job.Area, relArea);
                IReadOnlyList<ParsedSyntaxVariant> variants = RoslynSyntaxNormalizer.ParseVariants(
                    text,
                    job.FullPath,
                    currentLatestVersion);

                sourceResults[i] = new WorkspaceSourceFile(
                    job.Area,
                    job.FullPath,
                    relRepo,
                    relArea,
                    topFolder,
                    sliceName,
                    roleFolder,
                    partition,
                    isLegacyMock,
                    variants);
            });

            var xamlResults = new WorkspaceXamlFile[xamlJobs.Count];
            Parallel.For(0, xamlJobs.Count, i =>
            {
                var job = xamlJobs[i];
                string rawText = File.ReadAllText(job.FullPath);
                XDocument doc = XDocument.Parse(rawText);
                string relRepo = GetRelativeNormalizedPath(normalizedRoot, job.FullPath);
                string relArea = GetRelativeNormalizedPath(job.AreaRoot, job.FullPath);
                var (topFolder, sliceName, _, partition, _) = ClassifyPath(job.Area, relArea);
                List<XamlNamespaceBinding> xmlnsBindings = ExtractXmlnsBindings(doc);

                bool mergesTheme = doc.Descendants()
                    .Any(el => string.Equals(el.Name.LocalName, "SyntheticThemeDictionary", StringComparison.Ordinal));
                bool hasRelativeTheme = rawText.IndexOf("SyntheticTheme.xaml", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasSharedUi = rawText.IndexOf("Synthetic.Shared.UI", StringComparison.Ordinal) >= 0;

                xamlResults[i] = new WorkspaceXamlFile(
                    job.Area,
                    job.FullPath,
                    relRepo,
                    relArea,
                    topFolder,
                    sliceName,
                    partition,
                    rawText,
                    doc,
                    xmlnsBindings,
                    mergesTheme,
                    hasRelativeTheme,
                    hasSharedUi);
            });

            var xmlResults = new WorkspaceXmlFile[xmlJobs.Count];
            Parallel.For(0, xmlJobs.Count, i =>
            {
                var job = xmlJobs[i];
                string rawText = File.ReadAllText(job.FullPath);
                XDocument? doc = null;
                if (!string.Equals(job.Ext, ".shproj", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(job.Ext, ".projitems", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        doc = XDocument.Parse(rawText);
                    }
                    catch
                    {
                        doc = null;
                    }
                }
                else
                {
                    try
                    {
                        doc = XDocument.Parse(rawText);
                    }
                    catch
                    {
                        doc = null;
                    }
                }

                xmlResults[i] = new WorkspaceXmlFile(
                    job.Area,
                    job.FullPath,
                    GetRelativeNormalizedPath(normalizedRoot, job.FullPath),
                    GetRelativeNormalizedPath(job.AreaRoot, job.FullPath),
                    job.Ext,
                    rawText,
                    doc);
            });

            RibbonConfigSnapshot ribbonConfig = LoadRibbonConfig(srcRoot);
            SettingsTemplateSnapshot settingsTemplate = LoadSettingsTemplate(srcRoot);

            var allSrcTypes = new List<TypeEntry>();
            var allTestTypes = new List<TypeEntry>();
            var srcByFull = new Dictionary<string, List<TypeEntry>>(StringComparer.Ordinal);
            var srcBySimple = new Dictionary<string, List<TypeEntry>>(StringComparer.Ordinal);
            var testByFull = new Dictionary<string, List<TypeEntry>>(StringComparer.Ordinal);

            foreach (WorkspaceSourceFile file in sourceResults)
            {
                foreach (ParsedSyntaxVariant variant in file.Variants)
                {
                    foreach (TypeDeclarationRecord type in variant.Types)
                    {
                        var entry = new TypeEntry(file, variant, type);
                        if (file.Area == SourceArea.Src)
                        {
                            allSrcTypes.Add(entry);
                            AddToBucket(srcByFull, type.FullName, entry);
                            AddToBucket(srcBySimple, type.Name, entry);
                        }
                        else
                        {
                            allTestTypes.Add(entry);
                            AddToBucket(testByFull, type.FullName, entry);
                        }
                    }
                }
            }

            var constantResolver = new RoslynConstantResolver(srcByFull, srcBySimple);
            stopwatch.Stop();

            return new ArchitectureWorkspaceSnapshot(
                normalizedRoot,
                srcRoot,
                testsRoot,
                currentLatestVersion,
                sourceResults,
                xamlResults,
                xmlResults,
                ribbonConfig,
                settingsTemplate,
                allSrcTypes,
                allTestTypes,
                srcByFull,
                testByFull,
                constantResolver,
                stopwatch.Elapsed);
        }

        public static ArchitectureWorkspaceSnapshot CreateSyntheticForTest(
            int currentLatestVersion,
            IReadOnlyList<WorkspaceSourceFile> sourceFiles,
            IReadOnlyList<WorkspaceXamlFile>? xamlFiles = null,
            IReadOnlyList<WorkspaceXmlFile>? projectAndBuildFiles = null,
            RibbonConfigSnapshot? ribbonConfig = null,
            SettingsTemplateSnapshot? settingsTemplate = null,
            string projectRoot = "synthetic://root")
        {
            var allSrcTypes = new List<TypeEntry>();
            var allTestTypes = new List<TypeEntry>();
            var srcByFull = new Dictionary<string, List<TypeEntry>>(StringComparer.Ordinal);
            var srcBySimple = new Dictionary<string, List<TypeEntry>>(StringComparer.Ordinal);
            var testByFull = new Dictionary<string, List<TypeEntry>>(StringComparer.Ordinal);

            foreach (WorkspaceSourceFile file in sourceFiles)
            {
                foreach (ParsedSyntaxVariant variant in file.Variants)
                {
                    foreach (TypeDeclarationRecord type in variant.Types)
                    {
                        var entry = new TypeEntry(file, variant, type);
                        if (file.Area == SourceArea.Src)
                        {
                            allSrcTypes.Add(entry);
                            AddToBucket(srcByFull, type.FullName, entry);
                            AddToBucket(srcBySimple, type.Name, entry);
                        }
                        else
                        {
                            allTestTypes.Add(entry);
                            AddToBucket(testByFull, type.FullName, entry);
                        }
                    }
                }
            }

            return new ArchitectureWorkspaceSnapshot(
                projectRoot,
                Path.Combine(projectRoot, "src"),
                Path.Combine(projectRoot, "tests"),
                currentLatestVersion,
                sourceFiles,
                xamlFiles ?? Array.Empty<WorkspaceXamlFile>(),
                projectAndBuildFiles ?? Array.Empty<WorkspaceXmlFile>(),
                ribbonConfig ?? new RibbonConfigSnapshot(string.Empty, "{}", new JObject(), Array.Empty<RibbonEntryRecord>(), Array.Empty<string>()),
                settingsTemplate ?? new SettingsTemplateSnapshot(string.Empty, "{}", new JObject(), Array.Empty<string>()),
                allSrcTypes,
                allTestTypes,
                srcByFull,
                testByFull,
                new RoslynConstantResolver(srcByFull, srcBySimple),
                TimeSpan.Zero);
        }

        public static WorkspaceSourceFile CreateSyntheticSourceFile(
            SourceArea area,
            string relativeToAreaPath,
            string sourceText,
            int currentLatestVersion = 2026)
        {
            string normalizedAreaPath = relativeToAreaPath.Replace('\\', '/').TrimStart('/');
            string prefix = area == SourceArea.Src ? "src/" : "tests/";
            string relRepo = prefix + normalizedAreaPath;
            var (topFolder, sliceName, roleFolder, partition, isLegacyMock) = ClassifyPath(area, normalizedAreaPath);
            IReadOnlyList<ParsedSyntaxVariant> variants = RoslynSyntaxNormalizer.ParseVariants(
                sourceText,
                relRepo,
                currentLatestVersion);

            return new WorkspaceSourceFile(
                area,
                relRepo,
                relRepo,
                normalizedAreaPath,
                topFolder,
                sliceName,
                roleFolder,
                partition,
                isLegacyMock,
                variants);
        }

        private static void AddToBucket<TKey, TVal>(Dictionary<TKey, List<TVal>> dict, TKey key, TVal val)
            where TKey : notnull
        {
            if (!dict.TryGetValue(key, out List<TVal>? list))
            {
                list = new List<TVal>();
                dict[key] = list;
            }
            list.Add(val);
        }

        private static int ParseCurrentLatestVersion(string projectRoot)
        {
            string configPath = Path.Combine(projectRoot, "docs", "agents", "revit-config.md");
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Expected docs/agents/revit-config.md at '{configPath}'.", configPath);
            }

            string text = File.ReadAllText(configPath);
            Match match = LatestVersionRegex.Match(text);
            if (!match.Success ||
                !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
            {
                throw new InvalidOperationException("Failed to parse CURRENT_LATEST_VERSION from docs/agents/revit-config.md.");
            }

            return version;
        }

        private static void CollectFiles(
            string rootDir,
            SourceArea area,
            List<(SourceArea Area, string FullPath, string AreaRoot)> csJobs,
            List<(SourceArea Area, string FullPath, string AreaRoot)> xamlJobs,
            List<(SourceArea Area, string FullPath, string AreaRoot, string Ext)> xmlJobs)
        {
            if (!Directory.Exists(rootDir))
            {
                return;
            }

            var stack = new Stack<string>();
            stack.Push(rootDir);

            while (stack.Count > 0)
            {
                string currentDir = stack.Pop();
                foreach (string subDir in Directory.EnumerateDirectories(currentDir))
                {
                    string dirName = Path.GetFileName(subDir);
                    if (string.Equals(dirName, "obj", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "bin", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "TestResults", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "packages", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, ".git", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    stack.Push(subDir);
                }

                foreach (string file in Directory.EnumerateFiles(currentDir))
                {
                    string ext = Path.GetExtension(file);
                    if (string.Equals(ext, ".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        csJobs.Add((area, file, rootDir));
                    }
                    else if (string.Equals(ext, ".xaml", StringComparison.OrdinalIgnoreCase))
                    {
                        xamlJobs.Add((area, file, rootDir));
                    }
                    else if (XmlBuildExtensions.Contains(ext))
                    {
                        xmlJobs.Add((area, file, rootDir, ext));
                    }
                }
            }
        }

        public static (string TopFolder, string? SliceName, string? RoleFolder, PartitionKind Partition, bool IsLegacyMock)
            ClassifyPath(SourceArea area, string relativeToAreaPath)
        {
            string[] segments = relativeToAreaPath.Split('/');
            string topFolder = segments.Length > 0 ? segments[0] : string.Empty;

            if (area == SourceArea.Src)
            {
                string? sliceName = null;
                int searchStartIndex = 1;
                if ((string.Equals(topFolder, "Features", StringComparison.Ordinal) ||
                     string.Equals(topFolder, "Platform", StringComparison.Ordinal) ||
                     string.Equals(topFolder, "Domain", StringComparison.Ordinal)) &&
                    segments.Length >= 2)
                {
                    sliceName = segments[1];
                    searchStartIndex = 2;
                }

                PartitionKind partition = PartitionKind.None;
                string? roleFolder = null;

                if (string.Equals(topFolder, "Synthetic.Core", StringComparison.Ordinal) ||
                    topFolder.StartsWith("Synthetic.Bootstrap", StringComparison.Ordinal) ||
                    Regex.IsMatch(topFolder, @"^Synthetic20\d{2}$", RegexOptions.CultureInvariant))
                {
                    partition = PartitionKind.ProjectHead;
                }
                else
                {
                    for (int idx = searchStartIndex; idx < segments.Length; idx++)
                    {
                        if (string.Equals(segments[idx], "Core", StringComparison.Ordinal))
                        {
                            partition = PartitionKind.Core;
                            if (idx + 2 < segments.Length)
                            {
                                roleFolder = segments[idx + 1];
                            }
                            break;
                        }
                        if (string.Equals(segments[idx], "Addin", StringComparison.Ordinal))
                        {
                            partition = PartitionKind.Addin;
                            if (idx + 2 < segments.Length)
                            {
                                roleFolder = segments[idx + 1];
                            }
                            break;
                        }
                    }
                }

                return (topFolder, sliceName, roleFolder, partition, false);
            }
            else
            {
                if (string.Equals(topFolder, "RevitAPIMock", StringComparison.Ordinal) ||
                    string.Equals(topFolder, "RevitAPIUIMock", StringComparison.Ordinal))
                {
                    return (topFolder, null, null, PartitionKind.Mock, false);
                }

                if (string.Equals(topFolder, "Projects", StringComparison.Ordinal))
                {
                    bool isLegacyMock = segments.Length >= 2 &&
                                        string.Equals(segments[1], "Synthetic.LegacyMockTests", StringComparison.Ordinal);
                    return (topFolder, segments.Length >= 2 ? segments[1] : null, null, PartitionKind.ProjectHead, isLegacyMock);
                }

                if (string.Equals(topFolder, "Fixtures", StringComparison.Ordinal))
                {
                    PartitionKind fixturePart = PartitionKind.None;
                    if (segments.Length >= 2)
                    {
                        if (string.Equals(segments[1], "Shared", StringComparison.Ordinal))
                            fixturePart = PartitionKind.Shared;
                        else if (string.Equals(segments[1], "Revit", StringComparison.Ordinal))
                            fixturePart = PartitionKind.Revit;
                    }
                    return (topFolder, null, null, fixturePart, false);
                }

                string? sliceName = null;
                int searchStartIndex = 1;
                if ((string.Equals(topFolder, "Features", StringComparison.Ordinal) ||
                     string.Equals(topFolder, "Platform", StringComparison.Ordinal) ||
                     string.Equals(topFolder, "Domain", StringComparison.Ordinal)) &&
                    segments.Length >= 2)
                {
                    sliceName = segments[1];
                    searchStartIndex = 2;
                }

                PartitionKind partition = PartitionKind.None;
                for (int idx = searchStartIndex; idx < segments.Length; idx++)
                {
                    if (string.Equals(segments[idx], "Logic", StringComparison.Ordinal))
                    {
                        partition = PartitionKind.Logic;
                        break;
                    }
                    if (string.Equals(segments[idx], "Revit", StringComparison.Ordinal))
                    {
                        partition = PartitionKind.Revit;
                        break;
                    }
                }

                return (topFolder, sliceName, null, partition, false);
            }
        }

        private static List<XamlNamespaceBinding> ExtractXmlnsBindings(XDocument doc)
        {
            var bindings = new List<XamlNamespaceBinding>();
            foreach (XElement element in doc.Descendants())
            {
                foreach (XAttribute attr in element.Attributes())
                {
                    if (!attr.IsNamespaceDeclaration)
                    {
                        continue;
                    }

                    string prefix = string.Equals(attr.Name.NamespaceName, XNamespace.Xmlns.NamespaceName, StringComparison.Ordinal)
                        ? attr.Name.LocalName
                        : string.Empty;
                    string rawVal = attr.Value;
                    string? clrNs = null;
                    string? asm = null;

                    if (rawVal.StartsWith("clr-namespace:", StringComparison.Ordinal))
                    {
                        string body = rawVal.Substring("clr-namespace:".Length);
                        foreach (string part in body.Split(';'))
                        {
                            string trimmed = part.Trim();
                            if (trimmed.StartsWith("assembly=", StringComparison.Ordinal))
                            {
                                asm = trimmed.Substring("assembly=".Length).Trim();
                            }
                            else if (clrNs == null && !string.IsNullOrEmpty(trimmed))
                            {
                                clrNs = trimmed;
                            }
                        }
                    }

                    bindings.Add(new XamlNamespaceBinding(prefix, rawVal, clrNs, asm));
                }
            }

            return bindings;
        }

        private static RibbonConfigSnapshot LoadRibbonConfig(string srcRoot)
        {
            string assetsDir = Path.Combine(srcRoot, "Assets");
            string configPath = Path.Combine(assetsDir, "ribbon_config.json");
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Expected ribbon_config.json at '{configPath}'.", configPath);
            }

            string rawJson = File.ReadAllText(configPath);
            JObject root = JObject.Parse(rawJson);

            var entries = new List<RibbonEntryRecord>();
            CollectRibbonEntries(root, entries);

            var assetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(assetsDir))
            {
                foreach (string file in Directory.EnumerateFiles(assetsDir, "*.*", SearchOption.AllDirectories))
                {
                    assetPaths.Add(GetRelativeNormalizedPath(assetsDir, file));
                }
            }

            return new RibbonConfigSnapshot(configPath, rawJson, root, entries, assetPaths);
        }

        private static void CollectRibbonEntries(JToken token, List<RibbonEntryRecord> entries)
        {
            if (token is JArray array)
            {
                foreach (JToken child in array)
                {
                    CollectRibbonEntries(child, entries);
                }
                return;
            }

            if (token is JObject obj)
            {
                string? type = obj.Value<string>("type");
                string? name = obj.Value<string>("name");
                string? className = obj.Value<string>("class");
                string? availClass = obj.Value<string>("availability_class");
                string? image = obj.Value<string>("image");
                string? largeImage = obj.Value<string>("large_image");

                if (!string.IsNullOrEmpty(type) ||
                    !string.IsNullOrEmpty(className) ||
                    !string.IsNullOrEmpty(image) ||
                    !string.IsNullOrEmpty(largeImage))
                {
                    entries.Add(new RibbonEntryRecord(type, name, className, availClass, image, largeImage));
                }

                foreach (JProperty prop in obj.Properties())
                {
                    if (prop.Value is JArray || prop.Value is JObject)
                    {
                        CollectRibbonEntries(prop.Value, entries);
                    }
                }
            }
        }

        private static SettingsTemplateSnapshot LoadSettingsTemplate(string srcRoot)
        {
            string templatePath = Path.Combine(srcRoot, "Platform", "Settings", "Addin", "Defaults", "SyntheticSettings.template.json");
            if (!File.Exists(templatePath))
            {
                throw new FileNotFoundException($"Expected SyntheticSettings.template.json at '{templatePath}'.", templatePath);
            }

            string rawJson = File.ReadAllText(templatePath);
            JObject root = JObject.Parse(rawJson);
            var keys = new List<string>();
            if (root["Settings"] is JObject settingsObj)
            {
                foreach (JProperty prop in settingsObj.Properties())
                {
                    keys.Add(prop.Name);
                }
            }

            return new SettingsTemplateSnapshot(templatePath, rawJson, root, keys);
        }

        public static string GetRelativeNormalizedPath(string rootDir, string fullPath)
        {
            string normalizedRoot = Path.GetFullPath(rootDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedFull = Path.GetFullPath(fullPath);
            if (normalizedFull.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFull.Substring(normalizedRoot.Length).Replace('\\', '/');
            }

            return normalizedFull.Replace('\\', '/');
        }
    }
}
