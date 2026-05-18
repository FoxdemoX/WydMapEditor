namespace WydFormats;

/// <summary>
/// Reads MeshList.txt — the WYD file that maps object/mesh index to .msa file path.
///
/// Format (text, one entry per line):
///   index path\to\file.msa
///   e.g.   143 mesh\wall43.msa
///
/// The index is the same value stored in DatRecord.ObjType.
/// Paths use backslashes; this reader normalises them to the platform separator.
/// </summary>
public static class MeshListReader
{
    /// <summary>
    /// Loads MeshList.txt and returns a dictionary mapping ObjType → relative mesh path.
    /// The path is relative to the game root folder (e.g. "Mesh\wall43.msa").
    /// </summary>
    public static Dictionary<int, string> Load(string meshListPath)
    {
        var map = new Dictionary<int, string>();
        if (!File.Exists(meshListPath)) return map;

        try
        {
            foreach (var line in File.ReadLines(meshListPath))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Split on first whitespace; rest is filename (may contain spaces)
                int sep = -1;
                for (int i = 0; i < trimmed.Length; i++)
                {
                    if (trimmed[i] == ' ' || trimmed[i] == '\t') { sep = i; break; }
                }
                if (sep <= 0) continue;

                string indexStr = trimmed[..sep].Trim();
                string pathStr  = trimmed[(sep + 1)..].Trim();

                if (!int.TryParse(indexStr, out int idx)) continue;
                if (string.IsNullOrEmpty(pathStr)) continue;

                // Normalise backslashes to platform separator
                pathStr = pathStr.Replace('\\', Path.DirectorySeparatorChar)
                                 .Replace('/',  Path.DirectorySeparatorChar);

                map[idx] = pathStr;
            }
        }
        catch { /* ignore read errors, return partial results */ }

        return map;
    }

    /// <summary>
    /// Searches common locations for MeshList.txt starting from a known game folder.
    /// Returns null if not found.
    /// </summary>
    public static string? FindMeshList(string gameFolder)
    {
        var candidates = new[]
        {
            Path.Combine(gameFolder, "Mesh", "MeshList.txt"),
            Path.Combine(gameFolder, "mesh", "MeshList.txt"),
            Path.Combine(gameFolder, "UI", "MeshList.txt"),
            Path.Combine(gameFolder, "ui", "MeshList.txt"),
            Path.Combine(gameFolder, "MeshList.txt"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    /// <summary>
    /// Searches common locations for CommonMeshList.txt starting from a known game folder.
    /// Returns null if not found.
    /// </summary>
    public static string? FindCommonMeshList(string gameFolder)
    {
        var candidates = new[]
        {
            Path.Combine(gameFolder, "Mesh", "CommonMeshList.txt"),
            Path.Combine(gameFolder, "mesh", "CommonMeshList.txt"),
            Path.Combine(gameFolder, "CommonMeshList.txt"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    /// <summary>
    /// Loads MeshList.txt AND CommonMeshList.txt from the given game folder,
    /// merging both into one dictionary.
    /// MeshList.txt entries take precedence over CommonMeshList.txt for duplicate keys.
    /// If primaryMeshListPath is null, FindMeshList is used to locate it automatically.
    /// </summary>
    public static Dictionary<int, string> LoadMerged(string gameFolder,
                                                      string? primaryMeshListPath = null)
    {
        // Load CommonMeshList first (lower priority)
        var merged = new Dictionary<int, string>();
        string? commonPath = FindCommonMeshList(gameFolder);
        if (commonPath != null)
            foreach (var kv in Load(commonPath))
                merged[kv.Key] = kv.Value;

        // Load MeshList (higher priority — overwrites duplicates)
        string? mainPath = primaryMeshListPath ?? FindMeshList(gameFolder);
        if (mainPath != null)
            foreach (var kv in Load(mainPath))
                merged[kv.Key] = kv.Value;

        return merged;
    }

    /// <summary>
    /// Given the game root folder and a relative path from MeshList.txt,
    /// returns the full absolute path to the .msa file.
    /// </summary>
    public static string ResolvePath(string gameFolder, string relativePath)
        => Path.Combine(gameFolder, relativePath);
}
