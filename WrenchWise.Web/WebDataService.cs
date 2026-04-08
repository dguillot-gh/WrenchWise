using WrenchWise.Shared.Models;
using System.Net.Http.Json;

namespace WrenchWise.Web;

/// <summary>
/// Singleton cache of the WrenchWise store fetched from the backend API.
/// All pages share the same loaded data; call RefreshAsync() to reload.
/// </summary>
public class WebDataService(IHttpClientFactory clientFactory)
{
    public WrenchWiseStore? Store { get; private set; }
    public bool IsLoaded { get; private set; }
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (IsLoaded) return;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var client = clientFactory.CreateClient("BackendAPI");
            Store = await client.GetFromJsonAsync<WrenchWiseStore>("/api/store");
        }
        catch
        {
            Store = new WrenchWiseStore();
        }
        IsLoaded = true;
        Changed?.Invoke();
    }

    // ── Computed helpers ─────────────────────────────────────────────────────

    public decimal GetTotalSpent(Guid vehicleId) =>
        (Store?.MaintenanceRecords.Where(x => x.VehicleId == vehicleId).Sum(x => x.Cost) ?? 0) +
        (Store?.FuelRecords.Where(x => x.VehicleId == vehicleId).Sum(x => x.TotalCost) ?? 0) +
        (Store?.TireRecords.Where(x => x.VehicleId == vehicleId).Sum(x => x.PurchaseCost) ?? 0);

    public decimal GetAllTimeSpent() =>
        (Store?.MaintenanceRecords.Sum(x => x.Cost) ?? 0) +
        (Store?.FuelRecords.Sum(x => x.TotalCost) ?? 0) +
        (Store?.TireRecords.Sum(x => x.PurchaseCost) ?? 0);

    public string GetAverageMpg(Guid vehicleId)
    {
        var fills = Store?.FuelRecords
            .Where(x => x.VehicleId == vehicleId && x.TripMiles > 0 && x.Gallons > 0)
            .ToList() ?? new();
        if (!fills.Any()) return "—";
        var mpg = fills.Average(x => x.TripMiles / (double)x.Gallons);
        return mpg.ToString("0.0") + " mpg";
    }

    public bool IsReminderDue(ServiceReminder r)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var vehicle = Store?.Vehicles.FirstOrDefault(v => v.Id == r.VehicleId);
        bool dateDue = r.DueDate.HasValue && r.DueDate.Value <= today;
        bool odoDue  = r.DueOdometer.HasValue && vehicle != null && vehicle.CurrentOdometer >= r.DueOdometer.Value;
        return !r.IsCompleted && (dateDue || odoDue);
    }

    public bool IsReminderOverdue(ServiceReminder r)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var vehicle = Store?.Vehicles.FirstOrDefault(v => v.Id == r.VehicleId);
        bool dateOdo = r.DueDate.HasValue && r.DueDate.Value < today.AddDays(-7);
        bool odoOver = r.DueOdometer.HasValue && vehicle != null && vehicle.CurrentOdometer > r.DueOdometer.Value + 500;
        return !r.IsCompleted && (dateOdo || odoOver);
    }

    public bool IsDocumentExpiring(VehicleDocument d)
    {
        if (!d.ExpirationDate.HasValue) return false;
        var today = DateOnly.FromDateTime(DateTime.Today);
        return d.ExpirationDate.Value > today && d.ExpirationDate.Value <= today.AddDays(30);
    }

    public bool IsDocumentExpired(VehicleDocument d)
    {
        if (!d.ExpirationDate.HasValue) return false;
        var today = DateOnly.FromDateTime(DateTime.Today);
        return d.ExpirationDate.Value <= today;
    }
}
