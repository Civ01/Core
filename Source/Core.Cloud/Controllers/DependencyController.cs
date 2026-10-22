using CUE4Parse.UE4.Assets;

using Microsoft.AspNetCore.Mvc;

using Serilog;

/* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ */
/* Core Cloud Controller: Dependencies                                                                                              */
/*                                                                                                                                  */
/* Which packages each package under a folder imports, read straight from the cooked package headers. A cooked asset registry is     */
/* often shipped with its dependency section stripped, and this is the same graph read from the packages themselves, so an import   */
/* can be planned from the assets that pull in least.                                                                               */
/* ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ */

namespace Core.Cloud.Controllers;

public partial class CloudApiController
{
    /* Every package under a folder, against the packages it imports */
    [HttpGet("folder/dependencies")]
    public ActionResult GetFolderDependencies(string? path)
    {
        if (!IsBaseProfileReady || MainProfile is null) return NotInitializedResponse;

        if (string.IsNullOrWhiteSpace(path)) return BadRequest(new
        {
            errorCode = "cloud.dependencies.no_path",
            errorMessage = "No folder supplied",
            numericErrorCode = 1004
        });

        var folder = ToGameFolderPath(path, MainProfile.Provider);
        if (folder is null) return NotFoundResponse;

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectFolderPaths(MainProfile.Provider, folder, paths, seen);

        foreach (var profile in SecondaryBaseProfiles)
        {
            if (!profile.IsInitialized) continue;

            CollectFolderPaths(profile.Provider, ToGameFolderPath(path, profile.Provider) ?? folder, paths, seen);
        }

        if (paths.Count == 0) return NotFoundResponse;

        paths.Sort(StringComparer.OrdinalIgnoreCase);

        var packages = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();

        foreach (var packagePath in paths)
        {
            var profile = FindBaseProfileForPath(packagePath, found: out var found);

            if (!found || !profile.Provider.TryLoadPackage(packagePath, out var package))
            {
                failed.Add(packagePath);
                continue;
            }

            try
            {
                packages[package.Name] = ImportedPackageNames(package);
            }
            catch (Exception e)
            {
                Log.Warning(e, "[Core.Cloud]: could not read the imports of {Path}", packagePath);
                failed.Add(packagePath);
            }
        }

        return new JsonResult(new
        {
            path = folder,
            count = packages.Count,
            packages,
            failed
        });
    }

    /* The packages one package imports, by name, itself and duplicates left out */
    private static List<string> ImportedPackageNames(IPackage package)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        switch (package)
        {
            /* IoStore: the header names the packages it imports outright */
            case IoPackage io:
                foreach (var imported in io.ImportedPackages.Value)
                {
                    if (imported is not null) names.Add(imported.Name);
                }
                break;

            /* Pak: a package import is an import of class Package with nothing outside it */
            case Package pak:
                foreach (var import in pak.ImportMap)
                {
                    if (import.ClassName.Text == "Package" && (import.OuterIndex is null || import.OuterIndex.IsNull))
                    {
                        names.Add(import.ObjectName.Text);
                    }
                }
                break;
        }

        names.Remove(package.Name);

        var sorted = names.ToList();
        sorted.Sort(StringComparer.OrdinalIgnoreCase);

        return sorted;
    }
}
