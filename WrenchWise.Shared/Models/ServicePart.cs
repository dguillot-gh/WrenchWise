namespace WrenchWise.Shared.Models;

public class ServicePart
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PartName { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public decimal Cost { get; set; }
}
