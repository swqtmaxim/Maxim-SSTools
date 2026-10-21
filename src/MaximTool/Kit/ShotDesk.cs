using System.IO;
using System.Text.Json.Serialization;

public sealed class ScreenshotInfo {
  [JsonPropertyName("path")]
  public string Path { get; set; } = "";

  [JsonPropertyName("modified")]
  public string Modified { get; set; } = "";

  [JsonPropertyName("sizeLabel")]
  public string SizeLabel { get; set; } = "";

  [JsonPropertyName("image")]
  public string? Image {
    get; set;
  }
}

internal static class ShotDesk {
  private const long EmbedCap = 3L * 1024 * 1024;

  private static readonly string[] Exts = { ".png", ".jpg", ".jpeg" };

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

  public static ScreenshotInfo Scan() {
    var best = new ScreenshotInfo();
    DateTime bestTime = default;

    // Candidate folders: every discovered/overridden MC profile + console captures.
    var dirs = new List<string>(McProfiles.ScreenshotDirs());
    foreach (string userDir in GetUserDirs()) {
      dirs.Add(Path.Combine(userDir, "Pictures", "Screenshots"));
      dirs.Add(Path.Combine(userDir, ".lunarclient", "screenshots"));
    }

    foreach (string dir in dirs) {
      string[] files;
      try {
        if (!Directory.Exists(dir))
          continue;
        files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
      } catch { continue; }
      foreach (string f in files) {
        try {
          string ext = Path.GetExtension(f).ToLowerInvariant();
          bool img = false;
          foreach (string e in Exts)
            if (ext == e) {
              img = true;
              break;
            }
          if (!img)
            continue;
          var fi = new FileInfo(f);
          if (fi.LastWriteTime <= bestTime)
            continue;
          bestTime = fi.LastWriteTime;
          best.Path = fi.FullName;
          best.Modified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
          best.SizeLabel = AuditFormat.FormatBytes(fi.Length);
          best.Image = null;
          if (fi.Length > 0 && fi.Length <= EmbedCap) {
            try {
              best.Image = Convert.ToBase64String(File.ReadAllBytes(fi.FullName));
            } catch { best.Image = null; }
          }
        } catch { }
      }
    }
    return best;
  }
}
