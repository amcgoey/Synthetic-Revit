using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Modules.SharedCoordinates.Adapters;
using Synthetic.Modules.SharedCoordinates.Models;

namespace SyntheticTests.Modules.SharedCoordinates
{
    [TestFixture]
    public class SharedCoordinatesRepositionIntegrationTests
    {
        private Document _doc = null!;
        private UIDocument _uidoc = null!;
        private Document _linkedDoc = null!;

        [SetUp]
        public void SetUp()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _uidoc = new UIDocument(_doc);
            _linkedDoc = (Document)Activator.CreateInstance(typeof(Document), true)!;

            // Host active project location at origin
            var hostLoc = new ProjectLocation { Name = "Host Site" };
            hostLoc.MockTotalTransform = Transform.Identity;
            _doc.AddElement(hostLoc, new ElementId(101));
            _doc.ActiveProjectLocation = hostLoc;

            // Linked doc site "Floor 2" at (0, 0, 20)
            var linkedLocFloor2 = new ProjectLocation { Name = "Floor 2" };
            linkedLocFloor2.MockTotalTransform = new Transform
            {
                Origin = new XYZ(0, 0, 20.0),
                BasisX = new XYZ(1, 0, 0),
                BasisY = new XYZ(0, 1, 0),
                BasisZ = new XYZ(0, 0, 1)
            };
            _linkedDoc.AddElement(linkedLocFloor2, new ElementId(201));

            // Linked doc site "Floor 3" at (0, 0, 40)
            var linkedLocFloor3 = new ProjectLocation { Name = "Floor 3" };
            linkedLocFloor3.MockTotalTransform = new Transform
            {
                Origin = new XYZ(0, 0, 40.0),
                BasisX = new XYZ(1, 0, 0),
                BasisY = new XYZ(0, 1, 0),
                BasisZ = new XYZ(0, 0, 1)
            };
            _linkedDoc.AddElement(linkedLocFloor3, new ElementId(202));
        }

        [Test]
        public void GetLinkRepositionGroups_DetectsOutOfSyncAndUnassignedInstances()
        {
            // Arrange: Link type
            var linkType = new RevitLinkType { Name = "Unit_Tower.rvt" };
            var linkTypeId = new ElementId(301);
            _doc.AddElement(linkType, linkTypeId);

            // Instance 1: Assigned to Floor 2, currently at (0, 0, 20) -> Already Aligned!
            var instAligned = new RevitLinkInstance
            {
                Name = "Unit_Tower_L2",
                MockTypeId = linkTypeId,
                MockLinkDocument = _linkedDoc,
                MockSiteName = "Floor 2",
                MockTransform = new Transform { Origin = new XYZ(0, 0, 20.0) }
            };
            _doc.AddElement(instAligned, new ElementId(401));

            // Instance 2: Assigned to Floor 3, currently at (0, 0, 0) -> Out of sync! (expected Z = 40)
            var instOutOfSync = new RevitLinkInstance
            {
                Name = "Unit_Tower_L3",
                MockTypeId = linkTypeId,
                MockLinkDocument = _linkedDoc,
                MockSiteName = "Floor 3",
                MockTransform = new Transform { Origin = new XYZ(0, 0, 0.0) }
            };
            _doc.AddElement(instOutOfSync, new ElementId(402));

            // Instance 3: Unassigned (<Not Shared>)
            var instUnassigned = new RevitLinkInstance
            {
                Name = "Unit_Tower_Unassigned",
                MockTypeId = linkTypeId,
                MockLinkDocument = _linkedDoc,
                MockSiteName = "<Not Shared>"
            };
            _doc.AddElement(instUnassigned, new ElementId(403));

            var adapter = new RevitSharedCoordinatesAdapter(_doc, _uidoc);

            // Act
            var groups = adapter.GetLinkRepositionGroups();

            // Assert
            Assert.AreEqual(1, groups.Count);
            var group = groups[0];
            Assert.AreEqual("Unit_Tower.rvt", group.TypeName);
            Assert.AreEqual(3, group.TotalInstanceCount);
            Assert.AreEqual(1, group.OutOfSyncCount);
            Assert.AreEqual(1, group.UnassignedCount);
            Assert.IsTrue(group.IsChecked);
        }

        [Test]
        public void RepositionLinks_RepositionsOutOfSyncInstancesAndRestoresPinState()
        {
            // Arrange: Link type and pinned out-of-sync instance
            var linkType = new RevitLinkType { Name = "Unit_Tower.rvt" };
            var linkTypeId = new ElementId(301);
            _doc.AddElement(linkType, linkTypeId);

            var instPinned = new RevitLinkInstance
            {
                Name = "Unit_Tower_Pinned",
                MockTypeId = linkTypeId,
                MockLinkDocument = _linkedDoc,
                MockSiteName = "Floor 3",
                MockTransform = new Transform { Origin = new XYZ(0, 0, 0.0) }, // Expected is (0, 0, 40)
                Pinned = true // Pinned!
            };
            _doc.AddElement(instPinned, new ElementId(501));

            var adapter = new RevitSharedCoordinatesAdapter(_doc, _uidoc);
            var request = new RepositionLinksRequest
            {
                SelectedTypeIds = new List<string> { linkTypeId.ToString() }
            };

            // Act
            var summary = adapter.RepositionLinks(request);

            // Assert
            Assert.AreEqual(1, summary.RepositionedCount);
            Assert.AreEqual(0, summary.SkippedCount);
            Assert.AreEqual(0, summary.ErrorMessages.Count);
            Assert.Contains("Unit_Tower_Pinned", (System.Collections.ICollection)summary.RepositionedInstanceNames);

            // Transformed origin should now be at expected Z = 40
            Assert.AreEqual(40.0, instPinned.MockTransform.Origin.Z, 1e-5);

            // Pinned state must be restored after repositioning
            Assert.IsTrue(instPinned.Pinned);
        }

        [Test]
        public void RepositionLinks_SafelySkipsNotSharedOrUnresolvedInstances()
        {
            // Arrange
            var linkType = new RevitLinkType { Name = "Unit_Tower.rvt" };
            var linkTypeId = new ElementId(301);
            _doc.AddElement(linkType, linkTypeId);

            var instNotShared = new RevitLinkInstance
            {
                Name = "Unit_Tower_NotShared",
                MockTypeId = linkTypeId,
                MockLinkDocument = _linkedDoc,
                MockSiteName = "<Not Shared>"
            };
            _doc.AddElement(instNotShared, new ElementId(601));

            var adapter = new RevitSharedCoordinatesAdapter(_doc, _uidoc);
            var request = new RepositionLinksRequest
            {
                SelectedTypeIds = new List<string> { linkTypeId.ToString() }
            };

            // Act
            var summary = adapter.RepositionLinks(request);

            // Assert
            Assert.AreEqual(0, summary.RepositionedCount);
            Assert.AreEqual(1, summary.SkippedCount);
            Assert.Contains("Unit_Tower_NotShared", (System.Collections.ICollection)summary.SkippedInstanceNames);
        }
    }
}
