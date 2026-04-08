namespace WrenchWise.Shared.Models;

public class MaintenanceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VehicleId { get; set; }
    public string ServiceType { get; set; } = string.Empty;
    public DateOnly ServiceDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int Odometer { get; set; }
    public decimal Cost { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<ServicePart> Parts { get; set; } = new();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
