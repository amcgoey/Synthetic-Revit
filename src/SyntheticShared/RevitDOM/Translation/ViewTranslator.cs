using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitView = Autodesk.Revit.DB.View;
using Synthetic.Shared;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Translation
{
    /// <summary>
    /// Translator governing the extraction and injection of Revit View elements, scoped to ViewPlan and ViewDrafting.
    /// Manages viewrange, underlays, graphical overrides, filters, crop boxes, and parameter controls.
    /// </summary>
    internal class ViewTranslator : IModelTranslator<RevitView, ViewModel>
    {
        private readonly IIdentityService _identityService;

        public ViewTranslator(IIdentityService identityService)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        }

        public static bool IsGraphicalView(ViewType type)
        {
            return type != ViewType.Schedule &&
                   type != ViewType.DrawingSheet &&
                   type != ViewType.ProjectBrowser &&
                   type != ViewType.SystemBrowser &&
                   type != ViewType.Undefined &&
                   type != ViewType.CostReport &&
                   type != ViewType.LoadsReport &&
                   type != ViewType.PresureLossReport;
        }

        public void ExtractSpecifics(RevitView view, ViewModel model, Document doc)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            model.IsTemplate = view.IsTemplate;

            if (IsGraphicalView(view.ViewType))
            {
                model.DisplayStyle = new EnumModel(typeof(DisplayStyle), view.DisplayStyle);
                model.SunlightIntensity = view.SunlightIntensity;
                model.ShadowIntensity = view.ShadowIntensity;
                model.Scale = view.Scale;
                model.DetailLevel = new EnumModel(typeof(ViewDetailLevel), view.DetailLevel);
                model.Discipline = new EnumModel(typeof(ViewDiscipline), view.Discipline);

                if (!view.IsTemplate)
                {
                    try
                    {
                        model.CropBox = view.CropBox.ToModel();
                        model.CropBoxActive = view.CropBoxActive;
                        model.CropBoxVisible = view.CropBoxVisible;
                    }
                    catch (Exception) { }

                    Parameter annotationCropParam = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
                    if (annotationCropParam != null && annotationCropParam.HasValue)
                    {
                        model.AnnotationCropActive = annotationCropParam.AsInteger() == 1;
                    }

                    Parameter farClipActiveParam = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR);
                    if (farClipActiveParam != null && farClipActiveParam.HasValue)
                    {
                        model.FarClipSettings = farClipActiveParam.AsInteger();
                    }

                    Parameter farClipOffsetParam = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                    if (farClipOffsetParam != null && farClipOffsetParam.HasValue)
                    {
                        model.FarClipOffset = farClipOffsetParam.AsDouble();
                    }
                }

                model.PartsVisibility = new EnumModel(typeof(PartsVisibility), view.PartsVisibility);
                model.CategoryGraphicOverrides = CategoryGraphicOverrideTranslator.GetCategoryGraphicOverrides(view, _identityService);

                var filters = view.GetFilters();
                if (filters != null && filters.Count > 0)
                {
                    model.ViewFilterOverrides = new List<ViewFilterOverrideModel>();
                    foreach (ElementId filterId in filters)
                    {
                        ParameterFilterElement filter = doc.GetElement(filterId) as ParameterFilterElement;
                        if (filter != null)
                        {
                            OverrideGraphicSettings ogs = view.GetFilterOverrides(filterId);
                            var filterOverride = filter.ToModel(ogs, doc, _identityService);
                            filterOverride.IsVisible = view.GetFilterVisibility(filterId);
                            model.ViewFilterOverrides.Add(filterOverride);
                        }
                    }
                }
            }

            model.ViewFamilyTypeId = _identityService.ToModel(view.GetTypeId(), doc, model.IsTemplate);

            Parameter phaseParam = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
            if (phaseParam != null && phaseParam.HasValue)
            {
                model.Phase = _identityService.ToModel(phaseParam.AsElementId(), doc, model.IsTemplate);
            }

            Parameter phaseFilterParam = view.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER);
            if (phaseFilterParam != null && phaseFilterParam.HasValue)
            {
                model.PhaseFilter = _identityService.ToModel(phaseFilterParam.AsElementId(), doc, model.IsTemplate);
            }

            if (view is ViewPlan viewPlan)
            {
                model.LevelId = _identityService.ToModel(viewPlan.GenLevel?.Id, doc, model.IsTemplate);
            }

            if (view.IsTemplate)
            {
                ICollection<ElementId> nonControlledIds = view.GetNonControlledTemplateParameterIds();
                foreach (ElementId id in nonControlledIds)
                {
#if REVIT2022 || REVIT2023
                    model.NonControlledParameterIds.Add(id.IntegerValue);
#else
                    model.NonControlledParameterIds.Add((int)id.Value);
#endif
                }
            }

            // Extract Scope Box
            try
            {
                Parameter scopeBoxParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                if (scopeBoxParam != null && scopeBoxParam.HasValue)
                {
                    model.ScopeBoxId = _identityService.ToModel(scopeBoxParam.AsElementId(), doc, model.IsTemplate);
                }
            }
            catch (Exception) { }

            // Purge Scope Box from generic parameters
            if (model.Parameters != null)
            {
                model.Parameters.RemoveAll(p => p.Id == (int)BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            }
        }

        public RevitView? InjectSpecifics(ViewModel model, RevitView? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // Purge Scope Box parameter from generic parameters
            if (model.Parameters != null)
            {
                model.Parameters.RemoveAll(p => p.Id == (int)BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            }

            if (revitElement == null)
            {
                if (model.IsTemplate)
                {
                    // Duplication strategy for view templates
                    ViewType matchingViewType = GetMatchingViewType(model, doc);

                    // 1. Search for an existing template of the matching ViewType
                    RevitView? templateSource = new FilteredElementCollector(doc)
                        .OfClass(typeof(RevitView))
                        .Cast<RevitView>()
                        .FirstOrDefault(v => v.IsTemplate && v.ViewType == matchingViewType);

                    if (templateSource != null)
                    {
                        ElementId newViewId = templateSource.Duplicate(ViewDuplicateOption.Duplicate);
                        revitElement = doc.GetElement(newViewId) as RevitView;
                    }
                    else
                    {
                        // 2. Search for a standard view of the matching ViewType
                        RevitView? standardSource = new FilteredElementCollector(doc)
                            .OfClass(typeof(RevitView))
                            .Cast<RevitView>()
                            .FirstOrDefault(v => !v.IsTemplate && v.ViewType == matchingViewType);

                        if (standardSource != null)
                        {
                            revitElement = standardSource.CreateViewTemplate();
                        }
                        else
                        {
                            // 3. Fallback: Create a temporary view, create template from it, and delete the temporary view
                            RevitView? tempView = null;
                            if (matchingViewType == ViewType.DraftingView)
                            {
                                ElementId defaultDraftingType = GetDefaultViewFamilyTypeId(doc, model);
                                tempView = ViewDrafting.Create(doc, defaultDraftingType);
                            }
                            else if (matchingViewType == ViewType.Section || matchingViewType == ViewType.Detail)
                            {
                                ElementId defaultSectionType = GetDefaultViewFamilyTypeId(doc, model);
                                BoundingBoxXYZ tempBox = new BoundingBoxXYZ { Min = new XYZ(-10, -10, -10), Max = new XYZ(10, 10, 10) };
                                if (matchingViewType == ViewType.Detail)
                                {
                                    tempView = ViewSection.CreateDetail(doc, defaultSectionType, tempBox);
                                }
                                else
                                {
                                    tempView = ViewSection.CreateSection(doc, defaultSectionType, tempBox);
                                }
                            }
                            else if (matchingViewType == ViewType.Elevation)
                            {
                                ElementId defaultElevationType = GetDefaultViewFamilyTypeId(doc, model);
                                RevitView? hostPlan = new FilteredElementCollector(doc)
                                    .OfClass(typeof(ViewPlan))
                                    .Cast<ViewPlan>()
                                    .FirstOrDefault(v => !v.IsTemplate);

                                if (hostPlan != null)
                                {
                                    ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, defaultElevationType, XYZ.Zero, 100);
                                    tempView = marker.CreateElevation(doc, hostPlan.Id, 0);
                                }
                            }
                            else // FloorPlan or CeilingPlan or general plan view
                            {
                                ElementId defaultPlanType = GetDefaultViewFamilyTypeId(doc, model);
                                Level? defaultLevel = new FilteredElementCollector(doc)
                                    .OfClass(typeof(Level))
                                    .Cast<Level>()
                                    .FirstOrDefault();

                                if (defaultLevel != null)
                                {
                                    tempView = ViewPlan.Create(doc, defaultPlanType, defaultLevel.Id);
                                }
                            }

                            if (tempView != null)
                            {
                                revitElement = tempView.CreateViewTemplate();
                                doc.Delete(tempView.Id);
                            }
                            else
                            {
                                throw new InvalidOperationException($"Could not instantiate a temporary view of type {matchingViewType} to create template '{model.Name}'.");
                            }
                        }
                    }
                }
                else
                {
                    // Physical View Creation
                    ElementId viewFamilyTypeId = _identityService.ResolveElementId(model.ViewFamilyTypeId, doc);
                    if (viewFamilyTypeId == ElementId.InvalidElementId)
                    {
                        viewFamilyTypeId = GetDefaultViewFamilyTypeId(doc, model);
                    }

                    // Determine view type from the ViewFamilyType
                    ViewFamilyType? vft = doc.GetElement(viewFamilyTypeId) as ViewFamilyType;
                    bool isPlan = false;
                    bool isDrafting = false;
                    bool isSection = false;
                    bool isDetail = false;
                    bool isElevation = false;

                    if (vft != null)
                    {
                        isPlan = vft.ViewFamily == ViewFamily.FloorPlan || vft.ViewFamily == ViewFamily.CeilingPlan || vft.ViewFamily == ViewFamily.AreaPlan;
                        isDrafting = vft.ViewFamily == ViewFamily.Drafting;
                        isSection = vft.ViewFamily == ViewFamily.Section;
                        isDetail = vft.ViewFamily == ViewFamily.Detail;
                        isElevation = vft.ViewFamily == ViewFamily.Elevation;
                    }
                    else
                    {
                        string className = model.Class ?? string.Empty;
                        isPlan = className.Contains("ViewPlan");
                        isDrafting = className.Contains("ViewDrafting");
                        isSection = className.Contains("ViewSection") && !className.Contains("Detail") && !className.Contains("Elevation");
                    }

                    if (isPlan)
                    {
                        var levelId = _identityService.ResolveElementId(model.LevelId, doc);
                        if (levelId == ElementId.InvalidElementId)
                        {
                            throw new InvalidOperationException($"Cannot create plan view '{model.Name}' because Level ID is missing or unresolved.");
                        }

                        revitElement = ViewPlan.Create(doc, viewFamilyTypeId, levelId);
                    }
                    else if (isDrafting)
                    {
                        revitElement = ViewDrafting.Create(doc, viewFamilyTypeId);
                    }
                    else if (isSection || isDetail)
                    {
                        BoundingBoxXYZ sectionBox;
                        if (model.CropBox != null)
                        {
                            sectionBox = model.CropBox.ToNative();
                        }
                        else
                        {
                            sectionBox = new BoundingBoxXYZ();
                            sectionBox.Min = new XYZ(-10, -10, -10);
                            sectionBox.Max = new XYZ(10, 10, 10);
                        }

                        if (isDetail)
                        {
                            revitElement = ViewSection.CreateDetail(doc, viewFamilyTypeId, sectionBox);
                        }
                        else
                        {
                            revitElement = ViewSection.CreateSection(doc, viewFamilyTypeId, sectionBox);
                        }
                    }
                    else if (isElevation)
                    {
                        RevitView? planView = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewPlan))
                            .Cast<ViewPlan>()
                            .FirstOrDefault(v => !v.IsTemplate);

                        if (planView == null)
                        {
                            throw new InvalidOperationException($"Cannot create elevation view '{model.Name}' because no non-template plan view was found to host the marker.");
                        }

                        XYZ origin = XYZ.Zero;
                        if (model.CropBox != null && model.CropBox.Transform != null && model.CropBox.Transform.Origin != null)
                        {
                            origin = model.CropBox.Transform.Origin.ToNative();
                        }

                        int scale = (model.Scale.HasValue && model.Scale.Value > 0) ? model.Scale.Value : 100;

                        ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, viewFamilyTypeId, origin, scale);
                        revitElement = marker.CreateElevation(doc, planView.Id, 0);
                    }
                    else
                    {
                        throw new NotSupportedException($"Creating view type '{model.Class}' is not supported by ViewTranslator.");
                    }
                }
            }

            if (revitElement != null && !string.IsNullOrEmpty(model.Name) && revitElement.Name != model.Name)
            {
                try
                {
                    revitElement.Name = model.Name;
                }
                catch (Exception) { }
            }

            // Apply view modifications
            if (IsGraphicalView(revitElement.ViewType))
            {
                if (model.DisplayStyle != null && !string.IsNullOrEmpty(model.DisplayStyle.Type))
                {
                    revitElement.DisplayStyle = (DisplayStyle)model.DisplayStyle.ToEnum();
                }

                if (model.SunlightIntensity.HasValue)
                {
                    revitElement.SunlightIntensity = model.SunlightIntensity.Value;
                }
                if (model.ShadowIntensity.HasValue)
                {
                    revitElement.ShadowIntensity = model.ShadowIntensity.Value;
                }

                // Scale
                Parameter scaleParam = revitElement.get_Parameter(BuiltInParameter.VIEW_SCALE);
                if (scaleParam != null && !scaleParam.IsReadOnly && model.Scale.HasValue)
                {
                    revitElement.Scale = model.Scale.Value;
                }

                // DetailLevel
                Parameter detailParam = revitElement.get_Parameter(BuiltInParameter.VIEW_DETAIL_LEVEL);
                if (detailParam != null && !detailParam.IsReadOnly && model.DetailLevel != null && !string.IsNullOrEmpty(model.DetailLevel.Type))
                {
                    revitElement.DetailLevel = (ViewDetailLevel)model.DetailLevel.ToEnum();
                }

                // Discipline
                Parameter disciplineParam = revitElement.get_Parameter(BuiltInParameter.VIEW_DISCIPLINE);
                if (disciplineParam != null && !disciplineParam.IsReadOnly && model.Discipline != null && !string.IsNullOrEmpty(model.Discipline.Type))
                {
                    revitElement.Discipline = (ViewDiscipline)model.Discipline.ToEnum();
                }

                if (revitElement.AreGraphicsOverridesAllowed())
                {
                    if (model.CategoryGraphicOverrides != null)
                    {
                        foreach (CategoryGraphicOverridesModel catOverride in model.CategoryGraphicOverrides)
                        {
                            GraphicOverrideUtility.ModifyOverrideGraphicSettings(catOverride, revitElement, _identityService);
                        }
                    }

                    if (model.ViewFilterOverrides != null)
                    {
                        foreach (ViewFilterOverrideModel filterOverride in model.ViewFilterOverrides)
                        {
                            if (filterOverride.FilterId != null)
                            {
                                ElementId filterId = _identityService.ResolveElementId(filterOverride.FilterId, doc);
                                if (filterId != ElementId.InvalidElementId)
                                {
                                    if (!revitElement.GetFilters().Contains(filterId))
                                    {
                                        try
                                        {
                                            revitElement.AddFilter(filterId);
                                        }
                                        catch (Exception) { }
                                    }

                                    if (revitElement.GetFilters().Contains(filterId))
                                    {
                                        try
                                        {
                                            revitElement.SetFilterVisibility(filterId, filterOverride.IsVisible);
                                            revitElement.SetFilterOverrides(filterId, GraphicOverrideUtility.ToOverrideGraphicSettings(filterOverride, doc, _identityService));
                                        }
                                        catch (Exception) { }
                                    }
                                }
                            }
                        }
                    }
                }

                // Parts Visibility
                if (model.PartsVisibility != null && !string.IsNullOrEmpty(model.PartsVisibility.Type))
                {
                    revitElement.PartsVisibility = (PartsVisibility)model.PartsVisibility.ToEnum();
                }

                // Crop Box
                if (!revitElement.IsTemplate)
                {
                    try
                    {
                        if (model.CropBox != null)
                        {
                            BoundingBoxXYZ nativeCropBox = model.CropBox.ToNative();
                            if (nativeCropBox != null)
                            {
                                revitElement.CropBox = nativeCropBox;
                            }
                        }
                        if (model.CropBoxActive.HasValue)
                        {
                            revitElement.CropBoxActive = model.CropBoxActive.Value;
                        }
                        if (model.CropBoxVisible.HasValue)
                        {
                            revitElement.CropBoxVisible = model.CropBoxVisible.Value;
                        }
                    }
                    catch (Exception) { }

                    try
                    {
                        var annotationCropParam = revitElement.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
                        if (annotationCropParam != null && !annotationCropParam.IsReadOnly)
                        {
                        if (model.AnnotationCropActive.HasValue)
                        {
                            annotationCropParam.Set(model.AnnotationCropActive.Value ? 1 : 0);
                        }
                        }
                    }
                    catch (Exception) { }

                    try
                    {
                        // Depth Clipping
                        var farClipActiveParam = revitElement.get_Parameter(BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR);
                        if (farClipActiveParam != null && !farClipActiveParam.IsReadOnly && model.FarClipSettings.HasValue)
                        {
                            farClipActiveParam.Set(model.FarClipSettings.Value);
                        }
                        var farClipOffsetParam = revitElement.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                        if (farClipOffsetParam != null && !farClipOffsetParam.IsReadOnly && model.FarClipOffset.HasValue)
                        {
                            farClipOffsetParam.Set(model.FarClipOffset.Value);
                        }
                    }
                    catch (Exception) { }
                }
            }

            try
            {
                // Phase
                Parameter phaseParam = revitElement.get_Parameter(BuiltInParameter.VIEW_PHASE);
                if (phaseParam != null && !phaseParam.IsReadOnly && model.Phase != null)
                {
                    phaseParam.Set(_identityService.ResolveElementId(model.Phase, doc));
                }
            }
            catch (Exception) { }

            try
            {
                // PhaseFilter
                Parameter phaseFilterParam = revitElement.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER);
                if (phaseFilterParam != null && !phaseFilterParam.IsReadOnly && model.PhaseFilter != null)
                {
                    phaseFilterParam.Set(_identityService.ResolveElementId(model.PhaseFilter, doc));
                }
            }
            catch (Exception) { }



            // Scope Box assignment
            if (model.ScopeBoxId != null)
            {
                try
                {
                    ElementId scopeBoxId = _identityService.ResolveElementId(model.ScopeBoxId, doc);
                    if (scopeBoxId != ElementId.InvalidElementId)
                    {
                        Parameter scopeBoxParam = revitElement.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                        if (scopeBoxParam != null && !scopeBoxParam.IsReadOnly)
                        {
                            scopeBoxParam.Set(scopeBoxId);
                        }
                    }
                    else
                    {
                        SerializationResultModel.LogWarning($"Scope Box '{model.ScopeBoxId.Name}' for view '{model.Name}' was missing or unresolved. Skipping assignment and relying on native crop box.");
                    }
                }
                catch (Exception ex)
                {
                    SerializationResultModel.LogWarning($"Failed to apply Scope Box to view '{model.Name}': {ex.Message}");
                }
            }

            // ViewTemplate specific properties
            if (model.IsTemplate && revitElement.IsTemplate)
            {
                List<ElementId> nonControlledIds = new List<ElementId>();
                if (model.NonControlledParameterIds != null)
                {
                    foreach (int intId in model.NonControlledParameterIds)
                    {
#if REVIT2022 || REVIT2023
                        nonControlledIds.Add(new ElementId(intId));
#else
                        nonControlledIds.Add(new ElementId((long)intId));
#endif
                    }
                }

                try
                {
                    revitElement.SetNonControlledTemplateParameterIds(nonControlledIds);
                }
                catch (Exception) { }
            }

            return revitElement;
        }

        private ViewType GetMatchingViewType(ViewModel model, Document doc)
        {
            // Try to get ViewType from ViewFamilyType first
            ElementId vftId = _identityService.ResolveElementId(model.ViewFamilyTypeId, doc);
            if (vftId != ElementId.InvalidElementId)
            {
                ViewFamilyType? vft = doc.GetElement(vftId) as ViewFamilyType;
                if (vft != null)
                {
                    switch (vft.ViewFamily)
                    {
                        case ViewFamily.FloorPlan:
                            return ViewType.FloorPlan;
                        case ViewFamily.CeilingPlan:
                            return ViewType.CeilingPlan;
                        case ViewFamily.Drafting:
                            return ViewType.DraftingView;
                        case ViewFamily.Section:
                            return ViewType.Section;
                        case ViewFamily.Detail:
                            return ViewType.Detail;
                        case ViewFamily.Elevation:
                            return ViewType.Elevation;
                    }
                }
            }

            // Fallback to Class or ViewFamilyTypeId name
            string className = model.Class ?? string.Empty;
            string typeName = model.ViewFamilyTypeId?.Name ?? string.Empty;

            if (className.Contains("ViewPlan") || typeName.Contains("Floor"))
            {
                return ViewType.FloorPlan;
            }
            if (typeName.Contains("Ceiling"))
            {
                return ViewType.CeilingPlan;
            }
            if (className.Contains("ViewDrafting") || typeName.Contains("Drafting"))
            {
                return ViewType.DraftingView;
            }
            if (className.Contains("ViewSection"))
            {
                if (typeName.Contains("Detail"))
                {
                    return ViewType.Detail;
                }
                if (typeName.Contains("Elevation"))
                {
                    return ViewType.Elevation;
                }
                return ViewType.Section;
            }

            return ViewType.FloorPlan; // ultimate fallback
        }

        private ElementId GetDefaultViewFamilyTypeId(Document doc, ViewModel model)
        {
            ViewFamily family = ViewFamily.FloorPlan;
            string className = model.Class ?? string.Empty;
            string typeName = model.ViewFamilyTypeId?.Name ?? string.Empty;

            if (className.Contains("ViewDrafting") || typeName.Contains("Drafting"))
            {
                family = ViewFamily.Drafting;
            }
            else if (typeName.Contains("Ceiling"))
            {
                family = ViewFamily.CeilingPlan;
            }
            else if (typeName.Contains("Floor"))
            {
                family = ViewFamily.FloorPlan;
            }
            else if (className.Contains("ViewSection") || typeName.Contains("Section"))
            {
                if (typeName.Contains("Detail"))
                {
                    family = ViewFamily.Detail;
                }
                else if (typeName.Contains("Elevation"))
                {
                    family = ViewFamily.Elevation;
                }
                else
                {
                    family = ViewFamily.Section;
                }
            }

            var defaultType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == family);

            if (defaultType != null)
            {
                SerializationResultModel.LogWarning($"ViewFamilyType for view '{model.Name}' was missing or unresolved. Degraded to default system type '{defaultType.Name}' of family '{family}'.");
                return defaultType.Id;
            }

            return ElementId.InvalidElementId;
        }

        protected PlanViewRangeModel ExtractViewRange(PlanViewRange viewRange, Document doc)
        {
            if (viewRange == null) throw new ArgumentNullException(nameof(viewRange));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var model = new PlanViewRangeModel();
            model.TopLevelId = _identityService.ToModel(viewRange.GetLevelId(PlanViewPlane.TopClipPlane), doc, false);
            model.TopOffset = viewRange.GetOffset(PlanViewPlane.TopClipPlane);

            model.CutPlaneLevelId = _identityService.ToModel(viewRange.GetLevelId(PlanViewPlane.CutPlane), doc, false);
            model.CutPlaneOffset = viewRange.GetOffset(PlanViewPlane.CutPlane);

            model.BottomLevelId = _identityService.ToModel(viewRange.GetLevelId(PlanViewPlane.BottomClipPlane), doc, false);
            model.BottomOffset = viewRange.GetOffset(PlanViewPlane.BottomClipPlane);

            model.ViewDepthLevelId = _identityService.ToModel(viewRange.GetLevelId(PlanViewPlane.ViewDepthPlane), doc, false);
            model.ViewDepthOffset = viewRange.GetOffset(PlanViewPlane.ViewDepthPlane);

            return model;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((RevitView)revitElement, (ViewModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((ViewModel)model, (RevitView?)revitElement, doc);
        }

        #endregion
    }
}
