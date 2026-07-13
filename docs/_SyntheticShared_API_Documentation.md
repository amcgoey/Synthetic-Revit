# SyntheticShared API Documentation

This document contains automatically extracted XML developer documentation from the C# source files in the `SyntheticShared` project.

## Namespace: `Synthetic`

### Class: `App`
**File:** [src/SyntheticShared/App.cs](../src/SyntheticShared/App.cs)

The main external application class for the Revit addon. Manages the application lifecycle, ribbon UI creation, and document events.

#### Methods
##### `OnStartup`
```csharp
public Result OnStartup(UIControlledApplication appControlled)
```
Called when Revit starts up. Initializes the ribbon UI and registers document event handlers.

**Parameters:**
- `appControlled`: The UIControlledApplication object representing the Revit application.

**Returns:** A Result indicating success or failure of the startup process.

##### `OnShutdown`
```csharp
public Result OnShutdown(UIControlledApplication appControlled)
```
Called when Revit shuts down. Performs any necessary cleanup.

**Parameters:**
- `appControlled`: The UIControlledApplication object representing the Revit application.

**Returns:** A Result indicating success or failure of the shutdown process.

##### `OnDocumentOpen`
```csharp
public void OnDocumentOpen(object sender, Autodesk.Revit.DB.Events.DocumentOpenedEventArgs args)
```
Event handler triggered when a Revit document is opened. Reads and registers the project configuration for the opened document.

**Parameters:**
- `sender`: The source of the event.
- `args`: The DocumentOpenedEventArgs containing event data.

##### `OnDocumentClose`
```csharp
public void OnDocumentClose(object sender, Autodesk.Revit.DB.Events.DocumentClosingEventArgs args)
```
Event handler triggered when a Revit document is closing. Removes the project configuration for the closing document.

**Parameters:**
- `sender`: The source of the event.
- `args`: The DocumentClosingEventArgs containing event data.

---

#### Fields
- **`_path`**: `static string _path = System.Reflection.Assembly.GetExecutingAssembly().Location;`
- **`AppControlled`**: `public static UIControlledApplication AppControlled;`
  *Description:* Gets or sets the UIControlledApplication instance for the application session.
- **`Configurations`**: `public static ConfigCollection Configurations = new ConfigCollection();`
  *Description:* Gets the configuration collection managing app and project settings.
- **`doc`**: `Document doc = args.Document;`
- **`configProject`**: `Config configProject = Config.ReadProjectConfig(doc);`
- **`mainWindowHandle`**: `IntPtr mainWindowHandle = IntPtr.Zero;`
- **`uiApp`**: `var uiApp = new Autodesk.Revit.UI.UIApplication(doc.Application);`
- **`handler`**: `var handler = new Handlers.SyncExternalEventHandler();`
- **`externalEvent`**: `var externalEvent = ExternalEvent.Create(handler);`
- **`toast`**: `var toast = new Views.SyncToastNotification(mainWindowHandle, doc, linkedFilePath, handler, externalEvent);`
- **`doc`**: `Document doc = args.Document;`

---

### Class: `BBPrinterSettingsUtils`
**File:** [src/SyntheticShared/Utilities/BBPrinterSettingsUtils.cs](../src/SyntheticShared/Utilities/BBPrinterSettingsUtils.cs)

Utilities for setting BlueBeam Printer settings

#### Methods
##### `BBPrinterSettingsUtils`
```csharp
public BBPrinterSettingsUtils ()
```
Initializes a new instance of the <see cref="BBPrinterSettingsUtils"/> class by reading values from the registry.

##### `BBPrinterSettingsUtils`
```csharp
public BBPrinterSettingsUtils (string openInViewer, string promptForFileName, string path)
```
Initializes a new instance of the <see cref="BBPrinterSettingsUtils"/> class with custom settings.  <param name="openInViewer">Specifies whether to open in viewer.</param> <param name="promptForFileName">Specifies whether to prompt for file name.</param> <param name="path">The folder path for projects and save locations.</param>

##### `SetRegistryKeys`
```csharp
public void SetRegistryKeys ()
```
Sets registry keys according to the current property values.

##### `SetDefaultRegistryKeys`
```csharp
public void SetDefaultRegistryKeys()
```
Sets registry keys to their default values.

---

#### Fields
- **`OpenInViewer`**: `public string OpenInViewer;`
  *Description:* Gets or sets the value specifying whether to open in viewer.
- **`PromptForFileName`**: `public string PromptForFileName;`
  *Description:* Gets or sets the value specifying whether to prompt for file name.
- **`ProjectsFolder`**: `public string ProjectsFolder;`
  *Description:* Gets or sets the projects folder path.
- **`SaveAsFolder`**: `public string SaveAsFolder;`
  *Description:* Gets or sets the save as folder path.
- **`UseLastFolder`**: `public string UseLastFolder;`
  *Description:* Gets or sets the value specifying whether to use the last folder.

---

### Class: `CommandUtil`
**File:** [src/SyntheticShared/Utilities/CommandUtil.cs](../src/SyntheticShared/Utilities/CommandUtil.cs)

Utility methods for executing Revit commands and saving/loading results.

#### Methods
##### `SaveResults`
```csharp
public static string SaveResults(object results, Document document, string fileName = "Results", string path = null)
```
Saves a command results to a json file in the same location as the document.

**Parameters:**
- `results`: The object/results to serialize.
- `document`: The Revit document.
- `fileName`: The base file name for the saved JSON file.
- `path`: The folder path to save the file. If null, the document folder is used.

**Returns:** The full path of the saved file.

##### `SaveAsResults`
```csharp
public static string SaveAsResults(object results)
```
Displays a save file dialog and saves the command results to the chosen JSON file path.

**Parameters:**
- `results`: The object/results to serialize.

**Returns:** The full path of the saved file, or null if cancelled.

##### `SaveAsJSON`
```csharp
public static string SaveAsJSON(string initialFileName = null)
```
Opens a FileSaveDialog to select a path and file name for a JSON file.

**Parameters:**
- `initialFileName`: Optional initial file name to suggest in the dialog.

**Returns:** Full path of the file to save.

##### `OpenJSON`
```csharp
public static string OpenJSON()
```
Selects a JSON file to open

**Returns:** the full path of the JSON file.

---

#### Fields
- **`newFileName`**: `string newFileName = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " - " + fileName;`
- **`ext`**: `string ext = ".json";`
- **`fullPath`**: `string fullPath = Path.Combine(path, newFileName + ext);`
- **`json`**: `string json = Newtonsoft.Json.JsonConvert.SerializeObject(results, Formatting.Indented);`
- **`fullPath`**: `return fullPath;`
- **`file`**: `string file = null;`
- **`path`**: `string path = null;`
- **`proceed`**: `bool proceed = false;`
- **`fullPath`**: `string fullPath = null;`
- **`saveFileDialog`**: `FileSaveDialog saveFileDialog = new FileSaveDialog("JSON Files (*.json)|*.json");`
- **`resultSave`**: `ItemSelectionDialogResult resultSave = saveFileDialog.Show();`
- **`modelPath`**: `ModelPath modelPath = saveFileDialog.GetSelectedModelPath();`
- **`json`**: `string json = Newtonsoft.Json.JsonConvert.SerializeObject(results, Formatting.Indented);`
- **`fullPath`**: `return fullPath;`
- **`file`**: `string file = null;`
- **`path`**: `string path = null;`
- **`proceed`**: `bool proceed = false;`
- **`fullPath`**: `string fullPath = null;`
- **`saveFileDialog`**: `FileSaveDialog saveFileDialog = new FileSaveDialog("JSON Files (*.json)|*.json");`
- **`resultSave`**: `ItemSelectionDialogResult resultSave = saveFileDialog.Show();`
- **`modelPath`**: `ModelPath modelPath = saveFileDialog.GetSelectedModelPath();`
- **`fullPath`**: `return fullPath;`
- **`file`**: `string file = null;`
- **`path`**: `string path = null;`
- **`proceed`**: `bool proceed = false;`
- **`fullPath`**: `string fullPath = null;`
- **`openFileDialog`**: `FileOpenDialog openFileDialog = new FileOpenDialog("JSON Files (*.json)|*.json");`
- **`resultSave`**: `ItemSelectionDialogResult resultSave = openFileDialog.Show();`
- **`modelPath`**: `ModelPath modelPath = openFileDialog.GetSelectedModelPath();`
- **`fullPath`**: `return fullPath;`

---

### Class: `DocumentUtil`
**File:** [src/SyntheticShared/Utilities/DocumentUtil.cs](../src/SyntheticShared/Utilities/DocumentUtil.cs)

Utility methods for interacting with Revit Document objects.

#### Methods
##### `OpenRvtDetached`
```csharp
public static Document OpenRvtDetached(ModelPath path, UIApplication uiApp)
```
Opens an RVT Revit file as detached.

**Parameters:**
- `path`: Autodesk.Revit.DB.ModelPath object pointing to the Revit file to open.
- `uiApp`: The Autodesk.Revit.UI.UIApplication object to openthe file in.

**Returns:** The Autodeks.Revit.DB.Document object of the opened document.

##### `GetFilePath`
```csharp
public static string GetFilePath(Document document)
```
Retrieves the file path of the document depending on if the file is a cloud model, workshared or just a regular project.

**Parameters:**
- `document`: A Autodesk.Revit.DB.Document obejct.

**Returns:** A string of the path to the document.

##### `if`
```csharp
else if (document.IsWorkshared)
```
##### `GetFolderPath`
```csharp
public static string GetFolderPath(Document document)
```
Retrieves the folder path of the document depending on if the file is a cloud model, workshared or just a regular project.

**Parameters:**
- `document`: A Autodesk.Revit.DB.Document obejct.

**Returns:** A string of the path to the document.

##### `if`
```csharp
else if (document.IsWorkshared)
```
##### `Purge`
```csharp
public static bool Purge(Application app, Document doc)
```
Using etransmit, purges the model.

**Parameters:**
- `app`: The Revit Application
- `doc`: Revit Document object

**Returns:** True if purge was successful.

---

#### Fields
- **`doc`**: `Document doc = null;`
- **`pathString`**: `string pathString = ModelPathUtils.ConvertModelPathToUserVisiblePath(path);`
- **`fileInfo`**: `BasicFileInfo fileInfo = BasicFileInfo.Extract(pathString);`
- **`doc`**: `return doc;`
- **`fullPath`**: `string fullPath = null;`
- **`fullPath`**: `return fullPath;`
- **`fullPath`**: `string fullPath = null;`
- **`folderPath`**: `string folderPath = null;`
- **`folderPath`**: `return folderPath;`

---

### Class: `ElementUtil`
**File:** [src/SyntheticShared/Utilities/ElementUtil.cs](../src/SyntheticShared/Utilities/ElementUtil.cs)

Manipulation and modification of Dynamo wrapped Revit elements.

#### Methods
##### `ElementUtil`
```csharp
internal ElementUtil() { }
```
Dummy constructor for the class.  Not used.

##### `Document`
```csharp
public static RevitDoc Document(RevitElem Element)
```
Gets an elements document

**Parameters:**
- `Element`: A dynamo wrapped element

**Returns:** A Autodesk.Revit.DB.Document

##### `SetParamterByDictionary`
```csharp
public static RevitElem SetParamterByDictionary(RevitElem element, Dictionary<string, RevitDB.Parameter> dictionary)
```
Sets an element's parameters based on a Dictionary object with the Key being the parameter name and the Value being the parameter value.

**Parameters:**
- `element`: A Dynamo wrapped element.
- `dictionary`: A Synthetic Dictionary

##### `TransferParameters`
```csharp
public static RevitElem TransferParameters(RevitElem Element, RevitElem SourceElement)
```
Overwrite an elements parameters with the parameter values from a source element.

**Parameters:**
- `Element`: Destination element
- `SourceElement`: Source element for the parameter values

**Returns:** The destination element

##### `CopyElements`
```csharp
public static List<RevitElemId> CopyElements(RevitDoc sourceDoc, List<int> elementIds, RevitDoc destinationDoc)
```
Copy elements to the same location between documents.  Can be used to copy system types or view templates between documents.  Model elements are copied in the same location.  If the elements already exist, Revit will give you an option to either duplicate the types or cancel the operation.  Please note that documents are to be a Autodesk.Revit.DB.Document objects, not a Dynamo wrapped Revit Document.

**Parameters:**
- `sourceDoc`: The source document to copy items from.
- `elementIds`: List of Element Ids of elements to be copied.
- `destinationDoc`: The destination document.

##### `SetCategory`
```csharp
public static IDictionary SetCategory(RevitElem element, RevitDB.Category category)
```
Changes an element's subcategory.  Only works inside of Family documents

**Parameters:**
- `element`: Element to change
- `category`: Subcategory to set the element too.

**Returns:** If the element's category was changed.

##### `SetWorkset`
```csharp
public static List<RevitElem> SetWorkset(List<RevitElem> elements, Workset workset, RevitDoc document)
```
Given a list of elements, sets their worksets to the given workset.

**Parameters:**
- `elements`: A list of elements to change
- `workset`: A Revit Workset object
- `document`: A Revit Document

**Returns:** The list of elements for chaining.

##### `MergeElementTypes`
```csharp
public static IDictionary MergeElementTypes(RevitElem FromType, RevitElem ToType)
```
Merges ElementType FromType into ToType.  FromType will be deleted if all instances of the Type are successfully changed.  Elements in groups will not be changed.

**Parameters:**
- `FromType`: All instances of this ElementType will be merged into the ToType and the Type will be deleted.
- `ToType`: ElementType to merge into.

**Returns:** A list of instances that were successfully changed to ToType

##### `MergeElementTypesRevit`
```csharp
public static IDictionary MergeElementTypesRevit(RevitElem FromType, RevitElem ToType)
```
Merges ElementType FromType into ToType.  FromType will be deleted if all instances of the Type are successfully changed.  Elements in groups will not be changed.

**Parameters:**
- `FromType`: All instances of this ElementType will be merged into the ToType and the Type will be deleted.
- `ToType`: ElementType to merge into.

**Returns:** A list of instances that were successfully changed to ToType

##### `FilterByParameterValue`
```csharp
public static bool FilterByParameterValue(RevitElem element, List<string> parameterNames, string value)
```
Tests whether the element has a parameter of a given value.  Returns true if the parameter has an equal value and false otherwise.  A list of parameters names can be given to test against values in elements within parameters.  For example, one can test for a type description from a instance by creating a list of each parameter.  Please note that the comparision is done using the string representation of each parameter.

**Parameters:**
- `element`: A dynamo wrapped Revit element.
- `parameterNames`: A list of parameter names.  The parameters in the list will each be retrieved iteratively.  So the first name is on the input element, the next name on the element returned from the first parameter and so on.
- `value`: A parameter value as a string.

**Returns:** True if the element's parameter equals the input value, otherwise false.

##### `GetByElementId`
```csharp
public static RevitElem GetByElementId(RevitElemId elementId, RevitDoc document)
```
Gets an element given the ElementId

**Parameters:**
- `elementId`: A Autodesk.Revit.DB.ElementId
- `document`: Document that the element is in.

**Returns:** Returns a unwrapped Autodesk.Revit.DB.Element

##### `GetByUniqueId`
```csharp
public static RevitElem GetByUniqueId(string UniqueId, RevitDoc document)
```
Gets an element given its UniqueId

**Parameters:**
- `UniqueId`: A UniqueId as a string
- `document`: Document that the element is in.

**Returns:** Returns a unwrapped Autodesk.Revit.DB.Element

##### `Id`
```csharp
public static RevitElemId Id(System.Object Element)
```
Gets a Element's ElementId

**Parameters:**
- `Element`: A Autodesk.Revit.DB.Element, NOT a Dynamo wrapped element

**Returns:** The Autodesk.Revit.DB.ElementId

##### `Name`
```csharp
public static string Name(System.Object Element)
```
Gets a Element's name

**Parameters:**
- `Element`: A Autodesk.Revit.DB.Element, NOT a Dynamo wrapped element

**Returns:** The name of the element

##### `UniqueId`
```csharp
public static string UniqueId(System.Object Element)
```
Gets a Element's UniqueId

**Parameters:**
- `Element`: A Autodesk.Revit.DB.Element, NOT a Dynamo wrapped element

**Returns:** The UniqueId of the element

##### `CastRevitElement`
```csharp
public static RevitElem CastRevitElement(System.Object Object)
```
If the object

**Parameters:**
- `Object`: 

##### `PaintElement`
```csharp
public static RevitElem PaintElement(RevitElem Element, RevitElemId MaterialId)
```
Paints every face in an element with a material

**Parameters:**
- `Element`: The element to paint
- `MaterialId`: The material to paint

**Returns:** The modified element

##### `if`
```csharp
else if (geo is RevitDB.Face)
```
##### `RemovePaintElement`
```csharp
public static RevitElem RemovePaintElement(RevitElem Element)
```
Removes all painted faces on an elementl

**Parameters:**
- `Element`: The element to removve painted faces

**Returns:** The modified element

##### `if`
```csharp
else if (geo is RevitDB.Face)
```
##### `_transferParameters`
```csharp
private static void _transferParameters(RevitElem SourceElement, RevitElem DestinationElement)
```
---

#### Fields
- **`doc`**: `RevitDoc doc = element.Document;`
- **`result`**: `object result;`
- **`element`**: `return element;`
- **`doc`**: `RevitDoc doc = element.Document;`
- **`dict`**: `return dict;`
- **`transactionName`**: `string transactionName = "Transfer Parameter Between Elements";`
- **`document`**: `RevitDoc document = Element.Document;`
- **`Element`**: `return Element;`
- **`transactionName`**: `string transactionName = "Copy Elements from document " + sourceDoc.Title;`
- **`copiedElemsIds`**: `List<RevitElemId> copiedElemsIds;`
- **`revitElemIds`**: `List<RevitElemId> revitElemIds = new List<RevitElemId>();`
- **`copiedElemsIds`**: `return copiedElemsIds;`
- **`destinationDoc`**: `RevitDoc destinationDoc = Element.Document;`
- **`sourceDoc`**: `RevitDoc sourceDoc = SourceElement.Document;`
- **`transactionName`**: `string transactionName = "Element overwritten from " + sourceDoc.Title;`
- **`returnElem`**: `RevitElem returnElem;`
- **`revitElemIds`**: `List<RevitElemId> revitElemIds = new List<RevitElemId>();`
- **`ids`**: `List<RevitElemId> ids = (List<RevitElemId>)Autodesk.Revit.DB.ElementTransformUtils.CopyElements(sDoc, revitElemIds, dDoc, null, cpo);`
- **`tempElem`**: `RevitElem tempElem = dDoc.GetElement(ids[0]);`
- **`dElem`**: `return dElem;`
- **`returnElem`**: `return returnElem;`
- **`transactionName`**: `string transactionName = "Set Element Category to" + category.Name;`
- **`document`**: `RevitDoc document = element.Document;`
- **`elementsMerged`**: `List<RevitElem> elementsMerged = new List<RevitElem>();`
- **`elementsFailed`**: `List<RevitElem> elementsFailed = new List<RevitElem>();`
- **`groupId`**: `long groupId = elem.GroupId.IntegerValue;`
- **`groupId`**: `long groupId = elem.GroupId.Value;`
- **`transactionName`**: `string transactionName = "Move " + elements.Count + " elements to workset " + workset.Name;`
- **`worksetParam`**: `Parameter worksetParam = element.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);`
- **`elements`**: `return elements;`
- **`transactionName`**: `string transactionName = "Merge Element Type";`
- **`document`**: `RevitDoc document = rToType.Document;`
- **`elementsMerged`**: `List<RevitElem> elementsMerged = new List<RevitElem>();`
- **`elementsFailed`**: `List<RevitElem> elementsFailed = new List<RevitElem>();`
- **`groupId`**: `long groupId = elem.GroupId.IntegerValue;`
- **`groupId`**: `long groupId = elem.GroupId.Value;`
- **`dElem`**: `RevitElem dElem = elem;`
- **`dElem`**: `RevitElem dElem = elem;`
- **`transactionName`**: `string transactionName = "Merge Element Type";`
- **`document`**: `RevitDoc document = ToType.Document;`
- **`elementsMerged`**: `List<RevitElem> elementsMerged = new List<RevitElem>();`
- **`elementsFailed`**: `List<RevitElem> elementsFailed = new List<RevitElem>();`
- **`groupId`**: `long groupId = elem.GroupId.IntegerValue;`
- **`groupId`**: `long groupId = elem.GroupId.Value;`
- **`filter`**: `bool filter;`
- **`valueParam`**: `object valueParam = element;`
- **`vType`**: `Type vType = valueParam.GetType();`
- **`e`**: `RevitElem e = (RevitElem)valueParam;`
- **`filter`**: `return filter;`
- **`elem`**: `RevitElem elem = (RevitElem)Element;`
- **`elem`**: `RevitElem elem = (RevitElem)Element;`
- **`elem`**: `RevitElem elem = (RevitElem)Element;`
- **`transactionName`**: `string transactionName = "Paint Faces of Element";`
- **`document`**: `RevitDoc document = Element.Document;`
- **`elementId`**: `RevitElemId elementId = Element.Id;`
- **`Element`**: `return Element;`
- **`transactionName`**: `string transactionName = "Remove Painted Faces of Element";`
- **`document`**: `RevitDoc document = Element.Document;`
- **`elementId`**: `RevitElemId elementId = Element.Id;`
- **`Element`**: `return Element;`

---

### Class: `EnumUtil`
**File:** [src/SyntheticShared/Utilities/EnumUtil.cs](../src/SyntheticShared/Utilities/EnumUtil.cs)

Wrapper for using enumerations

#### Methods
##### `EnumUtil`
```csharp
internal EnumUtil() { }
```
##### `GetNames`
```csharp
public static List<string> GetNames(string enumTypeName)
```
Retrieves a list of the names of the constants in a specified enumeration.

**Parameters:**
- `enumTypeName`: The enum type as a string. Requires the full assembly path in front of the type.

**Returns:** A string list of the names of the constants in enumType.

##### `GetName`
```csharp
public static string GetName(Enum enumeration)
```
Retrieves a list of the names of the constants in a specified enumeration.

**Parameters:**
- `enumTypeName`: The enum type as a string. Requires the full assembly path in front of the type.

**Returns:** A string list of the names of the constants in enumType.

##### `GetValue`
```csharp
public static object GetValue(object enumeration)
```
Evaluates a enum and returns its value.

**Parameters:**
- `enumeration`: A enum

**Returns:** Returns the value of the enum

##### `GetEnumType`
```csharp
public static Type GetEnumType(string enumTypeName)
```
Retrieves the Enum Type from a string name.

**Parameters:**
- `enumTypeName`: The enum type as a string. Requires the full assembly path in front of the type.

**Returns:** Returns the enum type if it exists in the current domain.

##### `IsDefined`
```csharp
public static bool IsDefined(string enumTypeName, string name)
```
Returns an indication whether a constant with a specified value exists in a specified enumeration.

**Parameters:**
- `enumTypeName`: The enum type as a string. Requires the full assembly path in front of the type.
- `name`: Name of the enum.

**Returns:** True if a constant in enumType has a value equal to value; otherwise, false.

---

#### Fields
- **`et`**: `Type et = GetEnumType(enumTypeName);`
- **`e`**: `return e;`
- **`e`**: `List<string> e = new List<string>();`
- **`eType`**: `Type eType = GetEnumType(enumTypeName);`
- **`e`**: `return e;`
- **`enumType`**: `var enumType = enumeration.GetType();`
- **`eType`**: `Type eType = GetEnumType(enumTypeName);`
- **`eList`**: `return eList;`
- **`enumType`**: `var enumType = enumeration.GetType();`
- **`underlyingType`**: `var underlyingType = Enum.GetUnderlyingType(enumType);`
- **`numericValue`**: `var numericValue = System.Convert.ChangeType(enumeration, underlyingType);`
- **`numericValue`**: `return numericValue;`
- **`type`**: `var type = assembly.GetType(enumTypeName);`
- **`type`**: `return type;`
- **`null`**: `return null;`
- **`eType`**: `Type eType = GetEnumType(enumTypeName);`

---

### Class: `FamilySymbolUtil`
**File:** [src/SyntheticShared/Utilities/FamilySymbolUtil.cs](../src/SyntheticShared/Utilities/FamilySymbolUtil.cs)

Utility methods for managing and query Revit FamilySymbol elements.

#### Methods
##### `GetByName`
```csharp
public static FamilySymbol GetByName (RevitDoc doc, string familyName, string symbolName)
```
Retrieves a FamilySymbol by its family name and symbol/type name.

**Parameters:**
- `doc`: The active Revit document.
- `familyName`: The name of the Family.
- `symbolName`: The name of the FamilySymbol/Type.

**Returns:** The matching FamilySymbol, or null if not found.

##### `GetFamiliesOfCategory`
```csharp
public static IList<Element> GetFamiliesOfCategory (RevitDoc doc, BuiltInCategory category)
```
Gets all family symbols/types in the document belonging to the specified BuiltInCategory.

**Parameters:**
- `doc`: The active Revit document.
- `category`: The BuiltInCategory to query.

**Returns:** A list of family symbol elements matching the category.

##### `IsLoaded`
```csharp
public static bool IsLoaded (RevitDoc doc, string familyName, string symbolName)
```
Checks if a FamilySymbol with the specified family name and symbol name is loaded in the document.

**Parameters:**
- `doc`: The active Revit document.
- `familyName`: The name of the Family.
- `symbolName`: The name of the FamilySymbol/Type.

**Returns:** True if loaded; otherwise, false.

##### `GetOrigin`
```csharp
public static XYZ GetOrigin(FamilySymbol family)
```
Gets the location point origin of a FamilySymbol.

**Parameters:**
- `family`: The FamilySymbol to query.

**Returns:** The XYZ origin point, or null if not found.

##### `GetInstancesInView`
```csharp
public static FilteredElementCollector GetInstancesInView (Document doc, FamilySymbol family, View view)
```
Gets all instances of a FamilySymbol that exist within a specific View.

**Parameters:**
- `doc`: The active Revit document.
- `family`: The FamilySymbol whose instances to query.
- `view`: The Revit View to search in.

**Returns:** A FilteredElementCollector containing the family instances.

---

#### Fields
- **`familySymbol`**: `FamilySymbol familySymbol = null;`
- **`collector`**: `FilteredElementCollector collector = new FilteredElementCollector(doc);`
- **`collector`**: `FilteredElementCollector collector = new FilteredElementCollector (doc);`
- **`familySymbol`**: `FamilySymbol familySymbol = null;`
- **`collector`**: `FilteredElementCollector collector = new FilteredElementCollector(doc);`
- **`location`**: `LocationPoint location = (LocationPoint)family.Location;`
- **`null`**: `return null;`
- **`collector`**: `FilteredElementCollector collector = new FilteredElementCollector(doc, view.Id);`
- **`filterInstance`**: `ElementFilter filterInstance = new FamilyInstanceFilter(doc, family.Id);`
- **`collector`**: `return collector;`

---

### Class: `FamilyUtil`
**File:** [src/SyntheticShared/Utilities/FamilyUtil.cs](../src/SyntheticShared/Utilities/FamilyUtil.cs)

Utility methods for auditing, loading, and modifying Revit Families.

#### Methods
##### `LoadStandards`
```csharp
public static void LoadStandards(Document doc, IEnumerable<ElementModel> standards)
```
Opens every Annotation family in the project and creates the standard element types.

**Parameters:**
- `doc`: The Revit Document
- `standards`: A ModelsToSerialize object that contains the standard element types.

##### `ForceReinsertAnnotation`
```csharp
public static void ForceReinsertAnnotation(Document doc)
```
Forces all annotation families in the project to be reinserted/reloaded to ensure they are up to date.

**Parameters:**
- `doc`: The Revit Document.

##### `SetIsChanged`
```csharp
public static void SetIsChanged (Document doc, Document familyDoc)
```
Marks a family document as changed/dirty by creating and deleting a temporary text note.

**Parameters:**
- `doc`: The active Revit document.
- `familyDoc`: The family document to mark as changed.

##### `ResolveWarnings`
```csharp
private static void ResolveWarnings(object sender, FailuresProcessingEventArgs e)
```
Catches warners and resolves them so they are not displayed to the user.

**Parameters:**
- `sender`: 
- `e`: 

---

#### Fields
- **`results`**: `List<List<string>> results = new List<List<string>>();`
- **`errors`**: `List<List<string>> errors = new List<List<string>>();`
- **`families`**: `FilteredElementCollector families = new FilteredElementCollector(document);`
- **`opt`**: `IFamilyLoadOptions opt = new ffrFamilyLoadOptions();`
- **`app`**: `Application app = document.Application;`
- **`name`**: `string name = family.Name;`
- **`worksetIds`**: `IList<WorksetId> worksetIds = new List<WorksetId>();`
- **`family`**: `Family family = familyTuple.Item1;`
- **`familyId`**: `ElementId familyId = familyTuple.Item2;`
- **`searchElem`**: `Element searchElem = document.GetElement(familyId);`
- **`familyResult`**: `List<string> familyResult = new List<string>();`
- **`familyDoc`**: `Document familyDoc = document.EditFamily(family);`
- **`warnings`**: `IList<FailureMessage> warnings = familyDoc.GetWarnings();`
- **`warningString`**: `StringBuilder warningString = new StringBuilder();`
- **`purgeResult`**: `bool purgeResult = DocumentUtil.Purge(app, familyDoc);`
- **`schemas`**: `List<Schema> schemas = StorageUtil.GetDocumentSchemas(document);`
- **`filteredSchemas`**: `List<Schema> filteredSchemas = new List<Schema>();`
- **`schemaResults`**: `List<string> schemaResults = StorageUtil.PurgeSchema(filteredSchemas, document);`
- **`categories`**: `Categories categories = doc.Settings.Categories;`
- **`annotationCategories`**: `List<ElementId> annotationCategories = new List<ElementId>();`
- **`opt`**: `IFamilyLoadOptions opt = new ffrFamilyLoadOptions();`
- **`name`**: `string name = family.Name;`
- **`worksetIds`**: `IList<WorksetId> worksetIds = new List<WorksetId>();`
- **`family`**: `Family family = familyTuple.Item1;`
- **`familyId`**: `ElementId familyId = familyTuple.Item2;`
- **`searchElem`**: `Element searchElem = doc.GetElement(familyId);`
- **`familyDoc`**: `Document familyDoc = doc.EditFamily(family);`
- **`elem`**: `ElementType elem = (ElementType)ModelsToSerialize.CreateElementType(serialElement, familyDoc);`
- **`y`**: `int y = 0;`
- **`categories`**: `Categories categories = doc.Settings.Categories;`
- **`annotationCategories`**: `List<ElementId> annotationCategories = new List<ElementId>();`
- **`opt`**: `IFamilyLoadOptions opt = new ffrFamilyLoadOptions();`
- **`name`**: `string name = family.Name;`
- **`worksetIds`**: `IList<WorksetId> worksetIds = new List<WorksetId>();`
- **`family`**: `Family family = familyTuple.Item1;`
- **`familyId`**: `ElementId familyId = familyTuple.Item2;`
- **`searchElem`**: `Element searchElem = doc.GetElement(familyId);`
- **`familyDoc`**: `Document familyDoc = doc.EditFamily(family);`
- **`y`**: `int y = 0;`
- **`viewId`**: `ElementId viewId = null;`
- **`sheetId`**: `ElementId sheetId = null;`
- **`note`**: `TextNote note = null;`
- **`noteId`**: `ElementId noteId = null;`
- **`origin`**: `XYZ origin = XYZ.Zero;`
- **`text`**: `string text = "test";`
- **`fa`**: `FailuresAccessor fa = e.GetFailuresAccessor();`
- **`failList`**: `IList<FailureMessageAccessor> failList = new List<FailureMessageAccessor>();`
- **`failID`**: `FailureDefinitionId failID = failure.GetFailureDefinitionId();`

---

### Class: `FileUtil`
**File:** [src/SyntheticShared/Utilities/FileUtil.cs](../src/SyntheticShared/Utilities/FileUtil.cs)

Utility methods for file operations, alternate path searches, and file organization.

#### Methods
##### `SeparatePath`
```csharp
public static string SeparatePath (string filePath, List<string> rootNames)
```
Given a file path, returns the partial path after one of the root names.  If a root isn't found, it returns null.

**Parameters:**
- `filePath`: A string of the filepath
- `rootNames`: Names of directories to separate

---

#### Fields
- **`uniqueFilePaths`**: `List<string> uniqueFilePaths = filePaths.Distinct().ToList();`
- **`archiveRoots`**: `List<string> archiveRoots = null;`
- **`appConfig`**: `Config appConfig = Config.ReadAppConfig();`
- **`fileUtilSettings`**: `FileUtilitySettings fileUtilSettings = appConfig.GetSettings<FileUtilitySettings>(FileUtilitySettings.Name);`
- **`archiveDirectories`**: `List<string> archiveDirectories = new List<string>();`
- **`fullPath`**: `string fullPath = possiblePath.ToLower();`
- **`separator`**: `string separator = "bim";`
- **`index`**: `int index = fullPath.IndexOf(separator, StringComparison.OrdinalIgnoreCase);`
- **`projectRoot`**: `string projectRoot = fullPath.Substring(0, index);`
- **`projectName`**: `string projectName = fullPath.Substring(index + 1);`
- **`testDirectories`**: `List<string> testDirectories = new List<string>();`
- **`replaceKey`**: `string replaceKey = Path.Combine(projectRoot, projectName);`
- **`pattern`**: `string pattern = alternate.Key;`
- **`testPath`**: `string testPath = fullPath.Replace(pattern.ToLower(), replacement.ToLower());`
- **`ext`**: `string ext = Path.GetExtension(testPath);`
- **`fileName`**: `string fileName = Path.GetFileName(fullPath);`
- **`directory`**: `string directory = Path.GetDirectoryName(fullPath);`
- **`partialPath`**: `string partialPath = String.Empty;`
- **`existPath`**: `string existPath = fullPath;`
- **`newPath`**: `string newPath = Path.Combine(newPathRoot + partialPath, fileName);`
- **`fileName`**: `string fileName = Path.GetFileName(fullPath);`
- **`index`**: `int index = -1;`
- **`partialPath`**: `string partialPath = String.Empty;`
- **`tempIndex`**: `int tempIndex = filePath.LastIndexOf(root, StringComparison.OrdinalIgnoreCase);`
- **`partialPath`**: `return partialPath;`

---

### Class: `Select`
**File:** [src/SyntheticShared/Utilities/Select.cs](../src/SyntheticShared/Utilities/Select.cs)

Nodes that certain sets of elements using pre-configured Collectors and filters.

#### Methods
##### `Select`
```csharp
internal Select() { }
```
##### `ElementByNameClass`
```csharp
public static RevitElem ElementByNameClass(string Name, Type Class, RevitDoc document)
```
Retrieves a Revit element of the specified class/type that matches the given name.

**Parameters:**
- `Name`: The name of the element to retrieve.
- `Class`: The class type of the element (e.g. typeof(Material)).
- `document`: The Revit document to search.

**Returns:** The matching element, or null if not found.

##### `RevitClassByString`
```csharp
public static Type RevitClassByString(string typeName)
```
Get the Type of a Revit Class from RevitAPI.dll given its name.

**Parameters:**
- `typeName`: Name of the Autodesk.Revit.DB Class

**Returns:** The Type of a Revit Class

##### `InstanceClassFromTypeClass`
```csharp
public static Type InstanceClassFromTypeClass(Type elementType)
```
Given the an ElementType Type retrieves the corresponding Instance Type.  For example Type WallType returns Type Wall or Type TextNoteType returns Type TextNote.

**Parameters:**
- `elementType`: A Type of ElementType

**Returns:** The Type of Instance

##### `GetInstancesFromElemType`
```csharp
public static IEnumerable<RevitElem> GetInstancesFromElemType(RevitElem ElemType, RevitDoc document)
```
Retrieves all instances in the document that use the specified element type.

**Parameters:**
- `ElemType`: The element type whose instances are to be retrieved.
- `document`: The Revit document.

**Returns:** A collection of matching instance elements.

##### `GetElementTypeByName`
```csharp
public static RevitElem GetElementTypeByName(string Name, RevitDoc document)
```
Retrieves an ElementType by its name.

**Parameters:**
- `Name`: The name of the ElementType.
- `document`: The Revit document.

**Returns:** The matching ElementType, or null if not found.

---

#### Fields
- **`elem`**: `return elem;`
- **`assembly`**: `Assembly assembly = typeof(RevitElem).Assembly;`
- **`elemClass`**: `Type elemClass = assembly.GetType(typeName);`
- **`elemClass`**: `return elemClass;`
- **`instanceType`**: `Type instanceType = null;`
- **`instanceType`**: `return instanceType;`
- **`collector`**: `RevitFECollector collector = new RevitFECollector(document);`
- **`instanceType`**: `Type instanceType = Select.InstanceClassFromTypeClass(ElemType.GetType());`
- **`instances`**: `return instances;`
- **`collector`**: `RevitFECollector collector = new RevitFECollector(document);`

---

### Class: `StorageUtil`
**File:** [src/SyntheticShared/Utilities/StorageUtil.cs](../src/SyntheticShared/Utilities/StorageUtil.cs)

Utility methods for interacting with Revit Extensible Storage.

#### Methods
##### `DoesAnyStorageExist`
```csharp
public static bool DoesAnyStorageExist(Document doc)
```
Returns true if any extensible storage exists in the document, false otherwise.

##### `PurgeSchema`
```csharp
public static List<string> PurgeSchema(List<Schema> schemas, Document document)
```
Purges the given list of Schema from all open documents.

**Parameters:**
- `schemas`: List of schemas
- `document`: Document to perform the transaction in.

##### `GetDocumentSchemas`
```csharp
public static List<Schema> GetDocumentSchemas(Document doc)
```
Gets the schemas that have elements in the given document.

**Parameters:**
- `doc`: An Autodesk Revit Document obejct

**Returns:** List of schemas in the document.

##### `GetElementStringWithAllSchemas`
```csharp
public static string GetElementStringWithAllSchemas(Document doc)
```
Returns a formatted string containing schema guids and element info for all elements containing extensible storage.

##### `GetElementStringWithSchema`
```csharp
private static string GetElementStringWithSchema(Document doc, Schema schema)
```
Returns a formatted string containing a schema guid and element info for all elements containing extensible storage of a given schema.

##### `ElementsWithStorage`
```csharp
private static List<ElementId> ElementsWithStorage(Document doc, Schema schema)
```
Returns a list of ElementIds that contain extensible storage of a given schema using the ExtensibleStorageFilter ElementQuickFilter.

##### `PrintElementInfo`
```csharp
private static string PrintElementInfo(ElementId id, Document document)
```
Writes basic element info to a string.

---

#### Fields
- **`schemas`**: `IList<Schema> schemas = Schema.ListSchemas();`
- **`false`**: `return false;`
- **`ids`**: `List<ElementId> ids = ElementsWithStorage(doc, schema);`
- **`true`**: `return true;`
- **`false`**: `return false;`
- **`results`**: `List<string> results = new List<string>();`
- **`appVersion`**: `int appVersion = int.Parse(document.Application.VersionNumber);`
- **`transactionName`**: `string transactionName = "Erase Extensible Storage";`
- **`schemas`**: `IList<Schema> schemas = Schema.ListSchemas();`
- **`docSchemas`**: `List<Schema> docSchemas = new List<Schema>();`
- **`null`**: `return null;`
- **`ids`**: `List<ElementId> ids = ElementsWithStorage(doc, schema);`
- **`docSchemas`**: `return docSchemas;`
- **`sBuilder`**: `StringBuilder sBuilder = new StringBuilder();`
- **`schemas`**: `IList<Schema> schemas = Schema.ListSchemas();`
- **`sBuilder`**: `StringBuilder sBuilder = new StringBuilder();`
- **`elementsofSchema`**: `List<ElementId> elementsofSchema = ElementsWithStorage(doc, schema);`
- **`ids`**: `List<ElementId> ids = new List<ElementId>();`
- **`collector`**: `FilteredElementCollector collector = new FilteredElementCollector(doc);`
- **`ids`**: `return ids;`
- **`element`**: `Element element = document.GetElement(id);`
- **`retval`**: `string retval = (element.Id.ToString() + ", " + element.Name + ", " + element.GetType().FullName);`
- **`retval`**: `return retval;`

---

### Class: `ViewAutoNumModel`
**File:** [src/SyntheticShared/Models/ViewAutoNumModel.cs](../src/SyntheticShared/Models/ViewAutoNumModel.cs)

Class used to automatically renumber views based on a grid.  Uses a family on the sheet to determine the origin point and the spacing of the grid.

#### Properties
- **`Document`**: `public Document Document { get; set; }`
  *Description:* A Revit Document
- **`Family`**: `public FamilySymbol Family { get; set; }`
  *Description:* A Revit Family used as the Origin Point
- **`XSpacingParameter`**: `public string XSpacingParameter { get; set; }`
  *Description:* Name of the parameter in the Revit Family that specifies the X grid spacing
- **`YSpacingParameter`**: `public string YSpacingParameter { get; set; }`
  *Description:* Name of the parameter in the Revit Family that specifies the X grid spacing
- **`XSpacing`**: `public double XSpacing { get; set; }`
  *Description:* The X grid spacing used to number views on relative the origin point.
- **`YSpacing`**: `public double YSpacing { get; set; }`
  *Description:* The Y grid spacing used to number views on relative the origin point.

#### Methods
##### `ViewAutoNumModel`
```csharp
public ViewAutoNumModel(Document doc, string familyName, string symbolName, string xSpacingParameter, string ySpacingParameter)
```
Constructor  <param name="doc">A Revit Document</param> <param name="familyName">Name of the Family used as the origin point</param> <param name="symbolName">Name of the Family Type used as the origin</param> <param name="xSpacingParameter">Name of the Parameter in the Family that specifies the X grid spacing</param> <param name="ySpacingParameter">Name of the Parameter in the Family that specifies the Y grid spacing</param>

##### `AutoNumberOnSheet`
```csharp
public List<Viewport> AutoNumberOnSheet (ViewSheet sheet)
```
Renumbers the views on the given sheet based on the views location in a grid

**Parameters:**
- `sheet`: Sheet to renumber views on

**Returns:** Viewports on the sheet.

##### `_tempRenumberViewports`
```csharp
internal static List<Viewport> _tempRenumberViewports(List<ElementId> viewPortIds, Document doc)
```
Given a list of viewport element IDs, the function will get the viewport from the document and give each viewport a temporary sheet number.  Function will ignore legends.

**Parameters:**
- `viewPortIds`: Revit ElementId of the viewports.
- `doc`: The Revit Document the viewports are in.

**Returns:** Returns the Revit viewports.

##### `_renumberViewports`
```csharp
internal static List<Viewport> _renumberViewports(List<Viewport> viewports, double gridX, double gridY, double originX, double originY)
```
Given a list of viewports, grid spacing and an origin point, function will renumber the viewports based on grid location.

**Parameters:**
- `viewports`: Revit ViewPorts
- `gridX`: Grid spacing in the X direction
- `gridY`: Grid spacing in the Y direction
- `originX`: X coordinate of the grid origin
- `originY`: Y coordinate of the grid origin

**Returns:** The renumbered Revit ViewPorts

##### `_calculateViewNumber`
```csharp
internal static string _calculateViewNumber(double viewX, double viewY, double gridX, double gridY, double originX, double originY)
```
##### `if`
```csharp
else if (y == 0)
```
##### `_IntToLetters`
```csharp
internal static string _IntToLetters(int value)
```
Given an Int, returns an equivalent letter from the alphabet.

**Parameters:**
- `value`: An integer

**Returns:** A letter from the alphabet

---

#### Fields
- **`viewports`**: `List<Viewport> viewports = null;`
- **`instance`**: `FamilyInstance instance = (FamilyInstance)FamilySymbolUtil.GetInstancesInView(this.Document, this.Family, sheet).FirstElement();`
- **`location`**: `LocationPoint location = (LocationPoint)instance.Location;`
- **`originPoint`**: `XYZ originPoint = location.Point;`
- **`viewportIds`**: `List<ElementId> viewportIds = (List<ElementId>)sheet.GetAllViewports();`
- **`viewports`**: `return viewports;`
- **`viewPorts`**: `List<Viewport> viewPorts = new List<Viewport>();`
- **`i`**: `int i = 1;`
- **`vp`**: `Viewport vp = (Viewport)doc.GetElement(id);`
- **`v`**: `View v = (View)doc.GetElement(vp.ViewId);`
- **`param`**: `Parameter param = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);`
- **`viewPorts`**: `return viewPorts;`
- **`labelOutline`**: `Outline labelOutline = vp.GetLabelOutline();`
- **`minPt`**: `XYZ minPt = labelOutline.MinimumPoint;`
- **`viewNumber`**: `string viewNumber = _calculateViewNumber(minPt.X, minPt.Y, gridX, gridY, originX, originY);`
- **`param`**: `Parameter param = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);`
- **`viewports`**: `return viewports;`
- **`x`**: `double x = Math.Floor(((viewX - originX) + viewportOffset) / gridX + 1);`
- **`y`**: `double y = Math.Floor(((viewY - originY) + viewportOffset) / gridY + 1);`
- **`stringX`**: `string stringX = x.ToString();`
- **`stringY`**: `string stringY = _IntToLetters((int)Math.Abs(y));`
- **`result`**: `string result = string.Empty;`
- **`result`**: `return result;`

---

### Class: `ViewUtil`
**File:** [src/SyntheticShared/Utilities/ViewUtil.cs](../src/SyntheticShared/Utilities/ViewUtil.cs)

Utility methods for managing and auto-numbering Revit Views.

#### Methods
##### `AutoNumber`
```csharp
public static List<revitViewport> AutoNumber(RevitDoc doc, revitFamilySymbol familyType, string xGridName, string yGridName)
```
Renumbers the views on the Active Sheet

**Parameters:**
- `doc`: The active Revit document.
- `familyType`: Revit Family Symbol that represents the origin element
- `xGridName`: Name of the parameter that represents the X grid spacing
- `yGridName`: Name of the parameter that represents the Y grid spacing

**Returns:** Revit viewport objects on the sheet.

##### `_renumberViewsOnSheet`
```csharp
return _renumberViewsOnSheet(familyType, xGridName, yGridName, rSheet, doc);
```
##### `SetWorksetVisibilityInView`
```csharp
public static bool SetWorksetVisibilityInView(revitView view, revitDB.Workset workset)
```
Sets a workset to visible in a specific view.

**Parameters:**
- `view`: The Revit View
- `workset`: The Workset to show

**Returns:** True if successful, False if skipped due to template

##### `_renumberViewsOnSheet`
```csharp
internal static List<revitViewport> _renumberViewsOnSheet(revitFamilySymbol familyType, string xGridName, string yGridName, revitSheet rSheet, RevitDoc document)
```
##### `_tempRenumberViewports`
```csharp
internal static List<revitViewport> _tempRenumberViewports(List<revitElemId> viewPortIds, RevitDoc doc)
```
Given a list of viewport element IDs, the function will get the viewport from the document and give each viewport a temporary sheet number.  Function will ignore legends.

**Parameters:**
- `viewPortIds`: Revit ElementId of the viewports.
- `doc`: The Revit Document the viewports are in.

**Returns:** Returns the Revit viewports.

##### `_renumberViewports`
```csharp
internal static List<revitViewport> _renumberViewports(List<revitViewport> viewports, double gridX, double gridY, double originX, double originY)
```
Given a list of viewports, grid spacing and an origin point, function will renumber the viewports based on grid location.

**Parameters:**
- `viewports`: Revit ViewPorts
- `gridX`: Grid spacing in the X direction
- `gridY`: Grid spacing in the Y direction
- `originX`: X coordinate of the grid origin
- `originY`: Y coordinate of the grid origin

**Returns:** The renumbered Revit ViewPorts

##### `_calculateViewNumber`
```csharp
internal static string _calculateViewNumber(double viewX, double viewY, double gridX, double gridY, double originX, double originY)
```
##### `if`
```csharp
else if (y == 0)
```
##### `_IntToLetters`
```csharp
internal static string _IntToLetters(int value)
```
---

#### Fields
- **`rSheet`**: `revitSheet rSheet = (revitSheet)doc.ActiveView;`
- **`false`**: `return false;`
- **`true`**: `return true;`
- **`transactionName`**: `string transactionName = "Renumber views on sheet";`
- **`rFamilySymbol`**: `revitFamilySymbol rFamilySymbol = (revitFamilySymbol)familyType;`
- **`viewportIds`**: `List<revitElemId> viewportIds = (List<revitElemId>)rSheet.GetAllViewports();`
- **`viewports`**: `List<revitViewport> viewports = null;`
- **`symbolId`**: `revitElemId symbolId = familyType.Id;`
- **`collector`**: `revitCollector collector = new revitCollector(document, rSheet.Id);`
- **`filterInstance`**: `revitElementFilter filterInstance = new revitDB.FamilyInstanceFilter(document, symbolId);`
- **`originPoint`**: `revitXYZ originPoint = location.Point;`
- **`gridX`**: `double gridX = rFamilySymbol.LookupParameter(xGridName).AsDouble();`
- **`gridY`**: `double gridY = rFamilySymbol.LookupParameter(yGridName).AsDouble();`
- **`viewports`**: `return viewports;`
- **`viewPorts`**: `List<revitViewport> viewPorts = new List<revitViewport>();`
- **`i`**: `int i = 1;`
- **`vp`**: `revitViewport vp = (revitViewport)doc.GetElement(id);`
- **`v`**: `revitView v = (revitView)doc.GetElement(vp.ViewId);`
- **`viewPorts`**: `return viewPorts;`
- **`labelOutline`**: `revitOutline labelOutline = vp.GetLabelOutline();`
- **`minPt`**: `revitXYZ minPt = labelOutline.MinimumPoint;`
- **`viewNumber`**: `string viewNumber = _calculateViewNumber(minPt.X, minPt.Y, gridX, gridY, originX, originY);`
- **`viewports`**: `return viewports;`
- **`x`**: `double x = Math.Floor(((viewX - originX) + viewportOffset) / gridX + 1);`
- **`y`**: `double y = Math.Floor(((viewY - originY) + viewportOffset) / gridY + 1);`
- **`stringX`**: `string stringX = x.ToString();`
- **`stringY`**: `string stringY = _IntToLetters((int)Math.Abs(y));`
- **`result`**: `string result = string.Empty;`
- **`result`**: `return result;`

---

### Class: `WorksetUtil`
**File:** [src/SyntheticShared/Utilities/WorksetUtil.cs](../src/SyntheticShared/Utilities/WorksetUtil.cs)

Utilities for dealing with Worksets, WorksetModels and WorksetSettings.

#### Methods
##### `WorksetUtil`
```csharp
public WorksetUtil()
```
Constructor that creates an empty WorksetUtil object

##### `WorksetUtil`
```csharp
public WorksetUtil(List<List<object>> cells)
```
Creates a list of WorksetModels from Excel cells.  <param name="cells">List of List of Excel cells.</param>

##### `Add`
```csharp
public WorksetUtil Add (WorksetModel workset)
```
Adds a Workset to the WorksetUtil object.

**Parameters:**
- `workset`: 

##### `Names`
```csharp
public List<string> Names()
```
Gets a list of Workset names

**Returns:** List of Workset names as strings

##### `WorksetNames`
```csharp
public string WorksetNames()
```
Return a string Workset names with line breaks between

**Returns:** Return a string Workset names with line breaks between

##### `WorksetVisibilities`
```csharp
public string WorksetVisibilities()
```
Returns a string of Workset visibilities with line breaks between

**Returns:** Returns a string of Workset visibilities with line breaks between

##### `WorksetDescriptions`
```csharp
public string WorksetDescriptions()
```
Returns a string of Workset descriptions with line breaks between

**Returns:** Returns a string of Workset descriptions with line breaks between

##### `Filter`
```csharp
public WorksetUtil Filter(List<string> filters)
```
Given a list of WorksetModel names, updates the WorksetUtil object to only include those WorksetModels

**Parameters:**
- `filters`: List of WorksetModel names as strings

**Returns:** The modified WorksetUtil object.

##### `SetProjectInfo`
```csharp
public ProjectInfo SetProjectInfo (RevitDoc doc)
```
Sets the "Workset Names", "Workset Visibility", and "Workset Descriptions" parameters on the ProjectInfo element.

**Parameters:**
- `doc`: Revit Document

**Returns:** Returns the ProjectInfo element

##### `_create`
```csharp
return _create(doc, worksets);
```
##### `_create`
```csharp
return _create(doc, worksets);
```
##### `GetByName`
```csharp
public static revitWorkset GetByName (string name, RevitDoc doc)
```
Retrieves the workset with the given name.

**Parameters:**
- `name`: A workset name
- `doc`: The Revit document

**Returns:** Returns a workset.  Returns null if workset does not exist.

##### `GetByWorksetId`
```csharp
public static revitWorkset GetByWorksetId (WorksetId worksetId, RevitDoc doc)
```
Retrieves the workset with the given WorksetId.

**Parameters:**
- `worksetId`: The workset ID
- `doc`: A Revit document

**Returns:** Returns a workset.  Returns null if workset does not exist.

##### `GetByWorksetUniqueId`
```csharp
public static revitWorkset GetByWorksetUniqueId(System.Guid worksetUniqueId, RevitDoc doc)
```
Retrieves the workset with the given Workset's UniqueId.

**Parameters:**
- `worksetUniqueId`: The GUID of the workset
- `doc`: A Revit document

**Returns:** Returns a workset.  Returns null if workset does not exist.

##### `GetElementsOnWorkset`
```csharp
public static IList<Element> GetElementsOnWorkset(Workset workset, RevitDoc document)
```
Retrieves all elements belonging to a specific workset in the document.

**Parameters:**
- `workset`: The workset to query.
- `document`: The Revit document.

**Returns:** A list of elements on the specified workset.

##### `GetUserWorksets`
```csharp
public static List<revitWorkset> GetUserWorksets (RevitDoc doc)
```
Retrieves all the user worksets from a document.  Excludes view and family worksets.

**Returns:** Returns all user worksets in the document.

##### `Rename`
```csharp
public static revitWorkset Rename (revitWorkset workset, string name, RevitDoc doc)
```
Renames a workset.

**Parameters:**
- `workset`: A workset
- `name`: A workset name
- `doc`: The Revit document

**Returns:** renamed workeset.

##### `SetDefaultVisibility`
```csharp
public static revitWorkset SetDefaultVisibility (revitWorkset workset, bool visible, RevitDoc doc)
```
Sets the Default Visibility of a workset within a document.

**Parameters:**
- `workset`: The workset that you wish to set the visibility of.
- `visible`: The visibility of the workset
- `doc`: The Revit document

**Returns:** A Revit workset

---

#### Fields
- **`rowData`**: `List<string> rowData = new List<string>();`
- **`name`**: `string name = null;`
- **`vis`**: `string vis = "TRUE";`
- **`alias`**: `string alias = null;`
- **`description`**: `string description = string.Empty;`
- **`itemCount`**: `int itemCount = rowData.Count;`
- **`visibility`**: `bool visibility = true;`
- **`this`**: `return this;`
- **`visibilities`**: `string visibilities = String.Empty;`
- **`visibilities`**: `return visibilities;`
- **`descriptions`**: `string descriptions = String.Empty;`
- **`descriptions`**: `return descriptions;`
- **`this`**: `return this;`
- **`collector`**: `FilteredElementCollector collector = new FilteredElementCollector(doc);`
- **`projectInfo`**: `ProjectInfo projectInfo = (ProjectInfo)collector.OfClass(typeof(ProjectInfo)).FirstElement();`
- **`paramNames`**: `Parameter paramNames = projectInfo.LookupParameter("Workset Names");`
- **`paramVisibility`**: `Parameter paramVisibility = projectInfo.LookupParameter("Workset Visibility");`
- **`paramDescriptions`**: `Parameter paramDescriptions = projectInfo.LookupParameter("Workset Descriptions");`
- **`projectInfo`**: `return projectInfo;`
- **`workset`**: `revitWorkset workset = null;`
- **`worksets`**: `WorksetUtil worksets = new WorksetUtil();`
- **`workset`**: `revitWorkset workset = WorksetUtil.GetByName(wkset.Alias, doc);`
- **`workset`**: `revitWorkset workset = revitWorkset.Create(doc, wkset.Name);`
- **`defaultVisibility`**: `WorksetDefaultVisibilitySettings defaultVisibility = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);`
- **`workset`**: `revitWorkset workset = GetByName(wkset.Name, doc);`
- **`importedWorksets`**: `return importedWorksets;`
- **`null`**: `return null;`
- **`foundWorkset`**: `revitWorkset foundWorkset = null;`
- **`fwCollector`**: `FilteredWorksetCollector fwCollector = new FilteredWorksetCollector(doc);`
- **`foundWorkset`**: `return foundWorkset;`
- **`elementCollector`**: `FilteredElementCollector elementCollector = new FilteredElementCollector(document);`
- **`elementWorksetFilter`**: `ElementWorksetFilter elementWorksetFilter = new ElementWorksetFilter(workset.Id, false);`
- **`elements`**: `IList<Element> elements = elementCollector.WherePasses(elementWorksetFilter).ToElements();`
- **`elements`**: `return elements;`
- **`fwCollector`**: `FilteredWorksetCollector fwCollector = new FilteredWorksetCollector(doc);`
- **`worksets`**: `List<revitWorkset> worksets = new List<revitWorkset>();`
- **`worksets`**: `return worksets;`
- **`renamedWorkset`**: `revitWorkset renamedWorkset = null;`
- **`renamed`**: `bool renamed = false;`
- **`renamedWorkset`**: `return renamedWorkset;`
- **`defaultVisibility`**: `WorksetDefaultVisibilitySettings defaultVisibility = WorksetDefaultVisibilitySettings.GetWorksetDefaultVisibilitySettings(doc);`
- **`workset`**: `return workset;`

---

### Class: `ffrFamilyLoadOptions`
**File:** [src/SyntheticShared/Utilities/FamilyUtil.cs](../src/SyntheticShared/Utilities/FamilyUtil.cs)

#### Fields
- **`true`**: `return true;`
- **`true`**: `return true;`

---

## Namespace: `Synthetic.Commands`

### Class: `AuditPurgeAllFamilies`
**File:** [src/SyntheticShared/Commands/AuditPurgeAllFamilies.cs](../src/SyntheticShared/Commands/AuditPurgeAllFamilies.cs)

Revit external command to audit and purge all families loaded in the current project. Provides options to audit only, purge unused elements, and delete schema options.

#### Methods
##### `PurgeOptions`
```csharp
internal TaskDialogResult PurgeOptions()
```
---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`app`**: `Application app = uiapp.Application;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`commandResult`**: `Result commandResult = Result.Failed;`
- **`dialogResult`**: `TaskDialogResult dialogResult = PurgeOptions();`
- **`schemaExceptions`**: `List<string> schemaExceptions = new List<string>() { "Enscape", "Kinship", "CTC", "Synthetic"};`
- **`commandResult`**: `return commandResult;`
- **`result`**: `TaskDialogResult result = taskDialog.Show();`
- **`result`**: `return result;`

---

### Class: `CmdBatchTag`
**File:** [src/SyntheticShared/Commands/CmdBatchTag.cs](../src/SyntheticShared/Commands/CmdBatchTag.cs)

Revit external command to batch tag elements in the active view or a selection. Matches elements to saved tag templates and places tags accordingly.

#### Methods
##### `Execute`
```csharp
public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
```
Executes the batch tagging command.

**Parameters:**
- `commandData`: Revit external command data.
- `message`: A message returning errors if any.
- `elements`: Revit elements set.

**Returns:** Result code of the execution.

---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`repo`**: `var repo = new TemplateStorageRepository();`
- **`templates`**: `List<TagTemplate> templates = repo.GetTemplates(doc);`
- **`taggedElementIds`**: `HashSet<ElementId> taggedElementIds = new HashSet<ElementId>();`
- **`targetElements`**: `IEnumerable<FamilyInstance> targetElements;`
- **`selectedIds`**: `var selectedIds = uidoc.Selection.GetElementIds();`
- **`conflictsMap`**: `var conflictsMap = new Dictionary<string, (string DisplayName, List<TagTemplate> Candidates)>();`
- **`nonConflictingMap`**: `var nonConflictingMap = new Dictionary<string, TagTemplate>();`
- **`elementScopeMap`**: `var elementScopeMap = new Dictionary<FamilyInstance, string>();`
- **`catName`**: `string catName = host.Category?.Name;`
- **`famName`**: `string famName = host.Symbol?.FamilyName;`
- **`typeName`**: `string typeName = host.Name;`
- **`matches`**: `List<TagTemplate> matches = new List<TagTemplate>();`
- **`scopeKey`**: `string scopeKey = null;`
- **`scopeDisplayName`**: `string scopeDisplayName = null;`
- **`typeTemplates`**: `var typeTemplates = templates.Where(t => t.TargetCategory == catName && t.TargetFamily == famName && t.TargetType == typeName).ToList();`
- **`familyTemplates`**: `var familyTemplates = templates.Where(t => t.TargetCategory == catName && t.TargetFamily == famName && string.IsNullOrEmpty(t.TargetType)).ToList();`
- **`categoryTemplates`**: `var categoryTemplates = templates.Where(t => t.TargetCategory == catName && string.IsNullOrEmpty(t.TargetFamily) && string.IsNullOrEmpty(t.TargetType)).ToList();`
- **`resolvedScopeTemplates`**: `var resolvedScopeTemplates = new Dictionary<string, TagTemplate>();`
- **`vm`**: `var vm = new ResolveConflictsViewModel(conflictItems);`
- **`view`**: `var view = new ResolveConflictsView(uiapp.MainWindowHandle) { DataContext = vm };`
- **`successCount`**: `int successCount = 0;`
- **`skipCount`**: `int skipCount = 0;`
- **`errorCount`**: `int errorCount = 0;`
- **`localOffset`**: `XYZ localOffset = new XYZ(match.OffsetX, match.OffsetY, match.OffsetZ);`
- **`worldPoint`**: `XYZ worldPoint = CoordinateUtility.GetWorldPoint(host, localOffset);`
- **`orientation`**: `TagOrientation orientation = CoordinateUtility.CalculateTagOrientation(host, match);`

---

### Class: `CmdDetailItemFactory`
**File:** [src/SyntheticShared/Commands/CmdDetailItemFactory.cs](../src/SyntheticShared/Commands/CmdDetailItemFactory.cs)

#### Methods
##### `Execute`
```csharp
public Result Execute( ExternalCommandData commandData, ref string message, ElementSet elements)
```
Executes the Detail Item Factory command, showing the WPF UI options and running the batch conversion.

#### Fields
- **`IExternalCommand`**: `public class CmdDetailItemFactory : IExternalCommand {`
  *Description:* Production external command to batch-process selected 3D model elements into 2D Detail Item family documents via temporary DWG projection and tracing.

---
### Class: `CmdManageTemplates`
**File:** [src/SyntheticShared/Commands/CmdManageTemplates.cs](../src/SyntheticShared/Commands/CmdManageTemplates.cs)

Revit external command to manage tag templates. Provides a user interface to view, create, edit, or delete tag templates.

#### Methods
##### `Execute`
```csharp
public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
```
Executes the template manager command.

**Parameters:**
- `commandData`: Revit external command data.
- `message`: A message returning errors if any.
- `elements`: Revit elements set.

**Returns:** Result code of the execution.

##### `if`
```csharp
else if (vm.RequestedAction == ManageTemplatesAction.EditTemplate && vm.TemplateToEdit != null)
```
---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`keepOpen`**: `bool keepOpen = true;`
- **`vm`**: `var vm = new ManageTemplatesViewModel(doc);`
- **`view`**: `var view = new ManageTemplatesView(uiapp.MainWindowHandle) { DataContext = vm };`
- **`host`**: `FamilyInstance host = doc.GetElement(hostRef) as FamilyInstance;`
- **`tag`**: `IndependentTag tag = doc.GetElement(tagRef) as IndependentTag;`
- **`localOffset`**: `XYZ localOffset = CoordinateUtility.GetLocalOffset(host, tag.TagHeadPosition);`
- **`setVm`**: `SetTemplateViewModel setVm = new SetTemplateViewModel(doc, host, localOffset, tag.TagOrientation);`
- **`setView`**: `SetTemplateView setView = new SetTemplateView(uiapp.MainWindowHandle) { DataContext = setVm };`
- **`host`**: `FamilyInstance host = doc.GetElement(hostRef) as FamilyInstance;`
- **`tag`**: `IndependentTag tag = doc.GetElement(tagRef) as IndependentTag;`
- **`localOffset`**: `XYZ localOffset = CoordinateUtility.GetLocalOffset(host, tag.TagHeadPosition);`
- **`setVm`**: `SetTemplateViewModel setVm = new SetTemplateViewModel(doc, host, localOffset, tag.TagOrientation, vm.TemplateToEdit);`
- **`setView`**: `SetTemplateView setView = new SetTemplateView(uiapp.MainWindowHandle) { DataContext = setVm };`

---

### Class: `CmdMergeDuplicates`
**File:** [src/SyntheticShared/Commands/CmdMergeDuplicates.cs](../src/SyntheticShared/Commands/CmdMergeDuplicates.cs)

Revit command to scan model for duplicate families and display the Merge Duplicates Modeless UI.

#### Methods
##### `Execute`
```csharp
public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
```
Executes the merge duplicates command.

---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`doc`**: `Document doc = uiapp.ActiveUIDocument?.Document;`
- **`clusters`**: `var clusters = MergeAnalysisEngine.RunFastScan(doc, CancellationToken.None);`

---

### Class: `CmdSetTemplate`
**File:** [src/SyntheticShared/Commands/CmdSetTemplate.cs](../src/SyntheticShared/Commands/CmdSetTemplate.cs)

Revit external command to set a tag template. Prompts the user to select a host element and its associated tag, then registers the configuration.

#### Methods
##### `Execute`
```csharp
public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
```
Executes the set template command.

**Parameters:**
- `commandData`: Revit external command data.
- `message`: A message returning errors if any.
- `elements`: Revit elements set.

**Returns:** Result code of the execution.

---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`hostRef`**: `Reference hostRef = uidoc.Selection.PickObject(ObjectType.Element, new FamilyInstanceFilter(), "Select a Host Element (FamilyInstance).");`
- **`host`**: `FamilyInstance host = doc.GetElement(hostRef) as FamilyInstance;`
- **`tagRef`**: `Reference tagRef = uidoc.Selection.PickObject(ObjectType.Element, new IndependentTagFilter(), "Select the associated Independent Tag.");`
- **`tag`**: `IndependentTag tag = doc.GetElement(tagRef) as IndependentTag;`
- **`localOffset`**: `XYZ localOffset = CoordinateUtility.GetLocalOffset(host, tag.TagHeadPosition);`
- **`vm`**: `SetTemplateViewModel vm = new SetTemplateViewModel(doc, host, localOffset, tag.TagOrientation);`
- **`view`**: `SetTemplateView view = new SetTemplateView(uiapp.MainWindowHandle) { DataContext = vm };`

---

### Class: `ConvertDraftingToLegend`
**File:** [src/SyntheticShared/Commands/ConvertDraftingToLegend.cs](../src/SyntheticShared/Commands/ConvertDraftingToLegend.cs)

Converts Drafting Views to Legends

#### Methods
##### `SelectViews`
```csharp
internal IList<View> SelectViews(IList<View> views, IntPtr mainWindowHandle)
```
Displays a checkbox selection dialog allowing the user to choose views for batch conversion.

**Parameters:**
- `views`: The list of available views for selection.
- `mainWindowHandle`: The parent main window handle.

**Returns:** A list of user-selected views.

---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`transactionGroupName`**: `string transactionGroupName = "Convert Drafting Views to Legends";`
- **`views`**: `IList<View> views = SelectViews(draftingViews, uiapp.MainWindowHandle);`
- **`legends`**: `IList<View> legends = new List<View>();`
- **`warningTripped`**: `bool warningTripped = false;`
- **`isCanceled`**: `bool isCanceled = false;`
- **`progressWindow`**: `ProgressWindow progressWindow = new ProgressWindow(uiapp.MainWindowHandle);`
- **`legend`**: `View legend = LegendsUtil.ConvertFromDrafting(view, progressWindow, ref warningTripped);`
- **`successMsg`**: `string successMsg = $"Successfully converted {legends.Count} Drafting Views to Legends.";`
- **`itemList`**: `List<string> itemList = new List<string>();`
- **`selectedViews`**: `List<View> selectedViews = new List<View>();`
- **`viewModel`**: `ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();`
- **`viewWindow`**: `ListByCheckboxView viewWindow = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };`
- **`selectedList`**: `List<string> selectedList = viewModel.CheckedItems;`
- **`v`**: `View v = views.First(s => s.Name == selectedItem);`
- **`selectedViews`**: `return selectedViews;`

---

### Class: `ConvertLegendToDrafting`
**File:** [src/SyntheticShared/Commands/ConvertLegendToDrafting.cs](../src/SyntheticShared/Commands/ConvertLegendToDrafting.cs)

Converts Legend Views to Drafting Views

#### Methods
##### `SelectViews`
```csharp
internal IList<View> SelectViews(IList<View> views, IntPtr mainWindowHandle)
```
---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`transactionName`**: `string transactionName = "Convert Legend Views to Drafting";`
- **`views`**: `IList<View> views = SelectViews(legendViews, uiapp.MainWindowHandle);`
- **`drafting`**: `IList<View> drafting = new List<View>();`
- **`itemList`**: `List<string> itemList = new List<string>();`
- **`selectedViews`**: `List<View> selectedViews = new List<View>();`
- **`viewModel`**: `ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();`
- **`viewWindow`**: `ListByCheckboxView viewWindow = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };`
- **`selectedList`**: `List<string> selectedList = viewModel.CheckedItems;`
- **`v`**: `View v = views.First(s => s.Name == selectedItem);`
- **`selectedViews`**: `return selectedViews;`

---

### Class: `ElementTypeExportJson`
**File:** [src/SyntheticShared/Commands/ElementTypeExportJson.cs](../src/SyntheticShared/Commands/ElementTypeExportJson.cs)

Revit external command to export element types/styles to a JSON configuration file. Opens the Export Styles dialog interface.

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`vm`**: `ExportStylesViewModel vm = new ExportStylesViewModel(uiapp);`
- **`view`**: `ExportStylesView view = new ExportStylesView(uiapp.MainWindowHandle) { DataContext = vm };`

---

### Class: `ElementTypeImportJson`
**File:** [src/SyntheticShared/Commands/ElementTypeImportJson.cs](../src/SyntheticShared/Commands/ElementTypeImportJson.cs)

Revit external command to import element types/styles from a JSON configuration file. Opens the Import Styles dialog interface.

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`vm`**: `ImportStylesViewModel vm = new ImportStylesViewModel(uiapp);`
- **`view`**: `ImportStylesView view = new ImportStylesView(uiapp.MainWindowHandle) { DataContext = vm };`

---

### Class: `ElementsOnWorksetRecord`
**File:** [src/SyntheticShared/Commands/ElementsOnWorksetRecord.cs](../src/SyntheticShared/Commands/ElementsOnWorksetRecord.cs)

Revit external command to record the elements associated with a user-selected workset to a JSON file.

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`app`**: `Application app = uiapp.Application;`
- **`document`**: `Document document = uidoc.Document;`
- **`commandResult`**: `Result commandResult = Result.Succeeded;`
- **`worksets`**: `FilteredWorksetCollector worksets = new FilteredWorksetCollector(document).OfKind(WorksetKind.UserWorkset);`
- **`worksetNames`**: `List<string> worksetNames = new List<string>();`
- **`r`**: `var r = SelectWorkset(worksetNames, uiapp.MainWindowHandle);`
- **`dResult`**: `bool dResult = r.result;`
- **`selection`**: `string selection = r.selection;`
- **`docPath`**: `string docPath = DocumentUtil.GetFilePath(document);`
- **`docName`**: `string docName = Path.GetFileNameWithoutExtension(docPath);`
- **`initialFileName`**: `string initialFileName = docName + " - " + selection + ".json";`
- **`fullPath`**: `string fullPath = CommandUtil.SaveAsJSON(initialFileName);`
- **`workset`**: `Workset workset = worksets.Where(w => w.Name == selection).FirstOrDefault();`
- **`elementsOnWorkset`**: `ElementsOnWorkset elementsOnWorkset = new ElementsOnWorkset(workset, document);`
- **`json`**: `string json = elementsOnWorkset.ToJSON();`
- **`logFileName`**: `string logFileName = Path.GetFileNameWithoutExtension(fullPath);`
- **`logExtension`**: `string logExtension = Path.GetExtension(fullPath);`
- **`logPath`**: `string logPath = Path.GetDirectoryName(fullPath);`
- **`logFullPath`**: `string logFullPath = Path.Combine(logPath, logFileName + " - " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " Log Record" + logExtension);`
- **`commandResult`**: `return commandResult;`
- **`viewModel`**: `DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();`
- **`dialog`**: `DropdownSelectionView dialog = new DropdownSelectionView(mainWindowHandle) { DataContext = viewModel };`
- **`selection`**: `string selection = viewModel.SelectedItem;`

---

### Class: `ElementsOnWorksetReload`
**File:** [src/SyntheticShared/Commands/ElementsOnWorksetReload.cs](../src/SyntheticShared/Commands/ElementsOnWorksetReload.cs)

Revit external command to reload recorded elements from a JSON file and move them back to their recorded workset.

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`app`**: `Application app = uiapp.Application;`
- **`document`**: `Document document = uidoc.Document;`
- **`commandResult`**: `Result commandResult = Result.Succeeded;`
- **`fullPath`**: `string fullPath = CommandUtil.OpenJSON();`
- **`json`**: `string json = File.ReadAllText(fullPath);`
- **`elementsOnWorkset`**: `ElementsOnWorkset elementsOnWorkset = JsonConvert.DeserializeObject<ElementsOnWorkset>(json);`
- **`logFileName`**: `string logFileName = Path.GetFileNameWithoutExtension(fullPath);`
- **`logExtension`**: `string logExtension = Path.GetExtension(fullPath);`
- **`logPath`**: `string logPath = Path.GetDirectoryName(fullPath);`
- **`logFullPath`**: `string logFullPath = Path.Combine(logPath, logFileName + " - " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " Log Reload" + logExtension);`
- **`commandResult`**: `return commandResult;`

---

### Class: `FamiliesForceReinsert`
**File:** [src/SyntheticShared/Commands/FamiliesForceReinsert.cs](../src/SyntheticShared/Commands/FamiliesForceReinsert.cs)

Sets the path and sheet to an Excel file with Worksets.  Stores the setting in an Extensible Storage.

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`app`**: `Application app = uiapp.Application;`
- **`doc`**: `Document doc = uidoc.Document;`

---

### Class: `FamilyInstanceFilter`
**File:** [src/SyntheticShared/Commands/CmdSetTemplate.cs](../src/SyntheticShared/Commands/CmdSetTemplate.cs)

#### Methods
##### `AllowElement`
```csharp
public bool AllowElement(Element elem) => elem is FamilyInstance;
```
##### `AllowReference`
```csharp
public bool AllowReference(Reference reference, XYZ position) => false;
```
---

### Class: `IndependentTagFilter`
**File:** [src/SyntheticShared/Commands/CmdSetTemplate.cs](../src/SyntheticShared/Commands/CmdSetTemplate.cs)

#### Methods
##### `AllowElement`
```csharp
public bool AllowElement(Element elem) => elem is IndependentTag;
```
##### `AllowReference`
```csharp
public bool AllowReference(Reference reference, XYZ position) => false;
```
---

### Class: `MaterialImagesPackage`
**File:** [src/SyntheticShared/Commands/MaterialImagesPackage.cs](../src/SyntheticShared/Commands/MaterialImagesPackage.cs)

Revit external command to package and transmit all material bitmap images to a new directory.

#### Methods
##### `SelectPath`
```csharp
internal string SelectPath(IntPtr ownerHandle)
```
Allows the user to browse for a folder location

##### `SelectSearchPaths`
```csharp
internal List<string> SelectSearchPaths(List<string> defaultPaths, IntPtr mainWindowHandle)
```
Allows the user to add paths and select from a list of default paths for constructing a SearchPath

##### `SelectMaterials`
```csharp
internal List<Material> SelectMaterials(List<Material> materials, IntPtr mainWindowHandle)
```
Selects materials via checklist dialog

---

#### Fields
- **`CommandPath`**: `public static string CommandPath = typeof(MaterialImagesPackage).FullName;`
  *Description:* The full class path/name of this command.
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`app`**: `Application app = uiapp.Application;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`commandResult`**: `Result commandResult = Result.Succeeded;`
- **`libSettings`**: `MaterialLibrarySettings libSettings = SettingsManager.Get<MaterialLibrarySettings>(doc);`
- **`projSettings`**: `ProjectMaterialSettings projSettings = SettingsManager.Get<ProjectMaterialSettings>(doc);`
- **`LibraryPath`**: `string LibraryPath = libSettings.LibraryFolderPath;`
- **`ProjectPath`**: `string ProjectPath = projSettings.GetResolvedPath(doc);`
- **`defaultPaths`**: `List<string> defaultPaths = new List<string>() { ProjectPath, LibraryPath };`
- **`path`**: `string path = SelectPath(uiapp.MainWindowHandle);`
- **`allMaterials`**: `List<Material> allMaterials = MaterialUtil.GetAllMaterials(doc).Cast<Material>().ToList();`
- **`selectedMaterials`**: `List<Material> selectedMaterials = SelectMaterials(allMaterials, uiapp.MainWindowHandle);`
- **`pathList`**: `List<string> pathList = new List<string>();`
- **`filePaths`**: `List<string> filePaths = new List<string>();`
- **`tempPaths`**: `List<string> tempPaths = MaterialUtil.GetMaterialBitmapPaths(material);`
- **`rootNames`**: `List<string> rootNames = new List<string>() { "Materials","INC Material Maps", "Maps", "_Maps", "Substance" };`
- **`commandResult`**: `return commandResult;`
- **`viewModel`**: `SelectSearchPathsViewModel viewModel = new SelectSearchPathsViewModel();`
- **`dialog`**: `SelectSearchPathsView dialog = new SelectSearchPathsView(mainWindowHandle) { DataContext = viewModel };`
- **`pathList`**: `List<string> pathList = new List<string>();`
- **`pathList`**: `return pathList;`
- **`itemList`**: `List<string> itemList = new List<string>();`
- **`selectedMaterials`**: `List<Material> selectedMaterials = new List<Material>();`
- **`viewModel`**: `ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();`
- **`dialog`**: `ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };`
- **`selectedList`**: `List<string> selectedList = viewModel.CheckedItems;`
- **`m`**: `Material m = materials.First(s => s.Name == selectedItem);`
- **`selectedMaterials`**: `return selectedMaterials;`

---

### Class: `MaterialsRepathAll`
**File:** [src/SyntheticShared/Commands/MaterialsRepathAll.cs](../src/SyntheticShared/Commands/MaterialsRepathAll.cs)

Revit external command to repath material bitmap images based on search paths.

#### Methods
##### `SelectSearchPaths`
```csharp
internal List<string> SelectSearchPaths(List<string> defaultPaths, IntPtr mainWindowHandle)
```
Allows the user to add paths and select from a list of default paths for constructing a SearchPath

##### `SelectMaterials`
```csharp
internal List<Material> SelectMaterials(List<Material> materials, IntPtr mainWindowHandle)
```
Selects materials via checklist dialog

---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`app`**: `Application app = uiapp.Application;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`commandResult`**: `Result commandResult = Result.Succeeded;`
- **`libSettings`**: `MaterialLibrarySettings libSettings = SettingsManager.Get<MaterialLibrarySettings>(doc);`
- **`projSettings`**: `ProjectMaterialSettings projSettings = SettingsManager.Get<ProjectMaterialSettings>(doc);`
- **`LibraryPath`**: `string LibraryPath = libSettings.LibraryFolderPath;`
- **`ProjectPath`**: `string ProjectPath = projSettings.GetResolvedPath(doc);`
- **`defaultPaths`**: `List<string> defaultPaths = new List<string>() { ProjectPath, LibraryPath };`
- **`pathList`**: `List<string> pathList = SelectSearchPaths(defaultPaths, uiapp.MainWindowHandle);`
- **`allMaterials`**: `List<Material> allMaterials = MaterialUtil.GetAllMaterials(doc).Cast<Material>().ToList();`
- **`selectedMaterials`**: `List<Material> selectedMaterials = SelectMaterials(allMaterials, uiapp.MainWindowHandle);`
- **`searchPaths`**: `SearchPaths searchPaths = new SearchPaths(pathList);`
- **`transactionName`**: `string transactionName = "Replace Bitmap Paths on Materials";`
- **`commandResult`**: `return commandResult;`
- **`viewModel`**: `SelectSearchPathsViewModel viewModel = new SelectSearchPathsViewModel();`
- **`dialog`**: `SelectSearchPathsView dialog = new SelectSearchPathsView(mainWindowHandle) { DataContext = viewModel };`
- **`pathList`**: `List<string> pathList = new List<string>();`
- **`pathList`**: `return pathList;`
- **`itemList`**: `List<string> itemList = new List<string>();`
- **`selectedMaterials`**: `List<Material> selectedMaterials = new List<Material>();`
- **`viewModel`**: `ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();`
- **`dialog`**: `ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };`
- **`selectedList`**: `List<string> selectedList = viewModel.CheckedItems;`
- **`m`**: `Material m = materials.First(s => s.Name == selectedItem);`
- **`selectedMaterials`**: `return selectedMaterials;`

---

### Class: `OpenFromCloudCallback`
**File:** [src/SyntheticShared/Commands/PrintBatchMultiDoc.cs](../src/SyntheticShared/Commands/PrintBatchMultiDoc.cs)

#### Methods
##### `OnOpenConflict`
```csharp
public OpenConflictResult OnOpenConflict(OpenConflictScenario scenario)
```
---

### Class: `PaintElements`
**File:** [src/SyntheticShared/Commands/PaintElements.cs](../src/SyntheticShared/Commands/PaintElements.cs)

Paints all faces of selected elements with a material

#### Methods
##### `SelectMaterial`
```csharp
internal string SelectMaterial(List<string> materialNames, IntPtr mainWindowHandle)
```
---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`document`**: `Document document = uidoc.Document;`
- **`elemIds`**: `ICollection<ElementId> elemIds = uidoc.Selection.GetElementIds();`
- **`names`**: `List<string> names = collector.Select(x => x.Name).ToList();`
- **`selectedMaterial`**: `string selectedMaterial = SelectMaterial(names, uiapp.MainWindowHandle);`
- **`materialId`**: `ElementId materialId = MaterialUtil.GetByNameDocument(selectedMaterial, document).Id;`
- **`transactionName`**: `string transactionName = "Paint Elements with material " + selectedMaterial;`
- **`element`**: `Element element = document.GetElement(id);`
- **`viewModel`**: `DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();`
- **`dialog`**: `DropdownSelectionView dialog = new DropdownSelectionView(mainWindowHandle) { DataContext = viewModel };`
- **`dResult`**: `return dResult == true ? viewModel.SelectedItem : null;`

---

### Class: `PrintBatchMultiDoc`
**File:** [src/SyntheticShared/Commands/PrintBatchMultiDoc.cs](../src/SyntheticShared/Commands/PrintBatchMultiDoc.cs)

Converts Drafting Views to Legends

#### Methods
##### `if`
```csharp
else if (document.IsWorkshared)
```
##### `ResolveWarnings`
```csharp
private void ResolveWarnings(object sender, FailuresProcessingEventArgs e)
```
##### `selectFolderPath`
```csharp
internal string selectFolderPath (IntPtr ownerHandle)
```
##### `SelectDocuments`
```csharp
internal List<Document> SelectDocuments(DocumentSet docs, IntPtr mainWindowHandle)
```
##### `SelectViewSheetSet`
```csharp
internal ViewSheetSet SelectViewSheetSet(List<ViewSheetSet> viewSheetSet, IntPtr mainWindowHandle)
```
##### `SelectPrintSetting`
```csharp
internal PrintSetting SelectPrintSetting(List<PrintSetting> printSettings, IntPtr mainWindowHandle)
```
---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`elemIdActive`**: `ElementId elemIdActive = new FilteredElementCollector(doc, doc.ActiveView.Id).FirstElementId();`
- **`documents`**: `DocumentSet documents = uiapp.Application.Documents;`
- **`selectedDocuments`**: `List<Document> selectedDocuments = SelectDocuments(documents, uiapp.MainWindowHandle);`
- **`coll`**: `FilteredElementCollector coll = new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet));`
- **`viewSets`**: `List<ViewSheetSet> viewSets = coll.Cast<ViewSheetSet>().ToList();`
- **`selectedViewSheetSet`**: `ViewSheetSet selectedViewSheetSet = SelectViewSheetSet(viewSets, uiapp.MainWindowHandle);`
- **`viewSheetSetName`**: `string viewSheetSetName = null;`
- **`settingIds`**: `IList<ElementId> settingIds = (IList<ElementId>)doc.GetPrintSettingIds();`
- **`selectedPrintSetting`**: `PrintSetting selectedPrintSetting = SelectPrintSetting(printSettings, uiapp.MainWindowHandle);`
- **`printSettingSetName`**: `string printSettingSetName = null;`
- **`printPath`**: `string printPath = selectFolderPath(uiapp.MainWindowHandle);`
- **`defaultBB`**: `BBPrinterSettingsUtils defaultBB = new BBPrinterSettingsUtils();`
- **`newBB`**: `BBPrinterSettingsUtils newBB = new BBPrinterSettingsUtils("0", "0", printPath);`
- **`docTitle`**: `string docTitle = document.Title;`
- **`nextSettingIds`**: `IList<ElementId> nextSettingIds = (IList<ElementId>)document.GetPrintSettingIds();`
- **`printSettting`**: `PrintSetting printSettting = ps.OfType<PrintSetting>().FirstOrDefault(p => p.Name == printSettingSetName);`
- **`currentDoc`**: `Document currentDoc = document;`
- **`configuration`**: `WorksetConfiguration configuration = new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);`
- **`openOptions`**: `OpenOptions openOptions = new OpenOptions();`
- **`cloudCallback`**: `OpenFromCloudCallback cloudCallback = new OpenFromCloudCallback();`
- **`printManager`**: `PrintManager printManager = currentDoc.PrintManager;`
- **`oldPrintToFileName`**: `string oldPrintToFileName = printManager.PrintToFileName;`
- **`fileName`**: `string fileName = currentDoc.Title + ".pdf";`
- **`fa`**: `FailuresAccessor fa = e.GetFailuresAccessor();`
- **`failList`**: `IList<FailureMessageAccessor> failList = new List<FailureMessageAccessor>();`
- **`failID`**: `FailureDefinitionId failID = failure.GetFailureDefinitionId();`
- **`itemList`**: `List<string> itemList = new List<string>();`
- **`docList`**: `List<Document> docList = new List<Document>();`
- **`selectedDocs`**: `List<Document> selectedDocs = new List<Document>();`
- **`viewModel`**: `ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();`
- **`dialog`**: `ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };`
- **`selectedList`**: `List<string> selectedList = viewModel.CheckedItems;`
- **`d`**: `Document d = docList.First(s => s.Title == selectedItem);`
- **`selectedDocs`**: `return selectedDocs;`
- **`itemList`**: `List<string> itemList = new List<string>();`
- **`selectedViewSheet`**: `ViewSheetSet selectedViewSheet = null;`
- **`viewModel`**: `ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();`
- **`dialog`**: `ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };`
- **`selectedName`**: `string selectedName = viewModel.CheckedItems[0];`
- **`selectedViewSheet`**: `return selectedViewSheet;`
- **`itemList`**: `List<string> itemList = new List<string>();`
- **`selectedPrintSetting`**: `PrintSetting selectedPrintSetting = null;`
- **`viewModel`**: `ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();`
- **`dialog`**: `ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };`
- **`selectedName`**: `string selectedName = viewModel.CheckedItems[0];`
- **`selectedPrintSetting`**: `return selectedPrintSetting;`

---

### Class: `ScopeBoxesMoveToWorkset`
**File:** [src/SyntheticShared/Commands/ScopeBoxesMoveToWorkset.cs](../src/SyntheticShared/Commands/ScopeBoxesMoveToWorkset.cs)

Revit external command to move all scope boxes in the project to a selected workset and optionally update their visibility in views.

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`scopeBoxes`**: `IList<Element> scopeBoxes = ScopeBoxUtil.GetAllScopeBoxes(doc);`
- **`worksets`**: `FilteredWorksetCollector worksets = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset);`
- **`worksetNames`**: `List<string> worksetNames = worksets.Select(w => w.Name).OrderBy(n => n).ToList();`
- **`viewModel`**: `DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();`
- **`dialog`**: `DropdownSelectionView dialog = new DropdownSelectionView(uiapp.MainWindowHandle) { DataContext = viewModel };`
- **`selection`**: `string selection = viewModel.SelectedItem;`
- **`destinationWorkset`**: `Workset destinationWorkset = worksets.FirstOrDefault(w => w.Name == selection);`
- **`skippedViews`**: `List<string> skippedViews = new List<string>();`
- **`updatedCount`**: `int updatedCount = 0;`
- **`resultMessage`**: `StringBuilder resultMessage = new StringBuilder();`

---

### Class: `SettingsDashboardCommand`
**File:** [src/SyntheticShared/Commands/SettingsDashboardCommand.cs](../src/SyntheticShared/Commands/SettingsDashboardCommand.cs)

Revit external command to display and edit the current project's configuration settings using the Unified Settings Dashboard.

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`mainWindowHandle`**: `IntPtr mainWindowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;`
- **`viewModel`**: `var viewModel = new SettingsDashboardViewModel(doc, mainWindowHandle);`

---

### Class: `StandardsEditorShow`
**File:** [src/SyntheticShared/Commands/StandardsEditorShow.cs](../src/SyntheticShared/Commands/StandardsEditorShow.cs)

Command to display the Modeless Synthetic Standards JSON Editor.

#### Fields
- **`_windowInstance`**: `private static JsonEditorWindow _windowInstance;`
- **`_eventHandler`**: `private static JsonEditorExternalEventHandler _eventHandler;`
- **`_externalEvent`**: `private static ExternalEvent _externalEvent;`
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`vm`**: `var vm = new JsonEditorMainViewModel(uiapp);`

---

### Class: `StorageDelete`
**File:** [src/SyntheticShared/Commands/StorageDelete.cs](../src/SyntheticShared/Commands/StorageDelete.cs)

Deletes all extensible storage created by any application all active documents. This command will also report if there is no storage in the active document to delete. The document must be saved after the storage is deleted to commit the deletion.

#### Methods
##### `Execute`
```csharp
public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
```
Executes the extensible storage deletion command.

**Parameters:**
- `commandData`: Revit external command data.
- `message`: A message returning errors if any.
- `elements`: Revit elements set.

**Returns:** Result code of the execution.

---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`document`**: `Document document = uiapp.ActiveUIDocument.Document;`
- **`schemas`**: `IList<Schema> schemas = Schema.ListSchemas();`
- **`itemList`**: `List<string> itemList = new List<string>();`
- **`viewModel`**: `ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();`
- **`dialog`**: `ListByCheckboxView dialog = new ListByCheckboxView(uiapp.MainWindowHandle) { DataContext = viewModel };`
- **`filteredSchemas`**: `List<Schema> filteredSchemas = new List<Schema>();`
- **`purgedSchema`**: `List<string> purgedSchema = StorageUtil.PurgeSchema(filteredSchemas, document);`

---

### Class: `StorageQuery`
**File:** [src/SyntheticShared/Commands/StorageQuery.cs](../src/SyntheticShared/Commands/StorageQuery.cs)

Checks to see if any extensible storage in the document exists and displays elements containing storage to the user.

#### Methods
##### `Execute`
```csharp
public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
```
Execute a Revit Command

**Parameters:**
- `commandData`: commandData
- `message`: message
- `elements`: Currently selected elements

**Returns:** A Autodesk.Revit.UI.Result

---

#### Fields
- **`document`**: `Document document = commandData.Application.ActiveUIDocument.Document;`
- **`storageElements`**: `string storageElements = StorageUtil.GetElementStringWithAllSchemas(document);`

---

### Class: `ViewAutoNumberConfig`
**File:** [src/SyntheticShared/Commands/ViewAutoNumberConfig.cs](../src/SyntheticShared/Commands/ViewAutoNumberConfig.cs)

Revit Command to configure the View Autonumber command.

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`config`**: `Config config = null;`
- **`settings`**: `ViewAutoNumSettings settings = new ViewAutoNumSettings();`
- **`itemList`**: `List<string> itemList = new List<string>();`
- **`familyList`**: `IList<Element> familyList = FamilySymbolUtil.GetFamiliesOfCategory(doc, BuiltInCategory.OST_GenericAnnotation);`
- **`itemName`**: `string itemName = familySymbol.Family.Name + " | " + familySymbol.Name;`
- **`r`**: `var r = SelectFamily(itemList, uiapp.MainWindowHandle);`
- **`dResult`**: `bool dResult = r.result;`
- **`selection`**: `string selection = r.selection;`
- **`familySymbol`**: `FamilySymbol familySymbol = choices[selection];`
- **`itemList2`**: `List<string> itemList2 = new List<string>();`
- **`xGridParameters`**: `ParameterSet xGridParameters = familySymbol.Parameters;`
- **`dResult2`**: `bool dResult2 = r.result;`
- **`viewAutoNumXGridName`**: `string viewAutoNumXGridName = r.selection;`
- **`itemList3`**: `List<string> itemList3 = new List<string>();`
- **`yGridParameters`**: `ParameterSet yGridParameters = familySymbol.Parameters;`
- **`dResult3`**: `bool dResult3 = r.result;`
- **`viewAutoNumYGridName`**: `string viewAutoNumYGridName = r.selection;`
- **`viewModel`**: `DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();`
- **`dialog`**: `DropdownSelectionView dialog = new DropdownSelectionView(mainWindowHandle) { DataContext = viewModel };`
- **`selection`**: `string selection = viewModel.SelectedItem;`
- **`viewModel`**: `DropdownSelectionViewModel viewModel = new DropdownSelectionViewModel();`
- **`dialog`**: `DropdownSelectionView dialog = new DropdownSelectionView(mainWindowHandle) { DataContext = viewModel };`
- **`selection`**: `string selection = viewModel.SelectedItem;`

---

### Class: `ViewsAutoNumber`
**File:** [src/SyntheticShared/Commands/ViewsAutoNumber.cs](../src/SyntheticShared/Commands/ViewsAutoNumber.cs)

Revit Command to automatically number views on the active sheet based on a family that determines the origin point and the grid spacing.

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`transactionName`**: `string transactionName = "Autonumber views on sheet";`
- **`settings`**: `ViewAutoNumSettings settings = SettingsManager.Get<ViewAutoNumSettings>(doc);`
- **`tResult`**: `TaskDialogResult tResult = taskDialog.Show();`
- **`sheet`**: `ViewSheet sheet = (ViewSheet)doc.ActiveView;`
- **`tResult`**: `TaskDialogResult tResult = taskDialog.Show();`

---

### Class: `WorksetSetFile`
**File:** [src/SyntheticShared/Commands/WorksetSetFile.cs](../src/SyntheticShared/Commands/WorksetSetFile.cs)

Sets the path and sheet to an Excel file with Worksets.  Stores the setting in an Extensible Storage.

#### Methods
##### `if`
```csharp
else if (taskDialogResult == TaskDialogResult.CommandLink2)
```
##### `if`
```csharp
else if (taskDialogResult == TaskDialogResult.CommandLink3)
```
##### `if`
```csharp
else if (taskDialogResult == TaskDialogResult.Cancel)
```
##### `WorksetFileOptions`
```csharp
internal TaskDialogResult WorksetFileOptions()
```
---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`app`**: `Application app = uiapp.Application;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`worksetSettings`**: `WorksetSettings worksetSettings = new WorksetSettings();`
- **`config`**: `Config config = null;`
- **`taskDialogResult`**: `TaskDialogResult taskDialogResult = WorksetFileOptions();`
- **`file`**: `string file = doc.Title + " Worksets.xlsx";`
- **`path`**: `string path = doc.PathName;`
- **`basicFileInfo`**: `BasicFileInfo basicFileInfo = BasicFileInfo.Extract(path);`
- **`modelPath`**: `ModelPath modelPath = doc.GetWorksharingCentralModelPath();`
- **`centralServerPath`**: `string centralServerPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);`
- **`excel`**: `Excel excel = new Excel(Path.Combine(path, file));`
- **`itemList`**: `List<string> itemList = excel.WorkSheetNames();`
- **`viewModel`**: `ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();`
- **`dialog`**: `ListByCheckboxView dialog = new ListByCheckboxView(uiapp.MainWindowHandle) { DataContext = viewModel };`
- **`file`**: `string file = null;`
- **`path`**: `string path = null;`
- **`openFileDialog`**: `FileOpenDialog openFileDialog = new FileOpenDialog("Excel Files (*.xlsx,*.xls,*.csv)|*.xlsx;*.xls;*.csv");`
- **`result`**: `ItemSelectionDialogResult result = openFileDialog.Show();`
- **`modelPath`**: `ModelPath modelPath = openFileDialog.GetSelectedModelPath();`
- **`fullPath`**: `string fullPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);`
- **`excel`**: `Excel excel = new Excel(Path.Combine(path, file));`
- **`itemList`**: `List<string> itemList = excel.WorkSheetNames();`
- **`viewModel`**: `ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();`
- **`dialog`**: `ListByCheckboxView dialog = new ListByCheckboxView(uiapp.MainWindowHandle) { DataContext = viewModel };`
- **`result`**: `TaskDialogResult result = taskDialog.Show();`
- **`result`**: `return result;`

---

### Class: `WorksetSettingsShow`
**File:** [src/SyntheticShared/Commands/WorksetSettingsShow.cs](../src/SyntheticShared/Commands/WorksetSettingsShow.cs)

Displays the current path, worksheet and list of Worksets based on the current settings.

#### Methods
##### `ShowWorksets`
```csharp
internal TaskDialogResult ShowWorksets(string instructions, string content)
```
##### `FileNotSet`
```csharp
internal TaskDialogResult FileNotSet()
```
##### `MissingFile`
```csharp
internal TaskDialogResult MissingFile()
```
---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`settings`**: `WorksetSettings settings = SettingsManager.Get<WorksetSettings>(doc);`
- **`tResult`**: `TaskDialogResult tResult = FileNotSet();`
- **`worksetUtil`**: `WorksetUtil worksetUtil = settings.WorksetsByExcel();`
- **`instructions`**: `string instructions = "WorksetPath: " + settings.FullPath();`
- **`rResult`**: `TaskDialogResult rResult = ShowWorksets(instructions, worksetUtil.WorksetNames());`
- **`result`**: `TaskDialogResult result = resultDialog.Show();`
- **`result`**: `return result;`
- **`result`**: `TaskDialogResult result = taskDialog.Show();`
- **`result`**: `return result;`
- **`result`**: `TaskDialogResult result = taskDialog.Show();`
- **`result`**: `return result;`

---

### Class: `WorksetStartView`
**File:** [src/SyntheticShared/Commands/WorksetStartView.cs](../src/SyntheticShared/Commands/WorksetStartView.cs)

Revit external command to set project info (such as starting view) based on workset Excel configuration settings.

#### Methods
##### `FileNotSet`
```csharp
internal TaskDialogResult FileNotSet()
```
##### `MissingFile`
```csharp
internal TaskDialogResult MissingFile()
```
---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`settings`**: `WorksetSettings settings = SettingsManager.Get<WorksetSettings>(doc);`
- **`tResult`**: `TaskDialogResult tResult = FileNotSet();`
- **`worksetUtil`**: `WorksetUtil worksetUtil = settings.WorksetsByExcel();`
- **`result`**: `TaskDialogResult result = taskDialog.Show();`
- **`result`**: `return result;`
- **`result`**: `TaskDialogResult result = taskDialog.Show();`
- **`result`**: `return result;`

---

### Class: `WorksetsImport`
**File:** [src/SyntheticShared/Commands/WorksetsImport.cs](../src/SyntheticShared/Commands/WorksetsImport.cs)

Revit Command to import Worksets based on an Excel file.

#### Methods
##### `SelectWorksets`
```csharp
internal List<string> SelectWorksets (List<string> worksetNames, IntPtr mainWindowHandle)
```
##### `FileNotSet`
```csharp
internal TaskDialogResult FileNotSet()
```
##### `ShowWorksets`
```csharp
internal TaskDialogResult ShowWorksets (string instructions, string content)
```
##### `MissingFile`
```csharp
internal TaskDialogResult MissingFile ()
```
---

#### Fields
- **`uiapp`**: `UIApplication uiapp = commandData.Application;`
- **`uidoc`**: `UIDocument uidoc = uiapp.ActiveUIDocument;`
- **`app`**: `Application app = uiapp.Application;`
- **`doc`**: `Document doc = uidoc.Document;`
- **`settings`**: `WorksetSettings settings = SettingsManager.Get<WorksetSettings>(doc);`
- **`tResult`**: `TaskDialogResult tResult = FileNotSet();`
- **`worksetUtil`**: `WorksetUtil worksetUtil = settings.WorksetsByExcel();`
- **`selectedWorksets`**: `List<string> selectedWorksets = SelectWorksets(worksetUtil.Names(), uiapp.MainWindowHandle);`
- **`selectedList`**: `List<string> selectedList = null;`
- **`viewModel`**: `ListByCheckboxViewModel viewModel = new ListByCheckboxViewModel();`
- **`dialog`**: `ListByCheckboxView dialog = new ListByCheckboxView(mainWindowHandle) { DataContext = viewModel };`
- **`selectedList`**: `return selectedList;`

---

## Namespace: `Synthetic.Handlers`

### Class: `DetailItemFactoryEventHandler`
**File:** [src/SyntheticShared/Handlers/DetailItemFactoryEventHandler.cs](../src/SyntheticShared/Handlers/DetailItemFactoryEventHandler.cs)

#### Methods
##### `ConfiguredElements`
```csharp
public List<SelectedElementItemViewModel> ConfiguredElements
```
Gets or sets the collection of configured elements to process.

##### `Execute`
```csharp
public void Execute(UIApplication app)
```
Executes the batch processing loop asynchronously on the Revit main thread.

##### `GetName`
```csharp
public string GetName()
```
Gets the handler name.

##### `OnFamilyFound`
```csharp
public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
```
Callback when a family is found during loading.

##### `OnSharedFamilyFound`
```csharp
public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
```
Callback when a shared family is found during loading.

#### Fields
- **`IExternalEventHandler`**: `public class DetailItemFactoryEventHandler : IExternalEventHandler {`
  *Description:* External event handler to process selected elements into 2D Detail Items asynchronously on the Revit main thread.
- **`OutputFolder`**: `public string OutputFolder`
  *Description:* Gets or sets the target output folder path.
- **`TargetSubcategory`**: `public string TargetSubcategory`
  *Description:* Gets or sets the target subcategory name.
- **`OverwriteExisting`**: `public bool OverwriteExisting`
  *Description:* Gets or sets a value indicating whether existing family files should be overwritten on disk.
- **`ProgressView`**: `public DetailItemFactoryProgressView? ProgressView`
  *Description:* Gets or sets the reference to the modeless progress dialog.
- **`IFamilyLoadOptions`**: `public class SimpleFamilyLoadOptions : IFamilyLoadOptions {`
  *Description:* Standard implementation of IFamilyLoadOptions to automatically overwrite family parameters.

---
### Class: `JsonEditorExternalEventHandler`
**File:** [src/SyntheticShared/Handlers/JsonEditorExternalEventHandler.cs](../src/SyntheticShared/Handlers/JsonEditorExternalEventHandler.cs)

External event handler to marshal calls from modeless JSON Editor UI to the Revit API thread.

#### Methods
##### `QueueRequest`
```csharp
public void QueueRequest(JsonEditorRequestType requestType, JsonEditorMainViewModel mainVM, List<ElementModel> importPayload = null)
```
Queues a request to be executed on the Revit API thread.

##### `Execute`
```csharp
public void Execute(UIApplication app)
```
Executes the queued request on the Revit API thread.

##### `if`
```csharp
else if (currentRequest == JsonEditorRequestType.Import)
```
##### `GetName`
```csharp
public string GetName()
```
Gets the name of this external event handler.

---

#### Fields
- **`_lock`**: `private readonly object _lock = new object();`
- **`_requestType`**: `private JsonEditorRequestType _requestType = JsonEditorRequestType.None;`
- **`_mainVM`**: `private JsonEditorMainViewModel _mainVM;`
- **`_importPayload`**: `private List<ElementModel> _importPayload;`
- **`currentRequest`**: `JsonEditorRequestType currentRequest;`
- **`currentVM`**: `JsonEditorMainViewModel currentVM;`
- **`currentPayload`**: `List<ElementModel> currentPayload;`
- **`exportVM`**: `var exportVM = new ExportStylesViewModel(app, true);`
- **`exportView`**: `var exportView = new Views.ExportStylesView(app.MainWindowHandle) { DataContext = exportVM };`
- **`importVM`**: `var importVM = new ImportStylesViewModel(app, currentPayload);`
- **`importView`**: `var importView = new Views.ImportStylesView(app.MainWindowHandle) { DataContext = importVM };`

---

### Enum: `JsonEditorRequestType`
**File:** [src/SyntheticShared/Handlers/JsonEditorExternalEventHandler.cs](../src/SyntheticShared/Handlers/JsonEditorExternalEventHandler.cs)

Request types supported by the JsonEditorExternalEventHandler.

### Class: `MergeExecutionReport`
**File:** [src/SyntheticShared/Handlers/ProcessMergeEventHandler.cs](../src/SyntheticShared/Handlers/ProcessMergeEventHandler.cs)

Tracks the summary results of a merge cluster execution for Step 6.

#### Properties
- **`ClusterName`**: `public string ClusterName { get; set; }`
  *Description:* Gets or sets the name of the merge cluster.
- **`InstancesSwappedCount`**: `public int InstancesSwappedCount { get; set; }`
  *Description:* Gets or sets the count of instances swapped during the merge.
- **`DuplicateTypesPurged`**: `public int DuplicateTypesPurged { get; set; }`
  *Description:* Gets or sets the count of duplicate types purged.
- **`ExecutionSuccessStatus`**: `public bool ExecutionSuccessStatus { get; set; }`
  *Description:* Gets or sets a value indicating whether the merge execution succeeded.

### Class: `ProcessMergeEventHandler`
**File:** [src/SyntheticShared/Handlers/ProcessMergeEventHandler.cs](../src/SyntheticShared/Handlers/ProcessMergeEventHandler.cs)

External event handler to process the active merge queue on the Revit API thread.

#### Methods
##### `QueueRequest`
```csharp
public void QueueRequest(MergeQueueViewModel queueVM, Window window, MergeDuplicatesViewModel mainVM, CancellationToken cancellationToken)
```
Queues a merge execution request with its corresponding UI/VM contexts.

##### `Execute`
```csharp
public void Execute(UIApplication app)
```
Executes the merge processing on the Revit API thread.

##### `if`
```csharp
else if (primElement is GroupType)
```
##### `if`
```csharp
else if (primElement is AssemblyType)
```
##### `CopyParameterValue`
```csharp
private static void CopyParameterValue(Parameter sourceParam, Parameter targetParam)
```
##### `GetDefaultGroupAndType`
```csharp
private static void GetDefaultGroupAndType(out object paramGroup, out object paramType)
```
##### `GetParamGroupAndTypeFromSource`
```csharp
private static bool GetParamGroupAndTypeFromSource(Parameter sourceParam, out object paramGroup, out object paramType)
```
##### `InjectParameterToFamily`
```csharp
private static bool InjectParameterToFamily(FamilyManager famManager, string paramName, object paramGroup, object paramType)
```
##### `ExportStep6ResultsSummary`
```csharp
private void ExportStep6ResultsSummary(List<MergeExecutionReport> summaries)
```
##### `GetName`
```csharp
public string GetName()
```
Gets the name of the external event handler.

**Returns:** A string name of the handler.

---

#### Fields
- **`_lock`**: `private readonly object _lock = new object();`
- **`_queueVM`**: `private MergeQueueViewModel _queueVM;`
- **`_parentWindow`**: `private Window _parentWindow;`
- **`_mainVM`**: `private MergeDuplicatesViewModel _mainVM;`
- **`_cancellationToken`**: `private CancellationToken _cancellationToken;`
- **`currentQueue`**: `MergeQueueViewModel currentQueue;`
- **`currentWindow`**: `Window currentWindow;`
- **`currentMainVM`**: `MergeDuplicatesViewModel currentMainVM;`
- **`currentToken`**: `CancellationToken currentToken;`
- **`doc`**: `Document doc = app.ActiveUIDocument?.Document;`
- **`reports`**: `var reports = new List<MergeExecutionReport>();`
- **`clustersToProcess`**: `var clustersToProcess = currentQueue.QueuedClusters.ToList();`
- **`wasCancelled`**: `bool wasCancelled = false;`
- **`primaryItem`**: `var primaryItem = cluster.SelectedPrimary;`
- **`primElement`**: `Element primElement = doc.GetElement(primaryItem.RevitElementId);`
- **`parametersToInject`**: `var parametersToInject = new List<string>();`
- **`famDoc`**: `Document famDoc = doc.EditFamily(primFamily);`
- **`anyInjected`**: `bool anyInjected = false;`
- **`famManager`**: `FamilyManager famManager = famDoc.FamilyManager;`
- **`paramExists`**: `bool paramExists = false;`
- **`paramGroup`**: `object paramGroup = null;`
- **`paramType`**: `object paramType = null;`
- **`foundSource`**: `bool foundSource = false;`
- **`sourceSymbol`**: `FamilySymbol sourceSymbol = doc.GetElement(mapping.SourceType.RevitTypeId) as FamilySymbol;`
- **`sourceParam`**: `Parameter sourceParam = sourceSymbol.LookupParameter(paramName);`
- **`typeMap`**: `var typeMap = new Dictionary<ElementId, ElementId>();`
- **`resolvedTargetTypeId`**: `ElementId resolvedTargetTypeId = mapping.TargetType.RevitTypeId;`
- **`targetTypeElement`**: `ElementType targetTypeElement = doc.GetElement(mapping.TargetType.RevitTypeId) as ElementType;`
- **`existingSymbol`**: `FamilySymbol existingSymbol = null;`
- **`sym`**: `FamilySymbol sym = doc.GetElement(symbolId) as FamilySymbol;`
- **`duplicatedType`**: `ElementType duplicatedType = targetTypeElement.Duplicate(mapping.SourceType.Name);`
- **`sourceSymbol`**: `Element sourceSymbol = doc.GetElement(mapping.SourceType.RevitTypeId);`
- **`targetParam`**: `Parameter targetParam = duplicatedType.LookupParameter(sourceParam.Definition.Name);`
- **`targetTypeObj`**: `Element targetTypeObj = doc.GetElement(resolvedTargetTypeId);`
- **`winningElement`**: `Element winningElement = doc.GetElement(row.WinningValueElementId);`
- **`winningParam`**: `Parameter winningParam = winningElement.LookupParameter(row.ParameterName);`
- **`targetParam`**: `Parameter targetParam = targetTypeObj.LookupParameter(row.ParameterName);`
- **`instancesSwapped`**: `int instancesSwapped = 0;`
- **`dupTypeIds`**: `var dupTypeIds = typeMap.Keys.ToList();`
- **`targetTypeId`**: `ElementId targetTypeId = typeMap[instance.GetTypeId()];`
- **`targetSymbol`**: `FamilySymbol targetSymbol = doc.GetElement(targetTypeId) as FamilySymbol;`
- **`targetTypeId`**: `ElementId targetTypeId = typeMap[group.GetTypeId()];`
- **`targetGroupType`**: `GroupType targetGroupType = doc.GetElement(targetTypeId) as GroupType;`
- **`targetTypeId`**: `ElementId targetTypeId = typeMap[assembly.GetTypeId()];`
- **`purgedCount`**: `int purgedCount = 0;`
- **`allTypesMergedOrMigrated`**: `bool allTypesMergedOrMigrated = true;`
- **`mapping`**: `var mapping = cluster.TypeMappings.FirstOrDefault(m => m.SourceType.RevitTypeId == t.RevitTypeId);`
- **`mapping`**: `var mapping = cluster.TypeMappings.FirstOrDefault(m => m.SourceType.RevitTypeId == t.RevitTypeId);`
- **`successCount`**: `int successCount = reports.Count(r => r.ExecutionSuccessStatus);`
- **`groupTypeIdType`**: `Type groupTypeIdType = typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.GroupTypeId");`
- **`dataProp`**: `var dataProp = groupTypeIdType.GetProperty("Data");`
- **`specTypeIdString`**: `Type specTypeIdString = typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.SpecTypeId+String");`
- **`textProp`**: `var textProp = specTypeIdString.GetProperty("Text");`
- **`bipgType`**: `Type bipgType = typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.BuiltInParameterGroup");`
- **`ptType`**: `Type ptType = typeof(ElementId).Assembly.GetType("Autodesk.Revit.DB.ParameterType");`
- **`groupProp`**: `var groupProp = sourceParam.Definition.GetType().GetProperty("ParameterGroup");`
- **`groupMethod`**: `var groupMethod = sourceParam.Definition.GetType().GetMethod("GetGroupTypeId");`
- **`typeProp`**: `var typeProp = sourceParam.Definition.GetType().GetProperty("ParameterType");`
- **`typeMethod`**: `var typeMethod = sourceParam.Definition.GetType().GetMethod("GetDataType");`
- **`true`**: `return true;`
- **`false`**: `return false;`
- **`false`**: `return false;`
- **`json`**: `string json = JsonConvert.SerializeObject(summaries, settings);`
- **`assemblyPath`**: `string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;`
- **`assemblyDir`**: `string assemblyDir = Path.GetDirectoryName(assemblyPath);`
- **`logPath`**: `string logPath = Path.Combine(assemblyDir, "MergeDuplicates_Step6_ExecutionResult.json");`

---

### Class: `ProcessMergeFamilyLoadOptions`
**File:** [src/SyntheticShared/Handlers/ProcessMergeEventHandler.cs](../src/SyntheticShared/Handlers/ProcessMergeEventHandler.cs)

Standard family load options to automatically overwrite parameter values.

#### Methods
##### `OnFamilyFound`
```csharp
public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
```
Called when a family is found in the target document. Overwrites parameter values.

**Parameters:**
- `familyInUse`: Indicates if the family is currently in use.
- `overwriteParameterValues`: Output parameter indicating if parameter values should be overwritten.

**Returns:** True to load the family.

##### `OnSharedFamilyFound`
```csharp
public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
```
Called when a shared family is found in the target document. Overwrites parameter values.

**Parameters:**
- `sharedFamily`: The shared family being loaded.
- `familyInUse`: Indicates if the shared family is currently in use.
- `source`: Output parameter indicating the source of the family.
- `overwriteParameterValues`: Output parameter indicating if parameter values should be overwritten.

**Returns:** True to load the shared family.

---

#### Fields
- **`true`**: `return true;`
- **`true`**: `return true;`

---

### Class: `SyncExternalEventHandler`
**File:** [src/SyntheticShared/Handlers/SyncExternalEventHandler.cs](../src/SyntheticShared/Handlers/SyncExternalEventHandler.cs)

External event handler to marshal settings pull/push operations from modeless UI to the Revit API thread.

#### Methods
##### `QueueRequest`
```csharp
public void QueueRequest(SyncRequestType requestType, Document doc, string filePath)
```
Queues a sync request to be executed on the Revit API thread.

##### `Execute`
```csharp
public void Execute(UIApplication app)
```
Executes the queued sync request on the Revit API thread.

##### `if`
```csharp
else if (currentRequest == SyncRequestType.Push)
```
##### `GetName`
```csharp
public string GetName()
```
Gets the name of this external event handler.

---

#### Fields
- **`_lock`**: `private readonly object _lock = new object();`
- **`_requestType`**: `private SyncRequestType _requestType = SyncRequestType.None;`
- **`_doc`**: `private Document _doc;`
- **`_filePath`**: `private string _filePath;`
- **`currentRequest`**: `SyncRequestType currentRequest;`
- **`currentDoc`**: `Document currentDoc;`
- **`currentFilePath`**: `string currentFilePath;`
- **`jsonText`**: `string jsonText = File.ReadAllText(currentFilePath);`
- **`dict`**: `var dict = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>>(jsonText);`
- **`settings`**: `var settings = worksetToken.ToObject<WorksetSettings>();`
- **`settings`**: `var settings = viewAutoNumToken.ToObject<ViewAutoNumSettings>();`
- **`settings`**: `var settings = materialLibToken.ToObject<MaterialLibrarySettings>();`
- **`settings`**: `var settings = projectMatToken.ToObject<ProjectMaterialSettings>();`
- **`syncSettings`**: `var syncSettings = SettingsManager.Get<SyncSettings>(currentDoc);`
- **`jsonText`**: `string jsonText = JsonConvert.SerializeObject(docSettings, Formatting.Indented);`

---

### Enum: `SyncRequestType`
**File:** [src/SyntheticShared/Handlers/SyncExternalEventHandler.cs](../src/SyntheticShared/Handlers/SyncExternalEventHandler.cs)

Request types supported by the SyncExternalEventHandler.

## Namespace: `Synthetic.IO`

### Class: `Excel`
**File:** [src/SyntheticShared/IO/Excel.cs](../src/SyntheticShared/IO/Excel.cs)

Helper class to read data from Microsoft Excel spreadsheets using Interop.

#### Properties
- **`path`**: `public string path { get; set; }`
  *Description:* Gets or sets the file path to the Excel workbook.
- **`worksheetName`**: `public string worksheetName { get; set; }`
  *Description:* Gets or sets the name of the worksheet to read.
- **`cells`**: `public List<List<object>> cells { get; set; }`
  *Description:* Gets or sets the cell values read from the spreadsheet, stored as a 2D list of objects.

#### Methods
##### `Excel`
```csharp
public Excel(string path)
```
Initializes a new instance of the Excel class with a file path.  <param name="path">The file path to the Excel workbook.</param>

##### `Excel`
```csharp
public Excel(string path, string worksheetName)
```
Initializes a new instance of the Excel class with a file path and worksheet name.  <param name="path">The file path to the Excel workbook.</param> <param name="worksheetName">The name of the worksheet to load.</param>

##### `ReadExcel`
```csharp
public List<List<object>> ReadExcel()
```
Reads the spreadsheet and returns cell contents as a 2D list.

**Returns:** A list of rows, where each row is a list of cell values.

##### `WorkSheetNames`
```csharp
public List<string> WorkSheetNames ()
```
Retrieves the list of worksheet names from the Excel workbook.

**Returns:** A list of worksheet name strings.

---

#### Fields
- **`null`**: `return null;`
- **`rowCount`**: `int rowCount = range.Rows.Count;`
- **`columnCount`**: `int columnCount = range.Columns.Count;`
- **`rowData`**: `List<object> rowData = new List<object>();`
- **`null`**: `return null;`
- **`names`**: `List<string> names = new List<string>();`
- **`names`**: `return names;`

---

### Class: `Json`
**File:** [src/SyntheticShared/IO/Json.cs](../src/SyntheticShared/IO/Json.cs)

Convert Revit Elements to and from JSON.

#### Methods
##### `Json`
```csharp
internal Json() { }
```
Deserializes a JSON string into a general object list structure.

**Parameters:**
- `JSON`: The JSON string representing list data.

**Returns:** A deserialized object structure.

##### `Encode`
```csharp
public static string Encode(System.Object @object)
```
Serializes an element into JSON.

**Parameters:**
- `object`: An object to serialize.

**Returns:** A string of JSON.

##### `EncodeMinimal`
```csharp
public static string EncodeMinimal(System.Object @object)
```
Serializes an object to JSON string with no indentation (minimal formatting).

**Parameters:**
- `object`: The object to serialize.

**Returns:** A JSON string representation of the object.

##### `ListToJSON`
```csharp
public static string ListToJSON(List<System.Object> ListJSON)
```
Converts a list of objects to an indented JSON string.

**Parameters:**
- `ListJSON`: The list of objects to serialize.

**Returns:** An indented JSON string.

##### `JsonToList`
```csharp
public static object JsonToList(string JSON)
```
Deserializes a JSON string into a general object list structure.

**Parameters:**
- `JSON`: The JSON string representing list data.

**Returns:** A deserialized object structure.

---

## Namespace: `Synthetic.Models`

### Class: `BooleanModel`
**File:** [src/SyntheticShared/Models/BooleanModel.cs](../src/SyntheticShared/Models/BooleanModel.cs)

Model for serializing Revit Objects

#### Properties
- **`boolean`**: `public bool boolean { get; set; }`
  *Description:* Value of the boolean

#### Methods
##### `BooleanModel`
```csharp
public BooleanModel() { }
```
Constructor

##### `BooleanModel`
```csharp
public BooleanModel(bool boolean)
```
Constructor  <param name="boolean"></param>

---

### Class: `BrowserOrganizationModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model for Revit BrowserOrganization, inheriting from ElementModel.

#### Methods
##### `BrowserOrganizationModel`
```csharp
public BrowserOrganizationModel() : base() { }
```
Initializes a new instance of the BrowserOrganizationModel class.

##### `BrowserOrganizationModel`
```csharp
public BrowserOrganizationModel(BrowserOrganization elem, bool isTemplate) : base(elem, isTemplate) { }
```
Initializes a new instance of the BrowserOrganizationModel class from a Revit BrowserOrganization.  <param name="elem">The Revit BrowserOrganization.</param> <param name="isTemplate">Flag indicating if this model is a template.</param>

##### `ModifyBrowserOrganization`
```csharp
public static BrowserOrganization ModifyBrowserOrganization(BrowserOrganizationModel model, Document doc)
```
Modifies a Revit BrowserOrganization in the document using properties of this model.

**Parameters:**
- `model`: The BrowserOrganizationModel source.
- `doc`: The Revit document.

**Returns:** The modified Revit BrowserOrganization.

---

#### Fields
- **`elem`**: `BrowserOrganization elem = (BrowserOrganization)model.GetRevitElem(doc);`
- **`elem`**: `return elem;`

---

### Class: `CategoryGraphicOverridesModel`
**File:** [src/SyntheticShared/Models/CategoryGraphicOverrideModel.cs](../src/SyntheticShared/Models/CategoryGraphicOverrideModel.cs)

Model representing graphic override settings for a specific category within a Revit view.

#### Properties
- **`Category`**: `public CategoryIdModel Category { get; set; }`
  *Description:* Gets or sets the target category model.
- **`ParentCategory`**: `public CategoryIdModel ParentCategory { get; set; }`
  *Description:* Gets or sets the parent category model, if one exists.
- **`IsHidden`**: `public bool IsHidden { get; set; }`
  *Description:* Gets or sets a value indicating whether the category is hidden in the view.
- **`GraphicOverride`**: `public OverrideGraphicSettingsModel GraphicOverride { get; set; }`
  *Description:* Gets or sets the graphic override settings for the category.

#### Methods
##### `CategoryGraphicOverridesModel`
```csharp
public CategoryGraphicOverridesModel () { }
```
Initializes a new instance of the CategoryGraphicOverridesModel class.

##### `CategoryGraphicOverridesModel`
```csharp
public CategoryGraphicOverridesModel (RevitDB.Category category, RevitView view)
```
Initializes a new instance of the CategoryGraphicOverridesModel class using a Revit category and view.  <param name="category">The Revit category.</param> <param name="view">The Revit view.</param>

##### `IsModified`
```csharp
public bool IsModified()
```
Determines if the graphic overrides have been modified from default.

**Returns:** True if modified, false otherwise.

##### `ModifyOverrideGraphicSettings`
```csharp
public void ModifyOverrideGraphicSettings(RevitView view)
```
Applies the overrides to the specified Revit view.

**Parameters:**
- `view`: The target Revit view.

---

#### Fields
- **`document`**: `RevitDoc document = view.Document;`
- **`serialOverride`**: `OverrideGraphicSettingsModel serialOverride = new OverrideGraphicSettingsModel(category, overrideSettings, document);`
- **`modified`**: `bool modified = false;`
- **`modified`**: `return modified;`
- **`document`**: `RevitDoc document = view.Document;`

---

### Class: `CategoryIdModel`
**File:** [src/SyntheticShared/Models/CategoryIdModel.cs](../src/SyntheticShared/Models/CategoryIdModel.cs)

Model that acts as a wrapper for Revit Category ElementIds to facilitate JSON serialization.

#### Properties
- **`Name`**: `public string Name { get; set; }`
  *Description:* Gets or sets the name of the category.
- **`Id`**: `public long Id { get; set; }`
  *Description:* Gets or sets the integer ID of the category.
- **`Category`**: `public RevitCategory Category { get; set; }`
  *Description:* Gets or sets the associated Revit Category object. Ignored in JSON.
- **`Document`**: `public RevitDoc Document { get; set; }`
  *Description:* Gets or sets the associated Revit Document. Ignored in JSON.
- **`IsTemplate`**: `public bool IsTemplate { get; set; }`
  *Description:* If true, SerialElement is intended to be deserialized as a template for use as standards or transfer to another project. If false, SerialElement is intended to modify an element inside the project and will include ElementIds and UniqueIds.

#### Methods
##### `ShouldSerializeId`
```csharp
public bool ShouldSerializeId()
```
If IsTemplate, don't serialize the Element Id

**Returns:** True if not a template

##### `CategoryIdModel`
```csharp
public CategoryIdModel ()
```
Initializes a new instance of the CategoryIdModel class.

---

### Class: `CategoryModel`
**File:** [src/SyntheticShared/Models/CategoryModel.cs](../src/SyntheticShared/Models/CategoryModel.cs)

Model representation of a Revit Category, providing properties and methods to serialize and modify category settings.

#### Properties
- **`ParentCategoryName`**: `public string ParentCategoryName { get; set; }`
  *Description:* Gets or sets the name of the parent category, if applicable.
- **`LineColor`**: `public ColorModel LineColor { get; set; }`
  *Description:* Gets or sets the line color of the category.
- **`Material`**: `public ElementIdModel Material { get; set; }`
  *Description:* Gets or sets the material model of the category.
- **`LinePatternProjection`**: `public ElementIdModel LinePatternProjection { get; set; }`
  *Description:* Gets or sets the projection line pattern.
- **`LinePatternCut`**: `public ElementIdModel LinePatternCut { get; set; }`
  *Description:* Gets or sets the cut line pattern.
- **`CategoryId`**: `public CategoryIdModel CategoryId { get; set; }`
  *Description:* Gets or sets the CategoryIdModel wrapper. Ignored in JSON.

#### Methods
##### `CategoryModel`
```csharp
public CategoryModel() : base()
```
Initializes a new instance of the CategoryModel class.

##### `CategoryModel`
```csharp
public CategoryModel(RevitCategory category, RevitDoc Document, bool IsTemplate)
```
Initializes a new instance of the CategoryModel class using a Revit category, document, and template flag.  <param name="category">The Revit category.</param> <param name="Document">The Revit document.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `GetCategory`
```csharp
public RevitCategory GetCategory(RevitDoc document)
```
Resolves the Category object in the document.

**Parameters:**
- `document`: The Revit document.

**Returns:** The Revit Category if found; otherwise, null.

##### `PopulateParametersList`
```csharp
public void PopulateParametersList(RevitDoc doc)
```
Populates the model parameters list.

**Parameters:**
- `doc`: The Revit document.

##### `SyncFromParameters`
```csharp
public void SyncFromParameters()
```
Synchronizes the model's property values from the internal parameters list.

##### `if`
```csharp
else if (!string.IsNullOrWhiteSpace(pMaterial.Value))
```
##### `if`
```csharp
else if (!string.IsNullOrWhiteSpace(pLinePatternProjection.Value))
```
##### `if`
```csharp
else if (!string.IsNullOrWhiteSpace(pLinePatternCut.Value))
```
##### `if`
```csharp
else if (serialCategory.Id != 0)
```
---

#### Fields
- **`ParentCategoryName`**: `return ParentCategoryName;`
- **`catIdVal`**: `long catIdVal;`
- **`colorStr`**: `string colorStr = "";`
- **`pLineWeightProjection`**: `var pLineWeightProjection = this.Parameters.FirstOrDefault(p => p.Name == "LineWeightProjection");`
- **`pLineWeightCut`**: `var pLineWeightCut = this.Parameters.FirstOrDefault(p => p.Name == "LineWeightCut");`
- **`pLineColor`**: `var pLineColor = this.Parameters.FirstOrDefault(p => p.Name == "LineColor");`
- **`parts`**: `var parts = pLineColor.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);`
- **`pMaterial`**: `var pMaterial = this.Parameters.FirstOrDefault(p => p.Name == "Material");`
- **`pLinePatternProjection`**: `var pLinePatternProjection = this.Parameters.FirstOrDefault(p => p.Name == "LinePatternProjection");`
- **`pLinePatternCut`**: `var pLinePatternCut = this.Parameters.FirstOrDefault(p => p.Name == "LinePatternCut");`
- **`matElem`**: `var matElem = serialCategory.Material.GetElem(document);`
- **`lp`**: `var lp = serialCategory.LinePatternCut.GetElem(document);`
- **`lp`**: `var lp = serialCategory.LinePatternProjection.GetElem(document);`
- **`category`**: `return category;`

---

### Class: `ColorModel`
**File:** [src/SyntheticShared/Models/ColorModel.cs](../src/SyntheticShared/Models/ColorModel.cs)

Model representing a Revit color value (Red, Green, Blue components).

#### Properties
- **`Blue`**: `public Byte Blue { get; set; }`
  *Description:* Gets or sets the Blue component value of the color (0-255).
- **`Green`**: `public Byte Green { get; set; }`
  *Description:* Gets or sets the Green component value of the color (0-255).
- **`Red`**: `public Byte Red { get; set; }`
  *Description:* Gets or sets the Red component value of the color (0-255).
- **`IsValid`**: `public bool IsValid { get; set; }`
  *Description:* Gets or sets a value indicating whether the color is valid.

#### Methods
##### `ColorModel`
```csharp
public ColorModel()
```
Initializes a new instance of the ColorModel class as an invalid color.

##### `ColorModel`
```csharp
public ColorModel (Byte Red, Byte Green, Byte Blue)
```
Initializes a new instance of the ColorModel class with specific Red, Green, and Blue component values.  <param name="Red">Red component (0-255).</param> <param name="Green">Green component (0-255).</param> <param name="Blue">Blue component (0-255).</param>

##### `ColorModel`
```csharp
public ColorModel (RevitDB.Color color)
```
Initializes a new instance of the ColorModel class from a Revit Color object.  <param name="color">The Revit color object.</param>

##### `ByJSON`
```csharp
public static ColorModel ByJSON (string JSON)
```
Deserializes a JSON string into a ColorModel instance.

**Parameters:**
- `JSON`: The JSON string.

**Returns:** A ColorModel instance.

##### `ToJSON`
```csharp
public static string ToJSON (ColorModel color)
```
Serializes a ColorModel instance to a JSON string.

**Parameters:**
- `color`: The color model to serialize.

**Returns:** A JSON string.

---

### Class: `CompoundStructureModel`
**File:** [src/SyntheticShared/Models/CompoundStructureModel.cs](../src/SyntheticShared/Models/CompoundStructureModel.cs)

Model representing a Revit CompoundStructure (e.g. wall layers).

#### Properties
- **`Layers`**: `public List<SerialCompoundStructureLayer> Layers { get; set; }`
  *Description:* Gets or sets the list of layers in the compound structure.
- **`WallSweeps`**: `public List<string> WallSweeps { get; set; }`
  *Description:* Gets or sets the list of wall sweeps in the compound structure.

#### Methods
##### `CompoundStructureModel`
```csharp
public CompoundStructureModel () { }
```
Initializes a new instance of the CompoundStructureModel class.

---

#### Fields
- **`csLayers`**: `IList<RevitCSLayer> csLayers = CompoundStructure.GetLayers();`
- **`structuralIndex`**: `int structuralIndex = CompoundStructure.StructuralMaterialIndex;`
- **`csLayer`**: `RevitCSLayer csLayer = csLayers[i];`
- **`layerModel`**: `var layerModel = new SerialCompoundStructureLayer(csLayer, Document);`
- **`priority`**: `int priority = 0;`
- **`getPriorityMethod`**: `var getPriorityMethod = CompoundStructure.GetType().GetMethod("GetPriority", new Type[] { typeof(int) });`
- **`csLayers`**: `IList<RevitCSLayer> csLayers = new List<RevitCSLayer>();`
- **`structuralIndex`**: `int structuralIndex = -1;`
- **`layer`**: `SerialCompoundStructureLayer layer = this.Layers[i];`
- **`cs`**: `RevitCS cs = RevitCS.CreateSimpleCompoundStructure(csLayers);`
- **`setPriorityMethod`**: `var setPriorityMethod = cs.GetType().GetMethod("SetPriority", new Type[] { typeof(int), typeof(int) });`
- **`cs`**: `return cs;`

---

### Class: `CurtainSystemTypeModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model for a Revit CurtainSystemType, inheriting from HostObjTypeModel.

#### Methods
##### `CurtainSystemTypeModel`
```csharp
public CurtainSystemTypeModel() : base() { }
```
Initializes a new instance of the CurtainSystemTypeModel class.

##### `CurtainSystemTypeModel`
```csharp
public CurtainSystemTypeModel(CurtainSystemType elem, bool isTemplate) : base(elem, isTemplate) { }
```
Initializes a new instance of the CurtainSystemTypeModel class from a Revit CurtainSystemType.  <param name="elem">The Revit CurtainSystemType.</param> <param name="isTemplate">Flag indicating if this model is a template.</param>

---

### Class: `DimensionTypeModel`
**File:** [src/SyntheticShared/Models/DimensionTypeModel.cs](../src/SyntheticShared/Models/DimensionTypeModel.cs)

Serialize and deserialize Revit DimensionTypes

#### Properties
- **`DimensionType`**: `public RevitDimType DimensionType { get; set; }`
  *Description:* Gets or sets the underlying Revit DimensionType object. Ignored in JSON.

#### Methods
##### `DimensionTypeModel`
```csharp
public DimensionTypeModel() : base()
```
Initializes a new instance of the DimensionTypeModel class.

##### `DimensionTypeModel`
```csharp
public DimensionTypeModel(RevitDimType revitElemType, bool IsTemplate) : base(revitElemType, IsTemplate)
```
Initializes a new instance of the DimensionTypeModel class from a Revit DimensionType.  <param name="revitElemType">The Revit DimensionType.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `DimensionTypeModel`
```csharp
public DimensionTypeModel(ElementTypeModel serialElement, bool IsTemplate) : base(serialElement.ElementType, IsTemplate)
```
Initializes a new instance of the DimensionTypeModel class from a base ElementTypeModel.  <param name="serialElement">The element type model source.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

---

#### Fields
- **`template`**: `RevitElemType template;`
- **`assembly`**: `Assembly assembly = typeof(RevitElem).Assembly;`
- **`elemClass`**: `Type elemClass = assembly.GetType(serialDimensionType.Class);`
- **`t`**: `DimensionType t = serialDimensionType.DimensionType;`
- **`d`**: `DimensionStyleType d = serialDimensionType.DimensionStyle;`

---

### Class: `DuplicateClusterModel`
**File:** [src/SyntheticShared/Models/DuplicateClusterModel.cs](../src/SyntheticShared/Models/DuplicateClusterModel.cs)

Model representing a cluster of duplicate elements. Manages the selection of the primary element and details about schema or origin conflicts.

#### Methods
##### `DuplicateClusterModel`
```csharp
public DuplicateClusterModel()
```
Initializes a new instance of the DuplicateClusterModel class.

##### `UpdatePrimaryItem`
```csharp
public void UpdatePrimaryItem(DuplicateItemModel newPrimary)
```
Updates the designated primary item, resetting the primary flag on other items in the cluster.

**Parameters:**
- `newPrimary`: The new primary duplicate item.

##### `SubscribeToItems`
```csharp
private void SubscribeToItems()
```
##### `UnsubscribeFromItems`
```csharp
private void UnsubscribeFromItems()
```
##### `OnItemsCollectionChanged`
```csharp
private void OnItemsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
```
##### `OnItemPropertyChanged`
```csharp
private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
```
##### `OnPropertyChanged`
```csharp
protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
```
Raises the PropertyChanged event.

**Parameters:**
- `propertyName`: The name of the property that changed.

---

#### Fields
- **`_items`**: `private ObservableCollection<DuplicateItemModel> _items;`
- **`_clusterName`**: `private string _clusterName;`
- **`_hasSchemaMismatch`**: `private bool _hasSchemaMismatch;`
- **`_hasOriginMismatch`**: `private bool _hasOriginMismatch;`
- **`_isBlocked`**: `private bool _isBlocked;`
- **`_typeMappings`**: `private ObservableCollection<TypeMappingModel> _typeMappings = new ObservableCollection<TypeMappingModel>();`
- **`true`**: `return true;`

---

### Class: `DuplicateItemModel`
**File:** [src/SyntheticShared/Models/DuplicateItemModel.cs](../src/SyntheticShared/Models/DuplicateItemModel.cs)

Represents the parent container (e.g., the Family or parent element) for duplicates.

#### Methods
##### `DuplicateItemModel`
```csharp
public DuplicateItemModel()
```
Initializes a new instance of the DuplicateItemModel class.

##### `OnPropertyChanged`
```csharp
protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
```
Raises the PropertyChanged event.

**Parameters:**
- `propertyName`: The name of the property that changed.

---

#### Fields
- **`_revitElementId`**: `private ElementId _revitElementId;`
- **`_itemName`**: `private string _itemName;`
- **`_categoryName`**: `private string _categoryName;`
- **`_types`**: `private ObservableCollection<DuplicateTypeModel> _types;`
- **`_isPrimary`**: `private bool _isPrimary;`
- **`_isIncludedForMerge`**: `private bool _isIncludedForMerge = true;`
- **`_boundingBox`**: `private BoundingBoxXYZ _boundingBox;`
- **`_origin`**: `private XYZ _origin;`
- **`_instanceCount`**: `private int _instanceCount;`
- **`_isLoadableFamily`**: `private bool _isLoadableFamily;`
- **`true`**: `return true;`

---

### Class: `DuplicateTypeModel`
**File:** [src/SyntheticShared/Models/DuplicateTypeModel.cs](../src/SyntheticShared/Models/DuplicateTypeModel.cs)

Represents a specific duplicate FamilySymbol, GroupType, or AssemblyType.

#### Methods
##### `DuplicateTypeModel`
```csharp
public DuplicateTypeModel()
```
Initializes a new instance of the DuplicateTypeModel class.

##### `OnPropertyChanged`
```csharp
protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
```
Raises the PropertyChanged event.

**Parameters:**
- `propertyName`: The name of the property that changed.

---

#### Fields
- **`_revitTypeId`**: `private ElementId _revitTypeId;`
- **`_name`**: `private string _name;`
- **`true`**: `return true;`

---

### Class: `ElementIdModel`
**File:** [src/SyntheticShared/Models/ElementIdModel.cs](../src/SyntheticShared/Models/ElementIdModel.cs)

Model that acts as a wrapper for Revit ElementIds to facilitate JSON serialization. Supports resolving element instances by UniqueId, Id, Name, or Aliases.

#### Properties
- **`Class`**: `public string Class { get; set; }`
  *Description:* Gets or sets the class name of the referenced Revit element.
- **`Category`**: `public string Category { get; set; }`
  *Description:* Gets or sets the category name of the referenced Revit element.
- **`Name`**: `public string Name { get; set; }`
  *Description:* Name of the element the Id belongs too.
- **`Aliases`**: `public List<string> Aliases { get; set; }`
  *Description:* Gets or sets the list of name aliases to search if the primary Name is not found.
- **`Id`**: `public long Id { get; set; }`
  *Description:* Value of the Element Id as an int
- **`UniqueId`**: `public string UniqueId { get; set; }`
  *Description:* Gets or sets the unique string identifier of the element.
- **`IsTemplate`**: `public bool IsTemplate { get; set; }`
  *Description:* If true, SerialElement is intended to be deserialized as a template for use as standards or transfer to another project. If false, SerialElement is intended to modify an element inside the project and will include ElementIds and UniqueIds.

#### Methods
##### `ShouldSerializeId`
```csharp
public bool ShouldSerializeId()
```
If IsTemplate, don't serialize the Element Id

**Returns:** True if not a template

##### `ShouldSerializeUniqueId`
```csharp
public bool ShouldSerializeUniqueId()
```
If IsTemplate, don't serialize the Unqiue Id

**Returns:** True if not a template

##### `ElementIdModel`
```csharp
public ElementIdModel ()
```
Initializes a new instance of the ElementIdModel class.

##### `ElementIdModel`
```csharp
public ElementIdModel(bool IsTemplate)
```
Initializes a new instance of the ElementIdModel class.  <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `ElementIdModel`
```csharp
public ElementIdModel (string Name, int ElementId, string Class, string Category)
```
Initializes a new instance of the ElementIdModel class.  <param name="Name">Name of the element.</param> <param name="ElementId">Revit element ID integer.</param> <param name="Class">Class name of the element.</param> <param name="Category">Category name of the element.</param>

##### `ByJSON`
```csharp
public static ElementIdModel ByJSON (string JSON)
```
Deserializes a JSON string into an ElementIdModel instance.

**Parameters:**
- `JSON`: The JSON string.

**Returns:** An ElementIdModel instance.

##### `ToJSON`
```csharp
public static string ToJSON (ElementIdModel IdJSON)
```
Serializes an ElementIdModel instance to a JSON string.

**Parameters:**
- `IdJSON`: The instance to serialize.

**Returns:** A JSON string.

##### `GetElem`
```csharp
public RevitElem GetElem (RevitDoc document)
```
Try to select the element referenced in the ElementIdModel by first trying UniqueId, then Id, then Name, then Aliases of the name.

**Parameters:**
- `document`: A revit document

**Returns:** Returns a Revit Element

##### `ToElementId`
```csharp
public RevitElemId ToElementId ()
```
Converts the ElementIdModel representation back to a Revit ElementId.

**Returns:** A Revit ElementId object.

---

#### Fields
- **`elem`**: `RevitElem elem = Document.GetElement(Id);`
- **`elem`**: `RevitElem elem = null;`
- **`assembly`**: `Assembly assembly = typeof(RevitElem).Assembly;`
- **`elemClass`**: `Type elemClass = assembly.GetType(this.Class);`
- **`aliasElem`**: `List<RevitElem> aliasElem = new List<RevitElem>();`
- **`assembly`**: `Assembly assembly = typeof(RevitElem).Assembly;`
- **`elemClass`**: `Type elemClass = assembly.GetType(this.Class);`
- **`elem`**: `return elem;`

---

### Class: `ElementModel`
**File:** [src/SyntheticShared/Models/ElementModel.cs](../src/SyntheticShared/Models/ElementModel.cs)

Base model representing a Revit Element, providing properties and methods for serialization, parameter management, and modification.

#### Properties
- **`Parameters`**: `public List<ParameterModel> Parameters { get; set; }`
  *Description:* List of the Parameters that belong to the Element.
- **`ElementId`**: `public ElementIdModel ElementId { get; set; }`
  *Description:* The Revit ElementId of the element linked to the SerialElement.
- **`Element`**: `virtual public RevitElem Element { get; set; }`
  *Description:* The Revit Element that is linked to the SerialElement.
- **`Document`**: `public RevitDoc Document { get; set; }`
  *Description:* The Revit Document the element belongs too.
- **`IsTemplate`**: `public bool IsTemplate { get; set; }`
  *Description:* If true, SerialElement is intended to be deserialized as a template for use as standards or transfer to another project. If false, SerialElement is intended to modify an element inside the project and will include ElementIds and UniqueIds.

#### Methods
##### `ShouldSerializeId`
```csharp
public bool ShouldSerializeId()
```
If IsTemplate, don't serialize the Element Id

**Returns:** True if not a template

##### `ShouldSerializeUniqueId`
```csharp
public bool ShouldSerializeUniqueId()
```
If IsTemplate, don't serialize the Unqiue Id

**Returns:** True if not a template

##### `ElementModel`
```csharp
public ElementModel()
```
Initializes a new instance of the ElementModel class.

##### `ElementModel`
```csharp
public ElementModel(RevitElem revitElement, bool IsTemplate)
```
Initializes a new instance of the ElementModel class from a Revit element.  <param name="revitElement">The Revit element.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `_ByElement`
```csharp
private void _ByElement (RevitElem elem, bool IsTemplate)
```
##### `GetRevitElem`
```csharp
public RevitElem GetRevitElem ( RevitDoc document)
```
Resolves and returns the Revit Element instance referenced by this model.

**Parameters:**
- `document`: The Revit document.

**Returns:** The Revit Element if found; otherwise, null.

##### `GetAliasElements_Revit`
```csharp
public List<RevitElem> GetAliasElements_Revit ( RevitDoc document)
```
Resolves and returns a list of Revit elements corresponding to the aliases defined in this model.

**Parameters:**
- `document`: The Revit document.

**Returns:** A list of matching Revit Elements.

##### `SetAliases`
```csharp
public static ElementModel SetAliases (ElementModel serialElement, List<string> aliases)
```
Sets the SerialElement's aliases given a list of strings.

**Parameters:**
- `serialElement`: A SerialElement
- `aliases`: A list of strings representing name aliases.

**Returns:** Returns the modified SerialElement

##### `ModifyElement`
```csharp
public static RevitElem ModifyElement(ElementModel serialElement,  RevitDoc document)
```
Modifies or applies serialized properties onto a Revit Element in the document.

**Parameters:**
- `serialElement`: The element model containing updates.
- `document`: The Revit document.

**Returns:** The modified Revit Element.

##### `ToString`
```csharp
public override string ToString()
```
Returns a string representation of the ElementModel.

**Returns:** A string detailing the model name and ID.

##### `ByJSON`
```csharp
public static ElementModel ByJSON(string JSON)
```
Deserializes a JSON string into an ElementModel instance.

**Parameters:**
- `JSON`: The JSON string.

**Returns:** An ElementModel instance.

##### `ToJSON`
```csharp
public static string ToJSON(ElementModel serialElement)
```
Serializes an ElementModel instance to a JSON string.

**Parameters:**
- `serialElement`: The instance to serialize.

**Returns:** A JSON string.

##### `_ModifyProperties`
```csharp
virtual protected void _ModifyProperties (RevitElem elem)
```
Virtual method to modify properties of the Revit element.

**Parameters:**
- `elem`: The target Revit Element.

---

#### Fields
- **`elements`**: `List<RevitElem> elements = new List<RevitElem>();`
- **`assembly`**: `Assembly assembly = typeof(RevitElem).Assembly;`
- **`elemClass`**: `Type elemClass = assembly.GetType(this.Class);`
- **`elem`**: `RevitElem elem = Select.ElementByNameClass(aliasName, elemClass, document);`
- **`elements`**: `return elements;`
- **`serialElement`**: `return serialElement;`
- **`transactionName`**: `string transactionName = "Modify Element from Serialization";`

---

### Class: `ElementTypeModel`
**File:** [src/SyntheticShared/Models/ElementTypeModel.cs](../src/SyntheticShared/Models/ElementTypeModel.cs)

Model representation of a Revit ElementType, subclass of ElementModel.

#### Properties
- **`ElementType`**: `public RevitElemType ElementType { get; set; }`
  *Description:* Gets or sets the underlying Revit ElementType object. Ignored in JSON.

#### Methods
##### `ElementTypeModel`
```csharp
public ElementTypeModel () : base () { }
```
Initializes a new instance of the ElementTypeModel class.

##### `ElementTypeModel`
```csharp
public ElementTypeModel (RevitElemType revitElemType, bool IsTemplate) : base (revitElemType, IsTemplate) { }
```
Initializes a new instance of the ElementTypeModel class from a Revit ElementType.  <param name="revitElemType">The Revit ElementType.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `ElementTypeModel`
```csharp
public ElementTypeModel (ElementModel serialElement, bool IsTemplate) : base (serialElement.Element, IsTemplate) { }
```
Initializes a new instance of the ElementTypeModel class from a base ElementModel.  <param name="serialElement">The base element model source.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `CreateElementTypeByTemplate`
```csharp
return CreateElementTypeByTemplate(serialElementType, template, document);
```
Duplicates a template ElementType and modifies it with serialized properties from the ElementTypeModel.

**Parameters:**
- `serialElementType`: The element type model input.
- `templateElemType`: The template ElementType to duplicate from.
- `document`: The Revit document.

**Returns:** The created or updated Revit ElementType.

---

#### Fields
- **`transactionName`**: `string transactionName = "Duplicate Template Element Type";`
- **`dElem`**: `RevitElemType dElem = null;`
- **`newType`**: `RevitElemType newType = null;`
- **`foundElem`**: `var foundElem = serialElementType.GetRevitElem(document);`
- **`newSerial`**: `ElementTypeModel newSerial = (ElementTypeModel)serialElementType.MemberwiseClone();`
- **`dElem`**: `return dElem;`
- **`template`**: `RevitElemType template;`
- **`assembly`**: `Assembly assembly = typeof(RevitElem).Assembly;`
- **`elemClass`**: `Type elemClass = assembly.GetType(serialElementType.Class);`
- **`elementsMerged`**: `List<RevitElem> elementsMerged = new List<RevitElem>();`
- **`elementsFailed`**: `List<RevitElem> elementsFailed = new List<RevitElem>();`
- **`elemType`**: `RevitElemType elemType = null;`
- **`aliasTypes`**: `List<RevitElem> aliasTypes = serialElementType.GetAliasElements_Revit(document);`
- **`foundElem`**: `var foundElem = serialElementType.GetRevitElem(document);`
- **`m`**: `List<RevitElem> m = (List<RevitElem>)r["Merged"];`
- **`f`**: `List<RevitElem> f = (List<RevitElem>)r["Failed"];`

---

### Class: `ElementsOnWorkset`
**File:** [src/SyntheticShared/Models/ElementsOnWorkset.cs](../src/SyntheticShared/Models/ElementsOnWorkset.cs)

Model that tracks elements associated with a specific workset. Used for exporting elements on a workset and recreating them back.

#### Methods
##### `ElementsOnWorkset`
```csharp
public ElementsOnWorkset()
```
Initializes a new instance of the ElementsOnWorkset class.

##### `ElementsOnWorkset`
```csharp
public ElementsOnWorkset(Workset workset, Document document)
```
Initializes a new instance of the ElementsOnWorkset class from a Revit workset and document.  <param name="workset">The Revit workset.</param> <param name="document">The Revit document.</param>

##### `ElementsOnWorkset`
```csharp
public ElementsOnWorkset(string worksetName, Guid worksetUniqueId, List<string> elementUniqueIds)
```
Initializes a new instance of the ElementsOnWorkset class with specific properties.  <param name="worksetName">Name of the workset.</param> <param name="worksetUniqueId">Unique ID of the workset.</param> <param name="elementUniqueIds">List of element unique IDs.</param>

##### `Add`
```csharp
public ElementsOnWorkset Add (string UniqueId)
```
Add a new element's UniqueId to the collection.

**Parameters:**
- `UniqueId`: A string representing the UniqueId of the element

**Returns:** This ElementsOnWorkset object for chaining commands.

##### `MoveToWorkset`
```csharp
public List<Element> MoveToWorkset(Document document)
```
Moves the elements with UniqueIds in the ElementsOnWorkset object to the Workset.  If the workset doesn't exist, it will be created.

**Parameters:**
- `document`: A Revit Document

**Returns:** The Revit Elements with UniqueIds that were moved.

##### `ToJSON`
```csharp
public string ToJSON()
```
Serializes the object to JSON

**Returns:** A JSON string

##### `ByJSON`
```csharp
public static ElementsOnWorkset ByJSON(string JSON)
```
Deserializes the object from a JSON string.

**Parameters:**
- `JSON`: A string of JSON

**Returns:** The deserializedd object

##### `IfLog`
```csharp
public bool IfLog()
```
Checks if there are any results to log

**Returns:** True if there are errors or results to log.

##### `GetLog`
```csharp
public string GetLog()
```
Complies the log of results

**Returns:** A json string of the Log

---

#### Fields
- **`WorksetName`**: `public string WorksetName;`
  *Description:* Gets or sets the name of the workset.
- **`ElementUniqueIds`**: `public List<string> ElementUniqueIds;`
  *Description:* Gets or sets the list of unique element IDs.
- **`Results`**: `internal List<string> Results;`
- **`Errors`**: `internal List<string> Errors;`
- **`elements`**: `IList<Element> elements = WorksetUtil.GetElementsOnWorkset(workset, document);`
- **`this`**: `return this;`
- **`elements`**: `List<Element> elements = null;`
- **`workset`**: `Workset workset = document.GetWorksetTable().GetWorkset(this.WorksetUniqueId);`
- **`element`**: `Element element = document.GetElement(uniqueId);`
- **`elements`**: `return elements;`
- **`true`**: `return true;`

---

### Class: `EnumModel`
**File:** [src/SyntheticShared/Models/EnumModel.cs](../src/SyntheticShared/Models/EnumModel.cs)

Represents a serialized Enum value with its type information.

#### Properties
- **`Type`**: `public string Type { get; set; }`
  *Description:* Gets or sets the full name of the Enum type.
- **`Value`**: `public string Value { get; set; }`
  *Description:* Gets or sets the string value of the Enum.

#### Methods
##### `EnumModel`
```csharp
public EnumModel () { }
```
Initializes a new instance of the EnumModel class.

##### `EnumModel`
```csharp
public EnumModel (Type type, Enum value)
```
Initializes a new instance of the EnumModel class using the specified type and value.  <param name="type">The Type of the Enum.</param> <param name="value">The Enum value.</param>

##### `EnumModel`
```csharp
public EnumModel (string type, string value)
```
Initializes a new instance of the EnumModel class using the type name and value string.  <param name="type">The full name of the Enum type.</param> <param name="value">The Enum value as a string.</param>

##### `ToEnum`
```csharp
public Enum ToEnum()
```
Converts the EnumModel back to an Enum instance.

**Returns:** The deserialized Enum instance.

---

### Class: `FasciaTypeModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model for a Revit FasciaType, inheriting from ElementTypeModel.

#### Methods
##### `FasciaTypeModel`
```csharp
public FasciaTypeModel() : base() { }
```
Initializes a new instance of the FasciaTypeModel class.

##### `FasciaTypeModel`
```csharp
public FasciaTypeModel(FasciaType elem, bool isTemplate) : base(elem, isTemplate) { }
```
Initializes a new instance of the FasciaTypeModel class from a Revit FasciaType.  <param name="elem">The Revit FasciaType.</param> <param name="isTemplate">Flag indicating if this model is a template.</param>

---

### Class: `FillGridModel`
**File:** [src/SyntheticShared/Models/FillGridModel.cs](../src/SyntheticShared/Models/FillGridModel.cs)

Represents a model for a Revit FillGrid.

#### Properties
- **`Angle`**: `public Double Angle { get; set; }`
  *Description:* Gets or sets the angle of the fill grid lines.
- **`Offset`**: `public Double Offset { get; set; }`
  *Description:* Gets or sets the offset of the fill grid lines.
- **`Origin`**: `public UVModel Origin { get; set; }`
  *Description:* Gets or sets the origin point of the fill grid.
- **`Shift`**: `public Double Shift { get; set; }`
  *Description:* Gets or sets the shift of the fill grid lines.

#### Methods
##### `FillGridModel`
```csharp
public FillGridModel() { }
```
Initializes a new instance of the FillGridModel class.

##### `FillGridModel`
```csharp
public FillGridModel (Double Angle, Double Offset, UVModel Origin, Double Shift)
```
Initializes a new instance of the FillGridModel class with specified angle, offset, origin, and shift.  <param name="Angle">The angle of the lines.</param> <param name="Offset">The offset between lines.</param> <param name="Origin">The origin point.</param> <param name="Shift">The shift along the lines.</param>

##### `FillGridModel`
```csharp
public FillGridModel (RevitDB.FillGrid fillGrid)
```
Initializes a new instance of the FillGridModel class from a Revit FillGrid object.  <param name="fillGrid">The Revit FillGrid.</param>

##### `ByJSON`
```csharp
public static FillGridModel ByJSON (string JSON)
```
Deserializes a FillGridModel from a JSON string.

**Parameters:**
- `JSON`: The JSON representation.

**Returns:** A FillGridModel instance.

##### `ToJSON`
```csharp
public static string ToJSON (FillGridModel color)
```
Serializes a FillGridModel to a JSON string.

**Parameters:**
- `color`: The FillGridModel to serialize.

**Returns:** A JSON string.

---

#### Fields
- **`fillGrid`**: `return fillGrid;`

---

### Class: `FillPatternElementModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model for a Revit FillPatternElement, inheriting from ElementModel.

#### Properties
- **`Pattern`**: `public FillPatternModel Pattern { get; set; }`
  *Description:* Gets or sets the fill pattern model settings.

#### Methods
##### `FillPatternElementModel`
```csharp
public FillPatternElementModel() : base() { }
```
Initializes a new instance of the FillPatternElementModel class.

##### `FillPatternElementModel`
```csharp
public FillPatternElementModel(FillPatternElement elem, bool isTemplate) : base(elem, isTemplate)
```
Initializes a new instance of the FillPatternElementModel class from a Revit FillPatternElement.  <param name="elem">The Revit FillPatternElement.</param> <param name="isTemplate">Flag indicating if this model is a template.</param>

##### `ModifyFillPatternElement`
```csharp
public static FillPatternElement ModifyFillPatternElement(FillPatternElementModel model, Document doc)
```
Modifies or creates a Revit FillPatternElement in the document using properties of this model.

**Parameters:**
- `model`: The FillPatternElementModel source.
- `doc`: The Revit document.

**Returns:** The modified or created Revit FillPatternElement.

---

#### Fields
- **`fp`**: `var fp = elem.GetFillPattern();`
- **`elem`**: `FillPatternElement elem = (FillPatternElement)model.GetRevitElem(doc);`
- **`elem`**: `return elem;`

---

### Class: `FillPatternModel`
**File:** [src/SyntheticShared/Models/FillPatternModel.cs](../src/SyntheticShared/Models/FillPatternModel.cs)

Represents a model for a Revit FillPattern.

#### Properties
- **`Name`**: `public string Name { get; set; }`
  *Description:* Name of the Fill Pattern
- **`Target`**: `public FillPatternTarget Target { get; set; }`
  *Description:* Enum reprsenting Drafting or Model patterns
- **`HostOrientation`**: `public FillPatternHostOrientation HostOrientation { get; set; }`
  *Description:* Enum representing the orientation of the pattern relative the host or world.
- **`FillGrids`**: `public List<FillGridModel> FillGrids {  get; set; }`
  *Description:* List of Fillgrid objects

#### Methods
##### `FillPatternModel`
```csharp
public FillPatternModel() { }
```
Constructor for an empty object

##### `FillPatternModel`
```csharp
public FillPatternModel (string name, FillPatternTarget target, FillPatternHostOrientation orientation, List<FillGridModel> fillgrids)
```
Constructor that takes the Name, Target, Orientation and Fillgrids  <param name="name">Name of the Filled Region</param> <param name="target">Model or Drafting type of Filled Region</param> <param name="orientation">Orientation of the Filled Region</param> <param name="fillgrids">List of FillgridModels</param>

##### `FillPatternModel`
```csharp
public FillPatternModel (RevitDB.FillPattern pattern)
```
Constructor from a Revit FillPattern element.  <param name="pattern">A Revit FillPattern element</param>

##### `ByJSON`
```csharp
public static FillPatternModel ByJSON (string JSON)
```
Create a new FillPatternModel by deserializing a JSON string

**Parameters:**
- `JSON`: A JSON string representing the FillPatternModel

**Returns:** A FillPatternModel

##### `ToJSON`
```csharp
public static string ToJSON (FillPatternModel fillPatternModel)
```
Serializes a FillPatternModel to a JSON string.

**Parameters:**
- `fillPatternModel`: A FillPatternModel

**Returns:** A JSON string

---

#### Fields
- **`FillGrids`**: `List<FillGridModel> FillGrids = new List<FillGridModel>();`
- **`fillgrids`**: `IList<FillGrid> fillgrids = new List<FillGrid>();`
- **`fillPattern`**: `return fillPattern;`
- **`fillPatternElement`**: `return fillPatternElement;`

---

### Class: `FilledRegionTypeModel`
**File:** [src/SyntheticShared/Models/FilledRegionTypeModel.cs](../src/SyntheticShared/Models/FilledRegionTypeModel.cs)

Represents a model for Revit FilledRegionType, inheriting from ElementTypeModel.

#### Properties
- **`FilledRegionType`**: `public RevitFilledRegionType FilledRegionType { get; set; }`
  *Description:* Gets or sets the Revit FilledRegionType associated with this model. Ignored in JSON.
- **`IsMasking`**: `public bool IsMasking { get; set; }`
  *Description:* Gets or sets a value indicating whether the filled region is masking.
- **`LineWeight`**: `public int LineWeight { get; set; }`
  *Description:* Gets or sets the line weight.
- **`BackgroundPatternColor`**: `public ColorModel BackgroundPatternColor { get; set; }`
  *Description:* Gets or sets the background pattern color.
- **`BackgroundPattern`**: `public FillPatternModel BackgroundPattern {  get; set; }`
  *Description:* Gets or sets the background fill pattern.
- **`ForegroundPatternColor`**: `public ColorModel ForegroundPatternColor { get; set; }`
  *Description:* Gets or sets the foreground pattern color.
- **`ForegroundPattern`**: `public FillPatternModel ForegroundPattern { get; set; }`
  *Description:* Gets or sets the foreground fill pattern.

#### Methods
##### `FilledRegionTypeModel`
```csharp
public FilledRegionTypeModel () : base () { }
```
Initializes a new instance of the FilledRegionTypeModel class.

##### `FilledRegionTypeModel`
```csharp
public FilledRegionTypeModel (RevitFilledRegionType regionType, bool IsTemplate) : base (regionType, IsTemplate)
```
Initializes a new instance of the FilledRegionTypeModel class from a Revit FilledRegionType.  <param name="regionType">The Revit FilledRegionType.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `FilledRegionTypeModel`
```csharp
public FilledRegionTypeModel (ElementTypeModel serialElementType, bool IsTemplate) : base (serialElementType.ElementType, IsTemplate)
```
Initializes a new instance of the FilledRegionTypeModel class from an ElementTypeModel.  <param name="serialElementType">The ElementTypeModel source.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `_ApplyProperties`
```csharp
private void _ApplyProperties(RevitFilledRegionType regionType, RevitDoc document)
```
---

#### Fields
- **`document`**: `RevitDoc document = regionType.Document;`
- **`document`**: `RevitDoc document = serialElementType.Document;`
- **`filledRegionType`**: `return filledRegionType;`

---

### Class: `GridTypeModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model for a Revit GridType, inheriting from ElementTypeModel.

#### Methods
##### `GridTypeModel`
```csharp
public GridTypeModel() : base() { }
```
Initializes a new instance of the GridTypeModel class.

##### `GridTypeModel`
```csharp
public GridTypeModel(GridType elem, bool isTemplate) : base(elem, isTemplate) { }
```
Initializes a new instance of the GridTypeModel class from a Revit GridType.  <param name="elem">The Revit GridType.</param> <param name="isTemplate">Flag indicating if this model is a template.</param>

---

### Class: `GutterTypeModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model for a Revit GutterType, inheriting from ElementTypeModel.

#### Methods
##### `GutterTypeModel`
```csharp
public GutterTypeModel() : base() { }
```
Initializes a new instance of the GutterTypeModel class.

##### `GutterTypeModel`
```csharp
public GutterTypeModel(GutterType elem, bool isTemplate) : base(elem, isTemplate) { }
```
Initializes a new instance of the GutterTypeModel class from a Revit GutterType.  <param name="elem">The Revit GutterType.</param> <param name="isTemplate">Flag indicating if this model is a template.</param>

---

### Class: `HostObjTypeModel`
**File:** [src/SyntheticShared/Models/HostObjTypeModel.cs](../src/SyntheticShared/Models/HostObjTypeModel.cs)

Represents a model for a Revit HostObjAttributes (e.g. WallType), inheriting from ElementTypeModel.

#### Properties
- **`Function`**: `public EnumModel Function { get; set; }`
  *Description:* Gets or sets the functional classification of the host object type.
- **`Structure`**: `public CompoundStructureModel Structure { get; set; }`
  *Description:* Gets or sets the compound structure of the host object type.
- **`WallType`**: `public RevitHostObjType WallType { get; set; }`
  *Description:* Gets or sets the Revit WallType / HostObjAttributes associated with this model. Ignored in JSON.

#### Methods
##### `HostObjTypeModel`
```csharp
public HostObjTypeModel () : base () { }
```
Initializes a new instance of the HostObjTypeModel class.

##### `HostObjTypeModel`
```csharp
public HostObjTypeModel (RevitHostObjType wallType, bool IsTemplate) : base (wallType, IsTemplate)
```
Initializes a new instance of the HostObjTypeModel class from a Revit HostObjAttributes.  <param name="wallType">The Revit HostObjAttributes.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `HostObjTypeModel`
```csharp
public HostObjTypeModel(ElementTypeModel serialElementType, bool IsTemplate) : base (serialElementType.ElementType, IsTemplate)
```
Initializes a new instance of the HostObjTypeModel class from an ElementTypeModel.  <param name="serialElementType">The ElementTypeModel source.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `_ApplyProperties`
```csharp
private void _ApplyProperties (RevitHostObjType wallType, RevitDoc document)
```
##### `_ModifyProperties`
```csharp
protected override void _ModifyProperties(RevitDB.Element elem)
```
Modifies properties of the specified Revit element with values from this model.

**Parameters:**
- `elem`: The Revit element to modify.

---

#### Fields
- **`document`**: `RevitDoc document = wallType.Document;`
- **`document`**: `RevitDoc document = serialElementType.Document;`
- **`dElem`**: `RevitHostObjType dElem = null;`
- **`newType`**: `RevitHostObjType newType = (RevitHostObjType)serialWallType.GetRevitElem(document);`
- **`newSerial`**: `HostObjTypeModel newSerial = (HostObjTypeModel)serialWallType.MemberwiseClone();`
- **`dElem`**: `return dElem;`
- **`dElem`**: `RevitHostObjType dElem = null;`
- **`dElem`**: `return dElem;`

---

### Class: `ImportLogItem`
**File:** [src/SyntheticShared/Models/ImportLogItem.cs](../src/SyntheticShared/Models/ImportLogItem.cs)

Represents an entry in the import log, tracking actions taken on Revit elements.

#### Properties
- **`Action`**: `public string Action { get; set; }`
  *Description:* Gets or sets the action performed (e.g. Created, Updated, Renamed, Failed, Canceled).
- **`Class`**: `public string Class { get; set; }`
  *Description:* Gets or sets the class name of the Revit element.
- **`ElementName`**: `public string ElementName { get; set; }`
  *Description:* Gets or sets the name of the element.
- **`Message`**: `public string Message { get; set; }`
  *Description:* Gets or sets any details or error messages associated with the action.

### Class: `LevelTypeModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model for a Revit LevelType, inheriting from ElementTypeModel.

#### Methods
##### `LevelTypeModel`
```csharp
public LevelTypeModel() : base() { }
```
Initializes a new instance of the LevelTypeModel class.

##### `LevelTypeModel`
```csharp
public LevelTypeModel(LevelType elem, bool isTemplate) : base(elem, isTemplate) { }
```
Initializes a new instance of the LevelTypeModel class from a Revit LevelType.  <param name="elem">The Revit LevelType.</param> <param name="isTemplate">Flag indicating if this model is a template.</param>

---

### Class: `LinePatternElementModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model for a Revit LinePatternElement, inheriting from ElementModel.

#### Properties
- **`Segments`**: `public List<LinePatternSegmentModel> Segments { get; set; }`
  *Description:* Gets or sets the list of segments defining the line pattern.

#### Methods
##### `LinePatternElementModel`
```csharp
public LinePatternElementModel() : base() { }
```
Initializes a new instance of the LinePatternElementModel class.

##### `LinePatternElementModel`
```csharp
public LinePatternElementModel(LinePatternElement elem, bool isTemplate) : base(elem, isTemplate)
```
Initializes a new instance of the LinePatternElementModel class from a Revit LinePatternElement.  <param name="elem">The Revit LinePatternElement.</param> <param name="isTemplate">Flag indicating if this model is a template.</param>

##### `ModifyLinePatternElement`
```csharp
public static LinePatternElement ModifyLinePatternElement(LinePatternElementModel model, Document doc)
```
Modifies or creates a Revit LinePatternElement in the document using properties of this model.

**Parameters:**
- `model`: The LinePatternElementModel source.
- `doc`: The Revit document.

**Returns:** The modified or created Revit LinePatternElement.

---

#### Fields
- **`lp`**: `var lp = elem.GetLinePattern();`
- **`elem`**: `LinePatternElement elem = (LinePatternElement)model.GetRevitElem(doc);`
- **`lp`**: `var lp = new LinePattern(model.Name);`
- **`lp`**: `var lp = new LinePattern(model.Name);`
- **`elem`**: `return elem;`

---

### Class: `LinePatternSegmentModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model segment for a Revit LinePattern.

#### Properties
- **`Type`**: `public string Type { get; set; }`
  *Description:* Gets or sets the segment type as a string (e.g. Dash, Space, Dot).
- **`Length`**: `public double Length { get; set; }`
  *Description:* Gets or sets the length of the segment.

### Class: `ListMaterialModel`
**File:** [src/SyntheticShared/Models/ListModel.cs](../src/SyntheticShared/Models/ListModel.cs)

Represents a serialized list of MaterialModel objects, inheriting from ListModel.

#### Methods
##### `ListMaterialModel`
```csharp
public ListMaterialModel(List<MaterialModel> MaterialJSONs) : base()
```
##### `ListMaterialModel`
```csharp
public ListMaterialModel(List<revitMaterial> materials, bool IsTemplate)
```
Initializes a new instance of the ListMaterialModel class by converting a list of Revit materials.  <param name="materials">The list of Revit materials.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `ListMaterialModel`
```csharp
public ListMaterialModel()
```
Initializes a new instance of the ListMaterialModel class.

##### `ToString`
```csharp
public override string ToString()
```
Returns a string representation of the ListMaterialModel.

**Returns:** A string displaying class name and material count.

---

#### Fields
- **`materials`**: `ListMaterialModel materials = JsonConvert.DeserializeObject<ListMaterialModel>(JSON);`
- **`materials`**: `return materials;`
- **`elems`**: `List<revitElem> elems = new List<revitElem>();`
- **`elems`**: `return elems;`

---

### Class: `ListModel`
**File:** [src/SyntheticShared/Models/ListModel.cs](../src/SyntheticShared/Models/ListModel.cs)

Represents a serialized list of ElementModel objects.

#### Properties
- **`Elements`**: `public List<ElementModel> Elements { get; set; }`
  *Description:* Gets or sets the list of element models.

#### Methods
##### `ListModel`
```csharp
public ListModel()
```
Initializes a new instance of the ListModel class.

##### `ListModel`
```csharp
public ListModel(List<ElementModel> ElementJSONs)
```
Initializes a new instance of the ListModel class with the specified list of element models.  <param name="ElementJSONs">The list of element models.</param>

##### `ListModel`
```csharp
public ListModel(List<revitElem> elements, bool IsTemplate)
```
Initializes a new instance of the ListModel class by converting a list of Revit elements.  <param name="elements">The list of Revit elements.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `ByJSON`
```csharp
public static ListModel ByJSON(string JSON)
```
Deserializes a ListModel from a JSON string.

**Parameters:**
- `JSON`: The JSON string representation.

**Returns:** A ListModel instance.

##### `ToJSON`
```csharp
public static string ToJSON(ListModel ListJSON)
```
Serializes a ListModel to a JSON string.

**Parameters:**
- `ListJSON`: The ListModel to serialize.

**Returns:** A JSON string representation.

##### `ModifyElements`
```csharp
public static List<revitElem> ModifyElements(ListModel ListJSON, RevitDoc doc)
```
Modifies the Revit elements in the document using properties of the element models in the list.

**Parameters:**
- `ListJSON`: The ListModel containing the element models.
- `doc`: The Revit document.

**Returns:** A list of modified Revit elements.

##### `ToString`
```csharp
public override string ToString()
```
Returns a string representation of the ListModel.

**Returns:** A string displaying class name and element count.

---

#### Fields
- **`elems`**: `List<revitElem> elems = new List<revitElem>();`
- **`elems`**: `return elems;`

---

### Class: `ListStringModel`
**File:** [src/SyntheticShared/Models/ListModel.cs](../src/SyntheticShared/Models/ListModel.cs)

Represents a serialized list of string values.

#### Properties
- **`Elements`**: `public List<string> Elements { get; set; }`
  *Description:* Gets or sets the list of strings.

#### Methods
##### `ListStringModel`
```csharp
public ListStringModel () {}
```
Initializes a new instance of the ListStringModel class.

---

### Class: `MaterialModel`
**File:** [src/SyntheticShared/Models/MaterialModel.cs](../src/SyntheticShared/Models/MaterialModel.cs)

Represents a model for a Revit Material, inheriting from ElementModel.

#### Properties
- **`AppearanceAssetId`**: `public ElementIdModel AppearanceAssetId { get; set; }`
  *Description:* Gets or sets the appearance asset element ID model.
- **`CutForegroundPatternColor`**: `public ColorModel CutForegroundPatternColor { get; set; }`
  *Description:* Gets or sets the cut foreground pattern color.
- **`CutForegroundPatternId`**: `public ElementIdModel CutForegroundPatternId { get; set; }`
  *Description:* Gets or sets the cut foreground pattern element ID model.
- **`CutBackgroundPatternColor`**: `public ColorModel CutBackgroundPatternColor { get; set; }`
  *Description:* Gets or sets the cut background pattern color.
- **`CutBackgroundPatternId`**: `public ElementIdModel CutBackgroundPatternId { get; set; }`
  *Description:* Gets or sets the cut background pattern element ID model.
- **`SurfaceForegroundPatternColor`**: `public ColorModel SurfaceForegroundPatternColor { get; set; }`
  *Description:* Gets or sets the surface foreground pattern color.
- **`SurfaceForegroundPatternId`**: `public ElementIdModel SurfaceForegroundPatternId { get; set; }`
  *Description:* Gets or sets the surface foreground pattern element ID model.
- **`SurfaceBackgroundPatternColor`**: `public ColorModel SurfaceBackgroundPatternColor { get; set; }`
  *Description:* Gets or sets the surface background pattern color.
- **`SurfaceBackgroundPatternId`**: `public ElementIdModel SurfaceBackgroundPatternId { get; set; }`
  *Description:* Gets or sets the surface background pattern element ID model.
- **`Material`**: `public RevitMaterial Material { get; set; }`
  *Description:* Gets or sets the Revit Material object. Ignored in JSON.

#### Methods
##### `MaterialModel`
```csharp
public MaterialModel () : base () { }
```
Initializes a new instance of the MaterialModel class.

##### `MaterialModel`
```csharp
public MaterialModel (RevitMaterial revitMaterial, bool IsTemplate) : base (revitMaterial, IsTemplate)
```
Initializes a new instance of the MaterialModel class from a Revit Material.  <param name="revitMaterial">The Revit Material.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `MaterialModel`
```csharp
public MaterialModel (ElementModel serialElement, bool IsTemplate) : base (serialElement.Element, IsTemplate)
```
Initializes a new instance of the MaterialModel class from an ElementModel.  <param name="serialElement">The ElementModel source.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `_ApplyProperties`
```csharp
private void _ApplyProperties(RevitMaterial material)
```
##### `_ApplyDocProperties`
```csharp
private void _ApplyDocProperties(RevitMaterial material, RevitDoc document)
```
##### `ToJSON`
```csharp
public static string ToJSON(MaterialModel materialJSON)
```
Serializes a MaterialModel to a JSON string.

**Parameters:**
- `materialJSON`: The MaterialModel to serialize.

**Returns:** A JSON string representation.

##### `_ModifyProperties`
```csharp
protected override void _ModifyProperties (RevitDB.Element elem)
```
Modifies properties of the specified Revit element with values from this material model.

**Parameters:**
- `elem`: The Revit element to modify.

---

#### Fields
- **`document`**: `RevitDoc document = revitMaterial.Document;`
- **`document`**: `RevitDoc document = serialElement.Document;`
- **`material`**: `RevitMaterial material = (RevitMaterial)serialElement.ElementId.GetElem(document);`
- **`dElem`**: `RevitMaterial dElem = null;`
- **`mat`**: `RevitMaterial mat = (RevitMaterial)serialMaterial.GetRevitElem(document);`
- **`dElem`**: `return dElem;`
- **`dElem`**: `RevitMaterial dElem = null;`
- **`dElem`**: `return dElem;`

---

### Class: `ModelsToSerialize`
**File:** [src/SyntheticShared/Models/ModelsToSerialize.cs](../src/SyntheticShared/Models/ModelsToSerialize.cs)

Represents a container of various Revit element models to be serialized to/from JSON.

#### Properties
- **`Elements`**: `public List<ElementModel> Elements { get; set; }`
  *Description:* Gets or sets the list of generic element models.

#### Methods
##### `ModelsToSerialize`
```csharp
internal ModelsToSerialize ()
```
##### `ByElement`
```csharp
public static ElementModel ByElement (RevitElem revitElement, bool IsTemplate)
```
Creates an ElementModel (or subclass) from a Revit element.

**Parameters:**
- `revitElement`: The Revit element to serialize.
- `IsTemplate`: Flag indicating if this model is a template.

**Returns:** A serialized ElementModel instance.

##### `DeserializeByJson`
```csharp
public static IEnumerable<ElementModel> DeserializeByJson (string Json)
```
Deserializes a JSON string into a flat list of ElementModels.

**Parameters:**
- `Json`: The JSON representation of ModelsToSerialize.

**Returns:** An enumerable collection of ElementModels.

##### `SerializeToJson`
```csharp
public static string SerializeToJson (List<ObjectModel> serialList)
```
Serializes a list of object models into a formatted JSON string.

**Parameters:**
- `serialList`: The list of object models to serialize.

**Returns:** A formatted JSON string representation.

##### `SerializeToJsonBySerialElementType`
```csharp
public static string SerializeToJsonBySerialElementType (List<ElementTypeModel> serialList)
```
Serializes a list of ElementTypeModel objects into a formatted JSON string.

**Parameters:**
- `serialList`: The list of element type models.

**Returns:** A JSON string representation.

##### `SerializeToJson`
```csharp
return SerializeToJson(serialListObjects);
```
Serializes a list of object models into a formatted JSON string.

**Parameters:**
- `serialList`: The list of object models to serialize.

**Returns:** A formatted JSON string representation.

##### `_serialByType`
```csharp
private static ElementModel _serialByType(RevitElem revitElement, bool IsTemplate)
```
##### `if`
```csharp
else if (revitElement is RevitMaterial material)
```
##### `if`
```csharp
else if (revitElement is RevitDimType dimType)
```
##### `if`
```csharp
else if (revitElement is CurtainSystemType curtainSystemType)
```
##### `if`
```csharp
else if (revitElement is RevitHostObjType hostObjType)
```
##### `if`
```csharp
else if (revitElement is RevitView view)
```
##### `if`
```csharp
else if (revitElement is GridType gridType)
```
##### `if`
```csharp
else if (revitElement is LevelType levelType)
```
##### `if`
```csharp
else if (revitElement is MullionType mullionType)
```
##### `if`
```csharp
else if (revitElement is FasciaType fasciaType)
```
##### `if`
```csharp
else if (revitElement is GutterType gutterType)
```
##### `if`
```csharp
else if (revitElement is ViewFamilyType viewFamilyType)
```
##### `if`
```csharp
else if (revitElement is RevitElemType elemType)
```
##### `if`
```csharp
else if (revitElement is FillPatternElement fillPatternElement)
```
##### `if`
```csharp
else if (revitElement is LinePatternElement linePatternElement)
```
##### `if`
```csharp
else if (revitElement is PropertySetElement propertySetElement)
```
##### `if`
```csharp
else if (revitElement is BrowserOrganization browserOrganization)
```
##### `if`
```csharp
else if (revitElement is ParameterElement parameterElement)
```
##### `_sortObjectModel`
```csharp
private void _sortObjectModel(ObjectModel serialElement)
```
##### `if`
```csharp
else if (serialElement is MaterialModel material)
```
##### `if`
```csharp
else if (serialElement is DimensionTypeModel dimType)
```
##### `if`
```csharp
else if (serialElement is CurtainSystemTypeModel curtainSystemType)
```
##### `if`
```csharp
else if (serialElement is HostObjTypeModel hostObjType)
```
##### `if`
```csharp
else if (serialElement is ViewModel view)
```
##### `if`
```csharp
else if (serialElement is CategoryModel category)
```
##### `if`
```csharp
else if (serialElement is GridTypeModel gridType)
```
##### `if`
```csharp
else if (serialElement is LevelTypeModel levelType)
```
##### `if`
```csharp
else if (serialElement is MullionTypeModel mullionType)
```
##### `if`
```csharp
else if (serialElement is FasciaTypeModel fasciaType)
```
##### `if`
```csharp
else if (serialElement is GutterTypeModel gutterType)
```
##### `if`
```csharp
else if (serialElement is ViewFamilyTypeModel viewFamilyType)
```
##### `if`
```csharp
else if (serialElement is ElementTypeModel elemType)
```
##### `if`
```csharp
else if (serialElement is FillPatternElementModel fillPattern)
```
##### `if`
```csharp
else if (serialElement is LinePatternElementModel linePattern)
```
##### `if`
```csharp
else if (serialElement is PropertySetElementModel propSet)
```
##### `if`
```csharp
else if (serialElement is BrowserOrganizationModel browserOrganization)
```
##### `if`
```csharp
else if (serialElement is ParameterElementModel parameterElement)
```
##### `if`
```csharp
else if (serialElement is ElementModel elem)
```
##### `if`
```csharp
else if (serialElement is ViewModel viewModel)
```
##### `if`
```csharp
else if (serialElement is FillPatternElementModel fillPatternModel)
```
##### `if`
```csharp
else if (serialElement is LinePatternElementModel linePatternModel)
```
##### `if`
```csharp
else if (serialElement is PropertySetElementModel propSetModel)
```
##### `if`
```csharp
else if (serialElement is BrowserOrganizationModel browserOrgModel)
```
##### `if`
```csharp
else if (serialElement is ParameterElementModel paramElemModel)
```
##### `if`
```csharp
else if (serialElement is HostObjTypeModel hostObjTypeModel)
```
##### `if`
```csharp
else if (serialElement is CategoryModel catModel)
```
##### `if`
```csharp
else if (serialElement is ElementTypeModel etModel)
```
##### `CreateElementTypeByTemplate`
```csharp
public static RevitElem CreateElementTypeByTemplate (ElementTypeModel serialElementType, RevitElem templateType)
```
Creates a new Revit ElementType from a SerialElementType.  The new type is a duplicate of of the templateType element.  Any parameters or settings the the SerialElementType doesn't set will remain the same as the template.

**Parameters:**
- `serialElementType`: A SerialElementType or SerialDimensionType object that will used to create a new Revit ElementType
- `templateType`: The ElementType that will be duplicated to create the new type.    Any parameters or settings the the SerialElementType doesn't set will remain the same as the template.

**Returns:** Revit element of the new type.

##### `CreateElementType`
```csharp
public static RevitElem CreateElementType(ElementTypeModel serialElementType,  RevitDoc document)
```
Creates a new Revit ElementType from a SerialElementType.  The new type is a duplicate of of the templateType element.  Any parameters or settings the the SerialElementType doesn't set will remain the same as the template.

**Parameters:**
- `serialElementType`: A SerialElementType or SerialDimensionType object that will used to create a new Revit ElementType
- `templateType`: The ElementType that will be duplicated to create the new type.    Any parameters or settings the the SerialElementType doesn't set will remain the same as the template.

**Returns:** Revit element of the new type.

##### `ResolveAndRenameAliases`
```csharp
public static void ResolveAndRenameAliases(ElementModel serialElement, RevitDoc document, bool mergeAliases)
```
Resolves and renames element aliases in the document.

**Parameters:**
- `serialElement`: The element model containing standard name and aliases.
- `document`: The Revit document.
- `mergeAliases`: Flag indicating whether to merge aliased elements into standard one.

##### `SwapElementReferences`
```csharp
public static void SwapElementReferences(RevitDoc doc, RevitElemId oldId, RevitElemId newId)
```
Swaps references from an old element ID to a new element ID across parameters, category styles, compound structures, and view overrides.

**Parameters:**
- `doc`: The Revit document.
- `oldId`: The old element ID.
- `newId`: The new element ID.

##### `AddCategoryAndSubcategories`
```csharp
private static void AddCategoryAndSubcategories(Category cat, List<Category> list)
```
---

#### Fields
- **`serializeElement`**: `ElementModel serializeElement = null;`
- **`serializeElement`**: `return serializeElement;`
- **`CategoryModel`**: `CategoryModel CategoryModel = null;`
- **`CategoryModel`**: `return CategoryModel;`
- **`serializeJSON`**: `ModelsToSerialize serializeJSON = JsonConvert.DeserializeObject<ModelsToSerialize>(Json);`
- **`list`**: `List<ElementModel> list = new List<ElementModel>();`
- **`list`**: `return list;`
- **`newDict`**: `var newDict = new Dictionary<string, T>();`
- **`newDict`**: `return newDict;`
- **`serializeJSON`**: `ModelsToSerialize serializeJSON = new ModelsToSerialize();`
- **`serialListObjects`**: `List<ObjectModel> serialListObjects = serialList.Cast<ObjectModel>().ToList();`
- **`serializeElement`**: `ElementModel serializeElement = null;`
- **`serializeElement`**: `return serializeElement;`
- **`name`**: `string name = fillRegionType.Name;`
- **`name`**: `string name = material.Name;`
- **`name`**: `string name = dimType.Name;`
- **`name`**: `string name = curtainSystemType.Name;`
- **`name`**: `string name = hostObjType.Name;`
- **`name`**: `string name = view.Name;`
- **`name`**: `string name = category.Name;`
- **`name`**: `string name = gridType.Name;`
- **`name`**: `string name = levelType.Name;`
- **`name`**: `string name = mullionType.Name;`
- **`name`**: `string name = fasciaType.Name;`
- **`name`**: `string name = gutterType.Name;`
- **`name`**: `string name = viewFamilyType.Name;`
- **`name`**: `string name = elemType.Name;`
- **`name`**: `string name = fillPattern.Name;`
- **`name`**: `string name = linePattern.Name;`
- **`name`**: `string name = propSet.Name;`
- **`name`**: `string name = browserOrganization.Name;`
- **`name`**: `string name = parameterElement.Name;`
- **`elem`**: `RevitElem elem = null;`
- **`elem`**: `return elem;`
- **`document`**: `RevitDoc document = templateType.Document;`
- **`elem`**: `RevitElem elem = ElementTypeModel.CreateElementTypeByTemplate(serialElementType, (RevitElemType)templateType, document);`
- **`elem`**: `return elem;`
- **`elem`**: `RevitElem elem = null;`
- **`assembly`**: `Assembly assembly = typeof(RevitDB.DimensionType).Assembly;`
- **`elemClass`**: `Type elemClass = assembly.GetType(serialElementType.Class);`
- **`serialDimType`**: `DimensionTypeModel serialDimType = (DimensionTypeModel)serialElementType;`
- **`elem`**: `return elem;`
- **`assembly`**: `Assembly assembly = typeof(RevitElem).Assembly;`
- **`elemClass`**: `Type elemClass = assembly.GetType(serialElement.Class);`
- **`standardElem`**: `RevitElem standardElem = Select.ElementByNameClass(serialElement.Name, elemClass, document);`
- **`aliasedElem`**: `RevitElem aliasedElem = Select.ElementByNameClass(alias, elemClass, document);`
- **`firstAliasElem`**: `RevitElem firstAliasElem = null;`
- **`renamedAliasName`**: `string renamedAliasName = null;`
- **`aliasedElem`**: `RevitElem aliasedElem = Select.ElementByNameClass(alias, elemClass, document);`
- **`lp`**: `LinePattern lp = lpe.GetLinePattern();`
- **`otherAliasedElem`**: `RevitElem otherAliasedElem = Select.ElementByNameClass(alias, elemClass, document);`
- **`allElements`**: `var allElements = instances.Concat(types);`
- **`allCats`**: `List<Category> allCats = new List<Category>();`
- **`cs`**: `CompoundStructure cs = hostType.GetCompoundStructure();`
- **`layers`**: `IList<CompoundStructureLayer> layers = cs.GetLayers();`
- **`changed`**: `bool changed = false;`
- **`layer`**: `CompoundStructureLayer layer = layers[i];`
- **`allCats`**: `List<Category> allCats = new List<Category>();`
- **`settings`**: `OverrideGraphicSettings settings = view.GetCategoryOverrides(cat.Id);`
- **`changed`**: `bool changed = false;`

---

### Class: `MullionTypeModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model for a Revit MullionType, inheriting from ElementTypeModel.

#### Methods
##### `MullionTypeModel`
```csharp
public MullionTypeModel() : base() { }
```
Initializes a new instance of the MullionTypeModel class.

##### `MullionTypeModel`
```csharp
public MullionTypeModel(MullionType elem, bool isTemplate) : base(elem, isTemplate) { }
```
Initializes a new instance of the MullionTypeModel class from a Revit MullionType.  <param name="elem">The Revit MullionType.</param> <param name="isTemplate">Flag indicating if this model is a template.</param>

---

### Class: `ObjectModel`
**File:** [src/SyntheticShared/Models/ObjectModel.cs](../src/SyntheticShared/Models/ObjectModel.cs)

Basic class for generic objects.  Used to inherit from.

#### Methods
##### `ObjectModel`
```csharp
public ObjectModel() { }
```
Constructor

---

### Class: `OverrideGraphicSettingsModel`
**File:** [src/SyntheticShared/Models/OverrideGraphicSettingsModel.cs](../src/SyntheticShared/Models/OverrideGraphicSettingsModel.cs)

Represents a model for Revit OverrideGraphicSettings, mapping visibility and style overrides.

#### Properties
- **`IsModified`**: `public bool IsModified { get; set; }`
  *Description:* Gets or sets a value indicating whether any settings have been modified. Ignored in JSON.
- **`IsSurfaceBackgroundPatternVisible`**: `public bool IsSurfaceBackgroundPatternVisible { get; set; }`
  *Description:* Gets or sets a value indicating whether surface background pattern is visible.
- **`SurfaceBackgroundPatternColor`**: `public ColorModel SurfaceBackgroundPatternColor { get; set; }`
  *Description:* Gets or sets the surface background pattern color.
- **`SurfaceBackgroundPatternId`**: `public ElementIdModel SurfaceBackgroundPatternId { get; set; }`
  *Description:* Gets or sets the surface background pattern element ID model.
- **`IsSurfaceForegroundPatternVisible`**: `public bool IsSurfaceForegroundPatternVisible { get; set; }`
  *Description:* Gets or sets a value indicating whether surface foreground pattern is visible.
- **`SurfaceForegroundPatternColor`**: `public ColorModel SurfaceForegroundPatternColor { get; set; }`
  *Description:* Gets or sets the surface foreground pattern color.
- **`SurfaceForegroundPatternId`**: `public ElementIdModel SurfaceForegroundPatternId { get; set; }`
  *Description:* Gets or sets the surface foreground pattern element ID model.
- **`ProjectionLineColor`**: `public ColorModel ProjectionLineColor { get; set; }`
  *Description:* Gets or sets the projection line color.
- **`ProjectionLinePatternId`**: `public ElementIdModel ProjectionLinePatternId { get; set; }`
  *Description:* Gets or sets the projection line pattern element ID model.
- **`ProjectionLineWeight`**: `public int ProjectionLineWeight { get; set; }`
  *Description:* Gets or sets the projection line weight.
- **`IsCutBackgroundPatternVisible`**: `public bool IsCutBackgroundPatternVisible { get; set; }`
  *Description:* Gets or sets a value indicating whether cut background pattern is visible.
- **`CutBackgroundPatternColor`**: `public ColorModel CutBackgroundPatternColor { get; set; }`
  *Description:* Gets or sets the cut background pattern color.
- **`CutBackgroundPatternId`**: `public ElementIdModel CutBackgroundPatternId { get; set; }`
  *Description:* Gets or sets the cut background pattern element ID model.
- **`IsCutForegroundPatternVisible`**: `public bool IsCutForegroundPatternVisible { get; set; }`
  *Description:* Gets or sets a value indicating whether cut foreground pattern is visible.
- **`CutForegroundPatternColor`**: `public ColorModel CutForegroundPatternColor { get; set; }`
  *Description:* Gets or sets the cut foreground pattern color.
- **`CutForegroundPatternId`**: `public ElementIdModel CutForegroundPatternId { get; set; }`
  *Description:* Gets or sets the cut foreground pattern element ID model.
- **`CutLineColor`**: `public ColorModel CutLineColor { get; set; }`
  *Description:* Gets or sets the cut line color.
- **`CutLinePatternId`**: `public ElementIdModel CutLinePatternId { get; set; }`
  *Description:* Gets or sets the cut line pattern element ID model.
- **`CutLineWeight`**: `public int CutLineWeight { get; set; }`
  *Description:* Gets or sets the cut line weight.
- **`Transparency`**: `public int Transparency { get; set; }`
  *Description:* Gets or sets the transparency percentage (0-100).
- **`Halftone`**: `public bool Halftone { get; set; }`
  *Description:* Gets or sets a value indicating whether halftone is enabled.
- **`DetailLevel`**: `public EnumModel DetailLevel { get; set; }`
  *Description:* Gets or sets the view detail level override.

#### Methods
##### `OverrideGraphicSettingsModel`
```csharp
public OverrideGraphicSettingsModel () { }
```
Initializes a new instance of the OverrideGraphicSettingsModel class.

##### `_IsModified`
```csharp
private bool _IsModified(RevitDB.OverrideGraphicSettings ogs)
```
---

#### Fields
- **`IsModified`**: `return IsModified;`
- **`ogs`**: `return ogs;`

---

### Class: `ParameterDiffRowModel`
**File:** [src/SyntheticShared/Models/ParameterDiffRowModel.cs](../src/SyntheticShared/Models/ParameterDiffRowModel.cs)

Represents a single parameter row in the Diff Tool.

#### Methods
##### `ParameterDiffRowModel`
```csharp
public ParameterDiffRowModel()
```
Initializes a new instance of the ParameterDiffRowModel class.

##### `OnPropertyChanged`
```csharp
protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
```
Raises the

**Parameters:**
- `propertyName`: The name of the property that changed.

---

#### Fields
- **`_parameterName`**: `private string _parameterName;`
- **`_isSchemaMismatch`**: `private bool _isSchemaMismatch;`
- **`_winningValueElementId`**: `private ElementId _winningValueElementId;`
- **`_valueList`**: `private List<string> _valueList;`
- **`_options`**: `private List<ParameterValueOption> _options;`
- **`_hasConflict`**: `private bool _hasConflict;`
- **`_injectParameter`**: `private bool _injectParameter;`
- **`_isInjectEnabled`**: `private bool _isInjectEnabled = true;`
- **`true`**: `return true;`

---

### Class: `ParameterElementModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model for a Revit ParameterElement, inheriting from ElementModel.

#### Methods
##### `ParameterElementModel`
```csharp
public ParameterElementModel() : base() { }
```
Initializes a new instance of the ParameterElementModel class.

##### `ParameterElementModel`
```csharp
public ParameterElementModel(ParameterElement elem, bool isTemplate) : base(elem, isTemplate) { }
```
Initializes a new instance of the ParameterElementModel class from a Revit ParameterElement.  <param name="elem">The Revit ParameterElement.</param> <param name="isTemplate">Flag indicating if this model is a template.</param>

##### `ModifyParameterElement`
```csharp
public static ParameterElement ModifyParameterElement(ParameterElementModel model, Document doc)
```
Modifies a Revit ParameterElement in the document using properties of this model.

**Parameters:**
- `model`: The ParameterElementModel source.
- `doc`: The Revit document.

**Returns:** The modified Revit ParameterElement.

---

#### Fields
- **`elem`**: `ParameterElement elem = (ParameterElement)model.GetRevitElem(doc);`
- **`elem`**: `return elem;`

---

### Class: `ParameterModel`
**File:** [src/SyntheticShared/Models/ParameterModel.cs](../src/SyntheticShared/Models/ParameterModel.cs)

Represents a model representation of a Revit Parameter, mapping name, value, and storage type.

#### Properties
- **`Name`**: `public string Name { get; set; }`
  *Description:* Gets or sets the parameter name.
- **`Value`**: `public string Value { get; set; }`
  *Description:* Gets or sets the parameter value as a string.
- **`ValueElemId`**: `public ElementIdModel ValueElemId { get; set; }`
  *Description:* Gets or sets the parameter value as an ElementId model, if the parameter storage type is ElementId.
- **`StorageType`**: `public string StorageType { get; set; }`
  *Description:* Gets or sets the storage type of the parameter (e.g. Double, Integer, String, ElementId).
- **`Id`**: `public long Id { get; set; }`
  *Description:* Gets or sets the integer value of the parameter definition ID.
- **`GUID`**: `public string GUID { get; set; }`
  *Description:* Gets or sets the GUID of the parameter, if it is a shared parameter.
- **`IsShared`**: `public bool IsShared { get; set; }`
  *Description:* Gets or sets a value indicating whether this is a shared parameter.
- **`IsReadOnly`**: `public bool IsReadOnly { get; set; }`
  *Description:* Gets or sets a value indicating whether this parameter is read-only.
- **`IsTemplate`**: `public bool IsTemplate { get; set; }`
  *Description:* If true, SerialParameter is intended to be deserialized as a template for use as standards or transfer to another project. If false, SerialParameter is intended to modify an element inside the project and will include ElementIds and UniqueIds.

#### Methods
##### `ParameterModel`
```csharp
public ParameterModel(string Name, string Value, ElementIdModel ValueElemId, string StorageType, int Id, string GUID, bool IsShared, bool IsReadOnly)
```
##### `ToJSON`
```csharp
public static string ToJSON(ParameterModel parameter)
```
Serializes a ParameterModel to a JSON string.

**Parameters:**
- `parameter`: The ParameterModel to serialize.

**Returns:** A JSON string representation.

##### `ModifyParameter`
```csharp
public static RevitElem ModifyParameter(ParameterModel serialParameter, RevitElem Elem)
```
Modifies the parameter on a Revit Element to match the properties of the parameter model.

**Parameters:**
- `serialParameter`: The parameter model containing the new value.
- `Elem`: The Revit element containing the parameter.

**Returns:** The modified Revit element.

##### `if`
```csharp
else if (serialParameter.Id < 0)
```
##### `if`
```csharp
else if (serialParameter.Id > 0)
```
##### `_ModifyElementIdParameter`
```csharp
private static bool _ModifyElementIdParameter (RevitParam param, ElementIdModel ElementIdModel, RevitDoc document)
```
Depending on ElementIdModel properties, method attempts to find the Element referenced by the ElementId by first trying UniqueId, then Id, then Name, then Aliases of the name.  If an element is found, then set the parameter to the Element's Id.

**Parameters:**
- `param`: Parameter to modify
- `ElementIdModel`: A ElementIdModel that references an element in the Document.
- `document`: A revit document

**Returns:** Returns true if parameter was successfully set and false if not.  Parameter retains it previous value if Set was unsucessful.

---

#### Fields
- **`param`**: `RevitParam param = null;`
- **`doc`**: `RevitDoc doc = Elem.Document;`
- **`paramElem`**: `ParameterElement paramElem = (ParameterElement)doc.GetElement(new ElementId((int)serialParameter.Id));`
- **`paramElem`**: `ParameterElement paramElem = (ParameterElement)doc.GetElement(new ElementId(serialParameter.Id));`
- **`def`**: `Definition def = paramElem.GetDefinition();`
- **`i`**: `int i = Convert.ToInt32(serialParameter.Value);`
- **`Elem`**: `return Elem;`
- **`status`**: `bool status = false;`
- **`elem`**: `RevitElem elem = ElementIdModel.GetElem(document);`
- **`status`**: `return status;`

---

### Class: `ParameterValueOption`
**File:** [src/SyntheticShared/Models/ParameterDiffRowModel.cs](../src/SyntheticShared/Models/ParameterDiffRowModel.cs)

Represents a specific value option for a parameter.

#### Properties
- **`ElementId`**: `public ElementId ElementId { get; set; }`
  *Description:* Gets or sets the ElementId of the option.
- **`DisplayText`**: `public string DisplayText { get; set; }`
  *Description:* Gets or sets the display text for the option.

### Class: `PropertySetElementModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model for a Revit PropertySetElement (Material Asset), inheriting from ElementModel.

#### Methods
##### `PropertySetElementModel`
```csharp
public PropertySetElementModel() : base() { }
```
Initializes a new instance of the PropertySetElementModel class.

##### `PropertySetElementModel`
```csharp
public PropertySetElementModel(PropertySetElement elem, bool isTemplate) : base(elem, isTemplate) { }
```
Initializes a new instance of the PropertySetElementModel class from a Revit PropertySetElement.  <param name="elem">The Revit PropertySetElement.</param> <param name="isTemplate">Flag indicating if this model is a template.</param>

##### `ModifyPropertySetElement`
```csharp
public static PropertySetElement ModifyPropertySetElement(PropertySetElementModel model, Document doc)
```
Modifies or creates a Revit PropertySetElement in the document using properties of this model.

**Parameters:**
- `model`: The PropertySetElementModel source.
- `doc`: The Revit document.

**Returns:** The modified or created Revit PropertySetElement.

---

#### Fields
- **`elem`**: `PropertySetElement elem = (PropertySetElement)model.GetRevitElem(doc);`
- **`asset`**: `StructuralAsset asset = new StructuralAsset(model.Name, StructuralAssetClass.Generic);`
- **`elem`**: `return elem;`

---

### Enum: `RecommendedAction`
**File:** [src/SyntheticShared/Models/RecommendedAction.cs](../src/SyntheticShared/Models/RecommendedAction.cs)

Indicates the recommended resolution action for a duplicate type mapping.

### Class: `SerialCompoundStructureLayer`
**File:** [src/SyntheticShared/Models/CompoundStructureModel.cs](../src/SyntheticShared/Models/CompoundStructureModel.cs)

Model representing a single layer in a CompoundStructure.

#### Properties
- **`MaterialId`**: `public ElementIdModel MaterialId { get; set; }`
  *Description:* Gets or sets the material ID model.
- **`Function`**: `public string Function { get; set; }`
  *Description:* Gets or sets the layer function (e.g. Structure, Finish, Substrate).
- **`Width`**: `public double Width { get; set; }`
  *Description:* Gets or sets the width of the layer in feet.
- **`DeckEmbeddingType`**: `public string DeckEmbeddingType { get; set; }`
  *Description:* Gets or sets the deck embedding type.
- **`DeckProfileId`**: `public ElementIdModel DeckProfileId { get; set; }`
  *Description:* Gets or sets the deck profile element ID model.
- **`LayerCapFlag`**: `public bool LayerCapFlag { get; set; }`
  *Description:* Gets or sets a value indicating whether the layer caps.
- **`StructuralMaterial`**: `public bool StructuralMaterial { get; set; }`
  *Description:* Gets or sets a value indicating whether the layer is the structural material.
- **`Priority`**: `public int Priority { get; set; }`
  *Description:* Gets or sets the priority of the layer (Revit 2026+).

#### Methods
##### `SerialCompoundStructureLayer`
```csharp
public SerialCompoundStructureLayer () { }
```
Initializes a new instance of the SerialCompoundStructureLayer class.

---

#### Fields
- **`layer`**: `RevitCSLayer layer = new RevitCSLayer();`
- **`layer`**: `return layer;`

---

### Class: `TagTemplate`
**File:** [src/SyntheticShared/Models/TagTemplate.cs](../src/SyntheticShared/Models/TagTemplate.cs)

Data Transfer Object (DTO) representing a user-defined tag offset template.

#### Properties
- **`Id`**: `public Guid Id { get; set; } = Guid.NewGuid();`
  *Description:* Gets or sets the unique identifier of the tag template.
- **`TemplateName`**: `public string TemplateName { get; set; }`
  *Description:* Gets or sets the name of the template.
- **`TargetCategory`**: `public string TargetCategory { get; set; }`
  *Description:* Gets or sets the target category name.
- **`TargetFamily`**: `public string TargetFamily { get; set; }`
  *Description:* Gets or sets the target family name.
- **`TargetType`**: `public string TargetType { get; set; }`
  *Description:* Gets or sets the target type name.
- **`OffsetX`**: `public double OffsetX { get; set; }`
  *Description:* Gets or sets the relative X offset in decimal feet.
- **`OffsetY`**: `public double OffsetY { get; set; }`
  *Description:* Gets or sets the relative Y offset in decimal feet.
- **`OffsetZ`**: `public double OffsetZ { get; set; }`
  *Description:* Gets or sets the relative Z offset in decimal feet.
- **`Orientation`**: `public TagOrientation Orientation { get; set; }`
  *Description:* Gets or sets the orientation of the tag.
- **`AllowOrientationChange`**: `public bool AllowOrientationChange { get; set; }`
  *Description:* Gets or sets a value indicating whether orientation change is allowed.
- **`HostHandX`**: `public double HostHandX { get; set; }`
  *Description:* Gets or sets the X component of the host's hand orientation vector.
- **`HostHandY`**: `public double HostHandY { get; set; }`
  *Description:* Gets or sets the Y component of the host's hand orientation vector.
- **`HostHandZ`**: `public double HostHandZ { get; set; }`
  *Description:* Gets or sets the Z component of the host's hand orientation vector.

### Class: `TypeMappingModel`
**File:** [src/SyntheticShared/Models/TypeMappingModel.cs](../src/SyntheticShared/Models/TypeMappingModel.cs)

Manages the resolution state between two duplicate types.

#### Methods
##### `TypeMappingModel`
```csharp
public TypeMappingModel()
```
Initializes a new instance of the TypeMappingModel class.

##### `OnPropertyChanged`
```csharp
protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
```
Raises the

**Parameters:**
- `propertyName`: The name of the property that changed.

---

#### Fields
- **`_sourceType`**: `private DuplicateTypeModel _sourceType;`
- **`_targetType`**: `private DuplicateTypeModel _targetType;`
- **`_sourceFamily`**: `private DuplicateItemModel _sourceFamily;`
- **`_targetFamily`**: `private DuplicateItemModel _targetFamily;`
- **`_recommendedAction`**: `private RecommendedAction _recommendedAction;`
- **`_parameterResolutions`**: `private ObservableCollection<ParameterDiffRowModel> _parameterResolutions;`
- **`true`**: `return true;`

---

### Class: `UVModel`
**File:** [src/SyntheticShared/Models/UVModel.cs](../src/SyntheticShared/Models/UVModel.cs)

Represents a model for a Revit UV (2D coordinate/vector).

#### Properties
- **`U`**: `public Double U { get; set; }`
  *Description:* Gets or sets the U (horizontal) coordinate.
- **`V`**: `public Double V { get; set; }`
  *Description:* Gets or sets the V (vertical) coordinate.

#### Methods
##### `UVModel`
```csharp
public UVModel() { }
```
Initializes a new instance of the UVModel class.

##### `UVModel`
```csharp
public UVModel (Double U, Double V)
```
Initializes a new instance of the UVModel class with specified U and V values.  <param name="U">The U value.</param> <param name="V">The V value.</param>

##### `UVModel`
```csharp
public UVModel (RevitDB.UV UV)
```
Initializes a new instance of the UVModel class from a Revit UV object.  <param name="UV">The Revit UV.</param>

##### `ByJSON`
```csharp
public static UVModel ByJSON (string JSON)
```
Deserializes a UVModel from a JSON string.

**Parameters:**
- `JSON`: The JSON representation.

**Returns:** A UVModel instance.

##### `ToJSON`
```csharp
public static string ToJSON (UVModel color)
```
Serializes a UVModel to a JSON string.

**Parameters:**
- `color`: The UVModel to serialize.

**Returns:** A JSON string representation.

---

### Class: `ViewFamilyTypeModel`
**File:** [src/SyntheticShared/Models/MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)

Represents a model for a Revit ViewFamilyType, inheriting from ElementTypeModel.

#### Methods
##### `ViewFamilyTypeModel`
```csharp
public ViewFamilyTypeModel() : base() { }
```
Initializes a new instance of the ViewFamilyTypeModel class.

##### `ViewFamilyTypeModel`
```csharp
public ViewFamilyTypeModel(ViewFamilyType elem, bool isTemplate) : base(elem, isTemplate) { }
```
Initializes a new instance of the ViewFamilyTypeModel class from a Revit ViewFamilyType.  <param name="elem">The Revit ViewFamilyType.</param> <param name="isTemplate">Flag indicating if this model is a template.</param>

---

### Class: `ViewModel`
**File:** [src/SyntheticShared/Models/ViewModel.cs](../src/SyntheticShared/Models/ViewModel.cs)

Represents a model representation of a Revit View, inheriting from ElementModel.

#### Properties
- **`DisplayStyle`**: `public EnumModel DisplayStyle { get; set; }`
  *Description:* Gets or sets the display style of the view.
- **`ShadowIntesnity`**: `public int ShadowIntesnity { get; set; }`
  *Description:* Gets or sets the shadow intensity.
- **`SunlightIntensity`**: `public int SunlightIntensity { get; set; }`
  *Description:* Gets or sets the sunlight intensity.
- **`CategoryGraphicOverrides`**: `public List<CategoryGraphicOverridesModel> CategoryGraphicOverrides { get; set; }`
  *Description:* Gets or sets the list of graphic overrides applied to categories in this view.
- **`View`**: `public RevitView View { get; set; }`
  *Description:* Gets or sets the associated Revit View. Ignored in JSON.

#### Methods
##### `ViewModel`
```csharp
public ViewModel () { }
```
Initializes a new instance of the ViewModel class.

##### `ViewModel`
```csharp
public ViewModel (RevitView view, bool IsTemplate) : base (view, IsTemplate)
```
Initializes a new instance of the ViewModel class from a Revit View.  <param name="view">The Revit View.</param> <param name="IsTemplate">Flag indicating if this model is a template.</param>

##### `_ApplyProperties`
```csharp
private void _ApplyProperties (RevitView view)
```
##### `_GetCategoryGraphicOverrides`
```csharp
private List<CategoryGraphicOverridesModel> _GetCategoryGraphicOverrides(RevitView view)
```
##### `_ModifyProperties`
```csharp
protected override void _ModifyProperties(RevitDB.Element elem)
```
Modifies properties of the specified Revit element with values from this view model.

**Parameters:**
- `elem`: The Revit element to modify.

---

#### Fields
- **`overrides`**: `List<CategoryGraphicOverridesModel> overrides = new List<CategoryGraphicOverridesModel>();`
- **`document`**: `RevitDoc document = view.Document;`
- **`catOverride`**: `CategoryGraphicOverridesModel catOverride = new CategoryGraphicOverridesModel(category, view);`
- **`subCatOverride`**: `CategoryGraphicOverridesModel subCatOverride = new CategoryGraphicOverridesModel(subCategory, view);`
- **`overrides`**: `return overrides;`
- **`dElem`**: `RevitView dElem = null;`
- **`dElem`**: `return dElem;`

---

### Class: `WorksetModel`
**File:** [src/SyntheticShared/Models/WorksetModel.cs](../src/SyntheticShared/Models/WorksetModel.cs)

Represents a model representation of a Revit Workset, mapping its name, visibility, alias, and description.

#### Properties
- **`Name`**: `public string Name { get; set; }`
  *Description:* Gets or sets the name of the workset.
- **`Visibility`**: `public bool Visibility { get; set; }`
  *Description:* Gets or sets a value indicating whether the workset is visible.
- **`Alias`**: `public string Alias { get; set; }`
  *Description:* Gets or sets the alias name of the workset.
- **`Description`**: `public string Description { get; set; }`
  *Description:* Gets or sets the description of the workset.

#### Methods
##### `WorksetModel`
```csharp
public WorksetModel(string name, bool visibility = true, string alias = null, string description = null)
```
Initializes a new instance of the WorksetModel class.  <param name="name">The name of the workset.</param> <param name="visibility">Visibility state, default is true.</param> <param name="alias">The alias name.</param> <param name="description">The description.</param>

---

## Namespace: `Synthetic.Repositories`

### Class: `TemplateStorageRepository`
**File:** [src/SyntheticShared/Repositories/TemplateStorageRepository.cs](../src/SyntheticShared/Repositories/TemplateStorageRepository.cs)

Handles all CRUD operations for TagTemplates, abstracting Revit Extensible Storage and local JSON file I/O.

#### Methods
##### `GetSchema`
```csharp
private Schema GetSchema()
```
##### `GetDataStorage`
```csharp
private DataStorage GetDataStorage(Document doc, Schema schema)
```
##### `GetTemplates`
```csharp
public List<TagTemplate> GetTemplates(Document doc)
```
Retrieves all templates saved within the active Revit Document.

##### `SaveTemplate`
```csharp
public void SaveTemplate(Document doc, TagTemplate template)
```
Saves a template to the Revit Document. Creates the DataStorage element if it does not exist.

##### `ExportToJson`
```csharp
public void ExportToJson(List<TagTemplate> templates, string filePath)
```
Exports a list of templates to a local JSON file.

##### `ImportFromJson`
```csharp
public List<TagTemplate> ImportFromJson(string filePath)
```
Imports a list of templates from a local JSON file.

**Parameters:**
- `filePath`: The file path to the JSON file containing tag templates.

**Returns:** A list of deserialized TagTemplate objects.

##### `DeleteTemplate`
```csharp
public void DeleteTemplate(Document doc, Guid templateId)
```
Deletes a specific template from the Extensible Storage.

##### `SaveAllTemplates`
```csharp
public void SaveAllTemplates(Document doc, List<TagTemplate> newTemplates)
```
Bulk saves templates, merging them with existing templates by ID.

---

#### Fields
- **`_schemaGuid`**: `private readonly Guid _schemaGuid = new Guid("4A9119B9-7221-4A59-AC10-7B32F4D9C8A1");`
- **`schema`**: `Schema schema = Schema.Lookup(_schemaGuid);`
- **`builder`**: `SchemaBuilder builder = new SchemaBuilder(_schemaGuid);`
- **`collector`**: `var collector = new FilteredElementCollector(doc).OfClass(typeof(DataStorage));`
- **`entity`**: `Entity entity = ds.GetEntity(schema);`
- **`null`**: `return null;`
- **`schema`**: `Schema schema = GetSchema();`
- **`ds`**: `DataStorage ds = GetDataStorage(doc, schema);`
- **`entity`**: `Entity entity = ds.GetEntity(schema);`
- **`jsonData`**: `string jsonData = entity.Get<string>(FieldName);`
- **`templates`**: `List<TagTemplate> templates = GetTemplates(doc);`
- **`existing`**: `var existing = templates.FirstOrDefault(t => t.Id == template.Id);`
- **`jsonData`**: `string jsonData = JsonConvert.SerializeObject(templates);`
- **`schema`**: `Schema schema = GetSchema();`
- **`ds`**: `DataStorage ds = GetDataStorage(doc, schema);`
- **`entity`**: `Entity entity = new Entity(schema);`
- **`jsonData`**: `string jsonData = JsonConvert.SerializeObject(templates, Formatting.Indented);`
- **`jsonData`**: `string jsonData = File.ReadAllText(filePath);`
- **`templates`**: `List<TagTemplate> templates = GetTemplates(doc);`
- **`existing`**: `var existing = templates.FirstOrDefault(t => t.Id == templateId);`
- **`jsonData`**: `string jsonData = JsonConvert.SerializeObject(templates);`
- **`schema`**: `Schema schema = GetSchema();`
- **`ds`**: `DataStorage ds = GetDataStorage(doc, schema);`
- **`entity`**: `Entity entity = new Entity(schema);`
- **`templates`**: `List<TagTemplate> templates = GetTemplates(doc);`
- **`jsonData`**: `string jsonData = JsonConvert.SerializeObject(templates);`
- **`schema`**: `Schema schema = GetSchema();`
- **`ds`**: `DataStorage ds = GetDataStorage(doc, schema);`
- **`entity`**: `Entity entity = new Entity(schema);`

---

## Namespace: `Synthetic.Settings`

### Class: `Config`
**File:** [src/SyntheticShared/Settings/Config.cs](../src/SyntheticShared/Settings/Config.cs)

Represents configuration settings for the add-in.

#### Properties
- **`addinPath`**: `public static string addinPath { get { return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); } }`
  *Description:* Gets the folder path where the executing add-in assembly is located. Ignored in JSON.
- **`defaultSettingsFile`**: `public static string defaultSettingsFile { get { return "SyntheticSettings.json"; } }`
  *Description:* Gets the default filename for settings. Ignored in JSON.
- **`defaultSettingsPath`**: `public static string defaultSettingsPath { get { return Path.Combine(addinPath, defaultSettingsFile); } }`
  *Description:* Gets the default full file path to the settings file. Ignored in JSON.

#### Methods
##### `Config`
```csharp
public Config ()
```
Initializes a new instance of the Config class.

##### `Config`
```csharp
public Config (string name, object settingsValue) : base()
```
Initializes a new instance of the Config class with a single setting option.  <param name="name">The name of the setting.</param> <param name="settingsValue">The value of the setting.</param>

##### `Defaults`
```csharp
public Config Defaults ()
```
Populates the configuration with default settings.

**Returns:** This Config instance.

##### `FilePath`
```csharp
public string FilePath (string filename)
```
Gets the full path for a file relative to the add-in directory.

**Parameters:**
- `filename`: The filename.

**Returns:** The combined full path.

##### `Contains`
```csharp
public bool Contains(string key)
```
Determines whether the configuration contains a setting with the specified key.

**Parameters:**
- `key`: The setting key.

**Returns:** True if the key exists; otherwise, false.

##### `ToString`
```csharp
public override string ToString ()
```
Returns a string representation of all settings.

**Returns:** A formatted string displaying all key-value settings.

##### `FilePathDefaults`
```csharp
public static string FilePathDefaults ()
```
Gets the default settings file path.

**Returns:** The default settings file path.

##### `ByJson`
```csharp
public static Config ByJson(string json)
```
Deserializes a Config instance from a JSON string.

**Parameters:**
- `json`: The JSON string.

**Returns:** A Config instance.

##### `ReadFromFile`
```csharp
public static Config ReadFromFile(string path)
```
Reads a Config instance from a JSON file.

**Parameters:**
- `path`: The file path.

**Returns:** A Config instance, or null if file not found.

##### `WriteToFile`
```csharp
public static void WriteToFile(string path, Config settings)
```
Writes a Config instance to a JSON file.

**Parameters:**
- `path`: The file path to write to.
- `settings`: The Config instance to serialize.

##### `ReadAppConfig`
```csharp
public static Config ReadAppConfig()
```
Reads the application level configuration.

**Returns:** A Config instance.

##### `ReadFromFile`
```csharp
return ReadFromFile(FilePathDefaults());
```
Reads a Config instance from a JSON file.

**Parameters:**
- `path`: The file path.

**Returns:** A Config instance, or null if file not found.

##### `WriteAppConfig`
```csharp
public static void WriteAppConfig(Config settings)
```
Writes the application level configuration.

**Parameters:**
- `settings`: The Config settings to save.

##### `ReadProjectConfig`
```csharp
public static Config ReadProjectConfig(Document doc)
```
Reads the project specific configuration.

**Parameters:**
- `doc`: The Revit Document.

**Returns:** A Config instance, or null.

##### `WriteProjectConfig`
```csharp
public static void WriteProjectConfig(Document doc)
```
Writes the current project configuration to the project specific file.  This will overwrite the existing file.

**Parameters:**
- `doc`: The document the file belongs to

---

#### Fields
- **`json`**: `string json = JsonConvert.SerializeObject(val);`
- **`this`**: `return this;`
- **`text`**: `string text = string.Empty;`
- **`text`**: `return text;`
- **`config`**: `Config config = null;`
- **`json`**: `string json = File.ReadAllText(path);`
- **`config`**: `return config;`
- **`json`**: `string json = JsonConvert.SerializeObject(settings, Formatting.Indented);`
- **`null`**: `return null;`

---

### Class: `ConfigCollection`
**File:** [src/SyntheticShared/Settings/ConfigCollection.cs](../src/SyntheticShared/Settings/ConfigCollection.cs)

Manages application-level and project-level configurations.

#### Methods
##### `ConfigCollection`
```csharp
public ConfigCollection()
```
Initializes a new instance of the ConfigCollection class.

##### `GetAppConfig`
```csharp
public Config GetAppConfig()
```
Gets the application configuration.

**Returns:** A Config instance representing the app configuration.

##### `GetProjectConfig`
```csharp
public Config GetProjectConfig(string path)
```
Gets the project configuration associated with the specified file path.

**Parameters:**
- `path`: The document file path.

**Returns:** The Config instance.

##### `GetProjectConfig`
```csharp
public Config GetProjectConfig(RevitDoc doc)
```
Gets the project configuration associated with the specified file path.

**Parameters:**
- `path`: The document file path.

**Returns:** The Config instance.

##### `AddAppConfig`
```csharp
public ConfigCollection AddAppConfig (Config configApp)
```
Sets/adds the application configuration.

**Parameters:**
- `configApp`: The Config instance for the app.

**Returns:** This ConfigCollection instance.

##### `AddProjectConfig`
```csharp
public ConfigCollection AddProjectConfig(string path, Config value)
```
Sets/adds a project configuration associated with a file path.

**Parameters:**
- `path`: The file path.
- `value`: The Config instance to associate.

**Returns:** This ConfigCollection instance.

##### `AddProjectConfig`
```csharp
public ConfigCollection AddProjectConfig(RevitDoc doc, Config value)
```
Sets/adds a project configuration associated with a file path.

**Parameters:**
- `path`: The file path.
- `value`: The Config instance to associate.

**Returns:** This ConfigCollection instance.

##### `RemoveProjectConfig`
```csharp
public ConfigCollection RemoveProjectConfig(string path)
```
Removes the project configuration associated with the specified file path.

**Parameters:**
- `path`: The file path.

**Returns:** This ConfigCollection instance.

##### `RemoveProjectConfig`
```csharp
public ConfigCollection RemoveProjectConfig(RevitDoc doc)
```
Removes the project configuration associated with the specified file path.

**Parameters:**
- `path`: The file path.

**Returns:** This ConfigCollection instance.

##### `ContainsProjectConfig`
```csharp
public bool ContainsProjectConfig (string path)
```
Determines whether a project configuration exists for the specified file path.

**Parameters:**
- `path`: The file path.

**Returns:** True if configuration exists; otherwise, false.

##### `ContainsProjectConfig`
```csharp
public bool ContainsProjectConfig(RevitDoc doc)
```
Determines whether a project configuration exists for the specified file path.

**Parameters:**
- `path`: The file path.

**Returns:** True if configuration exists; otherwise, false.

---

#### Fields
- **`ConfigApp`**: `internal Config ConfigApp;`
- **`ConfigApp`**: `return ConfigApp;`
- **`path`**: `string path = DocumentUtil.GetFilePath(doc);`
- **`configProject`**: `Config configProject = null;`
- **`path`**: `string path = DocumentUtil.GetFilePath(doc);`
- **`this`**: `return this;`
- **`this`**: `return this;`
- **`path`**: `string path = DocumentUtil.GetFilePath(doc);`
- **`this`**: `return this;`
- **`this`**: `return this;`
- **`path`**: `string path = DocumentUtil.GetFilePath(doc);`
- **`this`**: `return this;`
- **`path`**: `string path = DocumentUtil.GetFilePath(doc);`

---

### Class: `FileUtilitySettings`
**File:** [src/SyntheticShared/Settings/FileUtilitySettings.cs](../src/SyntheticShared/Settings/FileUtilitySettings.cs)

Configuration settings for file utility operations, including archive and alternate paths.

#### Properties
- **`ArchiveDirectories`**: `public List<string> ArchiveDirectories { get; set; }`
  *Description:* Gets or sets the list of archive directories.

#### Methods
##### `FileUtilitySettings`
```csharp
public FileUtilitySettings()
```
Initializes a new instance of the <see cref="FileUtilitySettings"/> class.

##### `Defaults`
```csharp
public FileUtilitySettings Defaults()
```
Populates default values.

**Returns:** This settings instance.

##### `IsValid`
```csharp
public bool IsValid(Document doc)
```
Validates settings module.

---

#### Fields
- **`ModuleKey`**: `public string ModuleKey => Name;`
- **`this`**: `return this;`
- **`true`**: `return true;`

---

### Interface: `ISettingModule`
**File:** [src/SyntheticShared/Settings/ISettingModule.cs](../src/SyntheticShared/Settings/ISettingModule.cs)

Represents a settings module that can be serialized/deserialized and validated.

#### Properties
- **`ModuleKey`**: `string ModuleKey { get; }`
  *Description:* The unique key identifying the settings module.

#### Methods
##### `IsValid`
```csharp
bool IsValid(Document doc);
```
Validates the settings module settings within the context of the active document.

**Parameters:**
- `doc`: The Revit Document context.

**Returns:** True if settings are valid; otherwise, false.

---

### Class: `LegacySettingsMigration`
**File:** [src/SyntheticShared/Settings/LegacySettingsMigration.cs](../src/SyntheticShared/Settings/LegacySettingsMigration.cs)

Utility class to find and purge legacy settings storage schemas from the Revit document database.

#### Methods
##### `GetLegacyStorageIdSchema`
```csharp
private static Schema GetLegacyStorageIdSchema()
```
##### `GetLegacyConfigSettingsSchema`
```csharp
private static Schema GetLegacyConfigSettingsSchema()
```
##### `PurgeLegacySchemas`
```csharp
public static void PurgeLegacySchemas(Document doc)
```
Scans the document for DataStorage elements matching the legacy GUIDs and purges them.

**Parameters:**
- `doc`: The active Revit document.

---

#### Fields
- **`LegacyStorageIdGuid`**: `private static readonly Guid LegacyStorageIdGuid = new Guid("0b5fd0ef-3558-47d2-81b5-d1918952d2b1");`
- **`LegacyConfigSettingsGuid`**: `private static readonly Guid LegacyConfigSettingsGuid = new Guid("5855a6d8-e694-46e5-bd71-c227a305143b");`
- **`schema`**: `Schema schema = Schema.Lookup(LegacyStorageIdGuid);`
- **`builder`**: `SchemaBuilder builder = new SchemaBuilder(LegacyStorageIdGuid);`
- **`schema`**: `Schema schema = Schema.Lookup(LegacyConfigSettingsGuid);`
- **`builder`**: `SchemaBuilder builder = new SchemaBuilder(LegacyConfigSettingsGuid);`
- **`legacyStorageIdSchema`**: `Schema legacyStorageIdSchema = GetLegacyStorageIdSchema();`
- **`legacyConfigSchema`**: `Schema legacyConfigSchema = GetLegacyConfigSettingsSchema();`
- **`elementsToDelete`**: `var elementsToDelete = new List<ElementId>();`
- **`idEntity`**: `Entity idEntity = dataStorage.GetEntity(legacyStorageIdSchema);`
- **`configEntity`**: `Entity configEntity = dataStorage.GetEntity(legacyConfigSchema);`

---

### Class: `MaterialLibrarySettings`
**File:** [src/SyntheticShared/Settings/MaterialLibrarySettings.cs](../src/SyntheticShared/Settings/MaterialLibrarySettings.cs)

Configuration settings for the material library path.

#### Properties
- **`LibraryFolderPath`**: `public string LibraryFolderPath { get; set; }`
  *Description:* Gets or sets the folder path to the material library.

#### Methods
##### `MaterialLibrarySettings`
```csharp
public MaterialLibrarySettings() { }
```
Initializes a new instance of the <see cref="MaterialLibrarySettings"/> class.

##### `Defaults`
```csharp
public MaterialLibrarySettings Defaults()
```
Populates default values.

**Returns:** This settings instance.

##### `IsValid`
```csharp
public bool IsValid(Document doc)
```
Validates that the folder path exists.

---

#### Fields
- **`ModuleKey`**: `public string ModuleKey => Name;`
- **`this`**: `return this;`

---

### Class: `ProjectMaterialSettings`
**File:** [src/SyntheticShared/Settings/ProjectMaterialSettings.cs](../src/SyntheticShared/Settings/ProjectMaterialSettings.cs)

Configuration settings for project-specific material paths.

#### Properties
- **`DefaultRelativePath`**: `public string DefaultRelativePath { get; set; }`
  *Description:* Gets or sets the default relative path.
- **`OverrideFolderPath`**: `public string OverrideFolderPath { get; set; }`
  *Description:* Gets or sets the override folder path.

#### Methods
##### `ProjectMaterialSettings`
```csharp
public ProjectMaterialSettings() { }
```
Initializes a new instance of the <see cref="ProjectMaterialSettings"/> class.

##### `Defaults`
```csharp
public ProjectMaterialSettings Defaults()
```
Populates default values.

**Returns:** This settings instance.

##### `GetResolvedPath`
```csharp
public string GetResolvedPath(Document doc)
```
Resolves the absolute directory path of the materials.

**Parameters:**
- `doc`: The active Revit document.

**Returns:** The resolved absolute folder path, or null.

##### `IsValid`
```csharp
public bool IsValid(Document doc)
```
Validates that the resolved folder path exists.

---

#### Fields
- **`ModuleKey`**: `public string ModuleKey => Name;`
- **`this`**: `return this;`
- **`OverrideFolderPath`**: `return OverrideFolderPath;`
- **`null`**: `return null;`
- **`null`**: `return null;`
- **`null`**: `return null;`
- **`basePath`**: `string basePath = null;`
- **`centralModelPath`**: `ModelPath centralModelPath = doc.GetWorksharingCentralModelPath();`
- **`null`**: `return null;`
- **`dir`**: `string dir = Path.GetDirectoryName(basePath);`
- **`relative`**: `string relative = DefaultRelativePath ?? ".\\Materials\\";`
- **`null`**: `return null;`
- **`path`**: `string path = GetResolvedPath(doc);`

---

### Class: `SettingsManager`
**File:** [src/SyntheticShared/Settings/SettingsManager.cs](../src/SyntheticShared/Settings/SettingsManager.cs)

Manager class responsible for lazy-loading, caching, retrieving, and saving in-document settings modules.

#### Methods
##### `GetCacheKey`
```csharp
private static string GetCacheKey(Document doc, string moduleKey)
```
##### `WriteEntity`
```csharp
void WriteEntity(Document d, DataStorage targetStorage, string key, string json)
```
##### `Delete`
```csharp
public static void Delete(Document doc, string moduleKey)
```
Deletes the extensible storage element for the given settings module in the Revit document.

**Parameters:**
- `doc`: The Revit Document context.
- `moduleKey`: The key of the settings module.

##### `CheckSyncDrift`
```csharp
public static bool CheckSyncDrift(Document doc, out string linkedFilePath)
```
Checks if there is a sync drift between the document settings and the linked external JSON file.

**Parameters:**
- `doc`: The Revit Document context.
- `linkedFilePath`: Output containing the linked file path if drift exists.

**Returns:** True if drift is detected; otherwise, false.

##### `ExportAllToFile`
```csharp
public static void ExportAllToFile(Document doc, string filePath)
```
Exports all active settings from the Document Extensible Storage (excluding SyncSettings) to a JSON file.

**Parameters:**
- `doc`: The Revit Document context.
- `filePath`: The path of the destination JSON file.

##### `ImportAllFromFile`
```csharp
public static void ImportAllFromFile(Document doc, string filePath)
```
Imports settings from a JSON file and overwrites the Revit document's Extensible Storage.

**Parameters:**
- `doc`: The Revit Document context.
- `filePath`: The path of the source JSON file.

---

#### Fields
- **`_lock`**: `private static readonly object _lock = new object();`
- **`instance`**: `T instance = new T();`
- **`moduleKey`**: `string moduleKey = instance.ModuleKey;`
- **`cacheKey`**: `string cacheKey = GetCacheKey(doc, moduleKey);`
- **`settings`**: `T settings = default;`
- **`storageName`**: `string storageName = "Synthetic_" + moduleKey;`
- **`targetStorage`**: `DataStorage targetStorage = dataStorages.FirstOrDefault(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));`
- **`schema`**: `Schema schema = SyntheticSettingsJsonSchema.GetSchema();`
- **`entity`**: `Entity entity = targetStorage.GetEntity(schema);`
- **`jsonData`**: `string jsonData = entity.Get<string>(SyntheticSettingsJsonSchema.JsonDataFieldName);`
- **`defaultPath`**: `string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");`
- **`defaultConfig`**: `Config defaultConfig = Config.ReadFromFile(defaultPath);`
- **`defaultsMethod`**: `var defaultsMethod = typeof(T).GetMethod("Defaults");`
- **`settings`**: `return settings;`
- **`moduleKey`**: `string moduleKey = setting.ModuleKey;`
- **`cacheKey`**: `string cacheKey = GetCacheKey(doc, moduleKey);`
- **`jsonData`**: `string jsonData = JsonConvert.SerializeObject(setting, Formatting.Indented);`
- **`schema`**: `Schema schema = SyntheticSettingsJsonSchema.GetSchema();`
- **`entity`**: `Entity entity = new Entity(schema);`
- **`storageName`**: `string storageName = "Synthetic_" + moduleKey;`
- **`targetStorage`**: `DataStorage targetStorage = dataStorages.FirstOrDefault(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));`
- **`cacheKey`**: `string cacheKey = GetCacheKey(doc, moduleKey);`
- **`storageName`**: `string storageName = "Synthetic_" + moduleKey;`
- **`targetStorage`**: `DataStorage targetStorage = dataStorages.FirstOrDefault(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));`
- **`syncSettings`**: `var syncSettings = Get<SyncSettings>(doc);`
- **`false`**: `return false;`
- **`externalJson`**: `string externalJson = File.ReadAllText(linkedFilePath);`
- **`documentJson`**: `string documentJson = JsonConvert.SerializeObject(docSettings, Formatting.Indented);`
- **`extDict`**: `var extDict = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>>(externalJson);`
- **`docDict`**: `var docDict = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>>(documentJson);`
- **`false`**: `return false;`
- **`jsonText`**: `string jsonText = JsonConvert.SerializeObject(docSettings, Formatting.Indented);`
- **`jsonText`**: `string jsonText = File.ReadAllText(filePath);`
- **`dict`**: `var dict = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>>(jsonText);`
- **`settings`**: `var settings = worksetToken.ToObject<WorksetSettings>();`
- **`settings`**: `var settings = viewAutoNumToken.ToObject<ViewAutoNumSettings>();`
- **`settings`**: `var settings = materialLibToken.ToObject<MaterialLibrarySettings>();`
- **`settings`**: `var settings = projectMatToken.ToObject<ProjectMaterialSettings>();`
- **`settings`**: `var settings = fileUtilToken.ToObject<FileUtilitySettings>();`

---

### Class: `SyncSettings`
**File:** [src/SyntheticShared/Settings/SyncSettings.cs](../src/SyntheticShared/Settings/SyncSettings.cs)

Configuration settings for project configuration synchronization.

#### Properties
- **`LinkedFilePath`**: `public string LinkedFilePath { get; set; }`
  *Description:* Gets or sets the path to the linked external settings JSON file.

#### Methods
##### `SyncSettings`
```csharp
public SyncSettings() { }
```
Initializes a new instance of the <see cref="SyncSettings"/> class.

##### `IsValid`
```csharp
public bool IsValid(Document doc)
```
Validates the settings module within the document context.

**Parameters:**
- `doc`: The Revit Document.

**Returns:** True if settings are valid; otherwise, false.

---

#### Fields
- **`ModuleKey`**: `public string ModuleKey => Name;`

---

### Class: `SyntheticSettingsJsonSchema`
**File:** [src/SyntheticShared/Settings/SyntheticSettingsJsonSchema.cs](../src/SyntheticShared/Settings/SyntheticSettingsJsonSchema.cs)

Schema definition for storing settings modules as serialized JSON string payloads inside Revit extensible storage.

#### Methods
##### `GetSchema`
```csharp
public static Schema GetSchema()
```
Retrieves the existing settings schema or constructs and registers it if missing.

**Returns:** The registered Autodesk.Revit.DB.ExtensibleStorage.Schema.

---

#### Fields
- **`SchemaGuid`**: `private static readonly Guid SchemaGuid = new Guid("8a07153a-c85c-444f-9e79-50c117d91e60");`
- **`schema`**: `Schema schema = Schema.Lookup(SchemaGuid);`
- **`schema`**: `return schema;`
- **`schemaBuilder`**: `SchemaBuilder schemaBuilder = new SchemaBuilder(SchemaGuid);`

---

### Class: `ViewAutoNumSettings`
**File:** [src/SyntheticShared/Settings/ViewAutoNumSettings.cs](../src/SyntheticShared/Settings/ViewAutoNumSettings.cs)

Settings for Autonumbering views.

#### Properties
- **`ModuleKey`**: `public string ModuleKey { get { return Name; } }`
  *Description:* Gets the settings module key.
- **`defaultViewAutoNumFamily`**: `internal string defaultViewAutoNumFamily { get { return "Titleblock Grid Location Marker"; } }`
- **`defaultViewAutoNumFamilyType`**: `internal string defaultViewAutoNumFamilyType { get { return "Location Marker INC Titleblock - CD"; } }`
- **`defaultViewAutoNumXGridName`**: `internal string defaultViewAutoNumXGridName { get { return "Grid Size X Direction"; } }`
- **`defaultViewAutoNumYGridName`**: `internal string defaultViewAutoNumYGridName { get { return "Grid Size Y Direction"; } }`
- **`ViewAutoNumFamily`**: `public string ViewAutoNumFamily { get; set; }`
  *Description:* Gets or sets the family name used for view autonumbering.
- **`ViewAutoNumFamilyType`**: `public string ViewAutoNumFamilyType { get; set; }`
  *Description:* Gets or sets the family type name used for view autonumbering.
- **`ViewAutoNumXGridName`**: `public string ViewAutoNumXGridName { get; set; }`
  *Description:* Gets or sets the X grid spacing parameter name.
- **`ViewAutoNumYGridName`**: `public string ViewAutoNumYGridName { get; set; }`
  *Description:* Gets or sets the Y grid spacing parameter name.

#### Methods
##### `ViewAutoNumSettings`
```csharp
public ViewAutoNumSettings() { }
```
Constructor

##### `ViewAutoNumSettings`
```csharp
public ViewAutoNumSettings(string family, string familytype, string xGridName, string yGridName)
```
Constructor  <param name="family">Name of family to serve as the origin</param> <param name="familytype">Name of the family type</param> <param name="xGridName">Name of the parameter to determine the X grid spacing</param> <param name="yGridName">Name of the parameter to determine the Y grid spacing</param>

##### `Defaults`
```csharp
public ViewAutoNumSettings Defaults()
```
Reset the ViewAutoNumSettings properties to the Defaults

**Returns:** The ViewRenumberSettings to allow for chaining

##### `IsValid`
```csharp
public bool IsValid (Document doc)
```
Checks if all the setting values exist and the family and type are loaded in the project.

**Parameters:**
- `doc`: The Revit Document context.

**Returns:** True if settings are valid; otherwise, false.

---

#### Fields
- **`this`**: `return this;`
- **`false`**: `return false;`
- **`false`**: `return false;`

---

### Class: `WorksetSettings`
**File:** [src/SyntheticShared/Settings/WorksetSettings.cs](../src/SyntheticShared/Settings/WorksetSettings.cs)

Configuration settings for worksets, including excel mapping files and group configurations.

#### Properties
- **`ModuleKey`**: `public string ModuleKey { get { return Name; } }`
  *Description:* Gets the settings module key.
- **`defaultWorksetFile`**: `internal string defaultWorksetFile { get { return "SyntheticWorksets.xlsx"; } }`
- **`defaultWorksetGroup`**: `internal string defaultWorksetGroup { get { return "Worksets"; } }`
- **`WorksetFile`**: `public string WorksetFile { get; set; }`
  *Description:* Gets or sets the workset configuration Excel filename.
- **`WorksetPath`**: `public string WorksetPath { get; set; }`
  *Description:* Gets or sets the path to the workset Excel file.
- **`WorksetGroup`**: `public string WorksetGroup { get; set; }`
  *Description:* Gets or sets the workset group name.

#### Methods
##### `WorksetSettings`
```csharp
public WorksetSettings () { }
```
Initializes a new instance of the <see cref="WorksetSettings"/> class.

##### `WorksetSettings`
```csharp
public WorksetSettings (string file, string path = null, string group = null)
```
Constructor using individual parameters to create  <param name="file">File name as a string.</param> <param name="path">Path to file as a string.  If null, app will use the assembly path instead.</param> <param name="group">Name of workset group as a string.</param>

##### `Defaults`
```csharp
public WorksetSettings Defaults ()
```
App default WorksetSettings object

**Returns:** WorksetSettings object with app default settings.

##### `PathOrDefault`
```csharp
public string PathOrDefault()
```
If the WorksetPath setting is a path, return the path, otherwise return the addin's path.

**Returns:** Path as a string

##### `FullPath`
```csharp
public string FullPath()
```
Takes the WorksetPath and WorksetFile to create a path to the file.  If the WorksetPath is empty, uses the addin's path.

**Returns:** Full path to the file as a string

##### `IsValid`
```csharp
public bool IsValid (Document doc)
```
Validates the settings module within the document context.

**Parameters:**
- `doc`: The Revit Document.

**Returns:** True if settings are valid; otherwise, false.

##### `IsSettingsEmpty`
```csharp
public bool IsSettingsEmpty()
```
Checks if the settings are empty (i.e. no WorksetFile is defined).

**Returns:** True if WorksetFile is null or empty.

##### `FileExists`
```csharp
public bool FileExists ()
```
Checks if the Workset File exists at the path

**Returns:** True if the Workset File exists at the path, false if the Settings aren't valid or the file doesn't exist at the path.

##### `WorksetsByExcel`
```csharp
public WorksetUtil WorksetsByExcel ()
```
Loads workset configurations from the Excel file specified in the settings.

**Returns:** A WorksetUtil utility class containing loaded workset configurations.

---

#### Fields
- **`this`**: `return this;`
- **`path`**: `string path = this.FullPath();`
- **`worksetUtil`**: `WorksetUtil worksetUtil = null;`
- **`path`**: `string path = this.FullPath();`
- **`worksetGroup`**: `string worksetGroup = this.WorksetGroup;`
- **`excel`**: `Excel excel = new Excel(path, worksetGroup);`
- **`worksetUtil`**: `return worksetUtil;`

---

## Namespace: `Synthetic.UI`

### Class: `EnumToBooleanConverter`
**File:** [src/SyntheticShared/UI/EnumToBooleanConverter.cs](../src/SyntheticShared/UI/EnumToBooleanConverter.cs)

Converts an enum value to a boolean for binding RadioButtons to Enum properties.

#### Methods
##### `Convert`
```csharp
public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
```
Converts an enum value to a boolean. Returns true if the value equals the parameter.

**Parameters:**
- `value`: The enum value produced by the binding source.
- `targetType`: The type of the binding target property.
- `parameter`: The converter parameter to compare against.
- `culture`: The culture to use in the converter.

**Returns:** True if the value equals the parameter; otherwise, false.

##### `ConvertBack`
```csharp
public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
```
Converts a boolean back to the enum value. Returns the parameter if value is true.

**Parameters:**
- `value`: The boolean value produced by the binding target.
- `targetType`: The type to convert to.
- `parameter`: The converter parameter specifying the target enum value.
- `culture`: The culture to use in the converter.

**Returns:** The parameter value if true; otherwise DependencyProperty.UnsetValue.

---

#### Fields
- **`parameter`**: `return parameter;`

---

### Class: `FileDialogHelper`
**File:** [src/SyntheticShared/UI/FileDialogHelper.cs](../src/SyntheticShared/UI/FileDialogHelper.cs)

Helper methods for showing Win32 file and folder dialogs with proper window parenting.

#### Methods
##### `SelectFolder`
```csharp
public static string SelectFolder(IntPtr ownerHandle, string title, string initialPath = null)
```
Displays a folder browser dialog parented to the specified window handle.

**Parameters:**
- `ownerHandle`: The parent window handle (typically Revit's MainWindowHandle).
- `title`: The description/title to show in the folder browser dialog.
- `initialPath`: The initial folder path to display.

**Returns:** The selected folder path, or null if the selection was cancelled.

---

#### Fields
- **`null`**: `return null;`

---

### Class: `RevitWindowHelper`
**File:** [src/SyntheticShared/UI/RevitWindowHelper.cs](../src/SyntheticShared/UI/RevitWindowHelper.cs)

Helper methods for managing WPF window parenting/owners within Revit.

#### Methods
##### `SetOwner`
```csharp
public static void SetOwner(Window wpfWindow, IntPtr revitMainWindowHandle)
```
Sets the parent owner window of a WPF window to the main Revit application window.

**Parameters:**
- `wpfWindow`: The WPF window to parent.
- `revitMainWindowHandle`: The window handle of the main Revit window.

---

### Class: `SyntheticRibbon`
**File:** [src/SyntheticShared/UI/SyntheticRibbon.cs](../src/SyntheticShared/UI/SyntheticRibbon.cs)

Creation and management class for the Synthetic Revit ribbon interface.

#### Methods
##### `Create`
```csharp
public static void Create (UIControlledApplication appControlled, string path) {
```
Creates a new Ribbon Tab for the App along with panels and buttons.

**Parameters:**
- `appControlled`: The Revit UIControlledApplication object
- `path`: Path to the DLL assembly

---

#### Fields
- **`assetsDir`**: `string assetsDir = Path.Combine(Path.GetDirectoryName(path), "Assets");`
- **`panelViewsTags`**: `RibbonPanel panelViewsTags = appControlled.CreateRibbonPanel(TabName, "Views & Tags");`
- **`panelLegends`**: `RibbonPanel panelLegends = appControlled.CreateRibbonPanel(TabName, "Legends");`
- **`panelModel`**: `RibbonPanel panelModel = appControlled.CreateRibbonPanel(TabName, "Model Management");`
- **`panelWorkset`**: `RibbonPanel panelWorkset = appControlled.CreateRibbonPanel(TabName, "Worksets & Setup");`
- **`panelPublish`**: `RibbonPanel panelPublish = appControlled.CreateRibbonPanel(TabName, "Publish");`
- **`panelSettings`**: `RibbonPanel panelSettings = appControlled.CreateRibbonPanel(TabName, "Admin & Settings");`
- **`sbd1`**: `SplitButtonData sbd1 = new SplitButtonData("Synthetic.Split.Schema", "Ext. Storage");`
- **`sb1`**: `SplitButton sb1 = panelSettings.AddItem(sbd1) as SplitButton;`

---

### Class: `Win32WindowWrapper`
**File:** [src/SyntheticShared/UI/FileDialogHelper.cs](../src/SyntheticShared/UI/FileDialogHelper.cs)

#### Properties
- **`Handle`**: `public IntPtr Handle { get; }`

#### Methods
##### `Win32WindowWrapper`
```csharp
public Win32WindowWrapper(IntPtr handle) => Handle = handle;
```
---

## Namespace: `Synthetic.Utilities`

### Class: `CoordinateUtility`
**File:** [src/SyntheticShared/Utilities/CoordinateUtility.cs](../src/SyntheticShared/Utilities/CoordinateUtility.cs)

Provides linear algebra transformation mapping for tag placement relative to host elements.

#### Methods
##### `GetLocalOffset`
```csharp
public static XYZ GetLocalOffset(FamilyInstance host, XYZ worldPoint)
```
Converts a World Coordinate point into the Local Coordinate system of the host element.

##### `GetWorldPoint`
```csharp
public static XYZ GetWorldPoint(FamilyInstance host, XYZ localOffset)
```
Converts a Local Coordinate offset back into the global World Coordinate space of the model.

##### `CalculateTagOrientation`
```csharp
public static TagOrientation CalculateTagOrientation(FamilyInstance host, TagTemplate template)
```
Calculates the tag's target orientation based on the family instance's current rotation relative to the template's baseline.

---

#### Fields
- **`transform`**: `Transform transform = host.GetTransform();`
- **`origin`**: `XYZ origin = transform.Origin;`
- **`handDir`**: `XYZ handDir = host.HandOrientation;`
- **`faceDir`**: `XYZ faceDir = host.FacingOrientation;`
- **`upDir`**: `XYZ upDir = transform.BasisZ;`
- **`translation`**: `XYZ translation = worldPoint - origin;`
- **`x`**: `double x = translation.DotProduct(handDir);`
- **`y`**: `double y = translation.DotProduct(faceDir);`
- **`z`**: `double z = translation.DotProduct(upDir);`
- **`transform`**: `Transform transform = host.GetTransform();`
- **`origin`**: `XYZ origin = transform.Origin;`
- **`handDir`**: `XYZ handDir = host.HandOrientation;`
- **`faceDir`**: `XYZ faceDir = host.FacingOrientation;`
- **`upDir`**: `XYZ upDir = transform.BasisZ;`
- **`vTmpl`**: `XYZ vTmpl = new XYZ(template.HostHandX, template.HostHandY, template.HostHandZ);`
- **`vTgt`**: `XYZ vTgt = host.HandOrientation;`
- **`vTmpl2d`**: `XYZ vTmpl2d = new XYZ(vTmpl.X, vTmpl.Y, 0);`
- **`vTgt2d`**: `XYZ vTgt2d = new XYZ(vTgt.X, vTgt.Y, 0);`
- **`cosTheta`**: `double cosTheta = vTmpl2d.DotProduct(vTgt2d);`
- **`absCos`**: `double absCos = System.Math.Abs(cosTheta);`

---

### Class: `CopyUseDestination`
**File:** [src/SyntheticShared/Utilities/LegendsUtil.cs](../src/SyntheticShared/Utilities/LegendsUtil.cs)

Overrides IDuplicateTypeNamsHandler for use in converting Drafting Views to Legends.

### Class: `LegendsUtil`
**File:** [src/SyntheticShared/Utilities/LegendsUtil.cs](../src/SyntheticShared/Utilities/LegendsUtil.cs)

Utility functions for dealing with legends

#### Methods
##### `ConvertFromDrafting`
```csharp
public static View ConvertFromDrafting(View drafting, Synthetic.Views.ProgressWindow progressWindow, ref bool warningTripped)
```
Convert a Drafting view to a Legend

**Parameters:**
- `drafting`: Drafting View
- `progressWindow`: Progress Window for updates and cancellation check
- `warningTripped`: Flag set to true if any constraint warnings were suppressed

**Returns:** A Legend view

##### `ConvertToDrafting`
```csharp
public static View ConvertToDrafting(View legend)
```
Convert a Legend view to a Drafting view

**Parameters:**
- `legend`: Legend View

**Returns:** A Drafting view

---

#### Fields
- **`null`**: `return null;`
- **`doc`**: `Document doc = drafting.Document;`
- **`planesGroup`**: `Group planesGroup = null;`
- **`options`**: `FailureHandlingOptions options = t1.GetFailureHandlingOptions();`
- **`preprocessor`**: `SuppressConstraintsPreprocessor preprocessor = new SuppressConstraintsPreprocessor();`
- **`planeIds`**: `ICollection<ElementId> planeIds = ungroupedPlanes.Select(rp => rp.Id).ToList();`
- **`uniqueGroupName`**: `string uniqueGroupName = "RefPlanes_" + drafting.Name + "_" + Guid.NewGuid().ToString().Substring(0, 8);`
- **`groupsToRestoreInDrafting`**: `List<ElementId> groupsToRestoreInDrafting = new List<ElementId>();`
- **`line`**: `Line line = Line.CreateBound(XYZ.Zero, new XYZ(0.1, 0, 0));`
- **`detailLine`**: `DetailCurve detailLine = doc.Create.NewDetailCurve(drafting, line);`
- **`tempGroupInstance`**: `Group tempGroupInstance = doc.Create.NewGroup(new List<ElementId> { detailLine.Id });`
- **`tempGroupType`**: `GroupType tempGroupType = tempGroupInstance.GroupType;`
- **`newLegend`**: `View newLegend = null;`
- **`copiedElementIds`**: `List<ElementId> copiedElementIds = null;`
- **`draftingElements`**: `IList<Element> draftingElements = new FilteredElementCollector(doc, drafting.Id).ToElements();`
- **`elementsToCopy`**: `List<ElementId> elementsToCopy = new List<ElementId>();`
- **`copyPasteOptions`**: `CopyPasteOptions copyPasteOptions = new CopyPasteOptions();`
- **`newName`**: `string newName = drafting.Name;`
- **`suffix`**: `int suffix = 1;`
- **`dg`**: `Group dg = doc.GetElement(dgId) as Group;`
- **`sheetId`**: `ElementId sheetId = oldViewport.SheetId;`
- **`boxCenter`**: `XYZ boxCenter = oldViewport.GetBoxCenter();`
- **`typeId`**: `ElementId typeId = oldViewport.GetTypeId();`
- **`newViewport`**: `Viewport newViewport = Viewport.Create(doc, sheetId, newLegend.Id, boxCenter);`
- **`newLegend`**: `return newLegend;`
- **`newDrafting`**: `View newDrafting = null;`
- **`doc`**: `Document doc = legend.Document;`
- **`legendElements`**: `IList<Element> legendElements = new FilteredElementCollector(doc, legend.Id).ToElements();`
- **`elementsToCopy`**: `List<ElementId> elementsToCopy = new List<ElementId>();`
- **`viewType`**: `ViewFamilyType viewType = null;`
- **`copyPasteOptions`**: `CopyPasteOptions copyPasteOptions = new CopyPasteOptions();`
- **`newName`**: `string newName = legend.Name;`
- **`newDrafting`**: `return newDrafting;`

---

### Class: `MaterialPathUtils`
**File:** [src/SyntheticShared/Utilities/MaterialPathUtils.cs](../src/SyntheticShared/Utilities/MaterialPathUtils.cs)

Utility methods for resolving paths of Revit Material assets.

### Class: `MaterialUtil`
**File:** [src/SyntheticShared/Utilities/MaterialUtil.cs](../src/SyntheticShared/Utilities/MaterialUtil.cs)

Extensions of Dynamo Revit

#### Methods
##### `MaterialUtil`
```csharp
internal MaterialUtil() { }
```
##### `GetByNameDocument`
```csharp
public static revitMaterial GetByNameDocument(string Name, revitDoc Document)
```
Gets a material given its name and document

**Parameters:**
- `Name`: Name of a material
- `Document`: Document to get the material from

**Returns:** A Autodeks.Revit.DB.Material

##### `GetAllMaterials`
```csharp
public static FilteredElementCollector GetAllMaterials(revitDoc Document)
```
Retrieves all material elements in the specified document.

**Parameters:**
- `Document`: The Revit document.

**Returns:** A FilteredElementCollector containing the materials.

##### `GetMaterialBitmapPaths`
```csharp
public static List<string> GetMaterialBitmapPaths(revitMaterial Material)
```
Gets all the connected files with paths associated with a material.

**Parameters:**
- `Material`: A revit material

**Returns:** Full file names with paths

##### `if`
```csharp
else if ((MaterialUtil.IsPathFullyQualified(filePath)
```
##### `_ReadAssetPropertyPaths`
```csharp
private static List<string> _ReadAssetPropertyPaths(AssetProperty assetProperty)
```
Checks if a AssetProperty has connected properties and if those connected properties have a bitmap path, it returns the path.

**Parameters:**
- `assetProperty`: A Revit AssetProperty element

**Returns:** Bitmap paths

##### `IsPathFullyQualified`
```csharp
private static bool IsPathFullyQualified(string path)
```
##### `IsDirectorySeperator`
```csharp
private static bool IsDirectorySeperator(char c) => c == System.IO.Path.DirectorySeparatorChar | c == System.IO.Path.AltDirectorySeparatorChar;
```
##### `IsValidDriveChar`
```csharp
private static bool IsValidDriveChar(char c) => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z';
```
---

#### Fields
- **`collector`**: `return collector;`
- **`paths`**: `List<string> paths = null;`
- **`renderingAsset`**: `Asset renderingAsset = assetElem.GetRenderingAsset();`
- **`property`**: `AssetProperty property = renderingAsset.Get(idx);`
- **`tempPath`**: `List<string> tempPath = _ReadAssetPropertyPaths(property);`
- **`paths`**: `return paths;`
- **`results`**: `List<List<string>> results = null;`
- **`document`**: `revitDoc document = Material.Document;`
- **`pathsReplaced`**: `List<string> pathsReplaced = new List<string>();`
- **`pathsNotReplaced`**: `List<string> pathsNotReplaced = new List<string>();`
- **`file`**: `string file = null;`
- **`filePath`**: `string filePath = null;`
- **`newFilePath`**: `string newFilePath = null;`
- **`renderAsset`**: `Asset renderAsset = null;`
- **`property`**: `AssetProperty property = renderAsset.Get(idx);`
- **`connectedAsset`**: `Asset connectedAsset = property.GetSingleConnectedAsset();`
- **`bitmapProperty`**: `AssetPropertyString bitmapProperty = connectedAsset.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString;`
- **`separator`**: `char[] separator = { '|' };`
- **`paths`**: `List<string> paths = new List<string>();`
- **`connectedAsset`**: `Asset connectedAsset = assetProperty.GetSingleConnectedAsset();`
- **`bitmapProperty`**: `AssetPropertyString bitmapProperty = connectedAsset.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString;`
- **`path`**: `string path = bitmapProperty.Value;`
- **`num`**: `int num = assetProperty.NumberOfConnectedProperties;`
- **`paths`**: `return paths;`
- **`false`**: `return false; //Default`

---

### Class: `MergeAnalysisEngine`
**File:** [src/SyntheticShared/Utilities/MergeAnalysisEngine.cs](../src/SyntheticShared/Utilities/MergeAnalysisEngine.cs)

Analysis engine that scans the document for duplicates and calculates merge recommendations.

#### Methods
##### `GetBaseName`
```csharp
public static string GetBaseName(string name)
```
Retrieves the base name from a name string by stripping optional separators and trailing numbers.

**Parameters:**
- `name`: The name to process.

**Returns:** The base name string without trailing numbers or separators.

##### `GetParameterValueString`
```csharp
public static string GetParameterValueString(Parameter p)
```
Gets a string representation of a Parameter's storage type and value.

**Parameters:**
- `p`: The Parameter to read.

**Returns:** A string representation of the parameter value.

##### `IsIdentityParameter`
```csharp
public static bool IsIdentityParameter(string name)
```
Determines if the specified parameter name corresponds to an identity parameter.

**Parameters:**
- `name`: The name of the parameter to check.

**Returns:** True if the parameter name is an identity parameter; otherwise, false.

##### `GetElementCategoryName`
```csharp
public static string GetElementCategoryName(Document doc, Element elem)
```
Gets the category name of the specified Revit element.

**Parameters:**
- `doc`: The active Revit document.
- `elem`: The element whose category name should be retrieved.

**Returns:** The name of the category, or a default name if category is not found.

##### `if`
```csharp
else if (elem is GroupType gt)
```
##### `if`
```csharp
else if (elem is Autodesk.Revit.DB.Group g)
```
##### `if`
```csharp
else if (elem is AssemblyType at)
```
##### `if`
```csharp
else if (elem is AssemblyInstance ai)
```
##### `GetTargetElement`
```csharp
public static Element GetTargetElement(Document doc, ElementId id)
```
Retrieves the target mergeable element (e.g. Family, GroupType, AssemblyType) from a given ElementId.

**Parameters:**
- `doc`: The active Revit document.
- `id`: The ElementId to resolve.

**Returns:** The resolved target element, or null if not found.

##### `BuildClustersFromElements`
```csharp
private static ObservableCollection<DuplicateClusterModel> BuildClustersFromElements(Document doc, List<Element> elements, CancellationToken token)
```
##### `if`
```csharp
else if (elem is GroupType gt)
```
##### `if`
```csharp
else if (elem is AssemblyType at)
```
##### `if`
```csharp
else if (elem is ElementType et)
```
##### `if`
```csharp
else if (firstInstance is FamilyInstance fi)
```
##### `if`
```csharp
else if (elem is GroupType gType)
```
##### `if`
```csharp
else if (elem is AssemblyType aType)
```
##### `if`
```csharp
else if (elem is ElementType eType)
```
##### `RunFastScan`
```csharp
public static ObservableCollection<DuplicateClusterModel> RunFastScan(Document doc, CancellationToken token)
```
Scans the Revit document quickly for duplicate elements.

**Parameters:**
- `doc`: The active Revit document.
- `token`: A cancellation token to monitor for cancellation requests.

**Returns:** A collection of duplicate cluster models found during the scan.

##### `BuildClustersFromElements`
```csharp
return BuildClustersFromElements(doc, elements, token);
```
##### `RunTargetedScan`
```csharp
public static ObservableCollection<DuplicateClusterModel> RunTargetedScan(Document doc, ICollection<ElementId> selectedIds, CancellationToken token)
```
Scans the Revit document specifically for duplicate elements of the same types/categories as the selected elements.

**Parameters:**
- `doc`: The active Revit document.
- `selectedIds`: A collection of ElementIds specifying the user selection to scan against.
- `token`: A cancellation token to monitor for cancellation requests.

**Returns:** A collection of duplicate cluster models matching the targeted elements.

##### `BuildClustersFromElements`
```csharp
return BuildClustersFromElements(doc, elements, token);
```
##### `RunDeepScan`
```csharp
public static void RunDeepScan(Document doc, DuplicateClusterModel cluster, CancellationToken token)
```
Performs a deep comparison of the schemas and geometry in the given duplicate cluster.

**Parameters:**
- `doc`: The active Revit document.
- `cluster`: The duplicate cluster model to analyze.
- `token`: A cancellation token to monitor for cancellation requests.

##### `if`
```csharp
else if ((firstBBox == null) != (currentBBox == null))
```
##### `GenerateRecommendations`
```csharp
public static void GenerateRecommendations(DuplicateClusterModel cluster)
```
Generates recommended mapping actions and highlights parameter conflicts within the duplicate cluster.

**Parameters:**
- `cluster`: The duplicate cluster model for which recommendations are generated.

---

#### Fields
- **`match`**: `var match = Regex.Match(name, @"^(.*?)(?:[\s_#\-\.]+)?\d+$");`
- **`storageType`**: `string storageType = p.StorageType.ToString();`
- **`valueString`**: `string valueString = string.Empty;`
- **`id`**: `ElementId id = p.AsElementId();`
- **`symbolIds`**: `var symbolIds = f.GetFamilySymbolIds();`
- **`firstSymbol`**: `var firstSymbol = doc.GetElement(symbolIds.First()) as FamilySymbol;`
- **`typeId`**: `var typeId = ai.GetTypeId();`
- **`typeElem`**: `var typeElem = doc.GetElement(typeId) as AssemblyType;`
- **`elem`**: `Element elem = doc.GetElement(id);`
- **`symId`**: `var symId = fi.GetTypeId();`
- **`sym`**: `var sym = doc.GetElement(symId) as FamilySymbol;`
- **`typeId`**: `var typeId = ai.GetTypeId();`
- **`elemTypeId`**: `var elemTypeId = elem.GetTypeId();`
- **`typeElem`**: `var typeElem = doc.GetElement(elemTypeId);`
- **`null`**: `return null;`
- **`clusters`**: `var clusters = new ObservableCollection<DuplicateClusterModel>();`
- **`categoryName`**: `string categoryName = categoryGroup.Key;`
- **`items`**: `var items = new List<DuplicateItemModel>();`
- **`instances`**: `var instances = new List<Element>();`
- **`symbolIds`**: `var symbolIds = family.GetFamilySymbolIds();`
- **`symbolIdSet`**: `var symbolIdSet = new HashSet<ElementId>(symbolIds);`
- **`firstInstance`**: `var firstInstance = instances.FirstOrDefault();`
- **`bbox`**: `var bbox = firstInstance.get_BoundingBox(null);`
- **`symbolIds`**: `var symbolIds = familyElem.GetFamilySymbolIds();`
- **`symbols`**: `var symbols = symbolIds?.Select(id => doc.GetElement(id)).OfType<FamilySymbol>().ToList() ?? new List<FamilySymbol>();`
- **`symbolIds`**: `var symbolIds = fam.GetFamilySymbolIds();`
- **`symbol`**: `var symbol = doc.GetElement(sId) as FamilySymbol;`
- **`primaryItem`**: `var primaryItem = items.OrderBy(i => i.ItemName.Length).First();`
- **`clusters`**: `return clusters;`
- **`elements`**: `var elements = new List<Element>();`
- **`classes`**: `var classes = new List<Type> { typeof(Family), typeof(GroupType), typeof(AssemblyType) };`
- **`filter`**: `var filter = new ElementMulticlassFilter(classes);`
- **`collector`**: `var collector = new FilteredElementCollector(doc).WherePasses(filter);`
- **`clusters`**: `var clusters = new ObservableCollection<DuplicateClusterModel>();`
- **`selectedCategories`**: `var selectedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);`
- **`selectedBaseNames`**: `var selectedBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);`
- **`originalElem`**: `var originalElem = doc.GetElement(id);`
- **`target`**: `var target = GetTargetElement(doc, id);`
- **`categoryName`**: `string categoryName = GetElementCategoryName(doc, originalElem);`
- **`clusters`**: `return clusters;`
- **`elements`**: `var elements = new List<Element>();`
- **`classes`**: `var classes = new List<Type> { typeof(Family), typeof(GroupType), typeof(AssemblyType) };`
- **`filter`**: `var filter = new ElementMulticlassFilter(classes);`
- **`collector`**: `var collector = new FilteredElementCollector(doc).WherePasses(filter);`
- **`cat`**: `string cat = GetElementCategoryName(doc, fam);`
- **`cat`**: `string cat = GetElementCategoryName(doc, elem);`
- **`target`**: `var target = GetTargetElement(doc, id);`
- **`typeClass`**: `var typeClass = target.GetType();`
- **`cat`**: `string cat = GetElementCategoryName(doc, et);`
- **`schemaMismatch`**: `bool schemaMismatch = false;`
- **`allTypes`**: `var allTypes = cluster.Items.SelectMany(item => item.Types).ToList();`
- **`firstType`**: `var firstType = allTypes[0];`
- **`currentType`**: `var currentType = allTypes[i];`
- **`paramName`**: `string paramName = kvp.Key;`
- **`currentStorageType`**: `string currentStorageType = kvp.Value.Split(':')[0];`
- **`originMismatch`**: `bool originMismatch = false;`
- **`firstItem`**: `var firstItem = cluster.Items[0];`
- **`firstBBox`**: `var firstBBox = firstItem.BoundingBox;`
- **`firstLoc`**: `var firstLoc = firstItem.Location;`
- **`currentItem`**: `var currentItem = cluster.Items[i];`
- **`currentBBox`**: `var currentBBox = currentItem.BoundingBox;`
- **`currentLoc`**: `var currentLoc = currentItem.Location;`
- **`firstSize`**: `XYZ firstSize = firstBBox.Max - firstBBox.Min;`
- **`currentSize`**: `XYZ currentSize = currentBBox.Max - currentBBox.Min;`
- **`firstCenter`**: `XYZ firstCenter = (firstBBox.Max + firstBBox.Min) * 0.5;`
- **`currentCenter`**: `XYZ currentCenter = (currentBBox.Max + currentBBox.Min) * 0.5;`
- **`firstOffset`**: `XYZ firstOffset = firstLoc - firstCenter;`
- **`currentOffset`**: `XYZ currentOffset = currentLoc - currentCenter;`
- **`primary`**: `var primary = cluster.SelectedPrimary;`
- **`primaryTypes`**: `var primaryTypes = primary.Types;`
- **`tgtType`**: `var tgtType = primaryTypes.FirstOrDefault(t => t.Name.Equals(srcType.Name, StringComparison.Ordinal));`
- **`recommendation`**: `RecommendedAction recommendation = RecommendedAction.Merge;`
- **`allParamNames`**: `var allParamNames = srcType.Parameters.Keys.Union(tgtType.Parameters.Keys).ToList();`
- **`srcRaw`**: `string srcRaw = null;`
- **`tgtRaw`**: `string tgtRaw = null;`
- **`srcStorage`**: `string srcStorage = null, srcVal = null;`
- **`colonIndex`**: `int colonIndex = srcRaw.IndexOf(':');`
- **`tgtStorage`**: `string tgtStorage = null, tgtVal = null;`
- **`colonIndex`**: `int colonIndex = tgtRaw.IndexOf(':');`
- **`isSchemaMismatch`**: `bool isSchemaMismatch = false;`
- **`hasConflict`**: `bool hasConflict = (srcVal != tgtVal);`

---

### Class: `ScopeBoxUtil`
**File:** [src/SyntheticShared/Utilities/ScopeBoxUtil.cs](../src/SyntheticShared/Utilities/ScopeBoxUtil.cs)

Utility methods for querying and modifying Revit Scope Box elements.

#### Methods
##### `GetAllScopeBoxes`
```csharp
public static IList<RevitElem> GetAllScopeBoxes(RevitDoc doc)
```
Retrieves all Scope Boxes from the document.

**Parameters:**
- `doc`: Revit Document

**Returns:** List of Scope Box elements

##### `MoveToWorkset`
```csharp
public static void MoveToWorkset(RevitDoc doc, IList<RevitElem> scopeBoxes, RevitDB.Workset workset)
```
Moves a list of Scope Boxes to a specific workset.

**Parameters:**
- `doc`: Revit Document
- `scopeBoxes`: List of Scope Boxes
- `workset`: Destination Workset

---

### Class: `SearchPaths`
**File:** [src/SyntheticShared/Utilities/SearchPaths.cs](../src/SyntheticShared/Utilities/SearchPaths.cs)

SearchPaths are utility functions that search recursively through a list of paths for a list of files and provides the paths of the found files and a list of files not found.

#### Methods
##### `SearchPaths`
```csharp
public SearchPaths()
```
Creates a new SearchPaths object without any paths or a file library.

##### `SearchPaths`
```csharp
public SearchPaths(List<string> Paths)
```
Creates a new search path object with a library of all the unique files and their paths.  Only the first instance of a filename is included so the order of the Paths give priority.  <param name="Paths">List of Paths to be searched.</param>

##### `AddPaths`
```csharp
public SearchPaths AddPaths(List<string> paths)
```
Adds additional paths and files to an existing SearchPaths object.

**Parameters:**
- `paths`: A List of paths

**Returns:** This SearchPath object

##### `GetFilePath`
```csharp
public string GetFilePath(string File)
```
Searches the paths for a file and returns if path if found, otherwise returns null.

**Parameters:**
- `File`: Name of the file to search for.

**Returns:** A string of the file path if found.  If the file is not found, it returns null.

##### `GetRelativeFilePath`
```csharp
public string GetRelativeFilePath(string File)
```
Searches the paths for a file and returns a relative path if found, otherwise returns null.

**Parameters:**
- `File`: A string that is the path to the a file in the search paths

**Returns:** A string of the relative file path if found.  If the file is not found, it returns null.  The relative path removes the search path from the FilePath

##### `ContainsFile`
```csharp
public bool ContainsFile(string File)
```
Checks if the FileLibrary contains the file.

**Parameters:**
- `File`: A string of the file name.

**Returns:** Returns true if the file is in the File Library, false if it does not.

##### `ContainsPath`
```csharp
public bool ContainsPath(string Path)
```
Checks if the given path is a search path.

**Parameters:**
- `Path`: A string of the path

**Returns:** Returns True if the path is a search path and false if it is not.

##### `CopyFiles`
```csharp
public IDictionary CopyFiles(List<string> Files, string Path, bool Overwrite = false)
```
Copies the files given that are found in the Search Path File Library to a new location.

**Parameters:**
- `Files`: A list of file names to copy
- `Path`: The root path to copy files into
- `Overwrite`: If True, the method will overwrite any files if they already exist. If false, only new files will be copied.

**Returns:** If true, the file was copied, otherwise it was not because it didn't exist or there was an trouble copying the file.

##### `ToString`
```csharp
public override string ToString()
```
Prints the Searchpaths as a string including all the files in the library.

**Returns:** Converts to a string.

---

#### Fields
- **`paths`**: `private List<string> paths;`
- **`newPaths`**: `List<string> newPaths = new List<string>();`
- **`this`**: `return this;`
- **`null`**: `return null;`
- **`relativePath`**: `string relativePath = null;`
- **`FilePath`**: `string FilePath = this.fileLibrary[File];`
- **`relativePath`**: `return relativePath;`
- **`true`**: `return true;`
- **`false`**: `return false;`
- **`true`**: `return true;`
- **`false`**: `return false;`
- **`result`**: `bool result;`
- **`resultsBool`**: `List<bool> resultsBool = new List<bool>();`
- **`filesCopied`**: `List<string> filesCopied = new List<string>();`
- **`filesNotCopied`**: `List<string> filesNotCopied = new List<string>();`
- **`relativeFilePath`**: `string relativeFilePath = GetRelativeFilePath(file);`
- **`relativePath`**: `string relativePath = System.IO.Path.GetDirectoryName(relativeFilePath);`
- **`newPath`**: `string newPath = Path + relativePath;`
- **`newFilePath`**: `string newFilePath = newPath + "\\" + file;`
- **`newPathExists`**: `bool newPathExists = Directory.Exists(newPath);`
- **`newFileExists`**: `bool newFileExists = File.Exists(newFilePath);`
- **`filesAll`**: `List<string> filesAll = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).ToList();`
- **`files`**: `return files;`
- **`filesAll`**: `List<string> filesAll = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).ToList();`
- **`fileName`**: `string fileName = Path.GetFileName(filepath);`
- **`fileLibrary`**: `return fileLibrary;`
- **`t`**: `Type t = typeof(SearchPaths);`
- **`s`**: `string s = t.Namespace + "." + GetType().Name;`
- **`s`**: `return s;`

---

### Class: `SuppressConstraintsPreprocessor`
**File:** [src/SyntheticShared/Utilities/LegendsUtil.cs](../src/SyntheticShared/Utilities/LegendsUtil.cs)

Suppresses constraint deletion warnings when grouping reference planes.

#### Properties
- **`WarningTripped`**: `public bool WarningTripped { get; private set; } = false;`
  *Description:* Gets a value indicating whether a warning was tripped and suppressed.

#### Methods
##### `PreprocessFailures`
```csharp
public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
```
Preprocesses failures to delete warnings.

---

#### Fields
- **`failureMessages`**: `IList<FailureMessageAccessor> failureMessages = failuresAccessor.GetFailureMessages();`
- **`severity`**: `FailureSeverity severity = failure.GetSeverity();`

---

## Namespace: `Synthetic.ViewModels`

### Class: `CategorySelectionItem`
**File:** [src/SyntheticShared/ViewModels/ExportStylesViewModel.cs](../src/SyntheticShared/ViewModels/ExportStylesViewModel.cs)

Represents a category that can be checked or unchecked for export.

#### Properties
- **`Name`**: `public string Name { get; set; }`
  *Description:* Gets or sets the name of the category.
- **`Id`**: `public ElementId Id { get; set; }`
  *Description:* Gets or sets the ElementId of the category.

#### Fields
- **`_isChecked`**: `private bool _isChecked;`

---

### Class: `CheckableItem`
**File:** [src/SyntheticShared/ViewModels/ListByCheckboxViewModel.cs](../src/SyntheticShared/ViewModels/ListByCheckboxViewModel.cs)

Represents an item in a checklist that can be checked or unchecked.

#### Methods
##### `CheckableItem`
```csharp
public CheckableItem(string name, bool isChecked = false, Action<CheckableItem> onCheckChanged = null)
```
Initializes a new instance of the <see cref="CheckableItem"/> class.  <param name="name">The display name of the item.</param> <param name="isChecked">The initial check state of the item.</param> <param name="onCheckChanged">Action to invoke when checked status changes.</param>

##### `CheckableItem`
```csharp
public CheckableItem(string name, ElementId value, bool isChecked = false, Action<CheckableItem> onCheckChanged = null)
```
Initializes a new instance of the <see cref="CheckableItem"/> class.  <param name="name">The display name of the item.</param> <param name="value">The ElementId associated with the item.</param> <param name="isChecked">The initial check state of the item.</param> <param name="onCheckChanged">Action to invoke when checked status changes.</param>

---

#### Fields
- **`_name`**: `private string _name;`
- **`_value`**: `private ElementId _value;`
- **`_isChecked`**: `private bool _isChecked;`
- **`_onCheckChanged`**: `private readonly Action<CheckableItem> _onCheckChanged;`

---

### Class: `CompoundLayerRowVM`
**File:** [src/SyntheticShared/ViewModels/NestedDataEditorViewModel.cs](../src/SyntheticShared/ViewModels/NestedDataEditorViewModel.cs)

Wrapper for compound structure layers.

#### Properties
- **`Function`**: `public string Function { get; set; }`
  *Description:* Gets or sets the function of the layer.
- **`Width`**: `public double Width { get; set; }`
  *Description:* Gets or sets the width of the layer.
- **`LayerCapFlag`**: `public bool LayerCapFlag { get; set; }`
  *Description:* Gets or sets a value indicating whether the layer cap flag is set.
- **`StructuralMaterial`**: `public bool StructuralMaterial { get; set; }`
  *Description:* Gets or sets a value indicating whether the structural material flag is set.
- **`MaterialsList`**: `public List<string> MaterialsList { get; }`
  *Description:* Gets the list of available materials.

#### Methods
##### `CompoundLayerRowVM`
```csharp
public CompoundLayerRowVM(SerialCompoundStructureLayer layer, List<string> materialsList)
```
Initializes a new instance of the <see cref="CompoundLayerRowVM"/> class.  <param name="layer">The serial compound structure layer model.</param> <param name="materialsList">The list of available materials.</param>

##### `GetUpdatedLayer`
```csharp
public SerialCompoundStructureLayer GetUpdatedLayer()
```
Gets the updated compound structure layer model with modified properties synced back.

**Returns:** The updated layer model.

---

#### Fields
- **`_layer`**: `private readonly SerialCompoundStructureLayer _layer;`
- **`_priority`**: `private int _priority;`
- **`_selectedMaterial`**: `private string _selectedMaterial;`
- **`_layer`**: `return _layer;`

---

### Class: `ConflictItem`
**File:** [src/SyntheticShared/ViewModels/ResolveConflictsViewModel.cs](../src/SyntheticShared/ViewModels/ResolveConflictsViewModel.cs)

Represents an item with a naming conflict and its resolution options.

#### Properties
- **`DisplayName`**: `public string DisplayName { get; set; }`
  *Description:* Gets or sets the display name of the conflicted item.
- **`UniqueKey`**: `public string UniqueKey { get; set; }`
  *Description:* Gets or sets the unique key of the conflicted item.
- **`Candidates`**: `public List<TagTemplate> Candidates { get; set; }`
  *Description:* Gets or sets the list of candidates to resolve the conflict.

#### Fields
- **`_selectedCandidate`**: `private TagTemplate _selectedCandidate;`

---

### Class: `DetailItemFactoryResultsViewModel`
**File:** [src/SyntheticShared/ViewModels/DetailItemFactoryResultsViewModel.cs](../src/SyntheticShared/ViewModels/DetailItemFactoryResultsViewModel.cs)

#### Methods
##### `DetailItemFactoryResultsViewModel`
```csharp
public DetailItemFactoryResultsViewModel(string outputFolder, IEnumerable<DetailItemResultItem> results)
```
Initializes a new instance of the `DetailItemFactoryResultsViewModel` class.

#### Fields
- **`ViewModelBase`**: `public class DetailItemFactoryResultsViewModel : ViewModelBase {`
  *Description:* ViewModel for the Detail Item Factory batch results dashboard. Provides collections for list views and garbage collection commands for temporary DWG files.
- **`Results`**: `public ObservableCollection<DetailItemResultItem> Results`
  *Description:* Gets the collection of elements conversion results.
- **`DeleteTempDwgsCommand`**: `public ICommand DeleteTempDwgsCommand`
  *Description:* Gets the command to delete temporary DWG files from the output folder.
- **`CloseCommand`**: `public ICommand CloseCommand`
  *Description:* Gets the command to close the window.
- **`CloseAction`**: `public Action? CloseAction`
  *Description:* Delegate assigned by the View to handle window closure.

---
### Class: `DetailItemFactoryViewModel`
**File:** [src/SyntheticShared/ViewModels/DetailItemFactoryViewModel.cs](../src/SyntheticShared/ViewModels/DetailItemFactoryViewModel.cs)

#### Methods
##### `Elements`
```csharp
public ObservableCollection<SelectedElementItemViewModel> Elements
```
Gets the collection of elements configured for conversion.

##### `Subcategories`
```csharp
public ObservableCollection<string> Subcategories
```
Gets the collection of OST_DetailComponents subcategory names available in the document.

##### `DetailItemFactoryViewModel`
```csharp
public DetailItemFactoryViewModel(Document doc, View activeView, IEnumerable<ElementId> selectedIds, IntPtr mainWindowHandle)
```
Initializes a new instance of the `DetailItemFactoryViewModel` class.

#### Fields
- **`ViewModelBase`**: `public class DetailItemFactoryViewModel : ViewModelBase {`
  *Description:* ViewModel for the Detail Item Factory WPF dialog. Provides bindings and validation logic for batch processing Revit model geometry to 2D Detail Items. Supports per-element configuration of view orientations.
- **`OutputPath`**: `public string OutputPath {`
  *Description:* Gets or sets the target folder path where family files are saved.
- **`SelectedSubcategory`**: `public string SelectedSubcategory {`
  *Description:* Gets or sets the selected subcategory name.
- **`OverwriteExisting`**: `public bool OverwriteExisting {`
  *Description:* Gets or sets a value indicating whether existing family files should be overwritten on disk.
- **`BrowseCommand`**: `public ICommand BrowseCommand`
  *Description:* Command to browse for an output folder.
- **`RemoveElementCommand`**: `public ICommand RemoveElementCommand`
  *Description:* Command to remove an element from the queue.
- **`RunCommand`**: `public ICommand RunCommand`
  *Description:* Command to trigger processing (runs validation and closes window with a success result).
- **`CancelCommand`**: `public ICommand CancelCommand`
  *Description:* Command to abort processing and close the window with a cancelled result.
- **`CloseAction`**: `public Action<bool>? CloseAction`
  *Description:* Delegate assigned by the View to request window closure, passing the dialog result.

---
### Class: `DropdownSelectionViewModel`
**File:** [src/SyntheticShared/ViewModels/DropdownSelectionViewModel.cs](../src/SyntheticShared/ViewModels/DropdownSelectionViewModel.cs)

ViewModel for dropdown selection views.

#### Properties
- **`CloseAction`**: `public Action<bool> CloseAction { get; set; }`
  *Description:* Gets or sets the action to close the window, passing a boolean result.
- **`OkCommand`**: `public ICommand OkCommand { get; }`
  *Description:* Gets the OK command.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the Cancel command.

#### Methods
##### `DropdownSelectionViewModel`
```csharp
public DropdownSelectionViewModel()
```
Initializes a new instance of the <see cref="DropdownSelectionViewModel"/> class.

##### `CanOk`
```csharp
private bool CanOk(object parameter)
```
##### `OnOk`
```csharp
private void OnOk(object parameter)
```
##### `OnCancel`
```csharp
private void OnCancel(object parameter)
```
---

#### Fields
- **`_title`**: `private string _title = "Title";`
- **`_instruction`**: `private string _instruction = "Instructions";`
- **`_itemLabel`**: `private string _itemLabel = "Label";`
- **`_items`**: `private IEnumerable<string> _items = new List<string>();`
- **`_selectedItem`**: `private string _selectedItem;`
- **`_isSorted`**: `private bool _isSorted;`
- **`list`**: `var list = value ?? new List<string>();`

---

### Class: `ElementGroup`
**File:** [src/SyntheticShared/ViewModels/JsonEditorMainViewModel.cs](../src/SyntheticShared/ViewModels/JsonEditorMainViewModel.cs)

Represents a group of elements categorized by their JSON root key (e.g. "FillRegionTypes").

#### Properties
- **`Elements`**: `public ObservableCollection<ElementTypeWrapperVM> Elements { get; } = new ObservableCollection<ElementTypeWrapperVM>();`
  *Description:* Gets the collection of elements belonging to this group.

#### Methods
##### `ElementGroup`
```csharp
public ElementGroup(string name)
```
Initializes a new instance of the <see cref="ElementGroup"/> class.  <param name="name">The group name.</param>

---

#### Fields
- **`_name`**: `private string _name;`
- **`_isSelected`**: `private bool _isSelected;`

---

### Class: `ElementTypeWrapperComparer`
**File:** [src/SyntheticShared/ViewModels/JsonEditorMainViewModel.cs](../src/SyntheticShared/ViewModels/JsonEditorMainViewModel.cs)

Custom comparer to sort ElementTypeWrapperVM items in the Left TreeView. Sorts by Group (using critical order), then Class (alphabetically), then Name (alphabetically).

#### Methods
##### `Compare`
```csharp
public int Compare(object x, object y)
```
Compares two elements.

---

#### Fields
- **`a`**: `var a = x as ElementTypeWrapperVM;`
- **`b`**: `var b = y as ElementTypeWrapperVM;`
- **`indexA`**: `int indexA = GroupOrder.IndexOf(a.Group);`
- **`indexB`**: `int indexB = GroupOrder.IndexOf(b.Group);`
- **`groupCompare`**: `int groupCompare = indexA.CompareTo(indexB);`
- **`classCompare`**: `int classCompare = string.Compare(a.Class, b.Class, StringComparison.OrdinalIgnoreCase);`

---

### Class: `ElementTypeWrapperVM`
**File:** [src/SyntheticShared/ViewModels/ElementTypeWrapperVM.cs](../src/SyntheticShared/ViewModels/ElementTypeWrapperVM.cs)

ViewModel wrapper for the ElementTypeModel POCO. Provides change notification, data binding, and editable/read-only field policies.

#### Properties
- **`Group`**: `public string Group { get; set; }`
  *Description:* Gets or sets the group name for categorization in the UI.
- **`Parameters`**: `public ObservableCollection<ParameterWrapperVM> Parameters { get; }`
  *Description:* Gets the collection of parameter ViewModels.

#### Methods
##### `ElementTypeWrapperVM`
```csharp
public ElementTypeWrapperVM(ElementModel model)
```
Initializes a new instance of the <see cref="ElementTypeWrapperVM"/> class.  <param name="model">The underlying ElementModel POCO.</param>

##### `if`
```csharp
else if (_model is ViewModel)
```
##### `if`
```csharp
else if (string.Equals(_model.Class, "Autodesk.Revit.DB.LinePatternElement", StringComparison.OrdinalIgnoreCase))
```
##### `if`
```csharp
else if (string.Equals(_model.Class, "Autodesk.Revit.DB.ParameterFilterElement", StringComparison.OrdinalIgnoreCase))
```
##### `if`
```csharp
else if (string.Equals(_model.Class, "Autodesk.Revit.DB.SharedParameterElement", StringComparison.OrdinalIgnoreCase) ||
```
##### `GetUpdatedModel`
```csharp
public ElementModel GetUpdatedModel()
```
Synchronizes and returns the updated ElementTypeModel POCO for future serialization.

**Returns:** The updated ElementTypeModel POCO.

##### `GetErrors`
```csharp
public IEnumerable GetErrors(string propertyName)
```
Gets the validation errors for a specific property.

**Parameters:**
- `propertyName`: The name of the property to get errors for.

**Returns:** A collection of error messages.

##### `OnErrorsChanged`
```csharp
protected virtual void OnErrorsChanged(string propertyName)
```
Raises the ErrorsChanged event and notifies that HasErrors has changed.

**Parameters:**
- `propertyName`: The name of the property whose validation errors changed.

##### `ValidateName`
```csharp
public void ValidateName()
```
Validates the Name property to check for duplicate names within the same category.

##### `if`
```csharp
else if (IsNameDuplicateCallback != null && IsNameDuplicateCallback(this, Name))
```
##### `AddError`
```csharp
private void AddError(string propertyName, string error)
```
Adds a validation error to the dictionary and triggers ErrorsChanged.

---

#### Fields
- **`_model`**: `private readonly ElementModel _model;`
- **`_isSelected`**: `private bool _isSelected;`
- **`desc`**: `string desc = string.Join(", ", lpModel.Segments.Select(s => $"{s.Type}: {s.Length}"));`
- **`catNames`**: `var catNames = new List<string>();`
- **`cat`**: `var cat = Autodesk.Revit.DB.Category.GetCategory(filter.Document, catId);`
- **`def`**: `var def = spe.GetDefinition();`
- **`groupStr`**: `string groupStr = "";`
- **`groupProp`**: `var groupProp = def.GetType().GetProperty("ParameterGroup");`
- **`getGroupMethod`**: `var getGroupMethod = def.GetType().GetMethod("GetGroupTypeId");`
- **`gt`**: `var gt = getGroupMethod.Invoke(def, null);`
- **`pType`**: `string pType = "";`
- **`typeProp`**: `var typeProp = def.GetType().GetProperty("ParameterType");`
- **`getDataType`**: `var getDataType = def.GetType().GetMethod("GetDataType");`
- **`dt`**: `var dt = getDataType.Invoke(def, null);`
- **`true`**: `return true;`
- **`isEqual`**: `bool isEqual = _model.Aliases != null && _model.Aliases.SequenceEqual(list);`
- **`Class`**: `public string Class => _model.Class;`
  *Description:* Gets the class of the Element Type. This field is read-only.
- **`Category`**: `public string Category => _model.Category;`
  *Description:* Gets the category of the Element Type. This field is read-only.
- **`_model`**: `return _model;`
- **`HasErrors`**: `public bool HasErrors => _errors.Any(kvp => kvp.Value != null && kvp.Value.Count > 0);`
  *Description:* Gets whether there are any validation errors.
- **`errors`**: `return errors;`

---

### Class: `ExportStylesViewModel`
**File:** [src/SyntheticShared/ViewModels/ExportStylesViewModel.cs](../src/SyntheticShared/ViewModels/ExportStylesViewModel.cs)

ViewModel for exporting styles and configurations from Revit.

#### Properties
- **`CartItems`**: `public ObservableCollection<ElementTypeWrapperVM> CartItems { get; } = new ObservableCollection<ElementTypeWrapperVM>();`
  *Description:* Gets the collection of elements currently in the export cart.
- **`CategoryFilters`**: `public List<string> CategoryFilters { get; } = new List<string>`
  *Description:* Gets the list of category filter options.
- **`LoadableFamilyCategories`**: `public ObservableCollection<CategorySelectionItem> LoadableFamilyCategories { get; } = new ObservableCollection<CategorySelectionItem>();`
  *Description:* Gets the collection of loadable family categories available for selection.
- **`SelectAllCategoriesCommand`**: `public ICommand SelectAllCategoriesCommand { get; }`
  *Description:* Gets the command to select all categories.
- **`SelectNoneCategoriesCommand`**: `public ICommand SelectNoneCategoriesCommand { get; }`
  *Description:* Gets the command to deselect all categories.
- **`SelectAllAnnotationsCommand`**: `public ICommand SelectAllAnnotationsCommand { get; }`
  *Description:* Gets the command to select all annotations.
- **`SelectNoneAnnotationsCommand`**: `public ICommand SelectNoneAnnotationsCommand { get; }`
  *Description:* Gets the command to deselect all annotations.
- **`SelectAllMaterialsCommand`**: `public ICommand SelectAllMaterialsCommand { get; }`
  *Description:* Gets the command to select all materials.
- **`SelectNoneMaterialsCommand`**: `public ICommand SelectNoneMaterialsCommand { get; }`
  *Description:* Gets the command to deselect all materials.
- **`SelectAllSystemTypesCommand`**: `public ICommand SelectAllSystemTypesCommand { get; }`
  *Description:* Gets the command to select all system types.
- **`SelectNoneSystemTypesCommand`**: `public ICommand SelectNoneSystemTypesCommand { get; }`
  *Description:* Gets the command to deselect all system types.
- **`SelectAllViewsCommand`**: `public ICommand SelectAllViewsCommand { get; }`
  *Description:* Gets the command to select all views.
- **`SelectNoneViewsCommand`**: `public ICommand SelectNoneViewsCommand { get; }`
  *Description:* Gets the command to deselect all views.
- **`SelectAllStandardsCommand`**: `public ICommand SelectAllStandardsCommand { get; }`
  *Description:* Gets the command to select all standard types.
- **`SelectNoneStandardsCommand`**: `public ICommand SelectNoneStandardsCommand { get; }`
  *Description:* Gets the command to deselect all standard types.
- **`BrowseCommand`**: `public ICommand BrowseCommand { get; }`
  *Description:* Gets the command to browse for file paths.
- **`ExportCommand`**: `public ICommand ExportCommand { get; }`
  *Description:* Gets the command to perform the export operation.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the command to cancel the operation.
- **`AddAnnotationsCommand`**: `public ICommand AddAnnotationsCommand { get; }`
  *Description:* Gets the command to add annotations to the export cart.
- **`AddViewsCommand`**: `public ICommand AddViewsCommand { get; }`
  *Description:* Gets the command to add views to the export cart.
- **`AddModelSystemTypesCommand`**: `public ICommand AddModelSystemTypesCommand { get; }`
  *Description:* Gets the command to add model/system types to the export cart.
- **`AddFamilyTypesCommand`**: `public ICommand AddFamilyTypesCommand { get; }`
  *Description:* Gets the command to add family types to the export cart.
- **`AddMaterialsCommand`**: `public ICommand AddMaterialsCommand { get; }`
  *Description:* Gets the command to add materials to the export cart.
- **`AddCategoriesCommand`**: `public ICommand AddCategoriesCommand { get; }`
  *Description:* Gets the command to add categories to the export cart.
- **`AddElementsCommand`**: `public ICommand AddElementsCommand { get; }`
  *Description:* Gets the command to add elements to the export cart.
- **`RemoveCartItemCommand`**: `public ICommand RemoveCartItemCommand { get; }`
  *Description:* Gets the command to remove an item from the cart.
- **`CloseAction`**: `public Action CloseAction { get; set; }`
  *Description:* Gets or sets the close action for the window.

#### Methods
##### `ExportStylesViewModel`
```csharp
public ExportStylesViewModel(UIApplication uiapp) : this(uiapp, false)
```
Initializes a new instance of the <see cref="ExportStylesViewModel"/> class with default in-memory mode set to false.  <param name="uiapp">The Revit UI application instance.</param>

##### `ExportStylesViewModel`
```csharp
public ExportStylesViewModel(UIApplication uiapp, bool isInMemoryMode)
```
Initializes a new instance of the <see cref="ExportStylesViewModel"/> class.  <param name="uiapp">The Revit UI application instance.</param> <param name="isInMemoryMode">Whether to operate in memory only without writing to file.</param>

##### `CanExport`
```csharp
private bool CanExport()
```
##### `ExecuteBrowse`
```csharp
private void ExecuteBrowse(object obj)
```
##### `ExecuteExport`
```csharp
private void ExecuteExport(object obj)
```
##### `if`
```csharp
else if (!view.IsTemplate && ExportStandardViews)
```
##### `CollectSelectedElements`
```csharp
private List<Element> CollectSelectedElements(Document doc, List<Type> selectedSystemClasses, List<ElementId> checkedCategoryIds)
```
##### `CollectAnnotationStyles`
```csharp
private List<ElementType> CollectAnnotationStyles(Document doc)
```
##### `ExecuteCancel`
```csharp
private void ExecuteCancel(object obj)
```
##### `AllowUIToUpdate`
```csharp
private void AllowUIToUpdate()
```
##### `AddAnnotations`
```csharp
private void AddAnnotations()
```
##### `AddViews`
```csharp
private void AddViews()
```
##### `AddModelSystemTypes`
```csharp
private void AddModelSystemTypes()
```
##### `AddFamilyTypes`
```csharp
private void AddFamilyTypes()
```
##### `AddMaterials`
```csharp
private void AddMaterials()
```
##### `AddCategories`
```csharp
private void AddCategories()
```
##### `AddElements`
```csharp
private void AddElements()
```
##### `RemoveCartItem`
```csharp
private void RemoveCartItem(object wrapperParameter)
```
##### `SyncCartItemsDisplay`
```csharp
private void SyncCartItemsDisplay()
```
##### `if`
```csharp
else if (model is DimensionTypeModel) group = "DimensionTypes";
```
##### `if`
```csharp
else if (model is HostObjTypeModel) group = "HostObjTypes";
```
##### `if`
```csharp
else if (model is MaterialModel) group = "Materials";
```
##### `if`
```csharp
else if (model is ViewModel) group = "Views";
```
##### `if`
```csharp
else if (elem is Family) group = "FamilyTypes";
```
##### `ShowChecklistAndAdd`
```csharp
private void ShowChecklistAndAdd(string title, string instruction, IEnumerable<Tuple<string, ElementId>> items)
```
##### `SelectAllCategories`
```csharp
private void SelectAllCategories()
```
##### `SelectNoneCategories`
```csharp
private void SelectNoneCategories()
```
##### `ToggleGroupAnnotations`
```csharp
private void ToggleGroupAnnotations(bool check)
```
##### `ToggleGroupMaterials`
```csharp
private void ToggleGroupMaterials(bool check)
```
##### `ToggleGroupSystemTypes`
```csharp
private void ToggleGroupSystemTypes(bool check)
```
##### `ToggleGroupViews`
```csharp
private void ToggleGroupViews(bool check)
```
##### `ToggleGroupStandards`
```csharp
private void ToggleGroupStandards(bool check)
```
##### `PopulateLoadableFamilyCategories`
```csharp
private void PopulateLoadableFamilyCategories()
```
##### `HarvestDependencies`
```csharp
private void HarvestDependencies(List<Element> elementsToProcess, Document doc)
```
---

#### Fields
- **`_uiapp`**: `private readonly UIApplication _uiapp;`
- **`_doc`**: `private readonly Document _doc;`
- **`_selectedExportItems`**: `private ObservableCollection<ElementId> _selectedExportItems = new ObservableCollection<ElementId>();`
- **`_exportFilePath`**: `private string _exportFilePath;`
- **`_isInMemoryMode`**: `private readonly bool _isInMemoryMode;`
- **`_generatedStyles`**: `private List<ObjectModel> _generatedStyles;`
- **`GeneratedStyles`**: `public IEnumerable<ObjectModel> GeneratedStyles => _generatedStyles;`
  *Description:* Gets the exported models in memory when in in-memory mode.
- **`FileSelectionVisibility`**: `public Visibility FileSelectionVisibility => _isInMemoryMode ? Visibility.Collapsed : Visibility.Visible;`
  *Description:* Gets the visibility status of the file selection control.
- **`ExportButtonText`**: `public string ExportButtonText => _isInMemoryMode ? "Generate" : "Export";`
  *Description:* Gets the display text for the export/generate button.
- **`_scanFamilies`**: `private bool _scanFamilies = false;`
- **`_processNested`**: `private bool _processNested = true;`
- **`_selectedCategoryFilter`**: `private string _selectedCategoryFilter = "Annotations Only";`
- **`_statusMessage`**: `private string _statusMessage;`
- **`_progressValue`**: `private int _progressValue;`
- **`_progressMax`**: `private int _progressMax = 100;`
- **`_progressVisibility`**: `private Visibility _progressVisibility = Visibility.Collapsed;`
- **`_isIndeterminate`**: `private bool _isIndeterminate;`
- **`_isProcessing`**: `private bool _isProcessing;`
- **`_exportTextNoteTypes`**: `private bool _exportTextNoteTypes;`
- **`_exportLabelTypes`**: `private bool _exportLabelTypes;`
- **`_exportDimensionTypes`**: `private bool _exportDimensionTypes;`
- **`_exportFilledRegionTypes`**: `private bool _exportFilledRegionTypes;`
- **`_exportMaterials`**: `private bool _exportMaterials;`
- **`_exportWallTypes`**: `private bool _exportWallTypes;`
- **`_exportFloorTypes`**: `private bool _exportFloorTypes;`
- **`_exportRoofTypes`**: `private bool _exportRoofTypes;`
- **`_exportCeilingTypes`**: `private bool _exportCeilingTypes;`
- **`_exportRailingTypes`**: `private bool _exportRailingTypes;`
- **`_exportStairsTypes`**: `private bool _exportStairsTypes;`
- **`_exportStandardViews`**: `private bool _exportStandardViews;`
- **`_exportViewTemplates`**: `private bool _exportViewTemplates;`
- **`_exportCategories`**: `private bool _exportCategories;`
- **`_exportGridTypes`**: `private bool _exportGridTypes;`
- **`_exportLevelTypes`**: `private bool _exportLevelTypes;`
- **`_exportFillPatterns`**: `private bool _exportFillPatterns;`
- **`_exportLinePatterns`**: `private bool _exportLinePatterns;`
- **`_exportAppearanceAssets`**: `private bool _exportAppearanceAssets;`
- **`_exportCurtainSystemTypes`**: `private bool _exportCurtainSystemTypes;`
- **`_exportMullionTypes`**: `private bool _exportMullionTypes;`
- **`_exportFasciaTypes`**: `private bool _exportFasciaTypes;`
- **`_exportGutterTypes`**: `private bool _exportGutterTypes;`
- **`_exportTitleBlockTypes`**: `private bool _exportTitleBlockTypes;`
- **`_exportViewFamilyTypes`**: `private bool _exportViewFamilyTypes;`
- **`_exportBrowserOrganizations`**: `private bool _exportBrowserOrganizations;`
- **`_exportParameterElements`**: `private bool _exportParameterElements;`
- **`ownerWindow`**: `var ownerWindow = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this && w.IsVisible);`
- **`progressWindow`**: `var progressWindow = new Synthetic.Views.ProgressWindow(_uiapp.MainWindowHandle);`
- **`exportedStyles`**: `List<ObjectModel> exportedStyles = new List<ObjectModel>();`
- **`uniqueKeys`**: `HashSet<string> uniqueKeys = new HashSet<string>();`
- **`selectedSystemClasses`**: `var selectedSystemClasses = new List<Type>();`
- **`hasCartSelection`**: `bool hasCartSelection = SelectedExportItems != null && SelectedExportItems.Any();`
- **`elementsToProcess`**: `var elementsToProcess = new List<Element>();`
- **`catFilters`**: `var catFilters = checkedCategoryIds.Select(id => new ElementCategoryFilter(id)).Cast<ElementFilter>().ToList();`
- **`categoryFilter`**: `ElementFilter categoryFilter = catFilters.Count == 1 ? catFilters.First() : new LogicalOrFilter(catFilters);`
- **`scrapeCategory`**: `Action<Category> scrapeCategory = null;`
- **`categoryModel`**: `var categoryModel = ModelsToSerialize.ByCategory(c, _doc, true);`
- **`key`**: `string key = $"Category:{categoryModel.Name}";`
- **`elem`**: `var elem = _doc.GetElement(id);`
- **`topStyles`**: `var topStyles = CollectAnnotationStyles(_doc);`
- **`serialModel`**: `var serialModel = ModelsToSerialize.ByElement(elem, true);`
- **`key`**: `string key = $"{serialModel.Class}:{serialModel.Name}";`
- **`scanFamiliesForSelection`**: `bool scanFamiliesForSelection = false;`
- **`familyRelevantSystemClasses`**: `List<Type> familyRelevantSystemClasses = new List<Type>();`
- **`familiesToProcess`**: `List<Family> familiesToProcess = new List<Family>();`
- **`json`**: `string json = ModelsToSerialize.SerializeToJson(exportedStyles);`
- **`familyDoc`**: `Document familyDoc = null;`
- **`familyElements`**: `IEnumerable<Element> familyElements;`
- **`serialModel`**: `var serialModel = ModelsToSerialize.ByElement(elem, true);`
- **`key`**: `string key = $"{serialModel.Class}:{serialModel.Name}";`
- **`elements`**: `var elements = new List<Element>();`
- **`catFilters`**: `var catFilters = checkedCategoryIds.Select(id => new ElementCategoryFilter(id)).Cast<ElementFilter>().ToList();`
- **`categoryFilter`**: `ElementFilter categoryFilter = catFilters.Count == 1 ? catFilters.First() : new LogicalOrFilter(catFilters);`
- **`elements`**: `return elements;`
- **`styles`**: `List<ElementType> styles = new List<ElementType>();`
- **`styles`**: `return styles;`
- **`dispatcher`**: `var dispatcher = Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;`
- **`annotationTypes`**: `var annotationTypes = CollectAnnotationStyles(_doc);`
- **`list`**: `var list = annotationTypes.Select(e => Tuple.Create(e.Name, e.Id)).ToList();`
- **`list`**: `var list = new List<Tuple<string, ElementId>>();`
- **`list`**: `var list = new List<Tuple<string, ElementId>>();`
- **`symbols`**: `var symbols = f.GetFamilySymbolIds().Select(id => _doc.GetElement(id)).Cast<FamilySymbol>();`
- **`list`**: `var list = new List<Tuple<string, ElementId>>();`
- **`uidoc`**: `var uidoc = _uiapp.ActiveUIDocument;`
- **`id`**: `var id = reference.ElementId;`
- **`wrapper`**: `var wrapper = wrapperParameter as ElementTypeWrapperVM;`
- **`elem`**: `Element elem = _doc.GetElement(id);`
- **`model`**: `var model = ModelsToSerialize.ByElement(elem, true);`
- **`group`**: `string group = "ElementTypes";`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(model) { Group = group };`
- **`checkedIds`**: `var checkedIds = vm.CheckedElementIds;`
- **`processedIds`**: `var processedIds = new HashSet<ElementId>(elementsToProcess.Select(e => e.Id));`
- **`elem`**: `Element elem = elementsToProcess[i];`
- **`cs`**: `CompoundStructure cs = hostObjType.GetCompoundStructure();`
- **`matId`**: `ElementId matId = layer.MaterialId;`
- **`matElem`**: `Element matElem = doc.GetElement(matId);`
- **`patternIds`**: `var patternIds = new List<ElementId>();`
- **`fpElem`**: `Element fpElem = doc.GetElement(fpId);`
- **`assetId`**: `ElementId assetId = mat.AppearanceAssetId;`
- **`assetElem`**: `Element assetElem = doc.GetElement(assetId);`
- **`fpIds`**: `var fpIds = new List<ElementId>();`
- **`fpElem`**: `Element fpElem = doc.GetElement(fpId);`
- **`p`**: `Parameter p = elem.get_Parameter(bip);`
- **`fsId`**: `ElementId fsId = p.AsElementId();`
- **`fsElem`**: `Element fsElem = doc.GetElement(fsId);`
- **`filterIds`**: `var filterIds = view.GetFilters();`
- **`filterElem`**: `Element filterElem = doc.GetElement(filterId);`
- **`ogs`**: `OverrideGraphicSettings ogs = view.GetCategoryOverrides(cat.Id);`
- **`dep`**: `Element dep = doc.GetElement(id);`
- **`depId`**: `ElementId depId = p.AsElementId();`
- **`depElem`**: `Element depElem = doc.GetElement(depId);`

---

### Class: `FileUtilitySettingsViewModel`
**File:** [src/SyntheticShared/ViewModels/FileUtilitySettingsViewModel.cs](../src/SyntheticShared/ViewModels/FileUtilitySettingsViewModel.cs)

ViewModel for managing File Utility and Network Path configuration settings in the Dashboard.

#### Properties
- **`ConfigureCommand`**: `public ICommand ConfigureCommand { get; }`
  *Description:* <inheritdoc/>

#### Methods
##### `FileUtilitySettingsViewModel`
```csharp
public FileUtilitySettingsViewModel(Document doc, IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="FileUtilitySettingsViewModel"/> class.  <param name="doc">The Revit Document context.</param> <param name="mainWindowHandle">The parent main window handle.</param>

##### `OnConfigure`
```csharp
private void OnConfigure(object parameter)
```
##### `Save`
```csharp
public void Save()
```
<inheritdoc/>

##### `Reload`
```csharp
public void Reload()
```
<inheritdoc/>

---

#### Fields
- **`_doc`**: `private readonly Document _doc;`
- **`_mainWindowHandle`**: `private readonly IntPtr _mainWindowHandle;`
- **`_settings`**: `private FileUtilitySettings _settings;`
- **`_isOverridden`**: `private bool _isOverridden;`
- **`_isDirty`**: `private bool _isDirty;`
- **`ModuleName`**: `public string ModuleName => "Firm Network Paths";`
  *Description:* <inheritdoc/>
- **`displaySettings`**: `FileUtilitySettings displaySettings;`
- **`prefix`**: `string prefix;`
- **`defaultPath`**: `string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");`
- **`defaultConfig`**: `Config defaultConfig = Config.ReadFromFile(defaultPath);`
- **`sb`**: `var sb = new StringBuilder(prefix);`
- **`mapped`**: `string mapped = kvp.Value != null && kvp.Value.Count > 0 ? kvp.Value[0] : string.Empty;`
- **`storageName`**: `string storageName = "Synthetic_" + FileUtilitySettings.Name;`
- **`wizardVM`**: `var wizardVM = new NetworkPathsWizardViewModel(draftSettings, _mainWindowHandle);`
- **`dialogResult`**: `var dialogResult = wizardWindow.ShowDialog();`
- **`storageName`**: `string storageName = "Synthetic_" + FileUtilitySettings.Name;`

---

### Class: `FindReplaceViewModel`
**File:** [src/SyntheticShared/ViewModels/FindReplaceViewModel.cs](../src/SyntheticShared/ViewModels/FindReplaceViewModel.cs)

ViewModel for the Find & Replace dialog. Handles searching and replacing text in Element Names and editable parameter values.

#### Properties
- **`FindNextCommand`**: `public ICommand FindNextCommand { get; }`
  *Description:* Gets the command to find the next match.
- **`FindPreviousCommand`**: `public ICommand FindPreviousCommand { get; }`
  *Description:* Gets the command to find the previous match.
- **`ReplaceCommand`**: `public ICommand ReplaceCommand { get; }`
  *Description:* Gets the command to replace the current match and find next.
- **`ReplaceAllCommand`**: `public ICommand ReplaceAllCommand { get; }`
  *Description:* Gets the command to replace all occurrences.
- **`CloseCommand`**: `public ICommand CloseCommand { get; }`
  *Description:* Gets the command to close the window.

#### Methods
##### `FindReplaceViewModel`
```csharp
public FindReplaceViewModel(JsonEditorMainViewModel mainVM)
```
Initializes a new instance of the <see cref="FindReplaceViewModel"/> class.  <param name="mainVM">The main JSON editor view model.</param>

##### `ResetSearch`
```csharp
private void ResetSearch()
```
---

#### Fields
- **`_mainVM`**: `private readonly JsonEditorMainViewModel _mainVM;`
- **`_findText`**: `private string _findText = string.Empty;`
- **`_replaceText`**: `private string _replaceText = string.Empty;`
- **`_scope`**: `private SearchScope _scope = SearchScope.ElementNames;`
- **`_currentElementIndex`**: `private int _currentElementIndex = -1;`
- **`_currentFieldIndex`**: `private int _currentFieldIndex = -1; // 0 = Element Name, >= 1 = Parameter index`
- **`_currentMatchCharIndex`**: `private int _currentMatchCharIndex = -1;`

---

### Interface: `ISettingModuleViewModel`
**File:** [src/SyntheticShared/ViewModels/ISettingModuleViewModel.cs](../src/SyntheticShared/ViewModels/ISettingModuleViewModel.cs)

Contract defining the properties and behaviors for settings module view models.

#### Properties
- **`ModuleName`**: `string ModuleName { get; }`
  *Description:* Gets the name of the settings module for display in the sidebar.
- **`IsOverridden`**: `bool IsOverridden { get; set; }`
  *Description:* Gets or sets a value indicating whether firm settings are overridden for the project.
- **`IsValid`**: `bool IsValid { get; }`
  *Description:* Gets a value indicating whether the settings configured for this module are valid.
- **`SummaryText`**: `string SummaryText { get; }`
  *Description:* Gets a read-only text summary of the current settings.
- **`ConfigureCommand`**: `ICommand ConfigureCommand { get; }`
  *Description:* Gets the command that launches the module's configuration wizard window.
- **`IsDirty`**: `bool IsDirty { get; set; }`
  *Description:* Gets or sets a value indicating whether the settings module has unsaved changes.

#### Methods
##### `Save`
```csharp
void Save();
```
Saves or updates the settings in the project document or deletes them if overrides are disabled.

##### `Reload`
```csharp
void Reload();
```
Reloads the settings state from the Revit document database.

---

### Class: `ImportFamilyLoadOptions`
**File:** [src/SyntheticShared/ViewModels/ImportStylesViewModel.cs](../src/SyntheticShared/ViewModels/ImportStylesViewModel.cs)

#### Methods
##### `OnFamilyFound`
```csharp
public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
```
##### `OnSharedFamilyFound`
```csharp
public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
```
---

#### Fields
- **`true`**: `return true;`
- **`true`**: `return true;`

---

### Class: `ImportStylesViewModel`
**File:** [src/SyntheticShared/ViewModels/ImportStylesViewModel.cs](../src/SyntheticShared/ViewModels/ImportStylesViewModel.cs)

ViewModel for importing styles from a JSON file or memory.

#### Properties
- **`CategoryFilters`**: `public List<string> CategoryFilters { get; } = new List<string>`
  *Description:* Gets the list of category filter options.
- **`BrowseCommand`**: `public ICommand BrowseCommand { get; }`
  *Description:* Gets the command to browse for the JSON file.
- **`ImportCommand`**: `public ICommand ImportCommand { get; }`
  *Description:* Gets the command to start the import operation.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the command to cancel the operation.
- **`CloseAction`**: `public Action CloseAction { get; set; }`
  *Description:* Gets or sets the action to close the window.

#### Methods
##### `ImportStylesViewModel`
```csharp
public ImportStylesViewModel(UIApplication uiapp)
```
Initializes a new instance of the <see cref="ImportStylesViewModel"/> class.  <param name="uiapp">The Revit UI application instance.</param>

##### `ImportStylesViewModel`
```csharp
public ImportStylesViewModel(UIApplication uiapp, IEnumerable<ElementModel> inMemoryStyles)
```
Initializes a new instance of the <see cref="ImportStylesViewModel"/> class with in-memory style data.  <param name="uiapp">The Revit UI application instance.</param> <param name="inMemoryStyles">The list of style models to import.</param>

##### `CanImport`
```csharp
private bool CanImport()
```
##### `ExecuteBrowse`
```csharp
private void ExecuteBrowse(object obj)
```
##### `ExecuteImport`
```csharp
private void ExecuteImport(object obj)
```
##### `if`
```csharp
else if (!exists)
```
##### `if`
```csharp
else if (serialElement is ViewModel viewModel)
```
##### `if`
```csharp
else if (serialElement is ElementTypeModel etModel)
```
##### `if`
```csharp
else if (serialElement is ElementTypeModel etModel)
```
##### `ExecuteCancel`
```csharp
private void ExecuteCancel(object obj)
```
##### `AllowUIToUpdate`
```csharp
private void AllowUIToUpdate()
```
---

#### Fields
- **`_uiapp`**: `private readonly UIApplication _uiapp;`
- **`_doc`**: `private readonly Document _doc;`
- **`_filePath`**: `private string _filePath;`
- **`_inMemoryStyles`**: `private readonly IEnumerable<ElementModel> _inMemoryStyles;`
- **`FileSelectionVisibility`**: `public Visibility FileSelectionVisibility => _inMemoryStyles != null ? Visibility.Collapsed : Visibility.Visible;`
  *Description:* Gets the visibility status of the file selection control.
- **`_mergeAliases`**: `private bool _mergeAliases = true;`
- **`_updateFamilies`**: `private bool _updateFamilies = false;`
- **`_processNested`**: `private bool _processNested = true;`
- **`_purgeUnused`**: `private bool _purgeUnused = false;`
- **`_selectedCategoryFilter`**: `private string _selectedCategoryFilter = "All Categories";`
- **`_statusMessage`**: `private string _statusMessage;`
- **`_progressValue`**: `private int _progressValue;`
- **`_progressMax`**: `private int _progressMax = 100;`
- **`_progressVisibility`**: `private Visibility _progressVisibility = Visibility.Collapsed;`
- **`_isIndeterminate`**: `private bool _isIndeterminate;`
- **`_isProcessing`**: `private bool _isProcessing;`
- **`ownerWindow`**: `var ownerWindow = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this && w.IsVisible);`
- **`progressWindow`**: `var progressWindow = new Synthetic.Views.ProgressWindow(_uiapp.MainWindowHandle);`
- **`tracker`**: `var tracker = new System.Collections.ObjectModel.ObservableCollection<Synthetic.Models.ImportLogItem>();`
- **`styleTypes`**: `List<ElementModel> styleTypes;`
- **`json`**: `string json = File.ReadAllText(FilePath);`
- **`standards`**: `IEnumerable<ElementModel> standards = ModelsToSerialize.DeserializeByJson(json);`
- **`currentIndex`**: `int currentIndex = 0;`
- **`action`**: `string action = "Updated";`
- **`message`**: `string message = "Updated style element parameters.";`
- **`hasAliasMatch`**: `bool hasAliasMatch = false;`
- **`aliasMessage`**: `string aliasMessage = null;`
- **`elemClass`**: `Type elemClass = assembly.GetType(serialElement.Class);`
- **`standardElem`**: `Element standardElem = Select.ElementByNameClass(serialElement.Name, elemClass, _doc);`
- **`aliasedElem`**: `Element aliasedElem = Select.ElementByNameClass(alias, elemClass, _doc);`
- **`exists`**: `bool exists = false;`
- **`familiesToProcess`**: `List<Family> familiesToProcess = new List<Family>();`
- **`familyIndex`**: `int familyIndex = 0;`
- **`summaryVM`**: `var summaryVM = new ImportSummaryViewModel(tracker);`
- **`summaryWindow`**: `var summaryWindow = new Synthetic.Views.ImportSummaryWindow(_uiapp.MainWindowHandle, summaryVM);`
- **`summaryVM`**: `var summaryVM = new ImportSummaryViewModel(tracker);`
- **`summaryWindow`**: `var summaryWindow = new Synthetic.Views.ImportSummaryWindow(_uiapp.MainWindowHandle, summaryVM);`
- **`familyDoc`**: `Document familyDoc = null;`
- **`dispatcher`**: `var dispatcher = Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;`

---

### Class: `ImportSummaryViewModel`
**File:** [src/SyntheticShared/ViewModels/ImportSummaryViewModel.cs](../src/SyntheticShared/ViewModels/ImportSummaryViewModel.cs)

ViewModel for the import summary view, summarizing the results of an import operation.

#### Properties
- **`LogItems`**: `public ObservableCollection<ImportLogItem> LogItems { get; }`
  *Description:* Gets the collection of import log items.

#### Methods
##### `ImportSummaryViewModel`
```csharp
public ImportSummaryViewModel(ObservableCollection<ImportLogItem> logItems)
```
Initializes a new instance of the <see cref="ImportSummaryViewModel"/> class.  <param name="logItems">The list of import log items to summarize.</param>

---

#### Fields
- **`CreatedCount`**: `public int CreatedCount => LogItems.Count(item => item.Action == "Created");`
  *Description:* Gets the count of items that were created during import.
- **`UpdatedCount`**: `public int UpdatedCount => LogItems.Count(item => item.Action == "Updated");`
  *Description:* Gets the count of items that were updated during import.
- **`RenamedCount`**: `public int RenamedCount => LogItems.Count(item => item.Action == "Renamed");`
  *Description:* Gets the count of items that were renamed during import.
- **`ErrorsCount`**: `public int ErrorsCount => LogItems.Count(item => item.Action == "Failed");`
  *Description:* Gets the count of failed import operations.

---

### Class: `JsonEditorMainViewModel`
**File:** [src/SyntheticShared/ViewModels/JsonEditorMainViewModel.cs](../src/SyntheticShared/ViewModels/JsonEditorMainViewModel.cs)

Main orchestrator ViewModel for the JSON Editor. Handles loading, saving, and wrapping ElementTypeModel POCOs.

#### Properties
- **`Instance`**: `public static JsonEditorMainViewModel Instance { get; set; }`
  *Description:* Gets or sets the singleton instance of the
- **`DuplicateCommand`**: `public ICommand DuplicateCommand { get; }`
  *Description:* Gets the command to duplicate selected items.
- **`DeleteCommand`**: `public ICommand DeleteCommand { get; }`
  *Description:* Gets the command to delete selected items.
- **`MergeElementsCommand`**: `public ICommand MergeElementsCommand { get; }`
  *Description:* Gets the command to merge selected elements.
- **`SelectAllCommand`**: `public ICommand SelectAllCommand { get; }`
  *Description:* Gets the command to select all visible items.
- **`SelectNoneCommand`**: `public ICommand SelectNoneCommand { get; }`
  *Description:* Gets the command to clear selection.
- **`ShowFindReplaceCommand`**: `public ICommand ShowFindReplaceCommand { get; }`
  *Description:* Gets the command to open the Find and Replace dialog.
- **`LoadCommand`**: `public ICommand LoadCommand { get; }`
  *Description:* Gets the command to load a JSON file.
- **`SaveCommand`**: `public ICommand SaveCommand { get; }`
  *Description:* Gets the command to save the current file.
- **`SaveAsCommand`**: `public ICommand SaveAsCommand { get; }`
  *Description:* Gets the command to save the current file as a new file.
- **`ImportSelectedCommand`**: `public ICommand ImportSelectedCommand { get; }`
  *Description:* Gets the command to import selected elements into Revit.
- **`GenerateCommand`**: `public ICommand GenerateCommand { get; }`
  *Description:* Gets the command to generate/extract styles from Revit.
- **`WrappedElements`**: `public ObservableCollection<ElementTypeWrapperVM> WrappedElements { get; } = new ObservableCollection<ElementTypeWrapperVM>();`
  *Description:* Gets the collection of wrapped ElementType ViewModels.
- **`SelectedElements`**: `public ObservableCollection<ElementTypeWrapperVM> SelectedElements { get; } = new ObservableCollection<ElementTypeWrapperVM>();`
  *Description:* Gets the collection of currently selected elements in the TreeView.
- **`GroupedElements`**: `public ObservableCollection<ElementGroup> GroupedElements { get; } = new ObservableCollection<ElementGroup>();`
  *Description:* Gets the grouped elements for TreeView binding.
- **`DisplayParameters`**: `public ObservableCollection<ParameterWrapperVM> DisplayParameters { get; } = new ObservableCollection<ParameterWrapperVM>();`
  *Description:* Gets the parameters representing the intersection of all selected elements.

#### Methods
##### `SetExternalEvent`
```csharp
public void SetExternalEvent(ExternalEvent externalEvent, JsonEditorExternalEventHandler eventHandler)
```
Sets the Revit external event and handler for modeless API execution.

##### `JsonEditorMainViewModel`
```csharp
public JsonEditorMainViewModel(UIApplication uiapp)
```
Initializes a new instance of the <see cref="JsonEditorMainViewModel"/> class with Revit context.

##### `JsonEditorMainViewModel`
```csharp
public JsonEditorMainViewModel() : this(null)
```
Initializes a new instance of the <see cref="JsonEditorMainViewModel"/> class.

##### `FilterElement`
```csharp
private bool FilterElement(object obj)
```
##### `LoadJson`
```csharp
public void LoadJson(string filePath)
```
Loads the JSON from the specified file path, deserializes it, wraps the ElementTypeModels, and populates WrappedElements.

**Parameters:**
- `filePath`: The path of the JSON file to load.

##### `SaveJson`
```csharp
public void SaveJson(string filePath)
```
Saves the wrapped and preserved elements back to the specified JSON file path.

**Parameters:**
- `filePath`: The path of the JSON file to save to.

##### `ApplyFilter`
```csharp
public void ApplyFilter()
```
Applies search filtering on the CollectionView.

##### `UpdateSelectedElements`
```csharp
public void UpdateSelectedElements()
```
Synchronizes the collection of SelectedElements and triggers parameter intersection recalculation.

##### `UpdateSelectedElementsFromListBox`
```csharp
public void UpdateSelectedElementsFromListBox()
```
Synchronizes the collection of SelectedElements from ListBox and triggers parameter intersection recalculation.

##### `CalculateParameterIntersection`
```csharp
private void CalculateParameterIntersection()
```
Calculates the parameter intersection of all selected elements and builds DisplayParameters.

##### `DisplayParam_PropertyChanged`
```csharp
private void DisplayParam_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
```
Handles changes to intersecting display parameter values and pushes them to all selected elements.

##### `PushBulkValue`
```csharp
private void PushBulkValue(string paramName, string newValue)
```
Pushes a bulk edit to the matching parameter in all currently selected elements.

##### `AddElement`
```csharp
private void AddElement(ElementTypeWrapperVM wrapper)
```
##### `RemoveElement`
```csharp
private void RemoveElement(ElementTypeWrapperVM wrapper)
```
##### `ClearAllElements`
```csharp
private void ClearAllElements()
```
##### `IsNameDuplicate`
```csharp
public bool IsNameDuplicate(ElementTypeWrapperVM element, string name)
```
Checks if an element name is a duplicate within the same category.

##### `Element_PropertyChanged`
```csharp
private void Element_PropertyChanged(object sender, PropertyChangedEventArgs e)
```
##### `if`
```csharp
else if (e.PropertyName == nameof(ElementTypeWrapperVM.AliasesString))
```
##### `DuplicateSelected`
```csharp
private void DuplicateSelected()
```
##### `DeleteSelected`
```csharp
private void DeleteSelected()
```
##### `CanMergeElements`
```csharp
private bool CanMergeElements()
```
##### `MergeElements`
```csharp
private void MergeElements(object ownerWindowParameter)
```
##### `if`
```csharp
else if (_uiapp != null)
```
##### `IsElementReferenced`
```csharp
public bool IsElementReferenced(ElementTypeWrapperVM element, out List<ElementTypeWrapperVM> referencingElements)
```
Checks if a given element is referenced by other elements in the JSON workspace.

**Parameters:**
- `element`: The element to check for references.
- `referencingElements`: When this method returns, contains the list of elements that reference the target element.

**Returns:** True if the element is referenced by any other elements; otherwise, false.

##### `ReplaceReferences`
```csharp
public void ReplaceReferences(ElementTypeWrapperVM oldElement, string newName)
```
Replaces all references to an old element with references to a new element name.

**Parameters:**
- `oldElement`: The old element whose references should be replaced.
- `newName`: The new name to associate with the references.

##### `SelectAll`
```csharp
private void SelectAll()
```
##### `SelectNone`
```csharp
private void SelectNone()
```
##### `OpenFindReplace`
```csharp
private void OpenFindReplace(Window owner)
```
##### `LoadFile`
```csharp
private void LoadFile()
```
##### `SaveFile`
```csharp
private void SaveFile()
```
##### `SaveFileAs`
```csharp
private void SaveFileAs()
```
##### `ImportSelected`
```csharp
private void ImportSelected(Window owner)
```
##### `Generate`
```csharp
private void Generate(Window owner)
```
##### `IngestGeneratedStyles`
```csharp
public void IngestGeneratedStyles(IEnumerable<ObjectModel> styles)
```
Safely ingests generated ElementModel and other ObjectModel elements into the editor.

##### `if`
```csharp
else if (elem is DimensionTypeModel) group = "DimensionTypes";
```
##### `if`
```csharp
else if (elem is HostObjTypeModel) group = "HostObjTypes";
```
##### `if`
```csharp
else if (elem is MaterialModel || elem is FillPatternElementModel || elem is LinePatternElementModel || elem is PropertySetElementModel) group = "Materials";
```
##### `if`
```csharp
else if (elem is ViewModel view)
```
##### `if`
```csharp
else if (elem is ViewFamilyTypeModel || elem is BrowserOrganizationModel) group = "Views";
```
##### `if`
```csharp
else if (elem is CategoryModel) group = "Categories";
```
##### `if`
```csharp
else if (elem is ElementTypeModel elemType && string.Equals(elemType.Class, "Autodesk.Revit.DB.TextNoteType", StringComparison.OrdinalIgnoreCase))
```
---

#### Fields
- **`_currentFilePath`**: `private string _currentFilePath;`
- **`_searchText`**: `private string _searchText = string.Empty;`
- **`_selectedElement`**: `private ElementTypeWrapperVM _selectedElement;`
- **`_preservedNonElementTypeModels`**: `private readonly List<ObjectModel> _preservedNonElementTypeModels = new List<ObjectModel>();`
- **`_groupedElementsView`**: `private readonly ICollectionView _groupedElementsView;`
- **`GroupedElementsView`**: `public ICollectionView GroupedElementsView => _groupedElementsView;`
  *Description:* Gets the grouped CollectionView for ListBox binding.
- **`_uiapp`**: `private readonly UIApplication _uiapp;`
- **`_externalEvent`**: `private ExternalEvent _externalEvent;`
- **`_eventHandler`**: `private JsonEditorExternalEventHandler _eventHandler;`
- **`false`**: `return false;`
- **`MainWindowHandle`**: `public IntPtr MainWindowHandle => _uiapp != null ? _uiapp.MainWindowHandle : IntPtr.Zero;`
  *Description:* Gets the parent Revit main window handle.
- **`UIApplication`**: `public UIApplication UIApplication => _uiapp;`
  *Description:* Gets the UIApplication instance.
- **`firstClass`**: `string firstClass = SelectedElements[0].Class;`
- **`firstCategory`**: `string firstCategory = SelectedElements[0].Category;`
- **`first`**: `string first = SelectedElements[0].AliasesString;`
- **`first`**: `return first;`
- **`IsSingleElementSelected`**: `public bool IsSingleElementSelected => SelectedElements.Count == 1;`
  *Description:* Gets whether exactly one element is selected (enabling single-element edits like renaming).
- **`IsNameEditingAllowed`**: `public bool IsNameEditingAllowed => IsSingleElementSelected && SelectedElement != null && SelectedElement.IsNameEditable;`
  *Description:* Gets whether name editing is allowed on the selected element.
- **`IsEditorVisible`**: `public bool IsEditorVisible => SelectedElements.Count > 0;`
  *Description:* Gets whether the parameter editor should be visible.
- **`jsonContent`**: `string jsonContent = File.ReadAllText(filePath);`
- **`serializeJSON`**: `ModelsToSerialize serializeJSON = JsonConvert.DeserializeObject<ModelsToSerialize>(jsonContent);`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "FillRegionTypes" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "ElementTypes" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "DimensionTypes" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "HostObjTypes" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "Materials" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "Views" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "ElementTypes" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "ElementTypes" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "Materials" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "Materials" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "Materials" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "HostObjTypes" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "ElementTypes" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "ElementTypes" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "ElementTypes" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "Views" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "Views" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "ElementTypes" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "Categories" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "TextNoteTypes" };`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(item) { Group = "ViewTemplates" };`
- **`model`**: `var model = w.GetUpdatedModel();`
- **`serializedJson`**: `string serializedJson = ModelsToSerialize.SerializeToJson(allModels);`
- **`firstElement`**: `var firstElement = SelectedElements[0];`
- **`currentElement`**: `var currentElement = SelectedElements[i];`
- **`firstVal`**: `string firstVal = matchingParams[0].Value;`
- **`isMixed`**: `bool isMixed = matchingParams.Any(p => p.Value != firstVal || p.IsMixedValue);`
- **`isReadOnly`**: `bool isReadOnly = matchingParams.Any(p => p.IsReadOnly);`
- **`guid`**: `string guid = matchingParams[0].GUID;`
- **`id`**: `long id = matchingParams[0].Id;`
- **`isShared`**: `bool isShared = matchingParams[0].IsShared;`
- **`displayParam`**: `var displayParam = new ParameterWrapperVM(dummyModel);`
- **`displayParam`**: `var displayParam = sender as ParameterWrapperVM;`
- **`targetParam`**: `var targetParam = element.Parameters.FirstOrDefault(p => p.Name == paramName);`
- **`newSelections`**: `var newSelections = new List<ElementTypeWrapperVM>();`
- **`originalModel`**: `var originalModel = item.GetUpdatedModel();`
- **`json`**: `var json = JsonConvert.SerializeObject(originalModel);`
- **`clonedModel`**: `var clonedModel = (ElementModel)JsonConvert.DeserializeObject(json, originalModel.GetType());`
- **`baseName`**: `string baseName = clonedModel.Name + " - Copy";`
- **`candidateName`**: `string candidateName = baseName;`
- **`counter`**: `int counter = 1;`
- **`cloneWrapper`**: `var cloneWrapper = new ElementTypeWrapperVM(clonedModel) { Group = item.Group };`
- **`toDelete`**: `var toDelete = SelectedElements.ToList();`
- **`replacementName`**: `string replacementName = vm.SelectedItem;`
- **`false`**: `return false;`
- **`firstCategory`**: `string firstCategory = SelectedElements[0].Category;`
- **`firstClass`**: `string firstClass = SelectedElements[0].Class;`
- **`owner`**: `var owner = ownerWindowParameter as Window;`
- **`vm`**: `var vm = new MergeSelectionViewModel(SelectedElements);`
- **`handle`**: `IntPtr handle = IntPtr.Zero;`
- **`helper`**: `var helper = new System.Windows.Interop.WindowInteropHelper(owner);`
- **`primary`**: `var primary = vm.SelectedPrimary;`
- **`nonPrimaries`**: `var nonPrimaries = SelectedElements.Where(e => e != primary).ToList();`
- **`currentAliases`**: `var currentAliases = primary.AliasesString;`
- **`nameAndAliases`**: `var nameAndAliases = new List<string> { element.Name };`
- **`isMatch`**: `bool isMatch = false;`
- **`nameAndAliases`**: `var nameAndAliases = new List<string> { oldElement.Name };`
- **`visibleElements`**: `var visibleElements = GroupedElementsView.Cast<ElementTypeWrapperVM>().ToList();`
- **`selectedModels`**: `var selectedModels = SelectedElements.Select(w => w.GetUpdatedModel()).ToList();`
- **`vm`**: `var vm = new ImportStylesViewModel(_uiapp, selectedModels);`
- **`view`**: `var view = new Views.ImportStylesView(_uiapp.MainWindowHandle) { DataContext = vm };`
- **`vm`**: `var vm = new ExportStylesViewModel(_uiapp, true);`
- **`view`**: `var view = new Views.ExportStylesView(_uiapp.MainWindowHandle) { DataContext = vm };`
- **`group`**: `string group = "ElementTypes";`
- **`existing`**: `ElementTypeWrapperVM existing = null;`
- **`wrapper`**: `var wrapper = new ElementTypeWrapperVM(elem) { Group = group };`

---

### Class: `ListByCheckboxViewModel`
**File:** [src/SyntheticShared/ViewModels/ListByCheckboxViewModel.cs](../src/SyntheticShared/ViewModels/ListByCheckboxViewModel.cs)

ViewModel for lists where items can be selected using checkboxes.

#### Properties
- **`CloseAction`**: `public Action<bool> CloseAction { get; set; }`
  *Description:* Gets or sets the action to close the window, passing a boolean result.
- **`OkCommand`**: `public ICommand OkCommand { get; }`
  *Description:* Gets the OK command.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the Cancel command.
- **`SelectAllCommand`**: `public ICommand SelectAllCommand { get; }`
  *Description:* Gets the Select All command.
- **`SelectNoneCommand`**: `public ICommand SelectNoneCommand { get; }`
  *Description:* Gets the Select None command.

#### Methods
##### `ListByCheckboxViewModel`
```csharp
public ListByCheckboxViewModel()
```
Initializes a new instance of the <see cref="ListByCheckboxViewModel"/> class.

##### `SetItems`
```csharp
public void SetItems(IEnumerable<string> itemNames, bool checkAll = false)
```
Sets the checkable items from a list of item names.

**Parameters:**
- `itemNames`: The collection of item names.
- `checkAll`: Whether to check all items by default.

##### `SetItems`
```csharp
public void SetItems(IEnumerable<Tuple<string, ElementId>> items, bool checkAll = false)
```
Sets the checkable items from a list of item names.

**Parameters:**
- `itemNames`: The collection of item names.
- `checkAll`: Whether to check all items by default.

##### `OnItemCheckChanged`
```csharp
private void OnItemCheckChanged(CheckableItem changedItem)
```
##### `CanOk`
```csharp
private bool CanOk(object parameter)
```
##### `OnOk`
```csharp
private void OnOk(object parameter)
```
##### `OnCancel`
```csharp
private void OnCancel(object parameter)
```
##### `CanSelectAll`
```csharp
private bool CanSelectAll(object parameter) => !IsSingleSelection;
```
##### `OnSelectAll`
```csharp
private void OnSelectAll(object parameter)
```
##### `CanSelectNone`
```csharp
private bool CanSelectNone(object parameter) => true;
```
##### `OnSelectNone`
```csharp
private void OnSelectNone(object parameter)
```
---

#### Fields
- **`_title`**: `private string _title = "Title";`
- **`_instruction`**: `private string _instruction = "Instructions";`
- **`_items`**: `private ObservableCollection<CheckableItem> _items = new ObservableCollection<CheckableItem>();`
- **`_isSorted`**: `private bool _isSorted = true;`
- **`_isSingleSelection`**: `private bool _isSingleSelection;`
- **`_isUpdating`**: `private bool _isUpdating;`
- **`list`**: `var list = itemNames ?? new List<string>();`
- **`list`**: `var list = items ?? new List<Tuple<string, ElementId>>();`

---

### Enum: `ManageTemplatesAction`
**File:** [src/SyntheticShared/ViewModels/ManageTemplatesViewModel.cs](../src/SyntheticShared/ViewModels/ManageTemplatesViewModel.cs)

Specifies the actions that can be requested when managing templates.

### Class: `ManageTemplatesViewModel`
**File:** [src/SyntheticShared/ViewModels/ManageTemplatesViewModel.cs](../src/SyntheticShared/ViewModels/ManageTemplatesViewModel.cs)

ViewModel controlling the CRUD interface and state loop for managing templates.

#### Properties
- **`Templates`**: `public ObservableCollection<TagTemplate> Templates { get; set; }`
  *Description:* Gets or sets the collection of tag templates.
- **`RequestedAction`**: `public ManageTemplatesAction RequestedAction { get; private set; } = ManageTemplatesAction.None;`
  *Description:* Gets the action requested by the view model.
- **`TemplateToEdit`**: `public TagTemplate TemplateToEdit { get; private set; }`
  *Description:* Gets the template selected for editing.
- **`CloseAction`**: `public Action CloseAction { get; set; }`
  *Description:* Gets or sets the action to close the window.

#### Methods
##### `ManageTemplatesViewModel`
```csharp
public ManageTemplatesViewModel(Document doc)
```
Initializes a new instance of the <see cref="ManageTemplatesViewModel"/> class.  <param name="doc">The active Revit document.</param>

##### `LoadTemplates`
```csharp
private void LoadTemplates()
```
##### `CanExecuteSelectionBased`
```csharp
private bool CanExecuteSelectionBased(object obj) => SelectedTemplate != null;
```
##### `CanExecuteExport`
```csharp
private bool CanExecuteExport(object obj) => Templates != null && Templates.Any();
```
##### `ExecuteDelete`
```csharp
private void ExecuteDelete(object obj)
```
##### `ExecuteExport`
```csharp
private void ExecuteExport(object obj)
```
##### `ExecuteImport`
```csharp
private void ExecuteImport(object obj)
```
##### `ExecuteEdit`
```csharp
private void ExecuteEdit(object obj)
```
##### `ExecuteNewTemplate`
```csharp
private void ExecuteNewTemplate(object obj)
```
---

#### Fields
- **`_doc`**: `private readonly Document _doc;`
- **`_repository`**: `private readonly TemplateStorageRepository _repository;`
- **`_selectedTemplate`**: `private TagTemplate _selectedTemplate;`
- **`dbTemplates`**: `var dbTemplates = _repository.GetTemplates(_doc);`
- **`imported`**: `var imported = _repository.ImportFromJson(ofd.FileName);`

---

### Class: `MaterialLibraryViewModel`
**File:** [src/SyntheticShared/ViewModels/MaterialLibraryViewModel.cs](../src/SyntheticShared/ViewModels/MaterialLibraryViewModel.cs)

ViewModel for managing Material Library configuration settings.

#### Properties
- **`ConfigureCommand`**: `public ICommand ConfigureCommand { get; }`
  *Description:* <inheritdoc/>

#### Methods
##### `MaterialLibraryViewModel`
```csharp
public MaterialLibraryViewModel(Document doc, IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="MaterialLibraryViewModel"/> class.  <param name="doc">The Revit Document.</param> <param name="mainWindowHandle">The parent main window handle.</param>

##### `OnConfigure`
```csharp
private void OnConfigure(object parameter)
```
##### `Save`
```csharp
public void Save()
```
<inheritdoc/>

##### `Reload`
```csharp
public void Reload()
```
<inheritdoc/>

---

#### Fields
- **`_doc`**: `private readonly Document _doc;`
- **`_mainWindowHandle`**: `private readonly IntPtr _mainWindowHandle;`
- **`_settings`**: `private MaterialLibrarySettings _settings;`
- **`_isOverridden`**: `private bool _isOverridden;`
- **`_isDirty`**: `private bool _isDirty;`
- **`ModuleName`**: `public string ModuleName => "Material Library";`
  *Description:* <inheritdoc/>
- **`defaultSettings`**: `var defaultSettings = new MaterialLibrarySettings().Defaults();`
- **`defaultPath`**: `string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");`
- **`defaultConfig`**: `Config defaultConfig = Config.ReadFromFile(defaultPath);`
- **`storageName`**: `string storageName = "Synthetic_" + MaterialLibrarySettings.Name;`
- **`initialPath`**: `string initialPath = _settings.LibraryFolderPath;`
- **`selectedPath`**: `string selectedPath = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Material Library Folder", initialPath);`
- **`storageName`**: `string storageName = "Synthetic_" + MaterialLibrarySettings.Name;`

---

### Class: `MergeDetailedReviewViewModel`
**File:** [src/SyntheticShared/ViewModels/MergeDetailedReviewViewModel.cs](../src/SyntheticShared/ViewModels/MergeDetailedReviewViewModel.cs)

ViewModel for the detailed merge review view.

#### Properties
- **`CmdCommitToQueue`**: `public ICommand CmdCommitToQueue { get; }`
  *Description:* Gets the command to commit the cluster to the queue.
- **`CmdMoveItem`**: `public ICommand CmdMoveItem { get; }`
  *Description:* Gets the command to move the duplicate item out of this cluster.
- **`CloseAction`**: `public Action CloseAction { get; set; }`
  *Description:* Gets or sets the action to close the window.

#### Methods
##### `ExecuteCommitToQueue`
```csharp
private void ExecuteCommitToQueue(object parameter)
```
##### `ExecuteMoveItem`
```csharp
private void ExecuteMoveItem(object parameter)
```
---

#### Fields
- **`_cluster`**: `private DuplicateClusterModel _cluster;`
- **`_selectedTypeMapping`**: `private TypeMappingModel _selectedTypeMapping;`
- **`_commitCallback`**: `private Action<DuplicateClusterModel> _commitCallback;`
- **`_moveItemCallback`**: `private Action<DuplicateClusterModel> _moveItemCallback;`
- **`view`**: `var view = System.Windows.Data.CollectionViewSource.GetDefaultView(cluster.TypeMappings);`
- **`hasSchema`**: `bool hasSchema = Cluster.HasSchemaMismatch;`
- **`hasConflicts`**: `bool hasConflicts = false;`

---

### Class: `MergeDuplicatesViewModel`
**File:** [src/SyntheticShared/ViewModels/MergeDuplicatesViewModel.cs](../src/SyntheticShared/ViewModels/MergeDuplicatesViewModel.cs)

ViewModel for scanning and merging duplicate elements in the Revit database.

#### Properties
- **`CmdScanModel`**: `public ICommand CmdScanModel { get; }`
  *Description:* Gets the command to scan the model for duplicates.
- **`CmdLoadSelection`**: `public ICommand CmdLoadSelection { get; }`
  *Description:* Gets the command to load the current Revit selection.
- **`CmdAddToQueue`**: `public ICommand CmdAddToQueue { get; }`
  *Description:* Gets the command to add a cluster to the merge queue.
- **`CmdRemoveFromQueue`**: `public ICommand CmdRemoveFromQueue { get; }`
  *Description:* Gets the command to remove a cluster from the merge queue.
- **`CmdProcessMerge`**: `public ICommand CmdProcessMerge { get; }`
  *Description:* Gets the command to execute the merge queue.
- **`CmdCancelScan`**: `public ICommand CmdCancelScan { get; }`
  *Description:* Gets the command to cancel an active scan.
- **`CmdMoveItem`**: `public ICommand CmdMoveItem { get; }`
  *Description:* Gets the command to move an item from one cluster to another.
- **`CmdDetailedReview`**: `public ICommand CmdDetailedReview { get; }`
  *Description:* Gets the command to launch the detailed review window for a cluster.
- **`CmdAddStragglers`**: `public ICommand CmdAddStragglers { get; }`
  *Description:* Gets the command to add straggler elements to the duplicate cluster.

#### Methods
##### `MergeDuplicatesViewModel`
```csharp
public MergeDuplicatesViewModel()
```
Initializes a new instance of the <see cref="MergeDuplicatesViewModel"/> class.

##### `ExecuteScanModel`
```csharp
private void ExecuteScanModel(object parameter)
```
##### `ExecuteLoadSelection`
```csharp
private void ExecuteLoadSelection(object parameter)
```
##### `ExecuteAddToQueue`
```csharp
private void ExecuteAddToQueue(object parameter)
```
##### `ExecuteMoveItem`
```csharp
private void ExecuteMoveItem(object parameter)
```
##### `ExecuteDetailedReview`
```csharp
private void ExecuteDetailedReview(object parameter)
```
##### `ExecuteProcessMerge`
```csharp
private void ExecuteProcessMerge(object parameter)
```
##### `ExecuteCancelScan`
```csharp
private void ExecuteCancelScan(object parameter)
```
##### `ExecuteRemoveFromQueue`
```csharp
private void ExecuteRemoveFromQueue(object parameter)
```
##### `ExecuteAddStragglers`
```csharp
private void ExecuteAddStragglers(object parameter)
```
---

#### Fields
- **`_scannedClusters`**: `private ObservableCollection<DuplicateClusterModel> _scannedClusters;`
- **`_queueVM`**: `private MergeQueueViewModel _queueVM;`
- **`_progressValue`**: `private double _progressValue;`
- **`_progressMax`**: `private double _progressMax = 100;`
- **`_mainWindowHandle`**: `private IntPtr _mainWindowHandle;`
- **`_document`**: `private Document _document;`
- **`_mergeHandler`**: `private ProcessMergeEventHandler _mergeHandler;`
- **`_mergeEvent`**: `private ExternalEvent _mergeEvent;`
- **`QueuedClusters`**: `public ObservableCollection<DuplicateClusterModel> QueuedClusters => QueueVM?.QueuedClusters;`
  *Description:* Gets the collection of duplicate clusters queued for merging.
- **`hasSchemaMismatch`**: `bool hasSchemaMismatch = cluster.HasSchemaMismatch;`
- **`hasConflicts`**: `bool hasConflicts = false;`
- **`resolutionsCount`**: `int resolutionsCount = cluster.ParameterResolutions?.Count ?? 0;`
- **`isBlocked`**: `bool isBlocked = (hasSchemaMismatch || hasConflicts) && (resolutionsCount == 0);`
- **`itemNames`**: `var itemNames = sourceCluster.Items.Select(i => i.ItemName).ToList();`
- **`selectedItemName`**: `string selectedItemName = itemSelectVM.SelectedItem;`
- **`itemToMove`**: `var itemToMove = sourceCluster.Items.FirstOrDefault(i => i.ItemName == selectedItemName);`
- **`selectedClusterName`**: `string selectedClusterName = clusterSelectVM.SelectedItem;`
- **`targetCluster`**: `var targetCluster = ScannedClusters.FirstOrDefault(c => c.ClusterName == selectedClusterName);`
- **`newPrimary`**: `var newPrimary = sourceCluster.Items.OrderBy(i => i.ItemName.Length).First();`
- **`newPrimary`**: `var newPrimary = targetCluster.Items.OrderBy(i => i.ItemName.Length).First();`
- **`window`**: `var window = parameter as System.Windows.Window;`
- **`categoryName`**: `string categoryName = cluster.Items.FirstOrDefault()?.CategoryName;`
- **`candidateList`**: `var candidateList = new List<Tuple<string, ElementId>>();`
- **`existingIds`**: `var existingIds = new HashSet<ElementId>(cluster.Items.Select(i => i.RevitElementId));`
- **`classes`**: `var classes = new List<Type> { typeof(Family), typeof(GroupType), typeof(AssemblyType) };`
- **`filter`**: `var filter = new ElementMulticlassFilter(classes);`
- **`collector`**: `var collector = new FilteredElementCollector(Document).WherePasses(filter);`
- **`cat`**: `string cat = MergeAnalysisEngine.GetElementCategoryName(Document, elem);`
- **`cat`**: `string cat = MergeAnalysisEngine.GetElementCategoryName(Document, et);`
- **`selectedIds`**: `var selectedIds = listVM.CheckedElementIds;`
- **`elem`**: `var elem = Document.GetElement(id);`
- **`symbolIds`**: `var symbolIds = family.GetFamilySymbolIds();`
- **`symbol`**: `var symbol = Document.GetElement(sId) as FamilySymbol;`

---

### Class: `MergeQueueViewModel`
**File:** [src/SyntheticShared/ViewModels/MergeQueueViewModel.cs](../src/SyntheticShared/ViewModels/MergeQueueViewModel.cs)

ViewModel for managing a queue of elements to merge.

#### Properties
- **`CmdRemoveFromQueue`**: `public ICommand CmdRemoveFromQueue { get; }`
  *Description:* Gets the command to remove a cluster from the merge queue.
- **`CmdClearQueue`**: `public ICommand CmdClearQueue { get; }`
  *Description:* Gets the command to clear the entire merge queue.

#### Methods
##### `MergeQueueViewModel`
```csharp
public MergeQueueViewModel() : this(null)
```
Initializes a new instance of the <see cref="MergeQueueViewModel"/> class.

##### `MergeQueueViewModel`
```csharp
public MergeQueueViewModel(Action<DuplicateClusterModel> onRemoved)
```
Initializes a new instance of the <see cref="MergeQueueViewModel"/> class.  <param name="onRemoved">Callback triggered when an item is removed from the queue.</param>

##### `RemoveFromQueue`
```csharp
public void RemoveFromQueue(DuplicateClusterModel cluster)
```
Removes a duplicate cluster from the queue.

**Parameters:**
- `cluster`: The duplicate cluster to remove.

##### `ClearQueue`
```csharp
public void ClearQueue()
```
Clears all duplicate clusters from the queue.

##### `OnRemoveFromQueue`
```csharp
private void OnRemoveFromQueue(object parameter)
```
##### `OnClearQueue`
```csharp
private void OnClearQueue(object parameter)
```
---

#### Fields
- **`_queuedClusters`**: `private ObservableCollection<DuplicateClusterModel> _queuedClusters;`
- **`_onRemoved`**: `private readonly Action<DuplicateClusterModel> _onRemoved;`
- **`clusters`**: `var clusters = _queuedClusters.ToList();`

---

### Class: `MergeSelectionViewModel`
**File:** [src/SyntheticShared/ViewModels/MergeSelectionViewModel.cs](../src/SyntheticShared/ViewModels/MergeSelectionViewModel.cs)

ViewModel for selecting the primary element when merging multiple elements.

#### Properties
- **`CloseAction`**: `public Action<bool> CloseAction { get; set; }`
  *Description:* Gets or sets the action to close the window, passing a boolean result.
- **`OkCommand`**: `public ICommand OkCommand { get; }`
  *Description:* Gets the OK command.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the Cancel command.

#### Methods
##### `MergeSelectionViewModel`
```csharp
public MergeSelectionViewModel(IEnumerable<ElementTypeWrapperVM> elements)
```
Initializes a new instance of the <see cref="MergeSelectionViewModel"/> class.  <param name="elements">The collection of elements available to choose from as the primary master style.</param>

##### `CanOk`
```csharp
private bool CanOk(object parameter)
```
##### `OnOk`
```csharp
private void OnOk(object parameter)
```
##### `OnCancel`
```csharp
private void OnCancel(object parameter)
```
---

#### Fields
- **`_elements`**: `private IEnumerable<ElementTypeWrapperVM> _elements;`
- **`_selectedPrimary`**: `private ElementTypeWrapperVM _selectedPrimary;`

---

### Class: `NestedDataEditorViewModel`
**File:** [src/SyntheticShared/ViewModels/NestedDataEditorViewModel.cs](../src/SyntheticShared/ViewModels/NestedDataEditorViewModel.cs)

ViewModel for the nested data editor window, which handles editing compound layers, visibility overrides, or object properties.

#### Properties
- **`IsLayersMode`**: `public bool IsLayersMode { get; }`
  *Description:* Gets a value indicating whether the editor is in compound layers mode.
- **`IsOverridesMode`**: `public bool IsOverridesMode { get; }`
  *Description:* Gets a value indicating whether the editor is in visibility overrides mode.
- **`IsPropertyGridMode`**: `public bool IsPropertyGridMode { get; }`
  *Description:* Gets a value indicating whether the editor is in property grid mode.
- **`Layers`**: `public ObservableCollection<CompoundLayerRowVM> Layers { get; } = new ObservableCollection<CompoundLayerRowVM>();`
  *Description:* Gets the collection of compound structure layer rows.
- **`Overrides`**: `public ObservableCollection<VisibilityOverrideRowVM> Overrides { get; } = new ObservableCollection<VisibilityOverrideRowVM>();`
  *Description:* Gets the collection of visibility override rows.
- **`PropertyGridProperties`**: `public ObservableCollection<ParameterWrapperVM> PropertyGridProperties { get; private set; }`
  *Description:* Gets the collection of properties shown in the property grid mode.
- **`AvailableMaterials`**: `public List<string> AvailableMaterials { get; } = new List<string> { "<By Category>" };`
  *Description:* Gets the list of available materials.
- **`AvailablePatterns`**: `public List<string> AvailablePatterns { get; } = new List<string> { "<None>" };`
  *Description:* Gets the list of available patterns.
- **`AvailableLineStyles`**: `public List<string> AvailableLineStyles { get; } = new List<string> { "<None>" };`
  *Description:* Gets the list of available line styles.
- **`SaveCommand`**: `public ICommand SaveCommand { get; }`
  *Description:* Gets the Save command.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the Cancel command.
- **`CloseAction`**: `public Action CloseAction { get; set; }`
  *Description:* Gets or sets the close action for the window.
- **`DialogResult`**: `public bool DialogResult { get; private set; }`
  *Description:* Gets a value indicating the dialog result status when the window is closed.

#### Methods
##### `NestedDataEditorViewModel`
```csharp
public NestedDataEditorViewModel(object nestedData, ObservableCollection<ElementTypeWrapperVM> wrappedElements)
```
Initializes a new instance of the <see cref="NestedDataEditorViewModel"/> class.  <param name="nestedData">The nested data object to edit.</param> <param name="wrappedElements">A collection of wrapped elements for reference lookup.</param>

##### `if`
```csharp
else if (nestedData is List<CategoryGraphicOverridesModel> overridesList)
```
##### `if`
```csharp
else if (nestedData is IEnumerable list && !(nestedData is string))
```
##### `PopulateAvailableReferences`
```csharp
private void PopulateAvailableReferences()
```
##### `if`
```csharp
else if (string.Equals(el.Class, "Autodesk.Revit.DB.FillPatternElement", StringComparison.OrdinalIgnoreCase))
```
##### `if`
```csharp
else if (string.Equals(el.Class, "Autodesk.Revit.DB.LinePatternElement", StringComparison.OrdinalIgnoreCase))
```
##### `PopulateOverrides`
```csharp
private void PopulateOverrides(List<CategoryGraphicOverridesModel> existingOverrides)
```
##### `CreateDefaultOverride`
```csharp
private CategoryGraphicOverridesModel CreateDefaultOverride(RevitDB.Category category, RevitDB.Document doc)
```
##### `SortReferenceList`
```csharp
private void SortReferenceList(List<string> list)
```
##### `PopulatePropertyGridFromPoco`
```csharp
private void PopulatePropertyGridFromPoco(object poco)
```
##### `if`
```csharp
else if (val is double || val is float) storageType = "Double";
```
##### `if`
```csharp
else if (val is bool) storageType = "Boolean";
```
##### `ExecuteSave`
```csharp
private void ExecuteSave()
```
##### `if`
```csharp
else if (IsOverridesMode && _nestedData is List<CategoryGraphicOverridesModel> overridesList)
```
##### `if`
```csharp
else if (IsPropertyGridMode)
```
##### `SavePropertyGridChanges`
```csharp
private void SavePropertyGridChanges(object poco)
```
##### `if`
```csharp
else if (prop.PropertyType == typeof(string))
```
##### `if`
```csharp
else if (prop.PropertyType == typeof(bool))
```
##### `if`
```csharp
else if (prop.PropertyType == typeof(int))
```
##### `if`
```csharp
else if (prop.PropertyType == typeof(double))
```
##### `ExecuteCancel`
```csharp
private void ExecuteCancel()
```
---

#### Fields
- **`_nestedData`**: `private readonly object _nestedData;`
- **`_wrappedElements`**: `private readonly ObservableCollection<ElementTypeWrapperVM> _wrappedElements;`
- **`cv`**: `var cv = System.Windows.Data.CollectionViewSource.GetDefaultView(Overrides);`
- **`uiapp`**: `var uiapp = JsonEditorMainViewModel.Instance?.UIApplication;`
- **`doc`**: `var doc = uiapp?.ActiveUIDocument?.Document;`
- **`materials`**: `var materials = Select.AllMaterials(doc);`
- **`uiapp`**: `var uiapp = JsonEditorMainViewModel.Instance?.UIApplication;`
- **`doc`**: `var doc = uiapp?.ActiveUIDocument?.Document;`
- **`categories`**: `var categories = doc.Settings.Categories;`
- **`existingLookup`**: `var existingLookup = new Dictionary<string, CategoryGraphicOverridesModel>(StringComparer.OrdinalIgnoreCase);`
- **`parentOverride`**: `CategoryGraphicOverridesModel parentOverride;`
- **`subOverride`**: `CategoryGraphicOverridesModel subOverride;`
- **`catOverride`**: `return catOverride;`
- **`sorted`**: `var sorted = list.Distinct().OrderBy(s => (s == "<None>" || s == "<By Category>") ? "" : s).ToList();`
- **`name`**: `string name = prop.Name;`
- **`valueStr`**: `string valueStr = "";`
- **`val`**: `object val = prop.GetValue(poco);`
- **`valElemId`**: `ElementIdModel valElemId = null;`
- **`storageType`**: `string storageType = "String";`
- **`isReadOnly`**: `bool isReadOnly = !prop.CanWrite;`
- **`paramModel`**: `var paramModel = new ParameterModel(name, valueStr, valElemId, storageType, 0, null, false, isReadOnly);`
- **`wrapper`**: `var wrapper = new ParameterWrapperVM(paramModel);`

---

### Class: `NetworkPathsWizardViewModel`
**File:** [src/SyntheticShared/ViewModels/NetworkPathsWizardViewModel.cs](../src/SyntheticShared/ViewModels/NetworkPathsWizardViewModel.cs)

ViewModel for the Network Paths and File Utility Configuration Wizard.

#### Properties
- **`ArchiveDirectories`**: `public ObservableCollection<string> ArchiveDirectories { get; }`
  *Description:* Gets the collection of archive directories.
- **`AlternateMappings`**: `public ObservableCollection<PathMappingViewModel> AlternateMappings { get; }`
  *Description:* Gets the collection of alternate paths mapping views.
- **`AddArchiveCommand`**: `public ICommand AddArchiveCommand { get; }`
  *Description:* Gets the command to add a new archive directory.
- **`RemoveArchiveCommand`**: `public ICommand RemoveArchiveCommand { get; }`
  *Description:* Gets the command to remove the selected archive directory.
- **`BrowseArchiveCommand`**: `public ICommand BrowseArchiveCommand { get; }`
  *Description:* Gets the command to browse and select an archive directory.
- **`AddMappingCommand`**: `public ICommand AddMappingCommand { get; }`
  *Description:* Gets the command to add a new alternate path mapping row.
- **`RemoveMappingCommand`**: `public ICommand RemoveMappingCommand { get; }`
  *Description:* Gets the command to remove the selected alternate path mapping row.
- **`BrowseMappingCommand`**: `public ICommand BrowseMappingCommand { get; }`
  *Description:* Gets the command to browse and select a local mapped path.
- **`OkCommand`**: `public ICommand OkCommand { get; }`
  *Description:* Gets the command to validate paths and close the wizard on success.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the command to cancel editing and close the wizard.

#### Methods
##### `NetworkPathsWizardViewModel`
```csharp
public NetworkPathsWizardViewModel(FileUtilitySettings settings, IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="NetworkPathsWizardViewModel"/> class.  <param name="settings">The initial settings copy to edit.</param> <param name="mainWindowHandle">The parent main window handle.</param>

##### `OnAddArchive`
```csharp
private void OnAddArchive(object parameter)
```
##### `OnRemoveArchive`
```csharp
private void OnRemoveArchive(object parameter)
```
##### `OnBrowseArchive`
```csharp
private void OnBrowseArchive(object parameter)
```
##### `OnAddMapping`
```csharp
private void OnAddMapping(object parameter)
```
##### `OnRemoveMapping`
```csharp
private void OnRemoveMapping(object parameter)
```
##### `OnBrowseMapping`
```csharp
private void OnBrowseMapping(object parameter)
```
##### `OnOk`
```csharp
private void OnOk(object parameter)
```
##### `OnCancel`
```csharp
private void OnCancel(object parameter)
```
---

#### Fields
- **`_mainWindowHandle`**: `private readonly IntPtr _mainWindowHandle;`
- **`_selectedArchiveDirectory`**: `private string _selectedArchiveDirectory;`
- **`_selectedMapping`**: `private PathMappingViewModel _selectedMapping;`
- **`_isValidating`**: `private bool _isValidating;`
- **`mapped`**: `string mapped = kvp.Value != null && kvp.Value.Count > 0 ? kvp.Value[0] : string.Empty;`
- **`selected`**: `string selected = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Archive Directory");`
- **`selected`**: `string selected = FileDialogHelper.SelectFolder(_mainWindowHandle, "Browse Archive Directory", SelectedArchiveDirectory);`
- **`index`**: `int index = ArchiveDirectories.IndexOf(SelectedArchiveDirectory);`
- **`newMapping`**: `var newMapping = new PathMappingViewModel();`
- **`initialPath`**: `string initialPath = SelectedMapping.LocalMappedPath;`
- **`selected`**: `string selected = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Local Mapped Path", initialPath);`
- **`pathsToCheck`**: `var pathsToCheck = new List<string>();`
- **`anyOffline`**: `bool anyOffline = false;`
- **`result`**: `var result = dialog.Show();`

---

### Class: `ParameterWrapperVM`
**File:** [src/SyntheticShared/ViewModels/ParameterWrapperVM.cs](../src/SyntheticShared/ViewModels/ParameterWrapperVM.cs)

ViewModel wrapper for the ParameterModel POCO. Supports two-way data binding, field-locking, <Varies> state, and validation.

#### Methods
##### `ParameterWrapperVM`
```csharp
public ParameterWrapperVM(ParameterModel model)
```
Initializes a new instance of the <see cref="ParameterWrapperVM"/> class.  <param name="model">The underlying ParameterModel POCO.</param>

##### `GetModel`
```csharp
public ParameterModel GetModel()
```
Gets the underlying ParameterModel POCO reference.

**Returns:** The ParameterModel POCO.

##### `if`
```csharp
else if (Name.IndexOf("LinePattern", StringComparison.OrdinalIgnoreCase) >= 0 || Name.IndexOf("Line Pattern", StringComparison.OrdinalIgnoreCase) >= 0)
```
##### `if`
```csharp
else if (Name.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0)
```
##### `if`
```csharp
else if (string.Equals(targetClass, "Autodesk.Revit.DB.FillPatternElement", StringComparison.OrdinalIgnoreCase))
```
##### `if`
```csharp
else if (string.Equals(targetClass, "Autodesk.Revit.DB.LinePatternElement", StringComparison.OrdinalIgnoreCase))
```
##### `if`
```csharp
else if (!string.IsNullOrEmpty(targetCategory) && string.Equals(el.Category, targetCategory, StringComparison.OrdinalIgnoreCase))
```
##### `EditNestedData`
```csharp
private void EditNestedData()
```
##### `if`
```csharp
else if (Name == "VisibilityGraphics")
```
##### `GetErrors`
```csharp
public IEnumerable GetErrors(string propertyName)
```
Gets the validation errors for a specific property.

**Parameters:**
- `propertyName`: The name of the property to get errors for.

**Returns:** A collection of error messages.

##### `OnErrorsChanged`
```csharp
protected virtual void OnErrorsChanged(string propertyName)
```
Raises the ErrorsChanged event and notifies that HasErrors has changed.

**Parameters:**
- `propertyName`: The name of the property whose validation errors changed.

##### `ValidateValue`
```csharp
private void ValidateValue()
```
Validates the Value property based on StorageType.

##### `if`
```csharp
else if (StorageType == "Double")
```
##### `AddError`
```csharp
private void AddError(string propertyName, string error)
```
Adds a validation error to the dictionary and triggers ErrorsChanged.

---

#### Fields
- **`_model`**: `private readonly ParameterModel _model;`
- **`_isMixedValue`**: `private bool _isMixedValue;`
- **`_editNestedDataCommand`**: `private ICommand _editNestedDataCommand;`
- **`_model`**: `return _model;`
- **`Name`**: `public string Name => _model.Name;`
  *Description:* Gets the parameter Name. (Read-Only)
- **`StorageType`**: `public string StorageType => _model.StorageType;`
  *Description:* Gets the parameter StorageType (e.g. Integer, Double, ElementId, String). (Read-Only)
- **`Id`**: `public long Id => _model.Id;`
  *Description:* Gets the parameter Id. (Read-Only)
- **`GUID`**: `public string GUID => _model.GUID;`
  *Description:* Gets the parameter GUID if it is a shared parameter. (Read-Only)
- **`IsShared`**: `public bool IsShared => _model.IsShared;`
  *Description:* Gets whether the parameter is a shared parameter. (Read-Only)
- **`IsReadOnly`**: `public bool IsReadOnly => _model.IsReadOnly;`
  *Description:* Gets whether the parameter is read-only. (Read-Only)
- **`ValueElemId`**: `public ElementIdModel ValueElemId => _model.ValueElemId;`
  *Description:* Gets the full ElementIdModel for reference element parameters. (Read-Only)
- **`ValueElemIdClass`**: `public string ValueElemIdClass => _model.ValueElemId?.Class;`
  *Description:* Gets the Class of the reference element parameter. (Read-Only)
- **`ValueElemIdCategory`**: `public string ValueElemIdCategory => _model.ValueElemId?.Category;`
  *Description:* Gets the Category of the reference element parameter. (Read-Only)
- **`ValueElemIdName`**: `public string ValueElemIdName => _model.ValueElemId?.Name;`
  *Description:* Gets the Name of the reference element parameter. (Read-Only)
- **`noneOption`**: `return noneOption;`
- **`cleanValue`**: `string cleanValue = value;`
- **`targetClass`**: `string targetClass = ValueElemIdClass;`
- **`targetCategory`**: `string targetCategory = ValueElemIdCategory;`
- **`list`**: `var list = new List<string> { noneOption };`
- **`uiapp`**: `var uiapp = JsonEditorMainViewModel.Instance?.UIApplication;`
- **`doc`**: `var doc = uiapp?.ActiveUIDocument?.Document;`
- **`t`**: `Type t = assembly.GetType(targetClass);`
- **`isMatch`**: `bool isMatch = false;`
- **`_editNestedDataCommand`**: `return _editNestedDataCommand;`
- **`mainVM`**: `var mainVM = JsonEditorMainViewModel.Instance;`
- **`ownerHandle`**: `IntPtr ownerHandle = mainVM.MainWindowHandle;`
- **`selectedWrapper`**: `var selectedWrapper = mainVM.SelectedElement;`
- **`targetData`**: `object targetData = null;`
- **`model`**: `var model = selectedWrapper.GetUpdatedModel();`
- **`model`**: `var model = selectedWrapper.GetUpdatedModel();`
- **`nestedVM`**: `var nestedVM = new NestedDataEditorViewModel(targetData, mainVM.WrappedElements);`
- **`window`**: `var window = new Views.NestedDataEditorWindow(ownerHandle, nestedVM);`
- **`HasErrors`**: `public bool HasErrors => _errors.Any(kvp => kvp.Value != null && kvp.Value.Count > 0);`
  *Description:* Gets whether there are any validation errors.
- **`errors`**: `return errors;`
- **`currentValue`**: `string currentValue = Value;`

---

### Class: `PathMappingViewModel`
**File:** [src/SyntheticShared/ViewModels/PathMappingViewModel.cs](../src/SyntheticShared/ViewModels/PathMappingViewModel.cs)

ViewModel representing a single path mapping entry (Original -> LocalMapped).

#### Methods
##### `PathMappingViewModel`
```csharp
public PathMappingViewModel()
```
Initializes a new instance of the <see cref="PathMappingViewModel"/> class.

##### `PathMappingViewModel`
```csharp
public PathMappingViewModel(string originalServerPath, string localMappedPath)
```
Initializes a new instance of the <see cref="PathMappingViewModel"/> class with initial paths.

---

#### Fields
- **`_originalServerPath`**: `private string _originalServerPath;`
- **`_localMappedPath`**: `private string _localMappedPath;`

---

### Class: `ProjectMaterialsViewModel`
**File:** [src/SyntheticShared/ViewModels/ProjectMaterialsViewModel.cs](../src/SyntheticShared/ViewModels/ProjectMaterialsViewModel.cs)

ViewModel for managing Project-specific Material paths.

#### Properties
- **`ConfigureCommand`**: `public ICommand ConfigureCommand { get; }`
  *Description:* <inheritdoc/>

#### Methods
##### `ProjectMaterialsViewModel`
```csharp
public ProjectMaterialsViewModel(Document doc, IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="ProjectMaterialsViewModel"/> class.  <param name="doc">The Revit Document.</param> <param name="mainWindowHandle">The parent main window handle.</param>

##### `OnConfigure`
```csharp
private void OnConfigure(object parameter)
```
##### `Save`
```csharp
public void Save()
```
<inheritdoc/>

##### `Reload`
```csharp
public void Reload()
```
<inheritdoc/>

---

#### Fields
- **`_doc`**: `private readonly Document _doc;`
- **`_mainWindowHandle`**: `private readonly IntPtr _mainWindowHandle;`
- **`_settings`**: `private ProjectMaterialSettings _settings;`
- **`_isOverridden`**: `private bool _isOverridden;`
- **`_isDirty`**: `private bool _isDirty;`
- **`ModuleName`**: `public string ModuleName => "Project Materials";`
  *Description:* <inheritdoc/>
- **`false`**: `return false;`
- **`false`**: `return false;`
- **`true`**: `return true;`
- **`resolved`**: `string resolved = _settings.GetResolvedPath(_doc);`
- **`resolvedText`**: `string resolvedText = string.IsNullOrEmpty(resolved) ? "Unresolved (Document is unsaved)" : resolved;`
- **`defaultSettings`**: `var defaultSettings = new ProjectMaterialSettings().Defaults();`
- **`defaultPath`**: `string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");`
- **`defaultConfig`**: `Config defaultConfig = Config.ReadFromFile(defaultPath);`
- **`storageName`**: `string storageName = "Synthetic_" + ProjectMaterialSettings.Name;`
- **`initialPath`**: `string initialPath = _settings.OverrideFolderPath;`
- **`selectedPath`**: `string selectedPath = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Project Materials Folder", initialPath);`
- **`storageName`**: `string storageName = "Synthetic_" + ProjectMaterialSettings.Name;`

---

### Class: `RelayCommand`
**File:** [src/SyntheticShared/ViewModels/RelayCommand.cs](../src/SyntheticShared/ViewModels/RelayCommand.cs)

A command implementation that relays its functionality by invoking delegates.

#### Methods
##### `RelayCommand`
```csharp
public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
```
Initializes a new instance of the <see cref="RelayCommand"/> class.  <param name="execute">The execution logic delegate.</param> <param name="canExecute">The execution status logic delegate.</param>

##### `CanExecute`
```csharp
public bool CanExecute(object parameter)
```
Determines whether the command can execute in its current state.

**Parameters:**
- `parameter`: Data used by the command.

**Returns:** True if the command can execute, false otherwise.

##### `Execute`
```csharp
public void Execute(object parameter)
```
Executes the command.

**Parameters:**
- `parameter`: Data used by the command.

---

#### Fields
- **`_execute`**: `private readonly Action<object> _execute;`
- **`_canExecute`**: `private readonly Predicate<object> _canExecute;`
- **`_canExecute`**: `return _canExecute == null || _canExecute(parameter);`

---

### Class: `ResolveConflictsViewModel`
**File:** [src/SyntheticShared/ViewModels/ResolveConflictsViewModel.cs](../src/SyntheticShared/ViewModels/ResolveConflictsViewModel.cs)

ViewModel for resolving template conflicts.

#### Properties
- **`Conflicts`**: `public ObservableCollection<ConflictItem> Conflicts { get; }`
  *Description:* Gets the collection of conflict items.
- **`Proceeded`**: `public bool Proceeded { get; private set; } = false;`
  *Description:* Gets a value indicating whether the user proceeded with resolving the conflicts.
- **`ProceedCommand`**: `public ICommand ProceedCommand { get; }`
  *Description:* Gets the proceed command.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the cancel command.
- **`CloseAction`**: `public Action CloseAction { get; set; }`
  *Description:* Gets or sets the close action for the window.

#### Methods
##### `ResolveConflictsViewModel`
```csharp
public ResolveConflictsViewModel(List<ConflictItem> conflicts)
```
Initializes a new instance of the <see cref="ResolveConflictsViewModel"/> class.  <param name="conflicts">The list of naming conflicts to resolve.</param>

##### `CanExecuteProceed`
```csharp
private bool CanExecuteProceed(object obj)
```
##### `ExecuteProceed`
```csharp
private void ExecuteProceed(object obj)
```
##### `ExecuteCancel`
```csharp
private void ExecuteCancel(object obj)
```
---

### Enum: `SearchScope`
**File:** [src/SyntheticShared/ViewModels/FindReplaceViewModel.cs](../src/SyntheticShared/ViewModels/FindReplaceViewModel.cs)

Specifies the scope of a find and replace operation.

### Class: `SearchableField`
**File:** [src/SyntheticShared/ViewModels/FindReplaceViewModel.cs](../src/SyntheticShared/ViewModels/FindReplaceViewModel.cs)

#### Properties
- **`ElementIndex`**: `public int ElementIndex { get; set; }`
- **`FieldIndex`**: `public int FieldIndex { get; set; }`
- **`Element`**: `public ElementTypeWrapperVM Element { get; set; }`
- **`Value`**: `public string Value { get; set; }`

#### Methods
##### `FindNext`
```csharp
public bool FindNext()
```
Searches forward in the dataset for FindText.

##### `FindPrevious`
```csharp
public bool FindPrevious()
```
Searches backward in the dataset for FindText.

##### `Replace`
```csharp
public void Replace()
```
Replaces the current match and finds the next match.

##### `ReplaceAll`
```csharp
public void ReplaceAll()
```
Replaces all occurrences in all editable fields in the dataset.

##### `ReplaceCaseInsensitive`
```csharp
private string ReplaceCaseInsensitive(string input, string search, string replace, out bool replaced)
```
##### `GetFieldValue`
```csharp
private string GetFieldValue(ElementTypeWrapperVM el, int fieldIndex, out bool isEditable)
```
##### `SetFieldValue`
```csharp
private void SetFieldValue(ElementTypeWrapperVM el, int fieldIndex, string value)
```
##### `HighlightMatch`
```csharp
private void HighlightMatch(ElementTypeWrapperVM element)
```
##### `CloseWindow`
```csharp
private void CloseWindow(Window window)
```
---

#### Fields
- **`list`**: `var list = new System.Collections.Generic.List<SearchableField>();`
- **`el`**: `var el = _mainVM.WrappedElements[i];`
- **`param`**: `var param = el.Parameters[p];`
- **`list`**: `return list;`
- **`false`**: `return false;`
- **`fields`**: `var fields = GetSearchableFields();`
- **`false`**: `return false;`
- **`startIndex`**: `int startIndex = fields.FindIndex(f => f.ElementIndex == _currentElementIndex && f.FieldIndex == _currentFieldIndex);`
- **`startChar`**: `int startChar = _currentMatchCharIndex + 1;`
- **`fieldIdx`**: `int fieldIdx = startIndex;`
- **`fieldsSearched`**: `int fieldsSearched = 0;`
- **`field`**: `var field = fields[fieldIdx];`
- **`val`**: `string val = field.Value;`
- **`searchStart`**: `int searchStart = 0;`
- **`matchIdx`**: `int matchIdx = val.IndexOf(FindText, searchStart, StringComparison.OrdinalIgnoreCase);`
- **`true`**: `return true;`
- **`false`**: `return false;`
- **`false`**: `return false;`
- **`fields`**: `var fields = GetSearchableFields();`
- **`false`**: `return false;`
- **`startIndex`**: `int startIndex = fields.FindIndex(f => f.ElementIndex == _currentElementIndex && f.FieldIndex == _currentFieldIndex);`
- **`startChar`**: `int startChar = _currentMatchCharIndex - 1;`
- **`fieldIdx`**: `int fieldIdx = startIndex;`
- **`fieldsSearched`**: `int fieldsSearched = 0;`
- **`field`**: `var field = fields[fieldIdx];`
- **`val`**: `string val = field.Value;`
- **`searchLen`**: `int searchLen;`
- **`matchIdx`**: `int matchIdx = val.LastIndexOf(FindText, searchLen - 1, StringComparison.OrdinalIgnoreCase);`
- **`true`**: `return true;`
- **`false`**: `return false;`
- **`element`**: `var element = _mainVM.WrappedElements[_currentElementIndex];`
- **`val`**: `string val = GetFieldValue(element, _currentFieldIndex, out bool isEditable);`
- **`matchSubstring`**: `string matchSubstring = val.Substring(_currentMatchCharIndex, FindText.Length);`
- **`totalReplaced`**: `int totalReplaced = 0;`
- **`findText`**: `string findText = FindText;`
- **`replaceText`**: `string replaceText = ReplaceText ?? string.Empty;`
- **`name`**: `string name = element.Name;`
- **`newName`**: `string newName = ReplaceCaseInsensitive(name, findText, replaceText, out bool replacedName);`
- **`val`**: `string val = param.Value;`
- **`newVal`**: `string newVal = ReplaceCaseInsensitive(val, findText, replaceText, out bool replacedParam);`
- **`lastPos`**: `int lastPos = 0;`
- **`pos`**: `int pos = input.IndexOf(search, StringComparison.OrdinalIgnoreCase);`
- **`paramIndex`**: `int paramIndex = fieldIndex - 1;`
- **`param`**: `var param = el.Parameters[paramIndex];`
- **`null`**: `return null;`
- **`paramIndex`**: `int paramIndex = fieldIndex - 1;`
- **`param`**: `var param = el.Parameters[paramIndex];`

---

### Class: `SelectSearchPathsViewModel`
**File:** [src/SyntheticShared/ViewModels/SelectSearchPathsViewModel.cs](../src/SyntheticShared/ViewModels/SelectSearchPathsViewModel.cs)

ViewModel for selecting search paths.

#### Properties
- **`CloseAction`**: `public Action<bool> CloseAction { get; set; }`
  *Description:* Gets or sets the action to close the window, passing a boolean result.
- **`OkCommand`**: `public ICommand OkCommand { get; }`
  *Description:* Gets the OK command.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the Cancel command.
- **`AddPathCommand`**: `public ICommand AddPathCommand { get; }`
  *Description:* Gets the Add Path command.
- **`SelectAllUserPathsCommand`**: `public ICommand SelectAllUserPathsCommand { get; }`
  *Description:* Gets the command to select all user paths.
- **`SelectNoneUserPathsCommand`**: `public ICommand SelectNoneUserPathsCommand { get; }`
  *Description:* Gets the command to deselect all user paths.
- **`SelectAllDefaultPathsCommand`**: `public ICommand SelectAllDefaultPathsCommand { get; }`
  *Description:* Gets the command to select all default paths.
- **`SelectNoneDefaultPathsCommand`**: `public ICommand SelectNoneDefaultPathsCommand { get; }`
  *Description:* Gets the command to deselect all default paths.

#### Methods
##### `SelectSearchPathsViewModel`
```csharp
public SelectSearchPathsViewModel()
```
Initializes a new instance of the <see cref="SelectSearchPathsViewModel"/> class.

##### `CheckAllItems`
```csharp
public void CheckAllItems()
```
Checks all user search paths.

##### `CheckAllDefaults`
```csharp
public void CheckAllDefaults()
```
Checks all default search paths.

##### `OnOk`
```csharp
private void OnOk(object parameter)
```
##### `OnCancel`
```csharp
private void OnCancel(object parameter)
```
##### `OnAddPath`
```csharp
private void OnAddPath(object parameter)
```
##### `OnSelectAllUserPaths`
```csharp
private void OnSelectAllUserPaths(object parameter)
```
##### `OnSelectNoneUserPaths`
```csharp
private void OnSelectNoneUserPaths(object parameter)
```
##### `OnSelectAllDefaultPaths`
```csharp
private void OnSelectAllDefaultPaths(object parameter)
```
##### `OnSelectNoneDefaultPaths`
```csharp
private void OnSelectNoneDefaultPaths(object parameter)
```
---

#### Fields
- **`_title`**: `private string _title = "Select Search Paths";`
- **`_instruction`**: `private string _instruction = "Add and select the folder paths that you want to search for files.";`
- **`_userPaths`**: `private ObservableCollection<CheckableItem> _userPaths = new ObservableCollection<CheckableItem>();`
- **`_defaultPaths`**: `private ObservableCollection<CheckableItem> _defaultPaths = new ObservableCollection<CheckableItem>();`
- **`result`**: `var result = new List<string>();`
- **`result`**: `return result;`
- **`ownerHandle`**: `IntPtr ownerHandle = IntPtr.Zero;`
- **`selected`**: `string selected = FileDialogHelper.SelectFolder(ownerHandle, "Select Search Path to Add");`

---

### Class: `SelectedElementItemViewModel`
**File:** [src/SyntheticShared/ViewModels/SelectedElementItemViewModel.cs](../src/SyntheticShared/ViewModels/SelectedElementItemViewModel.cs)

#### Methods
##### `SelectedElementItemViewModel`
```csharp
public SelectedElementItemViewModel(Element element, View activeView)
```
Initializes a new instance of the `SelectedElementItemViewModel` class.

#### Fields
- **`ViewModelBase`**: `public class SelectedElementItemViewModel : ViewModelBase {`
  *Description:* ViewModel representing a single selected element for per-element configuration.
- **`ElementId`**: `public ElementId ElementId`
  *Description:* Gets the Revit Element ID.
- **`DisplayName`**: `public string DisplayName`
  *Description:* Gets the user-friendly display name.
- **`AvailableOrientations`**: `public List<string> AvailableOrientations`
  *Description:* Gets the list of available orientation options.
- **`SelectedOrientation`**: `public string SelectedOrientation {`
  *Description:* Gets or sets the selected orientation for this element.

---
### Class: `SetTemplateViewModel`
**File:** [src/SyntheticShared/ViewModels/SetTemplateViewModel.cs](../src/SyntheticShared/ViewModels/SetTemplateViewModel.cs)

ViewModel controlling the logic for capturing and saving a new TagTemplate.

#### Properties
- **`CategoryName`**: `public string CategoryName { get; }`
  *Description:* Gets the category name of the host element.
- **`FamilyName`**: `public string FamilyName { get; }`
  *Description:* Gets the family name of the host element.
- **`TypeName`**: `public string TypeName { get; }`
  *Description:* Gets the type name of the host element.
- **`IsCategoryScope`**: `public bool IsCategoryScope { get => SelectedScope == TemplateScope.Category; set { if(value) SelectedScope = TemplateScope.Category; } }`
  *Description:* Gets or sets a value indicating whether the scope is set to Category.
- **`IsFamilyScope`**: `public bool IsFamilyScope { get => SelectedScope == TemplateScope.Family; set { if(value) SelectedScope = TemplateScope.Family; } }`
  *Description:* Gets or sets a value indicating whether the scope is set to Family.
- **`IsTypeScope`**: `public bool IsTypeScope { get => SelectedScope == TemplateScope.Type; set { if(value) SelectedScope = TemplateScope.Type; } }`
  *Description:* Gets or sets a value indicating whether the scope is set to Type.
- **`CloseAction`**: `public Action CloseAction { get; set; }`
  *Description:* Delegate assigned by the View to allow the ViewModel to request closure without violating MVVM.

#### Methods
##### `SetTemplateViewModel`
```csharp
public SetTemplateViewModel(Document doc, FamilyInstance host, XYZ localOffset, TagOrientation tagOrientation, TagTemplate editingTemplate = null)
```
Initializes a new instance of the <see cref="SetTemplateViewModel"/> class.  <param name="doc">The active Revit document.</param> <param name="host">The host family instance.</param> <param name="localOffset">The tag offset coordinates.</param> <param name="tagOrientation">The tag orientation.</param> <param name="editingTemplate">Optional existing template being edited.</param>

##### `if`
```csharp
else if (!string.IsNullOrEmpty(_editingTemplate.TargetFamily))
```
##### `CanExecuteSave`
```csharp
private bool CanExecuteSave(object obj) => !string.IsNullOrWhiteSpace(TemplateName);
```
##### `ExecuteSave`
```csharp
private void ExecuteSave(object obj)
```
##### `ExecuteCancel`
```csharp
private void ExecuteCancel(object obj) => CloseAction?.Invoke();
```
---

#### Fields
- **`_doc`**: `private readonly Document _doc;`
- **`_host`**: `private readonly FamilyInstance _host;`
- **`_localOffset`**: `private readonly XYZ _localOffset;`
- **`_tagOrientation`**: `private readonly TagOrientation _tagOrientation;`
- **`_editingTemplate`**: `private readonly TagTemplate _editingTemplate;`
- **`_templateName`**: `private string _templateName;`
- **`_allowOrientationChange`**: `private bool _allowOrientationChange;`
- **`_selectedScope`**: `private TemplateScope _selectedScope = TemplateScope.Type;`
- **`template`**: `var template = _editingTemplate ?? new TagTemplate();`
- **`repo`**: `var repo = new TemplateStorageRepository();`

---

### Class: `SettingsDashboardViewModel`
**File:** [src/SyntheticShared/ViewModels/SettingsDashboardViewModel.cs](../src/SyntheticShared/ViewModels/SettingsDashboardViewModel.cs)

Main ViewModel for the Unified Settings Dashboard.

#### Properties
- **`SettingModules`**: `public ObservableCollection<ISettingModuleViewModel> SettingModules { get; }`
  *Description:* Gets the list of settings modules.
- **`SaveCommand`**: `public ICommand SaveCommand { get; }`
  *Description:* Gets the save command.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the cancel command.

#### Methods
##### `SettingsDashboardViewModel`
```csharp
public SettingsDashboardViewModel(Document doc, IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="SettingsDashboardViewModel"/> class.  <param name="doc">The Revit Document context.</param> <param name="mainWindowHandle">The main window handle.</param>

##### `WorksetSettingsViewModel`
```csharp
new WorksetSettingsViewModel(_doc, _mainWindowHandle),
```
##### `ViewAutoNumSettingsViewModel`
```csharp
new ViewAutoNumSettingsViewModel(_doc, _mainWindowHandle),
```
##### `MaterialLibraryViewModel`
```csharp
new MaterialLibraryViewModel(_doc, _mainWindowHandle),
```
##### `ProjectMaterialsViewModel`
```csharp
new ProjectMaterialsViewModel(_doc, _mainWindowHandle),
```
##### `FileUtilitySettingsViewModel`
```csharp
new FileUtilitySettingsViewModel(_doc, _mainWindowHandle),
```
##### `SyncSettingsViewModel`
```csharp
new SyncSettingsViewModel(_doc, _mainWindowHandle)
```
##### `Module_PropertyChanged`
```csharp
private void Module_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
```
##### `CanSave`
```csharp
private bool CanSave()
```
##### `OnSave`
```csharp
private void OnSave(object parameter)
```
##### `OnCancel`
```csharp
private void OnCancel(object parameter)
```
---

#### Fields
- **`_doc`**: `private readonly Document _doc;`
- **`_mainWindowHandle`**: `private readonly IntPtr _mainWindowHandle;`
- **`_selectedModule`**: `private ISettingModuleViewModel _selectedModule;`
- **`false`**: `return false;`
- **`anyDirty`**: `bool anyDirty = SettingModules.Any(m => m.IsDirty);`
- **`allDirtyValid`**: `bool allDirtyValid = SettingModules.Where(m => m.IsDirty).All(m => m.IsValid);`

---

### Class: `SyncSettingsViewModel`
**File:** [src/SyntheticShared/ViewModels/SyncSettingsViewModel.cs](../src/SyntheticShared/ViewModels/SyncSettingsViewModel.cs)

ViewModel for managing Sync Configuration & Link settings.

#### Properties
- **`LinkFileCommand`**: `public ICommand LinkFileCommand { get; }`
  *Description:* Gets the link file command.
- **`ExportCommand`**: `public ICommand ExportCommand { get; }`
  *Description:* Gets the export settings command.
- **`ImportCommand`**: `public ICommand ImportCommand { get; }`
  *Description:* Gets the import settings command.

#### Methods
##### `SyncSettingsViewModel`
```csharp
public SyncSettingsViewModel(Document doc, IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="SyncSettingsViewModel"/> class.  <param name="doc">The Revit Document.</param> <param name="mainWindowHandle">The parent main window handle.</param>

##### `OnLinkFile`
```csharp
private void OnLinkFile(object parameter)
```
##### `OnExport`
```csharp
private void OnExport(object parameter)
```
##### `OnImport`
```csharp
private void OnImport(object parameter)
```
##### `Save`
```csharp
public void Save()
```
<inheritdoc/>

##### `Reload`
```csharp
public void Reload()
```
<inheritdoc/>

---

#### Fields
- **`_doc`**: `private readonly Document _doc;`
- **`_mainWindowHandle`**: `private readonly IntPtr _mainWindowHandle;`
- **`_settings`**: `private SyncSettings _settings;`
- **`_isOverridden`**: `private bool _isOverridden;`
- **`ModuleName`**: `public string ModuleName => "Sync & Link";`
  *Description:* <inheritdoc/>
- **`_isDirty`**: `private bool _isDirty;`
- **`ConfigureCommand`**: `public ICommand ConfigureCommand => null;`
  *Description:* <inheritdoc/>
- **`storageName`**: `string storageName = "Synthetic_" + SyncSettings.Name;`
- **`storageName`**: `string storageName = "Synthetic_" + SyncSettings.Name;`

---

### Class: `SyncWizardViewModel`
**File:** [src/SyntheticShared/ViewModels/SyncWizardViewModel.cs](../src/SyntheticShared/ViewModels/SyncWizardViewModel.cs)

ViewModel for the Sync Settings Configuration Wizard.

#### Properties
- **`BrowseCommand`**: `public ICommand BrowseCommand { get; }`
  *Description:* Gets the browse command.
- **`OkCommand`**: `public ICommand OkCommand { get; }`
  *Description:* Gets the OK command.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the Cancel command.

#### Methods
##### `SyncWizardViewModel`
```csharp
public SyncWizardViewModel(SyncSettings settings)
```
Initializes a new instance of the <see cref="SyncWizardViewModel"/> class.  <param name="settings">The sync settings to edit.</param>

##### `OnBrowse`
```csharp
private void OnBrowse(object parameter)
```
##### `CanOk`
```csharp
private bool CanOk()
```
##### `OnOk`
```csharp
private void OnOk(object parameter)
```
##### `OnCancel`
```csharp
private void OnCancel(object parameter)
```
---

#### Fields
- **`_linkedFilePath`**: `private string _linkedFilePath;`
- **`IsPathValid`**: `public bool IsPathValid => string.IsNullOrEmpty(LinkedFilePath) || File.Exists(LinkedFilePath);`
  *Description:* Gets a value indicating whether the selected linked path is valid.

---

### Enum: `TemplateScope`
**File:** [src/SyntheticShared/ViewModels/SetTemplateViewModel.cs](../src/SyntheticShared/ViewModels/SetTemplateViewModel.cs)

Specifies the matching scope for a tag template (Category-level, Family-level, or Type-level).

### Class: `ViewAutoNumSettingsViewModel`
**File:** [src/SyntheticShared/ViewModels/ViewAutoNumSettingsViewModel.cs](../src/SyntheticShared/ViewModels/ViewAutoNumSettingsViewModel.cs)

ViewModel for managing View Auto-Numbering configuration settings.

#### Properties
- **`ConfigureCommand`**: `public ICommand ConfigureCommand { get; }`
  *Description:* <inheritdoc/>

#### Methods
##### `ViewAutoNumSettingsViewModel`
```csharp
public ViewAutoNumSettingsViewModel(Document doc, IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="ViewAutoNumSettingsViewModel"/> class.  <param name="doc">The Revit document.</param> <param name="mainWindowHandle">The Revit main window handle.</param>

##### `OnConfigure`
```csharp
private void OnConfigure(object parameter)
```
##### `Save`
```csharp
public void Save()
```
<inheritdoc/>

##### `Reload`
```csharp
public void Reload()
```
<inheritdoc/>

---

#### Fields
- **`_doc`**: `private readonly Document _doc;`
- **`_mainWindowHandle`**: `private readonly IntPtr _mainWindowHandle;`
- **`_settings`**: `private ViewAutoNumSettings _settings;`
- **`_isOverridden`**: `private bool _isOverridden;`
- **`ModuleName`**: `public string ModuleName => "View Auto-Numbering";`
  *Description:* <inheritdoc/>
- **`_isDirty`**: `private bool _isDirty;`
- **`defaultSettings`**: `var defaultSettings = new ViewAutoNumSettings().Defaults();`
- **`defaultPath`**: `string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");`
- **`defaultConfig`**: `Config defaultConfig = Config.ReadFromFile(defaultPath);`
- **`storageName`**: `string storageName = "Synthetic_" + ViewAutoNumSettings.Name;`
- **`wizardVM`**: `var wizardVM = new ViewAutoNumWizardViewModel(_doc, draftSettings);`
- **`dialogResult`**: `var dialogResult = wizardWindow.ShowDialog();`
- **`storageName`**: `string storageName = "Synthetic_" + ViewAutoNumSettings.Name;`

---

### Class: `ViewAutoNumWizardViewModel`
**File:** [src/SyntheticShared/ViewModels/ViewAutoNumWizardViewModel.cs](../src/SyntheticShared/ViewModels/ViewAutoNumWizardViewModel.cs)

ViewModel for the View Autonumbering Configuration Wizard.

#### Properties
- **`AvailableFamilies`**: `public ObservableCollection<string> AvailableFamilies { get; } = new ObservableCollection<string>();`
  *Description:* Gets the collection of available annotation families.
- **`AvailableTypes`**: `public ObservableCollection<string> AvailableTypes { get; } = new ObservableCollection<string>();`
  *Description:* Gets the collection of available types for the selected family.
- **`OkCommand`**: `public ICommand OkCommand { get; }`
  *Description:* Gets the OK command.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the Cancel command.

#### Methods
##### `ViewAutoNumWizardViewModel`
```csharp
public ViewAutoNumWizardViewModel(Document doc, ViewAutoNumSettings settings)
```
Initializes a new instance of the <see cref="ViewAutoNumWizardViewModel"/> class.  <param name="doc">The Revit Document context.</param> <param name="settings">The ViewAutoNum settings to edit.</param>

##### `LoadAvailableFamilies`
```csharp
private void LoadAvailableFamilies(string targetFamily, string targetType)
```
##### `if`
```csharp
else if (AvailableFamilies.Count > 0)
```
##### `LoadAvailableTypes`
```csharp
private void LoadAvailableTypes()
```
##### `CanOk`
```csharp
private bool CanOk()
```
##### `OnOk`
```csharp
private void OnOk(object parameter)
```
##### `OnCancel`
```csharp
private void OnCancel(object parameter)
```
---

#### Fields
- **`_doc`**: `private readonly Document _doc;`
- **`_selectedFamily`**: `private string _selectedFamily;`
- **`_selectedType`**: `private string _selectedType;`
- **`_xGridName`**: `private string _xGridName;`
- **`_yGridName`**: `private string _yGridName;`

---

### Class: `ViewModelBase`
**File:** [src/SyntheticShared/ViewModels/ViewModelBase.cs](../src/SyntheticShared/ViewModels/ViewModelBase.cs)

Abstract base class implementing

#### Methods
##### `OnPropertyChanged`
```csharp
protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
```
Raises the

**Parameters:**
- `propertyName`: The name of the property that changed.

---

#### Fields
- **`true`**: `return true;`

---

### Class: `VisibilityOverrideRowVM`
**File:** [src/SyntheticShared/ViewModels/NestedDataEditorViewModel.cs](../src/SyntheticShared/ViewModels/NestedDataEditorViewModel.cs)

Wrapper for category graphic overrides.

#### Properties
- **`PatternsList`**: `public List<string> PatternsList { get; }`
  *Description:* Gets the list of available patterns.
- **`LineStylesList`**: `public List<string> LineStylesList { get; }`
  *Description:* Gets the list of available line styles.
- **`DetailLevels`**: `public List<string> DetailLevels { get; } = new List<string> { "Undefined", "Coarse", "Medium", "Fine" };`
  *Description:* Gets the list of detail levels.

#### Methods
##### `VisibilityOverrideRowVM`
```csharp
public VisibilityOverrideRowVM(CategoryGraphicOverridesModel overrideModel, List<string> patternsList, List<string> lineStylesList)
```
Initializes a new instance of the <see cref="VisibilityOverrideRowVM"/> class.  <param name="overrideModel">The override settings model.</param> <param name="patternsList">The list of available fill patterns.</param> <param name="lineStylesList">The list of available line styles.</param>

##### `IsModelModified`
```csharp
public bool IsModelModified()
```
Checks if the model has been modified.

**Returns:** True if modified, false otherwise.

##### `GetOverrideModel`
```csharp
public CategoryGraphicOverridesModel GetOverrideModel()
```
Gets the override model.

**Returns:** The category graphic overrides model.

##### `SyncBackToOverride`
```csharp
public void SyncBackToOverride()
```
Synchronizes settings from the VM back to the override model.

---

#### Fields
- **`_override`**: `private readonly CategoryGraphicOverridesModel _override;`
- **`CategoryName`**: `public string CategoryName => _override.Category?.Name ?? "Unknown";`
  *Description:* Gets the name of the category.
- **`ParentCategoryName`**: `public string ParentCategoryName => _override.ParentCategory?.Name;`
  *Description:* Gets the name of the parent category.
- **`GroupName`**: `public string GroupName => string.IsNullOrEmpty(ParentCategoryName) ? CategoryName : ParentCategoryName;`
  *Description:* Gets the group name, which is either the parent category name or the category name if it has no parent.
- **`IsSubcategory`**: `public bool IsSubcategory => !string.IsNullOrEmpty(ParentCategoryName);`
  *Description:* Gets a value indicating whether this category is a subcategory.
- **`_isVisible`**: `private bool _isVisible;`
- **`_halftone`**: `private bool _halftone;`
- **`_transparency`**: `private int _transparency;`
- **`_projectionLineWeight`**: `private int _projectionLineWeight;`
- **`_projectionLineColor`**: `private string _projectionLineColor;`
- **`_selectedProjectionLinePattern`**: `private string _selectedProjectionLinePattern;`
- **`_cutLineWeight`**: `private int _cutLineWeight;`
- **`_cutLineColor`**: `private string _cutLineColor;`
- **`_selectedCutLinePattern`**: `private string _selectedCutLinePattern;`
- **`_detailLevel`**: `private string _detailLevel;`
- **`go`**: `var go = overrideModel.GraphicOverride;`
- **`false`**: `return false;`
- **`_override`**: `return _override;`
- **`go`**: `var go = _override.GraphicOverride;`
- **`hex`**: `var hex = ProjectionLineColor.Substring(1);`
- **`r`**: `byte r = Convert.ToByte(hex.Substring(0, 2), 16);`
- **`g`**: `byte g = Convert.ToByte(hex.Substring(2, 2), 16);`
- **`b`**: `byte b = Convert.ToByte(hex.Substring(4, 2), 16);`
- **`hex`**: `var hex = CutLineColor.Substring(1);`
- **`r`**: `byte r = Convert.ToByte(hex.Substring(0, 2), 16);`
- **`g`**: `byte g = Convert.ToByte(hex.Substring(2, 2), 16);`
- **`b`**: `byte b = Convert.ToByte(hex.Substring(4, 2), 16);`

---

### Class: `WorksetSettingsViewModel`
**File:** [src/SyntheticShared/ViewModels/WorksetSettingsViewModel.cs](../src/SyntheticShared/ViewModels/WorksetSettingsViewModel.cs)

ViewModel for managing Workset configuration settings.

#### Properties
- **`ConfigureCommand`**: `public ICommand ConfigureCommand { get; }`
  *Description:* <inheritdoc/>

#### Methods
##### `WorksetSettingsViewModel`
```csharp
public WorksetSettingsViewModel(Document doc, IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="WorksetSettingsViewModel"/> class.  <param name="doc">The Revit document.</param> <param name="mainWindowHandle">The Revit main window handle.</param>

##### `OnConfigure`
```csharp
private void OnConfigure(object parameter)
```
##### `Save`
```csharp
public void Save()
```
<inheritdoc/>

##### `Reload`
```csharp
public void Reload()
```
<inheritdoc/>

---

#### Fields
- **`_doc`**: `private readonly Document _doc;`
- **`_mainWindowHandle`**: `private readonly IntPtr _mainWindowHandle;`
- **`_settings`**: `private WorksetSettings _settings;`
- **`_isOverridden`**: `private bool _isOverridden;`
- **`ModuleName`**: `public string ModuleName => "Worksets";`
  *Description:* <inheritdoc/>
- **`_isDirty`**: `private bool _isDirty;`
- **`defaultSettings`**: `var defaultSettings = new WorksetSettings().Defaults();`
- **`defaultPath`**: `string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");`
- **`defaultConfig`**: `Config defaultConfig = Config.ReadFromFile(defaultPath);`
- **`storageName`**: `string storageName = "Synthetic_" + WorksetSettings.Name;`
- **`draftSettings`**: `var draftSettings = new WorksetSettings(_settings.WorksetFile, _settings.WorksetPath, _settings.WorksetGroup);`
- **`wizardVM`**: `var wizardVM = new WorksetWizardViewModel(draftSettings);`
- **`dialogResult`**: `var dialogResult = wizardWindow.ShowDialog();`
- **`storageName`**: `string storageName = "Synthetic_" + WorksetSettings.Name;`

---

### Class: `WorksetWizardViewModel`
**File:** [src/SyntheticShared/ViewModels/WorksetWizardViewModel.cs](../src/SyntheticShared/ViewModels/WorksetWizardViewModel.cs)

ViewModel for the Workset Configuration Wizard.

#### Properties
- **`AvailableGroups`**: `public ObservableCollection<string> AvailableGroups { get; } = new ObservableCollection<string>();`
  *Description:* Gets the collection of available workset groups (worksheet names).
- **`BrowseCommand`**: `public ICommand BrowseCommand { get; }`
  *Description:* Gets the browse command.
- **`OkCommand`**: `public ICommand OkCommand { get; }`
  *Description:* Gets the OK command.
- **`CancelCommand`**: `public ICommand CancelCommand { get; }`
  *Description:* Gets the Cancel command.

#### Methods
##### `WorksetWizardViewModel`
```csharp
public WorksetWizardViewModel(WorksetSettings settings)
```
Initializes a new instance of the <see cref="WorksetWizardViewModel"/> class.  <param name="settings">The workset settings to edit.</param>

##### `OnBrowse`
```csharp
private void OnBrowse(object parameter)
```
##### `CanOk`
```csharp
private bool CanOk()
```
##### `OnOk`
```csharp
private void OnOk(object parameter)
```
##### `OnCancel`
```csharp
private void OnCancel(object parameter)
```
##### `TriggerLoadAvailableGroups`
```csharp
private void TriggerLoadAvailableGroups()
```
##### `if`
```csharp
else if (AvailableGroups.Count > 0)
```
---

#### Fields
- **`_worksetFile`**: `private string _worksetFile;`
- **`_worksetPath`**: `private string _worksetPath;`
- **`_selectedGroup`**: `private string _selectedGroup;`
- **`_isLoading`**: `private bool _isLoading;`
- **`dir`**: `string dir = !string.IsNullOrEmpty(WorksetPath) ? WorksetPath : Config.addinPath;`
- **`path`**: `string path = FullPath;`
- **`sheetNames`**: `List<string> sheetNames = null;`
- **`excel`**: `var excel = new Excel(path);`

---

## Namespace: `Synthetic.Views`

### Class: `DropdownSelectionView`
**File:** [src/SyntheticShared/Views/DropdownSelectionView.xaml.cs](../src/SyntheticShared/Views/DropdownSelectionView.xaml.cs)

Interaction logic for DropdownSelectionView.xaml.

#### Methods
##### `DropdownSelectionView`
```csharp
public DropdownSelectionView(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="DropdownSelectionView"/> class.  <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>

---

### Class: `ExportStylesView`
**File:** [src/SyntheticShared/Views/ExportStylesView.xaml.cs](../src/SyntheticShared/Views/ExportStylesView.xaml.cs)

Interaction logic for ExportStylesView.xaml

#### Methods
##### `ExportStylesView`
```csharp
public ExportStylesView(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="ExportStylesView"/> class.  <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>

---

### Class: `FindReplaceWindow`
**File:** [src/SyntheticShared/Views/FindReplaceWindow.xaml.cs](../src/SyntheticShared/Views/FindReplaceWindow.xaml.cs)

Interaction logic for FindReplaceWindow.xaml

#### Methods
##### `FindReplaceWindow`
```csharp
public FindReplaceWindow(JsonEditorMainViewModel mainVM)
```
Initializes a new instance of the <see cref="FindReplaceWindow"/> class.  <param name="mainVM">The main editor view model.</param>

---

### Class: `ImportStylesView`
**File:** [src/SyntheticShared/Views/ImportStylesView.xaml.cs](../src/SyntheticShared/Views/ImportStylesView.xaml.cs)

Interaction logic for ImportStylesView.xaml

#### Methods
##### `ImportStylesView`
```csharp
public ImportStylesView(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="ImportStylesView"/> class.  <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>

---

### Class: `ImportSummaryWindow`
**File:** [src/SyntheticShared/Views/ImportSummaryWindow.xaml.cs](../src/SyntheticShared/Views/ImportSummaryWindow.xaml.cs)

Interaction logic for ImportSummaryWindow.xaml.

#### Methods
##### `ImportSummaryWindow`
```csharp
public ImportSummaryWindow(IntPtr parentMainWindowHandle, object viewModel)
```
Initializes a new instance of the <see cref="ImportSummaryWindow"/> class.  <param name="parentMainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param> <param name="viewModel">The view model associated with this summary window.</param>

##### `CloseButton_Click`
```csharp
private void CloseButton_Click(object sender, RoutedEventArgs e)
```
---

### Class: `JsonEditorWindow`
**File:** [src/SyntheticShared/Views/JsonEditorWindow.xaml.cs](../src/SyntheticShared/Views/JsonEditorWindow.xaml.cs)

Interaction logic for JsonEditorWindow.xaml

#### Methods
##### `JsonEditorWindow`
```csharp
public JsonEditorWindow(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="JsonEditorWindow"/> class. Sets the owner of the window to the Revit main window handle.  <param name="mainWindowHandle">The parent Revit main window handle.</param>

##### `ListBox_SelectionChanged`
```csharp
private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
```
Handles SelectionChanged events of the ListBox to update SelectedElements in the ViewModel.

##### `ExpandAll_Click`
```csharp
private void ExpandAll_Click(object sender, RoutedEventArgs e)
```
##### `CollapseAll_Click`
```csharp
private void CollapseAll_Click(object sender, RoutedEventArgs e)
```
##### `SetAllExpanders`
```csharp
private void SetAllExpanders(bool expand)
```
---

#### Fields
- **`_isSyncingSelection`**: `private bool _isSyncingSelection = false;`
- **`expanders`**: `var expanders = FindVisualChildren<Expander>(ElementsListBox);`
- **`child`**: `DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);`

---

### Class: `ListByCheckboxView`
**File:** [src/SyntheticShared/Views/ListByCheckboxView.xaml.cs](../src/SyntheticShared/Views/ListByCheckboxView.xaml.cs)

Interaction logic for ListByCheckboxView.xaml.

#### Methods
##### `ListByCheckboxView`
```csharp
public ListByCheckboxView(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="ListByCheckboxView"/> class.  <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>

---

### Class: `ManageTemplatesView`
**File:** [src/SyntheticShared/Views/ManageTemplatesView.xaml.cs](../src/SyntheticShared/Views/ManageTemplatesView.xaml.cs)

Interaction logic for ManageTemplatesView.xaml.

#### Methods
##### `ManageTemplatesView`
```csharp
public ManageTemplatesView(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="ManageTemplatesView"/> class.  <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>

---

### Class: `MergeDetailedReviewWindow`
**File:** [src/SyntheticShared/Views/MergeDetailedReviewWindow.xaml.cs](../src/SyntheticShared/Views/MergeDetailedReviewWindow.xaml.cs)

Modeless window that displays side-by-side parameter differences for a selected cluster.

#### Methods
##### `MergeDetailedReviewWindow`
```csharp
public MergeDetailedReviewWindow(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="MergeDetailedReviewWindow"/> class.  <param name="mainWindowHandle">The parent window handle.</param>

##### `BtnCancel_Click`
```csharp
private void BtnCancel_Click(object sender, RoutedEventArgs e)
```
---

### Class: `MergeDuplicatesWindow`
**File:** [src/SyntheticShared/Views/MergeDuplicatesWindow.xaml.cs](../src/SyntheticShared/Views/MergeDuplicatesWindow.xaml.cs)

Interaction logic for MergeDuplicatesWindow.xaml.

#### Methods
##### `MergeDuplicatesWindow`
```csharp
public MergeDuplicatesWindow(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="MergeDuplicatesWindow"/> class.  <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>

---

### Class: `MergeSelectionWindow`
**File:** [src/SyntheticShared/Views/MergeSelectionWindow.xaml.cs](../src/SyntheticShared/Views/MergeSelectionWindow.xaml.cs)

Interaction logic for MergeSelectionWindow.xaml.

#### Methods
##### `MergeSelectionWindow`
```csharp
public MergeSelectionWindow(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="MergeSelectionWindow"/> class.  <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>

---

### Class: `NestedDataEditorWindow`
**File:** [src/SyntheticShared/Views/NestedDataEditorWindow.xaml.cs](../src/SyntheticShared/Views/NestedDataEditorWindow.xaml.cs)

Interaction logic for NestedDataEditorWindow.xaml

#### Methods
##### `NestedDataEditorWindow`
```csharp
public NestedDataEditorWindow(IntPtr ownerHandle, NestedDataEditorViewModel vm)
```
Initializes a new instance of the <see cref="NestedDataEditorWindow"/> class.  <param name="ownerHandle">The handle to the main Revit application window, used to set the owner of this window.</param> <param name="vm">The view model associated with this editor window.</param>

---

### Class: `NetworkPathsWizardWindow`
**File:** [src/SyntheticShared/Views/NetworkPathsWizardWindow.xaml.cs](../src/SyntheticShared/Views/NetworkPathsWizardWindow.xaml.cs)

Interaction logic for NetworkPathsWizardWindow.xaml.

#### Methods
##### `NetworkPathsWizardWindow`
```csharp
public NetworkPathsWizardWindow(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="NetworkPathsWizardWindow"/> class.  <param name="mainWindowHandle">The parent Revit main window handle.</param>

---

### Class: `ProgressWindow`
**File:** [src/SyntheticShared/Views/ProgressWindow.xaml.cs](../src/SyntheticShared/Views/ProgressWindow.xaml.cs)

Interaction logic for ProgressWindow.xaml.

#### Properties
- **`IsCanceled`**: `public bool IsCanceled { get; private set; }`
  *Description:* Gets a value indicating whether the operation was canceled by the user.

#### Methods
##### `ProgressWindow`
```csharp
public ProgressWindow(IntPtr parentMainWindowHandle)
```
Initializes a new instance of the <see cref="ProgressWindow"/> class.  <param name="parentMainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>

##### `CancelButton_Click`
```csharp
private void CancelButton_Click(object sender, RoutedEventArgs e)
```
##### `AllowUIToUpdate`
```csharp
private void AllowUIToUpdate()
```
---

#### Fields
- **`dispatcher`**: `var dispatcher = System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;`

---

### Class: `ResolveConflictsView`
**File:** [src/SyntheticShared/Views/ResolveConflictsView.xaml.cs](../src/SyntheticShared/Views/ResolveConflictsView.xaml.cs)

Interaction logic for ResolveConflictsView.xaml

#### Methods
##### `ResolveConflictsView`
```csharp
public ResolveConflictsView(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="ResolveConflictsView"/> class.  <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>

---

### Class: `SelectSearchPathsView`
**File:** [src/SyntheticShared/Views/SelectSearchPathsView.xaml.cs](../src/SyntheticShared/Views/SelectSearchPathsView.xaml.cs)

Interaction logic for SelectSearchPathsView.xaml.

#### Methods
##### `SelectSearchPathsView`
```csharp
public SelectSearchPathsView(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="SelectSearchPathsView"/> class.  <param name="mainWindowHandle">The handle to the main Revit application window, used to set the owner of this window.</param>

---

### Class: `SetTemplateView`
**File:** [src/SyntheticShared/Views/SetTemplateView.xaml.cs](../src/SyntheticShared/Views/SetTemplateView.xaml.cs)

Interaction logic for SetTemplateView.xaml

#### Methods
##### `SetTemplateView`
```csharp
public SetTemplateView(IntPtr mainWindowHandle)
```
Initializes a new instance of the SetTemplateView class.  <param name="mainWindowHandle">The parent window handle.</param>

---

### Class: `SettingsDashboardWindow`
**File:** [src/SyntheticShared/Views/SettingsDashboardWindow.xaml.cs](../src/SyntheticShared/Views/SettingsDashboardWindow.xaml.cs)

Interaction logic for SettingsDashboardWindow.xaml.

#### Methods
##### `SettingsDashboardWindow`
```csharp
public SettingsDashboardWindow(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="SettingsDashboardWindow"/> class.  <param name="mainWindowHandle">The parent Revit main window handle.</param>

---

### Class: `SyncResolutionWindow`
**File:** [src/SyntheticShared/Views/SyncResolutionWindow.xaml.cs](../src/SyntheticShared/Views/SyncResolutionWindow.xaml.cs)

Interaction logic for SyncResolutionWindow.xaml.

#### Methods
##### `SyncResolutionWindow`
```csharp
public SyncResolutionWindow(IntPtr mainWindowHandle, Document doc, string filePath, Handlers.SyncExternalEventHandler handler, ExternalEvent externalEvent)
```
Initializes a new instance of the <see cref="SyncResolutionWindow"/> class.  <param name="mainWindowHandle">The parent Revit main window handle.</param> <param name="doc">The active Revit Document.</param> <param name="filePath">The path to the linked settings configuration JSON file.</param> <param name="handler">The external event handler for settings sync.</param> <param name="externalEvent">The external event associated with the handler.</param>

##### `OnPullClick`
```csharp
private void OnPullClick(object sender, RoutedEventArgs e)
```
##### `OnPushClick`
```csharp
private void OnPushClick(object sender, RoutedEventArgs e)
```
##### `OnIgnoreClick`
```csharp
private void OnIgnoreClick(object sender, RoutedEventArgs e)
```
---

#### Fields
- **`_doc`**: `private readonly Document _doc;`
- **`_filePath`**: `private readonly string _filePath;`
- **`_externalEvent`**: `private readonly ExternalEvent _externalEvent;`

---

### Class: `SyncSettingsView`
**File:** [src/SyntheticShared/Views/SyncSettingsView.xaml.cs](../src/SyntheticShared/Views/SyncSettingsView.xaml.cs)

Interaction logic for SyncSettingsView.xaml.

#### Methods
##### `SyncSettingsView`
```csharp
public SyncSettingsView()
```
Initializes a new instance of the <see cref="SyncSettingsView"/> class.

---

### Class: `SyncToastNotification`
**File:** [src/SyntheticShared/Views/SyncToastNotification.xaml.cs](../src/SyntheticShared/Views/SyncToastNotification.xaml.cs)

Interaction logic for SyncToastNotification.xaml.

#### Methods
##### `SyncToastNotification`
```csharp
public SyncToastNotification(IntPtr mainWindowHandle, Document doc, string filePath, Handlers.SyncExternalEventHandler handler, ExternalEvent externalEvent)
```
Initializes a new instance of the <see cref="SyncToastNotification"/> class.  <param name="mainWindowHandle">The parent Revit main window handle.</param> <param name="doc">The active Revit Document.</param> <param name="filePath">The path to the linked settings configuration JSON file.</param> <param name="handler">The external event handler for settings sync.</param> <param name="externalEvent">The external event associated with the handler.</param>

##### `OnSourceInitialized`
```csharp
protected override void OnSourceInitialized(EventArgs e)
```
<inheritdoc/>

##### `OnResolveClick`
```csharp
private void OnResolveClick(object sender, RoutedEventArgs e)
```
##### `OnCloseClick`
```csharp
private void OnCloseClick(object sender, RoutedEventArgs e)
```
---

#### Fields
- **`_mainWindowHandle`**: `private readonly IntPtr _mainWindowHandle;`
- **`_doc`**: `private readonly Document _doc;`
- **`_filePath`**: `private readonly string _filePath;`
- **`_externalEvent`**: `private readonly ExternalEvent _externalEvent;`
- **`workArea`**: `var workArea = SystemParameters.WorkArea;`
- **`resolutionWindow`**: `var resolutionWindow = new SyncResolutionWindow(_mainWindowHandle, _doc, _filePath, _handler, _externalEvent);`

---

### Class: `SyncWizardWindow`
**File:** [src/SyntheticShared/Views/SyncWizardWindow.xaml.cs](../src/SyntheticShared/Views/SyncWizardWindow.xaml.cs)

Interaction logic for SyncWizardWindow.xaml.

#### Methods
##### `SyncWizardWindow`
```csharp
public SyncWizardWindow(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="SyncWizardWindow"/> class.  <param name="mainWindowHandle">The parent Revit main window handle.</param>

---

### Class: `ViewAutoNumWizardWindow`
**File:** [src/SyntheticShared/Views/ViewAutoNumWizardWindow.xaml.cs](../src/SyntheticShared/Views/ViewAutoNumWizardWindow.xaml.cs)

Interaction logic for ViewAutoNumWizardWindow.xaml.

#### Methods
##### `ViewAutoNumWizardWindow`
```csharp
public ViewAutoNumWizardWindow(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="ViewAutoNumWizardWindow"/> class.  <param name="mainWindowHandle">The parent Revit main window handle.</param>

---

### Class: `WorksetWizardWindow`
**File:** [src/SyntheticShared/Views/WorksetWizardWindow.xaml.cs](../src/SyntheticShared/Views/WorksetWizardWindow.xaml.cs)

Interaction logic for WorksetWizardWindow.xaml.

#### Methods
##### `WorksetWizardWindow`
```csharp
public WorksetWizardWindow(IntPtr mainWindowHandle)
```
Initializes a new instance of the <see cref="WorksetWizardWindow"/> class.  <param name="mainWindowHandle">The parent Revit main window handle.</param>

---
