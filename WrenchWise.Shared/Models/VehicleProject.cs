namespace WrenchWise.Shared.Models;

public class VehicleProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VehicleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal EstimatedCost { get; set; }
    public decimal ActualCost { get; set; }
    public DateOnly? TargetDate { get; set; }
    public string Status { get; set; } = "Planning"; // Planning, InProgress, Completed
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
