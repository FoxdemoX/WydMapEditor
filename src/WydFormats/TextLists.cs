using System.Text;

namespace WydFormats;

public static class TextLists
{
    public static Dictionary<int, string> LoadIdToName(string path)
    {
        var map = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return map;

        using var sr = new StreamReader(path, Encoding.UTF8, true);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            if (!int.TryParse(parts[0], out var id))
                continue;

            map[id] = parts[1];
        }

        return map;
    }

    /// <summary>
    /// Loads MeshList.txt and returns id → relative path (e.g. "Mesh/building01.msa").
    /// Use GetMeshDisplayName() to get the short display name from the path.
    /// </summary>
    public static Dictionary<int, string> LoadMeshList(string path)
    {
        var map = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return map;

        using var sr = new StreamReader(path, Encoding.UTF8, true);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Lines starting with // or # are comments
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//") || trimmed.StartsWith("#"))
                continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            if (!int.TryParse(parts[0], out var id))
                continue;

            // Store the relative path normalized to forward slashes
            // e.g. "Mesh\building01.msa" -> "Mesh/building01.msa"
            string relPath = parts[1].Replace("\\", "/");
            map[id] = relPath;
        }

        return map;
    }

    /// <summary>Returns just the filename without extension for display purposes.</summary>
    public static string GetMeshDisplayName(string meshPath)
        => Path.GetFileNameWithoutExtension(meshPath);
}
