namespace WrenchWise.Shared.Models;

public class Vehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nickname { get; set; } = string.Empty;
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; } = DateTime.Today.Year;
    public string Vin { get; set; } = string.Empty;
    public int CurrentOdometer { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#594AE2";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
