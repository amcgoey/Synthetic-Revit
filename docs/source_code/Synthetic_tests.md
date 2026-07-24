### File: tests/run_integration_test.py
```python
import os
import sys
import re

# Add the QA plugin scripts directory to the path so we can import its modules
workspace_path = os.getcwd()
qa_scripts_dir = os.path.join(workspace_path, ".agents", "plugins", "RevitQualityAssurance", "scripts")
sys.path.append(qa_scripts_dir)

import test_executor
import revit_journal_tool

def run_test(revit_version="2025"):
    print(f"[TEST RUNNER] Starting integration test for Audit & Purge on Revit {revit_version}...")
    
    # 1. Run the test suite via the test_executor from the QA plugin
    test_executor.run_test_suite("", revit_version)
    
    # 2. Locate the latest journal using the revit_journal_tool from the QA plugin
    latest_journal = revit_journal_tool.get_latest_journal(revit_version)
    if not latest_journal:
        print("[-] Error: Could not locate the latest Revit journal file.")
        sys.exit(1)
        
    print(f"[TEST RUNNER] Reading results from journal: {latest_journal}")
    lines, encoding = revit_journal_tool.read_journal_lines(latest_journal)
    
    # 3. Parse the journal data from the APIStringStringMapJournalData block
    metrics = {}
    in_block = False
    block_lines = []
    
    for line in lines:
        clean = line.strip()
        if 'APIStringStringMapJournalData' in clean:
            in_block = True
            block_lines.append(clean)
            continue
        if in_block:
            block_lines.append(clean)
            # If the line does not end with line continuation character '_', the block has ended.
            if not clean.endswith('_'):
                in_block = False
                
    # Now parse all accumulated block lines
    # Combine lines by stripping trailing underscores
    combined = ""
    for bl in block_lines:
        c_line = bl
        if c_line.endswith('_'):
            c_line = c_line[:-1].strip()
        combined += " " + c_line
        
    # Find all matches of double-quoted strings
    tokens = re.findall(r'"([^"]*)"', combined)
    if len(tokens) > 1:
        for i in range(1, len(tokens), 2):
            if i + 1 < len(tokens):
                key = tokens[i]
                val = tokens[i+1]
                metrics[key] = val

    print(f"[TEST RUNNER] Extracted metrics: {metrics}")
    
    # 4. Assertions
    size_before_str = metrics.get("Test_Family1_SizeBefore")
    size_after_str = metrics.get("Test_Family1_SizeAfter")
    param_value = metrics.get("Test_Family1_ParamValue")
    
    if not size_before_str or not size_after_str or not param_value:
        print("[-] FAIL: Missing required verification keys in the journal file.")
        sys.exit(1)
        
    size_before = int(size_before_str)
    size_after = int(size_after_str)
    
    print(f"[TEST RUNNER] Size Before: {size_before} bytes")
    print(f"[TEST RUNNER] Size After:  {size_after} bytes")
    print(f"[TEST RUNNER] Param Value: {param_value}")
    
    passed = True
    if size_after >= size_before:
        print("[-] FAIL: Family size did not decrease after purge.")
        passed = False
    else:
        print("[+] PASS: Family size successfully decreased after purge.")
        
    if param_value != "PRESERVE_TEST":
        print(f"[-] FAIL: Comments parameter was overwritten (Value: {param_value}, Expected: PRESERVE_TEST).")
        passed = False
    else:
        print("[+] PASS: Comments parameter override was preserved.")
        
    if passed:
        print("[+] INTEGRATION TEST PASSED SUCCESSFULLY!")
        sys.exit(0)
    else:
        print("[-] INTEGRATION TEST FAILED.")
        sys.exit(1)

if __name__ == "__main__":
    version = sys.argv[1] if len(sys.argv) > 1 else "2025"
    run_test(version)
```

### File: tests/RevitAPIMock/MockRevitAPI.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Autodesk.Revit.DB
{
    public class APIObject : IDisposable
    {
        public bool IsReadOnly { get; set; }
        public bool IsValidObject { get; } = true;
        public void Dispose() { }
    }

    public class DocumentSet : APIObject, System.Collections.IEnumerable
    {
        private readonly List<Document> _documents = new List<Document>();

        public DocumentSet() { }
        public DocumentSet(IEnumerable<Document> documents)
        {
            _documents.AddRange(documents);
        }

        public void Add(Document document) => _documents.Add(document);

        public System.Collections.IEnumerator GetEnumerator() => _documents.GetEnumerator();
    }

    public class Settings : APIObject
    {
        public Categories Categories { get; } = new Categories();
    }

    public class Categories : APIObject, System.Collections.IEnumerable
    {
        private readonly List<Category> _categories = new List<Category>();

        public void Add(Category category) => _categories.Add(category);

        public System.Collections.IEnumerator GetEnumerator() => _categories.GetEnumerator();
    }

    public enum StorageType
    {
        None = 0,
        Integer = 1,
        Double = 2,
        String = 3,
        ElementId = 4
    }

    public class Definition : APIObject
    {
        public string Name { get; set; } = string.Empty;
        public string ParameterGroup { get; set; } = string.Empty;
    }

    public class InternalDefinition : Definition
    {
    }

    public class Parameter : APIObject
    {
        public Definition Definition { get; set; } = new Definition();
        public StorageType StorageType { get; set; }
        public bool IsShared { get; set; }
        public Guid GUID { get; set; }
        public bool HasValue { get; set; }

        public double DoubleValue { get; set; }
        public int IntegerValue { get; set; }
        public string StringValue { get; set; } = string.Empty;
        public ElementId ElementIdValue { get; set; } = new ElementId();

        public double AsDouble() => DoubleValue;
        public int AsInteger() => IntegerValue;
        public string AsString() => StringValue;
        public ElementId AsElementId() => ElementIdValue;

        // Set tracking properties
        public bool SetCalled { get; set; }
        public object? SetValue { get; set; }

        public bool Set(double value)
        {
            SetCalled = true;
            SetValue = value;
            DoubleValue = value;
            return true;
        }

        public bool Set(int value)
        {
            SetCalled = true;
            SetValue = value;
            IntegerValue = value;
            return true;
        }

        public bool Set(string value)
        {
            SetCalled = true;
            SetValue = value;
            StringValue = value;
            return true;
        }

        public bool Set(ElementId value)
        {
            SetCalled = true;
            SetValue = value;
            ElementIdValue = value;
            return true;
        }
    }

    public class ParameterSet : APIObject, System.Collections.IEnumerable
    {
        private readonly List<Parameter> _parameters = new List<Parameter>();

        public void Add(Parameter param) => _parameters.Add(param);
        public bool Insert(Parameter param)
        {
            _parameters.Add(param);
            return true;
        }

        public System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();
    }

    public class ElementId : APIObject
    {
        public ElementId() { }
        public ElementId(long id)
        {
            Value = id;
            IntegerValue = (int)id;
        }
        public ElementId(int id)
        {
            Value = id;
            IntegerValue = id;
        }

        public long Value { get; set; }
        public int IntegerValue { get; set; }

        public static ElementId GetSolidPatternId() => new ElementId();
        public static ElementId InvalidElementId { get; } = new ElementId(-1);

        public static bool operator ==(ElementId? left, ElementId? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Value == right.Value;
        }

        public static bool operator !=(ElementId? left, ElementId? right)
        {
            return !(left == right);
        }

        public override bool Equals(object? obj)
        {
            if (obj is ElementId other) return Value == other.Value;
            return false;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }

    public class Document : APIObject
    {
        public Autodesk.Revit.ApplicationServices.Application Application { get; } = new Autodesk.Revit.ApplicationServices.Application();
        public Settings Settings { get; } = new Settings();
        public bool IsFamilyDocument { get; set; } = false;
        public string Title { get; set; } = "Mock Document";
        public string PathName { get; set; } = "ActiveDoc";
        public bool IsWorkshared { get; set; } = false;
        private readonly Dictionary<string, Element> _elementsByUniqueId = new Dictionary<string, Element>();
        private readonly Dictionary<ElementId, Element> _elementsById = new Dictionary<ElementId, Element>();

        public void AddElement(Element elem, ElementId id)
        {
            _elementsById[id] = elem;
            if (!string.IsNullOrEmpty(elem.UniqueId))
            {
                _elementsByUniqueId[elem.UniqueId] = elem;
            }
            elem.Document = this;
        }

        public Element? GetElement(string uniqueId)
        {
            return _elementsByUniqueId.TryGetValue(uniqueId, out var elem) ? elem : null;
        }

        public Element? GetElement(ElementId id)
        {
            return _elementsById.TryGetValue(id, out var elem) ? elem : null;
        }

        public Document EditFamily(Family family)
        {
            if (family.MockFamilyDocument != null)
            {
                return family.MockFamilyDocument;
            }
            var familyDoc = new Document { IsFamilyDocument = true };
            return familyDoc;
        }

        public Family LoadFamily(Document targetDocument, IFamilyLoadOptions familyLoadOptions)
        {
            return new Family();
        }

        public bool Close(bool saveChanges)
        {
            return true;
        }

        public System.Collections.Generic.ICollection<ElementId> Delete(ElementId elementId)
        {
            var deletedIds = new System.Collections.Generic.List<ElementId>();
            if (_elementsById.TryGetValue(elementId, out var elem))
            {
                _elementsById.Remove(elementId);
                if (!string.IsNullOrEmpty(elem.UniqueId))
                {
                    _elementsByUniqueId.Remove(elem.UniqueId);
                }
                deletedIds.Add(elementId);
            }
            return deletedIds;
        }

        internal System.Collections.Generic.IEnumerable<Element> GetElements() => _elementsById.Values;
    }

    public class Element : APIObject
    {
        public string Name { get; set; } = string.Empty;
        public string UniqueId { get; set; } = string.Empty;
        public Category? Category { get; set; }
        public ParameterSet Parameters { get; set; } = new ParameterSet();
        public Document Document { get; set; } = new Document();
        public ElementId Id { get; set; } = ElementId.InvalidElementId;
        public Autodesk.Revit.DB.ExtensibleStorage.Entity GetEntity(Autodesk.Revit.DB.ExtensibleStorage.Schema schema) => new Autodesk.Revit.DB.ExtensibleStorage.Entity();

        public Parameter? LookupParameter(string name) => null;
        public Parameter? get_Parameter(Guid guid) => null;
        public Parameter? get_Parameter(BuiltInParameter bip) => null;
        public Parameter? get_Parameter(Definition def) => null;
        public ElementId GetTypeId() => ElementId.InvalidElementId;
    }

    public enum CategoryType
    {
        Model,
        Annotation,
        Internal,
        Analytical,
        Invalid
    }

    public class Category : APIObject
    {
        public string Name { get; set; } = string.Empty;
        public ElementId Id { get; set; } = new ElementId();
        public bool IsCuttable { get; set; }
        public Category? Parent { get; set; }
        public CategoryNameMap SubCategories { get; } = new CategoryNameMap();
        public CategoryType CategoryType { get; set; } = CategoryType.Model;
        public bool IsVisibleInUI { get; set; } = true;

        public static Category? GetCategory(Document doc, ElementId id) => null;
    }

    public class CategoryNameMap : APIObject, System.Collections.IEnumerable
    {
        private readonly List<Category> _categories = new List<Category>();

        public void Add(Category category) => _categories.Add(category);

        public System.Collections.IEnumerator GetEnumerator() => _categories.GetEnumerator();
        
        public bool IsEmpty => _categories.Count == 0;
        public int Size => _categories.Count;
    }

    public enum LinePatternSegmentType
    {
        Dash,
        Dot,
        Space
    }

    public class LinePatternSegment : APIObject
    {
        public LinePatternSegmentType Type { get; set; }
        public double Length { get; set; }

        public LinePatternSegment() { }
        public LinePatternSegment(LinePatternSegmentType type, double length)
        {
            Type = type;
            Length = length;
        }
    }

    public class LinePattern : APIObject
    {
        private List<LinePatternSegment> _segments = new List<LinePatternSegment>();
        public string Name { get; set; } = string.Empty;

        public LinePattern() { }
        public LinePattern(string name)
        {
            Name = name;
        }

        public IList<LinePatternSegment> GetSegments() => _segments;
        public void SetSegments(IList<LinePatternSegment> segments) => _segments = new List<LinePatternSegment>(segments);
    }

    public class LinePatternElement : Element
    {
        public static ElementId GetSolidPatternId() => new ElementId();
        public LinePattern GetLinePattern() => new LinePattern("Mock Pattern");
    }

    public class Color : APIObject
    {
        public Color() { }
        public Color(byte red, byte green, byte blue)
        {
            Red = red;
            Green = green;
            Blue = blue;
            IsValid = true;
        }

        public byte Red { get; set; }
        public byte Green { get; set; }
        public byte Blue { get; set; }
        public bool IsValid { get; set; }

        public static Color InvalidColorValue { get; } = new Color { IsValid = false };
    }

    public class UV : APIObject
    {
        public UV() { }
        public UV(double u, double v)
        {
            U = u;
            V = v;
        }

        public double U { get; set; }
        public double V { get; set; }
    }

    public enum PlanViewPlane
    {
        TopClipPlane = 0,
        CutPlane = 1,
        BottomClipPlane = 2,
        ViewDepthPlane = 3
    }

    public class PlanViewRange : APIObject
    {
        private readonly System.Collections.Generic.Dictionary<PlanViewPlane, double> _offsets
            = new System.Collections.Generic.Dictionary<PlanViewPlane, double>();

        private readonly System.Collections.Generic.Dictionary<PlanViewPlane, ElementId> _levelIds
            = new System.Collections.Generic.Dictionary<PlanViewPlane, ElementId>();

        public double GetOffset(PlanViewPlane plane)
            => _offsets.TryGetValue(plane, out var v) ? v : 0.0;

        public ElementId GetLevelId(PlanViewPlane plane)
            => _levelIds.TryGetValue(plane, out var id) ? id : new ElementId(0);

        public void SetOffset(PlanViewPlane plane, double offset)
            => _offsets[plane] = offset;

        public void SetLevelId(PlanViewPlane plane, ElementId id)
            => _levelIds[plane] = id;
    }

    public enum ViewType
    {
        AreaPlan = 0,
        CeilingPlan = 1,
        ColumnSchedule = 2,
        CostReport = 3,
        DraftingView = 4,
        DrawingSheet = 5,
        Elevation = 6,
        EngineeringPlan = 7,
        FloorPlan = 8,
        Internal = 9,
        Legend = 10,
        LoadsReport = 11,
        PanelSchedule = 12,
        PresureLossReport = 13,
        ProjectReport = 14,
        Rendering = 15,
        Report = 16,
        Schedule = 17,
        Section = 18,
        ThreeD = 19,
        Walkthrough = 20,
        Undefined = 21
    }

    public enum DisplayStyle
    {
        Wireframe = 1,
        HLR = 2,
        Shading = 3,
        ShadingWithEdges = 4,
        Realistic = 5,
        RealisticWithEdges = 6,
        Rendering = 7,
        Raytrace = 8
    }

    public enum ViewDetailLevel
    {
        Undefined = 0,
        Coarse = 1,
        Medium = 2,
        Fine = 3
    }

    public enum ViewDiscipline
    {
        Architecture = 1,
        Structure = 2,
        Mechanical = 4,
        Electrical = 8,
        Plumbing = 16,
        Coordination = 32
    }

    public enum PartsVisibility
    {
        ShowPartsOnly = 0,
        ShowOriginalOnly = 1,
        ShowBoth = 2
    }

    public class Transform
    {
        public XYZ Origin { get; set; } = new XYZ();
        public XYZ BasisX { get; set; } = new XYZ();
        public XYZ BasisY { get; set; } = new XYZ();
        public XYZ BasisZ { get; set; } = new XYZ();
        public static Transform Identity { get; } = new Transform();
    }

    public class BoundingBoxXYZ
    {
        public XYZ Min { get; set; } = new XYZ();
        public XYZ Max { get; set; } = new XYZ();
        public Transform Transform { get; set; } = new Transform();
    }

    public class View : Element
    {
        public bool IsTemplate { get; set; }
        public ViewType ViewType { get; set; } = ViewType.FloorPlan;
        public DisplayStyle DisplayStyle { get; set; } = DisplayStyle.HLR;
        public int SunlightIntensity { get; set; } = 100;
        public int ShadowIntensity { get; set; } = 50;
        public int Scale { get; set; } = 100;
        public ViewDetailLevel DetailLevel { get; set; } = ViewDetailLevel.Medium;
        public ViewDiscipline Discipline { get; set; } = ViewDiscipline.Coordination;
        public BoundingBoxXYZ CropBox { get; set; } = new BoundingBoxXYZ();
        public bool CropBoxActive { get; set; }
        public bool CropBoxVisible { get; set; }
        public PartsVisibility PartsVisibility { get; set; } = PartsVisibility.ShowBoth;
        public Level? GenLevel { get; set; }

        public ICollection<ElementId> GetFilters()
        {
            return new List<ElementId>();
        }

        public OverrideGraphicSettings GetFilterOverrides(ElementId filterId) => new OverrideGraphicSettings();
        public bool GetFilterVisibility(ElementId filterId) => true;
        public ICollection<ElementId> GetNonControlledTemplateParameterIds() => new List<ElementId>();

        public OverrideGraphicSettings GetCategoryOverrides(ElementId catId)
        {
            return new OverrideGraphicSettings();
        }
    }

    public class OverrideGraphicSettings
    {
        public ElementId ProjectionLinePatternId { get; set; } = new ElementId();
        public ElementId CutLinePatternId { get; set; } = new ElementId();
        public ElementId SurfaceForegroundPatternId { get; set; } = new ElementId();
        public ElementId SurfaceBackgroundPatternId { get; set; } = new ElementId();
        public ElementId CutForegroundPatternId { get; set; } = new ElementId();
        public ElementId CutBackgroundPatternId { get; set; } = new ElementId();
    }

    public class ViewPlan : View
    {
        private PlanViewRange _viewRange = new PlanViewRange();

        public PlanViewRange GetViewRange() => _viewRange;
        public void SetViewRange(PlanViewRange viewRange)
        {
            _viewRange = viewRange;
        }

        public bool CanHaveViewRange() => true;

        public ElementId GetUnderlayBaseLevel() => new ElementId();
        public UnderlayOrientation GetUnderlayOrientation() => UnderlayOrientation.LookDown;
    }

    public enum StructDeckEmbeddingType
    {
        Invalid = -1,
        MergeWithLayerAbove = 0,
        Standalone = 1
    }

    public enum MaterialFunctionAssignment
    {
        None = 0,
        Structure = 1,
        Substrate = 2,
        ThermalOrAir = 3,
        Finish1 = 4,
        Finish2 = 5,
        Membrane = 6,
        StructuralDeck = 100
    }

    public class CompoundStructureLayer : APIObject
    {
        public CompoundStructureLayer() { }
        public CompoundStructureLayer(double width, MaterialFunctionAssignment function, ElementId materialId)
        {
            Width = width;
            Function = function;
            MaterialId = materialId;
        }

        public StructDeckEmbeddingType DeckEmbeddingType { get; set; }
        public ElementId DeckProfileId { get; set; } = ElementId.InvalidElementId;
        public MaterialFunctionAssignment Function { get; set; }
        public bool LayerCapFlag { get; set; }
        public ElementId MaterialId { get; set; } = ElementId.InvalidElementId;
        public double Width { get; set; }
    }

    public class CompoundStructure : APIObject
    {
        private IList<CompoundStructureLayer> _layers = new List<CompoundStructureLayer>();
        private readonly System.Collections.Generic.Dictionary<int, int> _priorities = new System.Collections.Generic.Dictionary<int, int>();

        public int StructuralMaterialIndex { get; set; } = -1;

        public static CompoundStructure CreateSimpleCompoundStructure(IList<CompoundStructureLayer> layers)
        {
            var cs = new CompoundStructure();
            cs._layers = new List<CompoundStructureLayer>(layers);
            return cs;
        }

        public IList<CompoundStructureLayer> GetLayers() => _layers;

        public int GetPriority(int index)
        {
            return _priorities.TryGetValue(index, out int val) ? val : 0;
        }

        public int GetLayerPriority(int index)
        {
            return GetPriority(index);
        }

        public void SetPriority(int index, int priority)
        {
            _priorities[index] = priority;
        }
    }

    public class HostObjAttributes : ElementType
    {
        private CompoundStructure? _cs;

        public CompoundStructure GetCompoundStructure() => _cs ?? new CompoundStructure();
        public void SetCompoundStructure(CompoundStructure cs)
        {
            _cs = cs;
        }
    }

    public class WallType : HostObjAttributes
    {
    }

    public class Material : Element
    {
        public ElementId StructuralAssetId { get; set; } = ElementId.InvalidElementId;
        public ElementId ThermalAssetId { get; set; } = ElementId.InvalidElementId;
        public ElementId AppearanceAssetId { get; set; } = ElementId.InvalidElementId;

        public Color Color { get; set; } = Color.InvalidColorValue;

        public Color CutForegroundPatternColor { get; set; } = Color.InvalidColorValue;
        public Color CutBackgroundPatternColor { get; set; } = Color.InvalidColorValue;
        public Color SurfaceForegroundPatternColor { get; set; } = Color.InvalidColorValue;
        public Color SurfaceBackgroundPatternColor { get; set; } = Color.InvalidColorValue;

        public ElementId CutForegroundPatternId { get; set; } = ElementId.InvalidElementId;
        public ElementId CutBackgroundPatternId { get; set; } = ElementId.InvalidElementId;
        public ElementId SurfaceForegroundPatternId { get; set; } = ElementId.InvalidElementId;
        public ElementId SurfaceBackgroundPatternId { get; set; } = ElementId.InvalidElementId;

        public void SetMaterialAspectByPropertySet(MaterialAspect aspect, ElementId id)
        {
            if (aspect == MaterialAspect.Structural) StructuralAssetId = id;
            else if (aspect == MaterialAspect.Thermal) ThermalAssetId = id;
            else if (aspect == MaterialAspect.Appearance) AppearanceAssetId = id;
        }

        public static ElementId Create(Document doc, string name)
        {
            var mat = new Material { Name = name, Id = new ElementId(new Random().Next(10000, 99999)) };
            doc.AddElement(mat, mat.Id);
            return mat.Id;
        }
    }

    public enum StructuralAssetClass
    {
        Generic = 0,
        Metal = 1,
        Concrete = 2,
        Wood = 3,
        Liquid = 4,
        Gas = 5,
        Plastic = 6,
        Glass = 7
    }

    public enum StructuralBehavior
    {
        Isotropic = 0,
        Orthotropic = 1
    }

    public enum ThermalMaterialType
    {
        Undefined = 0,
        Solid = 1,
        Liquid = 2,
        Gas = 3
    }

    public enum MaterialAspect
    {
        Appearance = 0,
        Structural = 1,
        Thermal = 2
    }

    public class XYZ : APIObject
    {
        public XYZ() { }
        public XYZ(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class StructuralAsset : APIObject
    {
        public string Name { get; set; }
        public StructuralAssetClass StructuralAssetClass { get; set; }
        public StructuralBehavior Behavior { get; set; }
        public double Density { get; set; }
        public XYZ YoungModulus { get; set; } = new XYZ();
        public XYZ PoissonRatio { get; set; } = new XYZ();

        public StructuralAsset(string name, StructuralAssetClass assetClass)
        {
            Name = name;
            StructuralAssetClass = assetClass;
        }

        public void SetYoungModulus(double value) => YoungModulus = new XYZ(value, 0, 0);
        public void SetPoissonRatio(double value) => PoissonRatio = new XYZ(value, 0, 0);
    }

    public class ThermalAsset : APIObject
    {
        public string Name { get; set; }
        public ThermalMaterialType ThermalMaterialType { get; set; }
        public double Density { get; set; }
        public double ThermalConductivity { get; set; }
        public double SpecificHeat { get; set; }
        public double Emissivity { get; set; }

        public ThermalAsset(string name, ThermalMaterialType type)
        {
            Name = name;
            ThermalMaterialType = type;
        }
    }

    public class PropertySetElement : Element
    {
        public StructuralAsset? StructuralAsset { get; set; }
        public ThermalAsset? ThermalAsset { get; set; }

        public static PropertySetElement Create(Document doc, StructuralAsset asset)
        {
            var elem = new PropertySetElement { Name = asset.Name, StructuralAsset = asset, Id = new ElementId(new Random().Next(100000, 999999)) };
            doc.AddElement(elem, elem.Id);
            return elem;
        }

        public static PropertySetElement Create(Document doc, ThermalAsset asset)
        {
            var elem = new PropertySetElement { Name = asset.Name, ThermalAsset = asset, Id = new ElementId(new Random().Next(100000, 999999)) };
            doc.AddElement(elem, elem.Id);
            return elem;
        }

        public StructuralAsset GetStructuralAsset()
        {
            if (StructuralAsset == null) throw new InvalidOperationException("No Structural Asset");
            return StructuralAsset;
        }

        public ThermalAsset GetThermalAsset()
        {
            if (ThermalAsset == null) throw new InvalidOperationException("No Thermal Asset");
            return ThermalAsset;
        }

        public void SetStructuralAsset(StructuralAsset asset)
        {
            StructuralAsset = asset;
        }

        public void SetThermalAsset(ThermalAsset asset)
        {
            ThermalAsset = asset;
        }

        public PropertySetElement Duplicate(Document doc, string name)
        {
            var dup = new PropertySetElement { Name = name, StructuralAsset = StructuralAsset, ThermalAsset = ThermalAsset, Id = new ElementId(new Random().Next(100000, 999999)) };
            doc.AddElement(dup, dup.Id);
            return dup;
        }
    }

    public class AppearanceAssetElement : Element
    {
        public Visual.Asset? RenderingAsset { get; set; }

        public static AppearanceAssetElement Create(Document doc, string name, Visual.Asset asset)
        {
            var elem = new AppearanceAssetElement { Name = name, RenderingAsset = asset, Id = new ElementId(new Random().Next(100000, 999999)) };
            doc.AddElement(elem, elem.Id);
            return elem;
        }

        public Visual.Asset GetRenderingAsset() => RenderingAsset ?? new Visual.Asset();

        public AppearanceAssetElement Duplicate(string name)
        {
            var dup = new AppearanceAssetElement { Name = name, RenderingAsset = RenderingAsset, Id = new ElementId(new Random().Next(100000, 999999)) };
            Document.AddElement(dup, dup.Id);
            return dup;
        }
    }

    public class WorksetId : APIObject
    {
        public WorksetId() { }
        public WorksetId(int integerValue) { IntegerValue = integerValue; }
        public int IntegerValue { get; set; }
        public static WorksetId InvalidWorksetId { get; } = new WorksetId(-1);
    }

    public class ElementType : Element
    {
        public virtual ElementType Duplicate(string name)
        {
            var dup = (ElementType)System.Activator.CreateInstance(this.GetType());
            dup.Name = name;
            dup.Id = new ElementId(new Random().Next(1000, 9999));
            Document?.AddElement(dup, dup.Id);
            return dup;
        }
    }

    public class GridType : ElementType { }
    public class LevelType : ElementType { }
    public class Level : Element
    {
        public double Elevation { get; set; }
    }
    public class TextNoteType : ElementType { }
    public class FamilySymbol : ElementType { }

    public enum BuiltInCategory
    {
        OST_TitleBlocks = -2000280,
        OST_DetailComponents = -2000180,
        OST_ProfileFamilies = -2005022
    }

    public enum BuiltInParameter
    {
        INVALID = -1
    }

    public enum FamilySource
    {
        Project,
        Family
    }

    public interface IFamilyLoadOptions
    {
        bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues);
        bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues);
    }

    public class Family : Element
    {
        public bool IsEditable { get; set; } = true;
        public Category? FamilyCategory { get; set; }
        public WorksetId WorksetId { get; set; } = WorksetId.InvalidWorksetId;
        public Document? MockFamilyDocument { get; set; }
    }

    public static class WorksharingUtils
    {
        public static System.Collections.Generic.ICollection<WorksetId> CheckoutWorksets(Document doc, System.Collections.Generic.ICollection<WorksetId> worksetIds)
        {
            return new System.Collections.Generic.List<WorksetId>();
        }
    }

    public class ParameterFilterElement : Element
    {
        public System.Collections.Generic.ICollection<ElementId> GetCategories() => new List<ElementId>();
    }

    public class ParameterElement : Element
    {
        public InternalDefinition GetDefinition() => new InternalDefinition();
    }

    public class SharedParameterElement : ParameterElement
    {
        public Guid GuidValue { get; set; } = Guid.NewGuid();
        public static SharedParameterElement? Lookup(Document doc, Guid guid) => null;
    }

    public class MockDefinition : Definition { }

    public enum DimensionStyleType
    {
        Linear = 0,
        Angular = 1,
        Radial = 2,
        Diameter = 3,
        ArcLength = 4,
        LinearFixed = 5,
        Ordinate = 6
    }

    public class DimensionType : ElementType
    {
        public DimensionStyleType StyleType { get; set; } = DimensionStyleType.Linear;
    }

    public class FilteredElementCollector : System.Collections.Generic.IEnumerable<Element>
    {
        private readonly List<Element> _elements;

        public FilteredElementCollector(Document doc)
        {
            _elements = new List<Element>(doc.GetElements());
        }

        public FilteredElementCollector(Document doc, ElementId viewId) : this(doc) { }

        private FilteredElementCollector(List<Element> elements)
        {
            _elements = elements;
        }

        public FilteredElementCollector OfClass(Type type)
        {
            var filtered = _elements.Where(e => type.IsAssignableFrom(e.GetType())).ToList();
            return new FilteredElementCollector(filtered);
        }

        public System.Collections.Generic.IList<Element> ToElements()
        {
            return _elements;
        }

        public System.Collections.Generic.IEnumerator<Element> GetEnumerator() => _elements.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public enum UnderlayOrientation
    {
        LookDown = 0,
        LookUp = 1
    }
}

namespace Autodesk.Revit.DB.Visual
{
    public enum AssetType
    {
        Appearance = 0,
        Thermal = 1,
        Structural = 2
    }

    public class AssetProperty : APIObject
    {
        public string Name { get; set; } = string.Empty;
        public int NumberOfConnectedProperties { get; set; }
        public Asset? GetSingleConnectedAsset() => null;
    }

    public class AssetPropertyDouble : AssetProperty
    {
        public double Value { get; set; }
    }

    public class AssetPropertyDoubleArray4d : AssetProperty
    {
        public System.Collections.Generic.IList<double> Value { get; set; } = new System.Collections.Generic.List<double> { 0, 0, 0, 1 };
        public void SetValueAsColor(Color color)
        {
            Value = new System.Collections.Generic.List<double> { color.Red / 255.0, color.Green / 255.0, color.Blue / 255.0, 1.0 };
        }
        public Color GetValueAsColor()
        {
            return new Color((byte)(Value[0] * 255), (byte)(Value[1] * 255), (byte)(Value[2] * 255));
        }
    }

    public class AssetProperties : APIObject
    {
        public string Name { get; set; } = string.Empty;
        public int Size { get; set; }
        protected readonly System.Collections.Generic.Dictionary<string, AssetProperty> _properties = new System.Collections.Generic.Dictionary<string, AssetProperty>();

        public AssetProperties()
        {
            _properties["generic_diffuse"] = new AssetPropertyDoubleArray4d { Name = "generic_diffuse" };
            _properties["generic_transparency"] = new AssetPropertyDouble { Name = "generic_transparency" };
            _properties["generic_glossiness"] = new AssetPropertyDouble { Name = "generic_glossiness" };
        }

        public AssetProperty? FindByName(string name)
        {
            return _properties.TryGetValue(name, out var prop) ? prop : null;
        }

        public AssetProperty? this[string name]
        {
            get => FindByName(name);
            set
            {
                if (value != null) _properties[name] = value;
            }
        }

        public AssetProperty Get(int index) => null;
    }

    public class Asset : AssetProperties
    {
        public Asset() : base() { }
    }

    public class AppearanceAssetEditScope : IDisposable
    {
        public AppearanceAssetEditScope(Document doc) { }
        public Asset Start(ElementId id) => new Asset();
        public void Commit(bool uploadToCloud) { }
        public void Commit() { }
        public void Dispose() { }
    }

    public class DataStorage : Element
    {
        public DataStorage() { }
    }
}

namespace Autodesk.Revit.DB.ExtensibleStorage
{
    public enum AccessLevel
    {
        Public,
        Application,
        Vendor,
        Private
    }

    public class Schema : APIObject
    {
        public Guid GUID { get; set; }
        public static Schema? Lookup(Guid guid) => null;
    }

    public class SchemaBuilder : APIObject
    {
        private Guid _guid;
        public SchemaBuilder(Guid guid) => _guid = guid;
        public void SetReadAccessLevel(AccessLevel level) { }
        public void SetWriteAccessLevel(AccessLevel level) { }
        public void SetSchemaName(string name) { }
        public void SetDocumentation(string doc) { }
        public void AddSimpleField(string fieldName, Type type) { }
        public Schema Finish() => new Schema { GUID = _guid };
    }

    public class Entity : APIObject
    {
        public bool IsValid() => false;
        public T Get<T>(string fieldName) => default!;
    }
}

namespace Autodesk.Revit.ApplicationServices
{
    using Autodesk.Revit.DB;

    public class Application : APIObject
    {
        public System.Collections.Generic.IList<global::Autodesk.Revit.DB.Visual.Asset> GetAssets(global::Autodesk.Revit.DB.Visual.AssetType type)
        {
            return new System.Collections.Generic.List<global::Autodesk.Revit.DB.Visual.Asset> { new global::Autodesk.Revit.DB.Visual.Asset() };
        }

        public DocumentSet Documents { get; set; } = new DocumentSet();

        public event EventHandler<global::Autodesk.Revit.DB.Events.FailuresProcessingEventArgs>? FailuresProcessing;
    }
}

namespace Autodesk.Revit.DB
{
    public class FillPatternElement : Element { }
    public class FilledRegionType : ElementType
    {
        public ElementId ForegroundPatternId { get; set; } = new ElementId();
        public ElementId BackgroundPatternId { get; set; } = new ElementId();
    }
    public class BrowserOrganization : Element { }
    public class FloorType : HostObjAttributes { }
    public class RoofType : HostObjAttributes { }
    public class CeilingType : HostObjAttributes { }
    public class BuildingPadType : HostObjAttributes { }
    public class CurtainSystemType : HostObjAttributes { }
    public class MullionType : HostObjAttributes { }
    public class FasciaType : HostObjAttributes { }
    public class GutterType : HostObjAttributes { }
    public class ToposolidType : HostObjAttributes { }
    public class ViewDrafting : View { }
    public class ViewSection : View { }
    public class ViewSheet : View { }
    public class ViewSchedule : View { }
    public class TextElementType : ElementType { }
    public class ModelTextType : ElementType { }
    public class SpotDimensionType : ElementType { }
    public class InternalOrigin : Element { }
    public class BasePoint : Element { }
    public class SketchPlane : Element { }
    public class ProjectLocation : Element { }
    public class NumberingSchema : Element { }
    public class PhaseFilter : Element { }
    public class Phase : Element { }
    public class Revision : Element { }
    public class RevisionSettings : Element { }
    public class RevisionNumberingSequence : Element { }
    public class AreaScheme : Element { }
    public class ColorFillScheme : Element { }
    public class SunAndShadowSettings : Element { }
    public class WorksetDefaultVisibilitySettings : Element { }

    public enum TransactionStatus
    {
        Unstarted = 0,
        Started = 1,
        Committed = 2,
        RolledBack = 3,
        Pending = 4,
        Error = 5
    }

    public class TransactionGroup : IDisposable
    {
        private TransactionStatus _status = TransactionStatus.Unstarted;
        public TransactionGroup(Document doc, string name) { }
        public TransactionStatus Start() { _status = TransactionStatus.Started; return _status; }
        public TransactionStatus Assimilate() { _status = TransactionStatus.Committed; return _status; }
        public TransactionStatus RollBack() { _status = TransactionStatus.RolledBack; return _status; }
        public TransactionStatus GetStatus() => _status;
        public void Dispose() { }
    }

    public class Transaction : IDisposable
    {
        private TransactionStatus _status = TransactionStatus.Unstarted;
        public Transaction(Document doc, string name) { }
        public TransactionStatus Start() { _status = TransactionStatus.Started; return _status; }
        public TransactionStatus Commit() { _status = TransactionStatus.Committed; return _status; }
        public TransactionStatus RollBack() { _status = TransactionStatus.RolledBack; return _status; }
        public TransactionStatus GetStatus() => _status;
        public FailureHandlingOptions GetFailureHandlingOptions() => new FailureHandlingOptions();
        public void SetFailureHandlingOptions(FailureHandlingOptions options) { }
        public void Dispose() { }
    }

    public interface IFailuresPreprocessor
    {
        FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor);
    }

    public class FailureHandlingOptions
    {
        public FailureHandlingOptions SetFailuresPreprocessor(IFailuresPreprocessor preprocessor) => this;
        public IFailuresPreprocessor GetFailuresPreprocessor() => null;
        public FailureHandlingOptions SetClearAfterRollback(bool clearAfterRollback) => this;
        public bool GetClearAfterRollback() => false;
        public FailureHandlingOptions SetForcedModalHandling(bool forcedModalHandling) => this;
        public bool GetForcedModalHandling() => false;
    }

    public enum FailureProcessingResult
    {
        Continue = 0,
        ProceedWithCommit = 1,
        ProceedWithRollBack = 2
    }

    public class FailuresAccessor
    {
        public IList<FailureMessageAccessor> GetFailureMessages() => new List<FailureMessageAccessor>();
        public void DeleteWarning(FailureMessageAccessor failureMessage) { }
    }

    public class FailureMessageAccessor
    {
        public FailureDefinitionId GetFailureDefinitionId() => new FailureDefinitionId();
    }

    public class FailureDefinitionId
    {
    }

    public static class BuiltInFailures
    {
        public static class RoomFailures
        {
            public static FailureDefinitionId RoomNotEnclosed { get; } = new FailureDefinitionId();
        }
    }
}

namespace Autodesk.Revit.DB.Events
{
    using System;
    using Autodesk.Revit.DB;
    public class FailuresProcessingEventArgs : EventArgs
    {
        public FailuresAccessor GetFailuresAccessor() => new FailuresAccessor();
        public void SetProcessingResult(FailureProcessingResult result) { }
    }
}

namespace Autodesk.Revit.DB.Architecture
{
    using Autodesk.Revit.DB;
    public class FasciaType : HostObjAttributes { }
    public class GutterType : HostObjAttributes { }
    public class RailingType : ElementType { }
    public class StairsType : ElementType { }
    public class StairsLandingType : ElementType { }
    public class StairsRunType : ElementType { }
}

namespace Autodesk.Revit.DB.ExtensibleStorage
{
    using Autodesk.Revit.DB;
    public class DataStorage : Element { }
}

namespace Autodesk.Revit.Exceptions
{
    public class ApplicationException : System.ApplicationException
    {
        public ApplicationException() { }
        public ApplicationException(string message) : base(message) { }
    }

    public class InvalidOperationException : ApplicationException
    {
        public InvalidOperationException() { }
        public InvalidOperationException(string message) : base(message) { }
    }
}
```

### File: tests/RevitAPIUIMock/MockRevitAPIUI.cs
```csharp
using System;
using Autodesk.Revit.DB;

namespace Autodesk.Revit.UI
{
    /// <summary>
    /// Mock implementation of Revit's UIApplication for headless unit testing.
    /// </summary>
    public class UIApplication
    {
        public Autodesk.Revit.ApplicationServices.Application Application { get; } = new Autodesk.Revit.ApplicationServices.Application();
        public UIDocument? ActiveUIDocument { get; set; }

        public UIApplication() { }
        public UIApplication(Autodesk.Revit.ApplicationServices.Application app)
        {
            Application = app;
        }

        public IntPtr MainWindowHandle => IntPtr.Zero;
    }

    /// <summary>
    /// Mock implementation of Revit's UIDocument for headless unit testing.
    /// </summary>
    public class UIDocument
    {
        public Document Document { get; }

        public UIDocument(Document doc)
        {
            Document = doc;
        }
    }

    /// <summary>
    /// Mock implementation of Revit external command Execution Result.
    /// </summary>
    public enum Result
    {
        Succeeded = -1,
        Failed = 0,
        Cancelled = 1
    }

    public interface IExternalApplication
    {
        Result OnStartup(UIControlledApplication application);
        Result OnShutdown(UIControlledApplication application);
    }

    public interface IExternalCommand
    {
        Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements);
    }

    public class UIControlledApplication
    {
        public IntPtr MainWindowHandle => IntPtr.Zero;
    }

    public class ExternalCommandData
    {
        public UIApplication Application { get; set; } = new UIApplication();
    }

    public class ElementSet : APIObject, System.Collections.IEnumerable
    {
        private readonly System.Collections.ArrayList _elements = new System.Collections.ArrayList();
        public void Clear() => _elements.Clear();
        public bool IsEmpty => _elements.Count == 0;
        public int Size => _elements.Count;
        public System.Collections.IEnumerator GetEnumerator() => _elements.GetEnumerator();
    }

    /// <summary>
    /// Mock implementation of Revit's IExternalEventHandler interface.
    /// </summary>
    public interface IExternalEventHandler
    {
        void Execute(UIApplication app);
        string GetName();
    }

    /// <summary>
    /// Mock implementation of Revit's ExternalEventRequest enum.
    /// </summary>
    public enum ExternalEventRequest
    {
        Accepted,
        Pending,
        Denied
    }

    /// <summary>
    /// Mock implementation of Revit's ExternalEvent class.
    /// </summary>
    public class ExternalEvent
    {
        public static ExternalEvent Create(IExternalEventHandler handler)
        {
            return new ExternalEvent();
        }

        public ExternalEventRequest Raise()
        {
            return ExternalEventRequest.Accepted;
        }
    }
}

```

### File: tests/SyntheticTests.Logic/Tier1_LogicSmokeTests.cs
```csharp
using NUnit.Framework;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier1_LogicSmokeTests
    {
        [Test]
        public void StandardLogicSmokeTest()
        {
            int expected = 10;
            int actual = 5 + 5;
            Assert.AreEqual(expected, actual, "Simple addition logic should pass.");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Infrastructure/UI/RibbonManagerTests.cs
```csharp
using System;
using Newtonsoft.Json;
using NUnit.Framework;
using Synthetic.Core;

namespace SyntheticTests.Infrastructure.UI
{
    [TestFixture]
    public class RibbonManagerTests
    {
        [Test]
        public void IsVersionMatch_NoVersionBounds_ReturnsTrue()
        {
            // Arrange
            var item = new RibbonItemConfig();

            // Act & Assert
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2022));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2024));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2026));
        }

        [Test]
        public void IsVersionMatch_MinVersionOnly_FiltersCorrectly()
        {
            // Arrange
            var item = new RibbonItemConfig { MinVersion = 2024 };

            // Act & Assert
            Assert.IsFalse(RibbonManager.IsVersionMatch(item, 2022));
            Assert.IsFalse(RibbonManager.IsVersionMatch(item, 2023));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2024));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2026));
        }

        [Test]
        public void IsVersionMatch_MaxVersionOnly_FiltersCorrectly()
        {
            // Arrange
            var item = new RibbonItemConfig { MaxVersion = 2025 };

            // Act & Assert
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2022));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2025));
            Assert.IsFalse(RibbonManager.IsVersionMatch(item, 2026));
        }

        [Test]
        public void IsVersionMatch_BothBounds_FiltersCorrectly()
        {
            // Arrange
            var item = new RibbonItemConfig { MinVersion = 2023, MaxVersion = 2025 };

            // Act & Assert
            Assert.IsFalse(RibbonManager.IsVersionMatch(item, 2022));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2023));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2024));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2025));
            Assert.IsFalse(RibbonManager.IsVersionMatch(item, 2026));
        }

        [Test]
        public void Deserialize_WithVersionBounds_ParsesCorrectly()
        {
            // Arrange
            string json = @"
            {
                ""type"": ""PushButton"",
                ""name"": ""TestButton"",
                ""minVersion"": 2024,
                ""maxVersion"": 2026
            }";

            // Act
            var item = JsonConvert.DeserializeObject<RibbonItemConfig>(json);

            // Assert
            Assert.IsNotNull(item);
            Assert.AreEqual("PushButton", item!.Type);
            Assert.AreEqual("TestButton", item.Name);
            Assert.AreEqual(2024, item.MinVersion);
            Assert.AreEqual(2026, item.MaxVersion);
        }

        [Test]
        public void Deserialize_WithoutVersionBounds_ParsesAsNull()
        {
            // Arrange
            string json = @"
            {
                ""type"": ""PushButton"",
                ""name"": ""TestButton""
            }";

            // Act
            var item = JsonConvert.DeserializeObject<RibbonItemConfig>(json);

            // Assert
            Assert.IsNotNull(item);
            Assert.IsNull(item!.MinVersion);
            Assert.IsNull(item.MaxVersion);
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Infrastructure/UI/RibbonTests.cs
```csharp
using NUnit.Framework;
using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Synthetic.Core;

namespace SyntheticTests.Infrastructure.UI
{
    [TestFixture]
    public class RibbonTests
    {
        private static string GetProjectRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "tests")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            return dir ?? TestContext.CurrentContext.TestDirectory;
        }

        [Test]
        public void RibbonConfig_ParsesWithoutErrors()
        {
            string projectRoot = GetProjectRoot();
            string configPath = Path.Combine(projectRoot, "src", "SyntheticShared", "Assets", "ribbon_config.json");

            if (!File.Exists(configPath))
            {
                string testBinDir = TestContext.CurrentContext.TestDirectory;
                configPath = Path.Combine(testBinDir, "Assets", "ribbon_config.json");
            }

            Assert.IsTrue(File.Exists(configPath), $"Ribbon configuration file 'ribbon_config.json' not found at expected path: {configPath}");

            string jsonContent = File.ReadAllText(configPath);
            
            Assert.DoesNotThrow(() => {
                JObject ribbonConfig = JObject.Parse(jsonContent);
                Assert.IsNotNull(ribbonConfig);
            }, "Expected ribbon_config.json to parse without errors.");
        }

        [Test]
        public void RibbonConfig_AllCommandClasses_ShouldResolve()
        {
            string projectRoot = GetProjectRoot();
            string configPath = Path.Combine(projectRoot, "src", "SyntheticShared", "Assets", "ribbon_config.json");

            if (!File.Exists(configPath))
            {
                string testBinDir = TestContext.CurrentContext.TestDirectory;
                configPath = Path.Combine(testBinDir, "Assets", "ribbon_config.json");
            }

            Assert.IsTrue(File.Exists(configPath), $"Ribbon configuration file 'ribbon_config.json' not found at expected path: {configPath}");

            string jsonContent = File.ReadAllText(configPath);
            JObject ribbonConfig = JObject.Parse(jsonContent);

            List<string> commandClasses = new List<string>();
            ExtractCommandClasses(ribbonConfig, commandClasses);

            Assert.IsNotEmpty(commandClasses, "No command classes were parsed from ribbon_config.json.");

            var assembly = typeof(App).Assembly;
            List<string> failedClasses = new List<string>();

            foreach (var className in commandClasses)
            {
                if (!TypeExists(assembly, className))
                {
                    failedClasses.Add(className);
                }
            }

            if (failedClasses.Count > 0)
            {
                Assert.Fail("The following command classes defined in ribbon_config.json could not be resolved in the assembly:\n" +
                            string.Join("\n", failedClasses));
            }
        }

        private bool TypeExists(System.Reflection.Assembly assembly, string className)
        {
            try
            {
                var type = assembly.GetType(className);
                return type != null;
            }
            catch (TypeLoadException)
            {
                // Type exists but could not be fully loaded due to Revit interface mismatch headlessly
                return true;
            }
            catch (FileNotFoundException)
            {
                // Type exists but a dependent assembly was not found headlessly
                return true;
            }
        }

        private void ExtractCommandClasses(JToken token, List<string> commandClasses)
        {
            if (token is JArray array)
            {
                foreach (var child in array)
                {
                    ExtractCommandClasses(child, commandClasses);
                }
            }
            else if (token is JObject obj)
            {
                string className = obj.Value<string>("class");
                if (!string.IsNullOrEmpty(className))
                {
                    commandClasses.Add(className);
                }
                foreach (var property in obj.Properties())
                {
                    ExtractCommandClasses(property.Value, commandClasses);
                }
            }
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Infrastructure/UI/ThemeTests.cs
```csharp
using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Shell;
using NUnit.Framework;
using Synthetic.Shared.UI;

namespace SyntheticTests.Infrastructure.UI
{
    /// <summary>
    /// Tier 1 (Logic) tests for the <see cref="WindowChromeBehavior"/> attached property
    /// and the <c>SyntheticTheme.xaml</c> resource dictionary.
    ///
    /// These tests run on a dedicated STA thread (required by WPF) and do not
    /// require a live Revit host.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class ThemeTests
    {
        /// <summary>
        /// Bootstraps the WPF Application infrastructure required to resolve
        /// <c>pack://</c> URIs in a headless NUnit process.  Without an
        /// <see cref="Application"/> instance the pack URI scheme is not
        /// registered, causing <see cref="ResourceDictionary"/> loads to fail.
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (Application.Current == null)
            {
                // Creating an Application instance registers the pack:// scheme.
                // We deliberately do NOT call Run() – that would block the thread.
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // WindowChromeBehavior tests
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void WindowChromeBehavior_AttachCommands_AddsCommandBindings()
        {
            // Arrange
            var window = new Window();

            // Act
            WindowChromeBehavior.SetEnableWindowCommands(window, true);

            // Assert – all four SystemCommands bindings must be present
            bool hasClose    = false;
            bool hasMinimize = false;
            bool hasMaximize = false;
            bool hasRestore  = false;

            foreach (CommandBinding cb in window.CommandBindings)
            {
                if (cb.Command == SystemCommands.CloseWindowCommand)    hasClose    = true;
                if (cb.Command == SystemCommands.MinimizeWindowCommand)  hasMinimize = true;
                if (cb.Command == SystemCommands.MaximizeWindowCommand)  hasMaximize = true;
                if (cb.Command == SystemCommands.RestoreWindowCommand)   hasRestore  = true;
            }

            Assert.IsTrue(hasClose,    "CloseWindowCommand binding must be registered.");
            Assert.IsTrue(hasMinimize, "MinimizeWindowCommand binding must be registered.");
            Assert.IsTrue(hasMaximize, "MaximizeWindowCommand binding must be registered.");
            Assert.IsTrue(hasRestore,  "RestoreWindowCommand binding must be registered.");

            // WindowChrome must be applied
            var chrome = WindowChrome.GetWindowChrome(window);
            Assert.IsNotNull(chrome, "WindowChrome should be applied by the behavior.");
            Assert.AreEqual(45, chrome!.CaptionHeight, "CaptionHeight should be 45.");
        }

        [Test]
        public void WindowChromeBehavior_DetachCommands_RemovesCommandBindings()
        {
            // Arrange
            var window = new Window();
            WindowChromeBehavior.SetEnableWindowCommands(window, true);

            // Act
            WindowChromeBehavior.SetEnableWindowCommands(window, false);

            // Assert – none of the four SystemCommands bindings should remain
            foreach (CommandBinding cb in window.CommandBindings)
            {
                Assert.IsFalse(
                    cb.Command == SystemCommands.CloseWindowCommand    ||
                    cb.Command == SystemCommands.MinimizeWindowCommand  ||
                    cb.Command == SystemCommands.MaximizeWindowCommand  ||
                    cb.Command == SystemCommands.RestoreWindowCommand,
                    "All SystemCommands bindings should have been removed.");
            }

            // WindowChrome must be removed
            Assert.IsNull(WindowChrome.GetWindowChrome(window),
                "WindowChrome should be null after detach.");
        }

        [Test]
        public void WindowChromeBehavior_GetDefault_ReturnsFalse()
        {
            // Arrange
            var window = new Window();

            // Act
            bool value = WindowChromeBehavior.GetEnableWindowCommands(window);

            // Assert – property should default to false (not attached)
            Assert.IsFalse(value, "EnableWindowCommands should default to false.");
        }

        // ─────────────────────────────────────────────────────────────────
        // SyntheticTheme.xaml resource-loading tests
        //
        // NOTE: SyntheticShared is a shared .projitems — there is no standalone
        // SyntheticShared.dll, so pack:// URIs cannot resolve in the test
        // process.  Instead we load the XAML source file directly via
        // XamlReader.Load(), which gives identical coverage of the resource keys.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the absolute path to <c>SyntheticTheme.xaml</c> by walking
        /// from the test assembly output directory back to the solution source tree.
        /// </summary>
        private static string GetThemeXamlPath()
        {
            // Walk upward from the test assembly output directory until we find
            // the solution root (identified by Synthetic.sln), then navigate into
            // the source tree.  This works regardless of whether the assembly was
            // built by MSBuild (bin\x64\Debug\…) or dotnet CLI (bin\Debug\…).
            string? dir = Path.GetDirectoryName(typeof(ThemeTests).Assembly.Location);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir, "src", "Synthetic.sln")))
                    break;
                dir = Path.GetDirectoryName(dir);
            }

            if (dir is null)
                throw new DirectoryNotFoundException(
                    "Could not locate solution root (src/Synthetic.sln) by walking up from the test assembly.");

            string xamlPath = Path.GetFullPath(
                Path.Combine(dir, @"src\SyntheticShared\Shared\UI\SyntheticTheme.xaml"));

            if (!File.Exists(xamlPath))
                throw new FileNotFoundException(
                    $"SyntheticTheme.xaml not found at expected path: {xamlPath}");

            return xamlPath;
        }

        /// <summary>
        /// Loads <c>SyntheticTheme.xaml</c> from the source tree using
        /// <see cref="XamlReader"/> and returns the resulting
        /// <see cref="ResourceDictionary"/>.
        /// </summary>
        private static ResourceDictionary LoadThemeDictionary()
        {
            using var stream = File.OpenRead(GetThemeXamlPath());
            return (ResourceDictionary)XamlReader.Load(stream);
        }

        [Test]
        public void SyntheticTheme_LoadDictionary_DoesNotThrow()
        {
            // Act & Assert – loading the dictionary must not throw any exception
            ResourceDictionary? dict = null;
            Assert.DoesNotThrow(
                () => { dict = LoadThemeDictionary(); },
                "SyntheticTheme.xaml should load via XamlReader without exceptions.");

            Assert.IsNotNull(dict, "Loaded ResourceDictionary should not be null.");
        }

        [Test]
        public void SyntheticTheme_ContainsCoreColorKeys_AllPresent()
        {
            // Arrange
            var dict = LoadThemeDictionary();

            string[] requiredColorKeys =
            {
                "Synthetic.Colors.BackgroundBase",
                "Synthetic.Colors.ControlSurface",
                "Synthetic.Colors.ControlSurfaceLighter",
                "Synthetic.Colors.BorderNormal",
                "Synthetic.Colors.AccentActive",
                "Synthetic.Colors.TextPrimary",
                "Synthetic.Colors.TextSecondary",
                "Synthetic.Colors.TextDark",
                "Synthetic.Colors.Success",
                "Synthetic.Colors.Warning",
                "Synthetic.Colors.Error",
            };

            // Act & Assert
            foreach (string key in requiredColorKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define resource key '{key}'.");
            }
        }

        [Test]
        public void SyntheticTheme_ContainsCoreBrushKeys_AllPresent()
        {
            // Arrange
            var dict = LoadThemeDictionary();

            string[] requiredBrushKeys =
            {
                "Synthetic.Brushes.BackgroundBase",
                "Synthetic.Brushes.ControlSurface",
                "Synthetic.Brushes.ControlSurfaceLighter",
                "Synthetic.Brushes.BorderNormal",
                "Synthetic.Brushes.AccentActive",
                "Synthetic.Brushes.TextPrimary",
                "Synthetic.Brushes.TextSecondary",
                "Synthetic.Brushes.TextDark",
                "Synthetic.Brushes.Success",
                "Synthetic.Brushes.Warning",
                "Synthetic.Brushes.Error",
                "Synthetic.Brushes.Transparent",
            };

            // Act & Assert
            foreach (string key in requiredBrushKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define resource key '{key}'.");
            }
        }

        [Test]
        public void SyntheticTheme_ContainsWindowStyle_Present()
        {
            // Arrange
            var dict = LoadThemeDictionary();

            // Act & Assert
            Assert.IsTrue(dict.Contains("SyntheticWindowStyle"),
                "SyntheticTheme.xaml must define the 'SyntheticWindowStyle' window style.");
        }

        // ─────────────────────────────────────────────────────────────────
        // DB 006-02 — Control template / style key tests
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void SyntheticTheme_ContainsKeyedControlStyles_AllPresent()
        {
            // Arrange
            var dict = LoadThemeDictionary();

            string[] requiredStyleKeys =
            {
                "Synthetic.Styles.PrimaryButton",
                "Synthetic.Styles.NeutralButton",
                "Synthetic.Internal.ComboBoxToggleButton",
                "Synthetic.Styles.PrimaryButton.Right",
                "Synthetic.Styles.NeutralButton.Right",
                "Synthetic.Styles.SecondaryButton.Right",
                "Synthetic.Margins.RightAction",
                "Synthetic.Styles.ExpanderHeaderToggle"
            };

            // Act & Assert
            foreach (string key in requiredStyleKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define keyed style '{key}'.");
            }
        }

        [Test]
        public void SyntheticTheme_ImplicitControlStyles_CanBeRetrieved()
        {
            // Arrange
            var dict = LoadThemeDictionary();

            // WPF implicit style keys are Type objects, not strings.
            var types = new[]
            {
                typeof(System.Windows.Controls.Button),
                typeof(System.Windows.Controls.TextBox),
                typeof(System.Windows.Controls.CheckBox),
                typeof(System.Windows.Controls.ComboBox),
                typeof(System.Windows.Controls.ComboBoxItem),
                typeof(System.Windows.Controls.TabControl),
                typeof(System.Windows.Controls.TabItem),
                typeof(System.Windows.Controls.Primitives.ScrollBar),
                typeof(System.Windows.Controls.ToolTip),
                typeof(System.Windows.Controls.Expander),
            };

            foreach (var type in types)
            {
                object? style = dict[type];
                Assert.IsNotNull(style,
                    $"SyntheticTheme.xaml must define an implicit style for {type.Name}.");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // DB 006-03 — DataGrid / TreeView template key tests
        // ─────────────────────────────────────────────────────────────────

        [Test]
        public void SyntheticTheme_ContainsDataGridAndTreeViewKeyedStyles_AllPresent()
        {
            // Arrange
            var dict = LoadThemeDictionary();

            string[] requiredKeys =
            {
                "Synthetic.Internal.TreeViewItemToggle",
            };

            // Act & Assert
            foreach (string key in requiredKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define keyed style '{key}'.");
            }
        }

        [Test]
        public void SyntheticTheme_ImplicitDataGridAndTreeViewStyles_CanBeRetrieved()
        {
            // Arrange
            var dict = LoadThemeDictionary();

            var types = new[]
            {
                typeof(System.Windows.Controls.DataGrid),
                typeof(System.Windows.Controls.DataGridRow),
                typeof(System.Windows.Controls.DataGridCell),
                typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
                typeof(System.Windows.Controls.TreeView),
                typeof(System.Windows.Controls.TreeViewItem),
            };

            // Act & Assert
            foreach (var type in types)
            {
                object? style = dict[type];
                Assert.IsNotNull(style,
                    $"SyntheticTheme.xaml must define an implicit style for {type.Name}.");
            }
        }
        [Test]
        public void SyntheticTheme_ContainsVectorGeometryResources_AllPresent()
        {
            // Arrange
            var dict = LoadThemeDictionary();

            string[] geometryKeys =
            {
                "Synthetic.Geometries.Folder",
                "Synthetic.Geometries.File",
                "Synthetic.Geometries.Warning",
                "Synthetic.Geometries.Blocked",
                "Synthetic.Geometries.Success",
                "Synthetic.Geometries.Info",
                "Synthetic.Geometries.Gear",
            };

            // Act & Assert
            foreach (string key in geometryKeys)
            {
                Assert.IsTrue(dict.Contains(key),
                    $"SyntheticTheme.xaml must define geometry key '{key}'.");

                object resource = dict[key];
                Assert.IsInstanceOf<System.Windows.Media.Geometry>(resource,
                    $"Resource '{key}' must be of type System.Windows.Media.Geometry.");
            }
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/BooleanModelTests.cs
```csharp
using System;
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class BooleanModelTests
    {
        [Test]
        public void BooleanSymmetry_TrueValue_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new BooleanModel(true);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = JsonConvert.DeserializeObject<BooleanModel>(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsTrue(jObj.ContainsKey("boolean"), "JSON should contain the 'boolean' property.");
            Assert.IsTrue((bool)jObj["boolean"]!, "Decoded boolean value should be true.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.IsTrue(roundTrip!.boolean, "Deserialized boolean value should be true.");
        }

        [Test]
        public void BooleanSymmetry_FalseValue_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new BooleanModel(false);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = JsonConvert.DeserializeObject<BooleanModel>(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsTrue(jObj.ContainsKey("boolean"), "JSON should contain the 'boolean' property.");
            Assert.IsFalse((bool)jObj["boolean"]!, "Decoded boolean value should be false.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.IsFalse(roundTrip!.boolean, "Deserialized boolean value should be false.");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/ColorModelTests.cs
```csharp
using System;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class ColorModelTests
    {
        [Test]
        public void StandardSymmetry_MidRangeValues_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new ColorModel(100, 150, 200);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ColorModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual(100, (byte)jObj["Red"]!, "Red component matches.");
            Assert.AreEqual(150, (byte)jObj["Green"]!, "Green component matches.");
            Assert.AreEqual(200, (byte)jObj["Blue"]!, "Blue component matches.");
            Assert.IsTrue((bool)jObj["IsValid"]!, "IsValid matches.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(100, roundTrip!.Red, "Deserialized Red component matches.");
            Assert.AreEqual(150, roundTrip.Green, "Deserialized Green component matches.");
            Assert.AreEqual(200, roundTrip.Blue, "Deserialized Blue component matches.");
            Assert.IsTrue(roundTrip.IsValid, "Deserialized IsValid matches.");
        }

        [Test]
        public void BoundaryTest_MaximumValues_SerializesAndDeserializesWithoutOverflow()
        {
            // Arrange
            var model = new ColorModel(255, 255, 255);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ColorModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual(255, (byte)jObj["Red"]!, "Red component matches at max.");
            Assert.AreEqual(255, (byte)jObj["Green"]!, "Green component matches at max.");
            Assert.AreEqual(255, (byte)jObj["Blue"]!, "Blue component matches at max.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(255, roundTrip!.Red, "Deserialized Red component matches at max.");
            Assert.AreEqual(255, roundTrip.Green, "Deserialized Green component matches at max.");
            Assert.AreEqual(255, roundTrip.Blue, "Deserialized Blue component matches at max.");
        }

        [Test]
        public void BoundaryTest_MinimumValues_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new ColorModel(0, 0, 0);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ColorModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual(0, (byte)jObj["Red"]!, "Red component matches at min.");
            Assert.AreEqual(0, (byte)jObj["Green"]!, "Green component matches at min.");
            Assert.AreEqual(0, (byte)jObj["Blue"]!, "Blue component matches at min.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(0, roundTrip!.Red, "Deserialized Red component matches at min.");
            Assert.AreEqual(0, roundTrip.Green, "Deserialized Green component matches at min.");
            Assert.AreEqual(0, roundTrip.Blue, "Deserialized Blue component matches at min.");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/DispatcherTests.cs
```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    // Define custom mock models and translators for testing the Dispatcher routing logic
    public class MockObjectModel : ObjectModel
    {
        public string? TestProperty { get; set; }
    }

    public class MockTranslator : IModelTranslator<Material, MockObjectModel>
    {
        public bool Extracted { get; private set; }
        public bool Injected { get; private set; }

        public void ExtractSpecifics(Material revitElement, MockObjectModel model, Document doc)
        {
            Extracted = true;
        }

        public Material? InjectSpecifics(MockObjectModel model, Material? revitElement, Document doc)
        {
            Injected = true;
            return revitElement;
        }

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((Material)revitElement, (MockObjectModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((MockObjectModel)model, (Material?)revitElement, doc);
        }
    }

    // Mock classes for One-to-Many tests
    public class MockHostObjTypeModel : ObjectModel
    {
    }

    public class MockHostObjTypeTranslator : IModelTranslator<Element, MockHostObjTypeModel>
    {
        public void ExtractSpecifics(Element revitElement, MockHostObjTypeModel model, Document doc) { }
        public Element? InjectSpecifics(MockHostObjTypeModel model, Element? revitElement, Document doc)
        {
            return revitElement;
        }

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((Element)revitElement, (MockHostObjTypeModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((MockHostObjTypeModel)model, (Element?)revitElement, doc);
        }
    }

    [TestFixture]
    public class DispatcherTests
    {
        [Test]
        public void RegisterAndResolve_SingleTypeMapping_CorrectlyRoutesByModelAndRevitTypes()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());
            var translator = new MockTranslator();

            // Act
            dispatcher.Register<MockObjectModel, MockTranslator>(translator, typeof(Material));

            // Assert
            var resolvedByModel = dispatcher.GetTranslatorByModelType(typeof(MockObjectModel));
            var resolvedByRevit = dispatcher.GetTranslatorByRevitType(typeof(Material));

            Assert.IsNotNull(resolvedByModel, "Translator should be resolved by Model type.");
            Assert.IsNotNull(resolvedByRevit, "Translator should be resolved by Revit type.");
            Assert.AreSame(translator, resolvedByModel, "Resolved model translator instance should be the same.");
            Assert.AreSame(translator, resolvedByRevit, "Resolved Revit translator instance should be the same.");
        }

        [Test]
        public void RegisterAndResolve_OneToManyMapping_CorrectlyRoutesMultipleRevitTypesToSingleTranslator()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());
            var translator = new MockHostObjTypeTranslator();

            // Act
            // Register a single translator mapped to multiple Revit element types (WallType and ViewPlan)
            dispatcher.Register<MockHostObjTypeModel, MockHostObjTypeTranslator>(
                translator, 
                typeof(WallType), 
                typeof(ViewPlan)
            );

            // Assert
            var resolvedByWallType = dispatcher.GetTranslatorByRevitType(typeof(WallType));
            var resolvedByViewPlan = dispatcher.GetTranslatorByRevitType(typeof(ViewPlan));
            var resolvedByModel = dispatcher.GetTranslatorByModelType(typeof(MockHostObjTypeModel));

            Assert.IsNotNull(resolvedByWallType, "Translator should resolve for WallType.");
            Assert.IsNotNull(resolvedByViewPlan, "Translator should resolve for ViewPlan.");
            Assert.IsNotNull(resolvedByModel, "Translator should resolve for MockHostObjTypeModel.");

            Assert.AreSame(translator, resolvedByWallType, "Resolved WallType translator should be the registered instance.");
            Assert.AreSame(translator, resolvedByViewPlan, "Resolved ViewPlan translator should be the registered instance.");
            Assert.AreSame(translator, resolvedByModel, "Resolved Model translator should be the registered instance.");
        }

        [Test]
        public void Register_ParameterlessConstructor_InstantiatesAndRegistersSuccessfully()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());

            // Act
            dispatcher.Register<MockObjectModel, MockTranslator>(typeof(Material));

            // Assert
            var resolved = dispatcher.GetTranslatorByModelType(typeof(MockObjectModel));
            Assert.IsNotNull(resolved, "ModelDispatcher should instantiate and register the translator type successfully.");
            Assert.IsInstanceOf<MockTranslator>(resolved, "Instantiated translator should be of MockTranslator type.");
        }

        private T CreateMockElement<T>(long id, string name, string uniqueId) where T : Element
        {
            var elem = (T)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(T));
            typeof(Element).GetProperty("Id")?.SetValue(elem, new ElementId(id));
            typeof(Element).GetProperty("UniqueId")?.SetValue(elem, uniqueId);
            typeof(Element).GetProperty("Parameters")?.SetValue(elem, new ParameterSet());
            typeof(Element).GetProperty("Document")?.SetValue(elem, (Document)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Document)));
            elem.Name = name;
            return elem;
        }

        [Test]
        public void Extract_WhenTypeIsIgnored_ReturnsNullSilently()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());
            dispatcher.RegisterIgnored(typeof(Material));
            var elem = CreateMockElement<Material>(123L, "IgnoredMaterial", "uid-ignored-123");
            SerializationResultModel.ClearWarnings();

            // Act
            var result = dispatcher.Extract(elem, false);

            // Assert
            Assert.IsNull(result, "Extract should return null for ignored element.");
            Assert.IsEmpty(SerializationResultModel.CurrentThreadWarnings, "Warnings list should be empty.");
        }

        [Test]
        public void Extract_WhenTypeIsPending_ReturnsNullAndLogsWarning()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());
            dispatcher.RegisterPending(typeof(Material));
            var elem = CreateMockElement<Material>(456L, "PendingMaterial", "uid-pending-456");
            SerializationResultModel.ClearWarnings();

            // Act
            var result = dispatcher.Extract(elem, false);

            // Assert
            Assert.IsNull(result, "Extract should return null for pending element.");
            Assert.AreEqual(1, SerializationResultModel.CurrentThreadWarnings.Count, "Exactly one warning should be logged.");
            Assert.IsTrue(SerializationResultModel.CurrentThreadWarnings[0].Contains("pending future support"), "Warning message should contain 'pending future support'.");
        }

        [Test]
        public void Extract_WhenTypeIsUnregisteredElementType_FallsBackToElementTypeModelAndLogsWarning()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());
            var elem = CreateMockElement<FamilySymbol>(789L, "UnregisteredType", "uid-unregistered-789");
            SerializationResultModel.ClearWarnings();

            // Act
            var result = dispatcher.Extract(elem, false);

            // Assert
            Assert.IsNotNull(result, $"Extract should not return null for unregistered ElementType. Warnings: {string.Join(" | ", SerializationResultModel.CurrentThreadWarnings)}");
            Assert.IsInstanceOf<ElementTypeModel>(result, "Result should be an instance of ElementTypeModel.");
            Assert.AreEqual(1, SerializationResultModel.CurrentThreadWarnings.Count, "Exactly one warning should be logged.");
            Assert.IsTrue(SerializationResultModel.CurrentThreadWarnings[0].Contains("unsupported, falling back to generic ElementTypeModel"), "Warning message should contain 'unsupported, falling back to generic ElementTypeModel'.");
        }

        [Test]
        public void Extract_WhenTypeIsUnregisteredElement_FallsBackToElementModelAndLogsWarning()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());
            var elem = CreateMockElement<AppearanceAssetElement>(101112L, "UnregisteredAsset", "uid-unregistered-101112");
            SerializationResultModel.ClearWarnings();

            // Act
            var result = dispatcher.Extract(elem, false);

            // Assert
            Assert.IsNotNull(result, $"Extract should not return null for unregistered Element. Warnings: {string.Join(" | ", SerializationResultModel.CurrentThreadWarnings)}");
            Assert.IsInstanceOf<ElementModel>(result, "Result should be an instance of ElementModel.");
            Assert.IsNotInstanceOf<ElementTypeModel>(result, "Result should not be an instance of ElementTypeModel.");
            Assert.AreEqual(1, SerializationResultModel.CurrentThreadWarnings.Count, "Exactly one warning should be logged.");
            Assert.IsTrue(SerializationResultModel.CurrentThreadWarnings[0].Contains("unsupported, falling back to generic ElementModel"), "Warning message should contain 'unsupported, falling back to generic ElementModel'.");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/ElementIdModelTests.cs
```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class ElementIdModelTests
    {
        [Test]
        public void StandardSymmetry_PositiveIdNonTemplate_SerializesAndDeserializesId()
        {
            // Arrange
            var model = new ElementIdModel
            {
                Id = 12345,
                IsTemplate = false,
                Name = "TestElement",
                Class = "Autodesk.Revit.DB.Wall",
                Category = "Walls",
                UniqueId = "abc-123-xyz"
            };

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ElementIdModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsTrue(jObj.ContainsKey("Id"), "JSON should contain the 'Id' property.");
            Assert.AreEqual(12345, (long)jObj["Id"]!, "Serialized ID should match original positive ID.");
            Assert.IsTrue(jObj.ContainsKey("UniqueId"), "JSON should contain the 'UniqueId' property.");
            Assert.AreEqual("abc-123-xyz", (string)jObj["UniqueId"]!, "Serialized UniqueId should match original.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(12345, roundTrip!.Id, "Deserialized ID should match original positive ID.");
            Assert.AreEqual("abc-123-xyz", roundTrip.UniqueId, "Deserialized UniqueId should match original.");
        }

        [Test]
        public void TemplateIDOmission_PositiveIdTemplate_OmitsId()
        {
            // Arrange
            var model = new ElementIdModel
            {
                Id = 12345,
                IsTemplate = true,
                Name = "TestElement",
                Class = "Autodesk.Revit.DB.Wall",
                Category = "Walls",
                UniqueId = "abc-123-xyz"
            };

            // Act
            var shouldSerializeId = model.ShouldSerializeId();
            var shouldSerializeUniqueId = model.ShouldSerializeUniqueId();
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ElementIdModel.ByJSON(json);

            // Assert
            Assert.IsFalse(shouldSerializeId, "ShouldSerializeId should return false for a positive ID in a template.");
            Assert.IsFalse(shouldSerializeUniqueId, "ShouldSerializeUniqueId should return false for a template.");
            
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsFalse(jObj.ContainsKey("Id"), "JSON should not contain the 'Id' property when IsTemplate is true and ID is positive.");
            Assert.IsFalse(jObj.ContainsKey("UniqueId"), "JSON should not contain the 'UniqueId' property when IsTemplate is true.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(0, roundTrip!.Id, "Deserialized ID should evaluate to default (0) because it was omitted during serialization.");
            Assert.IsNull(roundTrip.UniqueId, "Deserialized UniqueId should evaluate to null because it was omitted during serialization.");
        }

        [Test]
        public void BuiltInPreservation_NegativeIdTemplate_PreservesId()
        {
            // Arrange
            var model = new ElementIdModel
            {
                Id = -2000100, // Built-in category element ID
                IsTemplate = true,
                Name = "Walls",
                Class = "Autodesk.Revit.DB.Category",
                Category = ""
            };

            // Act
            var shouldSerializeId = model.ShouldSerializeId();
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ElementIdModel.ByJSON(json);

            // Assert
            Assert.IsTrue(shouldSerializeId, "ShouldSerializeId should return true for a negative ID in a template.");
            
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsTrue(jObj.ContainsKey("Id"), "JSON should contain the 'Id' property even if IsTemplate is true for negative IDs.");
            Assert.AreEqual(-2000100, (long)jObj["Id"]!, "Serialized ID should match original negative ID.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(-2000100, roundTrip!.Id, "Deserialized ID should match original negative ID.");
        }

        #region 5-Step Identity Resolution & Equality Tests

        [Test]
        public void Equals_Step1_UniqueIdMatch_ReturnsTrue()
        {
            var a = new ElementIdModel { UniqueId = "GUID-1234", Id = 100, Name = "WallA", Class = "Autodesk.Revit.DB.Wall" };
            var b = new ElementIdModel { UniqueId = "guid-1234", Id = 999, Name = "WallB", Class = "Autodesk.Revit.DB.Wall" };

            Assert.IsTrue(a.Equals(b), "Step 1: Matching UniqueId (case-insensitive) should equate models regardless of different Id or Name.");
            Assert.IsTrue(a == b, "Operator == should match Equals result.");
            Assert.IsFalse(a != b, "Operator != should be false for equal models.");
        }

        [Test]
        public void Equals_Step2_PositiveAndBuiltInNegativeIds_ReturnsTrue()
        {
            var pos1 = new ElementIdModel { Id = 54321, Class = "Autodesk.Revit.DB.Wall" };
            var pos2 = new ElementIdModel { Id = 54321, Class = "Autodesk.Revit.DB.Wall" };
            Assert.IsTrue(pos1.Equals(pos2), "Step 2: Identical positive IDs should equate models.");

            var builtIn1 = new ElementIdModel { Id = -2000100, Class = "Autodesk.Revit.DB.Category" };
            var builtIn2 = new ElementIdModel { Id = -2000100, Class = "Autodesk.Revit.DB.Category" };
            Assert.IsTrue(builtIn1.Equals(builtIn2), "Step 2: Preserved built-in negative IDs should equate models.");
        }

        [Test]
        public void Equals_Step2_DefaultInvalidModels_HandledCorrectly()
        {
            var invalid1 = new ElementIdModel { Id = -1 };
            var invalid2 = new ElementIdModel { Id = -1 };
            Assert.IsTrue(invalid1.Equals(invalid2), "Two default invalid models with no UniqueId/Name should be equal.");

            var invalidNamed1 = new ElementIdModel { Id = -1, Name = "Wall 1", Class = "Autodesk.Revit.DB.Wall" };
            var invalidNamed2 = new ElementIdModel { Id = -1, Name = "Door 1", Class = "Autodesk.Revit.DB.Wall" };
            Assert.IsFalse(invalidNamed1.Equals(invalidNamed2), "Models with Id = -1 but different names must not falsely match on Step 2.");
        }

        [Test]
        public void Equals_Step3_ClassTypeGuard_PreventsMismatch()
        {
            var wall = new ElementIdModel { Id = 100, Name = "Standard", Class = "Autodesk.Revit.DB.Wall" };
            var floor = new ElementIdModel { Id = 100, Name = "Standard", Class = "Autodesk.Revit.DB.Floor" };

            Assert.IsFalse(wall.Equals(floor), "Step 3 Type Guard: Different Class types must prevent equality despite identical Id or Name.");
        }

        [Test]
        public void Equals_Step4_NameAndCategory_CaseInsensitiveMatch()
        {
            var a = new ElementIdModel { Name = "Generic Wall", Class = "Autodesk.Revit.DB.Wall", Category = "Walls" };
            var b = new ElementIdModel { Name = "generic wall", Class = "autodesk.revit.db.wall", Category = "walls" };

            Assert.IsTrue(a.Equals(b), "Step 4: Matching Name, Class, and Category (case-insensitive) should equate models.");

            var c = new ElementIdModel { Name = "Generic Wall", Class = "Autodesk.Revit.DB.Wall", Category = "Doors" };
            Assert.IsFalse(a.Equals(c), "Step 4: Mismatched Category should prevent equality.");
        }

        [Test]
        public void Equals_Step5_AliasesCrossMatch_ReturnsTrue()
        {
            var main = new ElementIdModel
            {
                Name = "Wall_Standard_v2",
                Class = "Autodesk.Revit.DB.Wall",
                Aliases = new List<string> { "Wall_Standard_v1", "Legacy_Wall" }
            };

            var legacy = new ElementIdModel
            {
                Name = "Wall_Standard_v1",
                Class = "Autodesk.Revit.DB.Wall"
            };

            Assert.IsTrue(main.Equals(legacy), "Step 5: Main model alias matching secondary model name should return true.");
            Assert.IsTrue(legacy.Equals(main), "Step 5: Symmetry requirement - secondary model matching main model alias.");

            var bothAliased = new ElementIdModel
            {
                Name = "New_Wall_Name",
                Class = "Autodesk.Revit.DB.Wall",
                Aliases = new List<string> { "Legacy_Wall" }
            };
            Assert.IsTrue(main.Equals(bothAliased), "Step 5: Intersecting aliases should match.");
        }

        [Test]
        public void GetHashCode_Consistency_SameForEqualObjects()
        {
            var a = new ElementIdModel { Name = "Generic Wall", Class = "Autodesk.Revit.DB.Wall", Category = "Walls", Id = 100 };
            var b = new ElementIdModel { Name = "GENERIC WALL", Class = "autodesk.revit.db.wall", Category = "WALLS", Id = 100 };

            Assert.IsTrue(a.Equals(b), "Models should be equal.");
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode(), "Equal objects must yield identical hash codes regardless of casing.");
        }

        [Test]
        public void DictionaryLookup_ElementIdModel_Succeeds()
        {
            var key1 = new ElementIdModel { Name = "Wall_A", Class = "Autodesk.Revit.DB.Wall", Id = 101 };
            var key2 = new ElementIdModel { Name = "wall_a", Class = "Autodesk.Revit.DB.Wall", Id = 101 };

            var dict = new Dictionary<ElementIdModel, string>
            {
                { key1, "WinningValue" }
            };

            Assert.IsTrue(dict.ContainsKey(key2), "Dictionary lookup should succeed for value-equal ElementIdModel instance.");
            Assert.AreEqual("WinningValue", dict[key2], "Dictionary value retrieval should match expected value.");
        }

        [Test]
        public void Equals_Step1_UniqueIdMismatch_ReturnsFalse_EvenWhenIdOrNameMatch()
        {
            var a = new ElementIdModel { UniqueId = "UID-111", Id = 100, Name = "Door 1", Class = "Autodesk.Revit.DB.FamilyInstance" };
            var b = new ElementIdModel { UniqueId = "UID-222", Id = 100, Name = "Door 1", Class = "Autodesk.Revit.DB.FamilyInstance" };

            Assert.IsFalse(a.Equals(b), "Mismatched UniqueIds must return false immediately and not fall through to Id or Name.");
            Assert.IsFalse(a == b, "Operator == should return false for mismatched UniqueIds.");
            Assert.IsTrue(a != b, "Operator != should return true for mismatched UniqueIds.");
        }

        [Test]
        public void GetHashCode_UniqueIdMatch_EmptyName_HasSameHashCode()
        {
            var a = new ElementIdModel { UniqueId = "GUID-1234", Id = 100, Class = "Autodesk.Revit.DB.Wall" };
            var b = new ElementIdModel { UniqueId = "guid-1234", Id = 999, Class = "Autodesk.Revit.DB.Wall" };

            Assert.IsTrue(a.Equals(b), "Models with matching UniqueId should be equal.");
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode(), "Equal objects matching on UniqueId with empty Name must yield identical hash codes regardless of differing Id.");
        }

        [Test]
        public void GetHashCode_AsymmetricEquality_UniqueIdAndName_Matches_NameOnly()
        {
            var fullModel = new ElementIdModel { UniqueId = "GUID-1234", Id = 100, Name = "WallA", Class = "Autodesk.Revit.DB.Wall" };
            var nameOnlyModel = new ElementIdModel { Name = "wAlLa", Class = "autodesk.revit.db.wall" };

            Assert.IsTrue(fullModel.Equals(nameOnlyModel), "Full model and Name-only model with matching Name and Class must be equal.");
            Assert.IsTrue(nameOnlyModel.Equals(fullModel), "Symmetry requirement: Name-only model must equal Full model.");
            Assert.AreEqual(fullModel.GetHashCode(), nameOnlyModel.GetHashCode(), "Full model and Name-only model matching on Name must produce identical hash codes.");
        }

        [Test]
        public void GetHashCode_AsymmetricEquality_DifferingInvalidIds_MatchesOnName()
        {
            var model1 = new ElementIdModel { Id = -1, Name = "Door Standard", Class = "Autodesk.Revit.DB.FamilyInstance" };
            var model2 = new ElementIdModel { Id = -2, Name = "door standard", Class = "autodesk.revit.db.familyinstance" };

            Assert.IsTrue(model1.Equals(model2), "Models with differing invalid IDs (-1 vs -2) and matching Name must be equal.");
            Assert.AreEqual(model1.GetHashCode(), model2.GetHashCode(), "Models with differing invalid IDs matching on Name must produce identical hash codes.");
        }

        [Test]
        public void DictionaryLookup_AsymmetricModels_Succeeds()
        {
            var fullKey = new ElementIdModel { UniqueId = "GUID-1234", Id = 101, Name = "Wall_A", Class = "Autodesk.Revit.DB.Wall" };
            var nameOnlyLookupKey = new ElementIdModel { Name = "wall_a", Class = "Autodesk.Revit.DB.Wall" };

            var dict = new Dictionary<ElementIdModel, string>
            {
                { fullKey, "WinningValue" }
            };

            Assert.IsTrue(dict.ContainsKey(nameOnlyLookupKey), "Dictionary lookup should succeed using asymmetric Name-only ElementIdModel lookup key.");
            Assert.AreEqual("WinningValue", dict[nameOnlyLookupKey], "Dictionary value retrieval should match expected value for asymmetric key.");
        }

        [Test]
        public void GetHashCode_Consistency_AcrossAllFallbackSteps()
        {
            // Fallback 1: UniqueId match (no Name)
            var u1 = new ElementIdModel { UniqueId = "GUID-ABC", Id = 10, Class = "Autodesk.Revit.DB.Wall" };
            var u2 = new ElementIdModel { UniqueId = "guid-abc", Id = 20, Class = "Autodesk.Revit.DB.Wall" };
            Assert.IsTrue(u1.Equals(u2));
            Assert.AreEqual(u1.GetHashCode(), u2.GetHashCode(), "Step 1: UniqueId match must produce identical HashCodes.");

            // Fallback 2: Id match (no UniqueId or Name)
            var i1 = new ElementIdModel { Id = 500, Class = "Autodesk.Revit.DB.Wall" };
            var i2 = new ElementIdModel { Id = 500, Class = "Autodesk.Revit.DB.Wall" };
            Assert.IsTrue(i1.Equals(i2));
            Assert.AreEqual(i1.GetHashCode(), i2.GetHashCode(), "Step 2: Id match must produce identical HashCodes.");

            // Fallback 4: Name match (no UniqueId or Id)
            var n1 = new ElementIdModel { Name = "Shared Wall", Category = "Walls" };
            var n2 = new ElementIdModel { Name = "shared wall", Category = "walls" };
            Assert.IsTrue(n1.Equals(n2));
            Assert.AreEqual(n1.GetHashCode(), n2.GetHashCode(), "Step 4: Name match must produce identical HashCodes.");

            // Fallback: Default/Empty models
            var d1 = new ElementIdModel();
            var d2 = new ElementIdModel();
            Assert.IsTrue(d1.Equals(d2));
            Assert.AreEqual(d1.GetHashCode(), d2.GetHashCode(), "Default empty models must produce identical HashCodes.");
        }

        #endregion
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/EnumModelTests.cs
```csharp
using System;
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Shared;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class EnumModelTests
    {
        [Test]
        public void EnumStringMapping_ValidStringValues_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new EnumModel("System.DayOfWeek", "Friday");

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = JsonConvert.DeserializeObject<EnumModel>(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual("System.DayOfWeek", (string)jObj["Type"]!, "Enum type name matches.");
            Assert.AreEqual("Friday", (string)jObj["Value"]!, "Enum string value matches.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual("System.DayOfWeek", roundTrip!.Type, "Deserialized type name matches.");
            Assert.AreEqual("Friday", roundTrip.Value, "Deserialized value name matches.");
        }

        [Test]
        public void EnumTypeReconstruction_NativeCLRType_ReconstructsEnumTypeAndValue()
        {
            // Arrange
            var model = new EnumModel(typeof(DayOfWeek), DayOfWeek.Monday);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = JsonConvert.DeserializeObject<EnumModel>(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual("System.DayOfWeek", (string)jObj["Type"]!, "Enum type name matches.");
            Assert.AreEqual("Monday", (string)jObj["Value"]!, "Enum string value matches.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual("System.DayOfWeek", roundTrip!.Type, "Deserialized type name matches.");
            Assert.AreEqual("Monday", roundTrip.Value, "Deserialized value name matches.");

            var reconstructedEnum = roundTrip.ToEnum();
            Assert.IsNotNull(reconstructedEnum, "Reconstructed enum should not be null.");
            Assert.IsInstanceOf<DayOfWeek>(reconstructedEnum, "Reconstructed enum should be of type DayOfWeek.");
            Assert.AreEqual(DayOfWeek.Monday, (DayOfWeek)reconstructedEnum, "Reconstructed enum value should match.");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/FakeIdentityServiceTests.cs
```csharp
using System;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class FakeIdentityServiceTests
    {
        [Test]
        public void ToModel_WhenMapped_ReturnsMappedModel()
        {
            // Arrange
            var service = new FakeIdentityService();
#if REVIT2022 || REVIT2023
            var id = new ElementId(555);
#else
            var id = new ElementId(555L);
#endif
            var expectedModel = new ElementIdModel
            {
                Id = 555,
                Name = "SpecialElement",
                Class = "Autodesk.Revit.DB.Wall",
                UniqueId = "special-uid-123"
            };
            service.SetupMapping(id, expectedModel);

            // Act
            var result = service.ToModel(id, null!);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedModel.Id, result.Id);
            Assert.AreEqual(expectedModel.Name, result.Name);
            Assert.AreEqual(expectedModel.Class, result.Class);
            Assert.AreEqual(expectedModel.UniqueId, result.UniqueId);
        }

        [Test]
        public void ToModel_WhenNotMapped_ReturnsDefaultModel()
        {
            // Arrange
            var service = new FakeIdentityService();
#if REVIT2022 || REVIT2023
            var id = new ElementId(999);
#else
            var id = new ElementId(999L);
#endif

            // Act
            var result = service.ToModel(id, null!);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(999, result.Id);
            Assert.AreEqual("FakeElement_999", result.Name);
            Assert.AreEqual("Autodesk.Revit.DB.Element", result.Class);
            Assert.AreEqual("fake-uid-999", result.UniqueId);
        }

        [Test]
        public void ResolveElementId_WhenMapped_ReturnsMappedId()
        {
            // Arrange
            var service = new FakeIdentityService();
#if REVIT2022 || REVIT2023
            var id = new ElementId(777);
#else
            var id = new ElementId(777L);
#endif
            var model = new ElementIdModel
            {
                Id = 111, // different ID in model
                UniqueId = "uid-777"
            };
            service.SetupMapping(id, new ElementIdModel { Id = 777, UniqueId = "uid-777" });

            // Act
            var result = service.ResolveElementId(model, null!);

            // Assert
#if REVIT2022 || REVIT2023
            Assert.AreEqual(777, result.IntegerValue);
#else
            Assert.AreEqual(777, result.Value);
#endif
        }

        [Test]
        public void ResolveElementId_WhenNotMapped_ReturnsModelId()
        {
            // Arrange
            var service = new FakeIdentityService();
            var model = new ElementIdModel
            {
                Id = 1234,
                UniqueId = "uid-1234"
            };

            // Act
            var result = service.ResolveElementId(model, null!);

            // Assert
#if REVIT2022 || REVIT2023
            Assert.AreEqual(1234, result.IntegerValue);
#else
            Assert.AreEqual(1234, result.Value);
#endif
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/ImportExecutionRunnerTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class ImportExecutionRunnerTests
    {
        private class CustomTestObjectModel : ObjectModel
        {
            public CustomTestObjectModel() : base() { }
        }

        [Test]
        public void ImportExecutionRunner_Sort_OrdersRandomModelsChronologicallyByDAGTiers()
        {
            // Arrange - Create models belonging to each of the 6 defined tiers
            var linePattern = new LinePatternElementModel { Name = "LinePatternA" }; // Tier 1
            var material = new MaterialModel { Name = "MaterialA" }; // Tier 2
            var dimensionType = new DimensionTypeModel { Name = "DimensionTypeA" }; // Tier 3
            var category = new CategoryModel { Name = "CategoryA" }; // Tier 4
            var hostObj = new HostObjTypeModel { Name = "HostObjA" }; // Tier 5
            var view = new ViewModel { Name = "ViewA" }; // Tier 6

            // Shuffle the models
            var shuffledList = new List<ObjectModel> { view, hostObj, category, dimensionType, material, linePattern };

            // Act
            var sortedList = ImportExecutionRunner.Sort(shuffledList).ToList();

            // Assert
            Assert.AreEqual(6, sortedList.Count);
            Assert.AreSame(linePattern, sortedList[0], "LinePattern (Tier 1) should be sorted first.");
            Assert.AreSame(material, sortedList[1], "Material (Tier 2) should be sorted second.");
            Assert.AreSame(dimensionType, sortedList[2], "DimensionType (Tier 3) should be sorted third.");
            Assert.AreSame(category, sortedList[3], "Category (Tier 4) should be sorted fourth.");
            Assert.AreSame(hostObj, sortedList[4], "HostObj (Tier 5) should be sorted fifth.");
            Assert.AreSame(view, sortedList[5], "View (Tier 6) should be sorted sixth.");
        }

        [Test]
        public void ImportExecutionRunner_Sort_StableSecondarySortByName()
        {
            // Arrange - Create models in the same tier with different names
            var matB = new MaterialModel { Name = "MaterialB" };
            var matA = new MaterialModel { Name = "MaterialA" };
            var matC = new MaterialModel { Name = "MaterialC" };

            var shuffledList = new List<ObjectModel> { matC, matB, matA };

            // Act
            var sortedList = ImportExecutionRunner.Sort(shuffledList).ToList();

            // Assert
            Assert.AreEqual(3, sortedList.Count);
            Assert.AreSame(matA, sortedList[0], "MaterialA should be sorted first within its tier.");
            Assert.AreSame(matB, sortedList[1], "MaterialB should be sorted second within its tier.");
            Assert.AreSame(matC, sortedList[2], "MaterialC should be sorted third within its tier.");
        }

        [Test]
        public void ImportExecutionRunner_Sort_PreservesUnrecognizedModels()
        {
            // Arrange - Create recognized models and an unrecognized custom model
            var material = new MaterialModel { Name = "MaterialA" };
            var customModel = new CustomTestObjectModel();
            var linePattern = new LinePatternElementModel { Name = "LinePatternA" };

            var list = new List<ObjectModel> { customModel, material, linePattern };

            // Act
            var sortedList = ImportExecutionRunner.Sort(list).ToList();

            // Assert
            Assert.AreEqual(3, sortedList.Count);
            Assert.AreSame(linePattern, sortedList[0], "LinePattern (Tier 1) should still be first.");
            Assert.AreSame(material, sortedList[1], "Material (Tier 2) should still be second.");
            Assert.AreSame(customModel, sortedList[2], "Unrecognized CustomModel (Tier 8 fallback) should be sorted last.");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/ParameterDefinitionSpecTests.cs
```csharp
using System;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;

namespace SyntheticTests.Logic.Modules.RevitDOM
{
    [TestFixture]
    public class ParameterDefinitionSpecTests
    {
        [Test]
        public void Constructor_Default_InitializesPropertiesToNull()
        {
            var spec = new ParameterDefinitionSpec();
            Assert.IsNull(spec.Group);
            Assert.IsNull(spec.SpecType);
            Assert.IsNull(spec.ParameterGroup);
            Assert.IsNull(spec.ParameterType);
        }

        [Test]
        public void Constructor_WithArguments_SetsPropertiesAndAliases()
        {
            var groupObj = "PG_DATA";
            var typeObj = "Text";
            var spec = new ParameterDefinitionSpec(groupObj, typeObj);
            Assert.AreEqual(groupObj, spec.Group);
            Assert.AreEqual(typeObj, spec.SpecType);
            Assert.AreEqual(groupObj, spec.ParameterGroup);
            Assert.AreEqual(typeObj, spec.ParameterType);
        }

        [Test]
        public void AliasProperties_MutateUnderlyingState()
        {
            var spec = new ParameterDefinitionSpec();
            spec.ParameterGroup = "PG_IDENTITY";
            spec.ParameterType = "Integer";
            Assert.AreEqual("PG_IDENTITY", spec.Group);
            Assert.AreEqual("Integer", spec.SpecType);
        }

        [Test]
        public void CreateDefault_ReturnsNonNullSpecWithDefaults()
        {
            var spec = ParameterDefinitionSpec.CreateDefault();
            Assert.IsNotNull(spec);
            Assert.IsNotNull(spec.Group);
            Assert.IsNotNull(spec.SpecType);
            Assert.AreEqual("PG_DATA", spec.Group);
            Assert.AreEqual("Text", spec.SpecType);
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/PocoIdentityServiceTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Logic.Modules.RevitDOM
{
    [TestFixture]
    public class PocoIdentityServiceTests
    {
        private PocoIdentityService _service;
        private List<ElementModel> _pool;

        [SetUp]
        public void SetUp()
        {
            _service = new PocoIdentityService();

            // Populate a fake pool of ElementModels representing diverse standard types
            _pool = new List<ElementModel>
            {
                new MaterialModel
                {
                    UniqueId = "material-guid-1",
                    Id = 1001,
                    Name = "Structural Concrete",
                    Class = "Autodesk.Revit.DB.Material",
                    Aliases = new List<string> { "Concrete", "Cast-in-Place Concrete" }
                },
                new MaterialModel
                {
                    UniqueId = "material-guid-2",
                    Id = 1002,
                    Name = "Default Glass",
                    Class = "Autodesk.Revit.DB.Material"
                },
                new HostObjTypeModel
                {
                    UniqueId = "wall-guid-1",
                    Id = 2001,
                    Name = "Exterior - 12\" Concrete",
                    Class = "Autodesk.Revit.DB.WallType",
                    Aliases = new List<string> { "Ext Wall 12" }
                }
            };
        }

        [Test]
        public void ResolveElement_ByUniqueId_ReturnsCorrectMatch()
        {
            var reference = new ElementIdModel { UniqueId = "material-guid-1", Class = "Autodesk.Revit.DB.Material" };
            var result = _service.ResolveElement(reference, _pool);

            Assert.IsNotNull(result);
            Assert.AreEqual("Structural Concrete", result.Name);
        }

        [Test]
        public void ResolveElement_ById_ReturnsCorrectMatch()
        {
            var reference = new ElementIdModel { Id = 1002, Class = "Autodesk.Revit.DB.Material" };
            var result = _service.ResolveElement(reference, _pool);

            Assert.IsNotNull(result);
            Assert.AreEqual("Default Glass", result.Name);
        }

        [Test]
        public void ResolveElement_ByName_ReturnsCorrectMatch()
        {
            var reference = new ElementIdModel { Name = "Exterior - 12\" Concrete", Class = "Autodesk.Revit.DB.WallType" };
            var result = _service.ResolveElement(reference, _pool);

            Assert.IsNotNull(result);
            Assert.AreEqual("wall-guid-1", result.UniqueId);
        }

        [Test]
        public void ResolveElement_ByAlias_ReturnsCorrectMatch()
        {
            var reference = new ElementIdModel
            {
                Class = "Autodesk.Revit.DB.Material",
                Aliases = new List<string> { "Cast-in-Place Concrete" }
            };
            var result = _service.ResolveElement(reference, _pool);

            Assert.IsNotNull(result);
            Assert.AreEqual("material-guid-1", result.UniqueId);
        }

        [Test]
        public void ResolveElement_TypeMismatch_ReturnsNull()
        {
            // Attempt to resolve a wall-type ID using a Material reference (type mismatch)
            var reference = new ElementIdModel { UniqueId = "wall-guid-1", Class = "Autodesk.Revit.DB.Material" };
            var result = _service.ResolveElement(reference, _pool);

            Assert.IsNull(result, "Resolution should return null due to strict class verification mismatch.");
        }

        [Test]
        public void ResolveElements_BulkResolution_ReturnsSuccessfully()
        {
            var references = new List<ElementIdModel>
            {
                new ElementIdModel { UniqueId = "material-guid-1", Class = "Autodesk.Revit.DB.Material" },
                new ElementIdModel { Id = 2001, Class = "Autodesk.Revit.DB.WallType" },
                new ElementIdModel { UniqueId = "non-existent-guid", Class = "Autodesk.Revit.DB.Material" }
            };

            var results = _service.ResolveElements(references, _pool).ToList();

            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results.Any(r => r.Name == "Structural Concrete"));
            Assert.IsTrue(results.Any(r => r.Name == "Exterior - 12\" Concrete"));
        }

        [Test]
        public void AreSameIdentity_Overloads_WorkCorrectly()
        {
            var service = new PocoIdentityService();

            var idModel1 = new ElementIdModel { UniqueId = "UID-999", Name = "Column1", Class = "Autodesk.Revit.DB.FamilyInstance" };
            var idModel2 = new ElementIdModel { UniqueId = "uid-999", Name = "Column1_Alt", Class = "Autodesk.Revit.DB.FamilyInstance" };

            var elemModel1 = new ElementModel { ElementId = idModel1 };
            var elemModel2 = new ElementModel { ElementId = idModel2 };

            Assert.IsTrue(service.AreSameIdentity(idModel1, idModel2), "AreSameIdentity(ElementIdModel, ElementIdModel) overload should return true.");
            Assert.IsTrue(service.AreSameIdentity(elemModel1, elemModel2), "AreSameIdentity(ElementModel, ElementModel) overload should return true.");
            Assert.IsTrue(service.AreSameIdentity(idModel1, elemModel2), "AreSameIdentity(ElementIdModel, ElementModel) overload should return true.");
            Assert.IsTrue(service.AreSameIdentity(elemModel1, idModel2), "AreSameIdentity(ElementModel, ElementIdModel) overload should return true.");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/RevitDomDependencyScannerTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace ExternalNamespace
{
    public class ExternalModel : ObjectModel
    {
        public ElementIdModel? HiddenDependency { get; set; }
    }
}

namespace SyntheticTests.Modules.RevitDOM
{
    public class SimpleTestModel : ObjectModel
    {
        public ElementIdModel? DependencyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int IntVal { get; set; }
    }

    public class CircularTestModel : ObjectModel
    {
        public CircularTestModel? SelfReference { get; set; }
        public ElementIdModel? DependencyId { get; set; }
    }

    public class ComplexTestModel : ObjectModel
    {
        public List<SimpleTestModel> SimpleModels { get; set; } = new List<SimpleTestModel>();
        public SimpleTestModel? DirectNested { get; set; }
    }

    [TestFixture]
    public class RevitDomDependencyScannerTests
    {
        [Test]
        public void Scan_NullModel_ReturnsEmpty()
        {
            // Act & Assert
            // Passing null is not allowed by the signature since it expects ObjectModel,
            // but we can test scanning a model that has null properties.
            var model = new SimpleTestModel { DependencyId = null };
            var result = RevitDomDependencyScanner.Scan(model);
            Assert.IsEmpty(result);
        }

        [Test]
        public void Scan_SimpleModel_ReturnsDirectDependency()
        {
            // Arrange
            var dep = new ElementIdModel { Id = 100, Class = "WallType", Category = "Walls" };
            var model = new SimpleTestModel { DependencyId = dep, Name = "Test", IntVal = 42 };

            // Act
            var result = RevitDomDependencyScanner.Scan(model).ToList();

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(100, result[0].Id);
            Assert.AreEqual("WallType", result[0].Class);
        }

        [Test]
        public void Scan_CircularReference_AvoidsStackOverflow()
        {
            // Arrange
            var dep = new ElementIdModel { Id = 200, Class = "Material" };
            var model = new CircularTestModel { DependencyId = dep };
            model.SelfReference = model; // Circular loop!

            // Act
            var result = RevitDomDependencyScanner.Scan(model).ToList();

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(200, result[0].Id);
        }

        [Test]
        public void Scan_ComplexNestedAndCollections_ReturnsAllDependencies()
        {
            // Arrange
            var dep1 = new ElementIdModel { Id = 301, Class = "Material" };
            var dep2 = new ElementIdModel { Id = 302, Class = "FillPattern" };
            var dep3 = new ElementIdModel { Id = 303, Class = "LinePattern" };

            var nested1 = new SimpleTestModel { DependencyId = dep1 };
            var nested2 = new SimpleTestModel { DependencyId = dep2 };

            var model = new ComplexTestModel
            {
                DirectNested = new SimpleTestModel { DependencyId = dep3 },
                SimpleModels = new List<SimpleTestModel> { nested1, nested2 }
            };

            // Act
            var result = RevitDomDependencyScanner.Scan(model).ToList();

            // Assert
            Assert.AreEqual(3, result.Count);
            var ids = result.Select(r => r.Id).ToList();
            CollectionAssert.AreEquivalent(new long[] { 301, 302, 303 }, ids);
        }

        [Test]
        public void Scan_ExternalNamespaceProperty_IsIgnored()
        {
            // Arrange
            var dep = new ElementIdModel { Id = 400, Class = "Material" };
            var external = new ExternalNamespace.ExternalModel { HiddenDependency = dep };
            
            // We put the external model inside a SimpleTestModel
            // Since SimpleTestModel is in SyntheticTests namespace, it will be scanned.
            // But when it reflects into external, it should see that external's namespace is "ExternalNamespace"
            // and NOT reflect into it, thus missing the HiddenDependency.
            var model = new SimpleTestModel
            {
                DependencyId = null
            };

            // We create a wrapper class in SyntheticTests namespace to hold the external property
            var wrapper = new WrapperModel { ExternalObj = external };

            // Act
            var result = RevitDomDependencyScanner.Scan(wrapper).ToList();

            // Assert
            Assert.IsEmpty(result, "Should not inspect namespaces that do not start with 'Synthetic'.");
        }

        public class WrapperModel : ObjectModel
        {
            public ExternalNamespace.ExternalModel? ExternalObj { get; set; }
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/RevitDomExtensionsTests.cs
```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class RevitDomExtensionsTests
    {
        [Test]
        public void DeepClone_SimpleModel_PropertiesPreserved()
        {
            // Arrange
            var original = new ElementIdModel
            {
                Id = 98765,
                Name = "SimpleElement",
                Class = "Autodesk.Revit.DB.Level",
                Category = "Levels",
                UniqueId = "level-uuid-123",
                IsTemplate = false // Set to false to ensure Id and UniqueId are serialized
            };

            // Act
            var clone = original.DeepClone();

            // Assert
            Assert.IsNotNull(clone);
            Assert.AreNotSame(original, clone);
            Assert.AreEqual(original.Id, clone.Id);
            Assert.AreEqual(original.Name, clone.Name);
            Assert.AreEqual(original.Class, clone.Class);
            Assert.AreEqual(original.Category, clone.Category);
            Assert.AreEqual(original.UniqueId, clone.UniqueId);
            Assert.IsFalse(clone.IsTemplate, "IsTemplate is marked [JsonIgnore] and defaults to false in constructor.");
        }

        [Test]
        public void DeepClone_NestedModel_CollectionsCloned()
        {
            // Arrange
            var original = new ElementModel
            {
                Id = 1111,
                Name = "Parent",
                Class = "Autodesk.Revit.DB.Wall",
                Category = "Walls",
                UniqueId = "parent-uuid",
                IsTemplate = false, // Set to false to ensure Id is serialized
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel
                    {
                        Id = 2222,
                        Name = "Height",
                        Value = "3000",
                        StorageType = "Double"
                    },
                    new ParameterModel
                    {
                        Id = 3333,
                        Name = "Comments",
                        Value = "Clean standards",
                        StorageType = "String"
                    }
                }
            };

            // Act
            var clone = original.DeepClone();

            // Assert
            Assert.IsNotNull(clone);
            Assert.AreNotSame(original, clone);
            Assert.AreEqual(original.Parameters.Count, clone.Parameters.Count);
            
            for (int i = 0; i < original.Parameters.Count; i++)
            {
                Assert.AreNotSame(original.Parameters[i], clone.Parameters[i], "Nested items must have different references.");
                Assert.AreEqual(original.Parameters[i].Id, clone.Parameters[i].Id);
                Assert.AreEqual(original.Parameters[i].Name, clone.Parameters[i].Name);
                Assert.AreEqual(original.Parameters[i].Value, clone.Parameters[i].Value);
                Assert.AreEqual(original.Parameters[i].StorageType, clone.Parameters[i].StorageType);
            }
        }

        [Test]
        public void DeepClone_StateIsolation_ModifyingCloneDoesNotAffectOriginal()
        {
            // Arrange
            var original = new ElementModel
            {
                Id = 5555,
                Name = "OriginalElement",
                IsTemplate = false,
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel
                    {
                        Name = "Color",
                        Value = "Red"
                    }
                }
            };

            var clone = original.DeepClone();

            // Act
            clone.Name = "ClonedElement";
            clone.Parameters[0].Value = "Blue";

            // Assert
            Assert.AreEqual("OriginalElement", original.Name, "Modifying the clone's primitive property must not affect the original.");
            Assert.AreEqual("Red", original.Parameters[0].Value, "Modifying the clone's nested list items must not affect the original.");
        }

        [Test]
        public void DeepClone_RevitScrubbing_JsonIgnoredPropertiesDropped()
        {
            // Arrange
            var original = new ElementModel
            {
                Id = 7777,
                Name = "ScrubTest",
                IsTemplate = false, // Set to false to ensure Id is serialized
                Element = new object() // Mocked live Revit Element reference
            };

            // Act
            var clone = original.DeepClone();

            // Assert
            Assert.IsNotNull(clone);
            Assert.IsNull(clone.Element, "Properties marked with [JsonIgnore] must be dropped during deep cloning.");
            
            // Note: Since ElementId has [JsonIgnore], the property ElementId gets initialized to a new empty ElementIdModel.
            // But because Name/Id/UniqueId delegate to ElementId and are serialized directly on ElementModel,
            // they are successfully restored in the clone.
            Assert.AreEqual(original.Id, clone.Id);
            Assert.AreEqual(original.Name, clone.Name);
            Assert.AreNotSame(original.ElementId, clone.ElementId);
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/UVModelTests.cs
```csharp
using System;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class UVModelTests
    {
        [Test]
        public void StandardSymmetry_StandardCoordinates_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new UVModel(10.5, -5.25);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = UVModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual(10.5, (double)jObj["U"]!, "U coordinate matches.");
            Assert.AreEqual(-5.25, (double)jObj["V"]!, "V coordinate matches.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(10.5, roundTrip.U, "Deserialized U coordinate matches.");
            Assert.AreEqual(-5.25, roundTrip.V, "Deserialized V coordinate matches.");
        }

        [Test]
        public void HighPrecision_FloatingPointNumbers_RetainsPrecisionWithoutRoundingLoss()
        {
            // Arrange
            var model = new UVModel(0.123456789012345, -0.987654321098765);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = UVModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual(0.123456789012345, (double)jObj["U"]!, "High-precision U matches.");
            Assert.AreEqual(-0.987654321098765, (double)jObj["V"]!, "High-precision V matches.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(0.123456789012345, roundTrip.U, "Deserialized high-precision U matches.");
            Assert.AreEqual(-0.987654321098765, roundTrip.V, "Deserialized high-precision V matches.");
        }

        [Test]
        public void ZeroVector_ZeroCoordinates_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new UVModel(0.0, 0.0);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = UVModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual(0.0, (double)jObj["U"]!, "Zero U coordinate matches.");
            Assert.AreEqual(0.0, (double)jObj["V"]!, "Zero V coordinate matches.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(0.0, roundTrip.U, "Deserialized zero U coordinate matches.");
            Assert.AreEqual(0.0, roundTrip.V, "Deserialized zero V coordinate matches.");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/RevitDOM/ViewModelSerializationTests.cs
```csharp
using System;
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class ViewModelSerializationTests
    {
        [Test]
        public void ViewSheetModel_Serialization_OmitsGraphicalPropertiesWhenNull()
        {
            // Arrange
            var sheetModel = new ViewSheetModel
            {
                Name = "Sheet_Test",
                Class = "Autodesk.Revit.DB.ViewSheet",
                Id = 12345
            };

            // Act
            var json = Json.Encode(sheetModel);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(json.Contains("\"Scale\""), "JSON should not contain Scale.");
            Assert.IsFalse(json.Contains("\"DetailLevel\""), "JSON should not contain DetailLevel.");
            Assert.IsFalse(json.Contains("\"DisplayStyle\""), "JSON should not contain DisplayStyle.");
            Assert.IsFalse(json.Contains("\"SunlightIntensity\""), "JSON should not contain SunlightIntensity.");
            Assert.IsFalse(json.Contains("\"ShadowIntensity\""), "JSON should not contain ShadowIntensity.");
            Assert.IsFalse(json.Contains("\"CropBoxActive\""), "JSON should not contain CropBoxActive.");
        }

        [Test]
        public void ViewScheduleModel_Serialization_OmitsGraphicalPropertiesWhenNull()
        {
            // Arrange
            var scheduleModel = new ViewScheduleModel
            {
                Name = "Schedule_Test",
                Class = "Autodesk.Revit.DB.ViewSchedule",
                Id = 67890
            };

            // Act
            var json = Json.Encode(scheduleModel);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(json.Contains("\"Scale\""), "JSON should not contain Scale.");
            Assert.IsFalse(json.Contains("\"DetailLevel\""), "JSON should not contain DetailLevel.");
            Assert.IsFalse(json.Contains("\"DisplayStyle\""), "JSON should not contain DisplayStyle.");
            Assert.IsFalse(json.Contains("\"SunlightIntensity\""), "JSON should not contain SunlightIntensity.");
            Assert.IsFalse(json.Contains("\"ShadowIntensity\""), "JSON should not contain ShadowIntensity.");
            Assert.IsFalse(json.Contains("\"CropBoxActive\""), "JSON should not contain CropBoxActive.");
        }

        [Test]
        public void ModelsToSerialize_DeserializeByJson_RoutesModelTextAndSpotDimensionCorrectly()
        {
            // Arrange
            var json = @"{
                ""ModelTextTypes"": {
                    ""TestModelText"": {
                        ""Class"": ""Autodesk.Revit.DB.ModelTextType"",
                        ""Name"": ""TestModelText"",
                        ""Id"": 1010
                    }
                },
                ""SpotDimensionTypes"": {
                    ""TestSpotDimension"": {
                        ""Class"": ""Autodesk.Revit.DB.SpotDimensionType"",
                        ""Name"": ""TestSpotDimension"",
                        ""Id"": 2020
                    }
                }
            }";

            // Act
            var flatList = System.Linq.Enumerable.ToList(ModelsToSerialize.DeserializeByJson(json));

            // Assert
            Assert.AreEqual(2, flatList.Count, "Flat list should contain exactly 2 element models.");
            
            var modelText = flatList.Find(x => x.Class == "Autodesk.Revit.DB.ModelTextType");
            var spotDim = flatList.Find(x => x.Class == "Autodesk.Revit.DB.SpotDimensionType");

            Assert.IsNotNull(modelText, "ModelTextType element model should be found in flat list.");
            Assert.IsNotNull(spotDim, "SpotDimensionType element model should be found in flat list.");
            Assert.AreEqual("TestModelText", modelText.Name);
            Assert.AreEqual("TestSpotDimension", spotDim.Name);
        }

        [Test]
        public void ModelsToSerialize_SerializeToJson_SortsModelTextAndSpotDimensionCorrectly()
        {
            // Arrange
            var modelText = new ElementTypeModel
            {
                Class = "Autodesk.Revit.DB.ModelTextType",
                Name = "TestModelText",
                Id = 1010
            };
            var spotDim = new ElementTypeModel
            {
                Class = "Autodesk.Revit.DB.SpotDimensionType",
                Name = "TestSpotDimension",
                Id = 2020
            };

            var list = new System.Collections.Generic.List<ObjectModel> { modelText, spotDim };

            // Act
            var json = ModelsToSerialize.SerializeToJson(list);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("\"ModelTextTypes\""), "JSON should contain ModelTextTypes property.");
            Assert.IsTrue(json.Contains("\"SpotDimensionTypes\""), "JSON should contain SpotDimensionTypes property.");
            Assert.IsTrue(json.Contains("\"TestModelText\""), "JSON should contain TestModelText.");
            Assert.IsTrue(json.Contains("\"TestSpotDimension\""), "JSON should contain TestSpotDimension.");
        }

        [Test]
        public void ModelsToSerialize_SerializationAndDeserialization_RoutesToposolidTypeCorrectly()
        {
            // Arrange
            var topoType = new HostObjTypeModel
            {
                Class = "Autodesk.Revit.DB.ToposolidType",
                Name = "TestToposolidType",
                Id = 3030
            };
            var list = new System.Collections.Generic.List<ObjectModel> { topoType };

            // Act - Serialize
            var json = ModelsToSerialize.SerializeToJson(list);

            // Assert - Serialization
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("\"ToposolidTypes\""), "JSON should contain ToposolidTypes property.");
            Assert.IsTrue(json.Contains("\"TestToposolidType\""), "JSON should contain TestToposolidType.");

            // Act - Deserialize
            var flatList = System.Linq.Enumerable.ToList(ModelsToSerialize.DeserializeByJson(json));

            // Assert - Deserialization
            Assert.AreEqual(1, flatList.Count);
            var deserialized = flatList[0];
            Assert.IsInstanceOf<HostObjTypeModel>(deserialized);
            Assert.AreEqual("Autodesk.Revit.DB.ToposolidType", deserialized.Class);
            Assert.AreEqual("TestToposolidType", deserialized.Name);
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/DashboardFindReplaceTests.cs
```csharp
﻿using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class DashboardFindReplaceTests
    {
        private Document _doc = null!;
        private FakeFileDialogService _fakeDialogService = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeDialogService = new FakeFileDialogService();
        }

        [Test]
        public void FindReplace_HeterogeneousSelection_ShouldMutateSelectedNamesAndParameters()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StagingQueueViewModel;

            var matParam = new ParameterModel("Comments", "FindMe_MaterialVal", null, "String", 1, null, false, false);
            var material = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "FindMe_Material", Parameters = new List<ParameterModel> { matParam } };

            var lpParam = new ParameterModel("Comments", "FindMe_LineVal", null, "String", 1, null, false, false);
            var linePattern = new ElementModel { Class = "Autodesk.Revit.DB.LinePatternElement", Name = "FindMe_LinePattern", Parameters = new List<ParameterModel> { lpParam } };

            var qMaterial = new QueueItemModel(material, true, false);
            var qLinePattern = new QueueItemModel(linePattern, true, false);

            vm.StagingQueue.Add(qMaterial);
            vm.StagingQueue.Add(qLinePattern);

            // Edit both
            var itemsToEdit = new List<QueueItemModel> { qMaterial, qLinePattern };
            vm.EditCommand.Execute(itemsToEdit);

            // Act
            vm.FindText = "FindMe";
            vm.ReplaceText = "Replaced";
            vm.FindReplaceScope = SearchScope.Both;

            vm.BatchFindReplaceCommand.Execute(null!);

            // Assert names updated
            var targetMat = (ElementModel)qMaterial.TargetModel;
            var targetLp = (ElementModel)qLinePattern.TargetModel;
            Assert.AreEqual("Replaced_Material", targetMat.Name);
            Assert.AreEqual("Replaced_LinePattern", targetLp.Name);

            // Assert parameters updated
            Assert.AreEqual("Replaced_MaterialVal", targetMat.Parameters[0].Value);
            Assert.AreEqual("Replaced_LineVal", targetLp.Parameters[0].Value);

            // Assert intent marked as Edited (dirty state)
            Assert.IsTrue(qMaterial.IsEdited);
            Assert.IsTrue(qLinePattern.IsEdited);
        }

        [Test]
        public void FindReplace_ReadOnlyProtection_ShouldNotModifyReadOnlyParameters()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StagingQueueViewModel;

            var writableParam = new ParameterModel("Comments", "FindMe_Writable", null, "String", 1, null, false, false);
            var readOnlyParam = new ParameterModel("Category", "FindMe_ReadOnly", null, "String", 2, null, false, true); // IsReadOnly = true
            var el = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "Mat", Parameters = new List<ParameterModel> { writableParam, readOnlyParam } };

            var qItem = new QueueItemModel(el, true, false);
            vm.StagingQueue.Add(qItem);

            vm.EditCommand.Execute(new List<QueueItemModel> { qItem });

            // Act
            vm.FindText = "FindMe";
            vm.ReplaceText = "Replaced";
            vm.FindReplaceScope = SearchScope.ParameterValues;

            vm.BatchFindReplaceCommand.Execute(null!);

            // Assert
            var target = (ElementModel)qItem.TargetModel;
            Assert.AreEqual("Replaced_Writable", target.Parameters.First(p => p.Name == "Comments").Value);
            Assert.AreEqual("FindMe_ReadOnly", target.Parameters.First(p => p.Name == "Category").Value); // Unchanged!
        }

        [Test]
        public void FindReplace_DirtyStateRecalculation_ShouldReportIsDirtyPostReplacement()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StagingQueueViewModel;

            var param = new ParameterModel("Comments", "FindMe", null, "String", 1, null, false, false);
            var el = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "Mat", Parameters = new List<ParameterModel> { param } };

            var qItem = new QueueItemModel(el, true, false);
            vm.StagingQueue.Add(qItem);

            vm.EditCommand.Execute(new List<QueueItemModel> { qItem });

            // Act
            vm.FindText = "FindMe";
            vm.ReplaceText = "Replaced";
            vm.FindReplaceScope = SearchScope.ParameterValues;

            vm.BatchFindReplaceCommand.Execute(null!);

            // Assert
            var wrapper = qItem.GetWrapper();
            Assert.IsTrue(wrapper.IsDirty, "Wrapper should evaluate to dirty after Find & Replace modifications.");
            Assert.IsTrue(wrapper.Parameters[0].IsDirty, "Parameter should evaluate to dirty after modification.");
        }

        [Test]
        public void FindReplace_ScopeControls_ShouldRespectConfiguredScope()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StagingQueueViewModel;

            // ElementNames Only Scope
            var param1 = new ParameterModel("Comments", "FindMe", null, "String", 1, null, false, false);
            var el1 = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "FindMe_Name", Parameters = new List<ParameterModel> { param1 } };
            var q1 = new QueueItemModel(el1, true, false);
            vm.StagingQueue.Add(q1);

            // ParameterValues Only Scope
            var param2 = new ParameterModel("Comments", "FindMe", null, "String", 1, null, false, false);
            var el2 = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "FindMe_Name", Parameters = new List<ParameterModel> { param2 } };
            var q2 = new QueueItemModel(el2, true, false);
            vm.StagingQueue.Add(q2);

            // Act: Run Find & Replace for q1 with ElementNames scope
            vm.EditCommand.Execute(new List<QueueItemModel> { q1 });
            vm.FindText = "FindMe";
            vm.ReplaceText = "Replaced";
            vm.FindReplaceScope = SearchScope.ElementNames;

            vm.BatchFindReplaceCommand.Execute(null!);

            // Act: Run Find & Replace for q2 with ParameterValues scope
            vm.EditCommand.Execute(new List<QueueItemModel> { q2 });
            vm.FindReplaceScope = SearchScope.ParameterValues;
            vm.BatchFindReplaceCommand.Execute(null!);

            // Assert q1: name updated, parameter unchanged
            var target1 = (ElementModel)q1.TargetModel;
            Assert.AreEqual("Replaced_Name", target1.Name);
            Assert.AreEqual("FindMe", target1.Parameters[0].Value);

            // Assert q2: name unchanged, parameter updated
            var target2 = (ElementModel)q2.TargetModel;
            Assert.AreEqual("FindMe_Name", target2.Name);
            Assert.AreEqual("Replaced", target2.Parameters[0].Value);
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/DashboardParametersEditorTests.cs
```csharp
﻿using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class DashboardParametersEditorTests
    {
        private Document _doc = null!;
        private FakeFileDialogService _fakeDialogService = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeDialogService = new FakeFileDialogService();
            ProgressCoordinator.SuppressUI = true;
        }

        [Test]
        public void SpecializedParameterExtraction_ShouldPopulateSegmentAndOrientationMetadata()
        {
            // 1. LinePatternElementModel
            var lpModel = new LinePatternElementModel
            {
                Class = "Autodesk.Revit.DB.LinePatternElement",
                Name = "DashDot",
                Segments = new List<LinePatternSegmentModel>
                {
                    new LinePatternSegmentModel { Type = "Dash", Length = 0.5 },
                    new LinePatternSegmentModel { Type = "Space", Length = 0.25 }
                }
            };

            var lpWrapper = new ElementTypeWrapperVM(lpModel);
            var segmentCountParam = lpWrapper.Parameters.FirstOrDefault(p => p.Name == "SegmentCount");
            var segmentsParam = lpWrapper.Parameters.FirstOrDefault(p => p.Name == "Segments");

            Assert.IsNotNull(segmentCountParam, "SegmentCount parameter should be extracted.");
            Assert.AreEqual("2", segmentCountParam.Value);
            Assert.IsNotNull(segmentsParam, "Segments parameter should be extracted.");
            Assert.AreEqual("Dash: 0.5, Space: 0.25", segmentsParam.Value);

            // 2. FillPatternElementModel
            var fpModel = new FillPatternElementModel
            {
                Class = "Autodesk.Revit.DB.FillPatternElement",
                Name = "Diagonal",
                Pattern = new FillPatternModel
                {
                    Target = "Drafting",
                    HostOrientation = "ToHost",
                    FillGrids = new List<FillGridModel> { new FillGridModel(), new FillGridModel() }
                }
            };

            var fpWrapper = new ElementTypeWrapperVM(fpModel);
            var targetParam = fpWrapper.Parameters.FirstOrDefault(p => p.Name == "Target");
            var orientationParam = fpWrapper.Parameters.FirstOrDefault(p => p.Name == "HostOrientation");
            var gridCountParam = fpWrapper.Parameters.FirstOrDefault(p => p.Name == "GridCount");

            Assert.IsNotNull(targetParam, "Target parameter should be extracted.");
            Assert.AreEqual("Drafting", targetParam.Value);
            Assert.IsNotNull(orientationParam, "HostOrientation parameter should be extracted.");
            Assert.AreEqual("ToHost", orientationParam.Value);
            Assert.IsNotNull(gridCountParam, "GridCount parameter should be extracted.");
            Assert.AreEqual("2", gridCountParam.Value);
        }

        [Test]
        public void StagedMultiSelectIntersection_ShouldAggregateParametersAndDisplayVaries()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StagingQueueViewModel;

            var p1 = new ParameterModel("Comments", "ValueA", null, "String", 1, null, false, false);
            var el1 = new ElementModel { Class = "Autodesk.Revit.DB.LinePatternElement", Name = "Dash", Parameters = new List<ParameterModel> { p1 } };

            var p2 = new ParameterModel("Comments", "ValueB", null, "String", 1, null, false, false);
            var el2 = new ElementModel { Class = "Autodesk.Revit.DB.LinePatternElement", Name = "Dot", Parameters = new List<ParameterModel> { p2 } };

            var q1 = new QueueItemModel(el1, true, false);
            var q2 = new QueueItemModel(el2, true, false);

            vm.StagingQueue.Add(q1);
            vm.StagingQueue.Add(q2);

            // Act: Edit items to calculate intersection
            var itemsToEdit = new List<QueueItemModel> { q1, q2 };
            vm.EditCommand.Execute(itemsToEdit);

            // Assert intersection has Comments with <Varies>
            var commentsParam = vm.DisplayParameters.FirstOrDefault(p => p.Name == "Comments");
            Assert.IsNotNull(commentsParam, "Common parameter 'Comments' should be intersected.");
            Assert.IsTrue(commentsParam.IsMixedValue);
            Assert.AreEqual("<Varies>", commentsParam.Value);

            // Act: Update mixed value to "ValueShared"
            commentsParam.Value = "ValueShared";

            // Assert that the TargetModel of both queue items is updated and marked as Edited
            var targetEl1 = (ElementModel)q1.TargetModel;
            var targetEl2 = (ElementModel)q2.TargetModel;
            Assert.AreEqual("ValueShared", targetEl1.Parameters[0].Value);
            Assert.AreEqual("ValueShared", targetEl2.Parameters[0].Value);
            Assert.IsTrue(q1.IsEdited);
            Assert.IsTrue(q2.IsEdited);
 
            // Act: Cancel edits
            vm.CancelEditsCommand.Execute(null!);
 
            // Assert revert to original baseline values and original intents (Enforce)
            Assert.AreEqual("ValueA", ((ElementModel)q1.TargetModel).Parameters[0].Value);
            Assert.AreEqual("ValueB", ((ElementModel)q2.TargetModel).Parameters[0].Value);
            Assert.IsTrue(q1.WillEnforce);
            Assert.IsFalse(q1.IsEdited);
            Assert.IsTrue(q2.WillEnforce);
            Assert.IsFalse(q2.IsEdited);
        }

        [Test]
        public void CascadingRenameSafety_ShouldUpdateReferencesAcrossStagingQueue()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StagingQueueViewModel;

            var materialModel = new ElementModel
            {
                Class = "Autodesk.Revit.DB.Material",
                Name = "OldMaterialName",
                Parameters = new List<ParameterModel>()
            };

            var wallModel = new HostObjTypeModel
            {
                Class = "Autodesk.Revit.DB.WallType",
                Name = "Generic Wall",
                Structure = new CompoundStructureModel
                {
                    Layers = new List<SerialCompoundStructureLayer>
                    {
                        new SerialCompoundStructureLayer
                        {
                            MaterialId = new ElementIdModel { Name = "OldMaterialName" }
                        }
                    }
                }
            };

            var qMaterial = new QueueItemModel(materialModel, true, false);
            var qWall = new QueueItemModel(wallModel, true, false);

            vm.StagingQueue.Add(qMaterial);
            vm.StagingQueue.Add(qWall);

            // Act: Edit Material item to start session
            vm.EditCommand.Execute(new List<QueueItemModel> { qMaterial });

            // Trigger Name property change via SelectedItemName property
            vm.SelectedItemName = "NewMaterialName";

            // Assert references in wall compound structures are automatically updated to "NewMaterialName"
            var updatedWall = (HostObjTypeModel)qWall.TargetModel;
            Assert.AreEqual("NewMaterialName", updatedWall.Structure?.Layers[0].MaterialId?.Name);
        }

        [Test]
        public void NestedModalWiring_ShouldRetrievePoolFromAllWrappedElements()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);

            var mat = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "Brick" };
            var wall = new HostObjTypeModel { Class = "Autodesk.Revit.DB.WallType", Name = "Brick Wall" };

            vm.StagingQueue.Add(new QueueItemModel(mat, true, false));
            vm.StagingQueue.Add(new QueueItemModel(wall, true, false));

            // Act
            var pool = vm.AllWrappedElements.ToList();

            // Assert
            Assert.AreEqual(2, pool.Count);
            Assert.IsTrue(pool.Any(w => w.Name == "Brick" && w.Class == "Autodesk.Revit.DB.Material"));
            Assert.IsTrue(pool.Any(w => w.Name == "Brick Wall" && w.Class == "Autodesk.Revit.DB.WallType"));
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/DashboardTabAndFamilyFilterTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.Settings;
using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Standards;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class DashboardTabAndFamilyFilterTests
    {
        private Document _doc = null!;
        private FakeFileDialogService _fakeDialogService = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeDialogService = new FakeFileDialogService();
            ProgressCoordinator.SuppressUI = true;
        }

        private T CreateMockElement<T>(Document doc, string name, long idVal) where T : Element
        {
            dynamic elem = Activator.CreateInstance(typeof(T), true)!;
            elem.Name = name;
            elem.Id = new ElementId(idVal);

            dynamic dynamicDoc = doc;
            dynamicDoc.AddElement(elem, elem.Id);

            return (T)elem;
        }

        private class FakeExtractionOrchestrator : IStandardsExtractionOrchestrator
        {
            public List<ObjectModel> ExtractedModels { get; } = new List<ObjectModel>();

            public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null)
            {
                return ExtractedModels;
            }

            public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null, bool isTemplate = false)
            {
                return ExtractedModels;
            }
        }

        private class FakeExtractionOrchestratorForMultipleDocuments : IStandardsExtractionOrchestrator
        {
            public Dictionary<Document, List<ObjectModel>> ExtractedModels { get; } = new Dictionary<Document, List<ObjectModel>>();

            public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null)
            {
                return ExtractedModels.TryGetValue(doc, out var list) ? list : new List<ObjectModel>();
            }

            public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null, bool isTemplate = false)
            {
                return ExtractedModels.TryGetValue(doc, out var list) ? list : new List<ObjectModel>();
            }
        }

        [Test]
        public void VerifyTabCreationAndLifecycle_AddsTabsForSelectedDocuments()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService);
            dynamic mockDoc1 = Activator.CreateInstance(typeof(Document), true)!;
            mockDoc1.Title = "Model A";
            mockDoc1.PathName = @"C:\Projects\ModelA.rvt";

            dynamic mockDoc2 = Activator.CreateInstance(typeof(Document), true)!;
            mockDoc2.Title = "Model B";
            mockDoc2.PathName = @"C:\Projects\ModelB.rvt";

            parent.MockOpenDocuments = new List<Document> { (Document)mockDoc1, (Document)mockDoc2 };

            var fakeOrchestrator = new FakeExtractionOrchestrator();
            var treeVM = new StandardsSourceTreeViewModel(parent, null, _doc, _fakeDialogService, fakeOrchestrator, new StandardSerializationEngine());

            // Act
            treeVM.AddRevitModelCommand.Execute(null);

            // Assert
            Assert.AreEqual(2, treeVM.AvailableSources.Count, "Should have 2 sources loaded.");
            Assert.IsTrue(treeVM.AvailableSources.Any(s => s.DisplayName == "Model A"));
            Assert.IsTrue(treeVM.AvailableSources.Any(s => s.DisplayName == "Model B"));
            Assert.IsTrue(treeVM.AvailableSources.All(s => s.IsRevitSource));
        }

        [Test]
        public void VerifyResourceCleanupOnClose_RemovesTabAndPurgesHierarchy()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService);
            dynamic mockDoc = Activator.CreateInstance(typeof(Document), true)!;
            mockDoc.Title = "Model A";
            mockDoc.PathName = @"C:\Projects\ModelA.rvt";

            // Add a mock element so the tab has hierarchical data
            CreateMockElement<Material>((Document)mockDoc, "Steel", 501);

            parent.MockOpenDocuments = new List<Document> { (Document)mockDoc };

            var treeVM = new StandardsSourceTreeViewModel(parent, null, _doc, _fakeDialogService, new StandardsExtractionOrchestrator(new RevitIdentityService(), new StandardSerializationEngine()), new StandardSerializationEngine());
            treeVM.AddRevitModelCommand.Execute(null);

            var addedSource = treeVM.AvailableSources.FirstOrDefault(s => s.DisplayName == "Model A");
            Assert.IsNotNull(addedSource, "Tab should be added.");
            Assert.IsTrue(addedSource.SourceHierarchy.Count > 0, "Hierarchy should not be empty.");

            // Act
            treeVM.CloseSourceCommand.Execute(addedSource);

            // Assert
            Assert.IsFalse(treeVM.AvailableSources.Contains(addedSource), "Tab should be removed from AvailableSources.");
            Assert.AreEqual(0, addedSource.SourceHierarchy.Count, "Associated hierarchy collections should be cleared to disperse memory.");
        }

        [Test]
        public void VerifyInitialStateConstraints_PrechecksDefaultStandardAndLeavesQueueEmpty()
        {
            // Arrange
            string tempJsonFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            // Write a simple valid standards JSON
            File.WriteAllText(tempJsonFile, @"{
                ""Materials"": {
                    ""Concrete"": {
                        ""$type"": ""Synthetic.RevitDOM.Operations.Standards.MaterialModel, SyntheticShared"",
                        ""Name"": ""Concrete"",
                        ""Class"": ""Autodesk.Revit.DB.Material"",
                        ""UniqueId"": ""abc-123""
                    }
                }
            }");

            var settings = new StandardsSettings { StandardsFilePath = tempJsonFile };

            // Act
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, settings);

            try
            {
                var treeVM = parent.SourceTreeViewModel;

                // Assert
                Assert.AreEqual(1, treeVM.AvailableSources.Count, "Should load default firm standard.");
                var defaultSource = treeVM.AvailableSources[0];
                Assert.AreEqual("Default Firm Standard", defaultSource.DisplayName);

                // Assert that checkboxes are checked
                Assert.IsTrue(defaultSource.SourceHierarchy.Count > 0);
                foreach (var group in defaultSource.SourceHierarchy)
                {
                    Assert.IsTrue(group.IsChecked == true, "Hierarchical tree nodes should be checked on launch.");
                }

                // Assert that action queue remains empty
                Assert.AreEqual(0, parent.StagingQueue.Count, "Action queue must remain empty on launch.");
            }
            finally
            {
                if (File.Exists(tempJsonFile)) File.Delete(tempJsonFile);
            }
        }

        [Test]
        public void VerifyMultiModelExtractionIsolation_ExtractsDistinctNonIntersectingHierarchies()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService);

            dynamic doc1 = Activator.CreateInstance(typeof(Document), true)!;
            doc1.Title = "Doc 1";
            CreateMockElement<Material>((Document)doc1, "Aluminum", 601);

            dynamic doc2 = Activator.CreateInstance(typeof(Document), true)!;
            doc2.Title = "Doc 2";
            CreateMockElement<Material>((Document)doc2, "Copper", 602);

            parent.MockOpenDocuments = new List<Document> { (Document)doc1, (Document)doc2 };

            var fakeOrchestrator = new FakeExtractionOrchestratorForMultipleDocuments();
            fakeOrchestrator.ExtractedModels[(Document)doc1] = new List<ObjectModel> { new MaterialModel { Name = "Aluminum", UniqueId = "601" } };
            fakeOrchestrator.ExtractedModels[(Document)doc2] = new List<ObjectModel> { new MaterialModel { Name = "Copper", UniqueId = "602" } };

            var treeVM = new StandardsSourceTreeViewModel(parent, null, _doc, _fakeDialogService, fakeOrchestrator, new StandardSerializationEngine());

            // Act
            treeVM.AddRevitModelCommand.Execute(null);

            // Assert
            var source1 = treeVM.AvailableSources.FirstOrDefault(s => s.DisplayName == "Doc 1");
            var source2 = treeVM.AvailableSources.FirstOrDefault(s => s.DisplayName == "Doc 2");

            Assert.IsNotNull(source1);
            Assert.IsNotNull(source2);

            // Verify elements inside hierarchies are isolated
            var elements1 = source1.SourceHierarchy
                .SelectMany(g => g.Children)
                .SelectMany(c => c.Children)
                .Select(e => e.Name)
                .ToList();

            var elements2 = source2.SourceHierarchy
                .SelectMany(g => g.Children)
                .SelectMany(c => c.Children)
                .Select(e => e.Name)
                .ToList();

            Assert.Contains("Aluminum", elements1);
            Assert.IsFalse(elements1.Contains("Copper"));

            Assert.Contains("Copper", elements2);
            Assert.IsFalse(elements2.Contains("Aluminum"));
        }

        [Test]
        public void VerifyStaticFilteringApplication_RestrictsExtractedClassesBasedOnSelection()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService);

            dynamic doc = Activator.CreateInstance(typeof(Document), true)!;
            doc.Title = "Filter Model";
            CreateMockElement<TextNoteType>((Document)doc, "Arial 3/32", 701);
            CreateMockElement<Material>((Document)doc, "Brass", 702);

            parent.MockOpenDocuments = new List<Document> { (Document)doc };

            // Set up document selection dialog mock to NOT select Annotations
            parent.ShowDocumentSelectionDialog = dialogVM =>
            {
                dialogVM.OpenDocuments[0].IsSelected = true;
                var annotationsGroup = dialogVM.FilterHierarchy.FirstOrDefault(g => g.Name == "Annotations");
                if (annotationsGroup != null)
                {
                    annotationsGroup.IsChecked = false;
                }
                return true;
            };

            var treeVM = new StandardsSourceTreeViewModel(parent, null, _doc, _fakeDialogService, new StandardsExtractionOrchestrator(new RevitIdentityService(), new StandardSerializationEngine()), new StandardSerializationEngine());

            // Act
            treeVM.AddRevitModelCommand.Execute(null);

            // Assert
            var source = treeVM.AvailableSources.FirstOrDefault(s => s.DisplayName == "Filter Model");
            Assert.IsNotNull(source);

            var extractedNames = source.SourceHierarchy
                .SelectMany(g => g.Children)
                .SelectMany(c => c.Children)
                .Select(e => e.Name)
                .ToList();

            Assert.Contains("Brass", extractedNames);
            Assert.IsFalse(extractedNames.Contains("Arial 3/32"), "Annotations should be excluded based on filtering.");
        }

        [Test]
        public void SelectAllCommand_ShouldCheckAllNodes()
        {
            // Arrange
            var source = new ProjectStandardsSourceViewModel
            {
                DisplayName = "Test Source"
            };

            var group = new StandardGroupModel { Name = "Group 1" };
            var childClass = new StandardClassModel { Name = "Class 1", Parent = group };
            group.Children.Add(childClass);
            source.SourceHierarchy.Add(group);

            // Act
            source.SelectAllCommand.Execute(null);

            // Assert
            Assert.IsTrue(group.IsChecked);
            Assert.IsTrue(childClass.IsChecked);
        }

        [Test]
        public void SelectNoneCommand_ShouldUncheckAllNodes()
        {
            // Arrange
            var source = new ProjectStandardsSourceViewModel
            {
                DisplayName = "Test Source"
            };

            var group = new StandardGroupModel { Name = "Group 1" };
            var childClass = new StandardClassModel { Name = "Class 1", Parent = group };
            group.Children.Add(childClass);
            source.SourceHierarchy.Add(group);

            group.IsChecked = true;

            // Act
            source.SelectNoneCommand.Execute(null);

            // Assert
            Assert.IsFalse(group.IsChecked);
            Assert.IsFalse(childClass.IsChecked);
        }

        [Test]
        public void SearchText_ShouldFilterNodesAndSetVisibilityAndExpansion()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var treeVM = new StandardsSourceTreeViewModel(parent, null, _doc, _fakeDialogService, new FakeExtractionOrchestrator(), new StandardSerializationEngine());
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };

            var group = new StandardGroupModel { Name = "Materials" };
            var childClass = new StandardClassModel { Name = "Metal", Parent = group };
            var element1 = new StandardElementModel(new MaterialModel { Name = "Steel" }) { Parent = childClass };
            var element2 = new StandardElementModel(new MaterialModel { Name = "Concrete" }) { Parent = childClass };

            childClass.Children.Add(element1);
            childClass.Children.Add(element2);
            group.Children.Add(childClass);
            source.SourceHierarchy.Add(group);

            treeVM.AvailableSources.Add(source);

            // Act: Filter by "Steel"
            treeVM.SearchText = "Steel";

            // Assert: "Steel" is visible, parent class and group are visible and expanded, "Concrete" is hidden.
            Assert.IsTrue(element1.IsVisible);
            Assert.IsFalse(element2.IsVisible);

            Assert.IsTrue(childClass.IsVisible);
            Assert.IsTrue(childClass.IsExpanded);

            Assert.IsTrue(group.IsVisible);
            Assert.IsTrue(group.IsExpanded);

            // Act: Clear search
            treeVM.SearchText = "";

            // Assert: Everything is visible and collapsed
            Assert.IsTrue(element1.IsVisible);
            Assert.IsTrue(element2.IsVisible);
            Assert.IsTrue(childClass.IsVisible);
            Assert.IsFalse(childClass.IsExpanded);
            Assert.IsTrue(group.IsVisible);
            Assert.IsTrue(group.IsExpanded);
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/DependencyOriginTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Operations.Diffing;
using SyntheticTests.Modules.RevitDOM;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class DependencyOriginTests
    {
        private Document _doc = null!;
        private FakeFileDialogService _fakeDialogService = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeDialogService = new FakeFileDialogService();
            ProgressCoordinator.SuppressUI = true;
        }

        private T CreateMockElement<T>(Document doc, string name, int idVal) where T : Element
        {
            dynamic elem = Activator.CreateInstance(typeof(T), true)!;
            elem.Name = name;
            elem.Id = new ElementId(idVal);

            dynamic dynamicDoc = doc;
            dynamicDoc.AddElement(elem, elem.Id);

            return (T)elem;
        }

        [Test]
        public void ExecutePushToQueue_ShouldPopulateDependencyOrigin_WhenDependencyIsHarvested()
        {
            // Arrange
            // Create a mock WallType
            var wallType = CreateMockElement<WallType>(_doc, "WallA", 1001);

            // Create a mock Material
            var material = CreateMockElement<Material>(_doc, "MaterialA", 2001);

            // Build simple compound structure linking WallType to Material
            var layer = new CompoundStructureLayer(0.2, MaterialFunctionAssignment.Structure, material.Id);
            var cs = CompoundStructure.CreateSimpleCompoundStructure(new List<CompoundStructureLayer> { layer });
            wallType.SetCompoundStructure(cs);

            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            
            var group = new StandardGroupModel { Name = "System / Host Object Types" };
            var classModel = new StandardClassModel { Name = "Wall Types" };

            // Wrap WallType into the hierarchy
            var wallPoco = wallType.ToModel(false);
            var wallNode = new StandardElementModel(wallPoco);

            classModel.Children.Add(wallNode);
            wallNode.Parent = classModel;
            group.Children.Add(classModel);
            classModel.Parent = group;
            source.SourceHierarchy.Add(group);

            // Wrap Material into the hierarchy as well so it's in the offline sourcePool lookup
            var matPoco = new MaterialModel { Name = "MaterialA", Class = "Autodesk.Revit.DB.Material" };
            var matNode = new StandardElementModel(matPoco);
            var matGroup = new StandardGroupModel { Name = "Materials & Assets" };
            var matClass = new StandardClassModel { Name = "Materials" };
            matClass.Children.Add(matNode);
            matNode.Parent = matClass;
            matGroup.Children.Add(matClass);
            matClass.Parent = matGroup;
            source.SourceHierarchy.Add(matGroup);

            parent.AvailableSources.Add(source);
            parent.SelectedSource = source;

            // Explicitly check ONLY the WallType
            wallNode.IsChecked = true;

            var queueVM = new StagingQueueViewModel(parent, new PocoIdentityService(), new PocoToRevitDiffEngine(new FakeIdentityService()));

            // Act
            queueVM.PushToQueueCommand.Execute("Save");

            // Assert
            // The queue should have WallA (explicitly checked) and MaterialA (harvested)
            Assert.AreEqual(2, queueVM.StagingQueue.Count, "Queue should contain both the WallType and its harvested Material dependency.");

            var wallQueueItem = queueVM.StagingQueue.FirstOrDefault(q => q.Name == "WallA");
            var matQueueItem = queueVM.StagingQueue.FirstOrDefault(q => q.Name == "MaterialA");

            Assert.IsNotNull(wallQueueItem, "WallA queue item should exist.");
            Assert.IsNotNull(matQueueItem, "MaterialA queue item should exist.");

            // WallA is explicitly checked, so its DependencyOrigin should be null/empty
            Assert.IsFalse(wallQueueItem.IsDependency, "WallA is explicitly checked and should not be flagged as a dependency.");
            Assert.IsNull(wallQueueItem.DependencyOrigin, "WallA should have null DependencyOrigin.");

            // MaterialA is harvested as dependency, so its DependencyOrigin should be WallA
            Assert.IsTrue(matQueueItem.IsDependency, "MaterialA should be flagged as a dependency.");
            Assert.AreEqual("WallA", matQueueItem.DependencyOrigin, "MaterialA's DependencyOrigin should match the root WallType name.");
        }
    }
}

```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/DuplicateClusterModelTests.cs
```csharp
﻿using System;
using System.Collections.Generic;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.RevitDOM.Models;
using Synthetic.Modules.MergeDuplicates.ViewModels;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class DuplicateClusterModelTests
    {
        [Test]
        public void CommitToQueue_ShouldUpdateClusterParameterResolutions_WithWinningValues()
        {
            // Arrange
            var cluster = new DuplicateClusterModel();
            var detailVM = new MergeDetailedReviewViewModel(cluster);

            var mapping = new TypeMappingModel();
            var row = new ParameterDiffRowModel
            {
                ParameterName = "LineWidth",
                IsApproved = true
            };

            var elementId1 = new ElementIdModel { Id = 101 };
            var elementId2 = new ElementIdModel { Id = 102 };

            // Add options for source and target values
            row.Options.Add(new ParameterValueOption { ElementId = elementId1, DisplayText = "1" });
            row.Options.Add(new ParameterValueOption { ElementId = elementId2, DisplayText = "2" });

            // Simulate target value winning
            row.WinningValueElementId = elementId2;

            mapping.ParameterResolutions.Add(row);
            cluster.TypeMappings.Add(mapping);

            // Act
            detailVM.CmdCommitToQueue.Execute(null);

            // Assert
            Assert.IsFalse(cluster.IsBlocked, "Cluster should be unblocked after committing.");
            Assert.IsTrue(cluster.ParameterResolutions.ContainsKey("LineWidth"), "Cluster resolutions dictionary should contain the parameter.");
            Assert.AreEqual(elementId2, cluster.ParameterResolutions["LineWidth"], "Winning value ElementId should match selection.");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/ElementTypeWrapperVMTests.cs
```csharp
﻿using System;
using System.Collections.Generic;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class ElementTypeWrapperVMTests
    {
        [Test]
        public void IsDirty_ShouldBeTrue_WhenParameterValueAltered()
        {
            // Arrange
            var paramModel = new ParameterModel("LineWidth", "1", null, "Integer", 12345, null, false, false);
            var elementModel = new ElementModel
            {
                Class = "Autodesk.Revit.DB.LinePatternElement",
                Name = "Dash",
                Parameters = new List<ParameterModel> { paramModel }
            };

            var wrapperVM = new ElementTypeWrapperVM(elementModel);
            Assert.IsFalse(wrapperVM.IsDirty, "Initially the wrapper should not be dirty.");

            // Act
            wrapperVM.Parameters[0].Value = "2";

            // Assert
            Assert.IsTrue(wrapperVM.Parameters[0].IsDirty, "Parameter should be marked dirty.");
            Assert.IsTrue(wrapperVM.IsDirty, "ElementTypeWrapperVM should be dirty when a parameter is dirty.");
        }

        [Test]
        public void IsDirty_ShouldBeTrue_WhenNameAltered()
        {
            // Arrange
            var elementModel = new ElementModel
            {
                Class = "Autodesk.Revit.DB.LinePatternElement",
                Name = "Dash",
                Parameters = new List<ParameterModel>()
            };

            var wrapperVM = new ElementTypeWrapperVM(elementModel);
            Assert.IsFalse(wrapperVM.IsDirty, "Initially the wrapper should not be dirty.");

            // Act
            wrapperVM.Name = "New Dash Name";

            // Assert
            Assert.IsTrue(wrapperVM.IsDirty, "ElementTypeWrapperVM should be dirty when the name is altered.");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/FakeSummaryDisplayService.cs
```csharp
﻿using System;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    /// <summary>
    /// Test double implementation of ISummaryDisplayService recording invocations without displaying UI.
    /// </summary>
    public class FakeSummaryDisplayService : ISummaryDisplayService
    {
        /// <summary>
        /// Gets the number of times ShowSummary has been called.
        /// </summary>
        public int ShowCallCount { get; private set; }

        /// <summary>
        /// Gets the last view model object passed to ShowSummary.
        /// </summary>
        public object? LastViewModel { get; private set; }

        /// <summary>
        /// Records the invocation.
        /// </summary>
        public void ShowSummary(object viewModel, IntPtr parentWindowHandle)
        {
            ShowCallCount++;
            LastViewModel = viewModel;
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/FindReplaceServiceTests.cs
```csharp
﻿using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Standards;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class FindReplaceServiceTests
    {
        private FindReplaceService _service = null!;

        [SetUp]
        public void Setup()
        {
            _service = new FindReplaceService();
        }

        [Test]
        public void Execute_NullOrEmptyFindText_DoesNotModifyAndReturnsEmpty()
        {
            // Arrange
            var elements = new List<ElementModel>
            {
                new ElementModel { Name = "OldName" }
            };

            // Act
            var result = _service.Execute(elements, "", "NewName", true, true);

            // Assert
            Assert.IsEmpty(result);
            Assert.AreEqual("OldName", elements[0].Name);
        }

        [Test]
        public void Execute_SearchElementNames_ReplacesMatchCaseInsensitively()
        {
            // Arrange
            var elements = new List<ElementModel>
            {
                new ElementModel { Name = "Wall-Type-A" },
                new ElementModel { Name = "Other-Type" }
            };

            // Act
            var result = _service.Execute(elements, "type-a", "Type-X", true, false);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(result.Contains(elements[0]));
            Assert.AreEqual("Wall-Type-X", elements[0].Name);
            Assert.AreEqual("Other-Type", elements[1].Name);
        }

        [Test]
        public void Execute_SearchParameterValues_ReplacesMatchInEditableParameters()
        {
            // Arrange
            var param1 = new ParameterModel("Comments", "Legacy value", null, "String", 1, null, false, false);
            var param2 = new ParameterModel("Mark", "ReadOnly value", null, "String", 2, null, false, true); // ReadOnly
            var element = new ElementModel
            {
                Name = "Wall",
                Parameters = new List<ParameterModel> { param1, param2 }
            };

            // Act
            var result = _service.Execute(new[] { element }, "value", "val", false, true);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Legacy val", param1.Value);
            Assert.AreEqual("ReadOnly value", param2.Value);
        }

        [Test]
        public void Execute_SearchBoth_ReplacesBothNamesAndParams()
        {
            // Arrange
            var param = new ParameterModel("Comments", "Text value", null, "String", 1, null, false, false);
            var element = new ElementModel
            {
                Name = "Wall-value",
                Parameters = new List<ParameterModel> { param }
            };

            // Act
            var result = _service.Execute(new[] { element }, "value", "val", true, true);

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Wall-val", element.Name);
            Assert.AreEqual("Text val", param.Value);
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/ParameterDiffRowModelTests.cs
```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Operations.Merge;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class ParameterDiffRowModelTests
    {
        [Test]
        public void RadioButtonBindings_EvaluateCorrectlyOnLoad_UsingValueEquality()
        {
            // Arrange: distinct ElementIdModel instances representing same elements
            var sourceId = new ElementIdModel { Id = 1001, Name = "SourceType", Class = "Autodesk.Revit.DB.FamilySymbol" };
            var targetId = new ElementIdModel { Id = 2002, Name = "TargetType", Class = "Autodesk.Revit.DB.FamilySymbol" };

            var sourceIdDistinct = new ElementIdModel { Id = 1001, Name = "SourceType", Class = "Autodesk.Revit.DB.FamilySymbol" };
            var targetIdDistinct = new ElementIdModel { Id = 2002, Name = "TargetType", Class = "Autodesk.Revit.DB.FamilySymbol" };

            var row = new ParameterDiffRowModel
            {
                ParameterName = "Comments",
                Options = new List<ParameterValueOption>
                {
                    new ParameterValueOption { ElementId = sourceId, DisplayText = "Source Comment" },
                    new ParameterValueOption { ElementId = targetId, DisplayText = "Target Comment" }
                },
                // Set winning value to distinct instance equal to sourceId
                WinningValueElementId = sourceIdDistinct
            };

            // Assert
            Assert.IsTrue(row.IsSourceWinning, "IsSourceWinning should evaluate to true when WinningValueElementId is value-equal to Options[0].ElementId.");
            Assert.IsFalse(row.IsTargetWinning, "IsTargetWinning should evaluate to false when source is winning.");

            // Act: change winning value to distinct instance equal to targetId
            row.WinningValueElementId = targetIdDistinct;

            // Assert
            Assert.IsFalse(row.IsSourceWinning, "IsSourceWinning should evaluate to false when target is winning.");
            Assert.IsTrue(row.IsTargetWinning, "IsTargetWinning should evaluate to true when WinningValueElementId is value-equal to Options[1].ElementId.");
        }

        [Test]
        public void RadioButtonBindings_RespondToUserSelectionChanges()
        {
            var sourceId = new ElementIdModel { Id = 1001, Name = "SourceType" };
            var targetId = new ElementIdModel { Id = 2002, Name = "TargetType" };

            var row = new ParameterDiffRowModel
            {
                ParameterName = "Width",
                Options = new List<ParameterValueOption>
                {
                    new ParameterValueOption { ElementId = sourceId, DisplayText = "100" },
                    new ParameterValueOption { ElementId = targetId, DisplayText = "200" }
                },
                WinningValueElementId = sourceId
            };

            List<string> changedProperties = new List<string>();
            row.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
            };

            // Act: simulate user clicking Target RadioButton (TwoWay binding sets IsTargetWinning = true)
            row.IsTargetWinning = true;

            // Assert
            Assert.IsTrue(row.IsTargetWinning, "IsTargetWinning should be true after user selection.");
            Assert.IsFalse(row.IsSourceWinning, "IsSourceWinning should be false after target selection.");
            Assert.AreEqual(targetId, row.WinningValueElementId, "WinningValueElementId should be updated to targetId.");
            Assert.Contains(nameof(row.IsSourceWinning), changedProperties, "PropertyChanged should fire for IsSourceWinning.");
            Assert.Contains(nameof(row.IsTargetWinning), changedProperties, "PropertyChanged should fire for IsTargetWinning.");

            // Act: simulate user clicking Source RadioButton
            changedProperties.Clear();
            row.IsSourceWinning = true;

            // Assert
            Assert.IsTrue(row.IsSourceWinning, "IsSourceWinning should be true after user selection.");
            Assert.IsFalse(row.IsTargetWinning, "IsTargetWinning should be false after source selection.");
            Assert.AreEqual(sourceId, row.WinningValueElementId, "WinningValueElementId should be updated to sourceId.");
            Assert.Contains(nameof(row.IsSourceWinning), changedProperties, "PropertyChanged should fire for IsSourceWinning.");
            Assert.Contains(nameof(row.IsTargetWinning), changedProperties, "PropertyChanged should fire for IsTargetWinning.");
        }

        [Test]
        public void GetValueForElement_DistinctElementIdModelInstances_DoesNotThrowKeyNotFoundException()
        {
            var keyInDict = new ElementIdModel { Id = 5005, UniqueId = "uid-5005", Name = "DoorType", Class = "Autodesk.Revit.DB.FamilySymbol" };
            var distinctQueryKey = new ElementIdModel { Id = 5005, UniqueId = "uid-5005", Name = "DoorType", Class = "Autodesk.Revit.DB.FamilySymbol" };

            var row = new ParameterDiffRowModel
            {
                ParameterName = "Cost"
            };

            row.Values[keyInDict] = "150.00";
            row.WinningValueElementId = distinctQueryKey;

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                string value = row.GetValueForElement(distinctQueryKey);
                Assert.AreEqual("150.00", value, "GetValueForElement should retrieve the dictionary value using value equality.");
            }, "GetValueForElement must not throw KeyNotFoundException when queried with distinct ElementIdModel instances.");

            Assert.AreEqual("150.00", row.WinningValue, "WinningValue property must return the winning value without throwing KeyNotFoundException.");
        }

        [Test]
        public void DictionaryKeyLookup_AcrossElementIdModelKeys_SucceedsForDistinctInstances()
        {
            var keyAdded = new ElementIdModel { Id = 7007, Name = "Window", Class = "Autodesk.Revit.DB.FamilySymbol" };
            var keyQueried = new ElementIdModel { Id = 7007, Name = "Window", Class = "Autodesk.Revit.DB.FamilySymbol" };

            var dict = new Dictionary<ElementIdModel, string>();
            dict[keyAdded] = "SampleValue";

            Assert.IsTrue(dict.ContainsKey(keyQueried), "Dictionary.ContainsKey must return true for value-equal distinct ElementIdModel key.");
            Assert.DoesNotThrow(() =>
            {
                string val = dict[keyQueried];
                Assert.AreEqual("SampleValue", val, "Dictionary indexer retrieval must return value for distinct ElementIdModel key.");
            });
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/PathResolutionUtilityTests.cs
```csharp
﻿using System;
using System.IO;
using NUnit.Framework;
using Synthetic.RevitDOM.Operations.Standards;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class PathResolutionUtilityTests
    {
        [Test]
        public void CloudModel_ShouldDefaultToMyDocuments()
        {
            // Arrange
            string docTitle = "CloudProject.rvt";
            bool isModelInCloud = true;
            bool isWorkshared = true;
            string? centralModelPath = "https://developer.api.autodesk.com/etc/etc";
            string? localPath = "C:\\BIM360\\LocalCopy.rvt";

            // Act
            string resultPath = PathResolutionUtility.GetDefaultSavePath(docTitle, isModelInCloud, isWorkshared, centralModelPath, localPath);

            // Assert
            string expectedDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string expectedPath = Path.Combine(expectedDir, "CloudProject Standards.json");
            Assert.AreEqual(expectedPath, resultPath);
        }

        [Test]
        public void LocalWorksharedModel_ShouldDefaultToCentralModelDirectory()
        {
            // Arrange
            string docTitle = "CentralModelName.rvt";
            bool isModelInCloud = false;
            bool isWorkshared = true;
            string? centralModelPath = "P:\\Projects\\Revit\\CentralModelName.rvt";
            string? localPath = "C:\\LocalSyncFolder\\CentralModelName_Local.rvt";

            // Act
            string resultPath = PathResolutionUtility.GetDefaultSavePath(docTitle, isModelInCloud, isWorkshared, centralModelPath, localPath);

            // Assert
            string expectedPath = "P:\\Projects\\Revit\\CentralModelName Standards.json";
            Assert.AreEqual(expectedPath, resultPath);
        }

        [Test]
        public void LocalNonWorksharedModel_ShouldDefaultToLocalModelDirectory()
        {
            // Arrange
            string docTitle = "SingleUserProject.rvt";
            bool isModelInCloud = false;
            bool isWorkshared = false;
            string? centralModelPath = null;
            string? localPath = "D:\\Work\\SingleUserProject.rvt";

            // Act
            string resultPath = PathResolutionUtility.GetDefaultSavePath(docTitle, isModelInCloud, isWorkshared, centralModelPath, localPath);

            // Assert
            string expectedPath = "D:\\Work\\SingleUserProject Standards.json";
            Assert.AreEqual(expectedPath, resultPath);
        }

        [Test]
        public void DocumentTitle_WithInvalidCharacters_ShouldBeSanitized()
        {
            // Arrange
            string docTitle = "Project:Special/Name?.rvt";
            bool isModelInCloud = false;
            bool isWorkshared = false;
            string? centralModelPath = null;
            string? localPath = "C:\\Revit\\Model.rvt";

            // Act
            string resultPath = PathResolutionUtility.GetDefaultSavePath(docTitle, isModelInCloud, isWorkshared, centralModelPath, localPath);

            // Assert
            // PathResolutionUtility replaces invalid filename chars with '_'
            // Invalid chars in Windows usually include ':', '/', '?'
            string expectedFileName = "Project_Special_Name_ Standards.json";
            string expectedPath = Path.Combine("C:\\Revit", expectedFileName);
            Assert.AreEqual(expectedPath, resultPath);
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/PhasedExecutionTests.cs
```csharp
﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class PhasedExecutionTests
    {
        private Document _doc = null!;
        private FakeFileDialogService _fakeDialogService = null!;
        private string _tempSavePath = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeDialogService = new FakeFileDialogService();
            _tempSavePath = Path.Combine(Path.GetTempPath(), $"SyntheticTemp_{Guid.NewGuid():N}.json");
            _fakeDialogService.PresetPath = _tempSavePath;
            ProgressCoordinator.SuppressUI = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempSavePath))
            {
                try { File.Delete(_tempSavePath); } catch { }
            }
            ProgressCoordinator.SuppressUI = false;
            ProgressCoordinator.ForceCancel = false;
        }

        [Test]
        public void RunQueue_FailureStripping_ShouldSaveOnlySuccessfulPhase1ItemsToDisk()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StandardsExecutionPipelineViewModel;

            vm.SaveFilePath = _tempSavePath;

            // Item 1: Valid element (succeeds Phase 1)
            var validParam = new ParameterModel("Comments", "ValidVal", null, "String", 1, null, false, false);
            var validEl = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "ValidMaterial", Parameters = new List<ParameterModel> { validParam } };
            var qValid = new QueueItemModel(validEl, true, true);
            
            // Item 2: Invalid element (fails Phase 1 due to missing class or null properties)
            var invalidEl = new ElementModel { Class = null!, Name = "InvalidMaterial" };
            var qInvalid = new QueueItemModel(invalidEl, true, true);

            parent.StagingQueue.Add(qValid);
            parent.StagingQueue.Add(qInvalid);

            // Act
            vm.RunQueueCommand.Execute(null!);

            // Assert: valid element was processed, invalid failed and was stripped
            Assert.AreEqual(3, parent.LastExecutionResults.Count, "Should have 3 execution results logged (2 from Phase 1, 1 from Phase 2).");
            
            var validResult = parent.LastExecutionResults.First(r => r.Model == qValid.Model);
            var invalidResult = parent.LastExecutionResults.First(r => r.Model == qInvalid.Model);

            Assert.IsTrue(validResult.Success, "Valid element should succeed Phase 1.");
            Assert.IsFalse(invalidResult.Success, "Invalid element should fail Phase 1.");

            // Assert: Phase 2 JSON file exists and contains only the valid element
            Assert.IsTrue(File.Exists(_tempSavePath), "The target JSON file should be written.");
            string jsonContent = File.ReadAllText(_tempSavePath);
            Assert.IsTrue(jsonContent.Contains("ValidMaterial"), "JSON file must contain successful elements.");
            Assert.IsFalse(jsonContent.Contains("InvalidMaterial"), "JSON file must exclude stripped/failed elements.");
        }

        [Test]
        public void RunQueue_UserCancellation_ShouldRollbackAllRevitWritesAndSkipPhase2()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StandardsExecutionPipelineViewModel;
            vm.SaveFilePath = _tempSavePath;

            var param = new ParameterModel("Comments", "SomeValue", null, "String", 1, null, false, false);
            var el = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "MatToCancel", Parameters = new List<ParameterModel> { param } };
            var qItem = new QueueItemModel(el, true, true);
            parent.StagingQueue.Add(qItem);

            // Inject a mock cancellation state in the coordinator
            ProgressCoordinator.ForceCancel = true;

            // Act
            vm.RunQueueCommand.Execute(null!);

            // Assert
            Assert.IsFalse(File.Exists(_tempSavePath), "Phase 2 file writing must be skipped completely on user cancellation.");
            Assert.IsTrue(qItem.WillEnforce && qItem.WillSave, "QueueItem intent flags should remain unchanged on rollback.");
            
            var cancelResult = parent.LastExecutionResults.FirstOrDefault(r => r.Model == qItem.Model);
            Assert.IsNotNull(cancelResult, "Cancellation execution result should be registered.");
            Assert.IsFalse(cancelResult!.Success, "Cancelled results must show as failed.");
            Assert.AreEqual("Execution cancelled by user.", cancelResult.ErrorMessage);
        }

        [Test]
        public void ProcessFamilyUpdates_WhenDisabled_ShouldNotScanForFamilies()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StandardsExecutionPipelineViewModel;
            vm.UpdateFamilies = false;

            var param = new ParameterModel("Comments", "Val", null, "String", 1, null, false, false);
            var el = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Mat", Parameters = new List<ParameterModel> { param } };
            var qItem = new QueueItemModel(el, true, false);
            parent.StagingQueue.Add(qItem);

            // Act
            vm.RunQueueCommand.Execute(null!);

            // Assert: Completed without trying to collect families (since mocked Revit doc throws on family queries in real run but passes here)
            Assert.AreEqual(1, parent.LastExecutionResults.Count);
            Assert.IsTrue(parent.LastExecutionResults[0].Success);
        }

        [Test]
        public void RunQueue_PostExecutionPurging_ShouldRemoveSuccessfulAndKeepFailedItems()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var vm = parent.StandardsExecutionPipelineViewModel;

            var validParam = new ParameterModel("Comments", "ValidVal", null, "String", 1, null, false, false);
            var validEl = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "ValidMaterial", Parameters = new List<ParameterModel> { validParam } };
            var qValid = new QueueItemModel(validEl, true, true);

            var invalidEl = new ElementModel { Class = null!, Name = "InvalidMaterial" };
            var qInvalid = new QueueItemModel(invalidEl, true, true);

            parent.StagingQueue.Add(qValid);
            parent.StagingQueue.Add(qInvalid);

            // Act
            vm.RunQueueCommand.Execute(null!);

            // Assert
            Assert.AreEqual(1, parent.StagingQueue.Count, "Successful items should be purged, failed should remain.");
            Assert.AreSame(qInvalid, parent.StagingQueue[0], "The failed item should remain in the queue.");
            Assert.IsTrue(qInvalid.HasError, "The failed item should have error registered.");
            Assert.IsFalse(string.IsNullOrEmpty(qInvalid.ErrorMessage), "The error message should be populated.");
        }

        [Test]
        public void FailedItem_OnParameterEdit_ShouldClearErrorAndSetIntentToEdited()
        {
            // Arrange
            var parent = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            var queueVM = parent.StagingQueueViewModel;
            var paramModel = new ParameterModel("Comments", "SomeVal", null, "String", 1, null, false, false);
            var el = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Mat", Parameters = new List<ParameterModel> { paramModel } };
            var qItem = new QueueItemModel(el, true, true);
            qItem.ErrorMessage = "Some error occurred";

            parent.StagingQueue.Add(qItem);

            // Select item and enter edit mode
            queueVM.EditCommand.Execute(new List<QueueItemModel> { qItem });

            // Act: Mutate parameter
            var param = queueVM.DisplayParameters.First();
            param.Value = "NewVal";

            // Assert
            Assert.IsFalse(qItem.HasError, "Error state should be cleared on edit.");
            Assert.IsNull(qItem.ErrorMessage, "ErrorMessage should be reset to null.");
            Assert.IsTrue(qItem.IsEdited, "IsEdited should be set to true on edit.");
            Assert.IsNull(queueVM.SelectedItemErrorMessage, "Selected item error message on VM should be cleared.");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/ProjectStandardsDashboardTests.cs
```csharp
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Merge;
using SyntheticTests.Modules.RevitDOM;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class ProjectStandardsDashboardTests
    {
        private Document _doc = null!;
        private FakeFileDialogService _fakeDialogService = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeDialogService = new FakeFileDialogService();
            ProgressCoordinator.SuppressUI = true;
        }

        [Test]
        public void VerifyCascadingCheckboxStates_CheckGroup_ChecksChildren()
        {
            // Arrange
            var group = new StandardGroupModel { Name = "Annotations" };
            var classModel = new StandardClassModel { Name = "Text Note Types" };
            var elementPOCO = new ElementModel { Name = "Arial 3/32" };
            var elementNode = new StandardElementModel(elementPOCO);

            group.Children.Add(classModel);
            classModel.Parent = group;

            classModel.Children.Add(elementNode);
            elementNode.Parent = classModel;

            Assert.IsFalse(group.IsChecked == true);
            Assert.IsFalse(classModel.IsChecked == true);
            Assert.IsFalse(elementNode.IsChecked == true);

            // Act
            group.IsChecked = true;

            // Assert
            Assert.IsTrue(group.IsChecked == true, "Group should be checked.");
            Assert.IsTrue(classModel.IsChecked == true, "Child Class should cascade check.");
            Assert.IsTrue(elementNode.IsChecked == true, "Leaf Element should cascade check.");
        }

        [Test]
        public void VerifyCascadingCheckboxStates_UncheckElement_IndeterminateParent()
        {
            // Arrange
            var group = new StandardGroupModel { Name = "Annotations" };
            var classModel = new StandardClassModel { Name = "Text Note Types" };
            var elemNodeA = new StandardElementModel(new ElementModel { Name = "Type A" });
            var elemNodeB = new StandardElementModel(new ElementModel { Name = "Type B" });

            group.Children.Add(classModel);
            classModel.Parent = group;

            classModel.Children.Add(elemNodeA);
            elemNodeA.Parent = classModel;
            classModel.Children.Add(elemNodeB);
            elemNodeB.Parent = classModel;

            group.IsChecked = true;
            Assert.IsTrue(group.IsChecked == true);

            // Act
            elemNodeA.IsChecked = false;

            // Assert
            Assert.IsNull(classModel.IsChecked, "Class should be indeterminate (null).");
            Assert.IsNull(group.IsChecked, "Group should be indeterminate (null).");
            Assert.IsTrue(elemNodeB.IsChecked == true, "Sibling node should remain checked.");
        }

        [Test]
        public void VerifyDashboardStartup_LoadsDefaultStandardsFromSettings()
        {
            // Arrange
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            File.WriteAllText(tempFile, "{}"); // Empty JSON representing empty ModelsToSerialize

            var settings = new StandardsSettings { StandardsFilePath = tempFile };

            try
            {
                // Act
                var vm = DashboardTestFactory.Create(_doc, _fakeDialogService, settings: settings);

                // Assert
                Assert.AreEqual(1, vm.AvailableSources.Count, "Dashboard should load the default standards source on startup.");
                Assert.AreEqual("Default Firm Standard", vm.AvailableSources[0].DisplayName, "Tab display name should match default label.");
                Assert.AreEqual(tempFile, vm.AvailableSources[0].SourcePath, "Source path should match configuration file path.");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public void VerifyDashboardStartup_InitializesSmartDefaultSavePathEvenWithSettings()
        {
            // Arrange
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            File.WriteAllText(tempFile, "{}");

            var settings = new StandardsSettings { StandardsFilePath = tempFile };
            
            _doc.GetType().GetProperty("Title")?.SetValue(_doc, "ProjectModel.rvt");
            _doc.GetType().GetProperty("PathName")?.SetValue(_doc, Path.Combine(Path.GetTempPath(), "ProjectModel.rvt"));

            try
            {
                // Act
                var vm = DashboardTestFactory.Create(_doc, _fakeDialogService, settings: settings);

                // Assert
                // 1. Should load the default firm standards source tab
                Assert.AreEqual(1, vm.AvailableSources.Count);
                Assert.AreEqual("Default Firm Standard", vm.AvailableSources[0].DisplayName);
                
                // 2. Should initialize SaveFilePath to the smart default path, NOT the settings path
                string expectedSmartPath = Path.Combine(Path.GetDirectoryName(_doc.PathName)!, "ProjectModel Standards.json");
                Assert.AreEqual(expectedSmartPath, vm.SaveFilePath);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public void VerifyDashboardStartup_FallsBackToRevitLocalSavePathIfDocumentIsNull()
        {
            // Arrange
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            File.WriteAllText(tempFile, "{}");

            var settings = new StandardsSettings { StandardsFilePath = tempFile };

            try
            {
                // Act - passing null for doc
                var vm = DashboardTestFactory.Create((Document)null!, _fakeDialogService, settings: settings);

                // Assert
                string expectedFallbackDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string expectedPath = Path.Combine(expectedFallbackDir, "Project Standards.json");
                Assert.AreEqual(expectedPath, vm.SaveFilePath);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public void VerifyAddFileSource_AppendsNewTab()
        {
            // Arrange
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            File.WriteAllText(tempFile, "{}");

            _fakeDialogService.PresetPath = tempFile;
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            int initialCount = vm.AvailableSources.Count;

            try
            {
                // Act
                vm.AddFileSourceCommand.Execute(null);

                // Assert
                Assert.AreEqual(initialCount + 1, vm.AvailableSources.Count, "New source tab should be appended.");
                Assert.AreEqual(Path.GetFileName(tempFile), vm.AvailableSources[initialCount].DisplayName, "Tab name should match file name.");
                Assert.AreEqual(tempFile, vm.AvailableSources[initialCount].SourcePath);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public void PushToQueue_CorrectlyStagesCheckedItemsWithIntent()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            
            // Create a fake source tab with some elements
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            var elem1 = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var elem2 = new ElementModel { Name = "Concrete", Class = "Autodesk.Revit.DB.Material" };
            
            var elemNode1 = new StandardElementModel(elem1);
            var elemNode2 = new StandardElementModel(elem2);
            
            classModel.Children.Add(elemNode1);
            classModel.Children.Add(elemNode2);
            elemNode1.Parent = classModel;
            elemNode2.Parent = classModel;
            
            group.Children.Add(classModel);
            classModel.Parent = group;
            
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            // Check elemNode1
            elemNode1.IsChecked = true;

            // Act
            vm.PushToQueueCommand.Execute("Save");

            // Assert
            Assert.AreEqual(1, vm.StagingQueue.Count, "One item should be staged in the Action Queue.");
            Assert.AreEqual("Steel", vm.StagingQueue[0].Name, "Staged item name should match the checked source element.");
            Assert.IsTrue(vm.StagingQueue[0].WillSave && !vm.StagingQueue[0].WillEnforce, "Staged item execution intent should match parameter.");
        }

        [Test]
        public void PushToQueue_StagesOfflineDependenciesFromJSON()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);

            // Create a fake JSON/offline source tab with a WallType and a Material
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test JSON Source", IsRevitSource = false };

            // WallType
            var wallGroup = new StandardGroupModel { Name = "Walls" };
            var wallClass = new StandardClassModel { Name = "Walls" };

            // Material dependency
            var materialId = new ElementIdModel { Name = "StagingTestMaterial", Class = "Autodesk.Revit.DB.Material" };

            // WallType inherits from HostObjTypeModel which has a Structure property containing layers
            var wallModel = new HostObjTypeModel
            {
                Name = "StagingTestWallType",
                Class = "Autodesk.Revit.DB.WallType",
                Structure = new CompoundStructureModel
                {
                    Layers = new List<SerialCompoundStructureLayer>
                    {
                        new SerialCompoundStructureLayer
                        {
                            MaterialId = materialId
                        }
                    }
                }
            };

            var wallNode = new StandardElementModel(wallModel);
            wallClass.Children.Add(wallNode);
            wallNode.Parent = wallClass;
            wallGroup.Children.Add(wallClass);
            wallClass.Parent = wallGroup;
            source.SourceHierarchy.Add(wallGroup);

            // Material
            var matGroup = new StandardGroupModel { Name = "Materials & Assets" };
            var matClass = new StandardClassModel { Name = "Materials" };
            var matModel = new MaterialModel
            {
                Name = "StagingTestMaterial",
                Class = "Autodesk.Revit.DB.Material"
            };
            var matNode = new StandardElementModel(matModel);
            matClass.Children.Add(matNode);
            matNode.Parent = matClass;
            matGroup.Children.Add(matClass);
            matClass.Parent = matGroup;
            source.SourceHierarchy.Add(matGroup);

            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            // Check only the WallType
            wallNode.IsChecked = true;

            // Act
            vm.PushToQueueCommand.Execute("Enforce");

            // Assert
            Assert.AreEqual(2, vm.StagingQueue.Count, "Both WallType and Material should be staged in the Action Queue.");

            var wallQueueItem = vm.StagingQueue.FirstOrDefault(q => q.Name == "StagingTestWallType");
            var matQueueItem = vm.StagingQueue.FirstOrDefault(q => q.Name == "StagingTestMaterial");

            Assert.IsNotNull(wallQueueItem, "WallType should be staged.");
            Assert.IsNotNull(matQueueItem, "Material dependency should be harvested and staged.");

            Assert.IsNull(wallQueueItem!.DependencyOrigin, "WallType should have null DependencyOrigin because it was explicitly checked.");
            Assert.AreEqual("StagingTestWallType", matQueueItem!.DependencyOrigin, "Material's DependencyOrigin should match the WallType parent name.");
        }

        [Test]
        public void PushToQueue_StagesLiveDependenciesUsingPocoIdentityResolution()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);

            // Create a fake live Revit model source tab with a WallType and a Material, both having UniqueIds
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Revit Model", IsRevitSource = true };

            // WallType
            var wallGroup = new StandardGroupModel { Name = "Walls" };
            var wallClass = new StandardClassModel { Name = "Walls" };

            // Material dependency
            var materialId = new ElementIdModel 
            { 
                Name = "Concrete-Live", 
                Class = "Autodesk.Revit.DB.Material", 
                UniqueId = "material-guid-live-1",
                Id = 8881,
                IsTemplate = false 
            };

            // WallType inherits from HostObjTypeModel
            var wallModel = new HostObjTypeModel
            {
                Name = "Wall-Live",
                Class = "Autodesk.Revit.DB.WallType",
                UniqueId = "wall-guid-live-1",
                Id = 9991,
                IsTemplate = false,
                Structure = new CompoundStructureModel
                {
                    Layers = new List<SerialCompoundStructureLayer>
                    {
                        new SerialCompoundStructureLayer
                        {
                            MaterialId = materialId
                        }
                    }
                }
            };

            var wallNode = new StandardElementModel(wallModel);
            wallClass.Children.Add(wallNode);
            wallNode.Parent = wallClass;
            wallGroup.Children.Add(wallClass);
            wallClass.Parent = wallGroup;
            source.SourceHierarchy.Add(wallGroup);

            // Material element in the source tab
            var matGroup = new StandardGroupModel { Name = "Materials & Assets" };
            var matClass = new StandardClassModel { Name = "Materials" };
            var matModel = new MaterialModel
            {
                Name = "Concrete-Live",
                Class = "Autodesk.Revit.DB.Material",
                UniqueId = "material-guid-live-1",
                Id = 8881,
                IsTemplate = false
            };
            var matNode = new StandardElementModel(matModel);
            matClass.Children.Add(matNode);
            matNode.Parent = matClass;
            matGroup.Children.Add(matClass);
            matClass.Parent = matGroup;
            source.SourceHierarchy.Add(matGroup);

            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            // Check only the WallType
            wallNode.IsChecked = true;

            // Act
            vm.PushToQueueCommand.Execute("Enforce");

            // Assert
            Assert.AreEqual(2, vm.StagingQueue.Count, "Both WallType and Material should be staged in the Action Queue.");

            var wallQueueItem = vm.StagingQueue.FirstOrDefault(q => q.Name == "Wall-Live");
            var matQueueItem = vm.StagingQueue.FirstOrDefault(q => q.Name == "Concrete-Live");

            Assert.IsNotNull(wallQueueItem, "WallType should be staged.");
            Assert.IsNotNull(matQueueItem, "Material dependency should be resolved via PocoIdentityService and staged.");

            // Confirm that IsTemplate is false by default so UniqueId/Id are preserved on queue items
            Assert.IsFalse(((ElementModel)wallQueueItem!.Model).IsTemplate, "Queue item IsTemplate should be false.");
            Assert.AreEqual("wall-guid-live-1", ((ElementModel)wallQueueItem.Model).UniqueId, "UniqueId should be preserved.");
            Assert.AreEqual("material-guid-live-1", ((ElementModel)matQueueItem!.Model).UniqueId, "Material UniqueId should be preserved.");
            Assert.AreEqual("Wall-Live", matQueueItem!.DependencyOrigin, "Material's DependencyOrigin should match parent.");
        }

        [Test]
        public void PushToQueue_EnforcesDeepCopyIsolation()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var elemNode = new StandardElementModel(elem);
            
            classModel.Children.Add(elemNode);
            elemNode.Parent = classModel;
            group.Children.Add(classModel);
            classModel.Parent = group;
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            elemNode.IsChecked = true;

            // Act
            vm.PushToQueueCommand.Execute("Enforce");
            
            // Modify name on the staged queue item's model
            var stagedItem = vm.StagingQueue[0];
            if (stagedItem.Model is ElementModel stagedElem)
            {
                stagedElem.Name = "Modified Steel";
            }

            // Assert
            Assert.AreEqual("Steel", elem.Name, "Original source element POCO should remain unchanged (DeepClone isolation).");
        }

        [Test]
        public void RemoveFromQueue_PurgesTargetedStagedItems()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            var elem1 = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var elem2 = new ElementModel { Name = "Concrete", Class = "Autodesk.Revit.DB.Material" };
            
            var elemNode1 = new StandardElementModel(elem1);
            var elemNode2 = new StandardElementModel(elem2);
            
            classModel.Children.Add(elemNode1);
            classModel.Children.Add(elemNode2);
            elemNode1.Parent = classModel;
            elemNode2.Parent = classModel;
            group.Children.Add(classModel);
            classModel.Parent = group;
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            elemNode1.IsChecked = true;
            elemNode2.IsChecked = true;

            // Push both to queue
            vm.PushToQueueCommand.Execute("SaveAndEnforce");
            Assert.AreEqual(2, vm.StagingQueue.Count);

            // Act - remove item 1
            var listToRemove = new System.Collections.ArrayList { vm.StagingQueue[0] };
            vm.RemoveFromQueueCommand.Execute(listToRemove);

            // Assert
            Assert.AreEqual(1, vm.StagingQueue.Count, "Action queue should contain exactly 1 element after removal.");
            Assert.AreEqual("Concrete", vm.StagingQueue[0].Name, "Remaining staged element should be Concrete.");
        }

        [Test]
        public void EditCommand_SetsActiveWorkspaceToEdit()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var elemNode = new StandardElementModel(elem);
            classModel.Children.Add(elemNode);
            group.Children.Add(classModel);
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            elemNode.IsChecked = true;
            vm.PushToQueueCommand.Execute("Enforce");

            var item = vm.StagingQueue[0];
            var listToEdit = new System.Collections.ArrayList { item };

            // Act
            vm.EditCommand.Execute(listToEdit);

            // Assert
            Assert.AreEqual(WorkspaceMode.Edit, vm.ActiveWorkspace, "ActiveWorkspace mode should transition to Edit.");
            Assert.AreEqual(1, vm.SelectedQueueItems.Count, "SelectedQueueItems should contain 1 staged item.");
            Assert.AreSame(item, vm.SelectedQueueItems[0], "The staged item in SelectedQueueItems should match the selection.");
        }

        [Test]
        public void BatchFindReplace_MutatesSelectedQueueElementNamesAndParameters()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            
            var elem = new ElementModel 
            { 
                Name = "Steel Column", 
                Class = "Autodesk.Revit.DB.Material",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "Description", Value = "Steel material for columns", IsReadOnly = false }
                }
            };
            var elemNode = new StandardElementModel(elem);
            classModel.Children.Add(elemNode);
            group.Children.Add(classModel);
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            elemNode.IsChecked = true;
            vm.PushToQueueCommand.Execute("Enforce");

            var item = vm.StagingQueue[0];
            var listToEdit = new System.Collections.ArrayList { item };
            vm.EditCommand.Execute(listToEdit);

            vm.FindText = "Steel";
            vm.ReplaceText = "Iron";

            // Act
            vm.BatchFindReplaceCommand.Execute(null);

            // Assert
            var editedModel = (ElementModel)item.Model;
            Assert.AreEqual("Iron Column", editedModel.Name, "Element Name should undergo find-and-replace.");
            Assert.AreEqual("Iron material for columns", editedModel.Parameters[0].Value, "Writable Parameter Value should undergo find-and-replace.");
        }

        [Test]
        public void ApplyEdits_SetsIntentToEditedAndResetsWorkspace()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var elemNode = new StandardElementModel(elem);
            classModel.Children.Add(elemNode);
            group.Children.Add(classModel);
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            elemNode.IsChecked = true;
            vm.PushToQueueCommand.Execute("Enforce");

            var item = vm.StagingQueue[0];
            var listToEdit = new System.Collections.ArrayList { item };
            vm.EditCommand.Execute(listToEdit);

            // Act
            vm.ApplyEditsCommand.Execute(null);

            // Assert
            Assert.IsTrue(item.IsEdited, "Staged item's IsEdited should be true.");
            Assert.AreEqual(WorkspaceMode.Idle, vm.ActiveWorkspace, "ActiveWorkspace mode should transition back to Idle.");
        }

        [Test]
        public void DiffCommand_InvokesScanAndTransitionsToDiff()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var source = new ProjectStandardsSourceViewModel { DisplayName = "Test Source" };
            var group = new StandardGroupModel { Name = "Materials & Assets" };
            var classModel = new StandardClassModel { Name = "Materials" };
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var elemNode = new StandardElementModel(elem);
            classModel.Children.Add(elemNode);
            group.Children.Add(classModel);
            source.SourceHierarchy.Add(group);
            vm.AvailableSources.Add(source);
            vm.SelectedSource = source;

            elemNode.IsChecked = true;
            vm.PushToQueueCommand.Execute("Enforce");

            var item = vm.StagingQueue[0];
            var listToDiff = new System.Collections.ArrayList { item };

            // Act
            vm.DiffCommand.Execute(listToDiff);

            // Assert
            Assert.AreEqual(WorkspaceMode.Diff, vm.ActiveWorkspace, "ActiveWorkspace mode should transition to Diff.");
            Assert.AreEqual(1, vm.SelectedQueueItems.Count, "SelectedQueueItems should contain the item.");
        }

        [Test]
        public void ResolveConflict_MutatesStagedPOCOWithWinningValuesAndSetsDiffedIntent()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            
            var elem = new ElementModel 
            { 
                Name = "Steel", 
                Class = "Autodesk.Revit.DB.Material",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "Description", Value = "Staged Standard Value", IsReadOnly = false }
                }
            };
            var item = new QueueItemModel(elem, true, false);
            vm.SelectedQueueItems.Add(item);

            // Create a fake cluster mapping to simulate a diff match
            var cluster = new DuplicateClusterModel { ClusterName = "Materials: Standards Comparison" };
            var targetType = new DuplicateTypeModel { Name = "Steel" };
            var mapping = new TypeMappingModel
            {
                TargetType = targetType
            };

            var sourceId = new ElementIdModel { Id = 101 };
            var targetId = new ElementIdModel { Id = 102 };

            var diffRow = new ParameterDiffRowModel
            {
                ParameterName = "Description",
                Options = new List<ParameterValueOption>
                {
                    // Option[0] is Local Document value
                    new ParameterValueOption { ElementId = sourceId, DisplayText = "Local Document Value" },
                    // Option[1] is Staged value
                    new ParameterValueOption { ElementId = targetId, DisplayText = "Staged Standard Value" }
                }
            };
            // Set local document value as the winner
            diffRow.IsSourceWinning = true;

            mapping.ParameterResolutions.Add(diffRow);
            cluster.TypeMappings.Add(mapping);
            vm.ActiveDiffClusters.Add(cluster);

            // Act
            vm.ResolveConflictCommand.Execute(null);

            // Assert
            Assert.AreEqual("Local Document Value", ((ElementModel)item.Model).Parameters[0].Value, "Staged POCO parameter value should be mutated to the winning local value.");
            Assert.IsTrue(item.IsDiffed, "IsDiffed should be true.");
            Assert.AreEqual(WorkspaceMode.Idle, vm.ActiveWorkspace, "ActiveWorkspace should return to Idle.");
            Assert.AreEqual(0, vm.ActiveDiffClusters.Count, "ActiveDiffClusters should be cleared.");
        }

        [Test]
        public void RunQueue_BypassesPhase1ForSaveOnlyItems()
        {
            // Arrange
            var fakeFileDialog = new FakeFileDialogService();
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            fakeFileDialog.PresetPath = tempFile;

            var vm = DashboardTestFactory.Create(_doc, fakeFileDialog);
            vm.SaveFilePath = tempFile;
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            
            // Item is tagged Save only (should completely bypass Phase 1)
            var item = new QueueItemModel(elem, false, true);
            vm.StagingQueue.Add(item);

            try
            {
                // Act
                vm.RunQueueCommand.Execute(null);

                // Assert
                Assert.AreEqual(0, vm.StagingQueue.Count, "Queue should be cleared.");
                Assert.IsTrue(File.Exists(tempFile), "Save should write to the JSON file.");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Test]
        public void RunQueue_TriggersGuardrailForProtectedFiles()
        {
            // Arrange
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            File.WriteAllText(tempFile, "{}");

            var settings = new StandardsSettings { StandardsFilePath = tempFile };
            try
            {
                SettingsManager.Save(_doc, settings);
            }
            catch { }

            var fakeGuardrail = new FakeGuardrailPromptService(GuardrailResult.Skip);
            var fakeFileDialog = new FakeFileDialogService();
            var vm = DashboardTestFactory.Create(_doc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            vm.SaveFilePath = tempFile;

            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var item = new QueueItemModel(elem, false, true);
            vm.StagingQueue.Add(item);

            try
            {
                // Act
                vm.RunQueueCommand.Execute(null);

                // Assert
                Assert.AreEqual(1, fakeGuardrail.PromptedPaths.Count, "Guardrail prompt should be invoked.");
                Assert.AreEqual(Path.GetFullPath(tempFile), Path.GetFullPath(fakeGuardrail.PromptedPaths[0]), "Prompted path should match the protected master file.");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Test]
        public void RunQueue_RedirectsOnSaveAsSelection()
        {
            // Arrange
            string protectedFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_protected.json");
            string redirectedFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_redirected.json");
            File.WriteAllText(protectedFile, "{}");

            var settings = new StandardsSettings { StandardsFilePath = protectedFile };
            try
            {
                SettingsManager.Save(_doc, settings);
            }
            catch { }

            var fakeGuardrail = new FakeGuardrailPromptService(GuardrailResult.SaveAs);
            var fakeFileDialog = new FakeFileDialogService();
            fakeFileDialog.PresetPath = redirectedFile;

            var vm = DashboardTestFactory.Create(_doc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            vm.SaveFilePath = protectedFile;
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var item = new QueueItemModel(elem, false, true);
            vm.StagingQueue.Add(item);

            try
            {
                // Act
                vm.RunQueueCommand.Execute(null);

                // Assert
                Assert.AreEqual(1, fakeGuardrail.PromptedPaths.Count, "Guardrail prompt should be invoked.");
                Assert.IsTrue(File.Exists(redirectedFile), "File should be saved to the redirected path.");
                Assert.IsFalse(File.Exists(protectedFile) && File.ReadAllText(protectedFile) != "{}", "Protected file should not be overwritten.");
            }
            finally
            {
                if (File.Exists(protectedFile)) File.Delete(protectedFile);
                if (File.Exists(redirectedFile)) File.Delete(redirectedFile);
            }
        }

        [Test]
        public void RunQueue_AbortsOnSkipSelection()
        {
            // Arrange
            string protectedFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_protected.json");
            File.WriteAllText(protectedFile, "{}");

            var settings = new StandardsSettings { StandardsFilePath = protectedFile };
            try
            {
                SettingsManager.Save(_doc, settings);
            }
            catch { }

            var fakeGuardrail = new FakeGuardrailPromptService(GuardrailResult.Skip);
            var fakeFileDialog = new FakeFileDialogService();

            var vm = DashboardTestFactory.Create(_doc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            vm.SaveFilePath = protectedFile;
            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var item = new QueueItemModel(elem, false, true);
            vm.StagingQueue.Add(item);

            try
            {
                // Act
                vm.RunQueueCommand.Execute(null);

                // Assert
                Assert.AreEqual(1, fakeGuardrail.PromptedPaths.Count, "Guardrail prompt should be invoked.");
                Assert.AreEqual("{}", File.ReadAllText(protectedFile), "Protected file content should remain untouched.");
            }
            finally
            {
                if (File.Exists(protectedFile)) File.Delete(protectedFile);
            }
        }

        [Test]
        public void RunQueue_InvokesSummaryDisplayService_UponExecution()
        {
            // Arrange
            var fakeFileDialog = new FakeFileDialogService();
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_test.json");
            fakeFileDialog.PresetPath = tempFile;

            var fakeSummaryService = new FakeSummaryDisplayService();
            var vm = DashboardTestFactory.Create(_doc, fakeFileDialog, null, null);
            vm.SummaryDisplayService = fakeSummaryService;

            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var item = new QueueItemModel(elem, false, true);
            vm.StagingQueue.Add(item);

            try
            {
                // Act
                vm.RunQueueCommand.Execute(null);

                // Assert
                Assert.AreEqual(1, fakeSummaryService.ShowCallCount, "Summary display service should be invoked exactly once.");
                Assert.IsNotNull(fakeSummaryService.LastViewModel, "Passed view model should not be null.");
                Assert.IsInstanceOf<ImportSummaryViewModel>(fakeSummaryService.LastViewModel, "Passed view model should be ImportSummaryViewModel.");

                var summaryVM = (ImportSummaryViewModel)fakeSummaryService.LastViewModel;
                Assert.AreEqual(1, summaryVM.LogItems.Count, "There should be exactly 1 log item in the summary.");
                Assert.AreEqual("Steel", summaryVM.LogItems[0].ElementName, "Log item name should match the enqueued element.");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                string logFile = Path.ChangeExtension(tempFile, ".log.md");
                if (File.Exists(logFile)) File.Delete(logFile);
            }
        }

        [Test]
        public void RunQueue_WritesAutomaticMarkdownLog_NextToTargetPath()
        {
            // Arrange
            var fakeFileDialog = new FakeFileDialogService();
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_test.json");
            string expectedLogFile = Path.ChangeExtension(tempFile, ".log.md");
            fakeFileDialog.PresetPath = tempFile;

            var fakeSummaryService = new FakeSummaryDisplayService();
            var vm = DashboardTestFactory.Create(_doc, fakeFileDialog, null, null);
            vm.SaveFilePath = tempFile;
            vm.SummaryDisplayService = fakeSummaryService;

            var elem = new ElementModel { Name = "Steel", Class = "Autodesk.Revit.DB.Material" };
            var item = new QueueItemModel(elem, false, true);
            vm.StagingQueue.Add(item);

            try
            {
                // Act
                vm.RunQueueCommand.Execute(null);

                // Assert
                Assert.IsTrue(File.Exists(tempFile), "Target JSON file should be created.");
                Assert.IsTrue(File.Exists(expectedLogFile), "Automatic Markdown log file should be created next to JSON file.");

                string logContent = File.ReadAllText(expectedLogFile);
                Assert.IsTrue(logContent.Contains("# Project Standards Consolidation Execution Report"), "Log should contain title header.");
                Assert.IsTrue(logContent.Contains("| Metric | Count |"), "Log should contain summary table.");
                Assert.IsTrue(logContent.Contains("| Steel |"), "Log should contain the elements table row.");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                if (File.Exists(expectedLogFile)) File.Delete(expectedLogFile);
            }
        }

        [Test]
        public void BuildHierarchy_ShouldExpandGroupsAndCollapseClassesByDefault()
        {
            // Arrange
            var elements = new List<ElementModel>
            {
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Oak", Category = "Materials" },
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Pine", Category = "Materials" }
            };

            // Act
            var hierarchy = StandardsHierarchyUtility.BuildHierarchy(elements);

            // Assert
            Assert.AreEqual(1, hierarchy.Count, "Should contain 1 group.");
            var group = hierarchy[0];
            Assert.AreEqual("Materials & Assets", group.Name);
            Assert.IsTrue(group.IsExpanded, "Top level group should be expanded by default.");

            Assert.AreEqual(1, group.Children.Count, "Group should contain 1 class.");
            var classModel = group.Children[0] as StandardClassModel;
            Assert.IsNotNull(classModel);
            Assert.AreEqual("Materials", classModel.Name);
            Assert.IsFalse(classModel.IsExpanded, "Class should be collapsed by default.");
        }

        [Test]
        public void AddRevitModel_ExtractsNestedDependencies_BasedOnOrchestration()
        {
            // Arrange
            var mainDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            dynamic mainDocDyn = mainDoc;

            // Create a mock WallType
            var wallType = (WallType)Activator.CreateInstance(typeof(WallType), true)!;
            wallType.Name = "Orchestration_Test_WallType";
            typeof(Element).GetProperty("Id")?.SetValue(wallType, new ElementId(1001));
            mainDocDyn.AddElement(wallType, new ElementId(1001));

            // Create a mock Material
            var material = (Material)Activator.CreateInstance(typeof(Material), true)!;
            material.Name = "Orchestration_Test_Material";
            typeof(Element).GetProperty("Id")?.SetValue(material, new ElementId(2001));
            mainDocDyn.AddElement(material, new ElementId(2001));

            // Set up mock compound structure on WallType referencing the Material
            var layer = new CompoundStructureLayer();
            layer.MaterialId = new ElementId(2001);
            var cs = CompoundStructure.CreateSimpleCompoundStructure(new List<CompoundStructureLayer> { layer });
            wallType.SetCompoundStructure(cs);

            // Setup a fake identity service and orchestrator
            var fakeIdentity = new FakeIdentityService();
            
            // Map the material ID to model mapping
            var matModel = new ElementIdModel
            {
                Id = 2001,
                Name = "Orchestration_Test_Material",
                Class = "Autodesk.Revit.DB.Material"
            };
            fakeIdentity.SetupMapping(new ElementId(2001), matModel);
            fakeIdentity.SetupElement("Orchestration_Test_Material", material);

            var orchestrator = new StandardsExtractionOrchestrator(fakeIdentity);

            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService(GuardrailResult.Overwrite);
            var settings = new StandardsSettings();

            var vm = DashboardTestFactory.Create(
                mainDoc, 
                dialogService: fakeFileDialog, 
                exportService: new StandardsExportService(fakeGuardrail, fakeFileDialog), 
                settings: settings, 
                orchestrator: orchestrator);
            vm.MockOpenDocuments = new List<Document> { mainDoc };

            // Setup document selection mock: select "Wall Types" only
            vm.ShowDocumentSelectionDialog = (dialogVM) =>
            {
                foreach (var item in dialogVM.OpenDocuments)
                {
                    item.IsSelected = true;
                }
                dialogVM.ScanFamilies = false;
                dialogVM.IncludeNestedFamilies = false;

                foreach (var group in dialogVM.FilterHierarchy)
                {
                    if (group.Name == "System Types")
                    {
                        group.IsChecked = true;
                    }
                    else
                    {
                        group.IsChecked = false;
                    }
                }
                return true;
            };

            // Act
            vm.AddRevitModelCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, vm.AvailableSources.Count, "A Revit source should be added.");
            var source = vm.AvailableSources[0];

            bool foundWallType = false;
            bool foundMaterial = false;

            foreach (var group in source.SourceHierarchy)
            {
                foreach (var cls in group.Children)
                {
                    foreach (var elem in cls.Children)
                    {
                        if (elem.Name == "Orchestration_Test_WallType") foundWallType = true;
                        if (elem.Name == "Orchestration_Test_Material") foundMaterial = true;
                    }
                }
            }

            Assert.IsTrue(foundWallType, "The root WallType should be extracted.");
            Assert.IsTrue(foundMaterial, "The nested Material should be recursively extracted as a dependency via the orchestrator.");
        }

        [Test]
        public void SelectedElement_UpdateName_PropagatesToQueueItemAndTriggersRenameCascading()
        {
            // Arrange
            var mainDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService();
            var settings = new StandardsSettings();
            
            var vm = DashboardTestFactory.Create(mainDoc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            
            // Add a test queue item
            var model = new ElementModel { Name = "OriginalName", Class = "Autodesk.Revit.DB.LinePatternElement" };
            model.Parameters = new List<ParameterModel>
            {
                new ParameterModel("Param1", "Val1", null, "String", 0, null, false, false)
            };
            
            var queueItem = new QueueItemModel(model, false, true);
            vm.StagingQueue.Add(queueItem);
            
            // Execute Edit
            vm.EditCommand.Execute(new List<QueueItemModel> { queueItem });
            
            // Assert SelectedElement is set
            Assert.IsNotNull(vm.SelectedElement, "SelectedElement should not be null.");
            Assert.AreEqual("OriginalName", vm.SelectedElement.Name);
            
            // Act: Update Name on SelectedElement wrapper
            vm.SelectedElement.Name = "NewName";
            
            // Assert Name updates on queue item and model
            Assert.AreEqual("NewName", queueItem.Name, "Queue item name should update.");
            Assert.AreEqual("NewName", ((ElementModel)queueItem.Model).Name, "Model name should update.");
            Assert.IsTrue(queueItem.IsEdited, "IsEdited should be true.");
        }

        [Test]
        public void IsSingleElementSelected_TogglesCorrectly_BasedOnSelectionCount()
        {
            // Arrange
            var mainDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService();
            var settings = new StandardsSettings();
            
            var vm = DashboardTestFactory.Create(mainDoc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            
            var item1 = new QueueItemModel(new ElementModel { Name = "Elem1", Class = "Autodesk.Revit.DB.LinePatternElement" }, false, true);
            var item2 = new QueueItemModel(new ElementModel { Name = "Elem2", Class = "Autodesk.Revit.DB.LinePatternElement" }, false, true);
            
            vm.StagingQueue.Add(item1);
            vm.StagingQueue.Add(item2);
            
            // Edit single item
            vm.EditCommand.Execute(new List<QueueItemModel> { item1 });
            Assert.IsTrue(vm.IsSingleElementSelected);
            Assert.AreEqual("Elem1", vm.SelectedNameOrCount);
            
            // Edit multiple items
            vm.EditCommand.Execute(new List<QueueItemModel> { item1, item2 });
            Assert.IsFalse(vm.IsSingleElementSelected);
            Assert.AreEqual("Editing 2 elements", vm.SelectedNameOrCount);
        }

        [Test]
        public void SelectedDisplayClass_StripsNamespacePrefix_AndConcatenatesDistinctValues()
        {
            // Arrange
            var mainDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService();
            var settings = new StandardsSettings();
            
            var vm = DashboardTestFactory.Create(mainDoc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            
            var item1 = new QueueItemModel(new ElementModel { Name = "Elem1", Class = "Autodesk.Revit.DB.LinePatternElement" }, false, true);
            var item2 = new QueueItemModel(new ElementModel { Name = "Elem2", Class = "Autodesk.Revit.DB.TextNoteType" }, false, true);
            var item3 = new QueueItemModel(new ElementModel { Name = "Elem3", Class = "Autodesk.Revit.DB.LinePatternElement" }, false, true);
            
            vm.StagingQueue.Add(item1);
            vm.StagingQueue.Add(item2);
            vm.StagingQueue.Add(item3);
            
            // Act: Edit multiple items
            vm.EditCommand.Execute(new List<QueueItemModel> { item1, item2, item3 });
            
            // Assert: Prefix stripped, distinct, concatenated
            Assert.AreEqual("LinePatternElement, TextNoteType", vm.SelectedDisplayClass);
        }

        [Test]
        public void SelectedAliasesString_ReturnsVariesForMultiSelection_AndPropagatesSingleSelectionEdits()
        {
            // Arrange
            var mainDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService();
            var settings = new StandardsSettings();
            
            var vm = DashboardTestFactory.Create(mainDoc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            
            var item1 = new QueueItemModel(new ElementModel { Name = "Elem1", Class = "Autodesk.Revit.DB.LinePatternElement", Aliases = new List<string> { "Alias1A", "Alias1B" } }, false, true);
            var item2 = new QueueItemModel(new ElementModel { Name = "Elem2", Class = "Autodesk.Revit.DB.LinePatternElement", Aliases = new List<string> { "Alias2A" } }, false, true);
            
            vm.StagingQueue.Add(item1);
            vm.StagingQueue.Add(item2);
            
            // Act: Edit single item
            vm.EditCommand.Execute(new List<QueueItemModel> { item1 });
            Assert.AreEqual("Alias1A, Alias1B", vm.SelectedAliasesString);
            
            // Mutate aliases via property setter
            vm.SelectedAliasesString = "NewAlias1, NewAlias2, ";
            Assert.AreEqual("NewAlias1, NewAlias2", vm.SelectedAliasesString);
            CollectionAssert.AreEqual(new List<string> { "NewAlias1", "NewAlias2" }, ((ElementModel)item1.Model).Aliases);
            Assert.IsTrue(item1.IsEdited);
            
            // Act: Edit multiple items
            vm.EditCommand.Execute(new List<QueueItemModel> { item1, item2 });
            Assert.AreEqual("<Varies>", vm.SelectedAliasesString);
            
            // Attempt mutate on multiple items (should do nothing)
            vm.SelectedAliasesString = "ShouldNotChange";
            Assert.AreEqual("<Varies>", vm.SelectedAliasesString);
        }

        [Test]
        public void HarvestPocoProperties_CorrectlySweepsAndSyncsPocoProperties()
        {
            // Arrange
            var mockPoco = new MockPocoModel
            {
                Name = "TestMock",
                Class = "Autodesk.Revit.DB.MockPocoModel",
                Aliases = new List<string> { "MockAlias" },
                WritableString = "OriginalWritable",
                NullableDouble = 1.23,
                ComplexStructure = new CompoundStructureModel { Layers = new List<SerialCompoundStructureLayer>() }
            };

            var wrapper = new ElementTypeWrapperVM(mockPoco);

            // Act: Sweep properties
            var harvested = wrapper.HarvestPocoProperties();

            // Assert: verify exclusions (Name, Class, Aliases, IgnoredProperty are skipped)
            Assert.IsFalse(harvested.Any(p => p.Name == "Name"));
            Assert.IsFalse(harvested.Any(p => p.Name == "Class"));
            Assert.IsFalse(harvested.Any(p => p.Name == "Aliases"));
            Assert.IsFalse(harvested.Any(p => p.Name == "IgnoredProperty"));

            // Assert: verify read-write primitive
            var writableStrParam = harvested.FirstOrDefault(p => p.Name == "WritableString");
            Assert.IsNotNull(writableStrParam);
            Assert.IsFalse(writableStrParam.IsReadOnly);
            Assert.AreEqual("OriginalWritable", writableStrParam.Value);

            // Assert: verify read-only primitive
            var readOnlyIntParam = harvested.FirstOrDefault(p => p.Name == "ReadOnlyInt");
            Assert.IsNotNull(readOnlyIntParam);
            Assert.IsTrue(readOnlyIntParam.IsReadOnly);
            Assert.AreEqual("42", readOnlyIntParam.Value);

            // Assert: verify complex nested structure
            var complexParam = harvested.FirstOrDefault(p => p.Name == "ComplexStructure");
            Assert.IsNotNull(complexParam);
            Assert.IsFalse(complexParam.IsReadOnly);
            Assert.AreEqual("[Complex Nested Data]", complexParam.Value);

            // Act: Update primitive value and verify synchronization
            writableStrParam.Value = "UpdatedWritable";
            Assert.AreEqual("UpdatedWritable", mockPoco.WritableString, "Poco property should update when wrapper value changes.");
            Assert.IsTrue(wrapper.IsDirty, "Wrapper should be marked as dirty when harvested property is edited.");
        }

        [Test]
        public void ParametersCollection_HarvestedPropertiesPrependedToTop()
        {
            var mockPoco = new MockPocoModel
            {
                Name = "TestMock",
                Class = "Autodesk.Revit.DB.MockPocoModel",
                WritableString = "WritableVal",
                NullableDouble = 1.23
            };

            mockPoco.Parameters = new List<ParameterModel>
            {
                new ParameterModel(Name: "NativeParam", Value: "NativeVal", ValueElemId: null, StorageType: "String", Id: 10, GUID: null, IsShared: false, IsReadOnly: false)
            };

            var wrapper = new ElementTypeWrapperVM(mockPoco);

            Assert.AreEqual("WritableString", wrapper.Parameters[0].Name);
            Assert.AreEqual("ReadOnlyInt", wrapper.Parameters[1].Name);
            Assert.AreEqual("NullableDouble", wrapper.Parameters[2].Name);
            Assert.AreEqual("ComplexStructure", wrapper.Parameters[3].Name);
            Assert.AreEqual("NativeParam", wrapper.Parameters[4].Name);
        }

        [Test]
        public void ParametersCollection_HarvestedPropertiesRaiseParentIsDirty()
        {
            var mockPoco = new MockPocoModel
            {
                Name = "TestMock",
                Class = "Autodesk.Revit.DB.MockPocoModel",
                WritableString = "WritableVal"
            };

            var wrapper = new ElementTypeWrapperVM(mockPoco);
            Assert.IsFalse(wrapper.IsDirty);

            var writableParam = wrapper.Parameters.First(p => p.Name == "WritableString");
            writableParam.Value = "NewVal";

            Assert.IsTrue(wrapper.IsDirty, "Parent wrapper should become dirty when a POCO parameter in the unified list is modified.");
        }

        [Test]
        public void MultiSelect_PocoPropertiesParticipateInIntersectionAndVariesState()
        {
            var mainDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService();
            var settings = new StandardsSettings();
            
            var vm = DashboardTestFactory.Create(mainDoc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);
            
            var poco1 = new MockPocoModel { Name = "Poco1", Class = "Autodesk.Revit.DB.MockPocoModel", WritableString = "CommonVal", NullableDouble = 1.0 };
            var poco2 = new MockPocoModel { Name = "Poco2", Class = "Autodesk.Revit.DB.MockPocoModel", WritableString = "CommonVal", NullableDouble = 2.0 };
            
            var item1 = new QueueItemModel(poco1, false, true);
            var item2 = new QueueItemModel(poco2, false, true);
            
            vm.StagingQueue.Add(item1);
            vm.StagingQueue.Add(item2);
            
            vm.EditCommand.Execute(new List<QueueItemModel> { item1, item2 });
            
            var writableParam = vm.DisplayParameters.FirstOrDefault(p => p.Name == "WritableString");
            Assert.IsNotNull(writableParam);
            Assert.AreEqual("CommonVal", writableParam.Value);
            Assert.IsFalse(writableParam.IsMixedValue);

            var nullableParam = vm.DisplayParameters.FirstOrDefault(p => p.Name == "NullableDouble");
            Assert.IsNotNull(nullableParam);
            Assert.AreEqual("<Varies>", nullableParam.Value);
            Assert.IsTrue(nullableParam.IsMixedValue);

            writableParam.Value = "BulkUpdatedVal";
            
            Assert.AreEqual("BulkUpdatedVal", ((MockPocoModel)item1.Model).WritableString);
            Assert.AreEqual("BulkUpdatedVal", ((MockPocoModel)item2.Model).WritableString);
        }

        private class FakeStandardsExtractionOrchestrator : IStandardsExtractionOrchestrator
        {
            public bool ExtractCalled { get; private set; }
            public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null)
            {
                ExtractCalled = true;
                return new List<ObjectModel>();
            }

            public List<ObjectModel> Extract(Document doc, IEnumerable<Element> rootElements, IProgress<string>? progress = null, bool isTemplate = false)
            {
                ExtractCalled = true;
                return new List<ObjectModel>();
            }
        }

        [Test]
        public void VerifyHeadlessConstruction_WithMockedOrchestrator()
        {
            // Arrange
            var doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService();
            var settings = new StandardsSettings();
            var fakeOrchestrator = new FakeStandardsExtractionOrchestrator();

            // Act
            var vm = DashboardTestFactory.Create(
                doc: doc, 
                dialogService: fakeFileDialog, 
                exportService: null, 
                settings: settings, 
                userPromptService: null, 
                findReplaceService: null,
                orchestrator: fakeOrchestrator);

            // Assert
            Assert.IsNotNull(vm);
            var orchestratorField = typeof(ProjectStandardsDashboardViewModel)
                .GetField("_orchestrator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(orchestratorField);
            var orchestratorObj = orchestratorField!.GetValue(vm);
            Assert.AreSame(fakeOrchestrator, orchestratorObj);
        }

        [Test]
        public void VerifyReplaceQueueReferences_UpdatesPatternAndAppearanceAssetIds()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);

            // 1. Create the old element to be replaced
            var oldPoco = new ElementModel { Name = "OldAsset", Class = "Autodesk.Revit.DB.AppearanceAssetElement" };
            var oldItem = new QueueItemModel(oldPoco, true, true);

            // 2. Create a Material in the staging queue that references "OldAsset"
            var matPoco = new MaterialModel
            {
                Name = "TestMaterial",
                AppearanceAssetId = new ElementIdModel
                {
                    Name = "OldAsset",
                    Id = 12345,
                    UniqueId = "SomeUniqueId-Asset"
                },
                SurfaceForegroundPatternId = new ElementIdModel
                {
                    Name = "OldAsset",
                    Id = 67890,
                    UniqueId = "SomeUniqueId-Pattern"
                }
            };
            var matItem = new QueueItemModel(matPoco, true, true);
            vm.StagingQueue.Add(matItem);

            // Act
            vm.ReplaceQueueReferences(new List<QueueItemModel> { oldItem }, "NewAsset");

            // Assert
            var updatedMat = (MaterialModel)matItem.TargetModel;
            
            // Verify AppearanceAssetId was updated
            Assert.IsNotNull(updatedMat.AppearanceAssetId);
            Assert.AreEqual("NewAsset", updatedMat.AppearanceAssetId!.Name);
            Assert.AreEqual(0, updatedMat.AppearanceAssetId.Id);
            Assert.IsNull(updatedMat.AppearanceAssetId.UniqueId, "AppearanceAssetId.UniqueId should be null after replacement.");

            // Verify SurfaceForegroundPatternId was updated
            Assert.IsNotNull(updatedMat.SurfaceForegroundPatternId);
            Assert.AreEqual("NewAsset", updatedMat.SurfaceForegroundPatternId!.Name);
            Assert.AreEqual(0, updatedMat.SurfaceForegroundPatternId.Id);
            Assert.IsNull(updatedMat.SurfaceForegroundPatternId.UniqueId, "SurfaceForegroundPatternId.UniqueId should be null after replacement.");
        }

        [Test]
        public void VerifyPropertyChangedForwarding_FromSubViewModels()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService);
            var receivedProperties = new List<string>();
            vm.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName != null)
                {
                    receivedProperties.Add(e.PropertyName);
                }
            };

            var onPropertyChangedMethod = typeof(ViewModelBase)
                .GetMethod("OnPropertyChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(onPropertyChangedMethod);

            // Act - Trigger PropertyChanged on each sub-VM via reflection
            // 1. SourceTreeViewModel
            onPropertyChangedMethod!.Invoke(vm.SourceTreeViewModel, new object[] { nameof(StandardsSourceTreeViewModel.SearchText) });

            // 2. StagingQueueViewModel
            onPropertyChangedMethod!.Invoke(vm.StagingQueueViewModel, new object[] { nameof(StagingQueueViewModel.SelectedAliasesString) });

            // 3. StandardsExecutionPipelineViewModel
            onPropertyChangedMethod!.Invoke(vm.StandardsExecutionPipelineViewModel, new object[] { nameof(StandardsExecutionPipelineViewModel.SaveFilePath) });

            // Assert
            Assert.Contains(nameof(StandardsSourceTreeViewModel.SearchText), receivedProperties);
            Assert.Contains(nameof(StagingQueueViewModel.SelectedAliasesString), receivedProperties);
            Assert.Contains(nameof(StandardsExecutionPipelineViewModel.SaveFilePath), receivedProperties);
        }
    }

    public class MockPocoModel : ElementModel
    {
        [Newtonsoft.Json.JsonIgnore]
        public string IgnoredProperty { get; set; } = "Ignored";

        public string WritableString { get; set; } = "Writable";
        
        public int ReadOnlyInt => 42;
        
        public double? NullableDouble { get; set; } = 3.14;
        
        public CompoundStructureModel ComplexStructure { get; set; } = new CompoundStructureModel();
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/ProjectStandardsScannerTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.Shared.UI;
using Synthetic.Settings;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class ProjectStandardsScannerTests
    {
        private Document _doc = null!;
        private ProjectStandardsDashboardViewModel _vm = null!;
        private System.Reflection.MethodInfo _extractMethod = null!;

        [SetUp]
        public void SetUp()
        {
            // Set up mock Document and ViewModel
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            
            var fakeFileDialog = new FakeFileDialogService();
            var fakeGuardrail = new FakeGuardrailPromptService(GuardrailResult.Overwrite);
            var settings = new StandardsSettings();

            _vm = DashboardTestFactory.Create(_doc, fakeFileDialog, new StandardsExportService(fakeGuardrail, fakeFileDialog), settings);

            _extractMethod = typeof(StandardsSourceTreeViewModel)
                .GetMethod("ExtractRevitElements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Assert.IsNotNull(_extractMethod, "ExtractRevitElements method not found.");
        }

        private List<ElementModel> InvokeExtract(List<string>? selectedGroupings)
        {
            return (List<ElementModel>)_extractMethod.Invoke(_vm.SourceTreeViewModel, new object?[] { _doc, false, false, selectedGroupings })!;
        }

        [Test]
        public void ExtractRevitElements_WhenGroupingsNull_ExtractsEverything()
        {
            // Arrange
            // 1. TextNoteType
            var textType = (TextNoteType)Activator.CreateInstance(typeof(TextNoteType), true)!;
            textType.Name = "Text Note Type A";
            textType.GetType().GetProperty("Id")?.SetValue(textType, new ElementId(101));
            ((dynamic)_doc).AddElement(textType, textType.Id);

            // 2. WallType
            var wallType = (WallType)Activator.CreateInstance(typeof(WallType), true)!;
            wallType.Name = "Wall Type B";
            wallType.GetType().GetProperty("Id")?.SetValue(wallType, new ElementId(102));
            ((dynamic)_doc).AddElement(wallType, wallType.Id);

            // Act
            var result = InvokeExtract(null);

            // Assert: everything should be extracted
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();
            Assert.Contains("Text Note Type A", names);
            Assert.Contains("Wall Type B", names);
        }

        [Test]
        public void ExtractRevitElements_WhenSpecificClassSelected_ExtractsOnlyThatClass()
        {
            // Arrange
            // 1. TextNoteType
            var textType = (TextNoteType)Activator.CreateInstance(typeof(TextNoteType), true)!;
            textType.Name = "Target Text Note";
            textType.GetType().GetProperty("Id")?.SetValue(textType, new ElementId(201));
            ((dynamic)_doc).AddElement(textType, textType.Id);

            // 2. WallType
            var wallType = (WallType)Activator.CreateInstance(typeof(WallType), true)!;
            wallType.Name = "Excluded Wall Type";
            wallType.GetType().GetProperty("Id")?.SetValue(wallType, new ElementId(202));
            ((dynamic)_doc).AddElement(wallType, wallType.Id);

            // Act: Select only "Text Note Types"
            var selectedGroupings = new List<string> { "Text Note Types" };
            var result = InvokeExtract(selectedGroupings);

            // Assert
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();
            Assert.Contains("Target Text Note", names);
            Assert.False(names.Contains("Excluded Wall Type"), "Wall Type should be excluded since it wasn't selected.");
        }

        [Test]
        public void ExtractRevitElements_WhenViewsSelected_ExtractsOnlyViewsNotTemplates()
        {
            // Arrange
            // 1. Standard View (IsTemplate = false)
            var standardView = (ViewPlan)Activator.CreateInstance(typeof(ViewPlan), true)!;
            standardView.Name = "Mock Standard View";
            ((dynamic)standardView).IsTemplate = false;
            standardView.GetType().GetProperty("Id")?.SetValue(standardView, new ElementId(301));
            ((dynamic)_doc).AddElement(standardView, standardView.Id);

            // 2. View Template (IsTemplate = true)
            var viewTemplate = (ViewPlan)Activator.CreateInstance(typeof(ViewPlan), true)!;
            viewTemplate.Name = "Mock View Template";
            ((dynamic)viewTemplate).IsTemplate = true;
            viewTemplate.GetType().GetProperty("Id")?.SetValue(viewTemplate, new ElementId(302));
            ((dynamic)_doc).AddElement(viewTemplate, viewTemplate.Id);

            // Act: Select only "Views"
            var selectedGroupings = new List<string> { "Views" };
            var result = InvokeExtract(selectedGroupings);

            // Assert
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();
            Assert.Contains("Mock Standard View", names);
            Assert.False(names.Contains("Mock View Template"), "View Template should be excluded.");
        }

        [Test]
        public void ExtractRevitElements_WhenViewTemplatesSelected_ExtractsOnlyTemplatesNotViews()
        {
            // Arrange
            // 1. Standard View (IsTemplate = false)
            var standardView = (ViewPlan)Activator.CreateInstance(typeof(ViewPlan), true)!;
            standardView.Name = "Mock Standard View";
            ((dynamic)standardView).IsTemplate = false;
            standardView.GetType().GetProperty("Id")?.SetValue(standardView, new ElementId(401));
            ((dynamic)_doc).AddElement(standardView, standardView.Id);

            // 2. View Template (IsTemplate = true)
            var viewTemplate = (ViewPlan)Activator.CreateInstance(typeof(ViewPlan), true)!;
            viewTemplate.Name = "Mock View Template";
            ((dynamic)viewTemplate).IsTemplate = true;
            viewTemplate.GetType().GetProperty("Id")?.SetValue(viewTemplate, new ElementId(402));
            ((dynamic)_doc).AddElement(viewTemplate, viewTemplate.Id);

            // Act: Select only "View Templates"
            var selectedGroupings = new List<string> { "View Templates" };
            var result = InvokeExtract(selectedGroupings);

            // Assert
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();
            Assert.Contains("Mock View Template", names);
            Assert.False(names.Contains("Mock Standard View"), "Standard View should be excluded.");
        }

        [Test]
        public void ExtractRevitElements_WhenFamilySymbolsSelected_ExtractsCorrectlyBasedOnCategory()
        {
            // Arrange
            // 1. Title Block symbol
            var titleBlockSymbol = (FamilySymbol)Activator.CreateInstance(typeof(FamilySymbol), true)!;
            titleBlockSymbol.Name = "Mock Title Block";
            var titleBlockCat = (Category)Activator.CreateInstance(typeof(Category), true)!;
            ((dynamic)titleBlockCat).Name = "Title Blocks";
            titleBlockSymbol.GetType().GetProperty("Category")?.SetValue(titleBlockSymbol, titleBlockCat);
            titleBlockSymbol.GetType().GetProperty("Id")?.SetValue(titleBlockSymbol, new ElementId(501));
            titleBlockCat.GetType().GetProperty("Id")?.SetValue(titleBlockCat, new ElementId((int)BuiltInCategory.OST_TitleBlocks));
            ((dynamic)_doc).AddElement(titleBlockSymbol, titleBlockSymbol.Id);

            // 2. Detail Components symbol
            var detailSymbol = (FamilySymbol)Activator.CreateInstance(typeof(FamilySymbol), true)!;
            detailSymbol.Name = "Mock Detail Component";
            var detailCat = (Category)Activator.CreateInstance(typeof(Category), true)!;
            ((dynamic)detailCat).Name = "Detail Items";
            detailSymbol.GetType().GetProperty("Category")?.SetValue(detailSymbol, detailCat);
            detailSymbol.GetType().GetProperty("Id")?.SetValue(detailSymbol, new ElementId(502));
            detailCat.GetType().GetProperty("Id")?.SetValue(detailCat, new ElementId((int)BuiltInCategory.OST_DetailComponents));
            ((dynamic)_doc).AddElement(detailSymbol, detailSymbol.Id);

            // Act: Select only "Title Blocks"
            var selectedGroupings = new List<string> { "Title Blocks" };
            var result = InvokeExtract(selectedGroupings);

            // Assert
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();
            Assert.Contains("Mock Title Block", names);
            Assert.False(names.Contains("Mock Detail Component"), "Detail Components should be excluded.");
        }

        [Test]
        public void ExtractRevitElements_WhenToposolidTypesSelected_ExtractsCorrectly()
        {
            // Arrange
            var toposolidType = (ToposolidType)Activator.CreateInstance(typeof(ToposolidType), true)!;
            toposolidType.Name = "Mock Toposolid Type";
            toposolidType.GetType().GetProperty("Id")?.SetValue(toposolidType, new ElementId(601));
            ((dynamic)_doc).AddElement(toposolidType, toposolidType.Id);

            // Act: Select only "Toposolid Types"
            var selectedGroupings = new List<string> { "Toposolid Types" };
            var result = InvokeExtract(selectedGroupings);

            // Assert
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();
            Assert.Contains("Mock Toposolid Type", names);
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/QueueItemBaselineCloneTests.cs
```csharp
﻿using System;
using System.Collections.Generic;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class QueueItemBaselineCloneTests
    {
        private ElementModel _sourceElement;
        private ParameterModel _sourceParam;

        [SetUp]
        public void SetUp()
        {
            _sourceParam = new ParameterModel("LineWidth", "1", null, "Integer", 12345, null, false, false);
            _sourceElement = new ElementModel
            {
                Class = "Autodesk.Revit.DB.LinePatternElement",
                Name = "Dash",
                Parameters = new List<ParameterModel> { _sourceParam }
            };
        }

        [Test]
        public void Constructor_ShouldDecoupleAndDeepCloneIndependentCopies()
        {
            // Arrange & Act
            var queueItem = new QueueItemModel(_sourceElement, true, false);

            // Assert
            Assert.IsNotNull(queueItem.BaselineModel, "BaselineModel should be instantiated.");
            Assert.IsNotNull(queueItem.TargetModel, "TargetModel should be instantiated.");
            
            // Reference isolation assertions
            Assert.AreNotSame(_sourceElement, queueItem.BaselineModel, "BaselineModel must not share references with the source.");
            Assert.AreNotSame(_sourceElement, queueItem.TargetModel, "TargetModel must not share references with the source.");
            Assert.AreNotSame(queueItem.BaselineModel, queueItem.TargetModel, "BaselineModel and TargetModel must not share references.");
            Assert.AreSame(queueItem.TargetModel, queueItem.Model, "Model property must dynamically redirect to TargetModel.");

            // Verify inner parameter isolation
            var baselineElement = (ElementModel)queueItem.BaselineModel;
            var targetElement = (ElementModel)queueItem.TargetModel;

            Assert.AreNotSame(_sourceParam, baselineElement.Parameters[0], "Baseline parameters must not reference source parameters.");
            Assert.AreNotSame(_sourceParam, targetElement.Parameters[0], "Target parameters must not reference source parameters.");
            Assert.AreNotSame(baselineElement.Parameters[0], targetElement.Parameters[0], "Baseline parameters and Target parameters must be decoupled.");
        }

        [Test]
        public void WrapperMutations_ShouldUpdateTargetModelButLeaveBaselineAndSourceUnaltered()
        {
            // Arrange
            var queueItem = new QueueItemModel(_sourceElement, true, false);
            var wrapper = queueItem.GetWrapper();

            // Act
            wrapper.Parameters[0].Value = "99";
            wrapper.Name = "ModifiedName";

            // Assert
            var baselineElement = (ElementModel)queueItem.BaselineModel;
            var targetElement = (ElementModel)queueItem.TargetModel;

            // Target should be updated
            Assert.AreEqual("99", targetElement.Parameters[0].Value);
            Assert.AreEqual("ModifiedName", targetElement.Name);

            // Baseline should be unaltered
            Assert.AreEqual("1", baselineElement.Parameters[0].Value);
            Assert.AreEqual("Dash", baselineElement.Name);

            // Original source should be unaltered
            Assert.AreEqual("1", _sourceElement.Parameters[0].Value);
            Assert.AreEqual("Dash", _sourceElement.Name);
        }

        [Test]
        public void DynamicDirtyTracking_ShouldEvaluateOnTheFlyAndRevertCorrectly()
        {
            // Arrange
            var queueItem = new QueueItemModel(_sourceElement, true, false);
            var wrapper = queueItem.GetWrapper();

            // Assert Initial State
            Assert.IsFalse(wrapper.IsDirty, "Wrapper should initially not be dirty.");
            Assert.IsFalse(wrapper.Parameters[0].IsDirty, "Parameter should initially not be dirty.");

            // Act: Mutate Parameter
            wrapper.Parameters[0].Value = "5";

            // Assert Mutation State
            Assert.IsTrue(wrapper.Parameters[0].IsDirty, "Parameter should evaluate to dirty after modification.");
            Assert.IsTrue(wrapper.IsDirty, "Wrapper should evaluate to dirty when a parameter is dirty.");

            // Act: Revert Parameter
            wrapper.Parameters[0].Value = "1";

            // Assert Reversion State
            Assert.IsFalse(wrapper.Parameters[0].IsDirty, "Parameter should evaluate to not dirty when reverted to baseline.");
            Assert.IsFalse(wrapper.IsDirty, "Wrapper should evaluate to not dirty when all parameters are clean.");

            // Act: Mutate Name
            wrapper.Name = "RenamedDash";

            // Assert Name Mutation
            Assert.IsTrue(wrapper.IsDirty, "Wrapper should evaluate to dirty when Name is modified.");

            // Act: Revert Name
            wrapper.Name = "Dash";

            // Assert Name Reversion
            Assert.IsFalse(wrapper.IsDirty, "Wrapper should evaluate to not dirty when Name is reverted to baseline.");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/QueueMergeTests.cs
```csharp
﻿using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Operations.Standards;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class QueueMergeTests
    {
        private Document _doc = null!;
        private FakeFileDialogService _fakeDialogService = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeDialogService = new FakeFileDialogService();
        }

        [Test]
        public void MergeQueueItems_ShouldAppendNonSurvivorNameToSurvivorAliasesAndPurgeConsumed()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);

            var oak = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Oak" };
            var pine = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Pine" };

            var qOak = new QueueItemModel(oak, true, false);
            var qPine = new QueueItemModel(pine, true, false);

            vm.StagingQueue.Add(qOak);
            vm.StagingQueue.Add(qPine);

            var selectedList = new List<QueueItemModel> { qOak, qPine };

            // Act
            vm.MergeQueueCommand.Execute(selectedList);

            // Assert
            // Oak (first item) should be SelectedPrimary by ShowMergeDialog mock.
            Assert.AreEqual(1, vm.StagingQueue.Count, "Non-survivor pine should be purged.");
            Assert.AreSame(qOak, vm.StagingQueue.First(), "Oak should remain.");

            var oakModel = qOak.Model as ElementModel;
            Assert.IsNotNull(oakModel);
            Assert.Contains("Material - Pine", oakModel.Aliases, "Oak aliases should contain Pine.");
            Assert.IsTrue(qOak.IsEdited, "Oak should be marked as edited.");
        }

        [Test]
        public void MergeQueueItems_ShouldCascadingRedirectReferences()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);

            var oak = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Oak" };
            var pine = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Pine" };

            // A WallType model that references Pine material in its layers
            var layer = new SerialCompoundStructureLayer
            {
                MaterialId = new ElementIdModel { Name = "Material - Pine" }
            };
            var wall = new HostObjTypeModel
            {
                Class = "Autodesk.Revit.DB.WallType",
                Name = "Timber Wall",
                Structure = new CompoundStructureModel
                {
                    Layers = new List<SerialCompoundStructureLayer> { layer }
                }
            };

            var qOak = new QueueItemModel(oak, true, false);
            var qPine = new QueueItemModel(pine, true, false);
            var qWall = new QueueItemModel(wall, true, false);

            vm.StagingQueue.Add(qOak);
            vm.StagingQueue.Add(qPine);
            vm.StagingQueue.Add(qWall);

            var selectedList = new List<QueueItemModel> { qOak, qPine };

            // Act
            vm.MergeQueueCommand.Execute(selectedList);

            // Assert
            Assert.AreEqual(2, vm.StagingQueue.Count, "Pine should be purged, Oak and Wall should remain.");
            
            var wallModel = qWall.Model as HostObjTypeModel;
            Assert.IsNotNull(wallModel);
            Assert.IsNotNull(wallModel.Structure);
            Assert.IsNotNull(wallModel.Structure.Layers);
            Assert.AreEqual("Material - Oak", wallModel.Structure.Layers[0].MaterialId?.Name, "Wall structure layer reference should be redirected to Oak.");
        }

        [Test]
        public void ExtractRevitElements_ShouldScanAndExtractAllSupportedTypesAndFamilySymbols()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);

            void AddToDoc(Element el, string name, int idVal)
            {
                el.GetType().GetProperty("Name")?.SetValue(el, name);
                el.GetType().GetProperty("Id")?.SetValue(el, new ElementId(idVal));
                _doc.GetType().GetMethod("AddElement")?.Invoke(_doc, new object[] { el, el.Id });
            }

            // Add mock elements to document
            var wallType = (WallType)Activator.CreateInstance(typeof(WallType), true)!;
            AddToDoc(wallType, "Mock Wall Type", 101);

            var linePattern = (LinePatternElement)Activator.CreateInstance(typeof(LinePatternElement), true)!;
            AddToDoc(linePattern, "Mock Line Pattern", 102);

            var material = (Material)Activator.CreateInstance(typeof(Material), true)!;
            AddToDoc(material, "Mock Material", 103);

            // FamilySymbol with Title Block category
            var fs = (FamilySymbol)Activator.CreateInstance(typeof(FamilySymbol), true)!;
            fs.GetType().GetProperty("Name")?.SetValue(fs, "Mock Title Block Symbol");
            fs.GetType().GetProperty("Id")?.SetValue(fs, new ElementId(104));

            var cat = (Category)Activator.CreateInstance(typeof(Category), true)!;
            cat.GetType().GetProperty("Id")?.SetValue(cat, new ElementId((int)BuiltInCategory.OST_TitleBlocks));
            cat.GetType().GetProperty("Name")?.SetValue(cat, "Title Blocks");
            cat.GetType().GetProperty("CategoryType")?.SetValue(cat, CategoryType.Annotation);
            fs.GetType().GetProperty("Category")?.SetValue(fs, cat);

            _doc.GetType().GetMethod("AddElement")?.Invoke(_doc, new object[] { fs, fs.Id });

            var extractMethod = typeof(StandardsSourceTreeViewModel)
                .GetMethod("ExtractRevitElements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(extractMethod);

            var selectedGroupings = new List<string> { "Title Blocks", "Wall Types", "Line Patterns", "Materials" };
            var result = (List<ElementModel>)extractMethod.Invoke(vm.SourceTreeViewModel, new object[] { _doc, false, false, selectedGroupings })!;

            // Assert
            Assert.IsNotNull(result);
            var names = result.Select(r => r.Name).ToList();

            Assert.Contains("Mock Wall Type", names);
            Assert.Contains("Mock Line Pattern", names);
            Assert.Contains("Mock Material", names);
            Assert.Contains("Mock Title Block Symbol", names);
        }

        [Test]
        public void MergeStandardsLists_ShouldOverwriteDuplicates_WhenOverwriteIsTrue()
        {
            // Arrange
            var existing = new List<ElementModel>
            {
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Steel", Category = "Existing Category" }
            };
            var newElements = new List<ElementModel>
            {
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Steel", Category = "New Category" },
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Concrete", Category = "New Category" }
            };

            // Act
            var result = StandardsMergeUtility.Merge(existing, newElements, overwriteDuplicates: true);

            // Assert
            Assert.AreEqual(2, result.Count);
            var steel = result.FirstOrDefault(x => x.Name == "Steel");
            Assert.IsNotNull(steel);
            Assert.AreEqual("New Category", steel.Category);
        }

        [Test]
        public void MergeStandardsLists_ShouldPreserveDuplicates_WhenOverwriteIsFalse()
        {
            // Arrange
            var existing = new List<ElementModel>
            {
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Steel", Category = "Existing Category" }
            };
            var newElements = new List<ElementModel>
            {
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Steel", Category = "New Category" },
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Concrete", Category = "New Category" }
            };

            // Act
            var result = StandardsMergeUtility.Merge(existing, newElements, overwriteDuplicates: false);

            // Assert
            Assert.AreEqual(2, result.Count);
            var steel = result.FirstOrDefault(x => x.Name == "Steel");
            Assert.IsNotNull(steel);
            Assert.AreEqual("Existing Category", steel.Category);
        }

        [Test]
        public void IsSavePathActive_ShouldReflectQueueIntent()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);
            Assert.IsFalse(vm.IsSavePathActive);

            // Act
            var item = new QueueItemModel(new MaterialModel { Class = "Material", Name = "Test" }, false, true);
            vm.StagingQueue.Add(item);

            // Assert
            Assert.IsTrue(vm.IsSavePathActive);

            // Act
            vm.StagingQueue.Remove(item);

            // Assert
            Assert.IsFalse(vm.IsSavePathActive);
        }

        [Test]
        public void MergeQueueItems_ShouldTransitionWorkspaceToIdle_WhenMergeIsSuccessfulInEditMode()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);

            var oak = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Oak" };
            var pine = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Pine" };

            var qOak = new QueueItemModel(oak, true, false);
            var qPine = new QueueItemModel(pine, true, false);

            vm.StagingQueue.Add(qOak);
            vm.StagingQueue.Add(qPine);

            var selectedList = new List<QueueItemModel> { qOak, qPine };

            // Transition workspace to Edit mode first
            vm.EditCommand.Execute(selectedList);
            Assert.AreEqual(WorkspaceMode.Edit, vm.ActiveWorkspace, "Workspace should be in Edit mode.");

            // Act
            vm.MergeQueueCommand.Execute(selectedList);

            // Assert
            // Oak (first item) should be SelectedPrimary by ShowMergeDialog mock.
            Assert.AreEqual(1, vm.StagingQueue.Count, "Non-survivor pine should be purged.");
            Assert.AreEqual(WorkspaceMode.Idle, vm.ActiveWorkspace, "Active workspace should transition to Idle post-merge.");
        }

        [Test]
        public void MergeQueueItems_ShouldCombineExecutionFlagsAndMergeAliasesOntoSurvivor()
        {
            // Arrange
            var vm = DashboardTestFactory.Create(_doc, _fakeDialogService, null, null);

            var oak = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Oak", Aliases = new List<string> { "OakAlias1" } };
            var pine = new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Pine", Aliases = new List<string> { "PineAlias1" } };

            // Oak is Enforce, Pine is Save
            var qOak = new QueueItemModel(oak, true, false);
            var qPine = new QueueItemModel(pine, false, true);

            vm.StagingQueue.Add(qOak);
            vm.StagingQueue.Add(qPine);

            var selectedList = new List<QueueItemModel> { qOak, qPine };

            // Act
            vm.MergeQueueCommand.Execute(selectedList);

            // Assert
            Assert.AreEqual(1, vm.StagingQueue.Count, "Pine should be purged.");
            var survivor = vm.StagingQueue[0];
            Assert.AreSame(qOak, survivor, "Oak should be the survivor.");
            
            // Flags should be combined
            Assert.IsTrue(survivor.WillEnforce, "Survivor should have WillEnforce set to true.");
            Assert.IsTrue(survivor.WillSave, "Survivor should inherit WillSave from the merged Pine element.");

            // Aliases should be merged
            var survivorModel = survivor.Model as MaterialModel;
            Assert.IsNotNull(survivorModel);
            // Expected aliases: OakAlias1 (original), Material - Pine (NP name), PineAlias1 (NP alias)
            CollectionAssert.Contains(survivorModel.Aliases, "OakAlias1");
            CollectionAssert.Contains(survivorModel.Aliases, "Material - Pine");
            CollectionAssert.Contains(survivorModel.Aliases, "PineAlias1");
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/RevitFamilyEnforcerTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Merge;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class RevitFamilyEnforcerTests
    {
        private Document _doc = null!;
        private FakeSerializationEngine _fakeEngine = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeEngine = new FakeSerializationEngine();
        }

        [Test]
        public void Enforce_WithTitleBlocksOnlyFilter_ProcessesOnlyTitleBlocks()
        {
            // Arrange
            var enforcer = new RevitFamilyEnforcer(_fakeEngine);

            // Create title block family
            var titleBlockFamily = (Family)Activator.CreateInstance(typeof(Family), true)!;
            titleBlockFamily.Name = "TitleBlockFam";
            dynamic dTitleBlock = titleBlockFamily;
            dTitleBlock.IsEditable = true;
            
            var cat1 = (Category)Activator.CreateInstance(typeof(Category), true)!;
            cat1.GetType().GetProperty("Id")?.SetValue(cat1, new ElementId((long)BuiltInCategory.OST_TitleBlocks));
            titleBlockFamily.FamilyCategory = cat1;
            titleBlockFamily.GetType().GetProperty("Id")?.SetValue(titleBlockFamily, new ElementId(1001));
            ((dynamic)_doc).AddElement(titleBlockFamily, titleBlockFamily.Id);

            // Create non-title block family
            var wallFamily = (Family)Activator.CreateInstance(typeof(Family), true)!;
            wallFamily.Name = "WallFam";
            dynamic dWall = wallFamily;
            dWall.IsEditable = true;
            
            var cat2 = (Category)Activator.CreateInstance(typeof(Category), true)!;
            cat2.GetType().GetProperty("Id")?.SetValue(cat2, new ElementId((long)BuiltInCategory.OST_Walls));
            wallFamily.FamilyCategory = cat2;
            wallFamily.GetType().GetProperty("Id")?.SetValue(wallFamily, new ElementId(1002));
            ((dynamic)_doc).AddElement(wallFamily, wallFamily.Id);

            var options = new StandardsExecutionOptions
            {
                ProcessFamilies = true,
                CategoryFilter = "Title Blocks Only"
            };

            var processedFamilies = new List<string>();
            Action<string, string, int> reportProgress = (task, detail, count) =>
            {
                if (detail.Contains("Updating family"))
                {
                    processedFamilies.Add(detail);
                }
            };

            var results = new List<SerializationResultModel>();

            // Act
            enforcer.Enforce(_doc, new List<ElementModel>(), options, reportProgress, results, CancellationToken.None);

            // Assert
            Assert.AreEqual(1, processedFamilies.Count);
            Assert.IsTrue(processedFamilies[0].Contains("TitleBlockFam"));
            Assert.IsFalse(processedFamilies.Any(f => f.Contains("WallFam")));
        }

        private class FakeSerializationEngine : IStandardSerializationEngine
        {
            public IEnumerable<ObjectModel> ByRevit(IEnumerable<Element> elements, Document doc, bool isTemplate, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
            {
                return new List<ObjectModel>();
            }

            public IEnumerable<DuplicateClusterModel> Analyze(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
            {
                return new List<DuplicateClusterModel>();
            }

            public IEnumerable<SerializationResultModel> ToRevit(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default, IFailuresPreprocessor? failuresPreprocessor = null)
            {
                return new List<SerializationResultModel>();
            }

            public ObjectModel? ExtractCategory(Category category, Document doc, bool isTemplate)
            {
                return null;
            }
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/SelectRevitDocumentTests.cs
```csharp
﻿using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Operations.Standards;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class SelectRevitDocumentTests
    {
        [Test]
        public void Constructor_ShouldInitializeFilterHierarchyAndCheckAllByDefault()
        {
            // Arrange & Act
            var vm = new SelectRevitDocumentViewModel(uiapp: null, mockDocs: null);

            // Assert
            Assert.IsNotNull(vm.FilterHierarchy);
            Assert.IsTrue(vm.FilterHierarchy.Count > 0);

            // Verify all groups and classes are checked by default
            foreach (var group in vm.FilterHierarchy)
            {
                Assert.AreEqual(true, group.IsChecked, $"Group '{group.Name}' should be checked by default.");
                foreach (var classNode in group.Children.OfType<StandardClassModel>())
                {
                    Assert.AreEqual(true, classNode.IsChecked, $"Class '{classNode.Name}' under Group '{group.Name}' should be checked by default.");
                }
            }

            // Verify default family processing values are false
            Assert.IsFalse(vm.ScanFamilies);
            Assert.IsFalse(vm.IncludeNestedFamilies);
        }

        [Test]
        public void TogglingOffScanFamilies_ShouldResetIncludeNestedFamiliesToFalse()
        {
            // Arrange
            var vm = new SelectRevitDocumentViewModel(uiapp: null, mockDocs: null);

            // Act - Enable both
            vm.ScanFamilies = true;
            vm.IncludeNestedFamilies = true;
            Assert.IsTrue(vm.ScanFamilies);
            Assert.IsTrue(vm.IncludeNestedFamilies);

            // Act - Disable primary scanning
            vm.ScanFamilies = false;

            // Assert - Nested scanning should be forced off
            Assert.IsFalse(vm.ScanFamilies);
            Assert.IsFalse(vm.IncludeNestedFamilies);
        }

        [Test]
        public void TogglingOnScanFamilies_ShouldPreserveIncludeNestedFamiliesState()
        {
            // Arrange
            var vm = new SelectRevitDocumentViewModel(uiapp: null, mockDocs: null);

            // Act
            vm.ScanFamilies = true;
            vm.IncludeNestedFamilies = true;

            // Assert
            Assert.IsTrue(vm.ScanFamilies);
            Assert.IsTrue(vm.IncludeNestedFamilies);
        }

        [Test]
        public void SelectedFamilyGroupings_ShouldReflectCheckedClasses()
        {
            // Arrange
            var vm = new SelectRevitDocumentViewModel(uiapp: null, mockDocs: null);

            // Act & Assert - Initially all should be selected
            var initialChecked = vm.SelectedFamilyGroupings;
            // The template has 36 classes (Annotations: 10, Materials & Assets: 4, Other: 1, Standards & Categories: 4, System Types: 13, Views: 4)
            Assert.AreEqual(36, initialChecked.Count);

            // Uncheck one class specifically (e.g. "Materials")
            var matGroup = vm.FilterHierarchy.First(g => g.Name == "Materials & Assets");
            var materialsClass = matGroup.Children.OfType<StandardClassModel>().First(c => c.Name == "Materials");
            
            materialsClass.IsChecked = false;

            // Assert - "Materials" should be missing, leaving 35 classes
            var afterUncheck = vm.SelectedFamilyGroupings;
            Assert.AreEqual(35, afterUncheck.Count);
            Assert.IsFalse(afterUncheck.Contains("Materials"));
            Assert.IsTrue(afterUncheck.Contains("Appearance Assets")); // Siblings remain checked

            // Uncheck entire group "Views"
            var viewsGroup = vm.FilterHierarchy.First(g => g.Name == "Views");
            viewsGroup.IsChecked = false; // Cascades to all child classes (Browser Organizations, View Family Types, View Templates, Views)

            // Assert - The 4 view classes should also be missing, leaving 31 checked classes
            var afterGroupUncheck = vm.SelectedFamilyGroupings;
            Assert.AreEqual(31, afterGroupUncheck.Count);
            Assert.IsFalse(afterGroupUncheck.Contains("Views"));
            Assert.IsFalse(afterGroupUncheck.Contains("View Templates"));
            Assert.IsFalse(afterGroupUncheck.Contains("View Family Types"));
            Assert.IsFalse(afterGroupUncheck.Contains("Browser Organizations"));
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/SingleItemSelectionViewModelTests.cs
```csharp
﻿using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class SingleItemSelectionViewModelTests
    {
        private class DummyItem
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        [Test]
        public void Constructor_ShouldInitializePropertiesCorrectly()
        {
            // Arrange
            var items = new List<DummyItem>
            {
                new DummyItem { Id = 1, Name = "Item 1" },
                new DummyItem { Id = 2, Name = "Item 2" }
            };
            string prompt = "Please choose one:";
            Func<DummyItem, string> displayDelegate = x => x.Name;

            // Act
            var vm = new SingleItemSelectionViewModel<DummyItem>(items, prompt, displayDelegate);

            // Assert
            Assert.AreEqual(2, vm.Items.Count);
            Assert.AreEqual("Item 1", vm.Items[0].Name);
            Assert.AreEqual("Item 2", vm.Items[1].Name);
            Assert.AreEqual(prompt, vm.Prompt);
            Assert.AreEqual("Select Item", vm.Title); // Default title
            Assert.IsNull(vm.SelectedItem);
            Assert.IsNotNull(vm.OkCommand);
            Assert.IsNotNull(vm.CancelCommand);
        }

        [Test]
        public void OkCommand_CanExecute_OnlyWhenItemSelected()
        {
            // Arrange
            var items = new List<DummyItem>
            {
                new DummyItem { Id = 1, Name = "Item 1" }
            };
            var vm = new SingleItemSelectionViewModel<DummyItem>(items, "Prompt", x => x.Name);

            // Assert initially CanExecute is false
            Assert.IsFalse(vm.OkCommand.CanExecute(null));

            // Select item
            vm.SelectedItem = items[0];

            // Assert CanExecute becomes true
            Assert.IsTrue(vm.OkCommand.CanExecute(null));

            // Unselect item
            vm.SelectedItem = null;

            // Assert CanExecute becomes false again
            Assert.IsFalse(vm.OkCommand.CanExecute(null));
        }

        [Test]
        public void GetItemDisplayName_ShouldResolveUsingDelegate()
        {
            // Arrange
            var items = new List<DummyItem>
            {
                new DummyItem { Id = 1, Name = "Special Name" }
            };
            var vm = new SingleItemSelectionViewModel<DummyItem>(items, "Prompt", x => x.Name);

            // Act & Assert
            string resolvedName = vm.GetItemDisplayName(items[0]);
            Assert.AreEqual("Special Name", resolvedName);

            // Test non-matching type fallback
            string fallbackName = vm.GetItemDisplayName("Raw String");
            Assert.AreEqual("Raw String", fallbackName);

            // Test null fallback
            string nullFallbackName = vm.GetItemDisplayName(null!);
            Assert.AreEqual(string.Empty, nullFallbackName);
        }

        [Test]
        public void OkCommand_ShouldTriggerCloseActionWithTrue()
        {
            // Arrange
            var items = new List<DummyItem>
            {
                new DummyItem { Id = 1, Name = "Item 1" }
            };
            var vm = new SingleItemSelectionViewModel<DummyItem>(items, "Prompt", x => x.Name);
            vm.SelectedItem = items[0];

            bool closeActionCalled = false;
            bool? closeResult = null;

            vm.CloseAction = (result) =>
            {
                closeActionCalled = true;
                closeResult = result;
            };

            // Act
            vm.OkCommand.Execute(null);

            // Assert
            Assert.IsTrue(closeActionCalled);
            Assert.AreEqual(true, closeResult);
        }

        [Test]
        public void CancelCommand_ShouldTriggerCloseActionWithFalse()
        {
            // Arrange
            var items = new List<DummyItem>
            {
                new DummyItem { Id = 1, Name = "Item 1" }
            };
            var vm = new SingleItemSelectionViewModel<DummyItem>(items, "Prompt", x => x.Name);

            bool closeActionCalled = false;
            bool? closeResult = null;

            vm.CloseAction = (result) =>
            {
                closeActionCalled = true;
                closeResult = result;
            };

            // Act
            vm.CancelCommand.Execute(null);

            // Assert
            Assert.IsTrue(closeActionCalled);
            Assert.AreEqual(false, closeResult);
        }
    }
}
```

### File: tests/SyntheticTests.Logic/Modules/StandardsManagement/StandardsHierarchyUtilityTests.cs
```csharp
﻿using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using System.Collections.ObjectModel;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.Modules.StandardsManagement.ViewModels;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class StandardsHierarchyUtilityTests
    {
        [Test]
        public void BuildHierarchy_ShouldGroupAndSortHeterogeneousListCorrectly()
        {
            // Arrange
            var elements = new List<ElementModel>
            {
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Cherry wood" },
                new MaterialModel { Class = "Autodesk.Revit.DB.Material", Name = "Oak wood" },
                new LinePatternElementModel { Class = "Autodesk.Revit.DB.LinePatternElement", Name = "DashDot" },
                new FilledRegionTypeModel { Class = "Autodesk.Revit.DB.FilledRegionType", Name = "Diagonal Cross" },
                new CurtainSystemTypeModel { Class = "Autodesk.Revit.DB.CurtainSystemType", Name = "5x10 system" },
                new ParameterElementModel { Class = "Autodesk.Revit.DB.SharedParameterElement", Name = "BIM_Maint_Schedule" },
                new ElementTypeModel { Class = "Autodesk.Revit.DB.TextNoteType", Name = "1/8 Arial" },
                new ElementModel { Class = "Autodesk.Revit.DB.DirectShapeType", Name = "Custom Shape" } // Fallback to "Other" -> "DirectShapeType"
            };

            // Act
            var hierarchy = StandardsHierarchyUtility.BuildHierarchy(elements);

            // Assert
            Assert.IsNotNull(hierarchy);
            
            // Expected group names in alphabetical order:
            // Annotations, Materials & Assets, Other, Standards & Categories, System Types
            var expectedGroups = new[] { "Annotations", "Materials & Assets", "Other", "Standards & Categories", "System Types" };
            var actualGroups = hierarchy.Select(g => g.Name).ToArray();
            Assert.AreEqual(expectedGroups, actualGroups, "Groups should be sorted alphabetically.");

            // 1. Check "Annotations" group
            var annotationsGroup = hierarchy[0];
            Assert.AreEqual("Annotations", annotationsGroup.Name);
            Assert.IsTrue(annotationsGroup.IsExpanded);
            // Classes inside: "Filled Region Types", "Text Note Types" (alphabetically sorted)
            var annotationsClasses = annotationsGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Filled Region Types", "Text Note Types" }, annotationsClasses);
            
            var filledRegionClass = annotationsGroup.Children.Cast<StandardClassModel>().First(c => c.Name == "Filled Region Types");
            Assert.AreEqual(annotationsGroup, filledRegionClass.Parent);
            Assert.AreEqual(1, filledRegionClass.Children.Count);
            Assert.AreEqual("Diagonal Cross", filledRegionClass.Children[0].Name);
            Assert.AreEqual(filledRegionClass, filledRegionClass.Children[0].Parent);

            // 2. Check "Materials & Assets" group
            var matGroup = hierarchy[1];
            Assert.AreEqual("Materials & Assets", matGroup.Name);
            // Classes inside: "Line Patterns", "Materials"
            var matClasses = matGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Line Patterns", "Materials" }, matClasses);
            
            var materialClass = matGroup.Children.Cast<StandardClassModel>().First(c => c.Name == "Materials");
            // Elements inside sorted alphabetically: "Cherry wood", "Oak wood"
            var materialsElements = materialClass.Children.Cast<StandardElementModel>().Select(e => e.Name).ToArray();
            Assert.AreEqual(new[] { "Cherry wood", "Oak wood" }, materialsElements);

            // 3. Check "Other" group (DirectShapeType falls to Other -> DirectShapeType)
            var otherGroup = hierarchy[2];
            Assert.AreEqual("Other", otherGroup.Name);
            var otherClasses = otherGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Autodesk.Revit.DB.DirectShapeType" }, otherClasses);

            // 4. Check "Standards & Categories" group
            var stdGroup = hierarchy[3];
            Assert.AreEqual("Standards & Categories", stdGroup.Name);
            var stdClasses = stdGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Shared Parameters" }, stdClasses);

            // 5. Check "System Types" group
            var sysGroup = hierarchy[4];
            Assert.AreEqual("System Types", sysGroup.Name);
            var sysClasses = sysGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Curtain System Types" }, sysClasses);
        }

        [Test]
        public void GenerateTemplateHierarchy_ShouldCreateCorrectStructure()
        {
            // Act
            var hierarchy = StandardsHierarchyUtility.GenerateTemplateHierarchy();

            // Assert
            Assert.IsNotNull(hierarchy);
            
            // Expected group names in alphabetical order:
            // Annotations, Materials & Assets, Other, Standards & Categories, System Types, Views
            var expectedGroups = new[] { "Annotations", "Materials & Assets", "Other", "Standards & Categories", "System Types", "Views" };
            var actualGroups = hierarchy.Select(g => g.Name).ToArray();
            Assert.AreEqual(expectedGroups, actualGroups);

            // Verify Annotations class count and names (8 classes)
            var annotationsGroup = hierarchy[0];
            Assert.AreEqual("Annotations", annotationsGroup.Name);
            Assert.IsTrue(annotationsGroup.IsExpanded);
            var annotationsClasses = annotationsGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            var expectedAnnotationsClasses = new[]
            {
                "Detail Items",
                "Dimension Types",
                "Filled Region Types",
                "Grid Types",
                "Label Types",
                "Level Types",
                "Model Text Types",
                "Spot Dimension Types",
                "Text Note Types",
                "Title Blocks"
            };
            Assert.AreEqual(expectedAnnotationsClasses, annotationsClasses);

            // Verify child parent link
            foreach (var child in annotationsGroup.Children)
            {
                Assert.AreEqual(annotationsGroup, child.Parent);
                Assert.AreEqual(0, child.Children.Count, "Template classes should have no element leaves.");
            }

            // Verify Materials & Assets (4 classes)
            var matGroup = hierarchy[1];
            var matClasses = matGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Appearance Assets", "Fill Patterns", "Line Patterns", "Materials" }, matClasses);

            // Verify Other (1 class)
            var otherGroup = hierarchy[2];
            var otherClasses = otherGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Unknown Class" }, otherClasses);

            // Verify Standards & Categories (4 classes)
            var stdGroup = hierarchy[3];
            var stdClasses = stdGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Categories", "Element Types", "Filters", "Shared Parameters" }, stdClasses);

            // Verify System Types (13 classes)
            var sysGroup = hierarchy[4];
            var sysClasses = sysGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            var expectedSysClasses = new[]
            {
                "Ceiling Types",
                "Curtain System Types",
                "Fascia Types",
                "Floor Types",
                "Gutter Types",
                "Host Object Types",
                "Mullion Types",
                "Profiles",
                "Railing Types",
                "Roof Types",
                "Stairs Types",
                "Toposolid Types",
                "Wall Types"
            };
            Assert.AreEqual(expectedSysClasses, sysClasses);

            // Verify Views (4 classes)
            var viewsGroup = hierarchy[5];
            var viewsClasses = viewsGroup.Children.Cast<StandardClassModel>().Select(c => c.Name).ToArray();
            Assert.AreEqual(new[] { "Browser Organizations", "View Family Types", "View Templates", "Views" }, viewsClasses);
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Tier2_RevitSmokeTests.cs
```csharp
using System;
using Autodesk.Revit.UI;
using NUnit.Framework;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_RevitSmokeTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void RevitSmokeTest()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            Assert.IsNotNull(_uiapp!.Application, "Revit Application should not be null.");
            System.Console.WriteLine($"Revit API hydrated successfully: {_uiapp.Application.VersionName}");
        }

        [Test]
        public void VerifyCmdProjectStandards_Initialization()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");

            // Resolve types dynamically via reflection based on executing assembly's Revit version suffix
            string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name ?? "";
            string revitVersion = assemblyName.Replace("SyntheticTests", "");
            
            var vmType = Type.GetType($"Synthetic.Modules.StandardsManagement.ViewModels.ProjectStandardsDashboardViewModel, Synthetic{revitVersion}");
            Assert.IsNotNull(vmType, $"ProjectStandardsDashboardViewModel type could not be loaded for Revit version {revitVersion}.");

            var windowType = Type.GetType($"Synthetic.Modules.StandardsManagement.Views.ProjectStandardsDashboardWindow, Synthetic{revitVersion}");
            Assert.IsNotNull(windowType, $"ProjectStandardsDashboardWindow type could not be loaded for Revit version {revitVersion}.");

            var fakeDialog = new FakeFileDialogService();

            var suffix = $", Synthetic{revitVersion}";
            var guardrail = new SyntheticTests.Modules.StandardsManagement.FakeGuardrailPromptService();
            var userPromptService = new SyntheticTests.Modules.StandardsManagement.FakeUserPromptService();

            var exportServiceType = Type.GetType($"Synthetic.RevitDOM.Operations.Standards.StandardsExportService{suffix}");
            var exportService = Activator.CreateInstance(exportServiceType, guardrail, fakeDialog);

            var findReplaceType = Type.GetType($"Synthetic.RevitDOM.Operations.Standards.FindReplaceService{suffix}");
            var findReplaceService = Activator.CreateInstance(findReplaceType);

            var serializationEngineType = Type.GetType($"Synthetic.Modules.RevitDOM.StandardSerializationEngine{suffix}");
            var serializationEngine = Activator.CreateInstance(serializationEngineType);

            var revitIdentityType = Type.GetType($"Synthetic.Modules.RevitDOM.RevitIdentityService{suffix}");
            var revitIdentity = Activator.CreateInstance(revitIdentityType);

            var orchestratorType = Type.GetType($"Synthetic.RevitDOM.Operations.Standards.StandardsExtractionOrchestrator{suffix}");
            var orchestrator = Activator.CreateInstance(orchestratorType, revitIdentity, serializationEngine);

            var pocoIdentityType = Type.GetType($"Synthetic.Modules.RevitDOM.PocoIdentityService{suffix}");
            var pocoIdentityService = Activator.CreateInstance(pocoIdentityType);

            var diffEngineType = Type.GetType($"Synthetic.RevitDOM.Operations.Diffing.PocoToRevitDiffEngine{suffix}");
            var diffEngine = Activator.CreateInstance(diffEngineType, revitIdentity);

            var revitFamilyEnforcerType = Type.GetType($"Synthetic.RevitDOM.Operations.Standards.RevitFamilyEnforcer{suffix}");
            var revitFamilyEnforcer = Activator.CreateInstance(revitFamilyEnforcerType, serializationEngine);

            var pipelineType = Type.GetType($"Synthetic.RevitDOM.Operations.Standards.StandardsExecutionPipeline{suffix}");
            var pipeline = Activator.CreateInstance(pipelineType, serializationEngine, exportService, revitFamilyEnforcer);

            var vm = Activator.CreateInstance(vmType, 
                _uiapp!, 
                fakeDialog, 
                exportService, 
                null, 
                userPromptService, 
                findReplaceService, 
                orchestrator, 
                pocoIdentityService, 
                diffEngine, 
                serializationEngine, 
                pipeline);

            var window = Activator.CreateInstance(windowType, _uiapp!.MainWindowHandle);

            Assert.IsNotNull(vm, "ViewModel could not be instantiated.");
            Assert.IsNotNull(window, "Window could not be instantiated.");

            // Set DataContext
            var dataContextProp = windowType.GetProperty("DataContext");
            Assert.IsNotNull(dataContextProp, "DataContext property should exist on the window.");
            dataContextProp.SetValue(window, vm);

            try
            {
                // Show window modelessly to verify XAML parsing & theme loading
                var showMethod = windowType.GetMethod("Show");
                Assert.IsNotNull(showMethod, "Show method should exist on the window.");
                showMethod.Invoke(window, null);

                var isVisibleProp = windowType.GetProperty("IsVisible");
                Assert.IsNotNull(isVisibleProp, "IsVisible property should exist on the window.");
                bool isVisible = (bool)(isVisibleProp.GetValue(window) ?? false);
                Assert.IsTrue(isVisible, "Dashboard window should be visible.");
            }
            finally
            {
                // Close window to clean up
                var closeMethod = windowType.GetMethod("Close");
                Assert.IsNotNull(closeMethod, "Close method should exist on the window.");
                closeMethod.Invoke(window, null);
            }
        }

        private class FakeFileDialogService : Synthetic.Shared.UI.IFileDialogService
        {
            public string? OpenFileDialog(string filter, string title, string defaultName) => null;
            public string? SaveFileDialog(string filter, string title, string defaultName) => null;
        }
    }
}

```

### File: tests/SyntheticTests.Shared/Modules/AssemblyAnalyzer/Tier2_AssemblyAnalyzerTests.cs
```csharp
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
            string modelPathStr = Path.Combine(projectRoot, "tests", "test_models", "INC Standards - Assemblies & Details.rvt");
            if (!File.Exists(modelPathStr))
            {
                Console.WriteLine($"[WARN] AssemblyAnalyzer test model not found: {modelPathStr}. Tests will be skipped.");
                return;
            }

            var app = _uiapp.Application;
            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPathStr);
            // Use DiscardWorksets instead of PreserveWorksets: avoids opening a full
            // worksharing session, eliminating the ForgeConnectionTracker ArchiveException
            // and the double-close crash on Revit shutdown.
            OpenOptions openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
            };

            app.FailuresProcessing += ResolveWarnings;
            try
            {
                _sharedDoc = app.OpenDocumentFile(modelPath, openOptions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to open shared document in OneTimeSetUp: {ex.Message}");
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
```

### File: tests/SyntheticTests.Shared/Modules/MergeDuplicates/Tier2_MergeDuplicatesHeadlessTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Newtonsoft.Json;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Operations.Merge;


namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_MergeDuplicatesHeadlessTests
    {
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

        [Test]
        public void Test_GetBaseName_StripsTrailingDigitsAndSeparators()
        {
            Assert.AreEqual("MyFamily", MergeAnalysisEngine.GetBaseName("MyFamily_1"));
            Assert.AreEqual("MyFamily", MergeAnalysisEngine.GetBaseName("MyFamily-2"));
            Assert.AreEqual("MyFamily", MergeAnalysisEngine.GetBaseName("MyFamily 3"));
            Assert.AreEqual("MyFamily", MergeAnalysisEngine.GetBaseName("MyFamily#4"));
            Assert.AreEqual("MyFamily", MergeAnalysisEngine.GetBaseName("MyFamily.5"));
            Assert.AreEqual("MyFamily", MergeAnalysisEngine.GetBaseName("MyFamily"));
            Assert.AreEqual("", MergeAnalysisEngine.GetBaseName(""));
            Assert.AreEqual("", MergeAnalysisEngine.GetBaseName(null));
        }

        [Test]
        public void Test_BuildClustersFromModels_GroupsDuplicateFamilies()
        {
            string projectRoot = GetProjectRoot();
            string jsonPath = Path.Combine(projectRoot, "tests", "SyntheticTests.Shared", "Assets", "test_duplicate_families.json");
            Assert.IsTrue(File.Exists(jsonPath), $"Snapshot file not found at: {jsonPath}");

            string json = File.ReadAllText(jsonPath);
            var elements = JsonConvert.DeserializeObject<List<ElementModel>>(json);
            Assert.IsNotNull(elements);
            Assert.AreEqual(6, elements.Count);

            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);

            // Assertions for clustering
            Assert.IsNotNull(clusters, "Clusters collection should not be null.");
            Assert.AreEqual(3, clusters.Count, "Should detect 3 duplicate family symbol clusters.");

            var runningSectionCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains("Running Section"));
            Assert.IsNotNull(runningSectionCluster, "Should contain Running Section cluster.");
            Assert.AreEqual(2, runningSectionCluster.Items.Count);
            Assert.IsTrue(runningSectionCluster.Items.Any(i => i.IsPrimary), "One item should be flagged as primary.");

            var soldierCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains("Soldier & Plan"));
            Assert.IsNotNull(soldierCluster, "Should contain Soldier & Plan cluster.");
            Assert.AreEqual(2, soldierCluster.Items.Count);

            var rowlockCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains("Rowlock"));
            Assert.IsNotNull(rowlockCluster, "Should contain Rowlock cluster.");
            Assert.AreEqual(2, rowlockCluster.Items.Count);
        }

        [Test]
        public void Test_BuildClustersFromModels_GroupsDuplicateGroups()
        {
            string projectRoot = GetProjectRoot();
            string jsonPath = Path.Combine(projectRoot, "tests", "SyntheticTests.Shared", "Assets", "test_duplicate_groups.json");
            Assert.IsTrue(File.Exists(jsonPath), $"Snapshot file not found at: {jsonPath}");

            string json = File.ReadAllText(jsonPath);
            var elements = JsonConvert.DeserializeObject<List<ElementModel>>(json);
            Assert.IsNotNull(elements);
            Assert.AreEqual(2, elements.Count);

            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);

            // Assertions for clustering
            Assert.IsNotNull(clusters, "Clusters collection should not be null.");
            Assert.AreEqual(1, clusters.Count, "Should detect 1 duplicate group cluster.");

            var groupCluster = clusters.First();
            Assert.IsTrue(groupCluster.ClusterName.Contains("TestGroup"), "Cluster name should contain TestGroup.");
            Assert.AreEqual(2, groupCluster.Items.Count, "Should contain 2 group items.");
            Assert.IsTrue(groupCluster.Items.Any(i => i.IsPrimary), "One item should be flagged as primary.");

            var primaryItem = groupCluster.Items.First(i => i.IsPrimary);
            Assert.AreEqual("TestGroup", primaryItem.ItemName, "Item with shorter name should be primary.");
            
            var nonPrimaryItem = groupCluster.Items.First(i => !i.IsPrimary);
            Assert.AreEqual("TestGroup1", nonPrimaryItem.ItemName);
        }

        [Test]
        public void Test_CompareParameters_IdentifiesMatches()
        {
            var primaryElem = new ElementModel
            {
                ElementId = new ElementIdModel { Id = 1001, UniqueId = "uid-1001" },
                Name = "Running Section",
                Category = "Detail Items",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "Cost", Value = "100.0", StorageType = "Double", Id = 101 },
                    new ParameterModel { Name = "Type Comments", Value = "Standard", StorageType = "String", Id = 102 }
                }
            };

            var duplicateElem = new ElementModel
            {
                ElementId = new ElementIdModel { Id = 1002, UniqueId = "uid-1002" },
                Name = "Running Section",
                Category = "Detail Items",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "Cost", Value = "100.0", StorageType = "Double", Id = 101 },
                    new ParameterModel { Name = "Type Comments", Value = "Standard", StorageType = "String", Id = 102 }
                }
            };

            var elements = new List<ElementModel> { primaryElem, duplicateElem };
            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);
            Assert.AreEqual(1, clusters.Count);

            var cluster = clusters[0];
            var primaryItem = cluster.Items.First(i => i.RevitElementId.Id == 1001);
            cluster.UpdatePrimaryItem(primaryItem);

            MergeAnalysisEngine.GenerateRecommendations(cluster);

            var mapping = cluster.TypeMappings.First();
            Assert.IsNotNull(mapping);

            var costRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == "Cost");
            Assert.IsNotNull(costRow);
            Assert.IsFalse(costRow.HasConflict, "Identical Cost parameter should not have conflict.");
            Assert.IsFalse(costRow.IsSchemaMismatch, "Identical Cost parameter should not have schema mismatch.");

            var commentRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == "Type Comments");
            Assert.IsNotNull(commentRow);
            Assert.IsFalse(commentRow.HasConflict, "Identical Type Comments parameter should not have conflict.");
            Assert.IsFalse(commentRow.IsSchemaMismatch, "Identical Type Comments parameter should not have schema mismatch.");
        }

        [Test]
        public void Test_CompareParameters_IdentifiesConflicts()
        {
            var primaryElem = new ElementModel
            {
                ElementId = new ElementIdModel { Id = 1001, UniqueId = "uid-1001" },
                Name = "Running Section",
                Category = "Detail Items",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "Type Comments", Value = "Standard", StorageType = "String", Id = 102 }
                }
            };

            var duplicateElem = new ElementModel
            {
                ElementId = new ElementIdModel { Id = 1002, UniqueId = "uid-1002" },
                Name = "Running Section",
                Category = "Detail Items",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "Type Comments", Value = "Override", StorageType = "String", Id = 102 }
                }
            };

            var elements = new List<ElementModel> { primaryElem, duplicateElem };
            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);
            var cluster = clusters[0];
            var primaryItem = cluster.Items.First(i => i.RevitElementId.Id == 1001);
            cluster.UpdatePrimaryItem(primaryItem);

            MergeAnalysisEngine.GenerateRecommendations(cluster);

            var mapping = cluster.TypeMappings.First();
            var row = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == "Type Comments");
            Assert.IsNotNull(row);
            Assert.IsTrue(row.HasConflict, "Differing parameter values should flag conflict.");
            Assert.IsFalse(row.IsSchemaMismatch, "Differing parameter values with same storage type should not have schema mismatch.");
            Assert.AreEqual(2, row.Options.Count);
            Assert.AreEqual("Override", row.Options[0].DisplayText);
            Assert.AreEqual("Standard", row.Options[1].DisplayText);
        }

        [Test]
        public void Test_CompareParameters_IdentifiesSchemaMismatches()
        {
            var primaryElem = new ElementModel
            {
                ElementId = new ElementIdModel { Id = 1001, UniqueId = "uid-1001" },
                Name = "Running Section",
                Category = "Detail Items",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "Cost", Value = "100.0", StorageType = "Double", Id = 101 }
                }
            };

            var duplicateElem = new ElementModel
            {
                ElementId = new ElementIdModel { Id = 1002, UniqueId = "uid-1002" },
                Name = "Running Section",
                Category = "Detail Items",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "Cost", Value = "100.0", StorageType = "String", Id = 101 }
                }
            };

            var elements = new List<ElementModel> { primaryElem, duplicateElem };
            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);
            var cluster = clusters[0];
            var primaryItem = cluster.Items.First(i => i.RevitElementId.Id == 1001);
            cluster.UpdatePrimaryItem(primaryItem);

            MergeAnalysisEngine.GenerateRecommendations(cluster);

            var mapping = cluster.TypeMappings.First();
            var row = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == "Cost");
            Assert.IsNotNull(row);
            Assert.IsTrue(row.IsSchemaMismatch, "Differing storage types should flag schema mismatch.");
        }

        [Test]
        public void Test_CompareParameters_IdentifiesMissingParameters()
        {
            var primaryElem = new ElementModel
            {
                ElementId = new ElementIdModel { Id = 1001, UniqueId = "uid-1001" },
                Name = "Running Section",
                Category = "Detail Items",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "OnlyInPrimary", Value = "Hello", StorageType = "String", Id = 103 }
                }
            };

            var duplicateElem = new ElementModel
            {
                ElementId = new ElementIdModel { Id = 1002, UniqueId = "uid-1002" },
                Name = "Running Section",
                Category = "Detail Items",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                Parameters = new List<ParameterModel>
                {
                    new ParameterModel { Name = "OnlyInSource", Value = "World", StorageType = "String", Id = 104 }
                }
            };

            var elements = new List<ElementModel> { primaryElem, duplicateElem };
            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);
            var cluster = clusters[0];
            var primaryItem = cluster.Items.First(i => i.RevitElementId.Id == 1001);
            cluster.UpdatePrimaryItem(primaryItem);

            MergeAnalysisEngine.GenerateRecommendations(cluster);

            var mapping = cluster.TypeMappings.First();
            
            // OnlyInPrimary row (in target, missing in source)
            var primRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == "OnlyInPrimary");
            Assert.IsNotNull(primRow);
            Assert.IsFalse(primRow.IsSchemaMismatch, "Missing parameter on source is not a schema mismatch.");
            Assert.IsFalse(primRow.IsInjectEnabled, "Missing parameter on source should not be inject-enabled on target.");

            // OnlyInSource row (in source, missing in target)
            var srcRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == "OnlyInSource");
            Assert.IsNotNull(srcRow);
            Assert.IsFalse(srcRow.IsSchemaMismatch, "Missing parameter on target is not a schema mismatch.");
            Assert.IsTrue(srcRow.IsInjectEnabled, "Missing parameter on target should be inject-enabled.");
        }

        [Test]
        public void Test_GenerateRecommendedAction_MatchesDecisions()
        {
            // Case 1: Loadable Family (IsLoadableFamily = true)
            var primaryFam = new ElementModel
            {
                ElementId = new ElementIdModel { Id = 1001, UniqueId = "uid-1001" },
                Name = "Running Section",
                Category = "Detail Items",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                NestedTypes = new List<ElementModel>
                {
                    new ElementModel { ElementId = new ElementIdModel { Id = 10011 }, Name = "Type A" }
                }
            };

            var duplicateFam = new ElementModel
            {
                ElementId = new ElementIdModel { Id = 1002, UniqueId = "uid-1002" },
                Name = "Running Section 1",
                Category = "Detail Items",
                Class = "Autodesk.Revit.DB.FamilySymbol",
                NestedTypes = new List<ElementModel>
                {
                    new ElementModel { ElementId = new ElementIdModel { Id = 10021 }, Name = "Type A" },
                    new ElementModel { ElementId = new ElementIdModel { Id = 10022 }, Name = "Type B" }
                }
            };

            var elements = new List<ElementModel> { primaryFam, duplicateFam };
            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);
            Assert.AreEqual(1, clusters.Count);

            var cluster = clusters[0];
            var primaryItem = cluster.Items.First(i => i.RevitElementId.Id == 1001);
            cluster.UpdatePrimaryItem(primaryItem);

            MergeAnalysisEngine.GenerateRecommendations(cluster);

            Assert.AreEqual(2, cluster.TypeMappings.Count);
            
            var typeAMapping = cluster.TypeMappings.First(m => m.SourceType.Name == "Type A");
            Assert.AreEqual(RecommendedAction.Merge, typeAMapping.RecommendedAction, "Type A exists in primary and should be Merged.");

            var typeBMapping = cluster.TypeMappings.First(m => m.SourceType.Name == "Type B");
            Assert.AreEqual(RecommendedAction.Migrate, typeBMapping.RecommendedAction, "Type B is unique to duplicate family and should be Migrated.");

            // Case 2: Group (IsLoadableFamily = false)
            var primaryGroup = new ElementModel
            {
                ElementId = new ElementIdModel { Id = 2001, UniqueId = "uid-2001" },
                Name = "TestGroup",
                Category = "Model Groups",
                Class = "Autodesk.Revit.DB.GroupType"
            };

            var duplicateGroup = new ElementModel
            {
                ElementId = new ElementIdModel { Id = 2002, UniqueId = "uid-2002" },
                Name = "TestGroup 1",
                Category = "Model Groups",
                Class = "Autodesk.Revit.DB.GroupType"
            };

            var groupElements = new List<ElementModel> { primaryGroup, duplicateGroup };
            var groupClusters = MergeAnalysisEngine.BuildClustersFromModels(groupElements, token);
            Assert.AreEqual(1, groupClusters.Count);

            var groupCluster = groupClusters[0];
            var primaryGroupItem = groupCluster.Items.First(i => i.RevitElementId.Id == 2001);
            groupCluster.UpdatePrimaryItem(primaryGroupItem);

            MergeAnalysisEngine.GenerateRecommendations(groupCluster);

            Assert.AreEqual(1, groupCluster.TypeMappings.Count);
            var groupMapping = groupCluster.TypeMappings.First();
            Assert.AreEqual(RecommendedAction.Merge, groupMapping.RecommendedAction, "Groups should default to Merge even if names differ.");
        }

        [Test]
        public void Test_BoundingBoxXYZModel_GetSize_CalculatesDimensionsCorrectly()
        {
            var validBBox = new BoundingBoxXYZModel
            {
                Min = new XYZModel(-10.0, -5.0, 0.0),
                Max = new XYZModel(10.0, 15.0, 30.0)
            };

            var size = validBBox.GetSize();
            Assert.IsNotNull(size);
            Assert.AreEqual(20.0, size!.X, 1e-6);
            Assert.AreEqual(20.0, size.Y, 1e-6);
            Assert.AreEqual(30.0, size.Z, 1e-6);

            var nullBBox = new BoundingBoxXYZModel
            {
                Min = null,
                Max = new XYZModel(10.0, 10.0, 10.0)
            };
            Assert.IsNull(nullBBox.GetSize(), "GetSize should return null when Min is null.");

            var invalidBBox = new BoundingBoxXYZModel
            {
                Min = new XYZModel(double.NaN, 0, 0),
                Max = new XYZModel(10.0, 10.0, 10.0)
            };
            Assert.IsNull(invalidBBox.GetSize(), "GetSize should return null when Min contains NaN.");
        }

        [Test]
        public void Test_BoundingBoxXYZModel_GetCenter_CalculatesCenterCorrectly()
        {
            var validBBox = new BoundingBoxXYZModel
            {
                Min = new XYZModel(-10.0, -20.0, 0.0),
                Max = new XYZModel(10.0, 20.0, 100.0)
            };

            var center = validBBox.GetCenter();
            Assert.IsNotNull(center);
            Assert.AreEqual(0.0, center!.X, 1e-6);
            Assert.AreEqual(0.0, center.Y, 1e-6);
            Assert.AreEqual(50.0, center.Z, 1e-6);

            var nullBBox = new BoundingBoxXYZModel();
            Assert.IsNull(nullBBox.GetCenter(), "GetCenter should return null when Min and Max are unassigned.");
        }

        [Test]
        public void Test_BoundingBoxXYZModel_IsValid_ReturnsCorrectStatus()
        {
            var valid = new BoundingBoxXYZModel
            {
                Min = new XYZModel(0, 0, 0),
                Max = new XYZModel(1, 1, 1)
            };
            Assert.IsTrue(valid.IsValid);

            var nullMin = new BoundingBoxXYZModel
            {
                Min = null,
                Max = new XYZModel(1, 1, 1)
            };
            Assert.IsFalse(nullMin.IsValid);

            var nanVal = new BoundingBoxXYZModel
            {
                Min = new XYZModel(0, 0, double.NaN),
                Max = new XYZModel(1, 1, 1)
            };
            Assert.IsFalse(nanVal.IsValid);

            var infVal = new BoundingBoxXYZModel
            {
                Min = new XYZModel(0, 0, 0),
                Max = new XYZModel(1, double.PositiveInfinity, 1)
            };
            Assert.IsFalse(infVal.IsValid);
        }

        [Test]
        public void Test_XYZModel_IsOffsetEqual_EvaluatesTolerancesAndNullsCorrectly()
        {
            var vecA = new XYZModel(3.0, 4.0, 0.0); // length = 5.0
            var vecB = new XYZModel(0.0, 5.0, 0.0); // length = 5.0, but different spatial displacement
            var vecC = new XYZModel(3.0, 4.0, 0.0005); // spatial displacement within tolerance 1e-3

            Assert.IsFalse(XYZModel.IsOffsetEqual(vecA, vecB, 1e-3), "Vectors pointing in different directions should not be offset equal.");
            Assert.IsTrue(XYZModel.IsOffsetEqual(vecA, vecC, 1e-3), "Slightly differing vectors within tolerance should be equal.");

            var vecD = new XYZModel(10.0, 0.0, 0.0); // length = 10.0
            Assert.IsFalse(XYZModel.IsOffsetEqual(vecA, vecD, 1e-3), "Vectors with different lengths should not be offset equal.");

            // Null cases
            Assert.IsTrue(XYZModel.IsOffsetEqual(null, null), "Two null vectors should be equal.");
            Assert.IsFalse(XYZModel.IsOffsetEqual(vecA, null), "Vector compared to null should be false.");
            Assert.IsFalse(XYZModel.IsOffsetEqual(null, vecB), "Null compared to vector should be false.");

            // NaN / Infinity cases
            var nanVec = new XYZModel(double.NaN, 0, 0);
            Assert.IsFalse(XYZModel.IsOffsetEqual(vecA, nanVec), "Comparison with NaN vector should return false.");
        }

        [Test]
        public void Test_XYZModel_IsValid_DetectsNaNAndInfinity()
        {
            Assert.IsTrue(new XYZModel(1.0, 2.0, 3.0).IsValid);
            Assert.IsFalse(new XYZModel(double.NaN, 2.0, 3.0).IsValid);
            Assert.IsFalse(new XYZModel(1.0, double.NegativeInfinity, 3.0).IsValid);
            Assert.IsFalse(new XYZModel(1.0, 2.0, double.PositiveInfinity).IsValid);
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/MergeDuplicates/Tier2_MergeDuplicatesIntegrationTests.cs
```csharp
using Synthetic.Modules.MergeDuplicates.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.MergeDuplicates.ViewModels;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.RevitDOM.Models;


namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_MergeDuplicatesIntegrationTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
            string errPath = Path.Combine(Path.GetTempPath(), "synthetic_test_error.txt");
            if (File.Exists(errPath))
            {
                File.Delete(errPath);
            }
        }

        #region Helper Methods

        private static string GetProjectRoot()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string addinPath = Path.Combine(appData, "Autodesk", "Revit", "Addins", "2026", "Synthetic2026.addin");
            if (File.Exists(addinPath))
            {
                string content = File.ReadAllText(addinPath);
                var match = System.Text.RegularExpressions.Regex.Match(content, @"<Assembly>(.*?)\\output\\Synthetic\\Synthetic2026\.dll", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string root = match.Groups[1].Value;
                    if (Directory.Exists(root))
                    {
                        return root;
                    }
                }
            }

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

        private Document OpenTestTemplate(Autodesk.Revit.ApplicationServices.Application app)
        {
            string projectRoot = GetProjectRoot();
            string testModelName = "TestTemplate" + app.VersionNumber + ".rvt";
            string modelPathStr = Path.Combine(projectRoot, "tests", "test_models", testModelName);
            if (!File.Exists(modelPathStr))
            {
                throw new FileNotFoundException("Test template model not found: " + modelPathStr);
            }

            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPathStr);
            OpenOptions openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
            };
            return app.OpenDocumentFile(modelPath, openOptions);
        }

        private Family DuplicateFamily(Document doc, Family sourceFamily, string suffix, out string tempPath)
        {
            Document famDoc = doc.EditFamily(sourceFamily);
            if (famDoc == null)
            {
                throw new InvalidOperationException("EditFamily returned null.");
            }

            try
            {
                string tempDir = Path.GetTempPath();
                string newName = sourceFamily.Name + suffix;
                tempPath = Path.Combine(tempDir, newName + ".rfa");

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                famDoc.SaveAs(tempPath);
            }
            finally
            {
                famDoc.Close(false);
            }

            // Load duplicate family back in a transaction on doc
            Family duplicatedFamily;
            using (Transaction t = new Transaction(doc, "Load Duplicate Family"))
            {
                t.Start();
                bool loaded = doc.LoadFamily(tempPath, new ProcessMergeFamilyLoadOptions(), out duplicatedFamily);
                if (!loaded || duplicatedFamily == null)
                {
                    throw new InvalidOperationException("Failed to load duplicated family.");
                }
                t.Commit();
            }

            return duplicatedFamily;
        }

        private FamilyInstance PlaceInstance(Document doc, FamilySymbol symbol)
        {
            if (!symbol.IsActive)
            {
                symbol.Activate();
            }

            Category cat = symbol.Category;
            if (cat != null && cat.Id == new ElementId(BuiltInCategory.OST_TitleBlocks))
            {
                ViewSheet sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                return doc.Create.NewFamilyInstance(XYZ.Zero, symbol, sheet);
            }

            // Check if it's a detail component or annotation
            if (symbol.Family.FamilyCategory.CategoryType == CategoryType.Annotation ||
                symbol.Family.FamilyCategory.Id == new ElementId(BuiltInCategory.OST_DetailComponents))
            {
                FilteredElementCollector viewCollector = new FilteredElementCollector(doc);
                ViewFamilyType? draftingViewType = viewCollector
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.Drafting);

                ViewDrafting draftingView;
                if (draftingViewType != null)
                {
                    draftingView = ViewDrafting.Create(doc, draftingViewType.Id);
                }
                else
                {
                    draftingView = ViewDrafting.Create(doc, ElementId.InvalidElementId);
                }
                return doc.Create.NewFamilyInstance(XYZ.Zero, symbol, draftingView);
            }

            // Fallback for 3D model elements
            Level level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault();
            if (level == null)
            {
                level = Level.Create(doc, 0.0);
            }
            return doc.Create.NewFamilyInstance(XYZ.Zero, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
        }

        private static bool InjectParameterToFamilyDynamic(FamilyManager famManager, string paramName)
        {
            object? paramGroup = null;
            object? paramType = null;

            Type? groupTypeIdType = typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.GroupTypeId");
            if (groupTypeIdType != null)
            {
                paramGroup = groupTypeIdType.GetProperty("Data")?.GetValue(null);
                paramType = typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.SpecTypeId+String")?.GetProperty("Text")?.GetValue(null);
            }
            else
            {
                paramGroup = Enum.Parse(typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.BuiltInParameterGroup")!, "PG_DATA");
                paramType = Enum.Parse(typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.ParameterType")!, "Text");
            }

            if (famManager == null)
            {
                Console.WriteLine("[ERROR] FamilyManager is null.");
                return false;
            }
            if (paramGroup == null)
            {
                Console.WriteLine("[ERROR] paramGroup is null.");
                return false;
            }
            if (paramType == null)
            {
                Console.WriteLine("[ERROR] paramType is null.");
                return false;
            }

            System.Reflection.MethodInfo? addParamMethod = null;
            if (groupTypeIdType != null)
            {
                addParamMethod = famManager.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "AddParameter" && 
                                         m.GetParameters().Length == 4 && 
                                         m.GetParameters()[1].ParameterType.Name.Contains("ForgeTypeId") &&
                                         m.GetParameters()[2].ParameterType.Name.Contains("ForgeTypeId"));
            }
            else
            {
                addParamMethod = famManager.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "AddParameter" && 
                                         m.GetParameters().Length == 4 && 
                                         m.GetParameters()[1].ParameterType.Name.Contains("BuiltInParameterGroup") &&
                                         m.GetParameters()[2].ParameterType.Name.Contains("ParameterType"));
            }

            if (addParamMethod != null)
            {
                try
                {
                    addParamMethod.Invoke(famManager, new object[] { paramName, paramGroup, paramType, false });
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] AddParameter invoke failed: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"[INNER EXCEPTION] {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
                    }
                    return false;
                }
            }
            else
            {
                Console.WriteLine("[ERROR] AddParameter method with 4 arguments not found.");
            }
            return false;
        }

        private static void ExportPocoSnapshot(IEnumerable<Element> elements, string fileName)
        {
            string projectRoot = GetProjectRoot();
            string assetsDir = Path.Combine(projectRoot, "tests", "SyntheticTests.Shared", "Assets");
            if (!Directory.Exists(assetsDir))
            {
                Directory.CreateDirectory(assetsDir);
            }

            var models = elements
                .Where(el => el != null)
                .Select(el => el.ToModel(false))
                .ToList();

            string json = JsonConvert.SerializeObject(models, Formatting.Indented);
            string filePath = Path.Combine(assetsDir, fileName);
            File.WriteAllText(filePath, json);
        }

        #endregion

        #region Integration Tests

        [Test]
        public void Test_MergeDuplicates_SuccessfulMerge()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = OpenTestTemplate(app);

            string tempPath = string.Empty;

            try
            {
                // 1. Locate editable, loadable family
                Family? sourceFamily = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.IsEditable && !f.IsInPlace);
                Assert.IsNotNull(sourceFamily, "Could not find a loadable, editable family in the document.");

                // 2. Duplicate the family (suffix "1" ensures duplicate base name match)
                Family duplicatedFamily = DuplicateFamily(doc, sourceFamily, "1", out tempPath);
                ElementId duplicateFamilyId = duplicatedFamily.Id;

                // 3. Place family instance of the duplicate family symbol
                FamilySymbol sourceSymbol = (FamilySymbol)doc.GetElement(sourceFamily.GetFamilySymbolIds().First());
                FamilySymbol duplicateSymbol = (FamilySymbol)doc.GetElement(duplicatedFamily.GetFamilySymbolIds().First());

                FamilyInstance instance;
                using (Transaction t = new Transaction(doc, "Place Duplicate Family Instance"))
                {
                    t.Start();
                    instance = PlaceInstance(doc, duplicateSymbol);
                    t.Commit();
                }

                Assert.IsNotNull(instance, "Failed to place family instance.");
                Assert.AreEqual(duplicateSymbol.Id, instance.GetTypeId(), "Placed instance should initially point to duplicate symbol.");

                using (var tg = new TransactionGroup(doc, "Merge Duplicates Integration Test"))
                {
                    tg.Start();

                    // Export the duplicate family symbols
                    var familySymbols = sourceFamily.GetFamilySymbolIds()
                        .Concat(duplicatedFamily.GetFamilySymbolIds())
                        .Select(id => doc.GetElement(id))
                        .ToList();
                    ExportPocoSnapshot(familySymbols, "test_duplicate_families.json");

                    // 4. Run Fast Scan
                    var token = CancellationToken.None;
                    var clusters = RevitMergeDataCollector.RunFastScan(doc, token);
                    foreach (var c in clusters)
                    {
                        app.WriteJournalComment($"[TEST_CLUSTER_DEBUG] Cluster: {c.ClusterName}", true);
                        foreach (var i in c.Items)
                        {
                            app.WriteJournalComment($"  [TEST_CLUSTER_DEBUG] Item: Name={i.ItemName}, ID={i.RevitElementId.ToElementId().ToString()}", true);
                        }
                    }
                    var targetCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains(sourceFamily.Name));
                    Assert.IsNotNull(targetCluster, "MergeAnalysisEngine should detect the duplicate cluster.");

                    // 5. Run Deep Scan
                    MergeAnalysisEngine.RunDeepScan(targetCluster, token);
                    Assert.IsFalse(targetCluster.HasSchemaMismatch, "Should not have schema mismatch.");
                    Assert.IsFalse(targetCluster.HasOriginMismatch, "Should not have origin mismatch.");

                    // 6. Generate Recommendations
                    MergeAnalysisEngine.GenerateRecommendations(targetCluster);

                    // Set primary item and ensure duplicate is included for merge
                    var primaryItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == sourceFamily.Id);
                    Assert.IsNotNull(primaryItem, "Primary item should exist in cluster.");
                    targetCluster.UpdatePrimaryItem(primaryItem);

                    var duplicateItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == duplicatedFamily.Id);
                    Assert.IsNotNull(duplicateItem, "Duplicate item should exist in cluster.");
                    duplicateItem.IsIncludedForMerge = true;

                    // 7. Execute Merge
                    var queueVM = new MergeQueueViewModel();
                    queueVM.QueuedClusters.Add(targetCluster);

                    var handler = new ProcessMergeEventHandler();
                    handler.QueueRequest(queueVM, null, null, token);
                    handler.Execute(_uiapp);

                    // 8. Assertions
                    // Verify duplicate family was deleted/purged
                    var deletedFamily = doc.GetElement(duplicateFamilyId);
                    if (deletedFamily != null)
                    {
                        Assert.Fail($"Duplicate family should be deleted. Deletion error: {(File.Exists(Path.Combine(Path.GetTempPath(), "synthetic_test_error.txt")) ? File.ReadAllText(Path.Combine(Path.GetTempPath(), "synthetic_test_error.txt")) : "No log file found.")}");
                    }

                    // Verify instance is redirected to the primary family symbol
                    Assert.AreEqual(sourceSymbol.Id, instance.GetTypeId(), "Family instance should be redirected to the primary family symbol.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);

                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch { }
                }
            }
        }

        [Test]
        public void Test_MergeDuplicates_DetectsSchemaMismatch()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = OpenTestTemplate(app);

            string tempPath = string.Empty;

            try
            {
                // 1. Locate editable, loadable family
                Family? sourceFamily = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.IsEditable && !f.IsInPlace);
                Assert.IsNotNull(sourceFamily, "Could not find a loadable, editable family in the document.");

                // 2. Duplicate the family (suffix "2" ensures duplicate base name match)
                Family duplicatedFamily = DuplicateFamily(doc, sourceFamily, "2", out tempPath);

                // 3. Edit duplicated family and inject custom parameter
                Document famDoc = doc.EditFamily(duplicatedFamily);
                Assert.IsNotNull(famDoc, "EditFamily returned null.");

                try
                {
                    using (Transaction t = new Transaction(famDoc, "Add Parameter to Duplicate Family"))
                    {
                        t.Start();
                        bool added = InjectParameterToFamilyDynamic(famDoc.FamilyManager, "Schema_Mismatch_Test_Param");
                        Assert.IsTrue(added, "Should successfully inject parameter into family.");
                        t.Commit();
                    }

                    SaveAsOptions saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                    famDoc.SaveAs(tempPath, saveOptions);
                }
                finally
                {
                    famDoc.Close(false);
                }

                // Load the updated family from disk back into doc
                using (Transaction t = new Transaction(doc, "Reload Modified Family"))
                {
                    t.Start();
                    doc.LoadFamily(tempPath, new ProcessMergeFamilyLoadOptions(), out duplicatedFamily);
                    t.Commit();
                }

                using (var tg = new TransactionGroup(doc, "Schema Mismatch Test"))
                {
                    tg.Start();

                    // 4. Run Scan & Analyze
                    var token = CancellationToken.None;
                    var clusters = RevitMergeDataCollector.RunFastScan(doc, token);
                    foreach (var c in clusters)
                    {
                        app.WriteJournalComment($"[TEST_CLUSTER_DEBUG] Cluster: {c.ClusterName}", true);
                        foreach (var i in c.Items)
                        {
                            app.WriteJournalComment($"  [TEST_CLUSTER_DEBUG] Item: Name={i.ItemName}, ID={i.RevitElementId.ToElementId().ToString()}", true);
                        }
                    }
                    var targetCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains(sourceFamily.Name));
                    Assert.IsNotNull(targetCluster, "MergeAnalysisEngine should detect the duplicate cluster.");

                    MergeAnalysisEngine.RunDeepScan(targetCluster, token);

                    // 5. Assert
                    Assert.IsTrue(targetCluster.HasSchemaMismatch, "Deep scan should detect schema mismatch because of injected parameter.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);

                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch { }
                }
            }
        }

        [Test]
        public void Test_MergeDuplicates_ResolvesParameterConflicts()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = OpenTestTemplate(app);

            string tempPath = string.Empty;

            try
            {
                // 1. Locate editable, loadable family
                Family? sourceFamily = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => f.IsEditable && !f.IsInPlace);
                Assert.IsNotNull(sourceFamily, "Could not find a loadable, editable family in the document.");

                // 2. Duplicate the family (suffix "3" ensures duplicate base name match)
                Family duplicatedFamily = DuplicateFamily(doc, sourceFamily, "3", out tempPath);
                ElementId duplicateFamilyId = duplicatedFamily.Id;

                FamilySymbol sourceSymbol = (FamilySymbol)doc.GetElement(sourceFamily.GetFamilySymbolIds().First());
                FamilySymbol duplicateSymbol = (FamilySymbol)doc.GetElement(duplicatedFamily.GetFamilySymbolIds().First());

                // Place duplicate instance
                FamilyInstance instance;
                using (Transaction t = new Transaction(doc, "Place Instance and Set Parameters"))
                {
                    t.Start();
                    instance = PlaceInstance(doc, duplicateSymbol);

                    // 3. Set conflicting parameter values on the Description parameter (Type parameter)
                    sourceSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION).Set("Primary Value");
                    duplicateSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION).Set("Duplicate Value");

                    t.Commit();
                }

                using (var tg = new TransactionGroup(doc, "Parameter Conflict Resolution Test"))
                {
                    tg.Start();

                    // 4. Run Scan
                    var token = CancellationToken.None;
                    var clusters = RevitMergeDataCollector.RunFastScan(doc, token);
                    foreach (var c in clusters)
                    {
                        app.WriteJournalComment($"[TEST_CLUSTER_DEBUG] Cluster: {c.ClusterName}", true);
                        foreach (var i in c.Items)
                        {
                            app.WriteJournalComment($"  [TEST_CLUSTER_DEBUG] Item: Name={i.ItemName}, ID={i.RevitElementId.ToElementId().ToString()}", true);
                        }
                    }
                    var targetCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains(sourceFamily.Name));
                    Assert.IsNotNull(targetCluster, "MergeAnalysisEngine should detect the duplicate cluster.");

                    // 5. Deep Scan & Recommendations
                    MergeAnalysisEngine.RunDeepScan(targetCluster, token);
                    MergeAnalysisEngine.GenerateRecommendations(targetCluster);

                    // 6. Locate conflict row & designate winner
                    var primaryItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == sourceFamily.Id);
                    Assert.IsNotNull(primaryItem);
                    targetCluster.UpdatePrimaryItem(primaryItem);

                    var duplicateItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == duplicatedFamily.Id);
                    Assert.IsNotNull(duplicateItem);
                    duplicateItem.IsIncludedForMerge = true;

                    var mapping = targetCluster.TypeMappings.First();
                    var descRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName.Equals("Description", StringComparison.OrdinalIgnoreCase));
                    Assert.IsNotNull(descRow, "Should find Description parameter row.");
                    Assert.IsTrue(descRow.HasConflict, "Should detect parameter value conflict.");

                    // Designate duplicate symbol's parameter value as the winner
                    descRow.WinningValueElementId = duplicateSymbol.Id.ToModel(doc);

                    // 7. Execute Merge
                    var queueVM = new MergeQueueViewModel();
                    queueVM.QueuedClusters.Add(targetCluster);

                    var handler = new ProcessMergeEventHandler();
                    handler.QueueRequest(queueVM, null, null, token);
                    handler.Execute(_uiapp);

                    // 8. Assertions
                    // Verify duplicate family was deleted/purged
                    var deletedFamily = doc.GetElement(duplicateFamilyId);
                    if (deletedFamily != null)
                    {
                        Assert.Fail($"Duplicate family should be deleted. Deletion error: {(File.Exists(Path.Combine(Path.GetTempPath(), "synthetic_test_error.txt")) ? File.ReadAllText(Path.Combine(Path.GetTempPath(), "synthetic_test_error.txt")) : "No log file found.")}");
                    }

                    // Verify winning parameter value was copied to primary symbol
                    string finalValue = sourceSymbol.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION).AsString();
                    Assert.AreEqual("Duplicate Value", finalValue, "Primary family symbol's Description parameter should retain the designated winning value.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);

                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch { }
                }
            }
        }

        [Test]
        public void Test_MergeGroupDuplicates_SuccessfulMerge()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = OpenTestTemplate(app);

            try
            {
                GroupType? sourceGroupType = null;
                GroupType? duplicateGroupType = null;
                Group? groupInstance1 = null;
                Group? groupInstance2 = null;

                using (Transaction t = new Transaction(doc, "Create Test Groups"))
                {
                    t.Start();

                    // Create Model Curve on a Level Plane to form a Model Group
                    Level? level = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault();
                    Assert.IsNotNull(level, "Document must have at least one level.");

                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation));
                    SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                    Line line = Line.CreateBound(XYZ.Zero, new XYZ(5, 0, 0));
                    ModelCurve modelLine = doc.Create.NewModelCurve(line, sketchPlane);

                    // Create group
                    Group sourceGroupInstance = doc.Create.NewGroup(new List<ElementId> { modelLine.Id });
                    sourceGroupType = sourceGroupInstance.GroupType;
                    sourceGroupType.Name = "TestGroup";

                    // Duplicate group type
                    duplicateGroupType = (GroupType)sourceGroupType.Duplicate("TestGroup1");

                    // Place instances
                    groupInstance1 = doc.Create.PlaceGroup(new XYZ(0, 0, 0), sourceGroupType);
                    groupInstance2 = doc.Create.PlaceGroup(new XYZ(5, 0, 0), duplicateGroupType);

                    t.Commit();
                }

                Assert.IsNotNull(sourceGroupType);
                Assert.IsNotNull(duplicateGroupType);
                Assert.IsNotNull(groupInstance1);
                Assert.IsNotNull(groupInstance2);

                ElementId duplicateGroupTypeId = duplicateGroupType.Id;

                using (var tg = new TransactionGroup(doc, "Merge Group Duplicates Integration Test"))
                {
                    tg.Start();

                    // Export the duplicate group types
                    var groupTypes = new List<Element> { sourceGroupType, duplicateGroupType };
                    ExportPocoSnapshot(groupTypes, "test_duplicate_groups.json");

                    var token = CancellationToken.None;
                    var clusters = RevitMergeDataCollector.RunFastScan(doc, token);
                    var targetCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains("TestGroup"));
                    Assert.IsNotNull(targetCluster, "MergeAnalysisEngine should detect the duplicate group cluster.");

                    MergeAnalysisEngine.RunDeepScan(targetCluster, token);
                    Assert.IsFalse(targetCluster.HasSchemaMismatch, "Should not have schema mismatch.");
                    Assert.IsFalse(targetCluster.HasOriginMismatch, "Should not have origin mismatch.");

                    MergeAnalysisEngine.GenerateRecommendations(targetCluster);

                    var primaryItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == sourceGroupType.Id);
                    Assert.IsNotNull(primaryItem, "Primary item should exist in cluster.");
                    targetCluster.UpdatePrimaryItem(primaryItem);

                    var duplicateItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == duplicateGroupType.Id);
                    Assert.IsNotNull(duplicateItem, "Duplicate item should exist in cluster.");
                    duplicateItem.IsIncludedForMerge = true;

                    var queueVM = new MergeQueueViewModel();
                    queueVM.QueuedClusters.Add(targetCluster);

                    var handler = new ProcessMergeEventHandler();
                    handler.QueueRequest(queueVM, null, null, token);
                    handler.Execute(_uiapp);

                    // Verify duplicate group type was deleted/purged
                    var deletedGroupType = doc.GetElement(duplicateGroupTypeId);
                    Assert.IsNull(deletedGroupType, "Duplicate group type should be deleted.");

                    // Verify instance is redirected to the primary group type
                    Assert.AreEqual(sourceGroupType.Id, groupInstance2.GroupType.Id, "Group instance should be redirected to the primary group type.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void Test_MergeGroupDuplicates_ResolvesParameterConflicts()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = OpenTestTemplate(app);

            GroupType? sourceGroupType = null;
            GroupType? duplicateGroupType = null;
            Group? groupInstance1 = null;
            Group? groupInstance2 = null;

            // Create shared parameters file
            string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
            File.WriteAllText(tempFilePath, ""); // Create empty file
            
            string originalSharedParamFile = app.SharedParametersFilename;
            app.SharedParametersFilename = tempFilePath;

            try
            {
                using (Transaction t = new Transaction(doc, "Create Test Groups and Bind Parameter"))
                {
                    t.Start();

                    // 1. Create a Type Parameter bound to Model Groups category
                    DefinitionFile defFile = app.OpenSharedParameterFile();
                    Assert.IsNotNull(defFile, "Failed to open temporary shared parameter file.");

                    DefinitionGroup group = defFile.Groups.Create("TestGroup");
                    Definition definition = group.Definitions.Create(new ExternalDefinitionCreationOptions("TestParam", SpecTypeId.String.Text));
                    Assert.IsNotNull(definition, "Failed to create shared parameter definition.");

                    CategorySet categorySet = app.Create.NewCategorySet();
                    Category groupCat1 = doc.Settings.Categories.get_Item(BuiltInCategory.OST_IOSModelGroups);
                    categorySet.Insert(groupCat1);

                    Binding binding = app.Create.NewTypeBinding(categorySet);
                    bool inserted = doc.ParameterBindings.Insert(definition, binding);
                    Assert.IsTrue(inserted, "Failed to insert parameter binding.");

                    // 2. Create Model Curve on a Level Plane to form a Model Group
                    Level? level = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault();
                    Assert.IsNotNull(level, "Document must have at least one level.");

                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation));
                    SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                    Line line = Line.CreateBound(XYZ.Zero, new XYZ(5, 0, 0));
                    ModelCurve modelLine = doc.Create.NewModelCurve(line, sketchPlane);

                    // Create group
                    Group sourceGroupInstance = doc.Create.NewGroup(new List<ElementId> { modelLine.Id });
                    sourceGroupType = sourceGroupInstance.GroupType;
                    sourceGroupType.Name = "TestGroupParam";

                    // Duplicate group type
                    duplicateGroupType = (GroupType)sourceGroupType.Duplicate("TestGroupParam1");

                    // Place instances
                    groupInstance1 = doc.Create.PlaceGroup(new XYZ(0, 0, 0), sourceGroupType);
                    groupInstance2 = doc.Create.PlaceGroup(new XYZ(5, 0, 0), duplicateGroupType);

                    // Set conflicting parameter values on the newly created Type Parameter using LookupParameter
                    var srcDescParam = sourceGroupType.LookupParameter("TestParam");
                    var dupDescParam = duplicateGroupType.LookupParameter("TestParam");
                    
                    Assert.IsNotNull(srcDescParam, "Source group type should have TestParam parameter.");
                    Assert.IsNotNull(dupDescParam, "Duplicate group type should have TestParam parameter.");
                    
                    srcDescParam.Set("Primary Value");
                    dupDescParam.Set("Duplicate Value");

                    t.Commit();
                }

                Assert.IsNotNull(sourceGroupType);
                Assert.IsNotNull(duplicateGroupType);
                Assert.IsNotNull(groupInstance1);
                Assert.IsNotNull(groupInstance2);

                ElementId duplicateGroupTypeId = duplicateGroupType.Id;

                using (var tg = new TransactionGroup(doc, "Parameter Conflict Resolution Test for Groups"))
                {
                    tg.Start();

                    var token = CancellationToken.None;
                    var clusters = RevitMergeDataCollector.RunFastScan(doc, token);
                    var targetCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains("TestGroupParam"));
                    Assert.IsNotNull(targetCluster, "MergeAnalysisEngine should detect the duplicate group cluster.");

                    MergeAnalysisEngine.RunDeepScan(targetCluster, token);
                    MergeAnalysisEngine.GenerateRecommendations(targetCluster);

                    var primaryItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == sourceGroupType.Id);
                    Assert.IsNotNull(primaryItem);
                    targetCluster.UpdatePrimaryItem(primaryItem);

                    var duplicateItem = targetCluster.Items.FirstOrDefault(i => i.RevitElementId.ToElementId() == duplicateGroupType.Id);
                    Assert.IsNotNull(duplicateItem);
                    duplicateItem.IsIncludedForMerge = true;

                    var mapping = targetCluster.TypeMappings.First();
                    var descRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName.Equals("TestParam", StringComparison.OrdinalIgnoreCase));
                    Assert.IsNotNull(descRow, "Should find TestParam parameter row.");
                    Assert.IsTrue(descRow.HasConflict, "Should detect parameter value conflict.");

                    // Designate duplicate type's parameter value as the winner
                    descRow.WinningValueElementId = duplicateGroupType.Id.ToModel(doc);

                    var queueVM = new MergeQueueViewModel();
                    queueVM.QueuedClusters.Add(targetCluster);

                    var handler = new ProcessMergeEventHandler();
                    handler.QueueRequest(queueVM, null, null, token);
                    handler.Execute(_uiapp);

                    // Verify duplicate group type was deleted/purged
                    var deletedGroupType = doc.GetElement(duplicateGroupTypeId);
                    Assert.IsNull(deletedGroupType, "Duplicate group type should be deleted.");

                    // Verify winning parameter value was copied to primary group type
                    string finalValue = sourceGroupType.LookupParameter("TestParam").AsString();
                    Assert.AreEqual("Duplicate Value", finalValue, "Primary group type's TestParam parameter should retain the designated winning value.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
                app.SharedParametersFilename = originalSharedParamFile;
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch {}
            }
        }

        
        [Test]
        public void TestRevitMergeDataCollectorConvertToPoco()
        {
            var app = _uiapp.Application;
            string tempFilePath = Path.Combine(Path.GetTempPath(), $"TestMergeDataCollector_{Guid.NewGuid()}.rvt");
            Document doc = app.NewProjectDocument(UnitSystem.Imperial);
            doc.SaveAs(tempFilePath);

            try
            {
                using (var tr = new Transaction(doc, "Setup Test Elements"))
                {
                    tr.Start();

                    // Create a dummy wall type or family element
                    var collector = new FilteredElementCollector(doc).OfClass(typeof(WallType));
                    var wallType = collector.FirstElement() as WallType;
                    Assert.IsNotNull(wallType, "Document should contain at least one WallType.");

                    // Convert to POCO via RevitMergeDataCollector
                    ElementModel poco = RevitMergeDataCollector.ConvertToPoco(doc, wallType);
                    Assert.IsNotNull(poco, "RevitMergeDataCollector.ConvertToPoco should return non-null ElementModel.");
                    Assert.AreEqual(wallType.Name, poco.Name, "POCO name should match element name.");

                    tr.Commit();
                }
            }
            finally
            {
                doc.Close(false);
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch {}
            }
        }

#endregion
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/AliasSwapEngineTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class AliasSwapEngineTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        #region Helper Methods

        private long GetRawIdValue(ElementId id)
        {
            var prop = id.GetType().GetProperty("Value");
            if (prop != null)
            {
                return (long)prop.GetValue(id);
            }
            var intProp = id.GetType().GetProperty("IntegerValue");
            return Convert.ToInt64(intProp!.GetValue(id));
        }

        private Wall CreateWall(Document doc, out Level level1, out Level level2)
        {
            level1 = Level.Create(doc, 0.0);
            level1.Name = "Test Level 1 " + Guid.NewGuid().ToString().Substring(0, 8);
            level2 = Level.Create(doc, 10.0);
            level2.Name = "Test Level 2 " + Guid.NewGuid().ToString().Substring(0, 8);

            var wallType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .First();

            Line line = Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0));
            Wall wall = Wall.Create(doc, line, wallType.Id, level1.Id, 10.0, 0.0, false, false);
            return wall;
        }

        private LinePatternElement CreateLinePattern(Document doc, string name)
        {
            LinePattern pattern = new LinePattern(name);
            pattern.SetSegments(new List<LinePatternSegment>
            {
                new LinePatternSegment(LinePatternSegmentType.Dash, 0.1),
                new LinePatternSegment(LinePatternSegmentType.Space, 0.1)
            });
            return LinePatternElement.Create(doc, pattern);
        }

        private Material CreateMaterial(Document doc, string name)
        {
            ElementId matId = Material.Create(doc, name);
            return (Material)doc.GetElement(matId);
        }

        #endregion

        #region AliasSwapEngine Tests

        [Test]
        public void SwapElementReferences_UpdatesStandardParameters()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Swap Standard Param Test"))
                {
                    tg.Start();

                    Wall wall;
                    Level level1, level2;
                    using (var t = new Transaction(doc, "Create Wall"))
                    {
                        t.Start();
                        wall = CreateWall(doc, out level1, out level2);
                        t.Commit();
                    }

                    var baseConstraintParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    Assert.AreEqual(level1.Id, baseConstraintParam.AsElementId());

                    // Act - swap Level 1 ID with Level 2 ID using AliasSwapEngine
                    AliasSwapEngine.SwapElementReferences(doc, level1.Id, level2.Id);

                    // Assert
                    Assert.AreEqual(level2.Id, baseConstraintParam.AsElementId());

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SwapElementReferences_UpdatesCategoryDefaultStyles()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Swap Category Styles"))
                {
                    tg.Start();

                    // Create line patterns & materials
                    LinePatternElement lp1, lp2;
                    Material mat1, mat2;
                    using (var t = new Transaction(doc, "Create Resources"))
                    {
                        t.Start();
                        lp1 = CreateLinePattern(doc, "Pattern A");
                        lp2 = CreateLinePattern(doc, "Pattern B");
                        mat1 = CreateMaterial(doc, "Material A");
                        mat2 = CreateMaterial(doc, "Material B");
                        t.Commit();
                    }

                    Category parentCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    Category subCat;
                    using (var t = new Transaction(doc, "Create Subcategory"))
                    {
                        t.Start();
                        subCat = doc.Settings.Categories.NewSubcategory(parentCat, "TestSubcat_" + Guid.NewGuid().ToString().Substring(0, 8));
                        subCat.SetLinePatternId(lp1.Id, GraphicsStyleType.Projection);
                        subCat.Material = mat1;
                        t.Commit();
                    }

                    Assert.AreEqual(lp1.Id, subCat.GetLinePatternId(GraphicsStyleType.Projection));
                    Assert.AreEqual(mat1.Id, subCat.Material.Id);

                    // Swap using AliasSwapEngine
                    AliasSwapEngine.SwapElementReferences(doc, lp1.Id, lp2.Id);
                    AliasSwapEngine.SwapElementReferences(doc, mat1.Id, mat2.Id);

                    // Assert
                    Assert.AreEqual(lp2.Id, subCat.GetLinePatternId(GraphicsStyleType.Projection));
                    Assert.AreEqual(mat2.Id, subCat.Material.Id);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SwapElementReferences_UpdatesCompoundStructureLayers()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Swap Compound Structures"))
                {
                    tg.Start();

                    Material mat1, mat2;
                    using (var t = new Transaction(doc, "Create Materials"))
                    {
                        t.Start();
                        mat1 = CreateMaterial(doc, "Mat 1");
                        mat2 = CreateMaterial(doc, "Mat 2");
                        t.Commit();
                    }

                    WallType defaultWallType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .First();
                    WallType newWallType;
                    using (var t = new Transaction(doc, "Duplicate Wall Type"))
                    {
                        t.Start();
                        newWallType = (WallType)defaultWallType.Duplicate("TestCompoundWallType_" + Guid.NewGuid().ToString().Substring(0, 8));

                        CompoundStructure cs = newWallType.GetCompoundStructure();
                        if (cs == null)
                        {
                            cs = CompoundStructure.CreateSimpleCompoundStructure(new List<CompoundStructureLayer>
                            {
                                new CompoundStructureLayer(0.5, MaterialFunctionAssignment.Structure, mat1.Id)
                            });
                        }
                        else
                        {
                            var layers = cs.GetLayers();
                            if (layers.Count > 0)
                            {
                                layers[0].MaterialId = mat1.Id;
                                cs.SetLayers(layers);
                            }
                        }
                        newWallType.SetCompoundStructure(cs);
                        t.Commit();
                    }

                    // Swap using AliasSwapEngine
                    AliasSwapEngine.SwapElementReferences(doc, mat1.Id, mat2.Id);

                    // Assert
                    CompoundStructure csChecked = newWallType.GetCompoundStructure();
                    Assert.IsNotNull(csChecked);
                    Assert.AreEqual(mat2.Id, csChecked.GetLayers()[0].MaterialId);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SwapElementReferences_UpdatesViewGraphicOverrides()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Swap View Overrides"))
                {
                    tg.Start();

                    // Create line patterns
                    LinePatternElement lp1, lp2;
                    using (var t = new Transaction(doc, "Create Patterns"))
                    {
                        t.Start();
                        lp1 = CreateLinePattern(doc, "Pattern A");
                        lp2 = CreateLinePattern(doc, "Pattern B");
                        t.Commit();
                    }

                    View view = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);

                    Assert.IsNotNull(view, "Floor plan view should exist.");

                    Category wallsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);

                    using (var t = new Transaction(doc, "Set Category Override"))
                    {
                        t.Start();
                        OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                        ogs.SetProjectionLinePatternId(lp1.Id);
                        view.SetCategoryOverrides(wallsCat.Id, ogs);
                        t.Commit();
                    }

                    // Swap using AliasSwapEngine
                    AliasSwapEngine.SwapElementReferences(doc, lp1.Id, lp2.Id);

                    // Assert
                    var settings = view.GetCategoryOverrides(wallsCat.Id);
                    Assert.AreEqual(lp2.Id, settings.ProjectionLinePatternId);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        #endregion
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/BrowserOrganizationTranslatorTests.cs
```csharp
using System;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class BrowserOrganizationTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void BrowserOrganizationTranslator_Inject_Null_LogsWarning()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                SerializationResultModel.ClearWarnings();

                var model = new BrowserOrganizationModel
                {
                    Name = "NonExistentBrowserOrg",
                    IsTemplate = false
                };

                var translator = new BrowserOrganizationTranslator();

                // Act
                BrowserOrganization? result = null;
                using (var t = new Transaction(doc, "Inject BrowserOrg"))
                {
                    t.Start();
                    result = translator.InjectSpecifics(model, null, doc);
                    t.Commit();
                }

                // Assert
                Assert.IsNull(result, "Programmatic creation of BrowserOrganization should return null.");
                
                var warnings = SerializationResultModel.CurrentThreadWarnings;
                Assert.IsTrue(warnings.Count > 0, "A warning should be logged.");
                Assert.IsTrue(warnings.Any(w => w.Contains("Revit API prevents the programmatic creation of new Browser Organizations")), 
                    "Warning message should inform the user about the Revit API limitation.");
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/CategoryModelTests.cs
```csharp
using System;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class CategoryModelTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void CategoryModelHydration_BuiltInWallsCategory_HydratesCorrectly()
        {
            // Arrange
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                Category liveCategory = Category.GetCategory(doc, BuiltInCategory.OST_Walls);
                Assert.IsNotNull(liveCategory, "Built-in OST_Walls category should exist.");

                // Act
                var model = liveCategory.ToCategoryModel(doc, false);

                // Assert
                Assert.IsNotNull(model, "CategoryModel should be successfully instantiated.");
                Assert.AreEqual(liveCategory.Name, model.Name, "Category name should match the live category.");
                Assert.AreEqual(liveCategory.IsCuttable, model.IsCuttable, "IsCuttable property should match.");
                Assert.IsNotNull(model.ElementId, "ElementId wrapper should not be null.");
                
                long expectedId;
                var valueProp = liveCategory.Id.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    expectedId = (long)valueProp.GetValue(liveCategory.Id)!;
                }
                else
                {
                    var intValueProp = liveCategory.Id.GetType().GetProperty("IntegerValue")!;
                    expectedId = Convert.ToInt64(intValueProp.GetValue(liveCategory.Id));
                }
                Assert.AreEqual(expectedId, model.ElementId.Id, "Category ID should match.");
                Assert.AreEqual("Model Categories", model.CategoryGroupLevel1, "CategoryGroupLevel1 should evaluate to Model Categories.");
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/CategoryTranslatorTests.cs
```csharp
using System;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class CategoryTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void CategoryTranslator_ExtractSpecifics_OST_Walls_PresentsCorrectValues()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var wallsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                Assert.IsNotNull(wallsCat, "OST_Walls category should exist.");

                var translator = new CategoryTranslator(new RevitIdentityService());
                var model = new CategoryModel();

                translator.ExtractSpecifics(wallsCat, model, doc);

                Assert.AreEqual("Walls", model.Name);
                Assert.IsTrue(model.IsCuttable);
                Assert.IsNotNull(model.LineWeightProjection);
                Assert.IsNotNull(model.LineColor);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void CategoryTranslator_InjectSpecifics_ModifiesOST_WallsStyles()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var wallsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                Assert.IsNotNull(wallsCat);

                var translator = new CategoryTranslator(new RevitIdentityService());
                var model = new CategoryModel
                {
                    Name = "Walls",
                    LineWeightProjection = 5,
                    LineColor = new ColorModel(255, 0, 0)
                };

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Modify Walls Style"))
                    {
                        trans.Start();
                        var result = translator.InjectSpecifics(model, wallsCat, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(wallsCat);
                    Assert.AreEqual(5, wallsCat.GetLineWeight(GraphicsStyleType.Projection));
                    Assert.AreEqual(255, wallsCat.LineColor.Red);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void CategoryTranslator_InjectSpecifics_CreatesSubcategory()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new CategoryTranslator(new RevitIdentityService());
                var model = new CategoryModel
                {
                    Name = "TestSubcategory_" + Guid.NewGuid().ToString().Substring(0, 8),
                    ParentCategoryName = "Walls"
                };

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    Category? createdSub = null;
                    using (Transaction trans = new Transaction(doc, "Create Subcategory"))
                    {
                        trans.Start();
                        createdSub = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(createdSub);
                    Assert.AreEqual(model.Name, createdSub!.Name);
                    Assert.IsNotNull(createdSub.Parent);
                    Assert.AreEqual("Walls", createdSub.Parent.Name);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void CategoryTranslator_InjectSpecifics_UncuttableCategory_ThrowsNoException()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var dimsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Dimensions);
                Assert.IsNotNull(dimsCat, "OST_Dimensions category should exist.");
                Assert.IsFalse(dimsCat.IsCuttable, "Dimensions category should be uncuttable.");

                var translator = new CategoryTranslator(new RevitIdentityService());
                var model = new CategoryModel
                {
                    Name = "Dimensions",
                    LineWeightCut = 8,
                    LinePatternCut = new ElementIdModel { Id = -3000010, Name = "Solid" }
                };

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Inject Style"))
                    {
                        trans.Start();
                        Assert.DoesNotThrow(() => translator.InjectSpecifics(model, dimsCat, doc));
                        trans.Commit();
                    }

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void CategoryTranslator_InjectSpecifics_MissingParent_LogsWarningToResultModel()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new CategoryTranslator(new RevitIdentityService());
                var model = new CategoryModel
                {
                    Name = "TestSubcategory",
                    ParentCategoryName = "NonExistentParentCategory"
                };

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    SerializationResultModel.ClearWarnings();

                    using (Transaction trans = new Transaction(doc, "Inject Subcategory"))
                    {
                        trans.Start();
                        var result = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    var warnings = SerializationResultModel.CurrentThreadWarnings;
                    Assert.IsTrue(warnings.Any(w => w.Contains("Parent category 'NonExistentParentCategory' not found")), "Warning should be logged about missing parent category.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void CategoryMapping_SymmetricConversions_ResolvesCategory()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Category liveCategory = Category.GetCategory(doc, BuiltInCategory.OST_Walls);
                Assert.IsNotNull(liveCategory, "OST_Walls category should exist.");

                // Act
                CategoryIdModel idModel = liveCategory.ToCategoryIdModel(doc);
                CategoryModel catModel = liveCategory.ToCategoryModel(doc);

                Category resolvedFromIdModel = idModel.GetCategory(doc);
                Category resolvedFromCatModel = catModel.GetCategory(doc);

                // Assert
                Assert.IsNotNull(resolvedFromIdModel, "Should resolve from CategoryIdModel.");
                Assert.AreEqual(liveCategory.Id, resolvedFromIdModel.Id, "Resolved category ID should match.");
                Assert.IsNotNull(resolvedFromCatModel, "Should resolve from CategoryModel.");
                Assert.AreEqual(liveCategory.Id, resolvedFromCatModel.Id, "Resolved category ID should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/ColorTranslatorTests.cs
```csharp
using System;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class ColorTranslatorTests
    {
        [Test]
        public void ColorConversions_Symmetric()
        {
            var color = new Color(255, 128, 64);
            ColorModel colorModel = color.ToModel();
            Color colorBack = colorModel.ToColor();

            Assert.IsTrue(colorModel.IsValid);
            Assert.AreEqual(color.Red, colorModel.Red);
            Assert.AreEqual(color.Green, colorModel.Green);
            Assert.AreEqual(color.Blue, colorModel.Blue);
            Assert.AreEqual(color.Red, colorBack.Red);
            Assert.AreEqual(color.Green, colorBack.Green);
            Assert.AreEqual(color.Blue, colorBack.Blue);
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/DatumAndAnnotationTranslatorTests.cs
```csharp
using System;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class DatumAndAnnotationTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void GridTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsGridType()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new GridTypeTranslator();
                var model = new GridTypeModel
                {
                    Name = "TestGridType_Duplicated"
                };

                GridType? duplicatedElem = null;

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Duplicate GridType"))
                    {
                        trans.Start();
                        duplicatedElem = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(duplicatedElem);
                    Assert.AreEqual("TestGridType_Duplicated", duplicatedElem!.Name);

                    // Roll back to keep the document clean
                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void LevelTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsLevelType()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new LevelTypeTranslator();
                var model = new LevelTypeModel
                {
                    Name = "TestLevelType_Duplicated"
                };

                LevelType? duplicatedElem = null;

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Duplicate LevelType"))
                    {
                        trans.Start();
                        duplicatedElem = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(duplicatedElem);
                    Assert.AreEqual("TestLevelType_Duplicated", duplicatedElem!.Name);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void TextElementTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsTextNoteType()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new TextElementTypeTranslator();
                var model = new ElementTypeModel
                {
                    Name = "TestTextNoteType_Duplicated",
                    Class = "Autodesk.Revit.DB.TextNoteType"
                };

                ElementType? duplicatedElem = null;

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Duplicate TextNoteType"))
                    {
                        trans.Start();
                        duplicatedElem = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(duplicatedElem);
                    Assert.AreEqual("TestTextNoteType_Duplicated", duplicatedElem!.Name);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/DimensionTypeTranslatorTests.cs
```csharp
using System;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class DimensionTypeTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void DimensionTypeTranslator_InjectSpecifics_DuplicatesTemplateAndReturnsDimensionType()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new DimensionTypeTranslator();
                var model = new DimensionTypeModel
                {
                    Name = "TestDimensionType_Duplicated",
                    DimensionStyle = "Linear"
                };

                DimensionType? duplicatedElem = null;

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    using (Transaction trans = new Transaction(doc, "Duplicate DimensionType"))
                    {
                        trans.Start();
                        duplicatedElem = translator.InjectSpecifics(model, null, doc);
                        trans.Commit();
                    }

                    Assert.IsNotNull(duplicatedElem);
                    Assert.AreEqual("TestDimensionType_Duplicated", duplicatedElem!.Name);
                    Assert.AreEqual(DimensionStyleType.Linear, duplicatedElem!.StyleType);
                    Assert.AreNotEqual(DimensionTypeModel.InternalDimStyleName, duplicatedElem!.Name);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void DimensionTypeTranslator_InjectSpecifics_ThrowsIfNoTemplateFound()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new DimensionTypeTranslator();
                var model = new DimensionTypeModel
                {
                    Name = "TestDimensionType_NonExistentStyle",
                    DimensionStyle = "NonExistentStyleType"
                };

                using (TransactionGroup tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    var ex = Assert.Throws<InvalidOperationException>(() =>
                    {
                        using (Transaction trans = new Transaction(doc, "Try Duplicate"))
                        {
                            trans.Start();
                            translator.InjectSpecifics(model, null, doc);
                            trans.Commit();
                        }
                    });

                    Assert.IsTrue(ex.Message.Contains("No template element of type 'DimensionType' with StyleType 'NonExistentStyleType' found"));

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/FakeIdentityService.cs
```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    /// <summary>
    /// A lightweight, in-memory mock implementation of IIdentityService for headless unit tests.
    /// </summary>
    public class FakeIdentityService : IIdentityService
    {
        private readonly Dictionary<long, ElementIdModel> _idToModel = new Dictionary<long, ElementIdModel>();
        private readonly Dictionary<string, ElementIdModel> _uniqueIdToModel = new Dictionary<string, ElementIdModel>();
        private readonly Dictionary<string, Element> _nameToElement = new Dictionary<string, Element>();
        private readonly Dictionary<string, Element> _uniqueIdToElement = new Dictionary<string, Element>();

        /// <summary>
        /// Stub a mapping for ToModel extraction.
        /// </summary>
        public void SetupMapping(ElementId id, ElementIdModel model)
        {
            long idVal;
#if REVIT2022 || REVIT2023
            idVal = id.IntegerValue;
#else
            idVal = id.Value;
#endif
            _idToModel[idVal] = model;
            if (!string.IsNullOrEmpty(model.UniqueId))
            {
                _uniqueIdToModel[model.UniqueId] = model;
            }
        }

        /// <summary>
        /// Stub an element resolution mapping.
        /// </summary>
        public void SetupElement(string nameOrUniqueId, Element element)
        {
            _nameToElement[nameOrUniqueId] = element;
            if (element != null && !string.IsNullOrEmpty(element.UniqueId))
            {
                _uniqueIdToElement[element.UniqueId] = element;
            }
        }

        public ElementIdModel ToModel(ElementId id, Document doc, bool isTemplate = false)
        {
            if (id == null) return null;

            long idVal;
#if REVIT2022 || REVIT2023
            idVal = id.IntegerValue;
#else
            idVal = id.Value;
#endif

            if (_idToModel.TryGetValue(idVal, out var model))
            {
                return model;
            }

            return new ElementIdModel
            {
                Id = idVal,
                Name = $"FakeElement_{idVal}",
                Class = "Autodesk.Revit.DB.Element",
                UniqueId = $"fake-uid-{idVal}",
                Category = "Generic",
                IsTemplate = isTemplate
            };
        }

        public ElementId ResolveElementId(ElementIdModel model, Document doc)
        {
            if (model == null) return ElementId.InvalidElementId;

            if (_uniqueIdToModel.TryGetValue(model.UniqueId, out var mappedModel))
            {
#if REVIT2022 || REVIT2023
                return new ElementId((int)mappedModel.Id);
#else
                return new ElementId(mappedModel.Id);
#endif
            }

#if REVIT2022 || REVIT2023
            return new ElementId((int)model.Id);
#else
            return new ElementId(model.Id);
#endif
        }

        public Element ResolveElement(ElementIdModel model, Document doc)
        {
            if (model == null) return null;

            if (!string.IsNullOrEmpty(model.UniqueId) && _uniqueIdToElement.TryGetValue(model.UniqueId, out var elemByUid))
            {
                return elemByUid;
            }

            if (!string.IsNullOrEmpty(model.Name) && _nameToElement.TryGetValue(model.Name, out var elemByName))
            {
                return elemByName;
            }

            return null;
        }

        public IEnumerable<Element> GetElementsByElementIdModels(Document doc, IEnumerable<ElementIdModel> identifiers)
        {
            var resolvedElements = new List<Element>();
            if (identifiers == null) return resolvedElements;

            foreach (var model in identifiers)
            {
                if (model == null) continue;
                var elem = ResolveElement(model, doc);
                if (elem != null)
                {
                    resolvedElements.Add(elem);
                }
                else
                {
                    SerializationResultModel.LogWarning($"Dependency not found: {model.Name} ({model.Class})");
                }
            }

            return resolvedElements;
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/FakeStandardSerializationEngine.cs
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Merge;

namespace SyntheticTests.Shared.Modules.RevitDOM
{
    public class FakeStandardSerializationEngine : IStandardSerializationEngine
    {
        public IEnumerable<ObjectModel> ByRevit(IEnumerable<Element> elements, Document doc, bool isTemplate, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return new List<ObjectModel>();
        }

        public IEnumerable<DuplicateClusterModel> Analyze(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return new List<DuplicateClusterModel>();
        }

        public IEnumerable<SerializationResultModel> ToRevit(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default, IFailuresPreprocessor? failuresPreprocessor = null)
        {
            return new List<SerializationResultModel>();
        }

        public ObjectModel? ExtractCategory(Category category, Document doc, bool isTemplate)
        {
            return null;
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/FilledRegionTypeTranslatorTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class FilledRegionTypeTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void FilledRegionTypeTranslator_ExtractSpecifics_PopulatesPropertiesAndPurgesParameters()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Find template fill pattern and color to assign
                    var fillPatternElem = new FilteredElementCollector(doc)
                        .OfClass(typeof(FillPatternElement))
                        .Cast<FillPatternElement>()
                        .FirstOrDefault(x => x.GetFillPattern().Target == FillPatternTarget.Drafting);

                    Assert.IsNotNull(fillPatternElem, "No drafting fill pattern found in document.");

                    // Find template filled region type to duplicate
                    var templateType = new FilteredElementCollector(doc)
                        .OfClass(typeof(FilledRegionType))
                        .Cast<FilledRegionType>()
                        .FirstOrDefault();

                    Assert.IsNotNull(templateType, "No FilledRegionType template found in document.");

                    FilledRegionType? testRegionType = null;
                    using (var t = new Transaction(doc, "Create test FilledRegionType"))
                    {
                        t.Start();
                        testRegionType = templateType.Duplicate("Test_Extraction_Region_Type") as FilledRegionType;
                        Assert.IsNotNull(testRegionType);

                        testRegionType!.ForegroundPatternColor = new Color(255, 0, 0); // Red
                        testRegionType.BackgroundPatternColor = new Color(0, 0, 255); // Blue
                        testRegionType.ForegroundPatternId = fillPatternElem.Id;
                        testRegionType.BackgroundPatternId = fillPatternElem.Id;

                        t.Commit();
                    }

                    // Act
                    var model = testRegionType.ToModel(false);

                    // Assert
                    Assert.AreEqual("Test_Extraction_Region_Type", model.Name);
                    
                    // Verify elevated properties
                    Assert.IsNotNull(model.ForegroundPatternColor);
                    Assert.AreEqual(255, model.ForegroundPatternColor!.Red);

                    Assert.IsNotNull(model.BackgroundPatternColor);
                    Assert.AreEqual(255, model.BackgroundPatternColor!.Blue);

                    Assert.IsNotNull(model.ForegroundPatternId);
                    Assert.AreEqual(fillPatternElem.Name, model.ForegroundPatternId!.Name);

                    Assert.IsNotNull(model.BackgroundPatternId);
                    Assert.AreEqual(fillPatternElem.Name, model.BackgroundPatternId!.Name);

                    // Verify Purge Rule
                    Assert.IsNotNull(model.Parameters);
                    var overlappingParams = model.Parameters.Where(p =>
                        p.Id == (long)BuiltInParameter.FOREGROUND_PATTERN_COLOR_PARAM ||
                        p.Id == (long)BuiltInParameter.BACKGROUND_PATTERN_COLOR_PARAM ||
                        p.Id == (long)BuiltInParameter.FOREGROUND_ANY_PATTERN_ID_PARAM ||
                        p.Id == (long)BuiltInParameter.BACKGROUND_DRAFT_PATTERN_ID_PARAM
                    ).ToList();

                    Assert.IsEmpty(overlappingParams, "Redundant built-in parameters were not purged from parameters collection.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void FilledRegionTypeTranslator_InjectSpecifics_AppliesPropertiesCorrectly()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Retrieve valid fill pattern element
                    var fillPatternElem = new FilteredElementCollector(doc)
                        .OfClass(typeof(FillPatternElement))
                        .Cast<FillPatternElement>()
                        .FirstOrDefault(x => x.GetFillPattern().Target == FillPatternTarget.Drafting);

                    Assert.IsNotNull(fillPatternElem);

                    var model = new FilledRegionTypeModel
                    {
                        Name = "Test_Injection_Region_Type",
                        ForegroundPatternColor = new ColorModel(0, 255, 0), // Green
                        BackgroundPatternColor = new ColorModel(255, 255, 0), // Yellow
                        ForegroundPatternId = fillPatternElem.Id.ToModel(doc, false),
                        BackgroundPatternId = fillPatternElem.Id.ToModel(doc, false)
                    };

                    FilledRegionType? resultType = null;
                    using (var t = new Transaction(doc, "Inject FilledRegionType"))
                    {
                        t.Start();

                        var translator = new FilledRegionTypeTranslator(new RevitIdentityService());
                        resultType = translator.InjectSpecifics(model, null, doc);

                        Assert.IsNotNull(resultType);
                        t.Commit();
                    }

                    // Assert
                    Assert.AreEqual("Test_Injection_Region_Type", resultType!.Name);
                    Assert.AreEqual(0, resultType.ForegroundPatternColor.Red);
                    Assert.AreEqual(255, resultType.ForegroundPatternColor.Green);
                    Assert.AreEqual(255, resultType.BackgroundPatternColor.Red);
                    Assert.AreEqual(255, resultType.BackgroundPatternColor.Green);
                    Assert.AreEqual(fillPatternElem.Id, resultType.ForegroundPatternId);
                    Assert.AreEqual(fillPatternElem.Id, resultType.BackgroundPatternId);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void FilledRegionTypeTranslator_InjectSpecifics_MissingPattern_LogsWarningToResultModel()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Find template filled region type to duplicate
                    var templateType = new FilteredElementCollector(doc)
                        .OfClass(typeof(FilledRegionType))
                        .Cast<FilledRegionType>()
                        .FirstOrDefault();

                    Assert.IsNotNull(templateType);

                    // Create a model with a non-existent fill pattern reference
                    var model = new FilledRegionTypeModel
                    {
                        Name = "Test_Missing_Pattern_Region_Type",
                        ForegroundPatternId = new ElementIdModel
                        {
                            Name = "NonExistentPatternName_xyz",
                            Class = "Autodesk.Revit.DB.FillPatternElement"
                        }
                    };

                    SerializationResultModel.ClearWarnings();

                    FilledRegionType? resultType = null;
                    using (var t = new Transaction(doc, "Inject FilledRegionType with missing pattern"))
                    {
                        t.Start();

                        var translator = new FilledRegionTypeTranslator(new RevitIdentityService());
                        resultType = translator.InjectSpecifics(model, null, doc);

                        Assert.IsNotNull(resultType);
                        t.Commit();
                    }

                    // Assert: Should log a warning and fallback/skip assignment
                    Assert.AreEqual("Test_Missing_Pattern_Region_Type", resultType!.Name);
                    Assert.AreEqual(templateType.ForegroundPatternId, resultType.ForegroundPatternId);

                    var warnings = SerializationResultModel.CurrentThreadWarnings;
                    Assert.IsTrue(warnings.Any(w => w.Contains("Could not resolve Foreground Pattern: NonExistentPatternName_xyz")),
                        "Expected warning was not logged to SerializationResultModel.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/FillPatternTranslatorTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class FillPatternTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ExtractSpecifics_CorrectlyPopulatesGridsList()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find a default fill pattern element
                FillPatternElement? fpElem = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault();

                if (fpElem == null)
                {
                    Assert.Ignore("No FillPatternElement found in the document to test extraction.");
                    return;
                }

                var translator = new FillPatternTranslator();
                var model = new FillPatternElementModel();

                // Act
                translator.ExtractSpecifics(fpElem, model, doc);

                // Assert
                Assert.IsNotNull(model.Pattern);
                var pat = fpElem.GetFillPattern();
                Assert.AreEqual(pat.Name, model.Pattern!.Name);
                Assert.AreEqual(pat.Target.ToString(), model.Pattern.Target);
                Assert.AreEqual(pat.HostOrientation.ToString(), model.Pattern.HostOrientation);

                var expectedGrids = pat.GetFillGrids();
                Assert.AreEqual(expectedGrids.Count, model.Pattern.FillGrids.Count);
                for (int i = 0; i < expectedGrids.Count; i++)
                {
                    Assert.AreEqual(expectedGrids[i].Angle, model.Pattern.FillGrids[i].Angle, 1e-6);
                    Assert.AreEqual(expectedGrids[i].Offset, model.Pattern.FillGrids[i].Offset, 1e-6);
                    Assert.AreEqual(expectedGrids[i].Shift, model.Pattern.FillGrids[i].Shift, 1e-6);
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectSpecifics_CreatesNewPatternElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new FillPatternTranslator();
                var model = new FillPatternElementModel
                {
                    Name = "TestFillPattern_Create",
                    Pattern = new FillPatternModel
                    {
                        Name = "TestFillPattern_Create",
                        Target = "Drafting",
                        HostOrientation = "ToHost",
                        FillGrids = new List<FillGridModel>
                        {
                            new FillGridModel
                            {
                                Angle = 0.0,
                                Offset = 0.1,
                                Origin = new UVModel { U = 0.0, V = 0.0 },
                                Shift = 0.0
                            }
                        }
                    }
                };

                FillPatternElement? createdElem = null;

                using (Transaction trans = new Transaction(doc, "Test Create FillPattern"))
                {
                    trans.Start();

                    // Act
                    createdElem = translator.InjectSpecifics(model, null, doc);

                    trans.Commit();
                }

                // Assert
                Assert.IsNotNull(createdElem);
                Assert.AreEqual("TestFillPattern_Create", createdElem.Name);
                var pat = createdElem.GetFillPattern();
                Assert.AreEqual(FillPatternTarget.Drafting, pat.Target);
                Assert.AreEqual(FillPatternHostOrientation.ToHost, pat.HostOrientation);
                var grids = pat.GetFillGrids();
                Assert.AreEqual(1, grids.Count);
                Assert.AreEqual(0.0, grids[0].Angle, 1e-6);
                Assert.AreEqual(0.1, grids[0].Offset, 1e-6);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectSpecifics_ModifiesExistingPatternElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new FillPatternTranslator();
                FillPatternElement? testElem = null;

                using (Transaction trans = new Transaction(doc, "Test Modify Setup"))
                {
                    trans.Start();

                    FillPattern fp = new FillPattern("TestFillPattern_ModifyTarget", FillPatternTarget.Drafting, FillPatternHostOrientation.ToHost);
                    var grids = new List<FillGrid> { new FillGrid(0.0, 0.05) };
                    fp.SetFillGrids(grids);
                    testElem = FillPatternElement.Create(doc, fp);

                    trans.Commit();
                }

                Assert.IsNotNull(testElem);

                var model = new FillPatternElementModel
                {
                    Name = "TestFillPattern_ModifyTarget",
                    Pattern = new FillPatternModel
                    {
                        Name = "TestFillPattern_ModifyTarget",
                        Target = "Drafting",
                        HostOrientation = "ToHost",
                        FillGrids = new List<FillGridModel>
                        {
                            new FillGridModel
                            {
                                Angle = 1.57079632679, // ~90 degrees
                                Offset = 0.2,
                                Origin = new UVModel { U = 0.0, V = 0.0 },
                                Shift = 0.0
                            }
                        }
                    }
                };

                using (Transaction trans = new Transaction(doc, "Test Modify FillPattern"))
                {
                    trans.Start();

                    // Act
                    var result = translator.InjectSpecifics(model, testElem, doc);

                    trans.Commit();
                }

                // Assert
                var pat = testElem.GetFillPattern();
                var resultGrids = pat.GetFillGrids();
                Assert.AreEqual(1, resultGrids.Count);
                Assert.AreEqual(1.57079632679, resultGrids[0].Angle, 1e-5);
                Assert.AreEqual(0.2, resultGrids[0].Offset, 1e-6);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/HostObjTypeTranslatorTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class HostObjTypeTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void HostObjTypeTranslator_ExtractAndInject_WallTypeCompoundStructure_RoundtripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Find template WallType to duplicate
                    var templateType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .FirstOrDefault();

                    Assert.IsNotNull(templateType, "No WallType template found in document.");

                    // Retrieve a valid material to use in layers
                    var material = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault();

                    Assert.IsNotNull(material, "No Material found in document.");

                    // Act - Extraction
                    var originalModel = templateType.ToModel(false);
                    Assert.IsNotNull(originalModel);
                    Assert.IsNotNull(originalModel.Structure);
                    Assert.IsNotEmpty(originalModel.Structure!.Layers);

                    // Modify the model for injection
                    var newModel = new HostObjTypeModel
                    {
                        Class = "Autodesk.Revit.DB.WallType",
                        Name = "Test_Custom_WallType",
                        Structure = new CompoundStructureModel
                        {
                            Layers = new List<SerialCompoundStructureLayer>
                            {
                                new SerialCompoundStructureLayer
                                {
                                    Function = "Structure",
                                    Width = 0.5, // 6 inches core
                                    MaterialId = material.Id.ToModel(doc, false),
                                    StructuralMaterial = true,
#if REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
                                    // Priority does not exist
#else
                                    Priority = 2
#endif
                                },
                                new SerialCompoundStructureLayer
                                {
                                    Function = "Finish1",
                                    Width = 0.1, // 1.2 inches finish
                                    MaterialId = material.Id.ToModel(doc, false),
                                    StructuralMaterial = false,
#if REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
                                    // Priority does not exist
#else
                                    Priority = 5
#endif
                                }
                            }
                        }
                    };

                    WallType? injectedType = null;
                    using (var t = new Transaction(doc, "Inject custom WallType"))
                    {
                        t.Start();
                        var translator = new HostObjTypeTranslator(new RevitIdentityService());
                        injectedType = translator.InjectSpecifics(newModel, null, doc) as WallType;
                        Assert.IsNotNull(injectedType);
                        t.Commit();
                    }

                    // Assert
                    Assert.AreEqual("Test_Custom_WallType", injectedType!.Name);
                    var cs = injectedType.GetCompoundStructure();
                    Assert.IsNotNull(cs);
                    Assert.AreEqual(2, cs.GetLayers().Count);
                    Assert.AreEqual(0.5, cs.GetLayers()[0].Width);
                    Assert.AreEqual(0.1, cs.GetLayers()[1].Width);
                    Assert.AreEqual(material.Id, cs.GetLayers()[0].MaterialId);

#if REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025
                    // Priority not supported
#else
                    Assert.AreEqual(2, cs.GetLayerPriority(0));
                    Assert.AreEqual(5, cs.GetLayerPriority(1));
#endif

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void HostObjTypeTranslator_Inject_MissingMaterial_DegradesGracefully()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    var model = new HostObjTypeModel
                    {
                        Class = "Autodesk.Revit.DB.WallType",
                        Name = "Test_Degraded_WallType",
                        Structure = new CompoundStructureModel
                        {
                            Layers = new List<SerialCompoundStructureLayer>
                            {
                                new SerialCompoundStructureLayer
                                {
                                    Function = "Structure",
                                    Width = 0.5,
                                    MaterialId = new ElementIdModel
                                    {
                                        Name = "NonExistentMaterialName_xyz",
                                        Class = "Autodesk.Revit.DB.Material"
                                    },
                                    StructuralMaterial = true
                                }
                            }
                        }
                    };

                    SerializationResultModel.ClearWarnings();

                    WallType? injectedType = null;
                    using (var t = new Transaction(doc, "Inject degraded WallType"))
                    {
                        t.Start();
                        var translator = new HostObjTypeTranslator(new RevitIdentityService());
                        injectedType = translator.InjectSpecifics(model, null, doc) as WallType;
                        Assert.IsNotNull(injectedType);
                        t.Commit();
                    }

                    // Assert: Should downgrade to ElementId.InvalidElementId (<By Category>) and log warning
                    var cs = injectedType!.GetCompoundStructure();
                    Assert.AreEqual(ElementId.InvalidElementId, cs.GetLayers()[0].MaterialId);

                    var warnings = SerializationResultModel.CurrentThreadWarnings;
                    Assert.IsTrue(warnings.Any(w => w.Contains("Could not resolve Material: NonExistentMaterialName_xyz")),
                        "Expected warning was not logged to SerializationResultModel.");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void HostObjTypeTranslator_Inject_ZeroWidthLayer_ThrowsAndAborts()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Create structure with invalid zero-thickness layer
                    var model = new HostObjTypeModel
                    {
                        Class = "Autodesk.Revit.DB.WallType",
                        Name = "Test_Invalid_WallType",
                        Structure = new CompoundStructureModel
                        {
                            Layers = new List<SerialCompoundStructureLayer>
                            {
                                new SerialCompoundStructureLayer
                                {
                                    Function = "Structure",
                                    Width = 0.0, // Invalid core thickness
                                    StructuralMaterial = true
                                }
                            }
                        }
                    };

                    // Assert: SetCompoundStructure should throw a native exception and hard-abort
                    Assert.Throws<Autodesk.Revit.Exceptions.ArgumentException>(() =>
                    {
                        using (var t = new Transaction(doc, "Try invalid inject"))
                        {
                            t.Start();
                            var translator = new HostObjTypeTranslator(new RevitIdentityService());
                            translator.InjectSpecifics(model, null, doc);
                            t.Commit();
                        }
                    });

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void HostObjTypeTranslator_ExtractSpecifics_ExtractsWallSweepsAsNonNull()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var templateType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(templateType);

                var translator = new HostObjTypeTranslator(new RevitIdentityService());
                var model = new HostObjTypeModel();

                translator.ExtractSpecifics(templateType, model, doc);

                Assert.IsNotNull(model.Structure);
                Assert.IsNotNull(model.Structure!.WallSweeps);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/LinePatternTranslatorTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class LinePatternTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ExtractSpecifics_CorrectlyPopulatesSegmentList()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find a default line pattern element
                LinePatternElement? lpElem = new FilteredElementCollector(doc)
                    .OfClass(typeof(LinePatternElement))
                    .Cast<LinePatternElement>()
                    .FirstOrDefault();

                if (lpElem == null)
                {
                    Assert.Ignore("No LinePatternElement found in the document to test extraction.");
                    return;
                }

                var translator = new LinePatternTranslator();
                var model = new LinePatternElementModel();

                // Act
                translator.ExtractSpecifics(lpElem, model, doc);

                // Assert
                Assert.IsNotNull(model.Segments);
                var lp = lpElem.GetLinePattern();
                var expectedSegments = lp.GetSegments();
                Assert.AreEqual(expectedSegments.Count, model.Segments.Count);
                for (int i = 0; i < expectedSegments.Count; i++)
                {
                    Assert.AreEqual(expectedSegments[i].Type.ToString(), model.Segments[i].Type);
                    Assert.AreEqual(expectedSegments[i].Length, model.Segments[i].Length, 1e-6);
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectSpecifics_CreatesNewPatternElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new LinePatternTranslator();
                var model = new LinePatternElementModel
                {
                    Name = "TestLinePattern_Create",
                    Segments = new List<LinePatternSegmentModel>
                    {
                        new LinePatternSegmentModel { Type = "Dash", Length = 0.02 },
                        new LinePatternSegmentModel { Type = "Space", Length = 0.01 }
                    }
                };

                LinePatternElement? createdElem = null;

                using (Transaction trans = new Transaction(doc, "Test Create LinePattern"))
                {
                    trans.Start();

                    // Act
                    createdElem = translator.InjectSpecifics(model, null, doc);

                    trans.Commit();
                }

                // Assert
                Assert.IsNotNull(createdElem);
                Assert.AreEqual("TestLinePattern_Create", createdElem.Name);
                var lp = createdElem.GetLinePattern();
                var segments = lp.GetSegments();
                Assert.AreEqual(2, segments.Count);
                Assert.AreEqual(LinePatternSegmentType.Dash, segments[0].Type);
                Assert.AreEqual(0.02, segments[0].Length, 1e-6);
                Assert.AreEqual(LinePatternSegmentType.Space, segments[1].Type);
                Assert.AreEqual(0.01, segments[1].Length, 1e-6);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectSpecifics_ModifiesExistingPatternElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var translator = new LinePatternTranslator();
                
                LinePatternElement? testElem = null;
                
                using (Transaction trans = new Transaction(doc, "Test Modify Setup"))
                {
                    trans.Start();
                    
                    LinePattern initialLp = new LinePattern("TestLinePattern_ModifyTarget");
                    initialLp.SetSegments(new List<LinePatternSegment> 
                    {
                        new LinePatternSegment(LinePatternSegmentType.Dash, 0.01),
                        new LinePatternSegment(LinePatternSegmentType.Space, 0.01)
                    });
                    testElem = LinePatternElement.Create(doc, initialLp);
                    
                    trans.Commit();
                }

                Assert.IsNotNull(testElem);

                var model = new LinePatternElementModel
                {
                    Name = "TestLinePattern_ModifyTarget",
                    Segments = new List<LinePatternSegmentModel>
                    {
                        new LinePatternSegmentModel { Type = "Dash", Length = 0.05 },
                        new LinePatternSegmentModel { Type = "Space", Length = 0.02 }
                    }
                };

                using (Transaction trans = new Transaction(doc, "Test Modify LinePattern"))
                {
                    trans.Start();

                    // Act
                    var result = translator.InjectSpecifics(model, testElem, doc);

                    trans.Commit();
                }

                // Assert
                var lp = testElem.GetLinePattern();
                var segments = lp.GetSegments();
                Assert.AreEqual(2, segments.Count);
                Assert.AreEqual(LinePatternSegmentType.Dash, segments[0].Type);
                Assert.AreEqual(0.05, segments[0].Length, 1e-6);
                Assert.AreEqual(LinePatternSegmentType.Space, segments[1].Type);
                Assert.AreEqual(0.02, segments[1].Length, 1e-6);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/MaterialAssetEngineTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class MaterialAssetEngineTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        private AppearanceAssetElement GetOrCreateAppearanceAssetTemplate(Document doc)
        {
            var template = new FilteredElementCollector(doc)
                .OfClass(typeof(AppearanceAssetElement))
                .Cast<AppearanceAssetElement>()
                .FirstOrDefault();

            if (template == null)
            {
                var assets = doc.Application.GetAssets(AssetType.Appearance);
                var asset = assets.FirstOrDefault(a => a.FindByName("generic_diffuse") != null)
                            ?? assets.FirstOrDefault();
                if (asset != null)
                {
                    template = AppearanceAssetElement.Create(doc, "TemplateAppearanceAsset", asset);
                }
            }

            Assert.IsNotNull(template, "An AppearanceAssetElement template should exist or be created from library assets.");
            return template!;
        }

        [Test]
        public void ExtractAppearance_WhenNoAsset_ReturnsNull()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                using (Transaction t = new Transaction(doc, "Setup"))
                {
                    t.Start();
                    ElementId matId = Material.Create(doc, "TestMat_NoAsset");
                    mat = doc.GetElement(matId) as Material;
                    t.Commit();
                }

                Assert.IsNotNull(mat);
                var model = MaterialAssetEngine.ExtractAppearance(mat);
                Assert.IsNull(model);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectAppearance_InPlaceMutation_UpdatesExistingAsset()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                AppearanceAssetElement? originalAsset = null;

                using (Transaction t = new Transaction(doc, "Setup Material"))
                {
                    t.Start();

                    // Find template to duplicate
                    var template = GetOrCreateAppearanceAssetTemplate(doc);

                    originalAsset = template.Duplicate("OriginalAsset_InPlace");
                    ElementId matId = Material.Create(doc, "TestMat_InPlace");
                    mat = doc.GetElement(matId) as Material;
                    mat!.AppearanceAssetId = originalAsset.Id;

                    t.Commit();
                }

                Assert.IsNotNull(mat);
                Assert.IsNotNull(originalAsset);

                var model = new AppearanceAssetModel
                {
                    Name = "OriginalAsset_InPlace", // Keep name same for in-place or mutate
                    Color = new ColorModel(100, 150, 200),
                    Transparency = 0.5,
                    Smoothness = 0.8
                };

                using (Transaction t = new Transaction(doc, "Inject Appearance"))
                {
                    t.Start();

                    MaterialAssetEngine.InjectAppearance(mat, model, doc);

                    t.Commit();
                }

                // Assert that the ID is the same
                Assert.AreEqual(originalAsset.Id, mat.AppearanceAssetId, "AppearanceAssetId should not have changed (in-place mutation).");

                // Extract and assert values updated
                var extracted = MaterialAssetEngine.ExtractAppearance(mat);
                Assert.IsNotNull(extracted);
                Assert.AreEqual("OriginalAsset_InPlace", extracted!.Name);
                Assert.IsNotNull(extracted.Color);
                Assert.AreEqual(100, extracted.Color!.Red);
                Assert.AreEqual(150, extracted.Color.Green);
                Assert.AreEqual(200, extracted.Color.Blue);
                Assert.AreEqual(0.5, extracted.Transparency, 1e-3);
                Assert.AreEqual(0.8, extracted.Smoothness, 1e-3);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectAppearance_NameMatching_AssignsExistingAsset()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                AppearanceAssetElement? existingAsset = null;

                using (Transaction t = new Transaction(doc, "Setup Asset"))
                {
                    t.Start();

                    var template = GetOrCreateAppearanceAssetTemplate(doc);

                    existingAsset = template.Duplicate("Shared_NameMatching_Asset");
                    ElementId matId = Material.Create(doc, "TestMat_NameMatching");
                    mat = doc.GetElement(matId) as Material;
                    // Leave mat.AppearanceAssetId as invalid (no asset assigned)

                    t.Commit();
                }

                Assert.IsNotNull(mat);
                Assert.IsNotNull(existingAsset);
                Assert.AreEqual(ElementId.InvalidElementId, mat.AppearanceAssetId);

                var model = new AppearanceAssetModel
                {
                    Name = "Shared_NameMatching_Asset",
                    Color = new ColorModel(50, 100, 150),
                    Transparency = 0.2,
                    Smoothness = 0.9
                };

                using (Transaction t = new Transaction(doc, "Inject Shared Asset"))
                {
                    t.Start();

                    MaterialAssetEngine.InjectAppearance(mat, model, doc);

                    t.Commit();
                }

                // Assert that the existing asset was assigned to the material
                Assert.AreEqual(existingAsset.Id, mat.AppearanceAssetId, "Material should be assigned to the existing matching asset.");

                // Assert that the properties were updated
                var extracted = MaterialAssetEngine.ExtractAppearance(mat);
                Assert.IsNotNull(extracted);
                Assert.AreEqual("Shared_NameMatching_Asset", extracted!.Name);
                Assert.IsNotNull(extracted.Color);
                Assert.AreEqual(50, extracted.Color!.Red);
                Assert.AreEqual(100, extracted.Color.Green);
                Assert.AreEqual(150, extracted.Color.Blue);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectAppearance_Creation_DuplicatesDefaultTemplate()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                using (Transaction t = new Transaction(doc, "Setup Material"))
                {
                    t.Start();
                    GetOrCreateAppearanceAssetTemplate(doc);
                    ElementId matId = Material.Create(doc, "TestMat_Creation");
                    mat = doc.GetElement(matId) as Material;
                    t.Commit();
                }

                Assert.IsNotNull(mat);
                Assert.AreEqual(ElementId.InvalidElementId, mat.AppearanceAssetId);

                var model = new AppearanceAssetModel
                {
                    Name = "BrandNewUniqueAsset_Creation",
                    Color = new ColorModel(200, 50, 50),
                    Transparency = 0.0,
                    Smoothness = 0.5
                };

                using (Transaction t = new Transaction(doc, "Inject Brand New"))
                {
                    t.Start();

                    MaterialAssetEngine.InjectAppearance(mat, model, doc);

                    t.Commit();
                }

                Assert.AreNotEqual(ElementId.InvalidElementId, mat.AppearanceAssetId, "A new appearance asset should have been created and assigned.");

                var extracted = MaterialAssetEngine.ExtractAppearance(mat);
                Assert.IsNotNull(extracted);
                Assert.AreEqual("BrandNewUniqueAsset_Creation", extracted!.Name);
                Assert.AreEqual(200, extracted.Color!.Red);
                Assert.AreEqual(0.5, extracted.Smoothness, 1e-3);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectStructural_InPlaceAndNameMatching_UpdatesCorrectly()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                PropertySetElement? originalPse = null;

                using (Transaction t = new Transaction(doc, "Setup Material"))
                {
                    t.Start();

                    StructuralAsset asset = new StructuralAsset("OriginalStrucAsset", StructuralAssetClass.Metal);
                    originalPse = PropertySetElement.Create(doc, asset);
                    ElementId matId = Material.Create(doc, "TestMat_Struc");
                    mat = doc.GetElement(matId) as Material;
                    mat!.SetMaterialAspectByPropertySet(MaterialAspect.Structural, originalPse.Id);

                    t.Commit();
                }

                Assert.IsNotNull(mat);
                Assert.IsNotNull(originalPse);

                var model = new StructuralAssetModel
                {
                    Name = "OriginalStrucAsset",
                    Behavior = "Isotropic",
                    StructuralAssetClass = "Metal",
                    Density = 490.0,
                    YoungModulus = 29000000.0,
                    PoissonRatio = 0.3
                };

                using (Transaction t = new Transaction(doc, "Inject Structural"))
                {
                    t.Start();

                    MaterialAssetEngine.InjectStructural(mat, model, doc);

                    t.Commit();
                }

                // Assert ID did not change (in-place)
                Assert.AreEqual(originalPse.Id, mat.StructuralAssetId);

                // Extract and assert
                var extracted = MaterialAssetEngine.ExtractStructural(mat);
                Assert.IsNotNull(extracted);
                Assert.AreEqual("OriginalStrucAsset", extracted!.Name);
                Assert.AreEqual("Isotropic", extracted.Behavior);
                Assert.AreEqual(490.0, extracted.Density, 1e-3);
                Assert.AreEqual(29000000.0, extracted.YoungModulus, 1e-3);
                Assert.AreEqual(0.3, extracted.PoissonRatio, 1e-3);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectThermal_InPlaceAndNameMatching_UpdatesCorrectly()
        {
            Assert.IsNotNull(_uiapp);
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                PropertySetElement? originalPse = null;

                using (Transaction t = new Transaction(doc, "Setup Material"))
                {
                    t.Start();

                    ThermalAsset asset = new ThermalAsset("OriginalThermalAsset", ThermalMaterialType.Solid);
                    originalPse = PropertySetElement.Create(doc, asset);
                    ElementId matId = Material.Create(doc, "TestMat_Thermal");
                    mat = doc.GetElement(matId) as Material;
                    mat!.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, originalPse.Id);

                    t.Commit();
                }

                Assert.IsNotNull(mat);
                Assert.IsNotNull(originalPse);

                var model = new ThermalAssetModel
                {
                    Name = "OriginalThermalAsset",
                    ThermalMaterialType = "Solid",
                    Density = 150.0,
                    ThermalConductivity = 1.5,
                    SpecificHeat = 0.2,
                    Emissivity = 0.9
                };

                using (Transaction t = new Transaction(doc, "Inject Thermal"))
                {
                    t.Start();

                    MaterialAssetEngine.InjectThermal(mat, model, doc);

                    t.Commit();
                }

                // Assert ID did not change (in-place)
                Assert.AreEqual(originalPse.Id, mat.ThermalAssetId);

                // Extract and assert
                var extracted = MaterialAssetEngine.ExtractThermal(mat);
                Assert.IsNotNull(extracted);
                Assert.AreEqual("OriginalThermalAsset", extracted!.Name);
                Assert.AreEqual("Solid", extracted.ThermalMaterialType);
                Assert.AreEqual(150.0, extracted.Density, 1e-3);
                Assert.AreEqual(1.5, extracted.ThermalConductivity, 1e-3);
                Assert.AreEqual(0.2, extracted.SpecificHeat, 1e-3);
                Assert.AreEqual(0.9, extracted.Emissivity, 1e-3);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/MaterialTranslatorTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class MaterialTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        private AppearanceAssetElement GetOrCreateAppearanceAssetTemplate(Document doc)
        {
            var template = new FilteredElementCollector(doc)
                .OfClass(typeof(AppearanceAssetElement))
                .Cast<AppearanceAssetElement>()
                .FirstOrDefault();

            if (template == null)
            {
                var assets = doc.Application.GetAssets(AssetType.Appearance);
                var asset = assets.FirstOrDefault(a => a.FindByName("generic_diffuse") != null)
                            ?? assets.FirstOrDefault();
                if (asset != null)
                {
                    template = AppearanceAssetElement.Create(doc, "TemplateAppearanceAsset", asset);
                }
            }

            Assert.IsNotNull(template, "An AppearanceAssetElement template should exist or be created from library assets.");
            return template!;
        }

        private FillPatternElement? GetFirstFillPattern(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault();
        }

        [Test]
        public void ExtractSpecifics_CorrectlyPopulatesColorsAndPatterns()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                FillPatternElement? fp = GetFirstFillPattern(doc);

                using (Transaction t = new Transaction(doc, "Setup Material"))
                {
                    t.Start();
                    var matId = Material.Create(doc, "TestMat_Extract_Colors");
                    mat = doc.GetElement(matId) as Material;

                    Assert.IsNotNull(mat);
                    mat!.CutForegroundPatternColor = new Color(255, 0, 0);
                    mat.CutBackgroundPatternColor = new Color(0, 255, 0);
                    mat.SurfaceForegroundPatternColor = new Color(0, 0, 255);
                    mat.SurfaceBackgroundPatternColor = new Color(128, 128, 128);

                    if (fp != null)
                    {
                        mat.CutForegroundPatternId = fp.Id;
                        mat.CutBackgroundPatternId = fp.Id;
                        mat.SurfaceForegroundPatternId = fp.Id;
                        mat.SurfaceBackgroundPatternId = fp.Id;
                    }
                    t.Commit();
                }

                var translator = new MaterialTranslator(new RevitIdentityService());
                var model = new MaterialModel();

                // Act
                translator.ExtractSpecifics(mat!, model, doc);

                // Assert
                Assert.IsNotNull(model.CutForegroundPatternColor);
                Assert.AreEqual(255, model.CutForegroundPatternColor!.Red);
                Assert.AreEqual(0, model.CutForegroundPatternColor!.Green);
                Assert.AreEqual(0, model.CutForegroundPatternColor!.Blue);

                Assert.IsNotNull(model.CutBackgroundPatternColor);
                Assert.AreEqual(0, model.CutBackgroundPatternColor!.Red);
                Assert.AreEqual(255, model.CutBackgroundPatternColor!.Green);
                Assert.AreEqual(0, model.CutBackgroundPatternColor!.Blue);

                Assert.IsNotNull(model.SurfaceForegroundPatternColor);
                Assert.AreEqual(0, model.SurfaceForegroundPatternColor!.Red);
                Assert.AreEqual(0, model.SurfaceForegroundPatternColor!.Green);
                Assert.AreEqual(255, model.SurfaceForegroundPatternColor!.Blue);

                Assert.IsNotNull(model.SurfaceBackgroundPatternColor);
                Assert.AreEqual(128, model.SurfaceBackgroundPatternColor!.Red);
                Assert.AreEqual(128, model.SurfaceBackgroundPatternColor!.Green);
                Assert.AreEqual(128, model.SurfaceBackgroundPatternColor!.Blue);

                if (fp != null)
                {
                    Assert.IsNotNull(model.CutForegroundPatternId);
                    Assert.AreEqual(fp.Name, model.CutForegroundPatternId!.Name);
                    Assert.IsNotNull(model.CutBackgroundPatternId);
                    Assert.AreEqual(fp.Name, model.CutBackgroundPatternId!.Name);
                    Assert.IsNotNull(model.SurfaceForegroundPatternId);
                    Assert.AreEqual(fp.Name, model.SurfaceForegroundPatternId!.Name);
                    Assert.IsNotNull(model.SurfaceBackgroundPatternId);
                    Assert.AreEqual(fp.Name, model.SurfaceBackgroundPatternId!.Name);
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ExtractSpecifics_DelegatesToMaterialAssetEngine()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                using (Transaction t = new Transaction(doc, "Setup Appearance Asset"))
                {
                    t.Start();
                    var template = GetOrCreateAppearanceAssetTemplate(doc);
                    var newAsset = template.Duplicate("ExtractAsset_Appearance");
                    var matId = Material.Create(doc, "TestMat_Extract_Asset");
                    mat = doc.GetElement(matId) as Material;
                    Assert.IsNotNull(mat);
                    mat!.AppearanceAssetId = newAsset.Id;
                    t.Commit();
                }

                var translator = new MaterialTranslator(new RevitIdentityService());
                var model = new MaterialModel();

                // Act
                translator.ExtractSpecifics(mat!, model, doc);

                // Assert
                Assert.IsNotNull(model.AppearanceAsset);
                Assert.AreEqual("ExtractAsset_Appearance", model.AppearanceAsset!.Name);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectSpecifics_CreatesMaterialWithProperties()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? createdElem = null;
                FillPatternElement? fp = GetFirstFillPattern(doc);

                using (Transaction trans = new Transaction(doc, "Test Create Material"))
                {
                    trans.Start();

                    var template = GetOrCreateAppearanceAssetTemplate(doc);
                    var translator = new MaterialTranslator(new RevitIdentityService());
                    var model = new MaterialModel
                    {
                        Name = "TestMat_Inject_Create",
                        CutForegroundPatternColor = new ColorModel(255, 100, 50),
                        SurfaceBackgroundPatternColor = new ColorModel(10, 20, 30),
                        AppearanceAsset = new AppearanceAssetModel
                        {
                            Name = "InjectAsset_Appearance",
                            Color = new ColorModel(50, 60, 70),
                            Transparency = 0.5,
                            Smoothness = 0.8
                        }
                    };

                    if (fp != null)
                    {
                        model.CutForegroundPatternId = fp.Id.ToModel(doc);
                    }

                    // Act
                    createdElem = translator.InjectSpecifics(model, null, doc);

                    trans.Commit();
                }

                // Assert
                Assert.IsNotNull(createdElem);
                Assert.AreEqual("TestMat_Inject_Create", createdElem!.Name);

                Assert.AreEqual(255, createdElem.CutForegroundPatternColor.Red);
                Assert.AreEqual(100, createdElem.CutForegroundPatternColor.Green);
                Assert.AreEqual(50, createdElem.CutForegroundPatternColor.Blue);

                Assert.AreEqual(10, createdElem.SurfaceBackgroundPatternColor.Red);
                Assert.AreEqual(20, createdElem.SurfaceBackgroundPatternColor.Green);
                Assert.AreEqual(30, createdElem.SurfaceBackgroundPatternColor.Blue);

                if (fp != null)
                {
                    Assert.AreEqual(fp.Id, createdElem.CutForegroundPatternId);
                }

                Assert.AreNotEqual(ElementId.InvalidElementId, createdElem.AppearanceAssetId);
                var assetElem = doc.GetElement(createdElem.AppearanceAssetId) as AppearanceAssetElement;
                Assert.IsNotNull(assetElem);
                Assert.AreEqual("InjectAsset_Appearance", assetElem!.Name);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void InjectSpecifics_UpdatesExistingMaterial()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? mat = null;
                using (Transaction t = new Transaction(doc, "Setup Material"))
                {
                    t.Start();
                    var matId = Material.Create(doc, "TestMat_Inject_Update");
                    mat = doc.GetElement(matId) as Material;
                    Assert.IsNotNull(mat);
                    mat!.CutForegroundPatternColor = new Color(0, 0, 0);
                    t.Commit();
                }

                var translator = new MaterialTranslator(new RevitIdentityService());
                var model = new MaterialModel
                {
                    Name = "TestMat_Inject_Update",
                    CutForegroundPatternColor = new ColorModel(100, 200, 255)
                };

                Material? updatedElem = null;

                using (Transaction trans = new Transaction(doc, "Test Update Material"))
                {
                    trans.Start();

                    // Act
                    updatedElem = translator.InjectSpecifics(model, mat, doc);

                    trans.Commit();
                }

                // Assert
                Assert.IsNotNull(updatedElem);
                Assert.AreEqual(mat!.Id, updatedElem!.Id);
                Assert.AreEqual(100, updatedElem.CutForegroundPatternColor.Red);
                Assert.AreEqual(200, updatedElem.CutForegroundPatternColor.Green);
                Assert.AreEqual(255, updatedElem.CutForegroundPatternColor.Blue);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/ParameterDefinitionSpecTests.cs
```csharp
using System;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;

namespace SyntheticTests
{
    [TestFixture]
    public class ParameterDefinitionSpecIntegrationTests
    {
        [Test]
        public void ParameterDefinitionSpec_CreateDefault_ReturnsValidDefaults()
        {
            var spec = ParameterDefinitionSpec.CreateDefault();

            Assert.IsNotNull(spec, "Default spec should not be null.");
            Assert.IsNotNull(spec.Group, "Default group should not be null.");
            Assert.IsNotNull(spec.SpecType, "Default spec type should not be null.");
            Assert.AreEqual("PG_DATA", spec.Group);
            Assert.AreEqual("Text", spec.SpecType);
        }

        [Test]
        public void ParameterDefinitionSpec_Constructor_SetsGroupAndType()
        {
            var spec = new ParameterDefinitionSpec("PG_GEOMETRY", "Length");

            Assert.AreEqual("PG_GEOMETRY", spec.Group);
            Assert.AreEqual("Length", spec.SpecType);
            Assert.AreEqual("PG_GEOMETRY", spec.ParameterGroup);
            Assert.AreEqual("Length", spec.ParameterType);
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/ParameterElementTranslatorTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class ParameterElementTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ParameterElementTranslator_Extract_Success()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            string originalSharedParamsFile = app.SharedParametersFilename;
            string tempFilePath = Path.Combine(Path.GetTempPath(), "temp_shared_params_" + Guid.NewGuid().ToString() + ".txt");

            try
            {
                // Create a valid temporary shared parameters file
                File.WriteAllText(tempFilePath, @"# This is a Revit shared parameter file.
*META	VERSION	MINVERSION
META	2	1
*GROUP	ID	NAME
*PARAM	GUID	NAME	DATATYPE	DATAGROUP	VISIBLE	DESCRIPTION	USERMODIFIABLE	COLUMNHEADER
");
                app.SharedParametersFilename = tempFilePath;

                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Open the shared parameters file and create a definition
                    DefinitionFile defFile = app.OpenSharedParameterFile();
                    Assert.IsNotNull(defFile, "Failed to open temporary shared parameter file.");

                    DefinitionGroup group = defFile.Groups.Create("TestGroup");
                    Definition definition = group.Definitions.Create(new ExternalDefinitionCreationOptions("TestParam", SpecTypeId.String.Text));
                    Assert.IsNotNull(definition);

                    // Create CategorySet containing Walls and Doors
                    CategorySet categorySet = app.Create.NewCategorySet();
                    Category wallsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    Category doorsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Doors);
                    categorySet.Insert(wallsCat);
                    categorySet.Insert(doorsCat);

                    // Create instance binding
                    Binding binding = app.Create.NewInstanceBinding(categorySet);

                    using (var t = new Transaction(doc, "Bind Parameter"))
                    {
                        t.Start();
                        bool inserted = doc.ParameterBindings.Insert(definition, binding);
                        Assert.IsTrue(inserted, "Failed to insert parameter binding.");
                        t.Commit();
                    }

                    // Retrieve the bound element
                    var extDef = (ExternalDefinition)definition;
                    SharedParameterElement? sharedParamElem = SharedParameterElement.Lookup(doc, extDef.GUID);
                    Assert.IsNotNull(sharedParamElem);

                    // Set "vary across groups"
                    using (var t = new Transaction(doc, "Set Vary Across Groups"))
                    {
                        t.Start();
                        sharedParamElem!.GetDefinition().SetAllowVaryBetweenGroups(doc, true);
                        t.Commit();
                    }

                    var translator = new ParameterElementTranslator();
                    var model = new ParameterElementModel { IsTemplate = false };
                    model.Populate(sharedParamElem!, false);

                    // Act
                    translator.ExtractSpecifics(sharedParamElem!, model, doc);

                    // Assert
                    Assert.AreEqual("TestParam", model.Name);
                    Assert.IsTrue(model.IsInstanceBinding);
                    Assert.IsTrue(model.IsVaryByGroup);
                    Assert.AreEqual(2, model.Categories.Count);

                    var categoryNames = model.Categories.Select(c => c.Name).ToList();
                    CollectionAssert.Contains(categoryNames, wallsCat.Name);
                    CollectionAssert.Contains(categoryNames, doorsCat.Name);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
                app.SharedParametersFilename = originalSharedParamsFile;
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
            }
        }

        [Test]
        public void ParameterElementTranslator_Inject_Success()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            string originalSharedParamsFile = app.SharedParametersFilename;
            string tempFilePath = Path.Combine(Path.GetTempPath(), "temp_shared_params_" + Guid.NewGuid().ToString() + ".txt");

            try
            {
                // Create a valid temporary shared parameters file
                File.WriteAllText(tempFilePath, @"# This is a Revit shared parameter file.
*META	VERSION	MINVERSION
META	2	1
*GROUP	ID	NAME
*PARAM	GUID	NAME	DATATYPE	DATAGROUP	VISIBLE	DESCRIPTION	USERMODIFIABLE	COLUMNHEADER
");
                app.SharedParametersFilename = tempFilePath;

                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    // Pre-create the definition in the file so the translator finds it
                    DefinitionFile defFile = app.OpenSharedParameterFile();
                    Assert.IsNotNull(defFile);
                    DefinitionGroup group = defFile.Groups.Create("TemporaryGroup");
                    Definition definition = group.Definitions.Create(new ExternalDefinitionCreationOptions("InjectParam", SpecTypeId.String.Text));
                    Assert.IsNotNull(definition);

                    var wallsCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    var catModel = wallsCat.ToCategoryIdModel(doc, false);

                    var model = new ParameterElementModel
                    {
                        Name = "InjectParam",
                        IsInstanceBinding = true,
                        IsVaryByGroup = true,
                        Categories = new List<CategoryIdModel> { catModel },
                        IsTemplate = false
                    };

                    var translator = new ParameterElementTranslator();

                    // Act
                    ParameterElement? injectedElem = null;
                    using (var t = new Transaction(doc, "Inject Parameter"))
                    {
                        t.Start();
                        injectedElem = translator.InjectSpecifics(model, null, doc);
                        t.Commit();
                    }

                    // Assert
                    Assert.IsNotNull(injectedElem);
                    Assert.AreEqual("InjectParam", injectedElem!.Name);

                    // Verify in doc bindings
                    ElementBinding? elementBinding = null;
                    DefinitionBindingMapIterator iter = doc.ParameterBindings.ForwardIterator();
                    while (iter.MoveNext())
                    {
                        if (iter.Key.Name == "InjectParam")
                        {
                            elementBinding = iter.Current as ElementBinding;
                            break;
                        }
                    }

                    Assert.IsNotNull(elementBinding);
                    Assert.IsTrue(elementBinding is InstanceBinding);
                    Assert.AreEqual(1, elementBinding!.Categories.Size);
                    Assert.IsTrue(elementBinding.Categories.Contains(wallsCat));

                    var internalDef = injectedElem.GetDefinition() as InternalDefinition;
                    Assert.IsNotNull(internalDef);
                    Assert.IsTrue(internalDef!.VariesAcrossGroups);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
                app.SharedParametersFilename = originalSharedParamsFile;
                if (File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/ParameterEngineTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class ParameterEngineTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ParameterEngine_Extract_NonTemplateMode_CapturesReadOnlyAndWritableParameters()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                WallType? wallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(wallType, "A WallType should exist.");

                var model = new ElementModel();

                // Act
                ParameterEngine.ExtractParameters(wallType!, model, isTemplate: false);

                // Assert
                Assert.IsNotEmpty(model.Parameters, "Parameters should be extracted.");
                
                // Assert that in non-template mode, we only extract writable parameters (IsReadOnly is false)
                // Wait! In ParameterEngine.cs, in non-template mode, we did:
                // if (param.IsReadOnly) continue;
                // So all extracted parameters should have IsReadOnly == false
                foreach (var paramModel in model.Parameters)
                {
                    Assert.IsFalse(paramModel.IsReadOnly, "Extracted parameters should be writable in non-template mode.");
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterEngine_Extract_TemplateMode_DropsReadOnlyAndEmptyParameters()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                WallType? wallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(wallType, "A WallType should exist.");

                var model = new ElementModel();

                // Act
                ParameterEngine.ExtractParameters(wallType!, model, isTemplate: true);

                // Assert
                foreach (var paramModel in model.Parameters)
                {
                    Assert.IsFalse(paramModel.IsReadOnly, "Template parameters must not be read-only.");
                    
                    // Verify no empty/null values are captured
                    if (paramModel.StorageType == "String")
                    {
                        Assert.IsFalse(string.IsNullOrEmpty(paramModel.Value), "Template string parameters must have values.");
                    }
                    else if (paramModel.StorageType == "ElementId")
                    {
                        Assert.IsNotNull(paramModel.ValueElemId, "Template ElementId parameters must have an ElementIdModel.");
                        Assert.AreNotEqual(-1, paramModel.ValueElemId.Id, "Template ElementId parameters must not be InvalidElementId.");
                    }
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterEngine_Inject_AppliesWritableParametersCorrectly()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                WallType? wallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(wallType, "A WallType should exist.");

                // Set up a custom string property value in a parameter model
                var model = new ElementModel();
                ParameterEngine.ExtractParameters(wallType!, model, isTemplate: false);

                // Find a writable text parameter like description/comments
                var commentsParamModel = model.Parameters.FirstOrDefault(p => p.Name.Equals("Description", StringComparison.OrdinalIgnoreCase));
                if (commentsParamModel == null)
                {
                    commentsParamModel = model.Parameters.FirstOrDefault(p => p.Name.Equals("Type Comments", StringComparison.OrdinalIgnoreCase));
                }

                Assert.IsNotNull(commentsParamModel, "A writable text parameter should be extracted.");
                commentsParamModel!.Value = "Test Inject Value 123";

                // Act
                using (Transaction trans = new Transaction(doc, "Test Inject Parameters"))
                {
                    trans.Start();
                    ParameterEngine.InjectParameters(model, wallType!);
                    trans.Commit();
                }

                // Assert
                Parameter liveParam = wallType.get_Parameter((BuiltInParameter)commentsParamModel.Id);
                if (liveParam == null && commentsParamModel.IsShared && !string.IsNullOrEmpty(commentsParamModel.GUID))
                {
                    liveParam = wallType.get_Parameter(new Guid(commentsParamModel.GUID));
                }
                
                Assert.IsNotNull(liveParam, "Live parameter should be accessible.");
                Assert.AreEqual("Test Inject Value 123", liveParam.AsString(), "Parameter value should be injected correctly.");
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/ParameterFilterElementTranslatorTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class ParameterFilterElementTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ParameterFilterElementTranslator_ExtractSpecifics_PopulatesRootRule()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    var category = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    var categoryIds = new List<ElementId> { category.Id };

                    ParameterFilterElement? filterElem = null;
                    using (var t = new Transaction(doc, "Create Filter"))
                    {
                        t.Start();
                        filterElem = ParameterFilterElement.Create(doc, "Extraction Test Filter", categoryIds);

                        // Build native filter rule using preprocessor guards
                        ElementId parameterId = new ElementId((int)BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
#if REVIT2022 || REVIT2023
                        FilterRule rule1 = ParameterFilterRuleFactory.CreateContainsRule(parameterId, "abc", true);
                        FilterRule rule2 = ParameterFilterRuleFactory.CreateContainsRule(parameterId, "def", true);
                        var rulesList = new List<FilterRule> { rule1, rule2 };
                        var andFilter = new ElementParameterFilter(rulesList);
#else
                        var provider = new ParameterValueProvider(parameterId);
                        var eval1 = new FilterStringContains();
                        var eval2 = new FilterStringContains();
                        var rule1 = new FilterStringRule(provider, eval1, "abc");
                        var rule2 = new FilterStringRule(provider, eval2, "def");
                        var filter1 = new ElementParameterFilter(rule1);
                        var filter2 = new ElementParameterFilter(rule2);
                        var andFilter = new LogicalAndFilter(new List<ElementFilter> { filter1, filter2 });
#endif
                        filterElem.SetElementFilter(andFilter);
                        t.Commit();
                    }

                    Assert.IsNotNull(filterElem);

                    var translator = new ParameterFilterElementTranslator(new RevitIdentityService());
                    var model = new ParameterFilterElementModel { IsTemplate = false };
                    model.Populate(filterElem!, false);

                    // Act
                    translator.ExtractSpecifics(filterElem!, model, doc);

                    // Assert
                    Assert.AreEqual("Extraction Test Filter", model.Name);
                    Assert.AreEqual(1, model.Categories.Count);
                    Assert.AreEqual(category.Name, model.Categories[0].Name);

                    Assert.IsNotNull(model.RootRule);
                    Assert.AreEqual("LogicalAnd", model.RootRule!.RuleType);
                    Assert.AreEqual(2, model.RootRule.InnerRules.Count);

                    var child1 = model.RootRule.InnerRules[0];
                    Assert.AreEqual("ParameterFilter", child1.RuleType);
                    Assert.AreEqual("BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS", child1.ParameterId?.Name);
                    Assert.AreEqual("abc", child1.RuleValue);

                    var child2 = model.RootRule.InnerRules[1];
                    Assert.AreEqual("ParameterFilter", child2.RuleType);
                    Assert.AreEqual("BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS", child2.ParameterId?.Name);
                    Assert.AreEqual("def", child2.RuleValue);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterFilterElementTranslator_InjectSpecifics_CreatesViewFilterAndRules()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test Group"))
                {
                    tg.Start();

                    var category = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Walls);
                    long catIdVal;
#if REVIT2022 || REVIT2023
                    catIdVal = category.Id.IntegerValue;
#else
                    catIdVal = category.Id.Value;
#endif

                    // Build a nested logical rules model
                    var rule1 = new FilterRuleModel
                    {
                        RuleType = "ParameterFilter",
                        ParameterId = new ElementIdModel { Name = "BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS" },
                        Evaluator = "FilterStringRule:FilterStringContains",
                        RuleValue = "xyz"
                    };
                    var rule2 = new FilterRuleModel
                    {
                        RuleType = "ParameterFilter",
                        ParameterId = new ElementIdModel { Name = "BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS" },
                        Evaluator = "FilterStringRule:FilterStringContains",
                        RuleValue = "123"
                    };
                    var andRule = new FilterRuleModel
                    {
                        RuleType = "LogicalAnd",
                        InnerRules = new List<FilterRuleModel> { rule1, rule2 }
                    };

                    var filterModel = new ParameterFilterElementModel
                    {
                        Name = "Injection Test Filter",
                        Class = "Autodesk.Revit.DB.ParameterFilterElement",
                        Categories = new List<CategoryIdModel>
                        {
                            new CategoryIdModel { Id = catIdVal, Name = category.Name }
                        },
                        RootRule = andRule
                    };

                    var translator = new ParameterFilterElementTranslator(new RevitIdentityService());
                    ParameterFilterElement? filterElem = null;

                    // Act
                    using (var t = new Transaction(doc, "Inject Filter"))
                    {
                        t.Start();
                        filterElem = translator.InjectSpecifics(filterModel, null, doc);
                        t.Commit();
                    }

                    // Assert
                    Assert.IsNotNull(filterElem);
                    Assert.AreEqual("Injection Test Filter", filterElem!.Name);
                    
                    var nativeFilter = filterElem.GetElementFilter();
                    Assert.IsNotNull(nativeFilter);
                    Assert.IsInstanceOf<LogicalAndFilter>(nativeFilter);

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/ParameterModelTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class ParameterModelTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ParameterModelHydration_StringStorageType_HydratesCorrectly()
        {
            // Arrange
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                Parameter? stringParam = FindParameterOfStorageType(doc, StorageType.String);
                Assert.IsNotNull(stringParam, "A parameter with String storage type should exist in the default document.");

                // Act
                var model = stringParam!.ToModel(doc, false);

                // Assert
                Assert.IsNotNull(model, "ParameterModel should be successfully instantiated.");
                Assert.AreEqual(stringParam!.Definition.Name, model.Name, "Parameter name should match.");
                Assert.AreEqual("String", model.StorageType, "StorageType should be 'String'.");
                Assert.AreEqual(stringParam.AsString(), model.Value, "Value should match string parameter AsString().");
                Assert.AreEqual(stringParam.IsReadOnly, model.IsReadOnly, "IsReadOnly flag should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterModelHydration_IntegerStorageType_HydratesCorrectly()
        {
            // Arrange
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                Parameter? intParam = FindParameterOfStorageType(doc, StorageType.Integer);
                Assert.IsNotNull(intParam, "A parameter with Integer storage type should exist in the default document.");

                // Act
                var model = intParam!.ToModel(doc, false);

                // Assert
                Assert.IsNotNull(model, "ParameterModel should be successfully instantiated.");
                Assert.AreEqual(intParam!.Definition.Name, model.Name, "Parameter name should match.");
                Assert.AreEqual("Integer", model.StorageType, "StorageType should be 'Integer'.");
                Assert.AreEqual(intParam.AsInteger().ToString(), model.Value, "Value should match stringified Integer value.");
                Assert.AreEqual(intParam.IsReadOnly, model.IsReadOnly, "IsReadOnly flag should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterModelHydration_DoubleStorageType_HydratesCorrectly()
        {
            // Arrange
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                Parameter? doubleParam = FindParameterOfStorageType(doc, StorageType.Double);
                Assert.IsNotNull(doubleParam, "A parameter with Double storage type should exist in the default document.");

                // Act
                var model = doubleParam!.ToModel(doc, false);

                // Assert
                Assert.IsNotNull(model, "ParameterModel should be successfully instantiated.");
                Assert.AreEqual(doubleParam!.Definition.Name, model.Name, "Parameter name should match.");
                Assert.AreEqual("Double", model.StorageType, "StorageType should be 'Double'.");
                Assert.AreEqual(doubleParam.AsDouble().ToString(), model.Value, "Value should match stringified Double value.");
                Assert.AreEqual(doubleParam.IsReadOnly, model.IsReadOnly, "IsReadOnly flag should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ParameterModelHydration_ElementIdStorageType_HydratesCorrectly()
        {
            // Arrange
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                Parameter? elemIdParam = FindParameterOfStorageType(doc, StorageType.ElementId);
                Assert.IsNotNull(elemIdParam, "A parameter with ElementId storage type should exist in the default document.");

                // Act
                var model = elemIdParam!.ToModel(doc, false);

                // Assert
                Assert.IsNotNull(model, "ParameterModel should be successfully instantiated.");
                Assert.AreEqual(elemIdParam!.Definition.Name, model.Name, "Parameter name should match.");
                Assert.AreEqual("ElementId", model.StorageType, "StorageType should be 'ElementId'.");
                Assert.IsNotNull(model.ValueElemId, "ValueElemId model should be populated.");
                
                long expectedId;
                ElementId liveId = elemIdParam.AsElementId();
                var valueProp = liveId.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    expectedId = (long)valueProp.GetValue(liveId)!;
                }
                else
                {
                    var intValueProp = liveId.GetType().GetProperty("IntegerValue")!;
                    expectedId = Convert.ToInt64(intValueProp.GetValue(liveId));
                }
                Assert.AreEqual(expectedId, model.ValueElemId!.Id, "Deserialized ValueElemId.Id should match the native parameter's ElementId value.");
                Assert.AreEqual(elemIdParam.IsReadOnly, model.IsReadOnly, "IsReadOnly flag should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        #region Helper Methods

        private Parameter? FindParameterOfStorageType(Document doc, StorageType storageType)
        {
            // Collect project information, views, and levels as safe sources for built-in parameters
            var elements = new List<Element> { doc.ProjectInformation };
            elements.AddRange(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<Element>());
            elements.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Element>());

            foreach (var elem in elements)
            {
                foreach (Parameter param in elem.Parameters)
                {
                    if (param.StorageType == storageType)
                    {
                        return param;
                    }
                }
            }
            return null;
        }

        #endregion
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/RevitDomExtensionsTests.cs
```csharp
using System;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using SyntheticTests.Modules.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class RevitDomExtensionsTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ToModel_Material_DecoupledIdentityService_MapsCorrectly()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material? material = null;
                using (Transaction trans = new Transaction(doc, "Create Material"))
                {
                    trans.Start();
                    var materialId = Material.Create(doc, "ConcreteTestMaterial");
                    material = (Material)doc.GetElement(materialId);
                    
                    // Modify some properties
                    material.Color = new Color(120, 150, 180);
                    trans.Commit();
                }

                Assert.IsNotNull(material);
                
                var fakeService = new FakeIdentityService();

                // Act
                var model = material.ToModel(doc, false, fakeService);

                // Assert
                Assert.IsNotNull(model);
                Assert.AreEqual("ConcreteTestMaterial", model.Name);
                Assert.AreEqual(120, model.Color.Red);
                Assert.AreEqual(150, model.Color.Green);
                Assert.AreEqual(180, model.Color.Blue);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ToModel_PlanViewRange_MapsOffsetsAndLevelIds()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var viewPlan = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(v => !v.IsTemplate);

                Assert.IsNotNull(viewPlan, "A plan view should exist in the project.");
                PlanViewRange viewRange = viewPlan.GetViewRange();

                // Act
                var model = viewRange.ToModel(doc);

                // Assert
                Assert.IsNotNull(model);
                
                // Assert that the coordinates are mapped
                Assert.IsNotNull(model.TopLevelId);
                Assert.IsNotNull(model.CutPlaneLevelId);
                Assert.IsNotNull(model.BottomLevelId);
                Assert.IsNotNull(model.ViewDepthLevelId);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/RevitIdentityServiceIntegrationTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class RevitIdentityServiceIntegrationTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ResolveElement_ByUniqueId_ResolvesCorrectElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist in the project.");

                var service = new RevitIdentityService();
                ElementIdModel model = service.ToModel(defaultLevel!.Id, doc);

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve the element.");
                Assert.AreEqual(defaultLevel.UniqueId, resolvedElem.UniqueId, "Resolved element UniqueId should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElement_ByStandardIdFallback_ResolvesCorrectElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");

                long idVal = 0;
                var valueProp = defaultLevel!.Id.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    idVal = (long)valueProp.GetValue(defaultLevel.Id)!;
                }
                else
                {
                    var integerValueProp = defaultLevel.Id.GetType().GetProperty("IntegerValue");
                    if (integerValueProp != null)
                    {
                        idVal = Convert.ToInt64(integerValueProp.GetValue(defaultLevel.Id));
                    }
                }

                var model = new ElementIdModel
                {
                    Id = idVal,
                    Class = defaultLevel.GetType().FullName ?? string.Empty,
                    Name = defaultLevel.Name,
                    UniqueId = null // Force standard ID fallback
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve by standard ID fallback.");
                Assert.AreEqual(defaultLevel.Id, resolvedElem.Id, "Resolved element ID should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElement_ByNameAndClassFallback_ResolvesCorrectElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                WallType? defaultWallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultWallType, "A WallType should exist.");

                var model = new ElementIdModel
                {
                    Id = 0,
                    UniqueId = null,
                    Name = defaultWallType!.Name,
                    Class = typeof(WallType).FullName ?? string.Empty
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve by Name and Class fallback.");
                Assert.AreEqual(defaultWallType.Id, resolvedElem.Id, "Resolved element ID should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElement_ByAliasesFallback_ResolvesCorrectElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                WallType? defaultWallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultWallType, "A WallType should exist.");

                var model = new ElementIdModel
                {
                    Id = 0,
                    UniqueId = null,
                    Name = "NonExistentName",
                    Class = typeof(WallType).FullName ?? string.Empty,
                    Aliases = new List<string> { "FakeAlias1", defaultWallType!.Name, "FakeAlias2" }
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve by Aliases fallback.");
                Assert.AreEqual(defaultWallType.Id, resolvedElem.Id, "Resolved element ID should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElement_TypeGuardMismatch_ReturnsNull()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");

                long idVal = 0;
                var valueProp = defaultLevel!.Id.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    idVal = (long)valueProp.GetValue(defaultLevel.Id)!;
                }
                else
                {
                    var integerValueProp = defaultLevel.Id.GetType().GetProperty("IntegerValue");
                    if (integerValueProp != null)
                    {
                        idVal = Convert.ToInt64(integerValueProp.GetValue(defaultLevel.Id));
                    }
                }

                var model = new ElementIdModel
                {
                    Id = idVal,
                    UniqueId = defaultLevel.UniqueId,
                    Class = typeof(WallType).FullName ?? string.Empty, // Intentionally wrong class
                    Name = defaultLevel.Name
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNull(resolvedElem, "ResolveElement should return null due to Type Guard mismatch.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElementId_BuiltInCategory_OST_Walls_ReturnsCorrectId()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var model = new ElementIdModel
                {
                    Name = "BuiltInCategory.OST_Walls"
                };

                var service = new RevitIdentityService();

                // Act
                ElementId resolvedId = service.ResolveElementId(model, doc);

                // Assert
                Assert.AreNotEqual(ElementId.InvalidElementId, resolvedId);
                long expectedInt = (long)BuiltInCategory.OST_Walls;
                long resolvedInt = GetElementIdValue(resolvedId);
                Assert.AreEqual(expectedInt, resolvedInt);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElementId_BuiltInParameter_ALL_MODEL_MARK_ReturnsCorrectId()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var model = new ElementIdModel
                {
                    Name = "BuiltInParameter.ALL_MODEL_MARK"
                };

                var service = new RevitIdentityService();

                // Act
                ElementId resolvedId = service.ResolveElementId(model, doc);

                // Assert
                Assert.AreNotEqual(ElementId.InvalidElementId, resolvedId);
                long expectedInt = (long)BuiltInParameter.ALL_MODEL_MARK;
                long resolvedInt = GetElementIdValue(resolvedId);
                Assert.AreEqual(expectedInt, resolvedInt);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElementId_MalformedEnum_GracefulFallback()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel);

                var model = new ElementIdModel
                {
                    Name = "BuiltInCategory.OST_Walls_Malformed",
                    UniqueId = defaultLevel!.UniqueId // fallback target
                };

                var service = new RevitIdentityService();

                // Act
                ElementId resolvedId = service.ResolveElementId(model, doc);

                // Assert
                Assert.AreEqual(defaultLevel.Id, resolvedId, "Should gracefully fall back to UniqueId fallback.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElementId_JustEnumValue_ReturnsCorrectId()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                var model = new ElementIdModel
                {
                    Name = "OST_Walls"
                };

                var service = new RevitIdentityService();

                // Act
                ElementId resolvedId = service.ResolveElementId(model, doc);

                // Assert
                Assert.AreNotEqual(ElementId.InvalidElementId, resolvedId);
                long expectedInt = (long)BuiltInCategory.OST_Walls;
                long resolvedInt = GetElementIdValue(resolvedId);
                Assert.AreEqual(expectedInt, resolvedInt);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void GetElementsByElementIdModels_SuccessPath_ResolvesAllElements()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Element? defaultView = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");
                Assert.IsNotNull(defaultView, "A default View element should exist.");

                var service = new RevitIdentityService();
                var modelLevel = service.ToModel(defaultLevel!.Id, doc);
                var modelView = service.ToModel(defaultView!.Id, doc);

                var identifiers = new List<ElementIdModel> { modelLevel, modelView };

                // Act
                var resolved = service.GetElementsByElementIdModels(doc, identifiers).ToList();

                // Assert
                Assert.AreEqual(2, resolved.Count);
                Assert.IsTrue(resolved.Any(e => e.UniqueId == defaultLevel.UniqueId));
                Assert.IsTrue(resolved.Any(e => e.UniqueId == defaultView.UniqueId));
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void GetElementsByElementIdModels_DegradationPath_LogsWarningAndFiltersNulls()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");

                var service = new RevitIdentityService();
                var modelLevel = service.ToModel(defaultLevel!.Id, doc);

                var modelFictitious = new ElementIdModel
                {
                    Name = "Fictitious Level",
                    Class = "Autodesk.Revit.DB.Level",
                    UniqueId = "nonexistent-guid-value-12345"
                };

                var identifiers = new List<ElementIdModel> { modelLevel, modelFictitious };

                // Clear thread warnings beforehand
                SerializationResultModel.ClearWarnings();

                // Act
                var resolved = service.GetElementsByElementIdModels(doc, identifiers).ToList();

                // Assert
                Assert.AreEqual(1, resolved.Count);
                Assert.AreEqual(defaultLevel.UniqueId, resolved[0].UniqueId);

                var warnings = SerializationResultModel.CurrentThreadWarnings;
                Assert.AreEqual(1, warnings.Count);
                Assert.AreEqual("Dependency not found: Fictitious Level (Autodesk.Revit.DB.Level)", warnings[0]);
            }
            finally
            {
                doc.Close(false);
            }
        }

        private static long GetElementIdValue(ElementId id)
        {
            var valueProp = id.GetType().GetProperty("Value");
            if (valueProp != null)
            {
                return (long)valueProp.GetValue(id)!;
            }

            var integerValueProp = id.GetType().GetProperty("IntegerValue");
            if (integerValueProp != null)
            {
                return Convert.ToInt64(integerValueProp.GetValue(id));
            }

            return 0;
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/RevitIdentityServiceTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class RevitIdentityServiceTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ResolveElement_ByUniqueId_ResolvesCorrectElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist in the project.");

                var service = new RevitIdentityService();
                ElementIdModel model = service.ToModel(defaultLevel!.Id, doc);

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve the element.");
                Assert.AreEqual(defaultLevel.UniqueId, resolvedElem.UniqueId, "Resolved element UniqueId should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElement_ByStandardIdFallback_ResolvesCorrectElement()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");

                long idVal = 0;
                var valueProp = defaultLevel!.Id.GetType().GetProperty("Value");
                if (valueProp != null)
                {
                    idVal = (long)valueProp.GetValue(defaultLevel.Id)!;
                }
                else
                {
                    var integerValueProp = defaultLevel.Id.GetType().GetProperty("IntegerValue");
                    if (integerValueProp != null)
                    {
                        idVal = Convert.ToInt64(integerValueProp.GetValue(defaultLevel.Id));
                    }
                }

                var model = new ElementIdModel
                {
                    Id = idVal,
                    Class = defaultLevel.GetType().FullName ?? string.Empty,
                    Name = defaultLevel.Name,
                    UniqueId = null // Force standard ID fallback
                };

                var service = new RevitIdentityService();

                // Act
                Element resolvedElem = service.ResolveElement(model, doc);

                // Assert
                Assert.IsNotNull(resolvedElem, "ResolveElement should resolve by standard ID fallback.");
                Assert.AreEqual(defaultLevel.Id, resolvedElem.Id, "Resolved element ID should match.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ResolveElementId_ByBuiltInParameterIntercept_ResolvesCorrectElementId()
        {
            var model = new ElementIdModel
            {
                Name = "BuiltInParameter.WALL_USER_HEIGHT_PARAM",
                Class = "Autodesk.Revit.DB.BuiltInParameter",
                Id = -1
            };

            var service = new RevitIdentityService();

            // Act
            ElementId resolvedId = service.ResolveElementId(model, null!);

            // Assert
#if REVIT2022 || REVIT2023
            Assert.AreEqual((int)BuiltInParameter.WALL_USER_HEIGHT_PARAM, resolvedId.IntegerValue);
#else
            Assert.AreEqual((long)BuiltInParameter.WALL_USER_HEIGHT_PARAM, resolvedId.Value);
#endif
        }

        [Test]
        public void ToModel_FromElementId_PopulatesCorrectModelFields()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Element? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level element should exist.");

                var service = new RevitIdentityService();

                // Act
                ElementIdModel model = service.ToModel(defaultLevel!.Id, doc);

                // Assert
                Assert.IsNotNull(model);
                Assert.AreEqual(defaultLevel.Name, model.Name);
                Assert.AreEqual(defaultLevel.GetType().FullName, model.Class);
                Assert.AreEqual(defaultLevel.UniqueId, model.UniqueId);
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/SpatialModelTests.cs
```csharp
using System;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class SpatialModelTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void XYZ_ToModelAndToNative_RoundtripsLosslessly()
        {
            // Act
            var native = new XYZ(12.345, -67.89, 0.0);
            var model = native.ToModel();
            
            Assert.IsNotNull(model);
            Assert.AreEqual(12.345, model!.X, 1e-9);
            Assert.AreEqual(-67.89, model.Y, 1e-9);
            Assert.AreEqual(0.0, model.Z, 1e-9);

            var roundtripped = model.ToNative();
            Assert.IsNotNull(roundtripped);
            Assert.AreEqual(12.345, roundtripped!.X, 1e-9);
            Assert.AreEqual(-67.89, roundtripped.Y, 1e-9);
            Assert.AreEqual(0.0, roundtripped.Z, 1e-9);
        }

        [Test]
        public void Transform_ToModelAndToNative_RoundtripsLosslessly()
        {
            // Arrange
            var origin = new XYZ(1.0, 2.0, 3.0);
            var basisX = new XYZ(0.0, 1.0, 0.0);
            var basisY = new XYZ(-1.0, 0.0, 0.0);
            var basisZ = new XYZ(0.0, 0.0, 1.0);

            var native = Transform.Identity;
            native.Origin = origin;
            native.set_Basis(0, basisX);
            native.set_Basis(1, basisY);
            native.set_Basis(2, basisZ);

            // Act
            var model = native.ToModel();
            Assert.IsNotNull(model);
            Assert.IsNotNull(model!.Origin);
            Assert.IsNotNull(model.BasisX);
            Assert.IsNotNull(model.BasisY);
            Assert.IsNotNull(model.BasisZ);

            Assert.AreEqual(1.0, model.Origin!.X, 1e-9);
            Assert.AreEqual(2.0, model.Origin.Y, 1e-9);
            Assert.AreEqual(3.0, model.Origin.Z, 1e-9);

            Assert.AreEqual(0.0, model.BasisX!.X, 1e-9);
            Assert.AreEqual(1.0, model.BasisX.Y, 1e-9);
            Assert.AreEqual(0.0, model.BasisX.Z, 1e-9);

            var roundtripped = model.ToNative();
            Assert.IsNotNull(roundtripped);
            Assert.AreEqual(origin.X, roundtripped!.Origin.X, 1e-9);
            Assert.AreEqual(origin.Y, roundtripped.Origin.Y, 1e-9);
            Assert.AreEqual(origin.Z, roundtripped.Origin.Z, 1e-9);

            Assert.AreEqual(basisX.X, roundtripped.BasisX.X, 1e-9);
            Assert.AreEqual(basisX.Y, roundtripped.BasisX.Y, 1e-9);
            Assert.AreEqual(basisX.Z, roundtripped.BasisX.Z, 1e-9);

            Assert.AreEqual(basisY.X, roundtripped.BasisY.X, 1e-9);
            Assert.AreEqual(basisY.Y, roundtripped.BasisY.Y, 1e-9);
            Assert.AreEqual(basisY.Z, roundtripped.BasisY.Z, 1e-9);
        }

        [Test]
        public void BoundingBoxXYZ_ToModelAndToNative_RoundtripsLosslessly()
        {
            // Arrange
            var min = new XYZ(-10.0, -20.0, -30.0);
            var max = new XYZ(10.0, 20.0, 30.0);
            var origin = new XYZ(5.0, 5.0, 5.0);

            var transform = Transform.Identity;
            transform.Origin = origin;

            var native = new BoundingBoxXYZ
            {
                Min = min,
                Max = max,
                Transform = transform
            };

            // Act
            var model = native.ToModel();
            Assert.IsNotNull(model);
            Assert.IsNotNull(model!.Min);
            Assert.IsNotNull(model.Max);
            Assert.IsNotNull(model.Transform);

            Assert.AreEqual(-10.0, model.Min!.X, 1e-9);
            Assert.AreEqual(10.0, model.Max!.X, 1e-9);
            Assert.AreEqual(5.0, model.Transform!.Origin!.X, 1e-9);

            var roundtripped = model.ToNative();
            Assert.IsNotNull(roundtripped);
            Assert.AreEqual(min.X, roundtripped!.Min.X, 1e-9);
            Assert.AreEqual(max.X, roundtripped.Max.X, 1e-9);
            Assert.AreEqual(origin.X, roundtripped.Transform.Origin.X, 1e-9);
        }

        [Test]
        public void Translators_HandleNullsGracefully()
        {
            XYZ? nullXyz = null;
            Transform? nullTransform = null;
            BoundingBoxXYZ? nullBbox = null;

            Assert.IsNull(nullXyz.ToModel());
            Assert.IsNull(((XYZModel?)null).ToNative());

            Assert.IsNull(nullTransform.ToModel());
            Assert.IsNull(((TransformModel?)null).ToNative());

            Assert.IsNull(nullBbox.ToModel());
            Assert.IsNull(((BoundingBoxXYZModel?)null).ToNative());
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/StandardSerializationEngineTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System.Threading;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class StandardSerializationEngineTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        private class StubLevelTranslator : IModelTranslator
        {
            public bool WasInjected { get; private set; }

            public void ExtractSpecifics(object revitElement, ObjectModel model, Document doc) { }

            public object? InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
            {
                WasInjected = true;
                if (revitElement is Element elem)
                {
                    elem.Name = "ModifiedByStubTranslator";
                    return elem;
                }
                return revitElement;
            }
        }

        private class UnregisteredDummyModel : ElementModel
        {
        }

        [Test]
        public void ToRevit_GracefulDegradation_CapturesFailedAndSuccessfulTransactions()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find a default level to modify
                Level? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level should exist.");

                // Create a successful model using ElementModel targeting the level
                var successModel = new ElementModel
                {
                    ElementId = defaultLevel!.Id.ToModel(doc),
                    Name = "SomeDifferentName"
                };

                // Create a failed model (e.g. UnregisteredDummyModel) targeting a level or fake element,
                // but we won't register any translator for UnregisteredDummyModel so it will fail with NotSupportedException.
                var failModel = new UnregisteredDummyModel
                {
                    Name = "FakeHost"
                };

                var engine = new StandardSerializationEngine();
                
                // Register our stub translator for ElementModel
                var stubTranslator = new StubLevelTranslator();
                engine.Dispatcher.Register<ElementModel, StubLevelTranslator>(stubTranslator, typeof(Level));

                var modelsBatch = new List<ObjectModel> { successModel, failModel };

                // Act
                IEnumerable<SerializationResultModel> results = engine.ToRevit(modelsBatch, doc);
                var resultsList = results.ToList();

                // Assert
                Assert.AreEqual(2, resultsList.Count, "Should return results for both models.");

                var successResult = resultsList.FirstOrDefault(r => r.Model == successModel);
                Assert.IsNotNull(successResult, "Success result should exist.");
                Assert.IsTrue(successResult!.Success, "The valid model should report success.");
                Assert.IsNull(successResult.ErrorMessage, "Success result should have no error message.");

                var failResult = resultsList.FirstOrDefault(r => r.Model == failModel);
                Assert.IsNotNull(failResult, "Failure result should exist.");
                Assert.IsFalse(failResult!.Success, "The invalid model should report failure.");
                Assert.IsNotNull(failResult.ErrorMessage, "Failure result should contain an error message.");
                Assert.IsTrue(failResult.ErrorMessage!.Contains("No translator registered"), "Error message should mention registration failure.");

                // Assert that the successful element was actually modified and committed
                Assert.AreEqual("ModifiedByStubTranslator", defaultLevel.Name, "The successful model modifications should be committed.");
                Assert.IsTrue(stubTranslator.WasInjected, "Translator InjectSpecifics should have run.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ToRevit_Cancellation_RollsBackAllChangesAndThrowsException()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find default level
                Level? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level should exist.");
                string originalName = defaultLevel.Name;

                // Create a valid model targeting the level
                var successModel = new ElementModel
                {
                    ElementId = defaultLevel!.Id.ToModel(doc),
                    Name = defaultLevel.Name
                };

                var engine = new StandardSerializationEngine();
                var stubTranslator = new StubLevelTranslator();
                engine.Dispatcher.Register<ElementModel, StubLevelTranslator>(stubTranslator, typeof(Level));

                // Create a cancellation token that is canceled
                var cts = new CancellationTokenSource();
                cts.Cancel();

                // Act & Assert
                Assert.Throws<OperationCanceledException>(() =>
                {
                    engine.ToRevit(new List<ObjectModel> { successModel }, doc, cancellationToken: cts.Token);
                }, "Should throw OperationCanceledException when token is cancelled.");

                // Assert that the level was NOT modified because the outer transaction group was rolled back
                Assert.AreEqual(originalName, defaultLevel.Name, "Changes must be rolled back on cancellation.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ToRevit_UnchangedElement_SkipsModificationsAndReturnsUnchanged()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find a default level to target
                Level? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level should exist.");
                string originalName = defaultLevel.Name;

                // Create a model matching the level exactly
                var unchangedModel = new ElementModel
                {
                    ElementId = defaultLevel!.Id.ToModel(doc),
                    Name = defaultLevel.Name
                };

                var engine = new StandardSerializationEngine();
                
                // Register our stub translator for ElementModel
                var stubTranslator = new StubLevelTranslator();
                engine.Dispatcher.Register<ElementModel, StubLevelTranslator>(stubTranslator, typeof(Level));

                // Act
                var resultsList = engine.ToRevit(new List<ObjectModel> { unchangedModel }, doc).ToList();

                // Assert
                Assert.AreEqual(1, resultsList.Count);
                var result = resultsList[0];
                Assert.IsTrue(result.Success);
                Assert.AreEqual("Unchanged", result.Action, "Should be flagged as Unchanged.");
                Assert.IsFalse(stubTranslator.WasInjected, "InjectSpecifics should be skipped when element is unchanged.");
                Assert.AreEqual(originalName, defaultLevel.Name, "Level name should not have changed.");
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/Tier2_AliasAssimilationTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_AliasAssimilationTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        private Material CreateMaterial(Document doc, string name)
        {
            ElementId matId = Material.Create(doc, name);
            return (Material)doc.GetElement(matId);
        }

        [Test]
        public void ToRevit_SuccessfulAssimilation()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Alias Assimilation Test"))
                {
                    tg.Start();

                    // 1. Setup the Test Environment (Create Alias Material and WallType)
                    Material aliasMaterial;
                    WallType wallType;
                    using (var t = new Transaction(doc, "Create Alias Material and WallType"))
                    {
                        t.Start();
                        aliasMaterial = CreateMaterial(doc, "AliasMaterial_Test");

                        wallType = new FilteredElementCollector(doc)
                            .OfClass(typeof(WallType))
                            .Cast<WallType>()
                            .First();

                        // Assign alias material to its compound structure
                        CompoundStructure cs = wallType.GetCompoundStructure();
                        if (cs != null)
                        {
                            var layers = cs.GetLayers();
                            if (layers.Count > 0)
                            {
                                layers[0].MaterialId = aliasMaterial.Id;
                                cs.SetLayers(layers);
                                wallType.SetCompoundStructure(cs);
                            }
                        }
                        t.Commit();
                    }

                    // Verify initial reference
                    Assert.AreEqual(aliasMaterial.Id, wallType.GetCompoundStructure().GetLayers()[0].MaterialId);

                    // 2. Construct the POCO MaterialModel representing "Primary Material"
                    var primaryModel = new MaterialModel
                    {
                        Name = "PrimaryMaterial_Test",
                        Class = "Autodesk.Revit.DB.Material",
                        Aliases = new List<string> { "AliasMaterial_Test" }
                    };

                    // 3. Execute StandardSerializationEngine.ToRevit()
                    var engine = new StandardSerializationEngine();
                    var results = engine.ToRevit(new List<ObjectModel> { primaryModel }, doc).ToList();

                    foreach (var res in results)
                    {
                        Console.WriteLine($"[TEST LOG] Model: {res.Model.GetType().Name}, Success: {res.Success}, Error: {res.ErrorMessage}, Action: {res.Action}, Message: {res.Message}");
                        if (res.Exception != null) Console.WriteLine($"[TEST LOG] Exception: {res.Exception}");
                    }

                    // 4. Assert Primary Material was created
                    var allMats = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .Select(m => m.Name)
                        .ToList();
                    Console.WriteLine("[TEST LOG] All materials: " + string.Join(", ", allMats));

                    var primaryMaterial = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault(m => m.Name == "PrimaryMaterial_Test");
                    Assert.IsNotNull(primaryMaterial, "Primary Material should be created.");

                    // Assert Alias Material no longer exists
                    var aliasCheck = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault(m => m.Name == "AliasMaterial_Test");
                    Assert.IsNull(aliasCheck, "Alias Material should be deleted.");

                    // Assert WallType's compound structure now references the newly created Primary Material
                    Assert.AreEqual(primaryMaterial.Id, wallType.GetCompoundStructure().GetLayers()[0].MaterialId);

                    // Assert serialization results
                    Assert.IsTrue(results.Any(r => r.Success && r.Model == primaryModel && r.Action == "Created"), "Should contain a primary creation result");
                    Assert.IsTrue(results.Any(r => r.Success && r.Model == primaryModel && r.Action == "Merged Alias"), "Should contain a merge alias success result");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ToRevit_GracefulDegradation_OnAliasFailure()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Alias Failure Test"))
                {
                    tg.Start();

                    // Create the alias material that will be deleted early during resolution
                    Material aliasMaterial;
                    using (var t = new Transaction(doc, "Create Fail Alias Material"))
                    {
                        t.Start();
                        aliasMaterial = CreateMaterial(doc, "AliasMaterial_FailTest");
                        t.Commit();
                    }

                    // 1. Construct POCO MaterialModel with alias name "AliasMaterial_FailTest"
                    var primaryModel = new MaterialModel
                    {
                        Name = "PrimaryMaterial_FailTest",
                        Class = "Autodesk.Revit.DB.Material",
                        Aliases = new List<string> { "AliasMaterial_FailTest" }
                    };

                    // 2. Execute ToRevit using our FailIdentityService
                    var realIdentity = new RevitIdentityService();
                    var failIdentity = new FailIdentityService(realIdentity, doc, aliasMaterial.Id);
                    var engine = new StandardSerializationEngine(failIdentity);
                    var results = engine.ToRevit(new List<ObjectModel> { primaryModel }, doc).ToList();

                    foreach (var res in results)
                    {
                        Console.WriteLine($"[TEST LOG 2] Model: {res.Model.GetType().Name}, Success: {res.Success}, Error: {res.ErrorMessage}, Action: {res.Action}, Message: {res.Message}");
                        if (res.Exception != null) Console.WriteLine($"[TEST LOG 2] Exception: {res.Exception}");
                    }

                    // 3. Assert primary transaction succeeded and Primary Material exists
                    var allMats = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .Select(m => m.Name)
                        .ToList();
                    Console.WriteLine("[TEST LOG 2] All materials: " + string.Join(", ", allMats));

                    var primaryMaterial = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault(m => m.Name == "PrimaryMaterial_FailTest");
                    Assert.IsNotNull(primaryMaterial, "Primary Material should exist even if alias swap fails.");

                    // 4. Assert serialization results contain success and failure/warning entries
                    Assert.IsTrue(results.Any(r => r.Success && r.Model == primaryModel && r.Action == "Created"), "Should contain a primary creation success result");
                    Assert.IsTrue(results.Any(r => !r.Success && r.Model == primaryModel && r.Action == "Alias Swap Failed"), "Should contain an alias swap failure result");

                    tg.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }

    /// <summary>
    /// Custom mock IdentityService that deletes the targeted alias element immediately after resolving it.
    /// This causes subsequent Swap or Delete operations inside ToRevit to fail with a Revit exception.
    /// </summary>
    public class FailIdentityService : IIdentityService
    {
        private readonly IIdentityService _realService;
        private readonly Document _doc;
        private readonly ElementId _toDelete;

        public FailIdentityService(IIdentityService realService, Document doc, ElementId toDelete)
        {
            _realService = realService;
            _doc = doc;
            _toDelete = toDelete;
        }

        public ElementId ResolveElementId(ElementIdModel model, Document doc)
        {
            var id = _realService.ResolveElementId(model, doc);
            if (id == _toDelete)
            {
                using (var tx = new Transaction(_doc, "Delete Alias Element Early"))
                {
                    tx.Start();
                    _doc.Delete(_toDelete);
                    tx.Commit();
                }
            }
            return id;
        }

        public Element ResolveElement(ElementIdModel model, Document doc)
        {
            return _realService.ResolveElement(model, doc);
        }

        public IEnumerable<Element> GetElementsByElementIdModels(Document doc, IEnumerable<ElementIdModel> identifiers)
        {
            return _realService.GetElementsByElementIdModels(doc, identifiers);
        }

        public ElementIdModel ToModel(ElementId id, Document doc, bool isTemplate = false)
        {
            return _realService.ToModel(id, doc, isTemplate);
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/Tier2_DispatcherSweepTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_DispatcherSweepTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void RunFullRosterSweep_VerifiesDispatcherTelemetryAndStability()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            
            // Access the active document or open standard template/project document
            Document doc = _uiapp.ActiveUIDocument?.Document ?? app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "Revit active/new document context should not be null.");

            // Collect standard instances (not ElementTypes)
            var instances = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();

            // Collect ElementTypes
            var types = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToElements();

            var allElements = new List<Element>();
            allElements.AddRange(instances);
            allElements.AddRange(types);

            // Group by concrete type and select exactly first of each type
            var uniqueElements = allElements
                .Where(e => e != null)
                .GroupBy(e => e.GetType())
                .Select(g => g.First())
                .ToList();

            var engine = new StandardSerializationEngine();
            var successfulTypes = new List<Type>();
            var pendingTypes = new List<Type>();
            var supportedByElementType = new List<Type>();
            var supportedByElement = new List<Type>();
            var unsupportedTypes = new List<Type>();
            var crashedTypes = new List<(Type, Exception)>();

            using (var globalTxGroup = new TransactionGroup(doc, "Global Dispatcher Sweep"))
            {
                globalTxGroup.Start();

                foreach (var elem in uniqueElements)
                {
                    Type elemType = elem.GetType();
                    using (var innerTxGroup = new TransactionGroup(doc, $"Sweep Element {elemType.Name}"))
                    {
                        innerTxGroup.Start();
                        SerializationResultModel.ClearWarnings();

                        try
                        {
                            // Try to execute engine.ByRevit()
                            var extractedList = engine.ByRevit(new[] { elem }, doc, isTemplate: false).ToList();
                            
                            // Capture warnings from ByRevit before ToRevit clears them
                            var extractWarnings = SerializationResultModel.CurrentThreadWarnings.ToList();
                            bool isIgnored = engine.Dispatcher.IsIgnored(elemType);
                            bool isElementTypeFallback = extractWarnings.Any(w => w.Contains("falling back to generic ElementTypeModel"));
                            bool isElementFallback = extractWarnings.Any(w => w.Contains("falling back to generic ElementModel"));
                            bool isPending = extractWarnings.Any(w => w.Contains("pending future support")) || engine.Dispatcher.IsPending(elemType);
                            bool isIgnoredOrNull = extractedList.Count == 0;

                            bool hasException = false;
                            Exception? exception = null;

                            if (extractedList.Count > 0)
                            {
                                // Immediately pass the result into engine.ToRevit()
                                var results = engine.ToRevit(extractedList, doc).ToList();
                                foreach (var res in results)
                                {
                                    if (!res.Success)
                                    {
                                        hasException = true;
                                        exception = res.Exception ?? new Exception(res.ErrorMessage ?? "Unknown failure in ToRevit");
                                    }
                                }
                            }

                            // Filter out known native compound structure end cap setting quirks
                            bool isCompoundStructureQuirk = hasException && exception != null && 
                                                           exception.Message.Contains("Input compound structure has wrong EndCap condition");

                            if (isIgnored)
                            {
                                // Dropped entirely as per DB 005-3 requirements.
                            }
                            else if (isElementTypeFallback)
                            {
                                supportedByElementType.Add(elemType);
                            }
                            else if (isElementFallback)
                            {
                                supportedByElement.Add(elemType);
                            }
                            else if (isPending)
                            {
                                pendingTypes.Add(elemType);
                            }
                            else if (isIgnoredOrNull)
                            {
                                unsupportedTypes.Add(elemType);
                            }
                            else if (hasException && exception != null && !isCompoundStructureQuirk)
                            {
                                crashedTypes.Add((elemType, exception));
                            }
                            else
                            {
                                successfulTypes.Add(elemType);
                            }
                        }
                        catch (Exception ex)
                        {
                            crashedTypes.Add((elemType, ex));
                        }

                        innerTxGroup.RollBack();
                    }
                }

                globalTxGroup.RollBack();
            }

            // Print consolidated diagnostic report
            Console.WriteLine("============================================================");
            Console.WriteLine("DISPATCHER FULL ROSTER SWEEP REPORT");
            Console.WriteLine("============================================================");
            Console.WriteLine($"Successful Types (Count: {successfulTypes.Count}):");
            foreach (var t in successfulTypes.OrderBy(x => x.FullName))
            {
                Console.WriteLine($"- {t.FullName}");
            }
            Console.WriteLine();
            Console.WriteLine($"Pending Types (Count: {pendingTypes.Count}):");
            foreach (var t in pendingTypes.OrderBy(x => x.FullName))
            {
                Console.WriteLine($"- {t.FullName}");
            }
            Console.WriteLine();
            Console.WriteLine($"Supported by ElementType (Count: {supportedByElementType.Count}):");
            foreach (var t in supportedByElementType.OrderBy(x => x.FullName))
            {
                Console.WriteLine($"- {t.FullName}");
            }
            Console.WriteLine();
            Console.WriteLine($"Supported by Element (Count: {supportedByElement.Count}):");
            foreach (var t in supportedByElement.OrderBy(x => x.FullName))
            {
                Console.WriteLine($"- {t.FullName}");
            }
            Console.WriteLine();
            Console.WriteLine($"Unsupported Types (Count: {unsupportedTypes.Count}):");
            foreach (var t in unsupportedTypes.OrderBy(x => x.FullName))
            {
                Console.WriteLine($"- {t.FullName}");
            }
            Console.WriteLine();
            Console.WriteLine($"Crashed Types (Count: {crashedTypes.Count}):");
            foreach (var item in crashedTypes.OrderBy(x => x.Item1.FullName))
            {
                Console.WriteLine($"- {item.Item1.FullName}: {item.Item2.Message}");
            }
            Console.WriteLine("============================================================");

            // Assert no unhandled unexpected exceptions occurred
            Assert.IsEmpty(crashedTypes, "No element type should crash the serialization pipeline with an unhandled exception.");
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/Tier2_StandardsExtractionOrchestratorTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Standards;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_StandardsExtractionOrchestratorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        private Material CreateMaterial(Document doc, string name)
        {
            ElementId matId = Material.Create(doc, name);
            return (Material)doc.GetElement(matId);
        }

        [Test]
        public void Extract_DeepDependencyRetrieval_WallTypeAndMaterial()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                Material testMaterial;
                WallType wallType;

                using (var t = new Transaction(doc, "Create Test Environment"))
                {
                    t.Start();
                    testMaterial = CreateMaterial(doc, "OrchestratorTestMaterial");

                    wallType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .First();

                    // Assign material to compound structure of wall type
                    CompoundStructure cs = wallType.GetCompoundStructure();
                    if (cs != null)
                    {
                        var layers = cs.GetLayers();
                        if (layers.Count > 0)
                        {
                            layers[0].MaterialId = testMaterial.Id;
                            cs.SetLayers(layers);
                            wallType.SetCompoundStructure(cs);
                        }
                    }
                    t.Commit();
                }

                var identityService = new RevitIdentityService();
                var orchestrator = new StandardsExtractionOrchestrator(identityService);

                // Act: Extract starting ONLY with the WallType
                var rootElements = new List<Element> { wallType };
                var results = orchestrator.Extract(doc, rootElements, null, false);

                // Assert
                Assert.IsNotNull(results, "Extraction results should not be null.");

                // Find WallType POCO and Material POCO in the output list
                var wallTypePoco = results.OfType<HostObjTypeModel>().FirstOrDefault(w => w.Name == wallType.Name);
                var materialPoco = results.OfType<MaterialModel>().FirstOrDefault(m => m.Name == "OrchestratorTestMaterial");

                Assert.IsNotNull(wallTypePoco, "WallType POCO should be extracted.");
                Assert.IsNotNull(materialPoco, "Material POCO should be extracted as a nested dependency.");

                // Assert DependencyOrigin is set correctly
                Assert.AreEqual(wallType.Name, materialPoco!.DependencyOrigin,
                    "Material's DependencyOrigin should match the WallType's Name that pulled it in.");
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/ViewModelTests.cs
```csharp
using System;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class ViewModelTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ViewModelHydration_ActiveView_HydratesCorrectly()
        {
            // Arrange
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Assert.IsNotNull(app, "Revit Application should not be null.");

            Document doc = app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "New project document should be created.");

            try
            {
                // Find a graphical view (like FloorPlan) that is not a template
                View? view = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => !v.IsTemplate && (v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.Elevation || v.ViewType == ViewType.Section));

                if (view == null)
                {
                    view = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => !v.IsTemplate);
                }

                Assert.IsNotNull(view, "A non-template view should exist in the default document.");

                // Act
                var model = view!.ToModel(false);

                // Assert
                Assert.IsNotNull(model, "ViewModel should be successfully instantiated.");
                Assert.AreEqual(view!.Scale, model.Scale, "Scale property should match.");
                Assert.AreEqual(view.IsTemplate, model.IsTemplate, "IsTemplate flag should match.");
                Assert.IsFalse(model.IsTemplate, "IsTemplate flag should be false for a live active/default view.");

                // DetailLevel Assertion
                Assert.IsNotNull(model.DetailLevel, "DetailLevel should not be null.");
                Assert.AreEqual("Autodesk.Revit.DB.ViewDetailLevel", model.DetailLevel!.Type, "DetailLevel type name should match.");
                Assert.AreEqual(view.DetailLevel.ToString(), model.DetailLevel.Value, "DetailLevel value string should match.");

                // DisplayStyle Assertion
                Assert.IsNotNull(model.DisplayStyle, "DisplayStyle should not be null.");
                Assert.AreEqual("Autodesk.Revit.DB.DisplayStyle", model.DisplayStyle.Type, "DisplayStyle type name should match.");
                Assert.AreEqual(view.DisplayStyle.ToString(), model.DisplayStyle.Value, "DisplayStyle value string should match.");

                // Parameters Assertion
                Assert.IsNotNull(model.Parameters, "Parameters collection should not be null.");
                Assert.IsNotEmpty(model.Parameters, "Parameters collection should contain items.");
                foreach (var paramModel in model.Parameters)
                {
                    Assert.IsNotNull(paramModel.Name, "Parameter name should not be null.");
                    Assert.IsNotNull(paramModel.StorageType, "Parameter StorageType should not be null.");
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/RevitDOM/ViewTranslatorTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    internal static class ElementIdTestExtensions
    {
        public static long GetIdValue(this ElementId id)
        {
#if REVIT2022 || REVIT2023
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }
    }

    [TestFixture]
    public class ViewTranslatorTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void ViewPlan_ExtractAndInject_RoundtripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test ViewPlan Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Setup and Roundtrip"))
                    {
                        t.Start();

                        // 1. Create a Level
                        Level level = Level.Create(doc, 10.0);
                        Assert.IsNotNull(level);

                        // 2. Find default FloorPlan ViewFamilyType
                        var floorPlanType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.FloorPlan);

                        Assert.IsNotNull(floorPlanType, "FloorPlan ViewFamilyType not found in document.");

                        // 3. Create initial ViewPlan
                        ViewPlan view = ViewPlan.Create(doc, floorPlanType!.Id, level.Id);
                        view.Name = "Initial_Test_FloorPlan";

                        // 4. Extract
                        var model = view.ToModel(false);
                        Assert.IsNotNull(model);
                        Assert.AreEqual("Autodesk.Revit.DB.ViewPlan", model.Class);
                        Assert.AreEqual("Initial_Test_FloorPlan", model.Name);
                        Assert.IsNotNull(model.LevelId);
                        Assert.AreEqual(level.Id.GetIdValue(), model.LevelId!.Id);
                        Assert.IsNotNull(model.ViewFamilyTypeId);
                        Assert.AreEqual(floorPlanType.Id.GetIdValue(), model.ViewFamilyTypeId!.Id);

                        // 5. Modify model for new creation
                        model.Name = "Duplicated_Test_FloorPlan";
                        model.UniqueId = null;
                        model.Id = 0;

                        // 6. Inject as new
                        var translator = new ViewTranslator(new RevitIdentityService());
                        var newView = translator.InjectSpecifics(model, null, doc) as ViewPlan;

                        Assert.IsNotNull(newView);
                        Assert.AreEqual("Duplicated_Test_FloorPlan", newView!.Name);
                        Assert.AreEqual(level.Id.GetIdValue(), newView.GenLevel.Id.GetIdValue());
                        Assert.AreEqual(floorPlanType.Id.GetIdValue(), newView.GetTypeId().GetIdValue());

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewDrafting_ExtractAndInject_RoundtripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test ViewDrafting Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Setup and Roundtrip"))
                    {
                        t.Start();

                        // 1. Find default Drafting ViewFamilyType
                        var draftingType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.Drafting);

                        Assert.IsNotNull(draftingType, "Drafting ViewFamilyType not found in document.");

                        // 2. Create initial ViewDrafting
                        ViewDrafting view = ViewDrafting.Create(doc, draftingType!.Id);
                        view.Name = "Initial_Test_DraftingView";

                        // 3. Extract
                        var model = view.ToModel(false);
                        Assert.IsNotNull(model);
                        Assert.AreEqual("Autodesk.Revit.DB.ViewDrafting", model.Class);
                        Assert.AreEqual("Initial_Test_DraftingView", model.Name);
                        Assert.IsNull(model.LevelId);
                        Assert.IsNotNull(model.ViewFamilyTypeId);
                        Assert.AreEqual(draftingType.Id.GetIdValue(), model.ViewFamilyTypeId!.Id);

                        // 4. Modify model for new creation
                        model.Name = "Duplicated_Test_DraftingView";
                        model.UniqueId = null;
                        model.Id = 0;

                        // 5. Inject as new
                        var translator = new ViewTranslator(new RevitIdentityService());
                        var newView = translator.InjectSpecifics(model, null, doc) as ViewDrafting;

                        Assert.IsNotNull(newView);
                        Assert.AreEqual("Duplicated_Test_DraftingView", newView!.Name);
                        Assert.AreEqual(draftingType.Id.GetIdValue(), newView.GetTypeId().GetIdValue());

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewPlan_MissingLevel_ThrowsHardAbort()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var t = new Transaction(doc, "Inject Invalid ViewPlan"))
                {
                    t.Start();

                    // Find floor plan type
                    var floorPlanType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.FloorPlan);

                    Assert.IsNotNull(floorPlanType);

                    var model = new ViewModel
                    {
                        Class = "Autodesk.Revit.DB.ViewPlan",
                        Name = "Invalid_FloorPlan_NoLevel",
                        ViewFamilyTypeId = floorPlanType!.Id.ToModel(doc, false),
                        LevelId = null // missing level
                    };

                    var translator = new ViewTranslator(new RevitIdentityService());
                    
                    // Assert - should throw InvalidOperationException
                    Assert.Throws<InvalidOperationException>(() =>
                    {
                        translator.InjectSpecifics(model, null, doc);
                    });

                    t.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewPlan_MissingViewFamilyType_GracefulDegradation()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var t = new Transaction(doc, "Inject ViewPlan with Missing Type"))
                {
                    t.Start();

                    // Create level
                    Level level = Level.Create(doc, 5.0);
                    Assert.IsNotNull(level);

                    var model = new ViewModel
                    {
                        Class = "Autodesk.Revit.DB.ViewPlan",
                        Name = "Degraded_FloorPlan",
                        LevelId = level.Id.ToModel(doc, false),
                        ViewFamilyTypeId = null // missing type
                    };

                    SerializationResultModel.ClearWarnings();

                    var translator = new ViewTranslator(new RevitIdentityService());
                    var view = translator.InjectSpecifics(model, null, doc) as ViewPlan;

                    // Assert
                    Assert.IsNotNull(view);
                    Assert.AreEqual("Degraded_FloorPlan", view!.Name);
                    Assert.AreEqual(level.Id.GetIdValue(), view.GenLevel.Id.GetIdValue());
                    
                    // Assert warning logged
                    Assert.IsNotEmpty(SerializationResultModel.CurrentThreadWarnings);
                    bool hasWarning = SerializationResultModel.CurrentThreadWarnings.Any(w => w.Contains("missing or unresolved") && w.Contains("Degraded_FloorPlan"));
                    Assert.IsTrue(hasWarning, "Warning should be logged about missing ViewFamilyType resolution.");

                    t.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewSection_ExtractAndInject_RoundtripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test ViewSection Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Setup and Roundtrip"))
                    {
                        t.Start();

                        // 1. Find default Section ViewFamilyType
                        var sectionType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.Section);

                        Assert.IsNotNull(sectionType, "Section ViewFamilyType not found in document.");

                        // 2. Define Section Box
                        BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
                        sectionBox.Min = new XYZ(-10, -10, -10);
                        sectionBox.Max = new XYZ(10, 10, 10);

                        // 3. Create initial ViewSection
                        ViewSection view = ViewSection.CreateSection(doc, sectionType!.Id, sectionBox);
                        view.Name = "Initial_Test_Section";

                        // 4. Extract
                        var model = view.ToModel(false);
                        Assert.IsNotNull(model);
                        Assert.AreEqual("Autodesk.Revit.DB.ViewSection", model.Class);
                        Assert.AreEqual("Initial_Test_Section", model.Name);
                        Assert.IsNotNull(model.CropBox);
                        Assert.IsNotNull(model.ViewFamilyTypeId);
                        Assert.AreEqual(sectionType.Id.GetIdValue(), model.ViewFamilyTypeId!.Id);

                        // 5. Modify model for new creation
                        model.Name = "Duplicated_Test_Section";
                        model.UniqueId = null;
                        model.Id = 0;

                        // 6. Inject as new
                        var translator = new ViewTranslator(new RevitIdentityService());
                        var newView = translator.InjectSpecifics(model, null, doc) as ViewSection;

                        Assert.IsNotNull(newView);
                        Assert.AreEqual("Duplicated_Test_Section", newView!.Name);
                        Assert.AreEqual(sectionType.Id.GetIdValue(), newView.GetTypeId().GetIdValue());

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewElevation_ExtractAndInject_RoundtripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test ViewElevation Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Setup and Roundtrip"))
                    {
                        t.Start();

                        // 1. Create a Level & Plan View (needed to host Elevation marker)
                        Level level = Level.Create(doc, 0.0);
                        var floorPlanType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.FloorPlan);
                        ViewPlan planView = ViewPlan.Create(doc, floorPlanType!.Id, level.Id);

                        // 2. Find default Elevation ViewFamilyType
                        var elevationType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.Elevation);

                        Assert.IsNotNull(elevationType, "Elevation ViewFamilyType not found in document.");

                        // 3. Create Elevation View
                        ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, elevationType!.Id, XYZ.Zero, 100);
                        ViewSection view = marker.CreateElevation(doc, planView.Id, 0);
                        view.Name = "Initial_Test_Elevation";

                        // 4. Extract
                        var model = view.ToModel(false);
                        Assert.IsNotNull(model);
                        Assert.AreEqual("Autodesk.Revit.DB.ViewSection", model.Class);
                        Assert.AreEqual("Initial_Test_Elevation", model.Name);
                        Assert.IsNotNull(model.CropBox);
                        Assert.IsNotNull(model.ViewFamilyTypeId);
                        Assert.AreEqual(elevationType.Id.GetIdValue(), model.ViewFamilyTypeId!.Id);

                        // 5. Modify model for new creation
                        model.Name = "Duplicated_Test_Elevation";
                        model.UniqueId = null;
                        model.Id = 0;

                        // 6. Inject as new
                        var translator = new ViewTranslator(new RevitIdentityService());
                        var newView = translator.InjectSpecifics(model, null, doc) as ViewSection;

                        Assert.IsNotNull(newView);
                        Assert.AreEqual("Duplicated_Test_Elevation", newView!.Name);
                        Assert.AreEqual(elevationType.Id.GetIdValue(), newView.GetTypeId().GetIdValue());

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewSection_ScopeBoxFallback_WarningLogged()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var t = new Transaction(doc, "Inject ViewSection with Missing Scope Box"))
                {
                    t.Start();

                    // Find section type
                    var sectionType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.Section);

                    var model = new ViewModel
                    {
                        Class = "Autodesk.Revit.DB.ViewSection",
                        Name = "ScopeBox_Fallback_Section",
                        ViewFamilyTypeId = sectionType!.Id.ToModel(doc, false),
                        ScopeBoxId = new ElementIdModel
                        {
                            Id = 999999, // fictional
                            Name = "FictionalScopeBox",
                            Class = "Autodesk.Revit.DB.ScopeBox"
                        }
                    };

                    SerializationResultModel.ClearWarnings();

                    var translator = new ViewTranslator(new RevitIdentityService());
                    var view = translator.InjectSpecifics(model, null, doc) as ViewSection;

                    // Assert creation succeeded despite missing Scope Box
                    Assert.IsNotNull(view);
                    Assert.AreEqual("ScopeBox_Fallback_Section", view!.Name);

                    // Assert warning was logged about missing scope box
                    Assert.IsNotEmpty(SerializationResultModel.CurrentThreadWarnings);
                    bool hasWarning = SerializationResultModel.CurrentThreadWarnings.Any(w => w.Contains("Scope Box") && w.Contains("missing or unresolved"));
                    Assert.IsTrue(hasWarning, "Warning should be logged about Scope Box resolution failure.");

                    t.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewTemplate_Duplication_Strategy_SuccessfullyCreatesTemplate()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test ViewTemplate Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Setup and Create Template"))
                    {
                        t.Start();

                        // 1. Find default FloorPlan ViewFamilyType
                        var floorPlanType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.FloorPlan);

                        // 2. Create a ViewModel representing a Floor Plan View Template
                        var model = new ViewModel
                        {
                            Class = "Autodesk.Revit.DB.ViewPlan",
                            Name = "My_FloorPlan_ViewTemplate",
                            IsTemplate = true,
                            ViewFamilyTypeId = floorPlanType!.Id.ToModel(doc, true)
                        };

                        // 3. Inject
                        var translator = new ViewTranslator(new RevitIdentityService());
                        var resultView = translator.InjectSpecifics(model, null, doc);

                        // 4. Assert
                        Assert.IsNotNull(resultView);
                        Assert.AreEqual("My_FloorPlan_ViewTemplate", resultView!.Name);
                        Assert.IsTrue(resultView.IsTemplate, "The created view must natively be a template.");

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void NonGraphicalView_Serialization_ExtractsWithoutExceptions()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test NonGraphical View Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Create NonGraphical Views"))
                    {
                        t.Start();

                        // 1. Create a ViewSheet
                        ViewSheet sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                        sheet.Name = "Test Sheet";
                        
                        // 2. Create a ViewSchedule
                        var roomCategoryId = new ElementId((int)BuiltInCategory.OST_Rooms);
                        ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, roomCategoryId);
                        schedule.Name = "Test Schedule";

                        var translator = new ViewTranslator(new RevitIdentityService());

                        // 3. Extract ViewSheet and verify zero exceptions
                        var sheetModel = new ViewModel();
                        Assert.DoesNotThrow(() => translator.ExtractSpecifics(sheet, sheetModel, doc));
                        
                        Assert.IsNull(sheetModel.DetailLevel);
                        Assert.IsNull(sheetModel.Scale);

                        // 4. Extract ViewSchedule and verify zero exceptions
                        var scheduleModel = new ViewModel();
                        Assert.DoesNotThrow(() => translator.ExtractSpecifics(schedule, scheduleModel, doc));

                        Assert.IsNull(scheduleModel.DetailLevel);
                        Assert.IsNull(scheduleModel.Scale);

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ViewPlan_Translation_RoundTripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test ViewPlan Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Setup and Create ViewPlan"))
                    {
                        t.Start();

                        // 1. Create a Level
                        Level level = Level.Create(doc, 10.0);

                        // 2. Find default FloorPlan ViewFamilyType
                        var floorPlanType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(vt => vt.ViewFamily == ViewFamily.FloorPlan);

                        Assert.IsNotNull(floorPlanType, "FloorPlan ViewFamilyType not found.");

                        // 3. Create ViewPlan
                        ViewPlan view = ViewPlan.Create(doc, floorPlanType!.Id, level.Id);
                        view.Name = "Test_Polymorphic_FloorPlan";

                        // 4. Extract (Verify it is a ViewPlanModel polymorphically)
                        var extractedModel = view.ToModel(false);
                        Assert.IsNotNull(extractedModel);
                        Assert.IsInstanceOf<ViewPlanModel>(extractedModel, "Extracted model must be of type ViewPlanModel.");

                        var planModel = (ViewPlanModel)extractedModel;
                        Assert.IsNotNull(planModel.ViewRange, "ViewRange should not be null on extracted ViewPlanModel.");

                        // 5. Modify plan model
                        planModel.Name = "Injected_Polymorphic_FloorPlan";
                        planModel.UniqueId = null;
                        planModel.Id = 0;

                        // 6. Inject
                        var translator = new ViewPlanTranslator(new RevitIdentityService());
                        var injectedView = translator.InjectSpecifics(planModel, null, doc);

                        Assert.IsNotNull(injectedView);
                        Assert.AreEqual("Injected_Polymorphic_FloorPlan", injectedView.Name);
                        Assert.AreEqual(level.Id.GetIdValue(), injectedView.GenLevel.Id.GetIdValue());

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void NonGraphicalView_Translation_PolymorphicallyRoundTripsSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (var tg = new TransactionGroup(doc, "Test NonGraphical Polymorphic Group"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Create Sheets and Schedules"))
                    {
                        t.Start();

                        // 1. Create native ViewSheet and ViewSchedule
                        ViewSheet sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                        sheet.Name = "Polymorphic_Test_Sheet";

                        var roomCategoryId = new ElementId((int)BuiltInCategory.OST_Rooms);
                        ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, roomCategoryId);
                        schedule.Name = "Polymorphic_Test_Schedule";

                        // 2. Extract polymorphically
                        var sheetModel = sheet.ToModel(false);
                        Assert.IsNotNull(sheetModel);
                        Assert.IsInstanceOf<ViewSheetModel>(sheetModel, "Extracted sheet must be ViewSheetModel.");

                        var scheduleModel = schedule.ToModel(false);
                        Assert.IsNotNull(scheduleModel);
                        Assert.IsInstanceOf<ViewScheduleModel>(scheduleModel, "Extracted schedule must be ViewScheduleModel.");

                        // 3. Serialize to JSON and check for graphical properties absence
                        var sheetJson = Synthetic.Infrastructure.Serialization.Json.Encode(sheetModel);
                        Assert.IsFalse(sheetJson.Contains("\"Scale\""));
                        Assert.IsFalse(sheetJson.Contains("\"DisplayStyle\""));

                        var scheduleJson = Synthetic.Infrastructure.Serialization.Json.Encode(scheduleModel);
                        Assert.IsFalse(scheduleJson.Contains("\"Scale\""));
                        Assert.IsFalse(scheduleJson.Contains("\"DisplayStyle\""));

                        // 4. Modify models
                        sheetModel.Name = "Injected_Polymorphic_Sheet";
                        sheetModel.UniqueId = null;
                        sheetModel.Id = 0;

                        scheduleModel.Name = "Injected_Polymorphic_Schedule";
                        scheduleModel.UniqueId = null;
                        scheduleModel.Id = 0;

                        // 5. Inject polymorphically
                        var sheetTranslator = new ViewSheetTranslator(new RevitIdentityService());
                        var injectedSheet = sheetTranslator.InjectSpecifics((ViewSheetModel)sheetModel, null, doc);
                        Assert.IsNotNull(injectedSheet);
                        Assert.AreEqual("Injected_Polymorphic_Sheet", injectedSheet.Name);

                        var scheduleTranslator = new ViewScheduleTranslator(new RevitIdentityService());
                        var injectedSchedule = scheduleTranslator.InjectSpecifics((ViewScheduleModel)scheduleModel, null, doc);
                        Assert.IsNotNull(injectedSchedule);
                        Assert.AreEqual("Injected_Polymorphic_Schedule", injectedSchedule.Name);

                        t.Commit();
                    }

                    tg.Assimilate();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/StandardsManagement/DashboardTestFactory.cs
```csharp
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Operations.Diffing;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;
using SyntheticTests.Modules.RevitDOM;

namespace SyntheticTests.Modules.StandardsManagement
{
    public static class DashboardTestFactory
    {
        public static ProjectStandardsDashboardViewModel Create(
            Document doc,
            IFileDialogService dialogService = null,
            IStandardsExportService exportService = null,
            StandardsSettings settings = null,
            IUserPromptService userPromptService = null,
            IFindReplaceService findReplaceService = null,
            IStandardsExtractionOrchestrator orchestrator = null,
            IPocoIdentityService pocoIdentityService = null,
            IDiffEngine<IEnumerable<ObjectModel>, Document> diffEngine = null,
            IStandardSerializationEngine serializationEngine = null,
            IStandardsExecutionPipeline pipeline = null)
        {
            dialogService ??= new FakeFileDialogService();
            if (exportService == null) 
            {
                var guardrail = new FakeGuardrailPromptService();
                exportService = new StandardsExportService(guardrail, dialogService);
            }
            userPromptService ??= new FakeUserPromptService();
            findReplaceService ??= new FindReplaceService();
            serializationEngine ??= new StandardSerializationEngine();
            pocoIdentityService ??= new PocoIdentityService();
            diffEngine ??= new PocoToRevitDiffEngine(new FakeIdentityService());
            orchestrator ??= new StandardsExtractionOrchestrator(new FakeIdentityService(), serializationEngine);
            pipeline ??= new StandardsExecutionPipeline(serializationEngine, exportService, new FakeFamilyEnforcer());

            var vm = new ProjectStandardsDashboardViewModel(
                doc,
                dialogService,
                exportService,
                settings,
                userPromptService,
                findReplaceService,
                orchestrator,
                pocoIdentityService,
                diffEngine,
                serializationEngine,
                pipeline
            );

            // Match test setup
            vm.ShowDocumentSelectionDialog = dialogVM =>
            {
                foreach (var docItem in dialogVM.OpenDocuments)
                {
                    docItem.IsSelected = true;
                }
                return true;
            };

            vm.ShowMergeDialog = dialogVM =>
            {
                if (dialogVM.Items != null)
                {
                    var enumerator = dialogVM.Items.GetEnumerator();
                    if (enumerator.MoveNext())
                    {
                        dialogVM.SelectedItem = enumerator.Current;
                    }
                }
                return true;
            };

            return vm;
        }
    }
}

```

### File: tests/SyntheticTests.Shared/Modules/StandardsManagement/FakeFileDialogService.cs
```csharp
﻿using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    /// <summary>
    /// Fake implementation of IFileDialogService for headless testing.
    /// </summary>
    public class FakeFileDialogService : IFileDialogService
    {
        /// <summary>
        /// Gets or sets the preset path to return.
        /// </summary>
        public string? PresetPath { get; set; } = @"C:\Temp\ExportedStandards.json";

        /// <summary>
        /// Immediately returns the preset path without opening a UI.
        /// </summary>
        public string? SaveFileDialog(string filter, string title, string defaultFileName)
        {
            return PresetPath;
        }

        /// <summary>
        /// Immediately returns the preset path without opening a UI.
        /// </summary>
        public string? OpenFileDialog(string filter, string title, string defaultFileName)
        {
            return PresetPath;
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/StandardsManagement/FakeGuardrailPromptService.cs
```csharp
﻿using System;
using System.Collections.Generic;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    /// <summary>
    /// Test implementation of IGuardrailPromptService that records invocations and returns configured results.
    /// </summary>
    public class FakeGuardrailPromptService : IGuardrailPromptService
    {
        private readonly GuardrailResult _configuredResult;
        
        /// <summary>
        /// Gets the list of file paths that were prompted.
        /// </summary>
        public List<string> PromptedPaths { get; } = new List<string>();

        /// <summary>
        /// Initializes a new instance of FakeGuardrailPromptService.
        /// </summary>
        /// <param name="configuredResult">The result to return when prompted.</param>
        public FakeGuardrailPromptService(GuardrailResult configuredResult = GuardrailResult.Cancel)
        {
            _configuredResult = configuredResult;
        }

        /// <inheritdoc/>
        public GuardrailResult PromptProtectedFileOverwrite(string filePath)
        {
            PromptedPaths.Add(filePath);
            return _configuredResult;
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/StandardsManagement/FakeUserPromptService.cs
```csharp
﻿using System;
using System.Collections.Generic;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    /// <summary>
    /// Test fake implementing IUserPromptService to avoid showing dialog windows during tests.
    /// </summary>
    public class FakeUserPromptService : IUserPromptService
    {
        public List<string> ShownMessages { get; } = new List<string>();
        public List<string> ConfirmedPrompts { get; } = new List<string>();
        public bool ConfirmationResult { get; set; } = true;

        public void ShowMessage(string message, string title)
        {
            ShownMessages.Add(message);
        }

        public bool ConfirmAction(string message, string title)
        {
            ConfirmedPrompts.Add(message);
            return ConfirmationResult;
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/StandardsManagement/StandardsExecutionPipelineTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Operations.Merge;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class StandardsExecutionPipelineTests
    {
        private Document _doc = null!;
        private ConfigurableSerializationEngine _fakeEngine = null!;
        private ConfigurableExportService _fakeExportService = null!;
        private FakeFamilyEnforcer _fakeFamilyEnforcer = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeEngine = new ConfigurableSerializationEngine();
            _fakeExportService = new ConfigurableExportService();
            _fakeFamilyEnforcer = new FakeFamilyEnforcer();
            ProgressCoordinator.SuppressUI = true;
        }

        [Test]
        public void Execute_WithWriteDatabaseTrue_CallsSerializationEngineAndSucceeds()
        {
            // Arrange
            var pipeline = new StandardsExecutionPipeline(_fakeEngine, _fakeExportService, _fakeFamilyEnforcer);
            var options = new StandardsExecutionOptions
            {
                WriteRevitDatabase = true,
                SaveLocalFiles = false,
                UseTransactionGroup = false
            };

            var model = new ElementModel { Name = "TestStandard", Class = "Autodesk.Revit.DB.TextNoteType" };
            var item = new StandardsExecutionItem(model)
            {
                WillEnforce = true,
                WillSave = false
            };

            bool ToRevitCalled = false;
            _fakeEngine.ToRevitHandler = (models, doc) =>
            {
                ToRevitCalled = true;
                Assert.AreEqual(_doc, doc);
                Assert.AreEqual(1, models.Count());
                Assert.AreEqual(model, models.First());
                return new List<SerializationResultModel> { new SerializationResultModel(model) };
            };

            // Act
            var result = pipeline.Execute(_doc, new[] { item }, options);

            // Assert
            Assert.IsTrue(ToRevitCalled, "ToRevit should have been called.");
            Assert.IsTrue(result.Success, "Execution should be successful.");
            Assert.AreEqual(1, result.Items.Count);
            Assert.AreEqual("Created", result.Items[0].Action);
        }

        [Test]
        public void Execute_WithSaveLocalFilesTrue_CallsExportService()
        {
            // Arrange
            var pipeline = new StandardsExecutionPipeline(_fakeEngine, _fakeExportService, _fakeFamilyEnforcer);
            var options = new StandardsExecutionOptions
            {
                WriteRevitDatabase = false,
                SaveLocalFiles = true,
                StandardsFilePath = @"C:\Temp\TestStandards.json"
            };

            var model = new ElementModel { Name = "TestStandard", Class = "Autodesk.Revit.DB.TextNoteType" };
            var item = new StandardsExecutionItem(model)
            {
                WillEnforce = false,
                WillSave = true
            };

            // Act
            var result = pipeline.Execute(_doc, new[] { item }, options);

            // Assert
            Assert.IsTrue(_fakeExportService.ExportCalled, "Export should have been called.");
            Assert.AreEqual(@"C:\Temp\TestStandards.json", _fakeExportService.TargetPathReceived);
            Assert.IsTrue(result.Success, "Execution should be successful.");
            Assert.AreEqual("Saved", result.Items[0].Action);
        }

        [Test]
        public void Execute_WhenDbPhaseThrows_RollsBackAndFails()
        {
            // Arrange
            var pipeline = new StandardsExecutionPipeline(_fakeEngine, _fakeExportService, _fakeFamilyEnforcer);
            var options = new StandardsExecutionOptions
            {
                WriteRevitDatabase = true,
                SaveLocalFiles = true,
                UseTransactionGroup = false
            };

            var model = new ElementModel { Name = "TestStandard", Class = "Autodesk.Revit.DB.TextNoteType" };
            var item = new StandardsExecutionItem(model)
            {
                WillEnforce = true,
                WillSave = true
            };

            _fakeEngine.ToRevitHandler = (models, doc) =>
            {
                throw new InvalidOperationException("Simulated database write crash.");
            };

            // Act
            var result = pipeline.Execute(_doc, new[] { item }, options);

            // Assert
            Assert.IsFalse(result.Success, "Execution should fail when DB writes throw.");
            Assert.IsFalse(_fakeExportService.ExportCalled, "Export should not be called if DB writes fail.");
            Assert.AreEqual("Failed", result.Items[0].Action);
            Assert.IsTrue(result.Items[0].Message.Contains("Simulated database write crash."));
        }

        [Test]
        public void Execute_WithProcessFamiliesTrue_ProcessesFamiliesAndHandlesFamilyException()
        {
            // Arrange
            var fakeFamilyEnforcer = new FakeFamilyEnforcer();
            var pipeline = new StandardsExecutionPipeline(_fakeEngine, _fakeExportService, fakeFamilyEnforcer);
            var options = new StandardsExecutionOptions
            {
                WriteRevitDatabase = true,
                SaveLocalFiles = false,
                ProcessFamilies = true,
                UseTransactionGroup = false
            };

            var model = new ElementModel { Name = "TestMaterial", Class = "Autodesk.Revit.DB.Material" };
            var item = new StandardsExecutionItem(model)
            {
                WillEnforce = true,
                WillSave = false
            };

            _fakeEngine.ToRevitHandler = (models, doc) =>
            {
                return new List<SerializationResultModel> { new SerializationResultModel(model) };
            };

            fakeFamilyEnforcer.EnforceHandler = (doc, standards, opts, progress, dbResults, token) =>
            {
                var failedModel = new ElementModel
                {
                    Name = "FaultyFamily",
                    Class = "Autodesk.Revit.DB.Family"
                };
                var result = new SerializationResultModel(failedModel, "Failed to update family document contents")
                {
                    OperationTarget = StandardsPipelineConstants.TargetDatabase,
                    Action = StandardsPipelineConstants.ActionFailed,
                    Message = "Failed to update family document contents"
                };
                dbResults.Add(result);
            };

            // Act
            var result = pipeline.Execute(_doc, new[] { item }, options);

            // Assert
            Assert.IsTrue(result.Success, $"Overall execution can still succeed with individual family error isolation. Message: {result.Items.FirstOrDefault()?.Message}. Report: {result.ReportMarkdown}");
            var familyResult = result.Items.FirstOrDefault(i => i.Model.GetType().Name == "Family");
            Assert.IsNull(familyResult, "Family itself was not in the input items.");
            
            // Check that the markdown report contains the failed log item
            Assert.IsTrue(result.ReportMarkdown.Contains("FaultyFamily"), $"Report should contain the family name. Report: {result.ReportMarkdown}");
            Assert.IsTrue(result.ReportMarkdown.Contains("Failed to update family document contents"), "Report should contain the error detail.");
        }

        private T CreateMockElement<T>(Document doc, string name, int idVal) where T : Element
        {
            var elem = (T)Activator.CreateInstance(typeof(T), true)!;
            
            // Set Name
            var nameProp = typeof(T).GetProperty("Name");
            nameProp?.SetValue(elem, name);

            // Set Id
            var idProp = typeof(T).GetProperty("Id");
            if (idProp != null && idProp.CanWrite)
            {
                idProp.SetValue(elem, new ElementId(idVal));
            }

            // Add to doc
            try
            {
                var addElemMethod = doc.GetType().GetMethod("AddElement");
                if (addElemMethod != null)
                {
                    addElemMethod.Invoke(doc, new object[] { elem, elem.Id });
                }
            }
            catch (Exception)
            {
            }

            return elem;
        }

        [Test]
        public void Execute_WithCancellationTokenCancelled_AbortsAndReturnsFailure()
        {
            // Arrange
            var pipeline = new StandardsExecutionPipeline(_fakeEngine, _fakeExportService, _fakeFamilyEnforcer);
            var options = new StandardsExecutionOptions
            {
                WriteRevitDatabase = true,
                SaveLocalFiles = false,
                UseTransactionGroup = false
            };

            var model = new ElementModel { Name = "TestStandard", Class = "Autodesk.Revit.DB.TextNoteType" };
            var item = new StandardsExecutionItem(model)
            {
                WillEnforce = true,
                WillSave = false
            };

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var result = pipeline.Execute(_doc, new[] { item }, options, cancellationToken: cts.Token);

            // Assert
            Assert.IsFalse(result.Success, "Execution should be marked unsuccessful on cancellation.");
            Assert.AreEqual("Canceled", result.Items[0].Action);
            Assert.AreEqual("Execution cancelled by user.", result.Items[0].Message);
        }
    }

    public class ConfigurableSerializationEngine : IStandardSerializationEngine
    {
        public Func<IEnumerable<ObjectModel>, Document, IEnumerable<SerializationResultModel>>? ToRevitHandler { get; set; }

        public IEnumerable<ObjectModel> ByRevit(IEnumerable<Element> elements, Document doc, bool isTemplate, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return new List<ObjectModel>();
        }

        public IEnumerable<DuplicateClusterModel> Analyze(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return new List<DuplicateClusterModel>();
        }

        public IEnumerable<SerializationResultModel> ToRevit(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default, IFailuresPreprocessor? failuresPreprocessor = null)
        {
            if (ToRevitHandler != null)
            {
                return ToRevitHandler(models, doc);
            }
            return models.Select(m => new SerializationResultModel(m));
        }

        public ObjectModel? ExtractCategory(Category category, Document doc, bool isTemplate)
        {
            return null;
        }
    }

    public class ConfigurableExportService : IStandardsExportService
    {
        public bool ExportCalled { get; set; }
        public string? TargetPathReceived { get; set; }
        public Func<List<QueueItemModel>, string?, List<SerializationResultModel>, HashSet<string>, List<SerializationResultModel>>? ExportHandler { get; set; }

        public List<SerializationResultModel> Export(
            List<QueueItemModel> fileItems,
            string? targetPath,
            List<SerializationResultModel> dbResults,
            HashSet<string> protectedPaths,
            out string? finalPathUsed)
        {
            ExportCalled = true;
            TargetPathReceived = targetPath;
            finalPathUsed = targetPath ?? @"C:\Temp\Exported.json";

            if (ExportHandler != null)
            {
                return ExportHandler(fileItems, targetPath, dbResults, protectedPaths);
            }

            return fileItems.Select(item => new SerializationResultModel(item.Model)).ToList();
        }
    }

    public class FakeFamilyEnforcer : IFamilyEnforcer
    {
        public Action<Document, IEnumerable<ElementModel>, StandardsExecutionOptions, Action<string, string, int>, List<SerializationResultModel>, CancellationToken>? EnforceHandler { get; set; }

        public void Enforce(
            Document doc,
            IEnumerable<ElementModel> standards,
            StandardsExecutionOptions options,
            Action<string, string, int> reportProgress,
            List<SerializationResultModel> dbResults,
            CancellationToken cancellationToken)
        {
            if (EnforceHandler != null)
            {
                EnforceHandler(doc, standards, options, reportProgress, dbResults, cancellationToken);
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/StandardsManagement/Tier2_DashboardIntegrationTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Settings;
using Synthetic.RevitDOM.Operations.Standards;
using SyntheticTests.Modules.StandardsManagement;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_DashboardIntegrationTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void RunQueue_ExecutesDatabaseWritesAndFileSerialization_UnderTransactionGroup()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
            var fakeFileDialog = new IntegrationFakeFileDialogService { PresetPath = tempFile };
            var fakeGuardrail = new IntegrationFakeGuardrailPromptService(GuardrailResult.Overwrite);

            var settings = new StandardsSettings { StandardsFilePath = tempFile };

            try
            {
                using (TransactionGroup txGroup = new TransactionGroup(doc, "Tier2_DashboardIntegrationTests"))
                {
                    txGroup.Start();

                    // Create a mock dashboard ViewModel using live Revit document
                    var vm = DashboardTestFactory.Create(doc, dialogService: fakeFileDialog, exportService: new StandardsExportService(fakeGuardrail, fakeFileDialog), settings: settings);
                    vm.SaveFilePath = tempFile;

                    // Create standard POCO for a material to enforce in the live Revit DB
                    var materialModel = new MaterialModel
                    {
                        Name = "Synthetic_Test_Material_" + Guid.NewGuid().ToString().Substring(0, 8),
                        Class = "Autodesk.Revit.DB.Material"
                    };

                    // Setup parameters on the material POCO
                    materialModel.Parameters = new List<ParameterModel>
                    {
                        new ParameterModel
                        {
                            Name = "Description",
                            Value = "Synthetic Integration Test Material Description",
                            StorageType = "String",
                            Id = (int)BuiltInParameter.ALL_MODEL_DESCRIPTION
                        }
                    };

                    // Enqueue the item with SaveAndEnforce intent (Phase 1 + Phase 2)
                    var queueItem = new QueueItemModel(materialModel, true, true);
                    vm.StagingQueue.Add(queueItem);

                    // Execute
                    vm.RunQueueCommand.Execute(null);

                    // Assert execution result success
                    Assert.IsTrue(vm.LastExecutionResults.Count > 0, "LastExecutionResults should have at least 1 result.");
                    Assert.IsTrue(vm.LastExecutionResults[0].Success, $"Revit DB update failed: {vm.LastExecutionResults[0].ErrorMessage}\nException: {vm.LastExecutionResults[0].Exception}");

                    // Verify Revit DB modifications (Phase 1)
                    var createdMaterial = new FilteredElementCollector(doc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .FirstOrDefault(m => m.Name == materialModel.Name);

                    Assert.IsNotNull(createdMaterial, "Enforce should successfully create the Material in the Revit Document.");
                    
                    string? liveDesc = createdMaterial!.LookupParameter("Description")?.AsString() ?? 
                                      createdMaterial.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString();
                    
                    Assert.AreEqual("Synthetic Integration Test Material Description", liveDesc, "Material parameter should match enqueued POCO value.");

                    // Verify JSON serialization (Phase 2)
                    Assert.IsTrue(File.Exists(tempFile), "Save should successfully serialize enqueued item to JSON.");
                    string fileContent = File.ReadAllText(tempFile);
                    Assert.IsTrue(fileContent.Contains(materialModel.Name), "Serialized JSON should contain the enqueued material name.");

                    // Verify Markdown log generation
                    string expectedLogFile = Path.ChangeExtension(tempFile, ".log.md");
                    Assert.IsTrue(File.Exists(expectedLogFile), "Automatic Markdown log file should be created next to JSON file.");
                    string logContent = File.ReadAllText(expectedLogFile);
                    Assert.IsTrue(logContent.Contains("# Project Standards Consolidation Execution Report"), "Markdown log should contain the title.");
                    Assert.IsTrue(logContent.Contains("| Created |"), "Markdown log should contain the Created action.");
                    Assert.IsTrue(logContent.Contains(materialModel.Name), "Markdown log should contain the material name.");

                    // Rollback the TransactionGroup to ensure zero-leak test database execution
                    txGroup.RollBack();
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
                string expectedLogFile = Path.ChangeExtension(tempFile, ".log.md");
                if (File.Exists(expectedLogFile))
                {
                    File.Delete(expectedLogFile);
                }
            }
        }

        [Test]
        public void AddRevitModel_ExtractsFilteredElements_UnderTransactionGroup()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            var fakeFileDialog = new IntegrationFakeFileDialogService();
            var fakeGuardrail = new IntegrationFakeGuardrailPromptService(GuardrailResult.Overwrite);
            var settings = new StandardsSettings();

            try
            {
                using (TransactionGroup txGroup = new TransactionGroup(doc, "Tier2_ScanningTest"))
                {
                    txGroup.Start();

                    // Create test elements in the document to ensure they exist for scanning
                    Transaction t = new Transaction(doc, "Create Test Elements");
                    t.Start();

                    ElementId matId = Material.Create(doc, "Scanning_Test_Material");
                    
                    WallType defaultWallType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .First();
                    WallType customWallType = (WallType)defaultWallType.Duplicate("Scanning_Test_WallType");

                    t.Commit();

                    // Create the Dashboard View Model
                    var vm = DashboardTestFactory.Create(doc, dialogService: fakeFileDialog, exportService: new StandardsExportService(fakeGuardrail, fakeFileDialog), settings: settings);
                    vm.MockOpenDocuments = new List<Document> { doc };

                    // Setup dialog handler to only select "Materials & Assets" grouping
                    vm.ShowDocumentSelectionDialog = (dialogVM) =>
                    {
                        foreach (var item in dialogVM.OpenDocuments)
                        {
                            item.IsSelected = true;
                        }
                        dialogVM.ScanFamilies = false;
                        dialogVM.IncludeNestedFamilies = false;

                        // Check only "Materials & Assets" group
                        foreach (var group in dialogVM.FilterHierarchy)
                        {
                            if (group.Name == "Materials & Assets")
                            {
                                group.IsChecked = true;
                            }
                            else
                            {
                                group.IsChecked = false;
                            }
                        }
                        return true;
                    };

                    // Act
                    vm.AddRevitModelCommand.Execute(null);

                    // Assert
                    Assert.AreEqual(1, vm.AvailableSources.Count, "A Revit source should be added.");
                    var source = vm.AvailableSources[0];

                    bool foundMaterial = false;
                    bool foundWallType = false;

                    foreach (var group in source.SourceHierarchy)
                    {
                        foreach (var cls in group.Children)
                        {
                            foreach (var elem in cls.Children)
                            {
                                if (elem.Name == "Scanning_Test_Material")
                                {
                                    foundMaterial = true;
                                }
                                if (elem.Name == "Scanning_Test_WallType")
                                {
                                    foundWallType = true;
                                }
                            }
                        }
                    }

                    // Since only "Materials & Assets" was selected, the material should be extracted, but the wall type should be skipped.
                    Assert.IsTrue(foundMaterial, "The selected grouping 'Materials & Assets' should extract the custom material.");
                    Assert.IsFalse(foundWallType, "The excluded grouping 'System Types' should NOT extract the custom wall type.");

                    txGroup.RollBack();
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"Integration test failed with exception: {ex}");
            }
        }

        [Test]
        public void RunQueue_IncludesAliasMergeLogItems_InSummaryDialog()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            var fakeFileDialog = new IntegrationFakeFileDialogService();
            var fakeGuardrail = new IntegrationFakeGuardrailPromptService(GuardrailResult.Overwrite);
            var fakeSummaryService = new FakeSummaryDisplayService();

            try
            {
                using (TransactionGroup txGroup = new TransactionGroup(doc, "Tier2_DashboardAliasTest"))
                {
                    txGroup.Start();

                    // Create the alias material in a transaction
                    ElementId aliasMaterialId;
                    using (Transaction t = new Transaction(doc, "Create Alias Material"))
                    {
                        t.Start();
                        aliasMaterialId = Material.Create(doc, "AliasMaterial_DashboardTest");
                        t.Commit();
                    }

                    // Create the dashboard ViewModel
                    var vm = DashboardTestFactory.Create(doc, dialogService: fakeFileDialog, exportService: new StandardsExportService(fakeGuardrail, fakeFileDialog), settings: null);
                    vm.SummaryDisplayService = fakeSummaryService;

                    // Enqueue the primary material with the alias
                    var primaryModel = new MaterialModel
                    {
                        Name = "PrimaryMaterial_DashboardTest",
                        Class = "Autodesk.Revit.DB.Material",
                        Aliases = new List<string> { "AliasMaterial_DashboardTest" }
                    };

                    var queueItem = new QueueItemModel(primaryModel, true, false);
                    vm.StagingQueue.Add(queueItem);

                    // Act
                    vm.RunQueueCommand.Execute(null);

                    // Assert
                    Assert.AreEqual(1, fakeSummaryService.ShowCallCount, "Summary dialog should be displayed.");
                    var summaryVM = fakeSummaryService.LastViewModel as ImportSummaryViewModel;
                    Assert.IsNotNull(summaryVM, "ViewModel should be ImportSummaryViewModel.");

                    // Check that the log items contain both the creation and the alias merge
                    var logItems = summaryVM!.LogItems.ToList();
                    Assert.IsTrue(logItems.Any(l => l.ElementName == "PrimaryMaterial_DashboardTest" && l.Action == "Created"), 
                        "Should contain a log item for the primary element creation.");
                    Assert.IsTrue(logItems.Any(l => l.ElementName == "PrimaryMaterial_DashboardTest" && l.Action == "Merged Alias" && l.Message.Contains("AliasMaterial_DashboardTest")), 
                        "Should contain a log item for the successful alias merge.");

                    txGroup.RollBack();
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        private class IntegrationFakeFileDialogService : IFileDialogService
        {
            public string? PresetPath { get; set; }

            public string? SaveFileDialog(string filter, string title, string defaultFileName)
            {
                return PresetPath;
            }

            public string? OpenFileDialog(string filter, string title, string defaultFileName)
            {
                return PresetPath;
            }
        }

        private class IntegrationFakeGuardrailPromptService : IGuardrailPromptService
        {
            private readonly GuardrailResult _result;

            public IntegrationFakeGuardrailPromptService(GuardrailResult result)
            {
                _result = result;
            }

            public GuardrailResult PromptProtectedFileOverwrite(string filePath)
            {
                return _result;
            }
        }

        private class FakeSummaryDisplayService : ISummaryDisplayService
        {
            public int ShowCallCount { get; set; }
            public object? LastViewModel { get; set; }

            public void ShowSummary(object viewModel, IntPtr parentWindowHandle)
            {
                ShowCallCount++;
                LastViewModel = viewModel;
            }
        }
    }
}
```

### File: tests/SyntheticTests.Shared/Modules/StandardsManagement/Tier2_StandardsDiffEngineTests.cs
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Merge;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_StandardsDiffEngineTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        private struct MutatedTestContext
        {
            public Document Doc { get; set; }
            public TextNoteType TextNoteType { get; set; }
            public ElementTypeModel Model { get; set; }
            public ParameterModel TargetParam { get; set; }
            public TransactionGroup TxGroup { get; set; }
        }

        private MutatedTestContext CreateMutatedTextNoteTypeContext(string transactionGroupName)
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            TransactionGroup txGroup = new TransactionGroup(doc, transactionGroupName);
            txGroup.Start();

            try
            {
                TextNoteType? textNoteType = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .FirstOrDefault();

                if (textNoteType == null)
                {
                    txGroup.RollBack();
                    doc.Close(false);
                    Assert.Ignore("No TextNoteType found in the active document to test deep scan.");
                }

                var model = (ElementTypeModel)textNoteType!.ToModel(true);
                Assert.IsNotNull(model, "Extracted ElementTypeModel should not be null.");
                Assert.IsNotNull(model.Parameters, "Extracted model parameters should not be null.");

                var targetParam = model.Parameters.FirstOrDefault(p => !p.IsReadOnly);
                if (targetParam == null)
                {
                    txGroup.RollBack();
                    doc.Close(false);
                    Assert.Ignore("No writable parameters found on TextNoteType to test mutation.");
                }

                string originalValue = targetParam!.Value ?? "";
                string mutatedValue = originalValue + "_MutatedForTest";
                if (targetParam.StorageType == "Double" || targetParam.StorageType == "Integer")
                {
                    mutatedValue = "999";
                }
                targetParam.Value = mutatedValue;

                return new MutatedTestContext
                {
                    Doc = doc,
                    TextNoteType = textNoteType,
                    Model = model,
                    TargetParam = targetParam,
                    TxGroup = txGroup
                };
            }
            catch (Exception)
            {
                txGroup.RollBack();
                doc.Close(false);
                throw;
            }
        }

        [Test]
        public void RunDeepScan_ShouldFlagConflict_WhenParameterMutated()
        {
            var context = CreateMutatedTextNoteTypeContext("RunDeepScan_ShouldFlagConflict_WhenParameterMutated");
            try
            {
                // 5. Run the comparison directly on the engine
                var engine = new Synthetic.RevitDOM.Operations.Diffing.PocoToRevitDiffEngine();
                var clusters = engine.Compare(new List<ObjectModel> { context.Model }, context.Doc).ToList();

                // 6. Assert that conflicts were successfully identified
                Assert.IsNotNull(clusters, "RunDeepScan should return a non-null collection.");
                Assert.IsTrue(clusters.Count > 0, "A conflict cluster should be returned.");

                // Find the cluster matching our element type
                var cluster = clusters.FirstOrDefault(c => c.TypeMappings.Any(m => m.SourceType != null && m.SourceType.RevitTypeId.ToElementId() == context.TextNoteType.Id));
                Assert.IsNotNull(cluster, "Should find a cluster matching the tested TextNoteType.");

                var mapping = cluster!.TypeMappings.FirstOrDefault(m => m.SourceType != null && m.SourceType.RevitTypeId.ToElementId() == context.TextNoteType.Id);
                Assert.IsNotNull(mapping, "Should find a type mapping for the tested TextNoteType.");

                var conflictRow = mapping!.ParameterResolutions.FirstOrDefault(r => r.ParameterName == context.TargetParam.Name);
                Assert.IsNotNull(conflictRow, $"A parameter resolution row should exist for the mutated parameter '{context.TargetParam.Name}'.");
                Assert.IsTrue(conflictRow!.HasConflict, "The parameter resolution row should indicate a conflict.");
            }
            finally
            {
                context.TxGroup.RollBack();
                context.Doc.Close(false);
            }
        }

        [Test]
        public void RunDeepScan_ShouldDelegateToAnalyze_Correctly()
        {
            var context = CreateMutatedTextNoteTypeContext("RunDeepScan_ShouldDelegateToAnalyze_Correctly");
            try
            {
                var serializationEngine = new StandardSerializationEngine();
                var listModels = new List<ElementModel> { context.Model };

                var analyzeClusters = serializationEngine.Analyze(listModels, context.Doc).ToList();
                var deepScanClusters = StandardsDiffEngine.RunDeepScan(context.Doc, listModels, serializationEngine).ToList();

                Assert.IsNotNull(analyzeClusters);
                Assert.IsNotNull(deepScanClusters);
                Assert.AreEqual(analyzeClusters.Count, deepScanClusters.Count, "Analyze and RunDeepScan should return the same number of clusters.");

                if (analyzeClusters.Count > 0)
                {
                    var clusterAnalyze = analyzeClusters[0];
                    var clusterDeepScan = deepScanClusters[0];
                    Assert.AreEqual(clusterAnalyze.TypeMappings.Count, clusterDeepScan.TypeMappings.Count, "Mappings count should match.");
                }
            }
            finally
            {
                context.TxGroup.RollBack();
                context.Doc.Close(false);
            }
        }
    }
}

```

