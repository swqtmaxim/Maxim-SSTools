using System.IO;
using Serilog;

public static class UsnSweep {
  private static readonly ILogger Logger = Log.ForContext(typeof(UsnSweep));

  private const int ReturnCap = 5000;

  private static UsnReport RunCore(string drive, CancellationToken ct) {
    var finding = new UsnReport { Admin = ForensicKit.IsAdministrator() };
    DateTime bootLocal = ForensicKit.GetBootTimeLocal();

    if (string.IsNullOrWhiteSpace(drive) || drive.Length < 1 ||
        !char.IsLetter(drive[0])) {
      finding.Error = "Pick a drive letter first.";
      return finding;
    }
    char letter = char.ToUpperInvariant(drive[0]);
    finding.Drive = letter + ":";

    var reader = new UsnReader();
    List<UsnReader.RawUsn> files;
    Dictionary<ulong, UsnReader.RawUsn> dirs;
    long scanned;
    try {
      if (!reader.EnumerateVolume(letter, WantedExts, ct, out dirs, out files, out scanned,
          out bool cut, out long batches, out int skips, out long firstUsn, out string? error)) {
        finding.Error = error ?? "Journal walk failed.";
        return finding;
      }
      finding.Truncated = cut;
      finding.Batches = batches;
      finding.Skips = skips;
      finding.DirsMapped = dirs.Count;
      // Keep only files with live journal history; older entries come
      // back as zero-filled stubs.
      if (firstUsn > 0)
        files.RemoveAll(f => f.Usn < firstUsn);
    } catch (OperationCanceledException) {
      throw;
    } catch (Exception ex) {
      Logger.Error(ex, "USN sweep failed");
      finding.Error = "Check failed: " + ex.Message;
      return finding;
    }

    finding.Scanned = scanned;
    files.Sort((a, b) => b.Usn.CompareTo(a.Usn));
    finding.Matched = files.Count;

    // Journal records on live volumes often carry no timestamp of their
    // own (Usn live, Time/Reason zero). Date from the filesystem itself;
    // only fully resolved paths can be trusted for presence checks.
    int session = 0, resolved = 0, dated = 0;
    foreach (var sourceLine in files) {
      if (finding.Entries.Count >= ReturnCap)
        break;
      string ext = "";
      int dot = sourceLine.Name.LastIndexOf('.');
      if (dot >= 0)
        ext = sourceLine.Name.Substring(dot).ToLowerInvariant();
      var (path, full) = Resolve(letter, sourceLine, dirs);
      if (full)
        resolved++;
      // Stat anything that looks like a real nested path. Partial chains
      // get a "~" time marker; only fully resolved paths earn GONE flags.
      DateTime fsTime = default;
      bool present = false;
      if (full || path.Count(c => c == '\\') >= 2) {
        try {
          present = File.Exists(path);
          if (present)
            fsTime = File.GetLastWriteTime(path);
        } catch { present = false; }
      }
      bool known = fsTime.Year >= 2000;
      if (known) {
        dated++;
        if (fsTime >= bootLocal)
          session++;
      }
      finding.Entries.Add(new UsnEntry {
        Name = sourceLine.Name,
        Path = path,
        Ext = ext,
        Reason = full && !present ? "GONE" : UsnReader.ReasonLabel(sourceLine.Reason),
        Time = known ? (full ? "" : "~") + fsTime.ToString("yyyy-MM-dd HH:mm:ss") : "—",
        Unix = known
              ? fsTime.Ticks / TimeSpan.TicksPerSecond - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond : 0,
        Usn = sourceLine.Usn,
        Gone = full && !present
      });
    }

    finding.Entries.Sort((a, b) => b.Unix.CompareTo(a.Unix));

    finding.Returned = finding.Entries.Count;
    finding.FindingCount = session;
    finding.Resolved = resolved;
    finding.Dated = dated;
    try {
      string? exe = Environment.ProcessPath;
      if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
        finding.Built = File.GetLastWriteTime(exe).ToString("HH:mm");
    } catch { }
    try {
      finding.BootUnix = bootLocal.Ticks / TimeSpan.TicksPerSecond
          - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond;
    } catch { }
    return finding;
  }

  public static List<string> NtfsDrives() {
    var bucket = new List<string>();
    foreach (var drive in DriveInfo.GetDrives()) {
      try {
        if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
          continue;
        if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
          continue;
        string letter = drive.Name.TrimEnd('\\');
        if (letter.Length == 2 && letter[1] == ':')
          bucket.Add(letter);
      } catch { }
    }
    return bucket;
  }

  public static Task<UsnReport> RunAsync(string drive, CancellationToken ct = default)
      => Task.Run(() => RunCore(drive, ct), ct);

  private static readonly HashSet<string> WantedExts = new(StringComparer.OrdinalIgnoreCase)
  {
        ".exe", ".dll", ".jar", ".pf"
    };

  private static (string Path, bool Full) Resolve(char letter, UsnReader.RawUsn sourceLine, Dictionary<ulong, UsnReader.RawUsn> dirs) {
    try {
      var parts = new List<string> { sourceLine.Name };
      ulong parent = sourceLine.Parent;
      for (int i = 0; i < 32 && parent != 0; i++) {
        // FRN 5 is the volume root on every NTFS volume: reaching it
        // means the chain is complete even if the root record itself
        // never appeared in the walk.
        if ((parent & 0xFFFFFFFFFFFFUL) == 5)
          break;
        if (!dirs.TryGetValue(parent, out UsnReader.RawUsn? dir))
          return (letter + ":\\" + string.Join("\\", parts.AsEnumerable().Reverse()), false);
        // Root and system entries may carry "." or empty names: skip,
        // keep climbing instead of aborting the whole chain.
        if (!string.IsNullOrEmpty(dir.Name) && dir.Name != ".")
          parts.Add(dir.Name);
        if (dir.Parent == parent)
          break;
        parent = dir.Parent;
      }
      parts.Reverse();
      return (letter + ":\\" + string.Join("\\", parts), true);
    } catch {
      return (letter + ":\\" + sourceLine.Name, false);
    }
  }
}
