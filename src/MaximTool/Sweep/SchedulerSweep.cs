using System.Diagnostics;
using System.IO;
using Serilog;

public static class SchedulerSweep {
  private static readonly ILogger Logger = Log.ForContext(typeof(SchedulerSweep));

  private const int TaskCap = 2000;

  private static List<string> ReadTriggers(object def) {
    var bucket = new List<string>();
    dynamic d = def;
    try {
      dynamic? set = TryDyn(() => d.Triggers);
      if (set is null)
        return new List<string> { "NONE" };
      foreach (dynamic tr in set) {
        try {
          string label = TriggerName(DynInt(() => tr.Type));
          if (!DynBool(() => tr.Enabled))
            label += " (OFF)";
          bucket.Add(label);
        } catch { }
      }
    } catch { }
    if (bucket.Count == 0)
      bucket.Add("NONE");
    return bucket;
  }

  private static (string Kind, string Path, string Args) ReadActions(object def) {
    string kind = "NONE", path = "", args = "";
    dynamic d = def;
    try {
      dynamic? set = TryDyn(() => d.Actions);
      if (set is null)
        return (kind, path, args);
      foreach (dynamic a in set) {
        int type = DynInt(() => a.Type);
        if (type == 0) {
          kind = "EXEC";
          path = CleanPath(DynStr(() => a.Path, ""));
          args = DynStr(() => a.Arguments, "");
          break;
        }
        kind = type == 5 ? "COM" : type == 6 ? "EMAIL" : type == 7 ? "MESSAGE" : "T" + type;
      }
    } catch { }
    return (kind, path, args);
  }

  private static string CleanPath(string sourceLine) {
    string s = (sourceLine ?? "").Trim().Trim('"');
    if (s.Length == 0)
      return "";
    try {
      s = Environment.ExpandEnvironmentVariables(s);
    } catch { }
    return s;
  }

  private static bool DynBool(Func<object?> read) {
    try {
      return TryDyn(read) is true;
    } catch { return false; }
  }

  private static DateTime DynDate(Func<object?> read) {
    try {
      return TryDyn(read) is DateTime dt ? dt : default;
    } catch { return default; }
  }

  private static int DynInt(Func<object?> read) {
    try {
      return Convert.ToInt32(TryDyn(read));
    } catch { return 0; }
  }

  private static string DynStr(Func<object?> read, string fallback = "") {
    try {
      return TryDyn(read)?.ToString()?.Trim() ?? fallback;
    } catch { return fallback; }
  }

  private static bool IsSystemPath(string path) {
    string win = "", pf = "", pf86 = "";
    try {
      win = ForensicKit.GetWindowsDirectory();
    } catch { }
    try {
      pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    } catch { }
    try {
      pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    } catch { }
    foreach (string root in new[] { win, pf, pf86 }) {
      if (string.IsNullOrEmpty(root))
        continue;
      if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
          (path.Length == root.Length || path[root.Length] is '\\' or '/'))
        return true;
    }
    return false;
  }

  private static DateTime ParseStamp(string sourceLine) {
    try {
      if (DateTime.TryParse(sourceLine, out DateTime dt))
        return dt.ToLocalTime();
    } catch { }
    return default;
  }

  private static SchedulerEntry ReadOne(dynamic task, string folderPath, DateTime bootLocal) {
    string name = DynStr(() => task.Name, "?");
    string full = DynStr(() => task.Path, folderPath.TrimEnd('\\') + "\\" + name);
    bool enabled = DynBool(() => task.Enabled);
    string state = TaskState(DynInt(() => task.State));

    DateTime lastRun = DynDate(() => task.LastRunTime);

    dynamic? def = TryDyn(() => task.Definition);
    if (def is null)
      throw new InvalidOperationException("No definition for " + name);

    string author = DynStr(() => def.RegistrationInfo.Author, "");
    DateTime created = ParseStamp(DynStr(() => def.RegistrationInfo.Date, ""));
    string user = DynStr(() => def.Principal.UserId, "");
    string level = DynInt(() => def.Principal.RunLevel) == 1 ? "HIGHEST" : "LIMITED";

    var triggers = ReadTriggers((object)def);
    var act = ReadActions((object)def);
    string kind = act.Kind, action = act.Path, args = act.Args;

    bool bootLogon = triggers.Contains("BOOT") || triggers.Contains("LOGON");
    bool nonSystem = kind == "EXEC" && !string.IsNullOrEmpty(action) && !IsSystemPath(action);
    bool missing = kind == "EXEC" && !string.IsNullOrEmpty(action) &&
        Path.IsPathRooted(action) && !File.Exists(action);
    bool hidden = kind != "EXEC";
    bool fresh = created.Year >= 2000 && created >= bootLocal;

    var reasons = new List<string>();
    if (bootLogon)
      reasons.Add("FIRE AT BOOT/LOGON");
    if (nonSystem)
      reasons.Add("NON-SYSTEM PATH");
    if (missing)
      reasons.Add("MISSING FILE");
    if (hidden)
      reasons.Add("HIDDEN HANDLER (" + kind + ")");
    if (fresh)
      reasons.Add("CREATED THIS SESSION");
    if (level == "HIGHEST" && (nonSystem || hidden))
      reasons.Add("RUNS ELEVATED");
    if (!enabled)
      reasons.Add("DISABLED");

    string severity = ((bootLogon && nonSystem) || (fresh && nonSystem) || (bootLogon && hidden)) ? "bad"
        : (nonSystem || missing || fresh || hidden) ? "warn" : "ok";

    return new SchedulerEntry {
      Name = name,
      Folder = folderPath,
      Enabled = enabled,
      State = state,
      Triggers = triggers,
      Action = kind == "EXEC" ? action : kind,
      Args = args,
      Author = string.IsNullOrEmpty(author) ? user : author,
      Created = created.Year >= 2000 ? created.ToString("yyyy-MM-dd HH:mm:ss") : "",
      CreatedUnix = created.Year >= 2000
            ? created.Ticks / TimeSpan.TicksPerSecond - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond : 0,
      LastRun = lastRun.Year >= 2000 ? lastRun.ToString("yyyy-MM-dd HH:mm:ss") : "",
      RunLevel = level,
      Missing = missing,
      Reasons = reasons,
      Severity = severity
    };
  }

  public static Task<SchedulerReport> RunAsync(CancellationToken ct = default)
      => Task.Run(() => RunCore(ct), ct);

  private static SchedulerReport RunCore(CancellationToken ct) {
    var finding = new SchedulerReport { Admin = ForensicKit.IsAdministrator() };
    DateTime bootLocal = ForensicKit.GetBootTimeLocal();
    try {
      Type? comType = Type.GetTypeFromProgID("Schedule.Service");
      if (comType is null) {
        finding.Error = "Task Scheduler COM unavailable.";
        return finding;
      }
      dynamic service = Activator.CreateInstance(comType)!;
      try {
        service.Connect();
        dynamic root = service.GetFolder("\\");
        WalkFolder(root, "\\", finding, bootLocal, ct);
      } finally {
        try {
          System.Runtime.InteropServices.Marshal.ReleaseComObject(service);
        } catch { }
      }
    } catch (OperationCanceledException) {
      throw;
    } catch (Exception ex) {
      Logger.Error(ex, "Scheduler sweep failed");
      finding.Error = "Check failed: " + ex.Message;
      return finding;
    }
    finding.TaskCount = finding.Entries.Count;
    finding.FindingCount = finding.Entries.Count(e => e.Severity == "bad");
    return finding;
  }

  private static string TaskState(int state) => state switch {
    1 => "DISABLED",
    2 => "QUEUED",
    3 => "READY",
    4 => "RUNNING",
    _ => "UNKNOWN"
  };

  private static string TriggerName(int type) => type switch {
    0 => "EVENT",
    1 => "TIME",
    2 => "DAILY",
    3 => "WEEKLY",
    4 => "MONTHLY",
    5 => "MONTHLYDOW",
    6 => "IDLE",
    7 => "REGISTRATION",
    8 => "BOOT",
    9 => "LOGON",
    11 => "SESSION",
    _ => "T" + type
  };

  private static object? TryDyn(Func<object?> read) {
    try {
      return read();
    } catch { return null; }
  }

  private static void WalkFolder(
      dynamic folder, string folderPath, SchedulerReport finding, DateTime bootLocal, CancellationToken ct) {
    dynamic? taskSet = TryDyn(() => folder.GetTasks(0));
    if (taskSet is null) {
      finding.Skipped++;
      return;
    }

    foreach (dynamic t in taskSet) {
      ct.ThrowIfCancellationRequested();
      if (finding.Entries.Count + finding.Skipped >= TaskCap)
        return;
      try {
        finding.Entries.Add(ReadOne(t, folderPath, bootLocal));
      } catch (OperationCanceledException) { throw; } catch { finding.Skipped++; }
    }

    dynamic? subsets = TryDyn(() => folder.GetFolders(0));
    if (subsets is null)
      return;

    foreach (dynamic sub in subsets) {
      ct.ThrowIfCancellationRequested();
      if (finding.Entries.Count + finding.Skipped >= TaskCap)
        return;
      try {
        string subPath = DynStr(() => sub.Path, folderPath.TrimEnd('\\') + "\\?");
        WalkFolder(sub, subPath, finding, bootLocal, ct);
      } catch (OperationCanceledException) { throw; } catch { finding.Skipped++; }
    }
  }
}
