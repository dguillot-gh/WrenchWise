namespace CarCareHybrid.Shared.Models;

public class TireRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VehicleId { get; set; }
    public string Position { get; set; } = string.Empty;
    public string BrandModel { get; set; } = string.Empty;
    public DateOnly InstalledDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int InstalledOdometer { get; set; }
    public DateOnly? RemovedDate { get; set; }
    public int? RemovedOdometer { get; set; }
    public decimal PurchaseCost { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
