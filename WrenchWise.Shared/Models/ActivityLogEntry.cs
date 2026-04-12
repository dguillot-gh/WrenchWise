namespace WrenchWise.Shared.Models;

public class ActivityLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Category { get; set; } = string.Empty; // Sync, Edit, Delete, Error, System
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info"; // Info, Warning, Error
    public Guid? VehicleId { get; set; }
    public Guid? EntityId { get; set; }
}
