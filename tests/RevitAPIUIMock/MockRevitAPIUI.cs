using System;
using Autodesk.Revit.DB;

namespace Autodesk.Revit.UI
{
    /// <summary>
    /// Mock implementation of Revit's UIApplication for headless unit testing.
    /// </summary>
    public class UIApplication
    {
        public Autodesk.Revit.ApplicationServices.Application Application { get; } = new Autodesk.Revit.ApplicationServices.Application();
        public UIDocument? ActiveUIDocument { get; set; }

        public UIApplication() { }
        public UIApplication(Autodesk.Revit.ApplicationServices.Application app)
        {
            Application = app;
        }

        public IntPtr MainWindowHandle => IntPtr.Zero;
    }

    /// <summary>
    /// Mock implementation of Revit's UIDocument for headless unit testing.
    /// </summary>
    public class UIDocument
    {
        public Document Document { get; }

        public UIDocument(Document doc)
        {
            Document = doc;
        }
    }

    /// <summary>
    /// Mock implementation of Revit external command Execution Result.
    /// </summary>
    public enum Result
    {
        Succeeded = -1,
        Failed = 0,
        Cancelled = 1
    }

    public interface IExternalApplication
    {
        Result OnStartup(UIControlledApplication application);
        Result OnShutdown(UIControlledApplication application);
    }

    public interface IExternalCommand
    {
        Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements);
    }

    public class UIControlledApplication
    {
        public IntPtr MainWindowHandle => IntPtr.Zero;
    }

    public class ExternalCommandData
    {
        public UIApplication Application { get; set; } = new UIApplication();
    }

    public class ElementSet : APIObject, System.Collections.IEnumerable
    {
        private readonly System.Collections.ArrayList _elements = new System.Collections.ArrayList();
        public void Clear() => _elements.Clear();
        public bool IsEmpty => _elements.Count == 0;
        public int Size => _elements.Count;
        public System.Collections.IEnumerator GetEnumerator() => _elements.GetEnumerator();
    }

    /// <summary>
    /// Mock implementation of Revit's IExternalEventHandler interface.
    /// </summary>
    public interface IExternalEventHandler
    {
        void Execute(UIApplication app);
        string GetName();
    }

    /// <summary>
    /// Mock implementation of Revit's ExternalEventRequest enum.
    /// </summary>
    public enum ExternalEventRequest
    {
        Accepted,
        Pending,
        Denied
    }

    /// <summary>
    /// Mock implementation of Revit's ExternalEvent class.
    /// </summary>
    public class ExternalEvent
    {
        public static ExternalEvent Create(IExternalEventHandler handler)
        {
            return new ExternalEvent();
        }

        public ExternalEventRequest Raise()
        {
            return ExternalEventRequest.Accepted;
        }
    }
}

