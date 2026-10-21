using System.IO;
using System.Text.Json.Serialization;

/// <summary>
/// Central Minecraft-directory resolver.
/// Some suspects run Minecraft from a non-default location (other drive, Documents,
/// launcher instances). This module discovers every candidate directory automatically,
/// detects which one was used LAST (via log/options mtime), and lets the operator
/// add/remove paths manually. ShotDesk and AltSweep consume it so screenshots,
/// accounts and mods are found regardless of where the game lives.
/// </summary>
public sealed class McProfileInfo {
  [JsonPropertyName("path")]
  public string Path { get; set; } = "";

  [JsonPropertyName("kind")]
  public string Kind { get; set; } = "VANILLA";

  // VANILLA | TLAUNCHER | LAUNCHER-INSTANCE
  [JsonPropertyName("lastUsed")]
  public string LastUsed { get; set; } = "";

  // yyyy-MM-dd HH:mm:ss, from log/options mtime
  [JsonPropertyName("lastUsedTs")]
  public long LastUsedTs { get; set; } = 0;

  // ticks for sorting
  [JsonPropertyName("source")]
  public string Source { get; set; } = "AUTO";

  // AUTO | MANUAL
  [JsonPropertyName("exists")]
  public bool Exists { get; set; } = true;
}

internal static class McProfiles {
  private static void AddIfDir(List<McProfileInfo> finding, HashSet<string> known, string path, string kind) {
    try {
      if (!Directory.Exists(path))
        return;
      string norm = Path.GetFullPath(path).TrimEnd('\\');
      if (!known.Add(norm))
        return;
      finding.Add(new McProfileInfo { Path = norm, Kind = kind });
    } catch { }
  }

  /// <summary>Add a manual override. Returns { ok, error?, path? }.</summary>
  public static object AddManual(string rawPath) {
    string norm = Normalize(rawPath);
    if (norm.Length == 0)
      return new {
        ok = false, error = "Empty path."
      };
    if (!Directory.Exists(norm))
      return new {
        ok = false, error = "Directory does not exist: " + norm
      };
    var manual = ReadManual();
    foreach (string m in manual)
      if (Normalize(m).Equals(norm, StringComparison.OrdinalIgnoreCase))
        return new {
          ok = true, path = norm, already = true
        };
    manual.Add(norm);
    WriteManual(manual);
    return new {
      ok = true, path = norm
    };
  }

  /* ---------------- discovery ---------------- */

  private static List<McProfileInfo> DiscoverAuto() {
    var finding = new List<McProfileInfo>();
    var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // 1) Known locations under every user profile.
    foreach (string userDir in GetUserDirs()) {
      AddIfDir(finding, known, Path.Combine(userDir, @"AppData\Roaming\.minecraft"), "VANILLA");
      AddIfDir(finding, known, Path.Combine(userDir, @"AppData\Roaming\.tlauncher"), "TLAUNCHER");
      AddIfDir(finding, known, Path.Combine(userDir, @"Documents\.minecraft"), "VANILLA");
      // MultiMC-family launcher instances: instances\<name>\minecraft acts as a .minecraft.
      foreach (string launcher in new[] { "PrismLauncher", "MultiMC", "PolyMC" }) {
        string instRoot = Path.Combine(userDir, @"AppData\Roaming", launcher, "instances");
        try {
          if (!Directory.Exists(instRoot))
            continue;
          foreach (string inst in Directory.GetDirectories(instRoot))
            AddIfDir(finding, known, Path.Combine(inst, "minecraft"), "LAUNCHER-INSTANCE");
        } catch { }
      }
    }

    // 2) Shallow scan of fixed drive roots (depth <= 2) for .minecraft / .tlauncher.
    //    Bounded so it never hangs on giant/network-ish volumes.
    foreach (var drive in DriveInfo.GetDrives()) {
      try {
        if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
          continue;
        int budget = 3000;
        ScanDepth(drive.Name, 0, 2, finding, known, ref budget);
      } catch { }
    }
    return finding;
  }

  /// <summary>Merged list: auto-discovered + manually added paths, newest-used first.</summary>
  public static List<McProfileInfo> GetAll() {
    var found = new Dictionary<string, McProfileInfo>(StringComparer.OrdinalIgnoreCase);
    foreach (var p in DiscoverAuto())
      found[p.Path] = p;
    foreach (string manual in ReadManual()) {
      string norm = Normalize(manual);
      if (norm.Length == 0)
        continue;
      if (found.TryGetValue(norm, out var existing)) {
        existing.Source = "AUTO+MANUAL";
        continue;
      }
      found[norm] = new McProfileInfo {
        Path = norm,
        Kind = GuessKind(norm),
        Source = "MANUAL",
        Exists = Directory.Exists(norm)
      };
    }
    var bucket = found.Values.ToList();
    foreach (var p in bucket)
      StampLastUsed(p);
    bucket.Sort((a, b) => b.LastUsedTs.CompareTo(a.LastUsedTs));
    return bucket;
  }

  private static List<string> GetUserDirs() {
    var dirs = new List<string>();
    foreach (var drive in DriveInfo.GetDrives()) {
      try {
        if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
          continue;
        string users = Path.Combine(drive.Name, "Users");
        if (!Directory.Exists(users))
          continue;
        foreach (var userDir in Directory.GetDirectories(users)) {
          string n = Path.GetFileName(userDir);
          if (n.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
              n.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
              n.Equals("All Users", StringComparison.OrdinalIgnoreCase))
            continue;
          dirs.Add(userDir);
        }
      } catch { }
    }
    return dirs;
  }

  private static string GuessKind(string path) {
    string n = Path.GetFileName(path);
    if (n.Equals(".tlauncher", StringComparison.OrdinalIgnoreCase))
      return "TLAUNCHER";
    if (n.Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
      return "VANILLA";
    return "LAUNCHER-INSTANCE";
  }

  private static string Normalize(string p) {
    try {
      string s = (p ?? "").Trim().Trim('"');
      if (s.Length == 0)
        return "";
      return Path.GetFullPath(s).TrimEnd('\\');
    } catch { return (p ?? "").Trim(); }
  }

  private static void ScanDepth(string dir, int depth, int maxDepth, List<McProfileInfo> finding,
      HashSet<string> known, ref int budget) {
    if (budget <= 0 || depth > maxDepth)
      return;
    string[] children;
    try {
      children = Directory.GetDirectories(dir);
    } catch { return; }
    foreach (string child in children) {
      if (budget <= 0)
        return;
      budget--;
      string name = Path.GetFileName(child);
      if (name.Equals(".minecraft", StringComparison.OrdinalIgnoreCase))
        AddIfDir(finding, known, child, "VANILLA");
      else if (name.Equals(".tlauncher", StringComparison.OrdinalIgnoreCase))
        AddIfDir(finding, known, child, "TLAUNCHER");
      // Do not descend into system / program trees.
      if (depth < maxDepth &&
          !name.Equals("Windows", StringComparison.OrdinalIgnoreCase) &&
          !name.Equals("Program Files", StringComparison.OrdinalIgnoreCase) &&
          !name.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) &&
          !name.Equals("ProgramData", StringComparison.OrdinalIgnoreCase) &&
          !name.Equals("Users", StringComparison.OrdinalIgnoreCase) &&
          !name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) &&
          !name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) &&
          (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) {
        ScanDepth(child, depth + 1, maxDepth, finding, known, ref budget);
      }
    }
  }

  private static void WriteManual(List<string> bucket) {
    var arr = new System.Text.Json.Nodes.JsonArray();
    foreach (string s in bucket)
      arr.Add(s);
    ConfigStore.SetPatch(
        "{\"mcProfiles\":" + arr.ToJsonString() + "}");
  }

  /* ---------------- manual list (config-backed) ---------------- */

  private static List<string> ReadManual() {
    var bucket = new List<string>();
    try {
      var node = System.Text.Json.Nodes.JsonNode.Parse(ConfigStore.GetAll())
          as System.Text.Json.Nodes.JsonObject;
      if (node != null && node["mcProfiles"] is System.Text.Json.Nodes.JsonArray arr)
        foreach (var entry in arr) {
          string? s = entry?.GetValue<string>();
          if (!string.IsNullOrWhiteSpace(s))
            bucket.Add(s);
        }
    } catch { }
    return bucket;
  }

  /// <summary>Newest mtime among latest.log / options.txt (and instance minecraft\ layout).</summary>
  private static void StampLastUsed(McProfileInfo p) {
    if (!p.Exists) {
      p.LastUsed = "";
      p.LastUsedTs = 0;
      return;
    }
    DateTime newest = DateTime.MinValue;
    foreach (string rel in new[]
    {
            @"logs\latest.log",
            "options.txt",
            @"minecraft\logs\latest.log",
            @"minecraft\options.txt"
        }) {
      try {
        string f = Path.Combine(p.Path, rel);
        if (!File.Exists(f))
          continue;
        var t = File.GetLastWriteTimeUtc(f);
        if (t > newest)
          newest = t;
      } catch { }
    }
    if (newest == DateTime.MinValue) {
      p.LastUsed = "";
      p.LastUsedTs = 0;
      return;
    }
    var local = newest.ToLocalTime();
    p.LastUsed = local.ToString("yyyy-MM-dd HH:mm:ss");
    p.LastUsedTs = local.Ticks;
  }

  /// <summary>Remove a manual override (auto dirs cannot be removed).</summary>
  public static object RemoveManual(string rawPath) {
    string norm = Normalize(rawPath);
    var manual = ReadManual();
    int before = manual.Count;
    manual.RemoveAll(m => Normalize(m).Equals(norm, StringComparison.OrdinalIgnoreCase));
    if (manual.Count == before)
      return new {
        ok = false, error = "Not a manual entry."
      };
    WriteManual(manual);
    return new {
      ok = true
    };
  }

  /// <summary>Screenshot directories of every existing profile (root + instance layout).</summary>
  public static List<string> ScreenshotDirs() {
    var dirs = new List<string>();
    foreach (var p in GetAll()) {
      if (!p.Exists)
        continue;
      dirs.Add(Path.Combine(p.Path, "screenshots"));
      dirs.Add(Path.Combine(p.Path, "minecraft", "screenshots"));
    }
    return dirs;
  }

  /// <summary>Mods directories of every existing profile (root + instance layout).</summary>
  public static List<string> ModsDirs() {
    var dirs = new List<string>();
    foreach (var p in GetAll()) {
      if (!p.Exists)
        continue;
      dirs.Add(Path.Combine(p.Path, "mods"));
      dirs.Add(Path.Combine(p.Path, "minecraft", "mods"));
    }
    return dirs;
  }
}
