using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Isolith.Level;

/// <summary>A course together with the resource path it was loaded from.</summary>
public readonly record struct CourseEntry(string Path, Course Course);

/// <summary>
/// Discovers the authored courses under <c>res://courses/</c>.
/// </summary>
/// <remarks>
/// One loader for the title screen, the in-game "next course" action, and the
/// main menu summary. A course that fails to parse is reported and skipped, not
/// fatal: a broken level file must not take the menu down with it.
/// </remarks>
public static class CourseCatalog
{
    public static List<CourseEntry> LoadAll()
    {
        var entries = new List<CourseEntry>();

        using DirAccess? dir = DirAccess.Open("res://courses");
        if (dir is null)
            return entries;

        foreach (string file in dir.GetFiles().Order())
        {
            // Exported projects rename .json to .json.remap; tolerate both.
            string name = file.EndsWith(".remap", StringComparison.Ordinal) ? file[..^6] : file;

            if (!name.EndsWith(".json", StringComparison.Ordinal))
                continue;

            string path = $"res://courses/{name}";

            try
            {
                entries.Add(new CourseEntry(path, Course.Load(path)));
            }
            catch (Exception ex)
            {
                GD.PushWarning($"Isolith: could not load course {name}: {ex.Message}");
            }
        }

        return entries.OrderBy(entry => entry.Course.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The course after <paramref name="currentPath"/>, wrapping at the end.
    /// Returns null when no other course exists.
    /// </summary>
    public static CourseEntry? Next(string currentPath)
    {
        List<CourseEntry> all = LoadAll();

        if (all.Count < 2)
            return null;

        int index = all.FindIndex(entry => entry.Path == currentPath);
        return index < 0 ? all[0] : all[(index + 1) % all.Count];
    }
}
