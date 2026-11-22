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
