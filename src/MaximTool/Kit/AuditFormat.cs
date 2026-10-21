internal static class AuditFormat {
  private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

  public static string FormatBytes(long bytes) {
    double value = bytes;
    int i = 0;
    while (value >= 1024 && i < Units.Length - 1) {
      value /= 1024;
      i++;
    }
    string num = i == 0 ? value.ToString("0") : value.ToString("0.0");
    return num + " " + Units[i];
  }

  public static string Pluralize(int count, string single, string many)
      => count == 1 ? $"1 {single}" : $"{count} {many}";
}
