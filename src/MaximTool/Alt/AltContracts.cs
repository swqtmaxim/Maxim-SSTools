using System.Text.Json.Serialization;

public sealed class AltReport {
  [JsonPropertyName("minecraftAccounts")]
  public List<string> MinecraftAccounts { get; set; } = new();

  [JsonPropertyName("discordIds")]
  public List<string> DiscordIds { get; set; } = new();

  [JsonPropertyName("discordAccounts")]
  public List<DiscordAccount> DiscordAccounts { get; set; } = new();

  [JsonPropertyName("launcherFilesScanned")]
  public int LauncherFilesScanned {
    get; set;
  }

  [JsonPropertyName("logFilesScanned")]
  public int LogFilesScanned {
    get; set;
  }

  [JsonPropertyName("discordDirectoriesScanned")]
  public int DiscordDirectoriesScanned {
    get; set;
  }

  [JsonPropertyName("browserDirectoriesScanned")]
  public int BrowserDirectoriesScanned {
    get; set;
  }

  [JsonPropertyName("cachedMcCount")]
  public int CachedMcCount {
    get; set;
  }

  [JsonPropertyName("cachedDcCount")]
  public int CachedDcCount {
    get; set;
  }

  [JsonPropertyName("hwid")]
  public HwidInfo Hwid { get; set; } = new();

  [JsonPropertyName("mods")]
  public List<ModFile> Mods { get; set; } = new();

  [JsonPropertyName("profiles")]
  public List<McProfileInfo> Profiles { get; set; } = new();
}

public sealed class DiscordAccount {
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  [JsonPropertyName("username")]
  public string Username { get; set; } = string.Empty;

  [JsonPropertyName("source")]
  public string Source { get; set; } = string.Empty;
}

public sealed class HwidInfo {
  [JsonPropertyName("machineGuid")]
  public string MachineGuid { get; set; } = "";

  [JsonPropertyName("disks")]
  public List<string> Disks { get; set; } = new();
}

public sealed class ModFile {
  [JsonPropertyName("name")]
  public string Name { get; set; } = "";

  [JsonPropertyName("path")]
  public string Path { get; set; } = "";

  [JsonPropertyName("sizeLabel")]
  public string SizeLabel { get; set; } = "";
}
