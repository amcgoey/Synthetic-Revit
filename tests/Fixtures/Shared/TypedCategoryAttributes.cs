using System;
using NUnit.Framework;

namespace SyntheticTests
{
    /// <summary>
    /// Marks a fast headless unit test running under <c>Synthetic.LogicTests</c> with zero Revit process dependency.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class UnitTestAttribute : CategoryAttribute
    {
        public UnitTestAttribute() : base("Unit") { }
    }

    /// <summary>
    /// Marks a headless structural/dependency guardrail test running under <c>Synthetic.LogicTests</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ArchitectureTestAttribute : CategoryAttribute
    {
        public ArchitectureTestAttribute() : base("Architecture") { }
    }

    /// <summary>
    /// Marks a live Revit host smoke test verifying add-in bootstrap, ribbon/UI initialization, or event dispatch.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RevitSmokeTestAttribute : CategoryAttribute
    {
        public RevitSmokeTestAttribute() : base("RevitSmoke") { }
    }

    /// <summary>
    /// Marks a live Revit adapter test operating against non-mutating or rolled-back document transactions.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RevitAdapterTestAttribute : CategoryAttribute
    {
        public RevitAdapterTestAttribute() : base("RevitAdapter") { }
    }

    /// <summary>
    /// Marks a live Revit test opening isolated template copies, background family documents, or linked models.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RevitIsolatedDocTestAttribute : CategoryAttribute
    {
        public RevitIsolatedDocTestAttribute() : base("RevitIsolatedDoc") { }
    }

    /// <summary>
    /// Marks a quarantined test (typically paired with <see cref="ExplicitAttribute"/>) for known upstream or legacy mock issues.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class QuarantineTestAttribute : CategoryAttribute
    {
        public QuarantineTestAttribute() : base("Quarantine") { }
    }
}
