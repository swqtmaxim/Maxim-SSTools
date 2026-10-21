using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Serilog;

internal static class PrefetchVolumeMap {
  private static readonly ILogger Logger = Log.ForContext(typeof(PrefetchVolumeMap));

  public static Map Build() {
    var map = new Map();
    foreach (var drive in DriveInfo.GetDrives()) {
      try {
        if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
          continue;
        string root = drive.RootDirectory.FullName;
        string letter = drive.Name.TrimEnd('\\');
        if (PrefetchNative.GetVolumeInformation(
                root, IntPtr.Zero, 0,
                out uint serial, out _, out _, IntPtr.Zero, 0)) {
          map.SerialToLetter[serial.ToString("X8")] = letter;
        }
        var name = new System.Text.StringBuilder(128);
        if (PrefetchNative.GetVolumeNameForMountPoint(root, name, (uint)name.Capacity)) {
          string guid = name.ToString().Trim();
          int open = guid.IndexOf('{');
          int close = guid.IndexOf('}');
          if (open >= 0 && close > open)
            map.GuidToLetter[guid.Substring(open + 1, close - open - 1)] = letter;
        }
      } catch (Exception ex) {
        Logger.Debug(ex, "Volume serial lookup failed for {Drive}", drive.Name);
      }
    }
    return map;
  }

  public static string ResolveVolumePath(string volumePath, Map map) {
    if (string.IsNullOrEmpty(volumePath))
      return volumePath;

    int start = volumePath.IndexOf("VOLUME{", StringComparison.OrdinalIgnoreCase);
    if (start < 0)
      return volumePath;
    int end = volumePath.IndexOf('}', start);
    if (end < 0)
      return volumePath;

    string inner = volumePath.Substring(start + 7, end - start - 7);
    int dash = inner.IndexOf('-');
    if (dash >= 0) {
      string serial = inner.Substring(dash + 1).ToUpperInvariant();
      if (map.SerialToLetter.TryGetValue(serial, out string? letter))
        return letter + volumePath.Substring(end + 1);
    }
    if (map.GuidToLetter.TryGetValue(inner, out string? guidLetter))
      return guidLetter + volumePath.Substring(end + 1);
    return volumePath;
  }

  public sealed class Map {
    public Dictionary<string, string> SerialToLetter {
      get;
    } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> GuidToLetter {
      get;
    } =
        new(StringComparer.OrdinalIgnoreCase);
  }
}

internal static class LogonSessions {
  public static List<long> GetInteractiveLogons() {
    var finding = new List<long>();
    try {
      uint status = PrefetchNative.EnumerateLogonSessions(out uint count, out IntPtr bucket);
      if (status != 0 || bucket == IntPtr.Zero)
        return finding;

      try {
        for (uint i = 0; i < count; i++) {
          IntPtr luid = IntPtr.Add(bucket, (int)(i * 8));
          if (PrefetchNative.GetLogonSessionData(luid, out IntPtr data) != 0 || data == IntPtr.Zero)
            continue;
          try {
            int logonType = Marshal.ReadInt32(data, PrefetchNative.LogonTypeOffset);
            if (logonType != PrefetchNative.InteractiveLogon &&
                logonType != PrefetchNative.RemoteInteractiveLogon)
              continue;
            long fileTime = Marshal.ReadInt64(data, PrefetchNative.LogonTimeOffset);
            if (fileTime == 0)
              continue;
            try {
              long unix = DateTime.FromFileTimeUtc(fileTime).Ticks / TimeSpan.TicksPerSecond
                  - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond;
              finding.Add(unix);
            } catch {
            }
          } finally {
            PrefetchNative.FreeReturnBuffer(data);
          }
        }
      } finally {
        PrefetchNative.FreeReturnBuffer(bucket);
      }
    } catch {
    }
    return finding;
  }
}

public static class PrefetchSweep {
  private static readonly ILogger Logger = Log.ForContext(typeof(PrefetchSweep));

  private const int RelatedSendCap = 300;

  private const int RelatedSigCap = 200;

  public static List<PrefetchRelatedSig> CheckRelatedSignatures(
      List<string> paths, CancellationToken ct = default) {
    var distinct = paths
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(RelatedSigCap)
        .ToList();

    var results = new PrefetchRelatedSig?[distinct.Count];
    var cache = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    var options = new ParallelOptions {
      MaxDegreeOfParallelism = 4,
      CancellationToken = ct
    };

    Parallel.For(0, distinct.Count, options, i => {
      ct.ThrowIfCancellationRequested();
      bool signed = false;
      try {
        signed = File.Exists(distinct[i]) &&
            GetCachedSignature(distinct[i], cache);
      } catch (OperationCanceledException) {
        throw;
      } catch {
        signed = false;
      }
      results[i] = new PrefetchRelatedSig { Path = distinct[i], Signed = signed };
    });

    return results.Where(r => r is not null).Select(r => r!).ToList();
  }

  private static string FormatUnix(long unix) {
    if (unix <= 0)
      return "";
    try {
      return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime()
          .ToString("yyyy-MM-dd HH:mm:ss");
    } catch {
      return "";
    }
  }

  private static bool GetCachedSignature(
      string path,
      System.Collections.Concurrent.ConcurrentDictionary<string, bool> cache) {
    if (cache.TryGetValue(path, out bool cached))
      return cached;
    bool finding = PrefetchSignature.IsFileSignatureValid(path);
    cache.TryAdd(path, finding);
    return finding;
  }

  private static string GetPrefetchDir() {
    try {
      string win = ForensicKit.GetWindowsDirectory();
      if (!string.IsNullOrEmpty(win))
        return Path.Combine(win, "Prefetch");
    } catch {
    }
    return @"C:\Windows\Prefetch";
  }

  private static bool IsInInstance(
      List<long> lastRunsUnix, string readableTime, List<long> logons, long nowUnix) {
    if (logons.Count == 0)
      return false;
    long firstLogon = logons.Min();
    if (lastRunsUnix is { Count: > 0 }) {
      foreach (long run in lastRunsUnix) {
        if (run > 0 && run >= firstLogon && run <= nowUnix)
          return true;
      }
      return false;
    }
    if (!DateTime.TryParseExact(readableTime, "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal, out DateTime local))
      return false;
    long execUnix = new DateTimeOffset(local).ToUnixTimeSeconds();
    return execUnix >= firstLogon && execUnix <= nowUnix;
  }

  public static PrefetchReport Run(CancellationToken ct = default) {
    try {
      return RunAsync(ct).ConfigureAwait(false).GetAwaiter().GetResult();
    } catch (OperationCanceledException) {
      return new PrefetchReport { Error = "Scan cancelled." };
    } catch (Exception ex) {
      Logger.Error(ex, "Prefetch scan failed");
      return new PrefetchReport { Error = "Check failed: " + ex.Message };
    }
  }

  public static Task<PrefetchReport> RunAsync(CancellationToken ct = default)
      => RunCoreAsync(ct);

  private static async Task<PrefetchReport> RunCoreAsync(CancellationToken ct) {
    var finding = new PrefetchReport {
      Admin = ForensicKit.IsAdministrator(),
      PrefetchDir = GetPrefetchDir()
    };

    string[] files;
    try {
      if (!Directory.Exists(finding.PrefetchDir)) {
        finding.Error = "Prefetch directory not found.";
        return finding;
      }
      files = Directory.GetFiles(finding.PrefetchDir, "*.pf");
    } catch (Exception ex) {
      finding.Error = "Cannot list Prefetch directory: " + ex.Message;
      return finding;
    }

    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
    finding.FilesFound = files.Length;

    var volumeMap = PrefetchVolumeMap.Build();
    var logons = LogonSessions.GetInteractiveLogons();
    long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    string ownPath = Environment.ProcessPath ?? "";
    var sigCache = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    var entries = new PrefetchEntry?[files.Length];
    var options = new ParallelOptions {
      MaxDegreeOfParallelism = Math.Min(8, Math.Max(4, Environment.ProcessorCount)),
      CancellationToken = ct
    };

    await Task.Run(() => Parallel.For(0, files.Length, options, i => {
      ct.ThrowIfCancellationRequested();
      try {
        entries[i] = ScanOne(files[i], volumeMap, logons, nowUnix, ownPath, sigCache, ct);
      } catch (OperationCanceledException) {
        throw;
      } catch (Exception ex) {
        Logger.Warning(ex, "Prefetch entry failed for {File}", files[i]);
        entries[i] = new PrefetchEntry {
          Filename = Path.GetFileName(files[i]),
          IsSigned = false,
          IsPresent = true,
          MatchedRules = new List<string> { "none" }
        };
      }
    }), ct).ConfigureAwait(false);

    string ownExeName = "";
    try {
      ownExeName = Path.GetFileNameWithoutExtension(ownPath);
    } catch { }
    foreach (var entry in entries) {
      if (entry is null)
        continue;
      finding.Entries.Add(entry);
    }

    finding.FindingCount = finding.Entries.Count(e => {
      if (!string.IsNullOrEmpty(ownExeName) && !string.IsNullOrEmpty(e.ProperPath)) {
        try {
          if (string.Equals(Path.GetFileNameWithoutExtension(e.ProperPath), ownExeName, StringComparison.OrdinalIgnoreCase))
            return false;
        } catch { }
      }
      return !e.IsSigned || e.MatchedRules.Any(r => !string.Equals(r, "none", StringComparison.Ordinal));
    });
    return finding;
  }

  private static PrefetchEntry? ScanOne(
      string filePath,
      PrefetchVolumeMap.Map volumeMap,
      List<long> logons,
      long nowUnix,
      string ownPath,
      System.Collections.Concurrent.ConcurrentDictionary<string, bool> sigCache,
      CancellationToken ct) {
    ct.ThrowIfCancellationRequested();

    var parser = new PrefetchParser(filePath);
    if (!parser.Success)
      return null;

    var entry = new PrefetchEntry {
      Filename = Path.GetFileName(filePath),
      ExecutedUnix = parser.ExecutedTime(),
      LastRunsUnix = parser.LastEightExecutionTimes().ToList(),
      IsPresent = true,
      MatchedRules = new List<string>(),
      ExeName = parser.ExeName,
      RunCount = parser.RunCount,
      PrefetchHash = parser.PrefetchHash.ToString("X8")
    };
    entry.ReadableTime = FormatUnix(entry.ExecutedUnix);
    entry.LastRunsExact = parser.LastRunTimesExact();
    try {
      var fi = new FileInfo(filePath);
      entry.PfSizeBytes = fi.Length;
      entry.PfCreated = fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
      entry.PfAccessed = fi.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss");
      entry.PfModified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
    } catch {
    }

    var related = parser.GetFilenamesStrings();
    entry.RelatedTotal = related.Count;

    var resolvedAll = new List<string>(related.Count);
    foreach (string name in related)
      resolvedAll.Add(PrefetchVolumeMap.ResolveVolumePath(name, volumeMap));

    string stem = entry.Filename;
    int hyphen = stem.LastIndexOf('-');
    if (hyphen > 0)
      stem = stem.Substring(0, hyphen);

    string? fallback = null;
    foreach (string candidate in resolvedAll) {
      string baseName;
      try {
        baseName = Path.GetFileName(candidate);
      } catch { continue; }
      if (string.IsNullOrEmpty(baseName) || baseName.IndexOf('.') < 0)
        continue;
      string baseStem;
      try {
        baseStem = Path.GetFileNameWithoutExtension(baseName);
      } catch { continue; }
      if (string.Equals(baseStem, stem, StringComparison.OrdinalIgnoreCase)) {
        entry.ProperPath = candidate;
        fallback = null;
        break;
      }
      if (fallback is null && baseName.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0)
        fallback = candidate;
    }
    if (string.IsNullOrEmpty(entry.ProperPath) && fallback is not null)
      entry.ProperPath = fallback;

    int send = Math.Min(resolvedAll.Count, RelatedSendCap);
    for (int r = 0; r < send; r++) {
      bool present;
      try {
        present = File.Exists(resolvedAll[r]);
      } catch { present = false; }
      entry.RelatedFiles.Add(new PrefetchRelatedFile { Path = resolvedAll[r], Present = present });
    }

    entry.IsInInstance = IsInInstance(entry.LastRunsUnix, entry.ReadableTime, logons, nowUnix);

    if (!string.IsNullOrEmpty(entry.ProperPath)) {
      ct.ThrowIfCancellationRequested();

      bool exists;
      try {
        exists = File.Exists(entry.ProperPath);
      } catch { exists = false; }

      if (!exists) {
        entry.IsSigned = false;
        entry.IsPresent = false;
        entry.MatchedRules.Add("none");
        return entry;
      }

      entry.IsSigned = GetCachedSignature(entry.ProperPath, sigCache);
      ct.ThrowIfCancellationRequested();
      if (!entry.IsSigned) {
        if (!string.IsNullOrEmpty(ownPath) &&
            string.Equals(entry.ProperPath, ownPath, StringComparison.OrdinalIgnoreCase)) {
          entry.MatchedRules.Add("none");
        } else {
          var hits = new List<RuleEngine.RuleHit>();
          if (RuleEngine.ScanFile(entry.ProperPath, hits) && hits.Count > 0) {
            foreach (var hit in hits) {
              entry.MatchedRules.Add(hit.Id);
              entry.MatchedDetails.Add(new PrefetchRuleHit {
                Id = hit.Id,
                Name = hit.DisplayName,
                Sample = hit.Sample,
                Encoding = hit.Encoding,
                Offset = hit.Offset,
                Count = hit.Count,
                Context = hit.Context
              });
            }
          } else
            entry.MatchedRules.Add("none");
        }
      } else {
        entry.MatchedRules.Add("none");
      }
    }

    return entry;
  }
}
