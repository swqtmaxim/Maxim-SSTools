using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ConfigStore {
  private static readonly object Gate = new();

  public static string ConfigPath => Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "Maxim-Screenshare-Tool", "config.json");

  public static string DefaultCaseDir => Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
      "Maxim-Screenshare-Tool", "cases");

  public static string? BrowseFolder() {
    try {
      using var dlg = new System.Windows.Forms.FolderBrowserDialog {
        Description = "Case directory for reports and case files",
        UseDescriptionForTitle = true,
        ShowNewFolderButton = true
      };
      return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
    } catch {
      return null;
    }
  }

  public static string EffectiveCaseDir() {
    try {
      if (JsonNode.Parse(GetAll()) is JsonObject cfg &&
          cfg["caseDir"]?.GetValue<string>() is string custom &&
          !string.IsNullOrWhiteSpace(custom))
        return custom;
    } catch { }
    return DefaultCaseDir;
  }

  public static string GetAll() {
    lock (Gate) {
      try {
        if (File.Exists(ConfigPath))
          return File.ReadAllText(ConfigPath);
      } catch { }
      return "{}";
    }
  }

  public static string OpenReport(string path) {
    string full = ResolveCaseFile(path);
    Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
    return full;
  }

  public static string ReadCaseFile(string name) {
    string full = ResolveCaseFile(name);
    if (!File.Exists(full))
      throw new FileNotFoundException("File not found.");
    var fi = new FileInfo(full);
    if (fi.Length > 25 * 1024 * 1024)
      throw new InvalidDataException("File too large.");
    return File.ReadAllText(full);
  }

  private static string ResolveCaseFile(string path) {
    if (string.IsNullOrWhiteSpace(path))
      throw new FileNotFoundException("File not found.");
    string dir = Path.GetFullPath(EffectiveCaseDir());
    string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(dir, path));
    if (!full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
      throw new UnauthorizedAccessException("Outside case directory.");
    return full;
  }

  public static string SaveReport(string html, string name) {
    if (string.IsNullOrEmpty(html) || html.Length > 25 * 1024 * 1024)
      throw new InvalidDataException("Empty or oversized report.");
    string base_ = string.IsNullOrWhiteSpace(name) ? "maxim-case.html" : name;
    string safe = string.Concat(base_.Split(Path.GetInvalidFileNameChars())).Replace("..", "");
    if (safe.Length == 0)
      safe = "maxim-case.html";
    bool wantJson = safe.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    if (!wantJson && !safe.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
      safe += ".html";
    string dir = EffectiveCaseDir();
    Directory.CreateDirectory(dir);
    string stamped = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + safe;
    string full = Path.Combine(dir, stamped);
    File.WriteAllText(full, html);
    return full;
  }

  public static string SetPatch(string patchJson) {
    lock (Gate) {
      JsonObject merged;
      try {
        merged = JsonNode.Parse(GetAll()) as JsonObject ?? new JsonObject();
      } catch {
        merged = new JsonObject();
      }
      try {
        if (JsonNode.Parse(patchJson) is JsonObject patch) {
          foreach (var kv in patch)
            merged[kv.Key] = kv.Value?.DeepClone();
        }
      } catch (Exception ex) {
        return "{\"ok\":false,\"error\":\"" + JsonEncodedText.Encode(ex.Message) + "\"}";
      }
      try {
        string? dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir))
          Directory.CreateDirectory(dir);
        string scratch = ConfigPath + ".tmp";
        File.WriteAllText(scratch, merged.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(scratch, ConfigPath, overwrite: true);
      } catch (Exception ex) {
        return "{\"ok\":false,\"error\":\"" + JsonEncodedText.Encode(ex.Message) + "\"}";
      }
      return "{\"ok\":true}";
    }
  }
}
