using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using View = Autodesk.Revit.DB.View;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

using Synthetic.Infrastructure.Diagnostics;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.DetailItemFactory.Models;

namespace Synthetic.Modules.DetailItemFactory.Handlers
{
    /// <summary>
    /// External event handler to process selected elements into 2D Detail Items asynchronously on the Revit main thread.
    /// </summary>
    public class DetailItemFactoryEventHandler : IExternalEventHandler
    {
        /// <summary>
        /// Gets or sets the collection of configured elements to process.
        /// </summary>
        public List<SelectedElementItemViewModel> ConfiguredElements { get; set; } = new List<SelectedElementItemViewModel>();

        /// <summary>
        /// Gets or sets the target output folder path.
        /// </summary>
        public string OutputFolder { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target subcategory name.
        /// </summary>
        public string TargetSubcategory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether existing family files should be overwritten on disk.
        /// </summary>
        public bool OverwriteExisting { get; set; }

        /// <summary>
        /// Executes the batch processing loop asynchronously on the Revit main thread.
        /// </summary>
        /// <param name="app">The Revit UIApplication context.</param>
        public void Execute(UIApplication app)
        {
            if (app == null) return;
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            Document doc = uidoc.Document;
            View activeView = doc.ActiveView;

            if (string.IsNullOrWhiteSpace(TargetSubcategory))
            {
                TargetSubcategory = "Detail Items";
            }

            // Resolve family template path dynamically
            string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string? assemblyDir = Path.GetDirectoryName(assemblyPath);
            string templatePath = Path.Combine(assemblyDir ?? string.Empty, "Assets", "Templates", "Detail Item.rft");

            if (!File.Exists(templatePath))
            {
                ProgressCoordinator.Close();
                TaskDialog.Show("Detail Item Factory", $"Template file not found at:\n{templatePath}");
                return;
            }

            // Results collection for the dashboard UI
            List<DetailItemResultItem> resultItems = new List<DetailItemResultItem>();

            // Initialize progress UI
            int totalCount = ConfiguredElements.Count;
            ProgressCoordinator.UpdateStatus("Starting batch processing...");

            // Extract project-level OST_DetailComponents subcategory settings
            Category detailComponentsCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
            Category? projectSubcategory = null;
            Autodesk.Revit.DB.Color projectLineColor = new Autodesk.Revit.DB.Color(0, 0, 0);
            int projectLineWeight = 1;
            ElementId projectLinePatternId = ElementId.InvalidElementId;
            string? projectLinePatternName = null;

            if (detailComponentsCategory != null && detailComponentsCategory.SubCategories.Contains(TargetSubcategory))
            {
                projectSubcategory = detailComponentsCategory.SubCategories.get_Item(TargetSubcategory);
                if (projectSubcategory != null)
                {
                    projectLineColor = projectSubcategory.LineColor;
                    int? weight = projectSubcategory.GetLineWeight(GraphicsStyleType.Projection);
                    if (weight.HasValue)
                    {
                        projectLineWeight = weight.Value;
                    }
                    projectLinePatternId = projectSubcategory.GetLinePatternId(GraphicsStyleType.Projection);
                    if (projectLinePatternId != ElementId.InvalidElementId)
                    {
                        if (doc.GetElement(projectLinePatternId) is LinePatternElement patternElem)
                        {
                            projectLinePatternName = patternElem.Name;
                        }
                    }
                }
            }

            // DWG Export Setup
            DWGExportOptions dwgOptions = new DWGExportOptions
            {
                HideScopeBox = true,
                HideReferencePlane = true,
                HideUnreferenceViewTags = true
            };

            // Loop selected elements
            for (int i = 0; i < totalCount; i++)
            {
                // Check for user cancellation
                if (ProgressCoordinator.IsCancelled())
                {
                    for (int j = i; j < totalCount; j++)
                    {
                        SelectedElementItemViewModel cancelledItem = ConfiguredElements[j];
                        ElementId cancelledId = cancelledItem.ElementId;
                        Element cancelledElement = doc.GetElement(cancelledId);
                        string name = cancelledItem.DisplayName ?? cancelledElement?.Name ?? $"ID: {cancelledId}";
                        resultItems.Add(new DetailItemResultItem
                        {
                            ElementName = name,
                            ElementId = cancelledId.ToString(),
                            Status = "Skipped",
                            Message = "Cancelled by user."
                        });
                    }
                    break;
                }

                SelectedElementItemViewModel configItem = ConfiguredElements[i];
                ElementId selectedId = configItem.ElementId;
                string viewOrientation = configItem.SelectedOrientation;
                Element element = doc.GetElement(selectedId);
                if (element == null) continue;

                int skippedCurvesCount = 0;

                string cleanFamilyName = "Unknown";
                string cleanTypeName = "Unknown";

                try
                {
                    // Update progress window
                    string statusMsg = $"Processing element {i + 1} of {totalCount}: {element.Name ?? "ID: " + element.Id}...";
                    ProgressCoordinator.UpdateStatus(statusMsg);

                    // Filter out annotations & non-model categories
                    if (element.Category == null || element.Category.CategoryType != CategoryType.Model)
                    {
                        string elemName = element.Name ?? $"ID: {element.Id}";
                        resultItems.Add(new DetailItemResultItem
                        {
                            ElementName = elemName,
                            ElementId = element.Id.ToString(),
                            Status = "Skipped",
                            Message = "Annotation or non-Model element skipped."
                        });
                        continue;
                    }

                    // Resolve names
                    string familyName = "";
                    string typeName = "";

                    if (element is FamilyInstance fi)
                    {
                        familyName = fi.Symbol.Family.Name;
                        typeName = fi.Symbol.Name;
                    }
                    else
                    {
                        ElementId typeId = element.GetTypeId();
                        if (typeId != ElementId.InvalidElementId && doc.GetElement(typeId) is ElementType et)
                        {
                            familyName = et.FamilyName;
                            typeName = et.Name;
                        }
                        else
                        {
                            familyName = element.Category?.Name ?? "UnknownCategory";
                            typeName = element.Name ?? "UnknownType";
                        }
                    }

                    cleanFamilyName = MakeValidFileName(familyName);
                    cleanTypeName = MakeValidFileName(typeName);
                    string fileName = $"2D - {cleanFamilyName} - {cleanTypeName} - {viewOrientation}.rfa";
                    string targetPath = Path.Combine(OutputFolder, fileName);

                    // Overwrite check
                    if (File.Exists(targetPath) && !OverwriteExisting)
                    {
                        resultItems.Add(new DetailItemResultItem
                        {
                            ElementName = $"{cleanFamilyName} - {cleanTypeName}",
                            ElementId = element.Id.ToString(),
                            Status = "Skipped",
                            Message = "Family file already exists and overwrite flag is disabled."
                        });
                        continue;
                    }

                    // Calculate Desired Origin (Project Side)
                    XYZ viewRelativeOffset = XYZ.Zero;
                    BoundingBoxXYZ bbox = element.get_BoundingBox(activeView);
                    if (bbox != null)
                    {
                        XYZ center = (bbox.Max + bbox.Min) / 2.0;
                        XYZ desiredOrigin = bbox.Min;
                        if (element.Location is LocationPoint locPoint)
                        {
                            desiredOrigin = locPoint.Point;
                        }

                        XYZ projectOffsetVector = desiredOrigin - center;
                        double offsetX = projectOffsetVector.DotProduct(activeView.RightDirection);
                        double offsetY = projectOffsetVector.DotProduct(activeView.UpDirection);
                        viewRelativeOffset = new XYZ(offsetX, offsetY, 0);
                    }

                    string dwgName = "temp_export_" + Path.GetFileNameWithoutExtension(fileName);

                    // 1. Temporary hide/isolate for export
                    EventHandler<Autodesk.Revit.UI.Events.DialogBoxShowingEventArgs> dialogHandler = 
                        (sender, args) =>
                        {
                            if (args is Autodesk.Revit.UI.Events.TaskDialogShowingEventArgs taskArgs)
                            {
                                if (taskArgs.DialogId == "TaskDialog_Really_Print_Or_Export_Temp_View_Modes")
                                {
                                    taskArgs.OverrideResult(1002);
                                }
                            }
                        };

                    app.DialogBoxShowing += dialogHandler;

                    try
                    {
                        using (Transaction t = new Transaction(doc, "Temporary Isolate for Export"))
                        {
                            t.Start();
                            activeView.CropBoxVisible = false;

                            List<ElementId> idsToIsolate = new List<ElementId> { element.Id };
                            if (element is FamilyInstance familyInstance)
                            {
                                ICollection<ElementId> subComponentIds = familyInstance.GetSubComponentIds();
                                if (subComponentIds != null && subComponentIds.Count > 0)
                                {
                                    idsToIsolate.AddRange(subComponentIds);
                                }
                            }

                            activeView.IsolateElementsTemporary(idsToIsolate);
                            doc.Regenerate();

                            doc.Export(OutputFolder, dwgName, new List<ElementId> { activeView.Id }, dwgOptions);

                            t.RollBack();
                        }
                    }
                    finally
                    {
                        app.DialogBoxShowing -= dialogHandler;
                    }

                    string[] matchingFiles = Directory.GetFiles(OutputFolder, dwgName + "*.dwg");
                    if (matchingFiles.Length == 0)
                    {
                        throw new Exception("DWG Export failed to generate a file on disk.");
                    }
                    string dwgPath = matchingFiles[0];

                    // 2. Create Family Document
                    Document familyDoc = doc.Application.NewFamilyDocument(templatePath);
                    if (familyDoc == null)
                    {
                        throw new Exception("Failed to generate a new family document from the template.");
                    }

                    // 3. Subcategory Serialization
                    GraphicsStyle? subcategoryStyle = null;

                    using (Transaction t = new Transaction(familyDoc, "Serialize Subcategory"))
                    {
                        t.Start();

                        Category familyDetailComponentsCategory = familyDoc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
                        Category? familySubcategory = null;

                        if (familyDetailComponentsCategory.SubCategories.Contains(TargetSubcategory))
                        {
                            familySubcategory = familyDetailComponentsCategory.SubCategories.get_Item(TargetSubcategory);
                        }
                        else
                        {
                            familySubcategory = familyDoc.Settings.Categories.NewSubcategory(familyDetailComponentsCategory, TargetSubcategory);
                        }

                        if (familySubcategory != null)
                        {
                            familySubcategory.LineColor = projectLineColor;
                            familySubcategory.SetLineWeight(projectLineWeight, GraphicsStyleType.Projection);

                            if (projectLinePatternName != null)
                            {
                                LinePatternElement? familyPattern = new FilteredElementCollector(familyDoc)
                                    .OfClass(typeof(LinePatternElement))
                                    .Cast<LinePatternElement>()
                                    .FirstOrDefault(p => p.Name == projectLinePatternName);

                                if (familyPattern == null)
                                {
                                    if (doc.GetElement(projectLinePatternId) is LinePatternElement patternElem)
                                    {
                                        try
                                        {
                                            familyPattern = LinePatternElement.Create(familyDoc, patternElem.GetLinePattern());
                                        }
                                        catch { }
                                    }
                                }

                                if (familyPattern != null)
                                {
                                    familySubcategory.SetLinePatternId(familyPattern.Id, GraphicsStyleType.Projection);
                                }
                            }

                            subcategoryStyle = familySubcategory.GetGraphicsStyle(GraphicsStyleType.Projection);
                        }

                        t.Commit();
                    }

                    // 4. Import & Trace DWG
                    using (Transaction t = new Transaction(familyDoc, "Import and Trace DWG"))
                    {
                        t.Start();

                        View? planView = familyDoc.ActiveView;
                        if (planView == null)
                        {
                            planView = new FilteredElementCollector(familyDoc)
                                .OfClass(typeof(View))
                                .Cast<View>()
                                .FirstOrDefault(v => v.ViewType == ViewType.FloorPlan || v.ViewType == ViewType.EngineeringPlan);
                        }

                        if (planView == null)
                        {
                            throw new Exception("Could not find a valid Floor Plan view inside the Family template.");
                        }

                        DWGImportOptions importOptions = new DWGImportOptions { ThisViewOnly = true };
                        ElementId importedElementId;
                        bool importSuccess = familyDoc.Import(dwgPath, importOptions, planView, out importedElementId);

                        if (!importSuccess || importedElementId == ElementId.InvalidElementId)
                        {
                            throw new Exception("Failed to import the temporary DWG payload into the Family Document.");
                        }

                        ImportInstance? importInstance = familyDoc.GetElement(importedElementId) as ImportInstance;
                        if (importInstance != null)
                        {
                            // Calibrate import origin to align with original project placement
                            BoundingBoxXYZ importBBox = importInstance.get_BoundingBox(familyDoc.ActiveView);
                            if (importBBox != null)
                            {
                                XYZ importCenter = (importBBox.Max + importBBox.Min) / 2.0;
                                XYZ currentDesiredOrigin = importCenter + viewRelativeOffset;
                                XYZ moveVector = XYZ.Zero - currentDesiredOrigin;
                                moveVector = new XYZ(moveVector.X, moveVector.Y, 0);

                                importInstance.Pinned = false;
                                ElementTransformUtils.MoveElement(familyDoc, importInstance.Id, moveVector);
                            }

                            Options geomOptions = new Options { ComputeReferences = true };
                            GeometryElement geomElement = importInstance.get_Geometry(geomOptions);

                            double shortCurveTolerance = familyDoc.Application.ShortCurveTolerance;

                            ProcessGeometry(geomElement, familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref skippedCurvesCount);

                            importInstance.Pinned = false;
                            familyDoc.Delete(importInstance.Id);
                        }

                        t.Commit();
                    }

                    // 5. Save and Load
                    SaveAsOptions saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                    familyDoc.SaveAs(targetPath, saveOptions);

                    Family? loadedFamily = null;
                    using (Transaction projectTx = new Transaction(doc, "Load Family: " + cleanFamilyName))
                    {
                        projectTx.Start();
                        doc.LoadFamily(targetPath, new SimpleFamilyLoadOptions(), out loadedFamily);
                        projectTx.Commit();
                    }

                    familyDoc.Close(false);

                    if (skippedCurvesCount == 0)
                    {
                        resultItems.Add(new DetailItemResultItem
                        {
                            ElementName = $"{cleanFamilyName} - {cleanTypeName}",
                            ElementId = element.Id.ToString(),
                            Status = "Success",
                            Message = "Successfully generated and loaded family."
                        });
                    }
                    else
                    {
                        resultItems.Add(new DetailItemResultItem
                        {
                            ElementName = $"{cleanFamilyName} - {cleanTypeName}",
                            ElementId = element.Id.ToString(),
                            Status = "Partial Success",
                            Message = $"Successfully generated, but skipped {skippedCurvesCount} microscopic or invalid curve{(skippedCurvesCount == 1 ? "" : "s")} during tracing."
                        });
                    }
                    // Last line of loop
                    ProgressCoordinator.UpdateProgress(element.Name ?? "ID: " + element.Id);
                }
                catch (Exception ex)
                {
                    resultItems.Add(new DetailItemResultItem
                    {
                        ElementName = $"{cleanFamilyName} - {cleanTypeName}",
                        ElementId = element.Id.ToString(),
                        Status = "Failed",
                        Message = ex.Message
                    });
                }
            }

            // Close the progress window
            ProgressCoordinator.UpdateStatus("Complete!");
            ProgressCoordinator.Close();

            // Initialize and show the results dashboard modally
            DetailItemFactoryResultsViewModel resultsVm = new DetailItemFactoryResultsViewModel(OutputFolder, resultItems);
            DetailItemFactoryResultsView resultsView = new DetailItemFactoryResultsView(app.MainWindowHandle) { DataContext = resultsVm };
            resultsView.ShowDialog();
        }

        private void ProcessGeometry(
            GeometryElement geomElement, 
            Document familyDoc, 
            View planView, 
            double shortCurveTolerance,
            GraphicsStyle? subcategoryStyle,
            ref int skippedCurvesCount)
        {
            if (geomElement == null) return;

            foreach (GeometryObject geomObj in geomElement)
            {
                if (geomObj is GeometryInstance geomInst)
                {
                    ProcessGeometry(geomInst.GetInstanceGeometry(), familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref skippedCurvesCount);
                }
                else if (geomObj is Curve curve)
                {
                    try
                    {
                        if (!curve.IsBound)
                        {

                            if (curve is Arc arc)
                            {
                                try
                                {
                                    XYZ center = arc.Center;
                                    double radius = arc.Radius;
                                    XYZ xAxis = arc.XDirection.Normalize();
                                    XYZ yAxis = arc.YDirection.Normalize();

                                    Arc half1 = Arc.Create(center, radius, 0.0, Math.PI, xAxis, yAxis);
                                    Arc half2 = Arc.Create(center, radius, Math.PI, 2.0 * Math.PI, xAxis, yAxis);

                                    Curve? flat1 = FlattenCurve(half1);
                                    Curve? flat2 = FlattenCurve(half2);

                                    if (flat1 != null)
                                    {
                                        DetailCurve dc1 = familyDoc.FamilyCreate.NewDetailCurve(planView, flat1);
                                        if (dc1 != null && subcategoryStyle != null) dc1.LineStyle = subcategoryStyle;
                                    }
                                    if (flat2 != null)
                                    {
                                        DetailCurve dc2 = familyDoc.FamilyCreate.NewDetailCurve(planView, flat2);
                                        if (dc2 != null && subcategoryStyle != null) dc2.LineStyle = subcategoryStyle;
                                    }
                                }
                                catch (Exception)
                                {
                                    skippedCurvesCount++;
                                }
                                continue;
                            }
                            else if (curve is Ellipse ellipse)
                            {
                                try
                                {
                                    XYZ center = ellipse.Center;
                                    double rx = ellipse.RadiusX;
                                    double ry = ellipse.RadiusY;
                                    XYZ xAxis = ellipse.XDirection.Normalize();
                                    XYZ yAxis = ellipse.YDirection.Normalize();

                                    Curve half1 = Ellipse.CreateCurve(center, rx, ry, xAxis, yAxis, 0.0, Math.PI);
                                    Curve half2 = Ellipse.CreateCurve(center, rx, ry, xAxis, yAxis, Math.PI, 2.0 * Math.PI);

                                    Curve? flat1 = FlattenCurve(half1);
                                    Curve? flat2 = FlattenCurve(half2);

                                    if (flat1 != null)
                                    {
                                        DetailCurve dc1 = familyDoc.FamilyCreate.NewDetailCurve(planView, flat1);
                                        if (dc1 != null && subcategoryStyle != null) dc1.LineStyle = subcategoryStyle;
                                    }
                                    if (flat2 != null)
                                    {
                                        DetailCurve dc2 = familyDoc.FamilyCreate.NewDetailCurve(planView, flat2);
                                        if (dc2 != null && subcategoryStyle != null) dc2.LineStyle = subcategoryStyle;
                                    }
                                }
                                catch (Exception)
                                {
                                    skippedCurvesCount++;
                                }
                                continue;
                            }

                            skippedCurvesCount++;
                            continue;
                        }

                        if (curve.Length < shortCurveTolerance)
                        {
                            DrawHealedCurve(curve, familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref skippedCurvesCount);
                            continue;
                        }

                        if (curve is NurbSpline || curve is HermiteSpline)
                        {
                            DrawHealedCurve(curve, familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref skippedCurvesCount);
                            continue;
                        }

                        Curve? flatCurve = FlattenCurve(curve);
                        if (flatCurve == null)
                        {
                            skippedCurvesCount++;
                            continue;
                        }

                        if (flatCurve.Length < shortCurveTolerance)
                        {
                            DrawHealedCurve(flatCurve, familyDoc, planView, shortCurveTolerance, subcategoryStyle, ref skippedCurvesCount);
                            continue;
                        }

                        DetailCurve detailCurve = familyDoc.FamilyCreate.NewDetailCurve(planView, flatCurve);
                        if (detailCurve != null && subcategoryStyle != null)
                        {
                            detailCurve.LineStyle = subcategoryStyle;
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        skippedCurvesCount++;
                    }
                    catch (Exception)
                    {
                        skippedCurvesCount++;
                    }
                }
                else if (geomObj is PolyLine polyLine)
                {
                    IList<XYZ> coords = polyLine.GetCoordinates();
                    for (int i = 0; i < coords.Count - 1; i++)
                    {
                        XYZ p1Flat = new XYZ(coords[i].X, coords[i].Y, 0.0);
                        XYZ p2Flat = new XYZ(coords[i+1].X, coords[i+1].Y, 0.0);

                        double segmentLength = p1Flat.DistanceTo(p2Flat);
                        if (segmentLength < shortCurveTolerance)
                        {
                            skippedCurvesCount++;
                            continue;
                        }

                        try
                        {
                            Line segmentLine = Line.CreateBound(p1Flat, p2Flat);
                            DetailCurve detailCurve = familyDoc.FamilyCreate.NewDetailCurve(planView, segmentLine);
                            if (detailCurve != null && subcategoryStyle != null)
                            {
                                detailCurve.LineStyle = subcategoryStyle;
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentException)
                        {
                            skippedCurvesCount++;
                        }
                        catch (Exception)
                        {
                            skippedCurvesCount++;
                        }
                    }
                }
            }
        }

        private void DrawHealedCurve(
            Curve curve, 
            Document familyDoc, 
            View planView, 
            double shortCurveTolerance, 
            GraphicsStyle? subcategoryStyle, 
            ref int skippedCurvesCount)
        {
            try
            {
                IList<XYZ> points = curve.Tessellate();
                if (points == null || points.Count < 2)
                {
                    skippedCurvesCount++;
                    return;
                }

                List<XYZ> simplifiedPoints = new List<XYZ>();
                simplifiedPoints.Add(points[0]);

                double threshold = shortCurveTolerance + 0.003;

                for (int i = 1; i < points.Count - 1; i++)
                {
                    XYZ current = points[i];
                    XYZ lastAdded = simplifiedPoints[simplifiedPoints.Count - 1];

                    XYZ currentFlat = new XYZ(current.X, current.Y, 0.0);
                    XYZ lastAddedFlat = new XYZ(lastAdded.X, lastAdded.Y, 0.0);

                    if (currentFlat.DistanceTo(lastAddedFlat) > threshold)
                    {
                        simplifiedPoints.Add(current);
                    }
                }

                XYZ lastPoint = points[points.Count - 1];
                XYZ lastPointFlat = new XYZ(lastPoint.X, lastPoint.Y, 0.0);
                XYZ currentLastAddedFlat = new XYZ(simplifiedPoints[simplifiedPoints.Count - 1].X, simplifiedPoints[simplifiedPoints.Count - 1].Y, 0.0);
                
                if (simplifiedPoints.Count == 1 || lastPointFlat.DistanceTo(currentLastAddedFlat) > 0.0001)
                {
                    simplifiedPoints.Add(lastPoint);
                }

                for (int i = 0; i < simplifiedPoints.Count - 1; i++)
                {
                    XYZ p0 = simplifiedPoints[i];
                    XYZ p1 = simplifiedPoints[i+1];

                    XYZ p0Flat = new XYZ(p0.X, p0.Y, 0.0);
                    XYZ p1Flat = new XYZ(p1.X, p1.Y, 0.0);

                    if (p0Flat.DistanceTo(p1Flat) < shortCurveTolerance)
                    {
                        skippedCurvesCount++;
                        continue;
                    }

                    try
                    {
                        Line segmentLine = Line.CreateBound(p0Flat, p1Flat);
                        DetailCurve detailCurve = familyDoc.FamilyCreate.NewDetailCurve(planView, segmentLine);
                        if (detailCurve != null && subcategoryStyle != null)
                        {
                            detailCurve.LineStyle = subcategoryStyle;
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        skippedCurvesCount++;
                    }
                    catch (Exception)
                    {
                        skippedCurvesCount++;
                    }
                }
            }
            catch (Exception)
            {
                skippedCurvesCount++;
            }
        }

        private Curve? FlattenCurve(Curve curve)
        {
            if (curve is Line line)
            {
                XYZ p0 = line.GetEndPoint(0);
                XYZ p1 = line.GetEndPoint(1);
                XYZ p0Flat = new XYZ(p0.X, p0.Y, 0);
                XYZ p1Flat = new XYZ(p1.X, p1.Y, 0);
                if (p0Flat.DistanceTo(p1Flat) > 0.0001)
                {
                    return Line.CreateBound(p0Flat, p1Flat);
                }
            }
            else if (curve is Arc arc)
            {
                XYZ p0 = arc.GetEndPoint(0);
                XYZ p1 = arc.Evaluate(0.5, true);
                XYZ p2 = arc.GetEndPoint(1);
                XYZ p0Flat = new XYZ(p0.X, p0.Y, 0);
                XYZ p1Flat = new XYZ(p1.X, p1.Y, 0);
                XYZ p2Flat = new XYZ(p2.X, p2.Y, 0);

                XYZ v1 = (p1Flat - p0Flat).Normalize();
                XYZ v2 = (p2Flat - p1Flat).Normalize();
                if (Math.Abs(v1.DotProduct(v2)) < 0.9999)
                {
                    return Arc.Create(p0Flat, p2Flat, p1Flat);
                }
                else
                {
                    if (p0Flat.DistanceTo(p2Flat) > 0.0001)
                    {
                        return Line.CreateBound(p0Flat, p2Flat);
                    }
                }
            }
            else if (curve is Ellipse ellipse)
            {
                try
                {
                    XYZ center = ellipse.Center;
                    XYZ centerFlat = new XYZ(center.X, center.Y, 0.0);
                    XYZ xDir = ellipse.XDirection;
                    XYZ yDir = ellipse.YDirection;
                    XYZ xDirFlat = new XYZ(xDir.X, xDir.Y, 0.0).Normalize();
                    XYZ yDirFlat = new XYZ(yDir.X, yDir.Y, 0.0).Normalize();
                    double rx = ellipse.RadiusX;
                    double ry = ellipse.RadiusY;

                    double p0 = ellipse.GetEndParameter(0);
                    double p1 = ellipse.GetEndParameter(1);

                    return Ellipse.CreateCurve(centerFlat, rx, ry, xDirFlat, yDirFlat, p0, p1);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        private static string MakeValidFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        /// <summary>
        /// Gets the handler name.
        /// </summary>
        /// <returns>The string handler name.</returns>
        public string GetName()
        {
            return "Detail Item Factory Event Handler";
        }
    }

    /// <summary>
    /// Standard implementation of IFamilyLoadOptions to automatically overwrite family parameters.
    /// </summary>
    public class SimpleFamilyLoadOptions : IFamilyLoadOptions
    {
        /// <summary>
        /// Callback when a family is found during loading.
        /// </summary>
        /// <param name="familyInUse">Indicates if the family is currently in use.</param>
        /// <param name="overwriteParameterValues">Output flag to overwrite parameter values.</param>
        /// <returns>True to overwrite the family.</returns>
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        /// <summary>
        /// Callback when a shared family is found during loading.
        /// </summary>
        /// <param name="sharedFamily">The shared family element.</param>
        /// <param name="familyInUse">Indicates if the family is currently in use.</param>
        /// <param name="source">Output family source.</param>
        /// <param name="overwriteParameterValues">Output flag to overwrite parameter values.</param>
        /// <returns>True to overwrite the family.</returns>
        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
}
