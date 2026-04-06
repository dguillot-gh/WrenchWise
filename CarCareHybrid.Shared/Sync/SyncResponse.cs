using CarCareHybrid.Shared.Models;

namespace CarCareHybrid.Shared.Sync;

public class SyncResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime ServerUtc { get; set; } = DateTime.UtcNow;
    public CarCareStore Store { get; set; } = new();
}
