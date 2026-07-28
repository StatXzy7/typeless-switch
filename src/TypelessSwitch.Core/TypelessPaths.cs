namespace TypelessSwitch.Core;

public sealed class TypelessPaths
{
    public TypelessPaths(
        string? roamingAppData = null,
        string? localAppData = null,
        string? programFiles = null,
        string? documentsDirectory = null)
    {
        RoamingAppData = roamingAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        LocalAppData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        ProgramFiles = programFiles ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        DocumentsDirectory = documentsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public string RoamingAppData { get; }
    public string LocalAppData { get; }
    public string ProgramFiles { get; }
    public string DocumentsDirectory { get; }
    public string UserDataDirectory => Path.Combine(RoamingAppData, "Typeless.exe");
    public string UserDataFile => Path.Combine(UserDataDirectory, "user-data.json");
    public string AppStorageFile => Path.Combine(UserDataDirectory, "app-storage.json");
    public string DeviceCacheFile => Path.Combine(RoamingAppData, "Typeless", "Cache", "device.cache");
    public string AppDataDirectory => Path.Combine(LocalAppData, "TypelessSwitch");
    public string AccountsFile => Path.Combine(AppDataDirectory, "accounts.json");
    public string AccountVaultDirectory => Path.Combine(AppDataDirectory, "AccountVault");
    public string WebViewDirectory => Path.Combine(AppDataDirectory, "WebView2");
    public string UpdatesDirectory => Path.Combine(AppDataDirectory, "Updates");
    public string DefaultExportDirectory => Path.Combine(DocumentsDirectory, "Typeless Switch", "Exports");
    public string DefaultExportJsonFile => Path.Combine(DefaultExportDirectory, "typeless-dictionary-export.json");

    public string? FindTypelessExecutable()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("TYPELESS_APP_PATH"),
            Path.Combine(LocalAppData, "Programs", "Typeless", "Typeless.exe"),
            Path.Combine(ProgramFiles, "Typeless", "Typeless.exe")
        };

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }
}
