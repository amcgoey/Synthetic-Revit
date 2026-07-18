using System;
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
