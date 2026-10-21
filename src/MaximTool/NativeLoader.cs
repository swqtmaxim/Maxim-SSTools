using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Serilog;

internal static class NativeLoader {
  private static readonly ILogger Logger = Log.ForContext(typeof(NativeLoader));

  private const string ResourceName = "MaximTool.Native.WebView2Loader.dll";

  private const string WebView2Version = "1.0.2535.41";

  private const string DllFileName = "WebView2Loader.dll";

  private static int m_initialized;

  private static void CleanupOldVersions(string currentDir) {
    try {
      string? parent = Path.GetDirectoryName(currentDir);
      if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        return;
      foreach (string dir in Directory.GetDirectories(parent)) {
        if (!string.Equals(dir, currentDir, StringComparison.OrdinalIgnoreCase)) {
          try {
            Directory.Delete(dir, recursive: true);
          } catch { }
        }
      }
    } catch {
    }
  }

  public static void EnsureWebView2Loader() {
    if (Interlocked.Exchange(ref m_initialized, 1) == 1)
      return;

    try {
      string dir = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "Maxim-Screenshare-Tool", "native", WebView2Version);
      string target = Path.Combine(dir, DllFileName);

      byte[]? payload = LoadResource();
      if (payload is null || payload.Length == 0)
        return;

      bool extracted = File.Exists(target) && MatchesHash(target, payload);
      if (!extracted) {
        try {
          Directory.CreateDirectory(dir);
        } catch { return; }

        try {
          string scratch = target + ".tmp";
          File.WriteAllBytes(scratch, payload);
          try {
            File.Move(scratch, target, overwrite: true);
          } catch {
            if (File.Exists(target) && MatchesHash(target, payload)) {
              try {
                File.Delete(scratch);
              } catch { }
            } else {
              try {
                File.Move(scratch, target, overwrite: false);
              } catch { }
            }
          }
          extracted = File.Exists(target) && MatchesHash(target, payload);
        } catch {
          extracted = File.Exists(target);
        }

        if (extracted)
          CleanupOldVersions(dir);
      }

      if (!extracted)
        return;

      try {
        NativeLibrary.Load(target);
      } catch (Exception ex) {
        Logger.Debug(ex, "WebView2Loader pre-load failed for {Target}", target);
      }
    } catch {
    }
  }

  private static byte[]? LoadResource() {
    try {
      var asm = Assembly.GetExecutingAssembly();
      using var stream = asm.GetManifestResourceStream(ResourceName);
      if (stream is null)
        return null;
      using var buffer = new MemoryStream();
      stream.CopyTo(buffer);
      return buffer.ToArray();
    } catch {
      return null;
    }
  }

  private static bool MatchesHash(string path, byte[] payload) {
    try {
      using var sha = SHA256.Create();
      byte[] expected = sha.ComputeHash(payload);
      using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
      byte[] actual = sha.ComputeHash(fs);
      return CryptographicOperations.FixedTimeEquals(actual, expected);
    } catch {
      return false;
    }
  }
}
