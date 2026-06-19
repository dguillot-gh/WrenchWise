namespace WrenchWise.Shared.Models;

public class Trip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VehicleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int StartOdometer { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? EndOdometer { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
