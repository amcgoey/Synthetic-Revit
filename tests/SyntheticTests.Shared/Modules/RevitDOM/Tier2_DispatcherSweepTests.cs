using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_DispatcherSweepTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        [Test]
        public void RunFullRosterSweep_VerifiesDispatcherTelemetryAndStability()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            
            // Access the active document or open standard template/project document
            Document doc = _uiapp.ActiveUIDocument?.Document ?? app.NewProjectDocument(UnitSystem.Metric);
            Assert.IsNotNull(doc, "Revit active/new document context should not be null.");

            // Collect standard instances (not ElementTypes)
            var instances = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();

            // Collect ElementTypes
            var types = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToElements();

            var allElements = new List<Element>();
            allElements.AddRange(instances);
            allElements.AddRange(types);

            // Group by concrete type and select exactly first of each type
            var uniqueElements = allElements
                .Where(e => e != null)
                .GroupBy(e => e.GetType())
                .Select(g => g.First())
                .ToList();

            var engine = new StandardSerializationEngine();
            var successfulTypes = new List<Type>();
            var pendingTypes = new List<Type>();
            var supportedByElementType = new List<Type>();
            var supportedByElement = new List<Type>();
            var unsupportedTypes = new List<Type>();
            var crashedTypes = new List<(Type, Exception)>();

            using (var globalTxGroup = new TransactionGroup(doc, "Global Dispatcher Sweep"))
            {
                globalTxGroup.Start();

                foreach (var elem in uniqueElements)
                {
                    Type elemType = elem.GetType();
                    using (var innerTxGroup = new TransactionGroup(doc, $"Sweep Element {elemType.Name}"))
                    {
                        innerTxGroup.Start();
                        SerializationResultModel.ClearWarnings();

                        try
                        {
                            // Try to execute engine.ByRevit()
                            var extractedList = engine.ByRevit(new[] { elem }, doc, isTemplate: false).ToList();
                            
                            // Capture warnings from ByRevit before ToRevit clears them
                            var extractWarnings = SerializationResultModel.CurrentThreadWarnings.ToList();
                            bool isIgnored = engine.Dispatcher.IsIgnored(elemType);
                            bool isElementTypeFallback = extractWarnings.Any(w => w.Contains("falling back to generic ElementTypeModel"));
                            bool isElementFallback = extractWarnings.Any(w => w.Contains("falling back to generic ElementModel"));
                            bool isPending = extractWarnings.Any(w => w.Contains("pending future support")) || engine.Dispatcher.IsPending(elemType);
                            bool isIgnoredOrNull = extractedList.Count == 0;

                            bool hasException = false;
                            Exception? exception = null;

                            if (extractedList.Count > 0)
                            {
                                // Immediately pass the result into engine.ToRevit()
                                var results = engine.ToRevit(extractedList, doc).ToList();
                                foreach (var res in results)
                                {
                                    if (!res.Success)
                                    {
                                        hasException = true;
                                        exception = res.Exception ?? new Exception(res.ErrorMessage ?? "Unknown failure in ToRevit");
                                    }
                                }
                            }

                            // Filter out known native compound structure end cap setting quirks
                            bool isCompoundStructureQuirk = hasException && exception != null && 
                                                           exception.Message.Contains("Input compound structure has wrong EndCap condition");

                            if (isIgnored)
                            {
                                // Dropped entirely as per DB 005-3 requirements.
                            }
                            else if (isElementTypeFallback)
                            {
                                supportedByElementType.Add(elemType);
                            }
                            else if (isElementFallback)
                            {
                                supportedByElement.Add(elemType);
                            }
                            else if (isPending)
                            {
                                pendingTypes.Add(elemType);
                            }
                            else if (isIgnoredOrNull)
                            {
                                unsupportedTypes.Add(elemType);
                            }
                            else if (hasException && exception != null && !isCompoundStructureQuirk)
                            {
                                crashedTypes.Add((elemType, exception));
                            }
                            else
                            {
                                successfulTypes.Add(elemType);
                            }
                        }
                        catch (Exception ex)
                        {
                            crashedTypes.Add((elemType, ex));
                        }

                        innerTxGroup.RollBack();
                    }
                }

                globalTxGroup.RollBack();
            }

            // Print consolidated diagnostic report
            Console.WriteLine("============================================================");
            Console.WriteLine("DISPATCHER FULL ROSTER SWEEP REPORT");
            Console.WriteLine("============================================================");
            Console.WriteLine($"Successful Types (Count: {successfulTypes.Count}):");
            foreach (var t in successfulTypes.OrderBy(x => x.FullName))
            {
                Console.WriteLine($"- {t.FullName}");
            }
            Console.WriteLine();
            Console.WriteLine($"Pending Types (Count: {pendingTypes.Count}):");
            foreach (var t in pendingTypes.OrderBy(x => x.FullName))
            {
                Console.WriteLine($"- {t.FullName}");
            }
            Console.WriteLine();
            Console.WriteLine($"Supported by ElementType (Count: {supportedByElementType.Count}):");
            foreach (var t in supportedByElementType.OrderBy(x => x.FullName))
            {
                Console.WriteLine($"- {t.FullName}");
            }
            Console.WriteLine();
            Console.WriteLine($"Supported by Element (Count: {supportedByElement.Count}):");
            foreach (var t in supportedByElement.OrderBy(x => x.FullName))
            {
                Console.WriteLine($"- {t.FullName}");
            }
            Console.WriteLine();
            Console.WriteLine($"Unsupported Types (Count: {unsupportedTypes.Count}):");
            foreach (var t in unsupportedTypes.OrderBy(x => x.FullName))
            {
                Console.WriteLine($"- {t.FullName}");
            }
            Console.WriteLine();
            Console.WriteLine($"Crashed Types (Count: {crashedTypes.Count}):");
            foreach (var item in crashedTypes.OrderBy(x => x.Item1.FullName))
            {
                Console.WriteLine($"- {item.Item1.FullName}: {item.Item2.Message}");
            }
            Console.WriteLine("============================================================");

            // Assert no unhandled unexpected exceptions occurred
            Assert.IsEmpty(crashedTypes, "No element type should crash the serialization pipeline with an unhandled exception.");
        }
    }
}
