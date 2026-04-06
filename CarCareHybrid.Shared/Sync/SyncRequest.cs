using CarCareHybrid.Shared.Models;

namespace CarCareHybrid.Shared.Sync;

public class SyncRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public List<SyncOperation> PendingOperations { get; set; } = [];
    public DateTime ClientUtc { get; set; } = DateTime.UtcNow;
    public CarCareStore? FullStoreFallback { get; set; }
}
