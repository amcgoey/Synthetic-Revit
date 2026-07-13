using Autodesk.Revit.DB;

using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Shared.RevitAPI{
    /// <summary>
    /// Provides linear algebra transformation mapping for tag placement relative to host elements.
    /// </summary>
    public static class CoordinateUtility
    {
        /// <summary>
        /// Converts a World Coordinate point into the Local Coordinate system of the host element.
        /// </summary>
        public static XYZ? GetLocalOffset(FamilyInstance? host, XYZ? worldPoint)
        {
            if (host == null || worldPoint == null) return null;
            
            Transform transform = host.GetTransform();
            if (!transform.IsConformal) return null; // Safety check for malformed scaling

            XYZ origin = transform.Origin;
            XYZ handDir = host.HandOrientation;
            XYZ faceDir = host.FacingOrientation;
            XYZ upDir = transform.BasisZ;

            XYZ translation = worldPoint - origin;
            
            double x = translation.DotProduct(handDir);
            double y = translation.DotProduct(faceDir);
            double z = translation.DotProduct(upDir);

            return new XYZ(x, y, z);
        }

        /// <summary>
        /// Converts a Local Coordinate offset back into the global World Coordinate space of the model.
        /// </summary>
        public static XYZ? GetWorldPoint(FamilyInstance? host, XYZ? localOffset)
        {
            if (host == null || localOffset == null) return null;

            Transform transform = host.GetTransform();
            XYZ origin = transform.Origin;
            XYZ handDir = host.HandOrientation;
            XYZ faceDir = host.FacingOrientation;
            XYZ upDir = transform.BasisZ;

            return origin + 
                   localOffset.X * handDir + 
                   localOffset.Y * faceDir + 
                   localOffset.Z * upDir;
        }

        /// <summary>
        /// Calculates the tag's target orientation based on the family instance's current rotation relative to the template's baseline.
        /// </summary>
        public static TagOrientation CalculateTagOrientation(FamilyInstance host, TagTemplate template)
        {
            if (host == null || template == null) return TagOrientation.Horizontal;
            if (!template.AllowOrientationChange) return template.Orientation;

            // Baseline hand orientation of the template
            XYZ vTmpl = new XYZ(template.HostHandX, template.HostHandY, template.HostHandZ);
            if (vTmpl.GetLength() < 1e-5)
            {
                // Default fallback if not previously saved or zero vector
                vTmpl = new XYZ(1, 0, 0);
            }

            XYZ vTgt = host.HandOrientation;

            // Project vectors onto the XY plane for 2D orientation calculation in plan views
            XYZ vTmpl2d = new XYZ(vTmpl.X, vTmpl.Y, 0);
            XYZ vTgt2d = new XYZ(vTgt.X, vTgt.Y, 0);

            if (vTmpl2d.GetLength() < 1e-5 || vTgt2d.GetLength() < 1e-5)
            {
                // If either is degenerate, default to template's baseline orientation
                return template.Orientation;
            }

            vTmpl2d = vTmpl2d.Normalize();
            vTgt2d = vTgt2d.Normalize();

            double cosTheta = vTmpl2d.DotProduct(vTgt2d);
            double absCos = System.Math.Abs(cosTheta);

            // Using cos(45 degrees) approx 0.70711 as the boundary.
            // If absCos >= 0.70711, angle is within [-45, 45] or [135, 225] -> Keep original orientation.
            // If absCos < 0.70711, angle is within (45, 135) or (225, 315) -> Toggle orientation.
            if (absCos >= 0.70711)
            {
                return template.Orientation;
            }
            else
            {
                return template.Orientation == TagOrientation.Horizontal 
                    ? TagOrientation.Vertical 
                    : TagOrientation.Horizontal;
            }
        }
    }
}
