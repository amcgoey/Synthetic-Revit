using System;
using System.Globalization;
using System.IO;
using System.Reflection;
#if !NETFRAMEWORK
using System.Runtime.Loader;
#endif
using Autodesk.Revit.DB;

namespace Synthetic.Shared.RevitAPI
{
    /// <summary>
    /// Registers an idempotent, host-decoupled assembly resolver for <c>eTransmitForRevitDB</c>.
    /// Ensures document purge utilities resolve <c>eTransmitForRevitDB.dll</c> across both
    /// standard Revit sessions and isolated test workers where <c>App.OnStartup</c> is bypassed.
    /// </summary>
    public static class ETransmitAssemblyResolver
    {
        private const string ETransmitAssemblySimpleName = "eTransmitForRevitDB";
        private const string ETransmitAssemblyFileName = "eTransmitForRevitDB.dll";

        private static readonly object RegistrationLock = new object();
        private static bool _isRegistered;

        [ThreadStatic]
        private static bool _resolving;

        /// <summary>
        /// Ensures the <c>eTransmitForRevitDB</c> assembly resolver is registered for the active runtime context.
        /// Safe to call repeatedly from any thread.
        /// </summary>
        public static void EnsureRegistered()
        {
            if (_isRegistered)
            {
                return;
            }

            lock (RegistrationLock)
            {
                if (_isRegistered)
                {
                    return;
                }

#if NETFRAMEWORK
                AppDomain.CurrentDomain.AssemblyResolve += OnAppDomainAssemblyResolve;
#else
                AssemblyLoadContext.Default.Resolving += OnAssemblyLoadContextResolving;
                AssemblyLoadContext? ownContext = AssemblyLoadContext.GetLoadContext(typeof(ETransmitAssemblyResolver).Assembly);
                if (ownContext != null && !ReferenceEquals(ownContext, AssemblyLoadContext.Default))
                {
                    ownContext.Resolving += OnAssemblyLoadContextResolving;
                }
#endif
                _isRegistered = true;
            }
        }

        /// <summary>
        /// Derives the four-digit Revit release year from the loaded <c>RevitAPI</c> assembly version,
        /// falling back to the compile-time <c>REVIT202X</c> symbol when running against mocks.
        /// </summary>
        internal static string ResolveRevitYear()
        {
            int? major = typeof(Document).Assembly.GetName().Version?.Major;
            if (major.HasValue)
            {
                int value = major.Value;
                if (value >= 20 && value <= 99)
                {
                    return (2000 + value).ToString(CultureInfo.InvariantCulture);
                }

                if (value >= 2000)
                {
                    return value.ToString(CultureInfo.InvariantCulture);
                }
            }

#if REVIT2023
            return "2023";
#elif REVIT2024
            return "2024";
#elif REVIT2025
            return "2025";
#elif REVIT2026
            return "2026";
#else
            return "2026";
#endif
        }

        /// <summary>
        /// Probes legacy standalone and Revit-bundled <c>eTransmitForRevitDB.dll</c> install locations.
        /// </summary>
        internal static string? FindETransmitAssemblyPath()
        {
            string year = ResolveRevitYear();
            string[] candidates = new[]
            {
                Path.Combine(@"C:\Program Files\Autodesk", $"eTransmit for Revit {year}", ETransmitAssemblyFileName),
                Path.Combine(@"C:\Program Files\Autodesk", $"Revit {year}", "AddIns", "eTransmitForRevit", ETransmitAssemblyFileName)
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsETransmitAssemblyName(string? simpleName)
        {
            return !string.IsNullOrEmpty(simpleName) &&
                   string.Equals(simpleName, ETransmitAssemblySimpleName, StringComparison.OrdinalIgnoreCase);
        }

#if NETFRAMEWORK
        private static Assembly? OnAppDomainAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            if (_resolving || string.IsNullOrWhiteSpace(args.Name))
            {
                return null;
            }

            AssemblyName requestedName = new AssemblyName(args.Name);
            if (!IsETransmitAssemblyName(requestedName.Name))
            {
                return null;
            }

            try
            {
                _resolving = true;

                foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (IsETransmitAssemblyName(loaded.GetName().Name))
                    {
                        return loaded;
                    }
                }

                string? candidatePath = FindETransmitAssemblyPath();
                if (!string.IsNullOrEmpty(candidatePath))
                {
                    return Assembly.LoadFrom(candidatePath);
                }

                return null;
            }
            finally
            {
                _resolving = false;
            }
        }
#else
        private static Assembly? OnAssemblyLoadContextResolving(AssemblyLoadContext alc, AssemblyName assemblyName)
        {
            if (_resolving || !IsETransmitAssemblyName(assemblyName.Name))
            {
                return null;
            }

            try
            {
                _resolving = true;

                foreach (Assembly loaded in alc.Assemblies)
                {
                    if (IsETransmitAssemblyName(loaded.GetName().Name))
                    {
                        return loaded;
                    }
                }

                string? candidatePath = FindETransmitAssemblyPath();
                if (!string.IsNullOrEmpty(candidatePath))
                {
                    return alc.LoadFromAssemblyPath(candidatePath);
                }

                return null;
            }
            finally
            {
                _resolving = false;
            }
        }
#endif
    }
}
