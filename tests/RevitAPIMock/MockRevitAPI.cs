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
        public void Dispose() { }
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
