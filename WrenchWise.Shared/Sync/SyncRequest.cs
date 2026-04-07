using WrenchWise.Shared.Models;

namespace WrenchWise.Shared.Sync;

public class SyncRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public List<SyncOperation> PendingOperations { get; set; } = [];
    public DateTime ClientUtc { get; set; } = DateTime.UtcNow;
    public WrenchWiseStore? FullStoreFallback { get; set; }
}
