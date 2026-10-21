using System.IO;

public sealed class UsnReader {
  public UsnJournalStatus CheckIntegrity(char driveLetter, TimeSpan bootGrace) {
    var st = new UsnJournalStatus();
    DateTime bootUtc = ForensicKit.GetBootTimeUtc();
    st.BootTime = bootUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    try {
      IntPtr handle = NativeApi.CreateFileW(
          $@"\\.\{driveLetter}:", NativeApi.GENERIC_READ,
          NativeApi.FILE_SHARE_READ | NativeApi.FILE_SHARE_WRITE,
          IntPtr.Zero, NativeApi.OPEN_EXISTING, 0, IntPtr.Zero);
      if (handle == NativeApi.InvalidHandleValue) {
        st.Error = "Cannot open volume (error " + NativeApi.GetLastError() + ")";
        return st;
      }

      try {
        if (QueryJournal(handle, out var journal, out int queryError)) {
          st.JournalEnabled = true;
          st.JournalId = journal.UsnJournalID;
          st.FirstUsn = journal.FirstUsn;
          st.NextUsn = journal.NextUsn;
          st.LowestValidUsn = journal.LowestValidUsn;
          st.MaxUsn = journal.MaxUsn;
          st.MaximumSize = journal.MaximumSize;
          st.UsnRecordCount = Math.Max(0, journal.NextUsn - journal.LowestValidUsn);
        } else {
          st.JournalEnabled = false;
          if (queryError == NativeApi.ERROR_JOURNAL_NOT_ACTIVE) {
            st.JournalDisabled = true;
            st.StatusDetail = "USN journal is not active (deleted or disabled with deletejournal /d)";
          } else if (queryError == NativeApi.ERROR_JOURNAL_DELETE_IN_PROGRESS) {
            st.JournalDisabled = true;
            st.StatusDetail = "USN journal delete is in progress";
          } else {
            st.Error = "Journal query failed (error " + queryError + ")";
          }
        }
      } finally {
        NativeApi.CloseHandle(handle);
      }

      if (TryGetJournalCreationUtc(driveLetter, out DateTime createdUtc)) {
        st.JournalFileExists = true;
        st.JournalCreationTime = createdUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        DateTime threshold = bootUtc + bootGrace;
        if (st.JournalEnabled && createdUtc >= threshold) {
          st.JournalRecreated = true;
          st.StatusDetail = "USN journal $J created after boot - journal was deleted and rebuilt";
        } else if (st.JournalEnabled && createdUtc > bootUtc && createdUtc < threshold) {
          st.StatusDetail = "USN journal $J created near boot (OS init) - not treated as a wipe";
        }
      } else {
        st.JournalFileExists = false;
        if (st.JournalEnabled)
          st.StatusDetail ??= "Journal is active but $J creation time is inaccessible";
      }
    } catch (Exception ex) {
      st.Error = ex.Message;
    }
    return st;
  }

  private const uint ReasonCreate = 0x100;

  private const uint ReasonDelete = 0x200;

  private const uint ReasonRenameOld = 0x1000;

  private const uint ReasonRenameNew = 0x2000;

  private const uint ReasonOverwrite = 0x1;

  private const uint ReasonExtend = 0x2;

  private const uint ReasonTrunc = 0x4;

  private const uint AttrDirectory = 0x10;

  public bool EnumerateVolume(
      char driveLetter,
      HashSet<string> exts,
      CancellationToken ct,
      out Dictionary<ulong, RawUsn> dirs,
      out List<RawUsn> files,
      out long scanned,
      out bool truncated,
      out long batches,
      out int skips,
      out long firstUsn,
      out string? error) {
    dirs = new Dictionary<ulong, RawUsn>();
    files = new List<RawUsn>();
    scanned = 0;
    truncated = false;
    batches = 0;
    skips = 0;
    firstUsn = 0;
    error = null;

    IntPtr handle = NativeApi.CreateFileW(
        $@"\\.\{driveLetter}:", NativeApi.GENERIC_READ,
        NativeApi.FILE_SHARE_READ | NativeApi.FILE_SHARE_WRITE,
        IntPtr.Zero, NativeApi.OPEN_EXISTING, 0, IntPtr.Zero);
    if (handle == NativeApi.InvalidHandleValue) {
      error = "Cannot open volume (error " + NativeApi.GetLastError() + ")";
      return false;
    }

    try {
      if (!QueryJournal(handle, out var journal, out int queryError)) {
        error = queryError == NativeApi.ERROR_JOURNAL_NOT_ACTIVE
            ? "USN journal is not active on " + driveLetter + ":"
            : "Journal query failed (error " + queryError + ")";
        return false;
      }

      byte[] inBuf = new byte[24];
      BitConverter.GetBytes((ulong)0).CopyTo(inBuf, 0);
      // Full walk: directories of every age are needed for path
      // resolution. Files are windowed to live history by the caller.
      BitConverter.GetBytes(journal.LowestValidUsn).CopyTo(inBuf, 8);
      BitConverter.GetBytes(long.MaxValue).CopyTo(inBuf, 16);
      firstUsn = journal.FirstUsn;

      byte[] outBuf = new byte[64 * 1024];
      int lastRet = outBuf.Length;
      int skipEvents = 0;
      const int MaxSkipEvents = 100;
      while (true) {
        ct.ThrowIfCancellationRequested();
        bool ok = NativeApi.DeviceIoControl(
            handle, NativeApi.FSCTL_ENUM_USN_DATA,
            inBuf, (uint)inBuf.Length, outBuf, (uint)outBuf.Length,
            out uint returned, IntPtr.Zero);
        batches++;
        if (!ok) {
          int err = NativeApi.GetLastError();
          if (err == NativeApi.ERROR_HANDLE_EOF &&
              lastRet >= outBuf.Length && skipEvents < MaxSkipEvents &&
              TryResume(handle, inBuf, outBuf, out returned)) {
            // Bad MFT region, not true end: jumped over it.
            skipEvents++;
            skips = skipEvents;
            truncated = true;
          } else {
            if (err == NativeApi.ERROR_HANDLE_EOF)
              truncated = lastRet >= outBuf.Length;
            else {
              error = "Journal walk failed (error " + err + ")";
              return false;
            }
            break;
          }
        }
        if (returned <= 8)
          break;
        lastRet = (int)returned;

        ulong nextFrn = BitConverter.ToUInt64(outBuf, 0);
        int off = 8;
        while (off + 60 <= returned) {
          uint recLen = BitConverter.ToUInt32(outBuf, off);
          if (recLen < 60 || off + recLen > returned + 1024)
            break;
          try {
            ParseRecord(outBuf, off, (int)Math.Min(recLen, returned - off),
              exts, dirs, files, ref scanned);
          } catch { }
          if (recLen == 0)
            break;
          off += (int)recLen;
        }

        BitConverter.GetBytes(nextFrn).CopyTo(inBuf, 0);
        if (nextFrn == 0)
          break;
      }
      return true;
    } catch (OperationCanceledException) {
      throw;
    } catch (Exception ex) {
      error = ex.Message;
      return false;
    } finally {
      NativeApi.CloseHandle(handle);
    }
  }

  private static void ParseRecord(
      byte[] buf, int off, int maxLen,
      HashSet<string> exts,
      Dictionary<ulong, RawUsn> dirs,
      List<RawUsn> files,
      ref long scanned) {
    ulong frn = BitConverter.ToUInt64(buf, off + 8);
    ulong parent = BitConverter.ToUInt64(buf, off + 16);
    long usn = BitConverter.ToInt64(buf, off + 24);
    long ft = BitConverter.ToInt64(buf, off + 32);
    uint reason = BitConverter.ToUInt32(buf, off + 40);
    uint attr = BitConverter.ToUInt32(buf, off + 52);
    ushort nameLen = BitConverter.ToUInt16(buf, off + 56);
    ushort nameOff = BitConverter.ToUInt16(buf, off + 58);
    if (nameLen == 0 || nameOff + nameLen > maxLen)
      return;
    string name = System.Text.Encoding.Unicode.GetString(buf, off + nameOff, nameLen);
    if (name.Length == 0)
      return;
    scanned++;

    bool isDir = (attr & AttrDirectory) != 0;
    if (isDir) {
      if (!dirs.ContainsKey(frn))
        dirs[frn] = new RawUsn { Frn = frn, Parent = parent, Name = name, IsDir = true };
      return;
    }
    string ext = "";
    int dot = name.LastIndexOf('.');
    if (dot >= 0)
      ext = name.Substring(dot).ToLowerInvariant();
    if (!exts.Contains(ext))
      return;
    files.Add(new RawUsn {
      Frn = frn,
      Parent = parent,
      Name = name,
      Usn = usn,
      FileTimeUtc = ft,
      Reason = reason,
      IsDir = false
    });
  }

  private static bool QueryJournal(
      IntPtr handle, out NativeApi.USN_JOURNAL_DATA_V0 journal, out int error) {
    journal = default;
    byte[] outBuf = new byte[128];
    bool ok = NativeApi.DeviceIoControl(
        handle, NativeApi.FSCTL_QUERY_USN_JOURNAL,
        null, 0, outBuf, (uint)outBuf.Length, out uint returned, IntPtr.Zero);
    error = ok ? 0 : NativeApi.GetLastError();
    if (!ok || returned < 56)
      return false;

    journal.UsnJournalID = BitConverter.ToUInt64(outBuf, 0);
    journal.FirstUsn = BitConverter.ToInt64(outBuf, 8);
    journal.NextUsn = BitConverter.ToInt64(outBuf, 16);
    journal.LowestValidUsn = BitConverter.ToInt64(outBuf, 24);
    journal.MaxUsn = BitConverter.ToInt64(outBuf, 32);
    journal.MaximumSize = BitConverter.ToUInt64(outBuf, 40);
    journal.AllocationDelta = BitConverter.ToUInt64(outBuf, 48);
    return true;
  }

  public static string ReasonLabel(uint reason) {
    if ((reason & ReasonCreate) != 0)
      return "CREATE";
    if ((reason & ReasonDelete) != 0)
      return "DELETE";
    if ((reason & (ReasonRenameOld | ReasonRenameNew)) != 0)
      return "RENAME";
    if ((reason & (ReasonOverwrite | ReasonExtend)) != 0)
      return "WRITE";
    if ((reason & ReasonTrunc) != 0)
      return "TRUNC";
    return "META";
  }

  private static bool TryGetJournalCreationUtc(char driveLetter, out DateTime createdUtc) {
    createdUtc = default;
    char letter = char.ToUpperInvariant(driveLetter);
    string[] paths =
    {
            $@"\\?\{letter}:\$Extend\$UsnJrnl:$J",
            $@"{letter}:\$Extend\$UsnJrnl:$J"
        };
    foreach (string path in paths) {
      try {
        DateTime t = File.GetCreationTimeUtc(path);
        if (t.Year is >= 2000 and < 2100) {
          createdUtc = t;
          return true;
        }
      } catch {
      }
    }
    return false;
  }

  private static bool TryResume(IntPtr handle, byte[] inBuf, byte[] outBuf, out uint returned) {
    returned = 0;
    ulong failed;
    try {
      failed = BitConverter.ToUInt64(inBuf, 0);
    } catch { return false; }
    foreach (ulong step in new ulong[] { 1, 16, 256, 4096, 65536 }) {
      ulong probe;
      try {
        checked {
          probe = failed + step;
        }
      } catch { return false; }
      BitConverter.GetBytes(probe).CopyTo(inBuf, 0);
      if (NativeApi.DeviceIoControl(
          handle, NativeApi.FSCTL_ENUM_USN_DATA,
          inBuf, (uint)inBuf.Length, outBuf, (uint)outBuf.Length,
          out returned, IntPtr.Zero))
        return true;
      if (NativeApi.GetLastError() != NativeApi.ERROR_HANDLE_EOF)
        return false;
    }
    return false;
  }

  public sealed class UsnJournalStatus {
    public bool JournalEnabled {
      get; set;
    }
    public bool JournalFileExists {
      get; set;
    }
    public bool JournalRecreated {
      get; set;
    }
    public bool JournalDisabled {
      get; set;
    }
    public string? JournalCreationTime {
      get; set;
    }
    public string? BootTime {
      get; set;
    }
    public string? StatusDetail {
      get; set;
    }
    public ulong JournalId {
      get; set;
    }
    public long FirstUsn {
      get; set;
    }
    public long NextUsn {
      get; set;
    }
    public long LowestValidUsn {
      get; set;
    }
    public long MaxUsn {
      get; set;
    }
    public ulong MaximumSize {
      get; set;
    }
    public long UsnRecordCount {
      get; set;
    }
    public string? Error {
      get; set;
    }
  }

  public sealed class RawUsn {
    public ulong Frn;
    public ulong Parent;
    public string Name = "";
    public long Usn;
    public long FileTimeUtc;
    public uint Reason;
    public bool IsDir;
  }
}
