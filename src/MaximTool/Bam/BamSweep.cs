using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using Serilog;

public static class BamSweep {
  private static readonly ILogger Logger = Log.ForContext(typeof(BamSweep));

  private const string BamKeyPath = @"SYSTEM\CurrentControlSet\Services\bam\State\UserSettings";

  private const string GlobalRootPrefix = @"\\?\GLOBALROOT";

  private static Dictionary<string, string> BuildVolumeMap() {
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var drive in DriveInfo.GetDrives()) {
      try {
        if (!drive.IsReady)
          continue;
        string letter = drive.Name.TrimEnd('\\');
        if (letter.Length != 2 || letter[1] != ':')
          continue;
        var buff = new StringBuilder(512);
        if (PrefetchNative.QueryDosDevice(letter, buff, (uint)buff.Capacity) == 0)
          continue;
        string volume = buff.ToString().TrimEnd('\0');
        int nul = volume.IndexOf('\0');
        if (nul >= 0)
          volume = volume.Substring(0, nul);
        map[volume] = letter;
      } catch {
      }
    }
    return map;
  }

  private static string GetCachedBamStatus(
      string path,
      System.Collections.Concurrent.ConcurrentDictionary<string, string> cache) {
    if (cache.TryGetValue(path, out string? cached))
      return cached;
    string status = PrefetchSignature.GetBamStatus(path);
    cache.TryAdd(path, status);
    return status;
  }

  private static bool IsInInstance(
      string readableTime, List<long> logons, long nowUnix) {
    if (logons.Count == 0)
      return false;
    if (!DateTime.TryParseExact(readableTime, "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal, out DateTime local))
      return false;
    long execUnix = new DateTimeOffset(local).ToUnixTimeSeconds();
    return execUnix >= logons.Min() && execUnix <= nowUnix;
  }

  private static List<(string Path, long ExecutedUnix, string ReadableTime)> ReadBamRegistry(
      CancellationToken ct, Dictionary<string, string> volumeMap) {
    var rows = new List<(string, long, string)>();
    using var key = Registry.LocalMachine.OpenSubKey(BamKeyPath, writable: false);
    if (key is null)
      throw new InvalidOperationException("BAM registry key not found.");
    foreach (string sub in key.GetSubKeyNames()) {
      ct.ThrowIfCancellationRequested();
      using var subKey = key.OpenSubKey(sub, writable: false);
      if (subKey is null)
        continue;
      string[] valueNames;
      try {
        valueNames = subKey.GetValueNames();
      } catch { continue; }
      foreach (string valueName in valueNames) {
        ct.ThrowIfCancellationRequested();
        RegistryValueKind kind;
        try {
          kind = subKey.GetValueKind(valueName);
        } catch { continue; }
        if (kind != RegistryValueKind.Binary)
          continue;
        if (subKey.GetValue(valueName) is not byte[] data || data.Length < 8)
          continue;
        if (valueName.IndexOf('\\') < 0)
          continue;

        long ft = BitConverter.ToInt64(data, 0);
        long unix = 0;
        string readable = "";
        if (ft != 0) {
          try {
            DateTime utc = DateTime.FromFileTimeUtc(ft);
            if (utc.Year is >= 2000 and < 2100) {
              DateTime local = utc.ToLocalTime();
              unix = utc.Ticks / TimeSpan.TicksPerSecond
                  - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond;
              readable = local.ToString("yyyy-MM-dd HH:mm:ss");
            }
          } catch {
          }
        }

        rows.Add((ResolveDevicePath(valueName, volumeMap), unix, readable));
      }
    }
    return rows;
  }

  internal static string ResolveDevicePath(string path) => ResolveDevicePath(path, BuildVolumeMap());

  private static string ResolveDevicePath(string path, Dictionary<string, string> volumeMap) {
    if (string.IsNullOrEmpty(path))
      return path;
    try {
      if (path.IndexOf("HarddiskVolume", StringComparison.OrdinalIgnoreCase) < 0)
        return path;

      foreach (var kv in volumeMap) {
        string volume = kv.Key;
        string letter = kv.Value;
        if (path.StartsWith(volume, StringComparison.OrdinalIgnoreCase) &&
            (path.Length == volume.Length || path[volume.Length] == '\\')) {
          string rest = path.Substring(volume.Length);
          if (!rest.StartsWith("\\"))
            rest = "\\" + rest;
          return letter + rest;
        }

        if (path.StartsWith(GlobalRootPrefix, StringComparison.OrdinalIgnoreCase)) {
          string stripped = path.Substring(GlobalRootPrefix.Length);
          if (stripped.StartsWith(volume, StringComparison.OrdinalIgnoreCase) &&
              (stripped.Length == volume.Length || stripped[volume.Length] == '\\')) {
            string rest = stripped.Substring(volume.Length);
            if (!rest.StartsWith("\\"))
              rest = "\\" + rest;
            return letter + rest;
          }
        }
      }
    } catch {
    }
    return path;
  }

  public static BamReport Run(CancellationToken ct = default) {
    try {
      return RunAsync(ct).ConfigureAwait(false).GetAwaiter().GetResult();
    } catch (OperationCanceledException) {
      return new BamReport { Error = "Scan cancelled." };
    } catch (Exception ex) {
      Logger.Error(ex, "BAM scan failed");
      return new BamReport { Error = "Check failed: " + ex.Message };
    }
  }

  public static Task<BamReport> RunAsync(CancellationToken ct = default)
      => RunCoreAsync(ct);

  private static async Task<BamReport> RunCoreAsync(CancellationToken ct) {
    var finding = new BamReport {
      Admin = ForensicKit.IsAdministrator()
    };

    List<(string Path, long ExecutedUnix, string ReadableTime)> rows;
    try {
      var volumeMap = BuildVolumeMap();
      rows = ReadBamRegistry(ct, volumeMap);
    } catch (OperationCanceledException) {
      throw;
    } catch (Exception ex) {
      finding.Error = "Cannot read BAM registry: " + ex.Message;
      return finding;
    }

    if (rows.Count == 0) {
      finding.Error = "No BAM entries found. Administrator rights are required.";
      return finding;
    }

    var logons = LogonSessions.GetInteractiveLogons();
    long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var sigCache = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    Dictionary<string, List<BamReplaceDetector.Hit>> replaceCache;
    try {
      var replace = await Task.Run(() => BamReplaceDetector.ScanDrives(ct), ct).ConfigureAwait(false);
      replaceCache = replace.Cache;
      finding.DrivesScanned = replace.DrivesScanned;
      finding.DrivesSkipped = replace.DrivesSkipped;
    } catch (OperationCanceledException) {
      throw;
    } catch (Exception ex) {
      Logger.Warning(ex, "BAM replace scan failed");
      replaceCache = new Dictionary<string, List<BamReplaceDetector.Hit>>(StringComparer.OrdinalIgnoreCase);
    }

    var entries = new BamEntry?[rows.Count];
    var options = new ParallelOptions {
      MaxDegreeOfParallelism = Math.Min(8, Math.Max(4, Environment.ProcessorCount)),
      CancellationToken = ct
    };

    await Task.Run(() => Parallel.For(0, rows.Count, options, i => {
      ct.ThrowIfCancellationRequested();
      try {
        entries[i] = ScanOne(rows[i], replaceCache, logons, nowUnix, sigCache, ct);
      } catch (OperationCanceledException) {
        throw;
      } catch (Exception ex) {
        Logger.Warning(ex, "BAM entry failed for {Path}", rows[i].Path);
        entries[i] = new BamEntry {
          Path = rows[i].Path,
          ReadableTime = rows[i].ReadableTime,
          ExecutedUnix = rows[i].ExecutedUnix,
          Signature = "Not signed",
          IsInInstance = false,
          MatchedRules = new List<string> { "none" }
        };
      }
    }), ct).ConfigureAwait(false);

    foreach (var entry in entries) {
      if (entry is null)
        continue;
      finding.Entries.Add(entry);
    }

    finding.Entries.Sort((a, b) => {
      int c = b.ExecutedUnix.CompareTo(a.ExecutedUnix);
      return c != 0 ? c : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
    });
    finding.FindingCount = finding.Entries.Count(e =>
        e.Signature != "Signed" ||
        e.MatchedRules.Any(r => !string.Equals(r, "none", StringComparison.Ordinal)) ||
        e.ReplaceResults.Count > 0);
    return finding;
  }

  private static BamEntry? ScanOne(
      (string Path, long ExecutedUnix, string ReadableTime) row,
      Dictionary<string, List<BamReplaceDetector.Hit>> replaceCache,
      List<long> logons,
      long nowUnix,
      System.Collections.Concurrent.ConcurrentDictionary<string, string> sigCache,
      CancellationToken ct) {
    ct.ThrowIfCancellationRequested();

    var entry = new BamEntry {
      Path = row.Path,
      ExecutedUnix = row.ExecutedUnix,
      ReadableTime = row.ReadableTime,
      MatchedRules = new List<string>()
    };
    entry.IsInInstance = IsInInstance(row.ReadableTime, logons, nowUnix);

    bool exists;
    try {
      exists = File.Exists(row.Path);
    } catch { exists = false; }

    if (!exists) {
      entry.Signature = "Deleted";
      entry.MatchedRules.Add("none");
    } else {
      entry.Signature = GetCachedBamStatus(row.Path, sigCache);
      ct.ThrowIfCancellationRequested();

      if (entry.Signature != "Signed") {
        var hits = new List<RuleEngine.RuleHit>();
        if (RuleEngine.ScanFile(row.Path, hits) && hits.Count > 0) {
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
      } else {
        entry.MatchedRules.Add("none");
      }
    }

    foreach (var hit in BamReplaceDetector.Lookup(replaceCache, row.Path)) {
      entry.ReplaceResults.Add(new BamReplaceResult {
        Filename = hit.Filename,
        ReplaceType = hit.ReplaceType,
        Details = hit.Details
      });
    }

    return entry;
  }
}
