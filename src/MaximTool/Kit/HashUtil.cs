using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

public sealed class FileHashResult {
  [JsonPropertyName("path")]
  public string Path { get; set; } = "";

  [JsonPropertyName("sha256")]
  public string Sha256 { get; set; } = "";

  [JsonPropertyName("size")]
  public long Size {
    get; set;
  }

  [JsonPropertyName("error")]
  public string? Error {
    get; set;
  }
}

internal static class HashUtil {
  private const int PathCap = 100;

  private const long SizeCap = 100L * 1024 * 1024;

  public static List<FileHashResult> HashPaths(List<string> paths, CancellationToken ct) {
    var collected = new List<FileHashResult>();
    var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string sourceLine in paths) {
      ct.ThrowIfCancellationRequested();
      if (collected.Count >= PathCap)
        break;
      if (string.IsNullOrWhiteSpace(sourceLine) || !known.Add(sourceLine))
        continue;
      var entry = new FileHashResult { Path = sourceLine };
      try {
        var fi = new FileInfo(sourceLine);
        if (!fi.Exists) {
          entry.Error = "missing";
        } else if (fi.Length > SizeCap || fi.Length <= 0) {
          entry.Error = "skipped-size";
        } else {
          entry.Size = fi.Length;
          using var fs = new FileStream(sourceLine, FileMode.Open, FileAccess.Read,
              FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
          using var sha = SHA256.Create();
          byte[] hash = sha.ComputeHash(fs);
          entry.Sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        }
      } catch (Exception ex) {
        entry.Error = ex.GetType().Name;
      }
      collected.Add(entry);
    }
    return collected;
  }
}
