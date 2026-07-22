using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace InventoryTools;

// Minimal self-contained localization helper mirroring ECommons.LanguageHelpers:
// same ini format (one entry per line: English key, double-equals separator, then
// the translation; literal \n escapes; ?? positional placeholders) and the same
// .Loc() string extension name.
// InventoryTools does not use ECommons, so we ship this tiny equivalent instead of
// pulling in the full library just for loc. English strings stay in code as keys;
// Traditional Chinese lives in LanguageChineseTraditional.ini next to the plugin dll.
public static class Localization
{
    private static readonly Dictionary<string, string> Translations = new();

    public static int EntryCount => Translations.Count;

    public static string? LoadError { get; private set; }

    public static void Init(string? directory)
    {
        Translations.Clear();
        LoadError = null;
        if (directory == null)
        {
            return;
        }
        var path = Path.Combine(directory, "LanguageChineseTraditional.ini");
        try
        {
            if (!File.Exists(path))
            {
                LoadError = $"File not found: {path}";
                return;
            }
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                var idx = line.IndexOf("==", StringComparison.Ordinal);
                if (idx <= 0)
                {
                    continue;
                }
                var key = line[..idx].Replace("\\n", "\n");
                var value = line[(idx + 2)..].TrimEnd('\r').Replace("\\n", "\n");
                Translations[key] = value;
            }
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
    }

    public static string Loc(this string s) => Translations.TryGetValue(s, out var t) ? t : s;

    public static string Loc(this string s, params object?[] args)
    {
        var result = s.Loc();
        foreach (var a in args)
        {
            var idx = result.IndexOf("??", StringComparison.Ordinal);
            if (idx < 0)
            {
                break;
            }
            result = result.Remove(idx, 2).Insert(idx, a?.ToString() ?? "");
        }
        return result;
    }
}
