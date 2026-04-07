using WrenchWise.Shared.Models;

namespace WrenchWise.Shared.Sync;

public class SyncResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime ServerUtc { get; set; } = DateTime.UtcNow;
    public WrenchWiseStore Store { get; set; } = new();
}
