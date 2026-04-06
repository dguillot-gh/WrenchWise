namespace CarCareHybrid.Shared.Models;

public class ServiceReminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VehicleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? DueOdometer { get; set; }
    public DateOnly? DueDate { get; set; }
    public int RepeatEveryMiles { get; set; }
    public int RepeatEveryDays { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
