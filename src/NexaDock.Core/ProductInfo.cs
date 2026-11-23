namespace NexaDock.Core;

/// <summary>Single source of truth for product naming and well-known identifiers.</summary>
public static class ProductInfo
{
    public const string Name = "NexaDock";
    public const string Tagline = "Smart Liquid Glass bar for Windows";
    public const string Version = "0.2.0";
    public const string Phase = "Phase 2";

    /// <summary>Folder under %LOCALAPPDATA% that holds the database and logs.</summary>
    public const string DataFolderName = "NexaDock";
    public const string DatabaseFileName = "nexadock.db";
    public const string LogFileName = "nexadock.log";

    public const string SingleInstanceMutexName = @"Local\NexaDock.SingleInstance";
    public const string ShowDockEventName = @"Local\NexaDock.ShowDock";
    public const string ShowSettingsEventName = @"Local\NexaDock.ShowSettings";

    public static string DisplayTitle => $"{Name} {Version}";
}
