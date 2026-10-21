using System.IO;
using Serilog;

public static class LeftoverSweep {
  private static readonly ILogger Logger = Log.ForContext(typeof(LeftoverSweep));

  private const int TempCap = 200;

  private const int LinkCap = 500;

  private const int DumpCap = 200;

  private const int HitCap = 100;

  private static readonly string[] BadKeywords =
  {
        "mimikatz", "sekurlsa", "lsassy", "procdump", "safetykatz",
        "encodedcommand", "downloadstring", "invoke-mimikatz"
    };

  private static readonly string[] WarnKeywords =
  {
        "bypass", "-hidden", "-enc", "frombase64", "invoke-expression"
    };

  private static void CollectDumps(
      string userDir, LeftoverReport finding, DateTime bootLocal, CancellationToken ct) {
    string dir = Path.Combine(userDir, @"AppData\Local\CrashDumps");
    string[] dumps;
    try {
      if (!Directory.Exists(dir))
        return;
      dumps = Directory.GetFiles(dir, "*.dmp", SearchOption.TopDirectoryOnly);
    } catch { return; }

    foreach (string d in dumps) {
      ct.ThrowIfCancellationRequested();
      if (finding.Dumps.Count >= DumpCap)
        break;
      FileInfo fi;
      DateTime m;
      try {
        fi = new FileInfo(d);
        m = fi.LastWriteTime;
      } catch { continue; }
      bool session = m >= bootLocal;
      finding.Dumps.Add(new LeftoverFile {
        Name = fi.Name,
        Path = fi.FullName,
        SizeLabel = AuditFormat.FormatBytes(fi.Length),
        Modified = m.ToString("yyyy-MM-dd HH:mm:ss"),
        Kind = "DMP",
        Note = session ? "DUMPED THIS SESSION" : "CRASH ARTIFACT",
        Severity = session ? "warn" : "ok"
      });
    }
  }

  private static void CollectHistory(string userDir, LeftoverReport finding, CancellationToken ct) {
    string path = Path.Combine(userDir,
        @"AppData\Roaming\Microsoft\Windows\PowerShell\PSReadline\ConsoleHost_history.txt");
    string[] lines;
    try {
      if (!File.Exists(path))
        return;
      lines = File.ReadAllLines(path);
    } catch { return; }

    foreach (string sourceLine in lines) {
      ct.ThrowIfCancellationRequested();
      if (finding.History.Count >= HitCap)
        break;
      string line = (sourceLine ?? "").Trim();
      if (line.Length == 0)
        continue;
      string? hit = MatchKeyword(line);
      if (hit is null)
        continue;
      finding.History.Add(new HistoryHit {
        File = path,
        Preview = (hit + " :: " + (line.Length > 160 ? line.Substring(0, 160) : line)).Trim(),
        Severity = IsBadKeyword(hit) ? "bad" : "warn"
      });
    }
  }

  private static void CollectHosts(
      string winDir, LeftoverReport finding, DateTime bootLocal, CancellationToken ct) {
    if (string.IsNullOrEmpty(winDir))
      return;
    string path = Path.Combine(winDir, @"System32\drivers\etc\hosts");
    string[] lines;
    try {
      if (!File.Exists(path))
        return;
      finding.HostsModified = File.GetLastWriteTime(path) >= bootLocal;
      lines = File.ReadAllLines(path);
    } catch { return; }

    foreach (string sourceLine in lines) {
      ct.ThrowIfCancellationRequested();
      string line = sourceLine.Trim();
      if (line.Length == 0 || line.StartsWith("#"))
        continue;
      int hash = line.IndexOf('#');
      if (hash >= 0)
        line = line.Substring(0, hash).Trim();
      string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length < 2)
        continue;
      string ip = parts[0];
      for (int i = 1; i < parts.Length; i++) {
        bool block = ip.StartsWith("0.") || ip.StartsWith("127.") ||
            ip.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
            ip.StartsWith("::ffff:127.", StringComparison.OrdinalIgnoreCase);
        finding.Hosts.Add(new HostEntry {
          Ip = ip,
          Host = parts[i],
          Severity = block ? "bad" : "warn"
        });
      }
    }
  }

  private static void CollectRecent(string userDir, LeftoverReport finding, CancellationToken ct) {
    string recent = Path.Combine(userDir, @"AppData\Roaming\Microsoft\Windows\Recent");
    string[] links;
    try {
      if (!Directory.Exists(recent))
        return;
      links = Directory.GetFiles(recent, "*.lnk", SearchOption.TopDirectoryOnly);
    } catch { return; }

    dynamic? shell = null;
    try {
      shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
    } catch { }
    if (shell is null)
      return;

    try {
      foreach (string lnk in links) {
        ct.ThrowIfCancellationRequested();
        if (finding.RecentLinks.Count >= LinkCap)
          break;
        DateTime m;
        try {
          m = File.GetLastWriteTime(lnk);
        } catch { continue; }
        string target = "";
        try {
          target = (string?)shell.CreateShortcut(lnk).TargetPath ?? "";
        } catch { }
        target = target.Trim();
        if (target.Length == 0)
          continue;

        string verdict = JudgeTarget(target, out string note);
        finding.RecentLinks.Add(new LeftoverFile {
          Name = Path.GetFileName(lnk),
          Path = lnk,
          SizeLabel = "",
          Modified = m.ToString("yyyy-MM-dd HH:mm:ss"),
          Kind = "LNK",
          Note = note + " → " + (target.Length > 90 ? target.Substring(0, 90) : target),
          Severity = verdict
        });
      }
    } finally {
      try {
        System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
      } catch { }
    }
    finding.RecentLinks.Sort((a, b) => string.Compare(b.Modified, a.Modified, StringComparison.Ordinal));
  }

  private static void CollectTemp(
      string dir, string label, LeftoverReport finding, DateTime bootLocal, CancellationToken ct) {
    string[] files;
    try {
      if (!Directory.Exists(dir))
        return;
      files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly);
    } catch { return; }

    var rows = new List<(DateTime M, FileInfo Fi)>();
    foreach (string f in files) {
      ct.ThrowIfCancellationRequested();
      if (rows.Count >= 500)
        break;
      FileInfo fi;
      try {
        fi = new FileInfo(f);
      } catch { continue; }
      DateTime m;
      try {
        m = fi.LastWriteTime;
      } catch { continue; }
      rows.Add((m, fi));
    }
    rows.Sort((a, b) => b.M.CompareTo(a.M));

    foreach (var (m, fi) in rows) {
      if (finding.TempFiles.Count >= TempCap)
        break;
      string ext = "";
      try {
        ext = fi.Extension;
      } catch { }
      bool exec = ExecExts.Contains(ext);
      bool session = m >= bootLocal;
      if (!exec && !session)
        continue;
      finding.TempFiles.Add(new LeftoverFile {
        Name = label + " · " + fi.Name,
        Path = fi.FullName,
        SizeLabel = AuditFormat.FormatBytes(fi.Length),
        Modified = m.ToString("yyyy-MM-dd HH:mm:ss"),
        Kind = exec ? "EXEC" : "FILE",
        Note = exec ? ext.ToUpperInvariant() + " IN TEMP" : "WRITTEN THIS SESSION",
        Severity = exec ? (session ? "bad" : "warn") : "warn"
      });
    }
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

  private static bool IsBadKeyword(string keyword) {
    foreach (string k in BadKeywords)
      if (string.Equals(k, keyword, StringComparison.OrdinalIgnoreCase))
        return true;
    return false;
  }

  private static string JudgeTarget(string target, out string note) {
    string lower = target.ToLowerInvariant();
    bool riskyDir = lower.Contains(@"\temp\") || lower.Contains(@"\downloads\") ||
        lower.Contains(@"\appdata\local\temp\");
    string ext = "";
    try {
      ext = Path.GetExtension(target);
    } catch { }

    if (!File.Exists(target) && !Directory.Exists(target)) {
      note = "TARGET MISSING";
      return "warn";
    }
    if (ExecExts.Contains(ext)) {
      note = riskyDir ? "EXEC FROM RISKY DIR" : "EXECUTABLE";
      return riskyDir ? "warn" : "ok";
    }
    if (DocExts.Contains(ext)) {
      note = "DOCUMENT";
      return "ok";
    }
    note = "ODD TARGET";
    return "warn";
  }

  private static string? MatchKeyword(string line) {
    foreach (string k in BadKeywords)
      if (line.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
        return k.ToUpperInvariant();
    foreach (string k in WarnKeywords)
      if (line.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
        return k.ToUpperInvariant();
    return null;
  }

  public static Task<LeftoverReport> RunAsync(CancellationToken ct = default)
      => Task.Run(() => RunCore(ct), ct);

  private static LeftoverReport RunCore(CancellationToken ct) {
    var finding = new LeftoverReport { Admin = ForensicKit.IsAdministrator() };
    DateTime bootLocal = ForensicKit.GetBootTimeLocal();
    try {
      string winDir = "";
      try {
        winDir = ForensicKit.GetWindowsDirectory();
      } catch { }
      if (!string.IsNullOrEmpty(winDir))
        CollectTemp(Path.Combine(winDir, "Temp"), "WIN-TEMP", finding, bootLocal, ct);

      foreach (string userDir in GetUserDirs()) {
        ct.ThrowIfCancellationRequested();
        CollectTemp(Path.Combine(userDir, @"AppData\Local\Temp"), "USER-TEMP", finding, bootLocal, ct);
        CollectRecent(userDir, finding, ct);
        CollectDumps(userDir, finding, bootLocal, ct);
        CollectHistory(userDir, finding, ct);
      }

      CollectHosts(winDir, finding, bootLocal, ct);
    } catch (OperationCanceledException) {
      throw;
    } catch (Exception ex) {
      Logger.Error(ex, "Leftover sweep failed");
      finding.Error = "Check failed: " + ex.Message;
      return finding;
    }

    finding.FindingCount =
        finding.TempFiles.Count(f => f.Severity == "bad") +
        finding.RecentLinks.Count(f => f.Severity == "bad") +
        finding.Dumps.Count(f => f.Severity == "bad") +
        finding.Hosts.Count(h => h.Severity == "bad") +
        finding.History.Count(h => h.Severity == "bad");
    return finding;
  }

  private static readonly HashSet<string> ExecExts = new(StringComparer.OrdinalIgnoreCase)
  {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js",
        ".jar", ".msi", ".com", ".scr", ".pif"
    };

  private static readonly HashSet<string> DocExts = new(StringComparer.OrdinalIgnoreCase)
  {
        ".txt", ".log", ".ini", ".cfg", ".json", ".xml",
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".bmp",
        ".mp3", ".mp4", ".avi", ".mkv",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".zip", ".rar", ".7z"
    };
}
