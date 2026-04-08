namespace WrenchWise.Shared.Models;

public class TireRotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int Odometer { get; set; }
}
