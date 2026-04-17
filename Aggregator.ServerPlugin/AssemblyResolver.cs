using System;
using System.IO;
using System.Reflection;

namespace Aggregator.ServerPlugin
{
    /// <summary>
    /// Resolves Microsoft.TeamFoundation.* assemblies from the Azure DevOps Server installation
    /// when they are not present in the Web Services\bin directory.
    ///
    /// Background: starting with Azure DevOps Server 2022 Update 2 (build 19.235.35102.1),
    /// Microsoft moved the legacy Work Item Tracking client object model out of
    /// "Application Tier\Web Services\bin" and into "Application Tier\Tools".
    /// Without this resolver the plugin fails to load with
    /// FileNotFoundException for Microsoft.TeamFoundation.WorkItemTracking.Client.
    /// </summary>
    internal static class AssemblyResolver
    {
        private static readonly object SyncRoot = new object();
        private static bool registered;

        public static void EnsureRegistered()
        {
            if (registered) return;
            lock (SyncRoot)
            {
                if (registered) return;
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                registered = true;
            }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            // Filter to TFS/ADOS assemblies — avoid intercepting unrelated lookups.
            string requested = args.Name ?? string.Empty;
            if (!requested.StartsWith("Microsoft.TeamFoundation.", StringComparison.OrdinalIgnoreCase)
                && !requested.StartsWith("Microsoft.VisualStudio.Services.", StringComparison.OrdinalIgnoreCase)
                && !requested.StartsWith("Microsoft.WITDataStore", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string simpleName = new AssemblyName(requested).Name;

            foreach (string probeDir in GetProbeDirectories())
            {
                if (string.IsNullOrEmpty(probeDir) || !Directory.Exists(probeDir))
                {
                    continue;
                }

                string candidate = Path.Combine(probeDir, simpleName + ".dll");
                if (File.Exists(candidate))
                {
                    try
                    {
                        return Assembly.LoadFrom(candidate);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "TFSAggregator2 AssemblyResolver: failed loading " + candidate + ": " + ex.Message);
                    }
                }
            }

            return null;
        }

        private static string[] GetProbeDirectories()
        {
            // Plugin DLL lives in: <InstallPath>\Application Tier\Web Services\bin\Plugins
            // Walk up to <InstallPath> and check the Tools and Web Services\bin folders.
            string pluginDir = Path.GetDirectoryName(typeof(AssemblyResolver).Assembly.Location);
            string installPath = ClimbUp(pluginDir, 3);

            return new[]
            {
                installPath != null ? Path.Combine(installPath, "Tools") : null,
                installPath != null ? Path.Combine(installPath, "Application Tier", "Tools") : null,
                pluginDir != null ? Path.Combine(pluginDir, "..") : null,
            };
        }

        private static string ClimbUp(string path, int levels)
        {
            for (int i = 0; i < levels && !string.IsNullOrEmpty(path); i++)
            {
                path = Path.GetDirectoryName(path);
            }
            return path;
        }
    }
}
