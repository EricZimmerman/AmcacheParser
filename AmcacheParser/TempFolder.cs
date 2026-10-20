using System;
using System.IO;
using System.Linq;

namespace AmcacheParser;

/// <summary>
///     Where the temporary copies of the databases go (they are opened instead of the evidence, and each is deleted when
///     done): the folder given with --temp, or a Temp folder next to AmcacheParser. Nothing goes to the user's %TEMP% (Eric,
///     2026-10-20: the tools write nothing outside their folder). If the folder can't be written to, AmcacheParser stops and
///     asks for --temp instead of picking another place
/// </summary>
internal static class TempFolder
{
    public const string Help =
        "Folder for the temporary copies of the SQLite databases, which are opened instead of the evidence and deleted when done. " +
        "Default: a Temp folder next to AmcacheParser. A folder that isn't there is created, and removed again when done. " +
        "If that folder can't be written to, AmcacheParser stops: give a writable folder with --temp";

    /// <summary>The default: Temp next to the program</summary>
    public static string Default => Path.Combine(AppContext.BaseDirectory, "Temp");

    /// <summary>
    ///     Creates <paramref name="given" /> (or the default) if needed and checks that it can be written to. Returns the
    ///     full path, or null with the reason in <paramref name="error" />
    /// </summary>
    public static string Prepare(string given, out bool created, out string error)
    {
        var folder = string.IsNullOrEmpty(given) ? Default : Path.GetFullPath(given);
        created = Directory.Exists(folder) == false;
        error = null;

        try
        {
            Directory.CreateDirectory(folder);

            var probe = Path.Combine(folder, $"AmcacheParser-write-test-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);

            return folder;
        }
        catch (Exception e)
        {
            error = $"{folder}: {e.Message}";
            return null;
        }
    }

    /// <summary>Removes the folder if this run created it and it is empty (another run may still be using it)</summary>
    public static void RemoveIfEmpty(string folder, bool created)
    {
        if (created == false || folder == null)
        {
            return;
        }

        try
        {
            if (Directory.EnumerateFileSystemEntries(folder).Any() == false)
            {
                Directory.Delete(folder, false);
            }
        }
        catch (Exception)
        {
            //another run put something there meanwhile, or it is gone already
        }
    }
}
