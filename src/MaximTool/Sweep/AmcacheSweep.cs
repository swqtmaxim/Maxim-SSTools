using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Serilog;

public static class AmcacheSweep {
  private static readonly ILogger Logger = Log.ForContext(typeof(AmcacheSweep));

  private const string MountName = "MaximAmcache";

  private const int KeyCap = 3000;

  private const int EntryCap = 2000;

  [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern int RegQueryInfoKey(
      IntPtr hKey,
      StringBuilder? lpClass,
      ref uint lpcchClass,
      IntPtr lpReserved,
      out uint lpcSubKeys,
      out uint lpcbMaxSubKeyLen,
      out uint lpcbMaxClassLen,
      out uint lpcValues,
      out uint lpcbMaxValueNameLen,
      out uint lpcbMaxValueLen,
      out uint lpcbSecurityDescriptor,
      out long lpftLastWriteTime);

  private static (string? Exposed, string Detail) CreateShadow(string volume) {
    string vol = volume.TrimEnd('\\');
    string? letter = FindFreeLetter();
    if (letter is null)
      return (null, "no free drive letter for shadow mount");
    string script = Path.Combine(Path.GetTempPath(), "MaximShadow.dsh");
    try {
      File.WriteAllLines(script, new[]
      {
                "set context persistent",
                "begin backup",
                "add volume " + vol + " alias cfgvol",
                "create",
                "expose %cfgvol% " + letter + ":",
                "end backup"
            });
      var psi = new ProcessStartInfo("diskshadow", "-s \"" + script + "\"") {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
      };
      using var p = Process.Start(psi);
      if (p is null)
        return (null, "diskshadow would not start");
      if (!p.WaitForExit(240000)) {
        try {
          p.Kill();
        } catch { }
        return (null, "diskshadow timed out after 240s");
      }
      string output = "";
      try {
        output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
      } catch { }
      if (p.ExitCode != 0 || !Directory.Exists(letter + ":\\"))
        return (null, "exit=" + p.ExitCode + " out=" + Snip(output));
      return (letter + ":\\", "");
    } catch (Exception ex) {
      return (null, ex.Message);
    } finally {
      try {
        if (File.Exists(script))
          File.Delete(script);
      } catch { }
    }
  }

  private static AmcacheEntry BuildEntry(string keyName, string path, List<string> pairs, DateTime written) {
    string ext = "";
    try {
      ext = Path.GetExtension(path);
    } catch { }
    bool exec = ExecExts.Contains(ext);
    bool nonSystem = exec && !IsSystemPath(path);
    bool missing = false;
    try {
      missing = !File.Exists(path);
    } catch { }

    var reasons = new List<string>();
    if (nonSystem)
      reasons.Add("NON-SYSTEM PATH");
    if (missing)
      reasons.Add("MISSING FILE");

    string details = string.Join("; ", pairs);
    if (details.Length > 300)
      details = details.Substring(0, 300);

    return new AmcacheEntry {
      File = keyName.Split('\\').Last(),
      Path = path,
      Details = details,
      KeyTime = written.Year >= 2000 ? written.ToString("yyyy-MM-dd HH:mm:ss") : "",
      KeyUnix = written.Year >= 2000
            ? written.Ticks / TimeSpan.TicksPerSecond - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond : 0,
      Missing = missing,
      Reasons = reasons,
      Severity = (nonSystem && missing) ? "bad" : (nonSystem || missing) ? "warn" : "ok"
    };
  }

  private static void DeleteShadow(string exposedRoot) {
    try {
      string letter = exposedRoot.TrimEnd('\\');
      string script = Path.Combine(Path.GetTempPath(), "MaximShadowDel.dsh");
      try {
        File.WriteAllLines(script, new[] { "delete shadows exposed " + letter });
      } catch { return; }
      try {
        var psi = new ProcessStartInfo("diskshadow", "-s \"" + script + "\"") {
          UseShellExecute = false,
          CreateNoWindow = true,
          RedirectStandardOutput = true,
          RedirectStandardError = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(60000);
      } finally {
        try {
          if (File.Exists(script))
            File.Delete(script);
        } catch { }
      }
    } catch { }
  }

  private static bool EnsureServiceRunning(string name, out bool started) {
    started = false;
    try {
      using var sc = new System.ServiceProcess.ServiceController(name);
      try {
        sc.Refresh();
      } catch { }
      if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
        return true;
      try {
        if (sc.StartType == System.ServiceProcess.ServiceStartMode.Disabled)
          return false;
      } catch { }
      try {
        sc.Start();
      } catch { return false; }
      try {
        sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running,
            TimeSpan.FromSeconds(30));
      } catch { return false; }
      try {
        sc.Refresh();
      } catch { }
      if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
        return false;
      started = true;
      return true;
    } catch {
      return false;
    }
  }

  private static bool EnsureShadowInfra(out bool touchedVss, out bool touchedSwprv) {
    touchedVss = false;
    touchedSwprv = false;
    bool vssOk = EnsureServiceRunning("VSS", out touchedVss);
    bool swprvOk = EnsureServiceRunning("swprv", out touchedSwprv);
    return vssOk && swprvOk;
  }

  private static string? FindFreeLetter() {
    try {
      var used = new HashSet<char>();
      foreach (var d in DriveInfo.GetDrives()) {
        try {
          if (!string.IsNullOrEmpty(d.Name) && d.Name.Length >= 1)
            used.Add(char.ToUpperInvariant(d.Name[0]));
        } catch { }
      }
      for (char c = 'Z'; c >= 'Q'; c--)
        if (!used.Contains(c))
          return c.ToString();
    } catch { }
    return null;
  }

  private static bool IsPathValue(string s) {
    if (s.Length < 5 || s.Length > 400)
      return false;
    if (s[1] != ':' || (s[2] != '\\' && s[2] != '/'))
      return false;
    return char.IsLetter(s[0]);
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

  private static DateTime KeyWriteTime(RegistryKey key) {
    try {
      IntPtr h = key.Handle.DangerousGetHandle();
      uint classLen = 0;
      int rc = RegQueryInfoKey(h, null, ref classLen, IntPtr.Zero,
          out _, out _, out _, out _, out _, out _, out _, out long ft);
      if (rc != 0 || ft <= 0)
        return default;
      return DateTime.FromFileTimeUtc(ft).ToLocalTime();
    } catch { return default; }
  }

  private static string PreviewValue(object? v) {
    try {
      switch (v) {
        case null:
          return "";
        case string s:
          s = s.Trim();
          return s.Length > 160 ? s.Substring(0, 160) : s;
        case string[] arr:
          return string.Join(" | ", arr.Take(5)).Trim();
        case int i:
          return i.ToString();
        case long l:
          return l.ToString();
        case byte[] b:
          if (b.Length == 0)
            return "";
          return BitConverter.ToString(b, 0, Math.Min(b.Length, 16)).Replace("-", "");
        default:
          string str = v.ToString()?.Trim() ?? "";
          return str.Length > 160 ? str.Substring(0, 160) : str;
      }
    } catch { return ""; }
  }

  private static bool RegExe(string args, int timeoutMs = 30000) {
    try {
      var psi = new ProcessStartInfo("reg", args) {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
      };
      using var p = Process.Start(psi);
      if (p is null)
        return false;
      if (!p.WaitForExit(timeoutMs)) {
        try {
          p.Kill();
        } catch { }
        return false;
      }
      return p.ExitCode == 0;
    } catch { return false; }
  }

  private static void RestoreService(string name) {
    try {
      using var sc = new System.ServiceProcess.ServiceController(name);
      try {
        sc.Refresh();
      } catch { }
      if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
        return;
      try {
        sc.Stop();
      } catch { return; }
      try {
        sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped,
            TimeSpan.FromSeconds(15));
      } catch { }
    } catch { }
  }

  public static Task<AmcacheReport> RunAsync(CancellationToken ct = default)
      => Task.Run(() => RunCore(ct), ct);

  private static AmcacheReport RunCore(CancellationToken ct) {
    var finding = new AmcacheReport { Admin = ForensicKit.IsAdministrator() };
    if (!finding.Admin) {
      finding.Error = "Administrator rights required (registry mount).";
      return finding;
    }

    string winDir = "";
    try {
      winDir = ForensicKit.GetWindowsDirectory();
    } catch { }
    if (string.IsNullOrEmpty(winDir)) {
      finding.Error = "Windows directory not found.";
      return finding;
    }
    string hive = Path.Combine(winDir, "AppCompat", "Programs", "Amcache.hve");
    if (!File.Exists(hive)) {
      finding.Error = "Amcache.hve not found.";
      return finding;
    }

    // The live hive is locked by the system: snapshot the volume with VSS
    // and mount a copy instead.
    string volume = Path.GetPathRoot(winDir) ?? @"C:\";
    if (string.IsNullOrEmpty(volume))
      volume = @"C:\";
    string? exposedRoot = null;
    string? wmiId = null;
    (bool Vss, bool Swprv) touchedInfra = (false, false);
    string tempCopy = Path.Combine(Path.GetTempPath(), "MaximAmcache.hve");
    try {
      if (File.Exists(tempCopy))
        File.Delete(tempCopy);
    } catch { }

    try {
      RegExe("unload HKLM\\" + MountName);
    } catch { }
    try {
      string? shadowVol = null;
      string shadowDetail = "";
      string? wmiRoot;
      (wmiRoot, wmiId, shadowDetail) = WmiShadow(volume);
      shadowVol = wmiRoot;
      if (shadowVol is null && EnsureShadowInfra(out bool touchedVss, out bool touchedSwprv)) {
        touchedInfra = (touchedVss, touchedSwprv);
        (wmiRoot, wmiId, shadowDetail) = WmiShadow(volume);
        shadowVol = wmiRoot;
      }
      if (shadowVol is null) {
        var (exposed, dDetail) = CreateShadow(volume);
        if (exposed is not null) {
          shadowVol = exposed;
          exposedRoot = exposed;
        } else {
          shadowDetail += " | diskshadow: " + dDetail;
        }
      }
      if (shadowVol is null) {
        finding.Error = "VSS snapshot failed: " + shadowDetail;
        return finding;
      }
      try {
        string rel = hive.Substring(volume.TrimEnd('\\').Length).TrimStart('\\');
        File.Copy(Path.Combine(shadowVol, rel), tempCopy, overwrite: true);
        // Transaction logs travel with the hive or the mount looks corrupt.
        foreach (string logExt in new[] { ".LOG1", ".LOG2" }) {
          try {
            File.Copy(Path.Combine(shadowVol, rel + logExt), tempCopy + logExt, overwrite: true);
          } catch { }
        }
      } catch (Exception ex) {
        Logger.Warning(ex, "Amcache shadow copy failed");
        finding.Error = "Could not copy Amcache.hve from snapshot.";
        return finding;
      }

      if (!RegExe("load HKLM\\" + MountName + " \"" + tempCopy + "\"")) {
        finding.Error = "Could not mount Amcache.hve copy.";
        return finding;
      }

      using var root = Registry.LocalMachine.OpenSubKey(MountName);
      if (root is null) {
        finding.Error = "Mount point unreadable.";
        return finding;
      }
      foreach (string sub in root.GetSubKeyNames()) {
        ct.ThrowIfCancellationRequested();
        if (!sub.StartsWith("Root", StringComparison.OrdinalIgnoreCase))
          continue;
        using var rootKey = root.OpenSubKey(sub);
        if (rootKey is null)
          continue;
        foreach (string inv in rootKey.GetSubKeyNames()) {
          ct.ThrowIfCancellationRequested();
          if (!inv.StartsWith("Inventory", StringComparison.OrdinalIgnoreCase))
            continue;
          using var invKey = rootKey.OpenSubKey(inv);
          if (invKey is null)
            continue;
          WalkKey(invKey, sub + "\\" + inv, finding, ct);
          if (finding.KeysScanned >= KeyCap || finding.Entries.Count >= EntryCap)
            break;
        }
        if (finding.KeysScanned >= KeyCap || finding.Entries.Count >= EntryCap)
          break;
      }
    } catch (OperationCanceledException) {
      throw;
    } catch (Exception ex) {
      Logger.Error(ex, "Amcache sweep failed");
      finding.Error = "Check failed: " + ex.Message;
      return finding;
    } finally {
      try {
        RegExe("unload HKLM\\" + MountName);
      } catch { }
      try {
        if (wmiId is not null)
          WmiDelete(wmiId);
      } catch { }
      try {
        if (exposedRoot is not null)
          DeleteShadow(exposedRoot);
      } catch { }
      try {
        if (File.Exists(tempCopy))
          File.Delete(tempCopy);
      } catch { }
      try {
        if (File.Exists(tempCopy + ".LOG1"))
          File.Delete(tempCopy + ".LOG1");
      } catch { }
      try {
        if (File.Exists(tempCopy + ".LOG2"))
          File.Delete(tempCopy + ".LOG2");
      } catch { }
      // Leave the machine as found: stop services we started ourselves.
      try {
        if (touchedInfra.Vss)
          RestoreService("VSS");
      } catch { }
      try {
        if (touchedInfra.Swprv)
          RestoreService("swprv");
      } catch { }
    }

    finding.FindingCount = finding.Entries.Count(e => e.Severity == "bad");
    return finding;
  }

  private static string Snip(string s) {
    try {
      s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
      while (s.Contains("  "))
        s = s.Replace("  ", " ");
      return s.Length > 300 ? s.Substring(0, 300) : s;
    } catch { return ""; }
  }

  private static void WalkKey(RegistryKey key, string rel, AmcacheReport finding, CancellationToken ct) {
    ct.ThrowIfCancellationRequested();
    if (finding.KeysScanned >= KeyCap || finding.Entries.Count >= EntryCap)
      return;
    finding.KeysScanned++;

    DateTime written = KeyWriteTime(key);
    var pairs = new List<string>();
    string path = "";
    try {
      foreach (string vn in key.GetValueNames()) {
        if (pairs.Count >= 40)
          break;
        object? v;
        try {
          v = key.GetValue(vn);
        } catch { continue; }
        string preview = PreviewValue(v);
        if (preview.Length == 0)
          continue;
        pairs.Add((vn.Length > 24 ? vn.Substring(0, 24) : vn) + "=" + preview);
        if (path.Length == 0 && IsPathValue(preview))
          path = preview;
      }
    } catch { }

    if (path.Length > 0 && finding.Entries.Count < EntryCap)
      finding.Entries.Add(BuildEntry(key.Name, path, pairs, written));

    string[] subs;
    try {
      subs = key.GetSubKeyNames();
    } catch { return; }
    foreach (string s in subs) {
      if (finding.KeysScanned >= KeyCap || finding.Entries.Count >= EntryCap)
        return;
      try {
        using var sub = key.OpenSubKey(s);
        if (sub is not null)
          WalkKey(sub, rel + "\\" + s, finding, ct);
      } catch (OperationCanceledException) { throw; } catch { }
    }
  }

  private static void WmiDelete(string id) {
    try {
      var scope = new ManagementScope(@"\\.\root\cimv2");
      scope.Connect();
      using var mo = new ManagementObject(scope,
          new ManagementPath("Win32_ShadowCopy.ID=\"" + id + "\""), null);
      mo.Delete();
    } catch { }
  }

  private static readonly HashSet<string> ExecExts = new(StringComparer.OrdinalIgnoreCase)
  {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js",
        ".jar", ".msi", ".com", ".scr", ".pif"
    };

  private static (string? Root, string? Id, string Detail) WmiShadow(string volume) {
    string vol = volume.TrimEnd('\\') + "\\";
    try {
      var scope = new ManagementScope(@"\\.\root\cimv2");
      scope.Connect();
      using var cls = new ManagementClass(scope, new ManagementPath("Win32_ShadowCopy"), null);
      using var inParams = cls.GetMethodParameters("Create");
      inParams["Context"] = "ClientAccessible";
      inParams["Volume"] = vol;
      using var outParams = cls.InvokeMethod("Create", inParams, null);
      uint rc = 0;
      try {
        rc = Convert.ToUInt32(outParams["ReturnValue"]);
      } catch { }
      if (rc != 0)
        return (null, null, "WMI Create returned " + rc);
      string? id = null;
      try {
        id = outParams["ShadowID"]?.ToString();
      } catch { }
      if (string.IsNullOrEmpty(id))
        return (null, null, "WMI Create gave no ShadowID");
      string? device = null;
      try {
        using var searcher = new ManagementObjectSearcher(scope,
            new ObjectQuery("SELECT DeviceObject FROM Win32_ShadowCopy WHERE ID='" + id + "'"));
        foreach (ManagementObject mo in searcher.Get()) {
          try {
            device = mo["DeviceObject"]?.ToString();
          } catch { }
          try {
            mo.Dispose();
          } catch { }
          break;
        }
      } catch (Exception ex) { return (null, id, "WMI lookup failed: " + ex.Message); }
      if (string.IsNullOrEmpty(device))
        return (null, id, "WMI gave no device object");
      if (!device.EndsWith("\\"))
        device += "\\";
      return (device, id, "");
    } catch (Exception ex) {
      return (null, null, "WMI error: " + ex.Message);
    }
  }
}
