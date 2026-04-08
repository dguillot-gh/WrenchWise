namespace WrenchWise.Shared.Models;

public class WrenchWiseStore
{
    public List<Vehicle> Vehicles { get; set; } = [];
    public List<MaintenanceRecord> MaintenanceRecords { get; set; } = [];
    public List<FuelRecord> FuelRecords { get; set; } = [];
    public List<ServiceReminder> ServiceReminders { get; set; } = [];
    public List<TireRecord> TireRecords { get; set; } = [];
    public List<VehicleProject> VehicleProjects { get; set; } = [];
    public List<VehicleDocument> VehicleDocuments { get; set; } = [];
}
