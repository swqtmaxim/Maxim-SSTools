internal static class TextCodec {
  public static string SafeSegment(string value) {
    if (string.IsNullOrEmpty(value))
      return "_";
    var buff = new System.Text.StringBuilder(value.Length);
    foreach (char c in value)
      buff.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
    return buff.ToString();
  }

  public static string ToHexPrefix(byte[] hash, int chars = 12) {
    if (hash is null || hash.Length == 0 || chars <= 0)
      return "";
    string hex = Convert.ToHexString(hash);
    return hex.Length <= chars ? hex : hex.Substring(0, chars);
  }

  public static string TrimSample(string value, int max = 80) {
    if (string.IsNullOrEmpty(value) || value.Length <= max)
      return value ?? "";
    return value.Substring(0, max);
  }
}
