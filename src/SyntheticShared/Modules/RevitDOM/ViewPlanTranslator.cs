using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Synthetic.Shared;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Translator governing the extraction and injection of Revit ViewPlan elements, inheriting from ViewTranslator.
    /// Manages plan-specific properties: ViewRange, UnderlayId, and UnderlayOrientation.
    /// </summary>
    internal class ViewPlanTranslator : ViewTranslator, IModelTranslator<ViewPlan, ViewPlanModel>
    {
        private readonly IIdentityService _identityService;

        public ViewPlanTranslator(IIdentityService identityService) : base(identityService)
        {
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        }

        public void ExtractSpecifics(ViewPlan viewPlan, ViewPlanModel model, Document doc)
        {
            if (viewPlan == null) throw new ArgumentNullException(nameof(viewPlan));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // Extract base class properties first
            base.ExtractSpecifics(viewPlan, model, doc);

            // Extract ViewPlan specific properties
            model.UnderlayId = _identityService.ToModel(viewPlan.GetUnderlayBaseLevel(), doc, model.IsTemplate);
            try
            {
                model.UnderlayOrientation = (int)viewPlan.GetUnderlayOrientation();
            }
            catch (Exception) { }

            try
            {
                PlanViewRange viewRange = viewPlan.GetViewRange();
                if (viewRange != null)
                {
                    model.ViewRange = ExtractViewRange(viewRange, doc);
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                model.ViewRange = null;
            }
        }

        public ViewPlan InjectSpecifics(ViewPlanModel model, ViewPlan? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // Inject base class properties first
            revitElement = (ViewPlan?)base.InjectSpecifics(model, revitElement, doc);

            if (revitElement != null)
            {
                if (model.UnderlayId != null)
                {
                    try
                    {
                        var resolvedUnderlayId = _identityService.ResolveElementId(model.UnderlayId, doc);
                        revitElement.SetUnderlayBaseLevel(resolvedUnderlayId);
                        revitElement.SetUnderlayOrientation((UnderlayOrientation)model.UnderlayOrientation);
                    }
                    catch (Exception) { }
                }

                if (model.ViewRange != null)
                {
                    try
                    {
                        PlanViewRange vr = revitElement.GetViewRange();
                        if (vr != null)
                        {
                            if (model.ViewRange.TopLevelId != null)
                            {
                                var lvlId = _identityService.ResolveElementId(model.ViewRange.TopLevelId, doc);
                                vr.SetLevelId(PlanViewPlane.TopClipPlane, lvlId);
                                vr.SetOffset(PlanViewPlane.TopClipPlane, model.ViewRange.TopOffset);
                            }

                            if (model.ViewRange.CutPlaneLevelId != null)
                            {
                                var lvlId = _identityService.ResolveElementId(model.ViewRange.CutPlaneLevelId, doc);
                                vr.SetLevelId(PlanViewPlane.CutPlane, lvlId);
                                vr.SetOffset(PlanViewPlane.CutPlane, model.ViewRange.CutPlaneOffset);
                            }

                            if (model.ViewRange.BottomLevelId != null)
                            {
                                var lvlId = _identityService.ResolveElementId(model.ViewRange.BottomLevelId, doc);
                                vr.SetLevelId(PlanViewPlane.BottomClipPlane, lvlId);
                                vr.SetOffset(PlanViewPlane.BottomClipPlane, model.ViewRange.BottomOffset);
                            }

                            if (model.ViewRange.ViewDepthLevelId != null)
                            {
                                var lvlId = _identityService.ResolveElementId(model.ViewRange.ViewDepthLevelId, doc);
                                vr.SetLevelId(PlanViewPlane.ViewDepthPlane, lvlId);
                                vr.SetOffset(PlanViewPlane.ViewDepthPlane, model.ViewRange.ViewDepthOffset);
                            }

                            revitElement.SetViewRange(vr);
                        }
                    }
                    catch (Exception) { }
                }
            }

            return revitElement!;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((ViewPlan)revitElement, (ViewPlanModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((ViewPlanModel)model, (ViewPlan?)revitElement, doc);
        }

        #endregion
    }
}
