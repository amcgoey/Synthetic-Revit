using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Events;
using NUnit.Framework;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_AssemblyAnalyzerTests
    {
        private UIApplication? _uiapp;

        // Shared document opened once for all analyzer tests to avoid repeated
        // heavy worksharing session opens that cause Revit shutdown crashes.
        private Document? _sharedDoc;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;

            string projectRoot = GetProjectRoot();
            
            // Resolve target Revit version suffix
            string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? "";
            string revitVersion = assemblyName.Replace("SyntheticTests", ""); // e.g. "2026", "2024", etc.
            if (string.IsNullOrEmpty(revitVersion))
            {
                revitVersion = "Shared";
            }
            
            string modelFolder = Path.Combine(projectRoot, "tests", "test_models");
            string originalModelPath = Path.Combine(modelFolder, "INC Standards - Assemblies & Details.rvt");
            string versionModelPath = Path.Combine(modelFolder, $"INC Standards - Assemblies & Details{revitVersion}.rvt");

            var app = _uiapp.Application;
            app.FailuresProcessing += ResolveWarnings;

            try
            {
                OpenOptions openOptions = new OpenOptions
                {
                    DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
                };

                if (File.Exists(versionModelPath))
                {
                    ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(versionModelPath);
                    _sharedDoc = app.OpenDocumentFile(modelPath, openOptions);
                }
                else
                {
                    if (!File.Exists(originalModelPath))
                    {
                        Console.WriteLine($"[WARN] AssemblyAnalyzer base test model not found: {originalModelPath}. Tests will be skipped.");
                        return;
                    }
                    
                    Console.WriteLine($"[INFO] Upgrading base model to Revit {revitVersion} and saving to: {versionModelPath}...");
                    ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(originalModelPath);
                    _sharedDoc = app.OpenDocumentFile(modelPath, openOptions);
                    
                    if (_sharedDoc != null)
                    {
                        SaveAsOptions saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                        _sharedDoc.SaveAs(versionModelPath, saveOptions);
                        Console.WriteLine("[INFO] Upgraded model saved successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to open/upgrade shared document in OneTimeSetUp: {ex.Message}");
                _sharedDoc = null;
            }
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (_uiapp != null)
            {
                _uiapp.Application.FailuresProcessing -= ResolveWarnings;
            }

            // Guard with IsValidObject to prevent double-close crash with ricaun adapter.
            if (_sharedDoc != null && _sharedDoc.IsValidObject)
            {
                try { _sharedDoc.Close(false); }
                catch (Exception ex) { Console.WriteLine($"[WARN] OneTimeTearDown doc.Close failed: {ex.Message}"); }
            }
            _sharedDoc = null;
        }

        [Test]
        public void AnalyzeBaseClassesAndEmitFluentAPI()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");
            Assert.IsNotNull(_sharedDoc, "Shared test document was not opened in OneTimeSetUp. Check that the model file exists.");

            Document? doc = _sharedDoc;
            Assert.IsTrue(doc!.IsValidObject, "Shared document is no longer valid.");

            try
            {

                StringBuilder finalOutput = new StringBuilder();
                finalOutput.AppendLine("// ==========================================================================");
                finalOutput.AppendLine("// AUTOMATICALLY GENERATED FLUENT API PROPERTY REGISTRATIONS FOR BASE CLASSES");
                finalOutput.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                finalOutput.AppendLine("// ==========================================================================");
                finalOutput.AppendLine();

                Type[] targetTypes = new[]
                {
                    typeof(Element),
                    typeof(ElementType),
                    typeof(Material),
                    typeof(ViewPlan),
                    typeof(WallType),
                    typeof(Category),
                    typeof(FillPatternElement),
                    typeof(ParameterFilterElement),
                    typeof(DimensionType)
                };
                HashSet<Type> processedTypes = new HashSet<Type>();

                foreach (Type targetType in targetTypes)
                {
                    object? dummyInstance = null;

                    if (targetType == typeof(Element))
                    {
                        dummyInstance = new FilteredElementCollector(doc)
                            .WhereElementIsNotElementType()
                            .FirstOrDefault(x => x.Category != null)
                            ?? new FilteredElementCollector(doc).WhereElementIsNotElementType().FirstOrDefault();
                    }
                    else if (targetType == typeof(ElementType))
                    {
                        dummyInstance = new FilteredElementCollector(doc)
                            .WhereElementIsElementType()
                            .FirstOrDefault();
                    }
                    else if (targetType == typeof(Category))
                    {
                        dummyInstance = doc.Settings.Categories.Cast<Category>().FirstOrDefault();
                    }
                    else
                    {
                        dummyInstance = new FilteredElementCollector(doc)
                            .OfClass(targetType)
                            .FirstOrDefault();
                    }

                    if (dummyInstance == null)
                    {
                        string warnMsg = $"// [WARNING] No live instance of {targetType.Name} found in the document. Skipping dry-run.";
                        finalOutput.AppendLine(warnMsg);
                        finalOutput.AppendLine();
                        Console.WriteLine(warnMsg);
                        continue;
                    }

                    finalOutput.AppendLine($"// ==========================================================================");
                    finalOutput.AppendLine($"// TARGET CLASS: {targetType.FullName}");
                    finalOutput.AppendLine($"// ==========================================================================");
                    finalOutput.AppendLine();

                    // Walk up the inheritance chain: e.g. ElementType -> Element -> APIObject
                    List<Type> inheritanceChain = new List<Type>();
                    Type? current = targetType;
                    while (current != null && current != typeof(object))
                    {
                        inheritanceChain.Add(current);
                        current = current.BaseType;
                    }
                    inheritanceChain.Reverse(); // Walk base-first: APIObject, Element, etc.

                    // Get all public instance properties
                    PropertyInfo[] allProps = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                    foreach (Type declaringType in inheritanceChain)
                    {
                        if (processedTypes.Contains(declaringType))
                            continue;

                        var props = allProps
                            .Where(p => p.DeclaringType == declaringType && p.GetIndexParameters().Length == 0)
                            .OrderBy(p => p.Name)
                            .ToList();

                        if (props.Count == 0)
                            continue;

                        processedTypes.Add(declaringType);

                        finalOutput.AppendLine($"// --------------------------------------------------------------------------");
                        finalOutput.AppendLine($"// Declaring Type: {declaringType.FullName}");
                        finalOutput.AppendLine($"// --------------------------------------------------------------------------");
                        finalOutput.AppendLine($"registry.ForType<{declaringType.Name}>()");

                        for (int i = 0; i < props.Count; i++)
                        {
                            var prop = props[i];
                            bool canWrite = prop.CanWrite && prop.GetSetMethod() != null;
                            string suffix = (i == props.Count - 1) ? ";" : "";

                            try
                            {
                                // Dry-run property read invocation
                                var val = prop.GetValue(dummyInstance);

                                // If read succeeds, generate Fluent API string
                                if (canWrite)
                                {
                                    finalOutput.AppendLine($"    .Register(\"{prop.Name}\", x => x.{prop.Name}, (x, v) => x.{prop.Name} = v){suffix}");
                                }
                                // Special case for Element.Id or ElementType.Id since they don't have standard setters but are read-only properties
                                else
                                {
                                    finalOutput.AppendLine($"    .RegisterReadOnly(\"{prop.Name}\", x => x.{prop.Name}){suffix}");
                                }
                            }
                            catch (TargetInvocationException ex)
                            {
                                var innerEx = ex.InnerException ?? ex;
                                string message = innerEx.Message.Replace("\r\n", " ").Replace("\n", " ");
                                if (canWrite)
                                {
                                    finalOutput.AppendLine($"    // [Exception: {innerEx.GetType().Name} - {message}] .Register(\"{prop.Name}\", x => x.{prop.Name}, (x, v) => x.{prop.Name} = v){suffix}");
                                }
                                else
                                {
                                    finalOutput.AppendLine($"    // [Exception: {innerEx.GetType().Name} - {message}] .RegisterReadOnly(\"{prop.Name}\", x => x.{prop.Name}){suffix}");
                                }
                            }
                            catch (Exception ex)
                            {
                                string message = ex.Message.Replace("\r\n", " ").Replace("\n", " ");
                                if (canWrite)
                                {
                                    finalOutput.AppendLine($"    // [Exception: {ex.GetType().Name} - {message}] .Register(\"{prop.Name}\", x => x.{prop.Name}, (x, v) => x.{prop.Name} = v){suffix}");
                                }
                                else
                                {
                                    finalOutput.AppendLine($"    // [Exception: {ex.GetType().Name} - {message}] .RegisterReadOnly(\"{prop.Name}\", x => x.{prop.Name}){suffix}");
                                }
                            }
                        }
                        finalOutput.AppendLine();
                    }
                }

                // Print to console for NUnit test output
                string resultStr = finalOutput.ToString();
                Console.WriteLine(resultStr);

                // Write to local file in output directory
                string projectRoot = GetProjectRoot();
                string outFilePath = Path.Combine(projectRoot, "output", "assembly_analyzer_base.txt");

                try
                {
                    string dir = Path.GetDirectoryName(outFilePath)!;
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.WriteAllText(outFilePath, resultStr, Encoding.UTF8);
                    Console.WriteLine($"[INFO] Successfully saved generated configuration to: {outFilePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Failed to write config to file: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] AnalyzeBaseClassesAndEmitFluentAPI encountered error: {ex.Message}");
                throw;
            }
        }

        [Test]
        public void ScavengeRemainingTypesAndEmitCleanConfig()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");
            Assert.IsNotNull(_sharedDoc, "Shared test document was not opened in OneTimeSetUp. Check that the model file exists.");

            Document? doc = _sharedDoc;
            Assert.IsTrue(doc!.IsValidObject, "Shared document is no longer valid.");

            try
            {
                // Local helper functions for the Primitive Purge Heuristic
                bool IsPrimitivePurgeType(Type t)
                {
                    Type underlying = Nullable.GetUnderlyingType(t) ?? t;
                    return underlying == typeof(int) || 
                           underlying == typeof(double) || 
                           underlying == typeof(bool) || 
                           underlying == typeof(string);
                }

                bool AreValuesEqual(object? reflectedVal, Parameter param)
                {
                    if (param == null) return false;

                    if (reflectedVal == null)
                    {
                        if (param.StorageType == StorageType.String)
                        {
                            return string.IsNullOrEmpty(param.AsString());
                        }
                        return false;
                    }

                    Type valType = reflectedVal.GetType();
                    Type underlying = Nullable.GetUnderlyingType(valType) ?? valType;

                    if (underlying == typeof(bool))
                    {
                        bool boolVal = (bool)reflectedVal;
                        if (param.StorageType == StorageType.Integer)
                        {
                            return param.AsInteger() == (boolVal ? 1 : 0);
                        }
                        return false;
                    }

                    if (underlying == typeof(int))
                    {
                        int intVal = (int)reflectedVal;
                        if (param.StorageType == StorageType.Integer)
                        {
                            return param.AsInteger() == intVal;
                        }
                        if (param.StorageType == StorageType.Double)
                        {
                            return Math.Abs(param.AsDouble() - intVal) < 1e-9;
                        }
                        return false;
                    }

                    if (underlying == typeof(double))
                    {
                        double doubleVal = (double)reflectedVal;
                        if (param.StorageType == StorageType.Double)
                        {
                            return Math.Abs(param.AsDouble() - doubleVal) < 1e-9;
                        }
                        if (param.StorageType == StorageType.Integer)
                        {
                            return Math.Abs(param.AsInteger() - doubleVal) < 1e-9;
                        }
                        return false;
                    }

                    if (underlying == typeof(string))
                    {
                        string stringVal = (string)reflectedVal;
                        if (param.StorageType == StorageType.String)
                        {
                            string? paramVal = param.AsString();
                            return (string.IsNullOrEmpty(stringVal) && string.IsNullOrEmpty(paramVal)) || (stringVal == paramVal);
                        }
                        return false;
                    }

                    return false;
                }

                bool IsElementId(Type t)
                {
                    return t == typeof(Autodesk.Revit.DB.ElementId);
                }

                var exclusionList = new HashSet<Type>
                {
                    typeof(Autodesk.Revit.DB.Element),
                    typeof(Autodesk.Revit.DB.ElementType),
                    typeof(Autodesk.Revit.DB.Material),
                    typeof(Autodesk.Revit.DB.View),
                    typeof(Autodesk.Revit.DB.ViewPlan),
                    typeof(Autodesk.Revit.DB.WallType),
                    typeof(Autodesk.Revit.DB.Category),
                    typeof(Autodesk.Revit.DB.FillPatternElement),
                    typeof(Autodesk.Revit.DB.ParameterFilterElement),
                    typeof(Autodesk.Revit.DB.DimensionType),
                    typeof(Autodesk.Revit.DB.APIObject)
                };

                // Define Base routing lists
                var activeBaseTypes = new List<Type>
                {
                    typeof(Element),
                    typeof(ElementType),
                    typeof(HostObjAttributes),
                    typeof(View),
                    typeof(Material),
                    typeof(Category),
                    typeof(GraphicsStyle),
                    typeof(ProjectInfo),
                    typeof(SiteLocation)
                };

                var archiveBaseTypes = new List<Type>
                {
                    typeof(Wall),
                    typeof(Floor),
                    typeof(FamilyInstance),
                    typeof(Viewport),
                    typeof(Sketch),
                    typeof(Room),
                    typeof(Dimension)
                };

                // Get all elements in the document
                var allElements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Concat(new FilteredElementCollector(doc).WhereElementIsElementType().ToElements())
                    .ToList();

                var exclusionNames = new HashSet<string>
                {
                    "Autodesk.Revit.DB.Electrical.CircuitNamingSchemeSettings",
                    "Autodesk.Revit.DB.Electrical.ElectricalSetting",
                    "Autodesk.Revit.DB.Mechanical.MEPHiddenLineSettings",
                    "Autodesk.Revit.DB.Electrical.CableTrayType",
                    "Autodesk.Revit.DB.Electrical.ElectricalDemandFactorDefinition",
                    "Autodesk.Revit.DB.Electrical.ElectricalLoadClassification",
                    "Autodesk.Revit.DB.Electrical.CableType",
                    "Autodesk.Revit.DB.Mechanical.SystemZoneElementType",
                    "Autodesk.Revit.DB.Structure.RebarBendingDetailType"
                };

                var grouped = allElements
                    .Where(e => e != null)
                    .GroupBy(e => e.GetType())
                    .Where(g => !exclusionList.Contains(g.Key) && !exclusionNames.Contains(g.Key.FullName))
                    .ToList();

                // Prioritization: Priority 1 (ElementType derivatives), Priority 2 (others)
                var priority1 = grouped.Where(g => typeof(ElementType).IsAssignableFrom(g.Key)).ToList();
                var priority2 = grouped.Where(g => !typeof(ElementType).IsAssignableFrom(g.Key)).ToList();
                var prioritizedGroups = priority1.Concat(priority2).ToList();

                HashSet<Type> processedTypes = new HashSet<Type>(exclusionList);

                // Tri-State StringBuilders
                StringBuilder activeOutput = new StringBuilder();
                StringBuilder archiveOutput = new StringBuilder();
                StringBuilder quarantineOutput = new StringBuilder();

                activeOutput.AppendLine("// ==========================================================================");
                activeOutput.AppendLine("// AUTOMATICALLY GENERATED ACTIVE PROPERTY REGISTRATIONS (PHASE 3 SCAVENGER)");
                activeOutput.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                activeOutput.AppendLine("// ==========================================================================");
                activeOutput.AppendLine();

                archiveOutput.AppendLine("// ==========================================================================");
                archiveOutput.AppendLine("// AUTOMATICALLY GENERATED ARCHIVE PROPERTY REGISTRATIONS (PHASE 3 SCAVENGER)");
                archiveOutput.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                archiveOutput.AppendLine("// ==========================================================================");
                archiveOutput.AppendLine();

                quarantineOutput.AppendLine("// ==========================================================================");
                quarantineOutput.AppendLine("// QUARANTINED EXCEPTIONS AND HEAVY OBJECT GRAPHS (PHASE 3 SCAVENGER)");
                quarantineOutput.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                quarantineOutput.AppendLine("// ==========================================================================");
                quarantineOutput.AppendLine();

                foreach (var group in prioritizedGroups)
                {
                    Type targetType = group.Key;
                    Element dummyInstance = group.First();

                    // Route based on class inheritance
                    bool isArchive = archiveBaseTypes.Any(b => b.IsAssignableFrom(targetType));

                    // Walk up the inheritance chain
                    List<Type> inheritanceChain = new List<Type>();
                    Type? current = targetType;
                    while (current != null && current != typeof(object))
                    {
                        inheritanceChain.Add(current);
                        current = current.BaseType;
                    }
                    inheritanceChain.Reverse(); // Base-first

                    PropertyInfo[] allProps = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                    foreach (Type declaringType in inheritanceChain)
                    {
                        if (processedTypes.Contains(declaringType))
                            continue;

                        var props = allProps
                            .Where(p => p.DeclaringType == declaringType && p.GetIndexParameters().Length == 0)
                            .Where(p => !(declaringType.FullName == "Autodesk.Revit.DB.ViewSheet" && (p.Name == "SheetTitleBlockId" || p.Name == "SheetCollectionId")))
                            .OrderBy(p => p.Name)
                            .ToList();

                        if (props.Count == 0)
                            continue;

                        processedTypes.Add(declaringType);

                        StringBuilder typeBlock = new StringBuilder();
                        StringBuilder quarantineTypeBlock = new StringBuilder();

                        string typeHeader = $"// Declaring Type: {declaringType.FullName}";
                        string forTypeLine = $"registry.ForType<{declaringType.FullName}>()";

                        quarantineTypeBlock.AppendLine($"// Declaring Type Quarantined: {declaringType.FullName}");

                        int cleanCount = 0;
                        int quarantineCount = 0;
                        List<string> linesList = new List<string>();
                        int lastActiveIndex = -1;

                        for (int i = 0; i < props.Count; i++)
                        {
                            var prop = props[i];
                            bool canWrite = prop.CanWrite && prop.GetSetMethod() != null;

                            // If it's an Archive class, the Primitive Purge is disabled, but we still quarantine heavy objects
                            if (isArchive)
                            {
                                if (IsHeavyOrComplexObject(prop.PropertyType))
                                {
                                    quarantineCount++;
                                    quarantineTypeBlock.AppendLine($"    // [OMITTED - Heavy/Complex Type: {prop.PropertyType.FullName}] {prop.Name}");
                                    continue;
                                }

                                try
                                {
                                    // Dry-run property read invocation
                                    var val = prop.GetValue(dummyInstance);

                                    cleanCount++;
                                    if (canWrite)
                                    {
                                        lastActiveIndex = linesList.Count;
                                        linesList.Add($"    .Register(\"{prop.Name}\", x => x.{prop.Name}, (x, v) => x.{prop.Name} = v)");
                                    }
                                    else
                                    {
                                        lastActiveIndex = linesList.Count;
                                        linesList.Add($"    .RegisterReadOnly(\"{prop.Name}\", x => x.{prop.Name})");
                                    }
                                }
                                catch (TargetInvocationException ex)
                                {
                                    var innerEx = ex.InnerException ?? ex;
                                    string message = innerEx.Message.Replace("\r\n", " ").Replace("\n", " ");
                                    quarantineCount++;
                                    quarantineTypeBlock.AppendLine($"    // [Exception: {innerEx.GetType().Name} - {message}] {prop.Name}");
                                }
                                catch (InvalidOperationException ex)
                                {
                                    string message = ex.Message.Replace("\r\n", " ").Replace("\n", " ");
                                    quarantineCount++;
                                    quarantineTypeBlock.AppendLine($"    // [Exception: {ex.GetType().Name} - {message}] {prop.Name}");
                                }
                                catch (Exception ex)
                                {
                                    string message = ex.Message.Replace("\r\n", " ").Replace("\n", " ");
                                    quarantineCount++;
                                    quarantineTypeBlock.AppendLine($"    // [Exception: {ex.GetType().Name} - {message}] {prop.Name}");
                                }
                            }
                            // Active class with Primitive Purge Heuristic enabled
                            else
                            {
                                try
                                {
                                    // First, dry-run property read invocation to check for exceptions
                                    var val = prop.GetValue(dummyInstance);

                                    // Evaluation based on type assignability
                                    if (IsPrimitivePurgeType(prop.PropertyType))
                                    {
                                        bool hasMatchingParam = false;
                                        try
                                        {
                                            if (dummyInstance.Parameters != null)
                                            {
                                                foreach (Parameter param in dummyInstance.Parameters)
                                                {
                                                    if (AreValuesEqual(val, param))
                                                    {
                                                        hasMatchingParam = true;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception)
                                        {
                                            // Fallback
                                        }

                                        if (hasMatchingParam)
                                        {
                                            cleanCount++;
                                            linesList.Add($"    // [DELEGATED TO PARAMETER ENGINE] Property: {prop.Name} ({prop.PropertyType.Name})");
                                        }
                                        else
                                        {
                                            quarantineCount++;
                                            quarantineTypeBlock.AppendLine($"    // [UNMATCHED PRIMITIVE] Property: {prop.Name} ({prop.PropertyType.Name})");
                                        }
                                    }
                                    else if (IsElementId(prop.PropertyType))
                                    {
                                        cleanCount++;
                                        if (canWrite)
                                        {
                                            lastActiveIndex = linesList.Count;
                                            linesList.Add($"    .Register(\"{prop.Name}\", x => x.{prop.Name}, (x, v) => x.{prop.Name} = v)");
                                        }
                                        else
                                        {
                                            lastActiveIndex = linesList.Count;
                                            linesList.Add($"    .RegisterReadOnly(\"{prop.Name}\", x => x.{prop.Name})");
                                        }
                                    }
                                    else
                                    {
                                        // Complex object/class (excluding string)
                                        cleanCount++;
                                        linesList.Add($"    // [REQUIRES EMBED TRANSLATOR] Property: {prop.Name} ({prop.PropertyType.FullName})");
                                    }
                                }
                                catch (TargetInvocationException ex)
                                {
                                    var innerEx = ex.InnerException ?? ex;
                                    string message = innerEx.Message.Replace("\r\n", " ").Replace("\n", " ");
                                    quarantineCount++;
                                    quarantineTypeBlock.AppendLine($"    // [Exception: {innerEx.GetType().Name} - {message}] {prop.Name}");
                                }
                                catch (InvalidOperationException ex)
                                {
                                    string message = ex.Message.Replace("\r\n", " ").Replace("\n", " ");
                                    quarantineCount++;
                                    quarantineTypeBlock.AppendLine($"    // [Exception: {ex.GetType().Name} - {message}] {prop.Name}");
                                }
                                catch (Exception ex)
                                {
                                    string message = ex.Message.Replace("\r\n", " ").Replace("\n", " ");
                                    quarantineCount++;
                                    quarantineTypeBlock.AppendLine($"    // [Exception: {ex.GetType().Name} - {message}] {prop.Name}");
                                }
                            }
                        }

                        // Apply semicolon to the correct line:
                        if (lastActiveIndex != -1)
                        {
                            linesList[lastActiveIndex] += ";";
                        }
                        else
                        {
                            forTypeLine += ";";
                        }

                        // Assemble typeBlock
                        typeBlock.AppendLine(typeHeader);
                        typeBlock.AppendLine(forTypeLine);
                        for (int i = 0; i < linesList.Count; i++)
                        {
                            typeBlock.AppendLine(linesList[i]);
                        }

                        if (cleanCount > 0)
                        {
                            string blockStr = typeBlock.ToString().TrimEnd();
                            if (isArchive)
                            {
                                archiveOutput.AppendLine(blockStr);
                                archiveOutput.AppendLine();
                            }
                            else
                            {
                                activeOutput.AppendLine(blockStr);
                                activeOutput.AppendLine();
                            }
                        }
                        if (quarantineCount > 0)
                        {
                            quarantineOutput.AppendLine(quarantineTypeBlock.ToString());
                        }
                    }
                }

                // Write output files
                string projectRoot = GetProjectRoot();
                string activeFilePath = Path.Combine(projectRoot, "output", "assembly_analyzer_active.txt");
                string archiveFilePath = Path.Combine(projectRoot, "output", "assembly_analyzer_instances_archive.txt");
                string quarantineFilePath = Path.Combine(projectRoot, "output", "assembly_analyzer_quarantine.txt");

                Directory.CreateDirectory(Path.GetDirectoryName(activeFilePath)!);
                File.WriteAllText(activeFilePath, activeOutput.ToString(), Encoding.UTF8);
                File.WriteAllText(archiveFilePath, archiveOutput.ToString(), Encoding.UTF8);
                File.WriteAllText(quarantineFilePath, quarantineOutput.ToString(), Encoding.UTF8);

                Console.WriteLine($"[INFO] Successfully saved active config to: {activeFilePath}");
                Console.WriteLine($"[INFO] Successfully saved archive config to: {archiveFilePath}");
                Console.WriteLine($"[INFO] Successfully saved quarantined config to: {quarantineFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] ScavengeRemainingTypesAndEmitCleanConfig encountered error: {ex.Message}");
                throw;
            }
        }

        [Test]
        public void ScavengeNestedSubObjectsAndEmitConfig()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");
            Assert.IsNotNull(_sharedDoc, "Shared test document was not opened in OneTimeSetUp. Check that the model file exists.");

            Document? doc = _sharedDoc;
            Assert.IsTrue(doc!.IsValidObject, "Shared document is no longer valid.");

            try
            {

                var targetSubObjects = new List<(string Name, object? Instance)>();

                // 1. BalusterPlacement & NonContinuousRailStructure (from RailingType)
                var railingType = new FilteredElementCollector(doc)
                    .OfClass(typeof(RailingType))
                    .Cast<RailingType>()
                    .FirstOrDefault();
                if (railingType != null)
                {
                    targetSubObjects.Add(("Autodesk.Revit.DB.Architecture.BalusterPlacement", railingType.BalusterPlacement));
                    targetSubObjects.Add(("Autodesk.Revit.DB.Architecture.NonContinuousRailStructure", railingType.RailStructure));
                }
                else
                {
                    Console.WriteLine("[WARN] RailingType not found in document.");
                }

                // 2. PrintParameters (from PrintSetup)
                try
                {
                    var printParams = doc.PrintManager.PrintSetup.CurrentPrintSetting.PrintParameters;
                    targetSubObjects.Add(("Autodesk.Revit.DB.PrintParameters", printParams));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Failed to retrieve PrintParameters: {ex.Message}");
                }

                // 3. ScheduleDefinition (from ViewSchedule)
                var viewSchedule = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(x => !x.IsTemplate);
                if (viewSchedule != null)
                {
                    targetSubObjects.Add(("Autodesk.Revit.DB.ScheduleDefinition", viewSchedule.Definition));
                }
                else
                {
                    Console.WriteLine("[WARN] ViewSchedule not found in document.");
                }

                // 4. CurtainGrid (from Wall)
                var curtainWall = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .FirstOrDefault(w => w.CurtainGrid != null);
                if (curtainWall != null)
                {
                    targetSubObjects.Add(("Autodesk.Revit.DB.CurtainGrid", curtainWall.CurtainGrid));
                }
                else
                {
                    Console.WriteLine("[WARN] Wall with CurtainGrid not found in document.");
                }

                // 5. CurtainGridSet (from CurtainSystem or RoofBase)
                object? curtainGridSet = null;
                var curtainSystem = new FilteredElementCollector(doc)
                    .OfClass(typeof(CurtainSystem))
                    .Cast<CurtainSystem>()
                    .FirstOrDefault();
                if (curtainSystem != null)
                {
                    curtainGridSet = GetCurtainGridsHelper(curtainSystem);
                }
                else
                {
                    var roof = new FilteredElementCollector(doc)
                        .OfClass(typeof(RoofBase))
                        .Cast<RoofBase>()
                        .FirstOrDefault(r => GetCurtainGridsHelper(r) != null);
                    if (roof != null)
                    {
                        curtainGridSet = GetCurtainGridsHelper(roof);
                    }
                }
                if (curtainGridSet != null)
                {
                    targetSubObjects.Add(("Autodesk.Revit.DB.CurtainGridSet", curtainGridSet));
                }
                else
                {
                    Console.WriteLine("[WARN] CurtainGridSet (CurtainSystem/Roof) not found in document.");
                }

                // 6. SlabShapeEditor (from Floor or RoofBase)
                object? slabShapeEditor = null;
                var floor = new FilteredElementCollector(doc)
                    .OfClass(typeof(Floor))
                    .Cast<Floor>()
                    .FirstOrDefault(f => GetSlabShapeEditorHelper(f) != null);
                if (floor != null)
                {
                    slabShapeEditor = GetSlabShapeEditorHelper(floor);
                }
                else
                {
                    var roof = new FilteredElementCollector(doc)
                        .OfClass(typeof(RoofBase))
                        .Cast<RoofBase>()
                        .FirstOrDefault(r => GetSlabShapeEditorHelper(r) != null);
                    if (roof != null)
                    {
                        slabShapeEditor = GetSlabShapeEditorHelper(roof);
                    }
                }
                if (slabShapeEditor != null)
                {
                    targetSubObjects.Add(("Autodesk.Revit.DB.SlabShapeEditor", slabShapeEditor));
                }
                else
                {
                    Console.WriteLine("[WARN] SlabShapeEditor not found in document.");
                }

                // 7. DimensionSegmentArray (from Dimension)
                var dimension = new FilteredElementCollector(doc)
                    .OfClass(typeof(Dimension))
                    .Cast<Dimension>()
                    .FirstOrDefault(d => d.Segments != null && d.Segments.Size > 0);
                if (dimension != null)
                {
                    targetSubObjects.Add(("Autodesk.Revit.DB.DimensionSegmentArray", dimension.Segments));
                }
                else
                {
                    var anyDim = new FilteredElementCollector(doc)
                        .OfClass(typeof(Dimension))
                        .Cast<Dimension>()
                        .FirstOrDefault(d => d.Segments != null);
                    if (anyDim != null)
                    {
                        targetSubObjects.Add(("Autodesk.Revit.DB.DimensionSegmentArray", anyDim.Segments));
                    }
                    else
                    {
                        Console.WriteLine("[WARN] Dimension with Segments not found in document.");
                    }
                }

                StringBuilder cleanOutput = new StringBuilder();
                StringBuilder quarantineOutput = new StringBuilder();

                cleanOutput.AppendLine("// ==========================================================================");
                cleanOutput.AppendLine("// AUTOMATICALLY GENERATED CLEAN NESTED SUB-OBJECT PROPERTIES (PHASE 4)");
                cleanOutput.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                cleanOutput.AppendLine("// ==========================================================================");
                cleanOutput.AppendLine();

                quarantineOutput.AppendLine("// ==========================================================================");
                quarantineOutput.AppendLine("// QUARANTINED EXCEPTIONS FOR NESTED SUB-OBJECTS (PHASE 4)");
                quarantineOutput.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                quarantineOutput.AppendLine("// ==========================================================================");
                quarantineOutput.AppendLine();

                int totalScavenged = 0;

                foreach (var subObj in targetSubObjects)
                {
                    if (subObj.Instance == null)
                    {
                        Console.WriteLine($"[INFO] Sub-object {subObj.Name} is null, skipping reflection sweep.");
                        continue;
                    }

                    totalScavenged++;
                    Type type = subObj.Instance.GetType();
                    Console.WriteLine($"[INFO] Sweeping nested sub-object instance of type: {type.FullName}");

                    // Walk up inheritance chain (except standard system objects)
                    List<Type> inheritanceChain = new List<Type>();
                    Type? current = type;
                    while (current != null && current != typeof(object) && current.FullName != "Autodesk.Revit.DB.APIObject")
                    {
                        inheritanceChain.Add(current);
                        current = current.BaseType;
                    }
                    inheritanceChain.Reverse();

                    PropertyInfo[] allProps = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                    foreach (Type declaringType in inheritanceChain)
                    {
                        var props = allProps
                            .Where(p => p.DeclaringType == declaringType && p.GetIndexParameters().Length == 0)
                            .OrderBy(p => p.Name)
                            .ToList();

                        if (props.Count == 0)
                            continue;

                        StringBuilder cleanTypeBlock = new StringBuilder();
                        StringBuilder quarantineTypeBlock = new StringBuilder();

                        cleanTypeBlock.AppendLine($"// Declaring Type: {declaringType.FullName}");
                        cleanTypeBlock.AppendLine($"registry.ForType<{declaringType.Name}>()");

                        quarantineTypeBlock.AppendLine($"// Declaring Type Quarantined: {declaringType.FullName}");

                        int cleanCount = 0;
                        int quarantineCount = 0;

                        for (int i = 0; i < props.Count; i++)
                        {
                            var prop = props[i];
                            bool canWrite = prop.CanWrite && prop.GetSetMethod() != null;

                            if (IsHeavyOrComplexObject(prop.PropertyType))
                            {
                                quarantineCount++;
                                quarantineTypeBlock.AppendLine($"    // [OMITTED - Heavy/Complex Type: {prop.PropertyType.FullName}] {prop.Name}");
                                continue;
                            }

                            try
                            {
                                var val = prop.GetValue(subObj.Instance);
                                cleanCount++;
                                if (canWrite)
                                {
                                    cleanTypeBlock.AppendLine($"    .Register(\"{prop.Name}\", x => x.{prop.Name}, (x, v) => x.{prop.Name} = v)");
                                }
                                else
                                {
                                    cleanTypeBlock.AppendLine($"    .RegisterReadOnly(\"{prop.Name}\", x => x.{prop.Name})");
                                }
                            }
                            catch (TargetInvocationException ex)
                            {
                                var innerEx = ex.InnerException ?? ex;
                                string message = innerEx.Message.Replace("\r\n", " ").Replace("\n", " ");
                                quarantineCount++;
                                quarantineTypeBlock.AppendLine($"    // [Exception: {innerEx.GetType().Name} - {message}] {prop.Name}");
                            }
                            catch (Exception ex)
                            {
                                string message = ex.Message.Replace("\r\n", " ").Replace("\n", " ");
                                quarantineCount++;
                                quarantineTypeBlock.AppendLine($"    // [Exception: {ex.GetType().Name} - {message}] {prop.Name}");
                            }
                        }

                        if (cleanCount > 0)
                        {
                            string blockStr = cleanTypeBlock.ToString().TrimEnd();
                            cleanOutput.AppendLine(blockStr + ";");
                            cleanOutput.AppendLine();
                        }
                        if (quarantineCount > 0)
                        {
                            quarantineOutput.AppendLine(quarantineTypeBlock.ToString());
                        }
                    }
                }

                Assert.IsTrue(totalScavenged > 0, "Should scavenge at least one live sub-object instance.");

                // Write output files
                string projectRoot = GetProjectRoot();
                string cleanFilePath = Path.Combine(projectRoot, "output", "assembly_analyzer_nested_clean.txt");
                string quarantineFilePath = Path.Combine(projectRoot, "output", "assembly_analyzer_nested_quarantine.txt");

                Directory.CreateDirectory(Path.GetDirectoryName(cleanFilePath)!);
                File.WriteAllText(cleanFilePath, cleanOutput.ToString(), Encoding.UTF8);
                File.WriteAllText(quarantineFilePath, quarantineOutput.ToString(), Encoding.UTF8);

                Console.WriteLine($"[INFO] Successfully saved clean config to: {cleanFilePath}");
                Console.WriteLine($"[INFO] Successfully saved quarantined config to: {quarantineFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] ScavengeNestedSubObjectsAndEmitConfig encountered error: {ex.Message}");
                throw;
            }
        }

        private static object? GetSlabShapeEditorHelper(object element)
        {
            if (element == null) return null;
            var type = element.GetType();
            var prop = type.GetProperty("SlabShapeEditor", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                try { return prop.GetValue(element); } catch {}
            }
            var method = type.GetMethod("GetSlabShapeEditor", BindingFlags.Public | BindingFlags.Instance);
            if (method != null)
            {
                try { return method.Invoke(element, null); } catch {}
            }
            return null;
        }

        private static object? GetCurtainGridsHelper(object element)
        {
            if (element == null) return null;
            var type = element.GetType();
            var prop = type.GetProperty("CurtainGrids", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                try { return prop.GetValue(element); } catch {}
            }
            return null;
        }

        private bool IsHeavyOrComplexObject(Type type)
        {
            if (type == null) return false;
            if (typeof(Autodesk.Revit.DB.Element).IsAssignableFrom(type)) return true;
            if (typeof(Autodesk.Revit.DB.Category).IsAssignableFrom(type)) return true;

            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
            {
                if (type.IsGenericType)
                {
                    foreach (var arg in type.GetGenericArguments())
                    {
                        if (typeof(Autodesk.Revit.DB.Element).IsAssignableFrom(arg) ||
                            typeof(Autodesk.Revit.DB.Category).IsAssignableFrom(arg))
                        {
                            return true;
                        }
                    }
                }
                if (type.IsArray)
                {
                    var elementType = type.GetElementType();
                    if (elementType != null &&
                        (typeof(Autodesk.Revit.DB.Element).IsAssignableFrom(elementType) ||
                         typeof(Autodesk.Revit.DB.Category).IsAssignableFrom(elementType)))
                    {
                        return true;
                    }
                }
            }

            string fullName = type.FullName ?? "";
            if (fullName.StartsWith("Autodesk.Revit.DB."))
            {
                if (type == typeof(Autodesk.Revit.DB.ElementId) ||
                    type == typeof(Autodesk.Revit.DB.XYZ) ||
                    type == typeof(Autodesk.Revit.DB.UV) ||
                    type == typeof(Autodesk.Revit.DB.Color))
                {
                    return false;
                }
                if (type.IsClass || (type.IsValueType && !type.IsPrimitive && !type.IsEnum))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetProjectRoot()
        {
            string envPath = Environment.GetEnvironmentVariable("SYNTHETIC_PROJECT_ROOT");
            if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            {
                return envPath;
            }

            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "tests")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            return dir ?? TestContext.CurrentContext.TestDirectory;
        }

        private void ResolveWarnings(object? sender, FailuresProcessingEventArgs e)
        {
            FailuresAccessor fa = e.GetFailuresAccessor();
            IList<FailureMessageAccessor> failList = fa.GetFailureMessages();

            if (failList.Count == 0)
            {
                e.SetProcessingResult(FailureProcessingResult.Continue);
                return;
            }

            foreach (FailureMessageAccessor failure in failList)
            {
                fa.DeleteWarning(failure);
            }
            e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);
        }
    }
}
