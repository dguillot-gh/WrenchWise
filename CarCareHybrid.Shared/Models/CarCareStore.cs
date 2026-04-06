namespace CarCareHybrid.Shared.Models;

public class CarCareStore
{
    public List<Vehicle> Vehicles { get; set; } = [];
    public List<MaintenanceRecord> MaintenanceRecords { get; set; } = [];
    public List<FuelRecord> FuelRecords { get; set; } = [];
    public List<ServiceReminder> ServiceReminders { get; set; } = [];
    public List<TireRecord> TireRecords { get; set; } = [];
}
