namespace NexaDock.UI.Services;

public static class Format
{
    public static string Bytes(double bytes) => bytes switch
    {
        >= 1L << 40 => $"{bytes / (1L << 40):0.0} TB",
        >= 1L << 30 => $"{bytes / (1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / 1024:0} KB",
        _ => $"{bytes:0} B",
    };
}
