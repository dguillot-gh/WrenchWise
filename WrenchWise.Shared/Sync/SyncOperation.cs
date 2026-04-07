namespace WrenchWise.Shared.Sync;

public class SyncOperation
{
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public SyncOperationType Type { get; set; }
    public Guid EntityId { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
