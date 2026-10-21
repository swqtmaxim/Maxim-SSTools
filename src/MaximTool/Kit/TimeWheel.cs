internal static class TimeWheel {
  public static int DayBucket(long unix, long nowUnix) {
    long diff = nowUnix - unix;
    if (diff < 0)
      return 0;
    long days = diff / 86_400;
    return days > 13 ? 14 : (int)days;
  }

  public static string FormatStamp(DateTime local)
      => local.ToString("yyyy-MM-dd HH:mm:ss");

  public static long ToUnixSeconds(DateTime utc)
      => utc.Ticks / TimeSpan.TicksPerSecond
          - DateTime.UnixEpoch.Ticks / TimeSpan.TicksPerSecond;
}
