using System;
using System.IO;
using NUnit.Framework;
using Synthetic.Modules.StandardsManagement.Utilities;

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
