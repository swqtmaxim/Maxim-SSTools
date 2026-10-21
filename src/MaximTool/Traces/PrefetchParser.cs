using System.Runtime.InteropServices;

internal sealed class PrefetchParser {
  private const int MinSize = 0x100;

  private const int MaxFileBytes = 64 * 1024 * 1024;

  private const int MaxFileNames = 10000;

  private const int MaxSingleNameChars = 2048;

  private const int OffVersion = 0x00;

  private const int OffSignature = 0x04;

  private const int OffHeaderFileSize = 0x0C;

  private const int OffExeName = 0x10;

  private const int ExeNameChars = 30;

  private const int OffPrefetchHash = 0x4C;

  private const int OffFileNameStrings = 0x64;

  private const int OffFileNameStringsSize = 0x68;

  private const int OffExecTimes = 0x80;

  private const int ExecSlotCount = 8;

  private const int OffRunCountD0 = 0xD0;

  private const int OffRunCountC0 = 0xC0;

  private const uint MamMagic = 0x004D414D;

  private byte[] m_data = Array.Empty<byte>();

  public bool Success => m_data.Length != 0;

  public int Version {
    get; private set;
  }

  public string ExeName { get; private set; } = "";

  public uint PrefetchHash {
    get; private set;
  }

  public int RunCount {
    get; private set;
  }

  public int HeaderFileSize {
    get; private set;
  }

  private static byte[]? DecompressMam(byte[] content) {
    if (content.Length < 8)
      return null;

    uint signature = BitConverter.ToUInt32(content, 0);
    uint decompressedSize = BitConverter.ToUInt32(content, 4);
    if ((signature & 0x00FFFFFF) != MamMagic)
      return null;
    if (((signature & 0xF0000000) >> 28) != 0)
      return null;
    if (decompressedSize == 0 || decompressedSize > (uint)MaxFileBytes)
      return null;

    ushort format = (ushort)((signature & 0x0F000000) >> 24);
    if (PrefetchNative.GetCompressionWorkSpaceSize(format, out uint wsSize, out _) != 0)
      return null;

    byte[] compressed = new byte[content.Length - 8];
    Buffer.BlockCopy(content, 8, compressed, 0, compressed.Length);
    byte[] decompressed = new byte[decompressedSize];

    IntPtr workspace = Marshal.AllocHGlobal((int)Math.Max(wsSize, 1));
    try {
      int status = PrefetchNative.DecompressBufferEx(
          format, decompressed, decompressedSize,
          compressed, (uint)compressed.Length,
          out _, workspace);
      if (status != 0)
        return null;
    } finally {
      Marshal.FreeHGlobal(workspace);
    }

    return decompressed;
  }

  public long ExecutedTime() {
    long ft = ExecutedTimestamp();
    if (ft == 0)
      return 0;
    try {
      return DateTime.FromFileTimeUtc(ft).Ticks / TimeSpan.TicksPerSecond
          - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond;
    } catch {
      return 0;
    }
  }

  private long ExecutedTimestamp() {
    if (OffExecTimes + 8 > m_data.Length)
      return 0;
    return BitConverter.ToInt64(m_data, OffExecTimes);
  }

  private int FileNameStringsOffset() => TryReadI32(OffFileNameStrings, out int v) ? v : 0;

  private int FileNameStringsSize() => TryReadI32(OffFileNameStringsSize, out int v) ? v : 0;

  public List<string> GetFilenamesStrings() {
    var finding = new List<string>();
    int offset = FileNameStringsOffset();
    int size = FileNameStringsSize();
    if (offset < MinSize || size <= 0 || offset >= m_data.Length)
      return finding;
    if ((long)offset + size > m_data.Length)
      return finding;
    size = Math.Min(size, m_data.Length - offset);

    int end = offset + (size & ~1);
    var name = new System.Text.StringBuilder(260);
    for (int i = offset; i < end; i += 2) {
      char ch = (char)(m_data[i] | (m_data[i + 1] << 8));
      if (ch == '\0') {
        if (name.Length > 0) {
          finding.Add(name.ToString());
          name.Clear();
          if (finding.Count >= MaxFileNames)
            break;
        }
        continue;
      }
      if (name.Length >= MaxSingleNameChars)
        continue;
      if (ch == '\0')
        continue;
      name.Append(ch);
    }
    if (name.Length > 0 && finding.Count < MaxFileNames)
      finding.Add(name.ToString());

    return finding;
  }

  private static bool HasSccaSignature(byte[] data) {
    return data.Length > OffSignature + 3
        && data[OffSignature] == (byte)'S' && data[OffSignature + 1] == (byte)'C'
        && data[OffSignature + 2] == (byte)'C' && data[OffSignature + 3] == (byte)'A';
  }

  public long[] LastEightExecutionTimes() {
    var times = new long[ExecSlotCount];
    if (m_data.Length <= OffExecTimes)
      return times;

    int available = m_data.Length - OffExecTimes;
    int count = Math.Min(ExecSlotCount, available / 8);
    for (int i = 0; i < count; i++) {
      long ft = BitConverter.ToInt64(m_data, OffExecTimes + i * 8);
      if (ft == 0)
        continue;
      try {
        times[i] = DateTime.FromFileTimeUtc(ft).Ticks / TimeSpan.TicksPerSecond
            - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond;
      } catch {
      }
    }

    return times;
  }

  public List<string> LastRunTimesExact() {
    var bucket = new List<string>(ExecSlotCount);
    for (int i = 0; i < ExecSlotCount; i++) {
      string s = "";
      if (TryReadI64(OffExecTimes + i * 8, out long ft) && ft != 0) {
        try {
          s = DateTime.FromFileTimeUtc(ft).ToLocalTime()
              .ToString("yyyy-MM-dd HH:mm:ss.fff");
        } catch {
        }
      }
      bucket.Add(s);
    }
    return bucket;
  }

  private void ParseHeaderFields() {
    if (TryReadI32(OffVersion, out int version) && version is >= 17 and <= 31)
      Version = version;
    if (TryReadI32(OffHeaderFileSize, out int hs) && hs > 0 && hs <= MaxFileBytes)
      HeaderFileSize = hs;
    ExeName = ReadFixedUnicodeString(OffExeName, ExeNameChars);
    if (TryReadI32(OffPrefetchHash, out int hash))
      PrefetchHash = unchecked((uint)hash);
    if (TryReadI32(OffRunCountD0, out int rc) && rc > 0 && rc < 1000000)
      RunCount = rc;
    else if (TryReadI32(OffRunCountC0, out int rc2) && rc2 > 0 && rc2 < 1000000)
      RunCount = rc2;
  }

  public PrefetchParser(string filePath) {
    byte[]? content = ForensicKit.ReadAllBytesBounded(filePath, MaxFileBytes);
    if (content is null || content.Length < MinSize)
      return;

    if (content.Length >= 8 && content[0] == (byte)'M' && content[1] == (byte)'A' && content[2] == (byte)'M') {
      byte[]? decompressed = DecompressMam(content);
      if (decompressed is null || decompressed.Length < MinSize)
        return;
      if (!HasSccaSignature(decompressed))
        return;
      m_data = decompressed;
    } else if (HasSccaSignature(content)) {
      m_data = content;
    } else {
      return;
    }

    ParseHeaderFields();
  }

  private string ReadFixedUnicodeString(int offset, int maxChars) {
    try {
      if (offset < 0 || offset + 2 > m_data.Length)
        return "";
      int chars = 0;
      while (chars < maxChars && offset + chars * 2 + 1 < m_data.Length) {
        char c = (char)(m_data[offset + chars * 2] | (m_data[offset + chars * 2 + 1] << 8));
        if (c == '\0')
          break;
        if (c < 0x20 || c == 0x7F)
          return "";
        chars++;
      }
      if (chars == 0)
        return "";
      return System.Text.Encoding.Unicode.GetString(m_data, offset, chars * 2);
    } catch {
      return "";
    }
  }

  private bool TryReadI32(int offset, out int value) {
    value = 0;
    if (offset < 0 || offset + 4 > m_data.Length)
      return false;
    value = BitConverter.ToInt32(m_data, offset);
    return true;
  }

  private bool TryReadI64(int offset, out long value) {
    value = 0;
    if (offset < 0 || offset + 8 > m_data.Length)
      return false;
    value = BitConverter.ToInt64(m_data, offset);
    return true;
  }
}
