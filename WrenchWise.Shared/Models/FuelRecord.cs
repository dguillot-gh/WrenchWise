namespace WrenchWise.Shared.Models;

public class FuelRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VehicleId { get; set; }
    public DateOnly FillDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int Odometer { get; set; }
    public decimal Gallons { get; set; }
    public decimal TotalCost { get; set; }
    public bool FullTank { get; set; } = true;
    public string Station { get; set; } = string.Empty;
    public string FuelGrade { get; set; } = "Regular";
    public decimal? EthanolPercent { get; set; }
    public string AdditiveNotes { get; set; } = string.Empty;
    public string ReceiptImagePath { get; set; } = string.Empty;
    public string ReceiptOcrText { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
