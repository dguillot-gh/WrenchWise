using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WrenchWise.Shared.Models;
using WrenchWise.Shared.Sync;

namespace WrenchWise.Services;

public class WrenchWiseService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dataPath = Path.Combine(FileSystem.AppDataDirectory, "wrenchwise-offline.json");
    private bool _initialized;

    private OfflineStore _offline = new();

    public event Action? Changed;

    public IReadOnlyList<Vehicle> Vehicles => _offline.Store.Vehicles;
    public IReadOnlyList<MaintenanceRecord> MaintenanceRecords => _offline.Store.MaintenanceRecords;
    public IReadOnlyList<FuelRecord> FuelRecords => _offline.Store.FuelRecords;
    public IReadOnlyList<ServiceReminder> ServiceReminders => _offline.Store.ServiceReminders;
    public IReadOnlyList<TireRecord> TireRecords => _offline.Store.TireRecords;
    public int PendingSyncCount => _offline.PendingOperations.Count;
    public DateTime? LastSyncUtc => _offline.LastSyncUtc == default ? null : _offline.LastSyncUtc;
    public string ApiBaseUrl => _offline.ApiBaseUrl;
    public Guid ActiveVehicleId => _offline.ActiveVehicleId;
    public TripState? ActiveTrip => _offline.ActiveTrip;

    private static readonly FilePickerFileType JsonFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.Android, ["application/json", "text/json"] },
        { DevicePlatform.WinUI, [".json"] },
        { DevicePlatform.iOS, ["public.json"] },
        { DevicePlatform.MacCatalyst, ["public.json"] }
    });

    private static readonly FilePickerFileType CsvFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.Android, ["text/csv", "text/plain"] },
        { DevicePlatform.WinUI, [".csv", ".txt"] },
        { DevicePlatform.iOS, ["public.comma-separated-values-text", "public.plain-text"] },
        { DevicePlatform.MacCatalyst, ["public.comma-separated-values-text", "public.plain-text"] }
    });

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            if (File.Exists(_dataPath))
            {
                var json = await File.ReadAllTextAsync(_dataPath);
                _offline = JsonSerializer.Deserialize<OfflineStore>(json, JsonOptions) ?? new OfflineStore();
            }
            else
            {
                SeedDemoData();
                await PersistUnsafeAsync();
            }

            if (string.IsNullOrWhiteSpace(_offline.DeviceId))
            {
                _offline.DeviceId = Guid.NewGuid().ToString("N");
            }

            EnsureActiveVehicle();

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetApiBaseUrlAsync(string url)
    {
        _offline.ApiBaseUrl = url.Trim();
        await PersistAndNotifyAsync();
    }

    public async Task SyncNowAsync()
    {
        if (string.IsNullOrWhiteSpace(_offline.ApiBaseUrl))
        {
            throw new InvalidOperationException("Set API URL first.");
        }

        var request = new SyncRequest
        {
            DeviceId = _offline.DeviceId,
            ClientUtc = DateTime.UtcNow,
            PendingOperations = [.. _offline.PendingOperations]
        };

        if (_offline.PendingOperations.Count == 0 && _offline.Store.Vehicles.Count > 0 && _offline.LastSyncUtc == default)
        {
            request.FullStoreFallback = _offline.Store;
        }

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        var endpoint = $"{_offline.ApiBaseUrl.TrimEnd('/')}/api/sync";
        var response = await client.PostAsJsonAsync(endpoint, request, JsonOptions);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SyncResponse>(JsonOptions);
        if (payload is null || !payload.Success)
        {
            throw new InvalidOperationException(payload?.Message ?? "Sync failed.");
        }

        _offline.Store = payload.Store;
        _offline.PendingOperations.Clear();
        _offline.LastSyncUtc = payload.ServerUtc;
        await PersistAndNotifyAsync();
    }

    public async Task AddVehicleAsync(Vehicle vehicle)
    {
        vehicle.UpdatedUtc = DateTime.UtcNow;
        _offline.Store.Vehicles.Add(vehicle);
        if (_offline.ActiveVehicleId == Guid.Empty)
        {
            _offline.ActiveVehicleId = vehicle.Id;
        }
        QueueUpsert(SyncOperationType.UpsertVehicle, vehicle.Id, vehicle);
        await PersistAndNotifyAsync();
    }

    public async Task UpdateVehicleAsync(Vehicle vehicle)
    {
        var existing = _offline.Store.Vehicles.FirstOrDefault(x => x.Id == vehicle.Id);
        if (existing is null)
        {
            return;
        }

        existing.Nickname = vehicle.Nickname;
        existing.Make = vehicle.Make;
        existing.Model = vehicle.Model;
        existing.Year = vehicle.Year;
        existing.Vin = vehicle.Vin;
        existing.CurrentOdometer = vehicle.CurrentOdometer;
        existing.Notes = vehicle.Notes;
        existing.UpdatedUtc = DateTime.UtcNow;

        QueueUpsert(SyncOperationType.UpsertVehicle, existing.Id, existing);
        await PersistAndNotifyAsync();
    }

    public async Task RemoveVehicleAsync(Guid vehicleId)
    {
        _offline.Store.Vehicles.RemoveAll(v => v.Id == vehicleId);
        _offline.Store.MaintenanceRecords.RemoveAll(x => x.VehicleId == vehicleId);
        _offline.Store.FuelRecords.RemoveAll(x => x.VehicleId == vehicleId);
        _offline.Store.ServiceReminders.RemoveAll(x => x.VehicleId == vehicleId);
        _offline.Store.TireRecords.RemoveAll(x => x.VehicleId == vehicleId);
        if (_offline.ActiveVehicleId == vehicleId)
        {
            _offline.ActiveVehicleId = _offline.Store.Vehicles.FirstOrDefault()?.Id ?? Guid.Empty;
        }
        QueueDelete(SyncOperationType.DeleteVehicle, vehicleId);
        await PersistAndNotifyAsync();
    }

    public async Task SetActiveVehicleAsync(Guid vehicleId)
    {
        if (vehicleId == Guid.Empty || _offline.Store.Vehicles.Any(v => v.Id == vehicleId))
        {
            _offline.ActiveVehicleId = vehicleId;
            await PersistAndNotifyAsync();
        }
    }

    public int GetSuggestedOdometer(Guid vehicleId)
    {
        var vehicle = _offline.Store.Vehicles.FirstOrDefault(v => v.Id == vehicleId);
        if (vehicle is null)
        {
            return 0;
        }

        var maxFuel = _offline.Store.FuelRecords.Where(x => x.VehicleId == vehicleId).Select(x => x.Odometer).DefaultIfEmpty(0).Max();
        var maxMaintenance = _offline.Store.MaintenanceRecords.Where(x => x.VehicleId == vehicleId).Select(x => x.Odometer).DefaultIfEmpty(0).Max();
        var maxTires = _offline.Store.TireRecords.Where(x => x.VehicleId == vehicleId).Select(x => x.InstalledOdometer).DefaultIfEmpty(0).Max();
        return Math.Max(vehicle.CurrentOdometer, Math.Max(maxFuel, Math.Max(maxMaintenance, maxTires)));
    }

    public async Task UpdateVehicleOdometerAsync(Guid vehicleId, int odometer)
    {
        var vehicle = _offline.Store.Vehicles.FirstOrDefault(v => v.Id == vehicleId);
        if (vehicle is null)
        {
            return;
        }

        vehicle.CurrentOdometer = odometer;
        vehicle.UpdatedUtc = DateTime.UtcNow;
        QueueUpsert(SyncOperationType.UpsertVehicle, vehicle.Id, vehicle);
        await PersistAndNotifyAsync();
    }

    public async Task AddMaintenanceRecordAsync(MaintenanceRecord record)
    {
        record.UpdatedUtc = DateTime.UtcNow;
        _offline.Store.MaintenanceRecords.Add(record);
        QueueUpsert(SyncOperationType.UpsertMaintenance, record.Id, record);
        await UpdateVehicleOdometerAsync(record.VehicleId, record.Odometer);
        await PersistAndNotifyAsync();
    }

    public async Task DeleteMaintenanceRecordAsync(Guid recordId)
    {
        _offline.Store.MaintenanceRecords.RemoveAll(r => r.Id == recordId);
        QueueDelete(SyncOperationType.DeleteMaintenance, recordId);
        await PersistAndNotifyAsync();
    }

    public async Task AddFuelRecordAsync(FuelRecord record)
    {
        record.UpdatedUtc = DateTime.UtcNow;
        _offline.Store.FuelRecords.Add(record);
        QueueUpsert(SyncOperationType.UpsertFuel, record.Id, record);
        await UpdateVehicleOdometerAsync(record.VehicleId, record.Odometer);
        await PersistAndNotifyAsync();
    }

    public async Task UpdateFuelRecordAsync(FuelRecord record)
    {
        var existing = _offline.Store.FuelRecords.FirstOrDefault(x => x.Id == record.Id);
        if (existing is null)
        {
            return;
        }

        existing.FillDate = record.FillDate;
        existing.Odometer = record.Odometer;
        existing.Gallons = record.Gallons;
        existing.TotalCost = record.TotalCost;
        existing.FullTank = record.FullTank;
        existing.Station = record.Station;
        existing.FuelGrade = record.FuelGrade;
        existing.EthanolPercent = record.EthanolPercent;
        existing.AdditiveNotes = record.AdditiveNotes;
        existing.ReceiptImagePath = record.ReceiptImagePath;
        existing.ReceiptOcrText = record.ReceiptOcrText;
        existing.UpdatedUtc = DateTime.UtcNow;

        QueueUpsert(SyncOperationType.UpsertFuel, existing.Id, existing);
        await PersistAndNotifyAsync();
    }

    public async Task DeleteFuelRecordAsync(Guid recordId)
    {
        _offline.Store.FuelRecords.RemoveAll(r => r.Id == recordId);
        QueueDelete(SyncOperationType.DeleteFuel, recordId);
        await PersistAndNotifyAsync();
    }

    public async Task AddTireRecordAsync(TireRecord record)
    {
        record.UpdatedUtc = DateTime.UtcNow;
        _offline.Store.TireRecords.Add(record);
        QueueUpsert(SyncOperationType.UpsertTire, record.Id, record);
        await PersistAndNotifyAsync();
    }

    public async Task UpdateTireRecordAsync(TireRecord record)
    {
        var existing = _offline.Store.TireRecords.FirstOrDefault(x => x.Id == record.Id);
        if (existing is null)
        {
            return;
        }

        existing.Position = record.Position;
        existing.BrandModel = record.BrandModel;
        existing.InstalledDate = record.InstalledDate;
        existing.InstalledOdometer = record.InstalledOdometer;
        existing.RemovedDate = record.RemovedDate;
        existing.RemovedOdometer = record.RemovedOdometer;
        existing.PurchaseCost = record.PurchaseCost;
        existing.Notes = record.Notes;
        existing.UpdatedUtc = DateTime.UtcNow;

        QueueUpsert(SyncOperationType.UpsertTire, existing.Id, existing);
        await PersistAndNotifyAsync();
    }

    public async Task DeleteTireRecordAsync(Guid recordId)
    {
        _offline.Store.TireRecords.RemoveAll(r => r.Id == recordId);
        QueueDelete(SyncOperationType.DeleteTire, recordId);
        await PersistAndNotifyAsync();
    }

    public async Task AddReminderAsync(ServiceReminder reminder)
    {
        reminder.UpdatedUtc = DateTime.UtcNow;
        _offline.Store.ServiceReminders.Add(reminder);
        QueueUpsert(SyncOperationType.UpsertReminder, reminder.Id, reminder);
        await PersistAndNotifyAsync();
    }

    public async Task ToggleReminderStatusAsync(Guid reminderId)
    {
        var reminder = _offline.Store.ServiceReminders.FirstOrDefault(r => r.Id == reminderId);
        if (reminder is null)
        {
            return;
        }

        reminder.IsCompleted = !reminder.IsCompleted;
        reminder.UpdatedUtc = DateTime.UtcNow;
        QueueUpsert(SyncOperationType.UpsertReminder, reminder.Id, reminder);
        await PersistAndNotifyAsync();
    }

    public async Task DeleteReminderAsync(Guid reminderId)
    {
        _offline.Store.ServiceReminders.RemoveAll(r => r.Id == reminderId);
        QueueDelete(SyncOperationType.DeleteReminder, reminderId);
        await PersistAndNotifyAsync();
    }

    public async Task<bool> StartTripAsync(Guid vehicleId, string tripName, int startOdometer)
    {
        var vehicle = _offline.Store.Vehicles.FirstOrDefault(v => v.Id == vehicleId);
        if (vehicle is null || string.IsNullOrWhiteSpace(tripName))
        {
            return false;
        }

        _offline.ActiveTrip = new TripState
        {
            VehicleId = vehicleId,
            TripName = tripName.Trim(),
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            StartOdometer = startOdometer
        };
        await PersistAndNotifyAsync();
        return true;
    }

    public async Task EndTripAsync(int endOdometer)
    {
        if (_offline.ActiveTrip is null)
        {
            return;
        }

        await UpdateVehicleOdometerAsync(_offline.ActiveTrip.VehicleId, endOdometer);
        _offline.ActiveTrip = null;
        await PersistAndNotifyAsync();
    }

    public async Task<bool> AddTripFuelAsync(decimal gallons, decimal totalCost, int odometer, string station, string fuelGrade)
    {
        if (_offline.ActiveTrip is null)
        {
            return false;
        }

        var trip = _offline.ActiveTrip;
        await AddFuelRecordAsync(new FuelRecord
        {
            VehicleId = trip.VehicleId,
            FillDate = DateOnly.FromDateTime(DateTime.Today),
            Odometer = odometer,
            Gallons = gallons,
            TotalCost = totalCost,
            FullTank = true,
            Station = station,
            FuelGrade = string.IsNullOrWhiteSpace(fuelGrade) ? "Regular" : fuelGrade,
            AdditiveNotes = $"Trip: {trip.TripName}"
        });
        return true;
    }

    public async Task<bool> AddTripExpenseAsync(decimal amount, int odometer, string description)
    {
        if (_offline.ActiveTrip is null)
        {
            return false;
        }

        var trip = _offline.ActiveTrip;
        await AddMaintenanceRecordAsync(new MaintenanceRecord
        {
            VehicleId = trip.VehicleId,
            ServiceDate = DateOnly.FromDateTime(DateTime.Today),
            Odometer = odometer,
            Cost = amount,
            ServiceType = $"Trip Expense: {(string.IsNullOrWhiteSpace(description) ? "Misc" : description.Trim())}",
            ShopName = "Trip"
        });
        return true;
    }

    public async Task<string?> AttachReceiptPhotoAsync(Guid fuelRecordId)
    {
        var fuel = _offline.Store.FuelRecords.FirstOrDefault(x => x.Id == fuelRecordId);
        if (fuel is null)
        {
            return null;
        }

        IReadOnlyList<FileResult>? results;
        try
        {
            results = await MediaPicker.Default.PickPhotosAsync();
        }
        catch
        {
            return null;
        }

        var result = results?.FirstOrDefault();
        if (result is null)
        {
            return null;
        }

        var receiptsDir = Path.Combine(FileSystem.AppDataDirectory, "receipts");
        Directory.CreateDirectory(receiptsDir);

        var extension = Path.GetExtension(result.FileName);
        var localPath = Path.Combine(receiptsDir, $"{fuelRecordId:N}-{DateTime.UtcNow:yyyyMMddHHmmss}{extension}");

        await using var source = await result.OpenReadAsync();
        await using var destination = File.Create(localPath);
        await source.CopyToAsync(destination);

        fuel.ReceiptImagePath = localPath;
        fuel.UpdatedUtc = DateTime.UtcNow;
        QueueUpsert(SyncOperationType.UpsertFuel, fuel.Id, fuel);
        await PersistAndNotifyAsync();
        return localPath;
    }

    public async Task ApplyReceiptTextAsync(Guid fuelRecordId, string receiptText)
    {
        var fuel = _offline.Store.FuelRecords.FirstOrDefault(x => x.Id == fuelRecordId);
        if (fuel is null)
        {
            return;
        }

        fuel.ReceiptOcrText = receiptText;

        var gallons = TryParseDecimal(receiptText, @"(\d{1,2}\.\d{1,3})\s*(gal|gallon)");
        var total = TryParseDecimal(receiptText, @"(?:\$|total[:\s]*)(\d{1,4}\.\d{1,2})");

        if (gallons.HasValue && gallons.Value > 0)
        {
            fuel.Gallons = gallons.Value;
        }

        if (total.HasValue && total.Value > 0)
        {
            fuel.TotalCost = total.Value;
        }

        fuel.UpdatedUtc = DateTime.UtcNow;
        QueueUpsert(SyncOperationType.UpsertFuel, fuel.Id, fuel);
        await PersistAndNotifyAsync();
    }

    public decimal GetAverageMpg(Guid vehicleId)
    {
        var segments = BuildFuelSegments(vehicleId);
        if (segments.Count == 0)
        {
            return 0;
        }

        var totalMiles = segments.Sum(x => x.Miles);
        var totalGallons = segments.Sum(x => x.Gallons);
        return totalGallons == 0 ? 0 : Math.Round(totalMiles / totalGallons, 2);
    }

    public decimal GetTotalSpent(Guid vehicleId)
    {
        var maintenance = _offline.Store.MaintenanceRecords.Where(m => m.VehicleId == vehicleId).Sum(m => m.Cost);
        var fuel = _offline.Store.FuelRecords.Where(f => f.VehicleId == vehicleId).Sum(f => f.TotalCost);
        var tires = _offline.Store.TireRecords.Where(t => t.VehicleId == vehicleId).Sum(t => t.PurchaseCost);
        return maintenance + fuel + tires;
    }

    public decimal GetCostPerMile(Guid vehicleId)
    {
        var odometers = new List<int>();
        odometers.AddRange(_offline.Store.FuelRecords.Where(x => x.VehicleId == vehicleId).Select(x => x.Odometer));
        odometers.AddRange(_offline.Store.MaintenanceRecords.Where(x => x.VehicleId == vehicleId).Select(x => x.Odometer));
        odometers.AddRange(_offline.Store.TireRecords.Where(x => x.VehicleId == vehicleId).Select(x => x.InstalledOdometer));

        if (odometers.Count < 2)
        {
            return 0;
        }

        var miles = odometers.Max() - odometers.Min();
        if (miles <= 0)
        {
            return 0;
        }

        return Math.Round(GetTotalSpent(vehicleId) / miles, 4);
    }

    public List<GradeMpg> GetMpgByGrade(Guid vehicleId)
    {
        return BuildFuelSegments(vehicleId)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Grade) ? "Unknown" : x.Grade)
            .Select(g => new GradeMpg(
                g.Key,
                Math.Round(g.Sum(x => x.Miles) / Math.Max(g.Sum(x => x.Gallons), 0.0001m), 2)))
            .OrderByDescending(x => x.Mpg)
            .ToList();
    }

    public List<MonthlySpend> GetMonthlySpend(Guid vehicleId, int months = 6)
    {
        var start = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-(months - 1));
        var items = new List<MonthlySpend>();

        for (var i = 0; i < months; i++)
        {
            var month = start.AddMonths(i);
            var fuel = _offline.Store.FuelRecords
                .Where(x => x.VehicleId == vehicleId && x.FillDate.Year == month.Year && x.FillDate.Month == month.Month)
                .Sum(x => x.TotalCost);
            var maintenance = _offline.Store.MaintenanceRecords
                .Where(x => x.VehicleId == vehicleId && x.ServiceDate.Year == month.Year && x.ServiceDate.Month == month.Month)
                .Sum(x => x.Cost);
            var tires = _offline.Store.TireRecords
                .Where(x => x.VehicleId == vehicleId && x.InstalledDate.Year == month.Year && x.InstalledDate.Month == month.Month)
                .Sum(x => x.PurchaseCost);

            items.Add(new MonthlySpend(month.ToString("yyyy-MM"), fuel, maintenance, tires));
        }

        return items;
    }

    public bool IsReminderDue(ServiceReminder reminder)
    {
        if (reminder.IsCompleted)
        {
            return false;
        }

        var vehicle = _offline.Store.Vehicles.FirstOrDefault(v => v.Id == reminder.VehicleId);
        if (vehicle is null)
        {
            return false;
        }

        var dueByMileage = reminder.DueOdometer.HasValue && vehicle.CurrentOdometer >= reminder.DueOdometer.Value;
        var dueByDate = reminder.DueDate.HasValue && DateOnly.FromDateTime(DateTime.Today) >= reminder.DueDate.Value;
        return dueByDate || dueByMileage;
    }

    public async Task<string> ExportJsonBackupAsync()
    {
        var backupDir = Path.Combine(FileSystem.AppDataDirectory, "backups");
        Directory.CreateDirectory(backupDir);

        var filePath = Path.Combine(backupDir, $"wrenchwise-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        var payload = JsonSerializer.Serialize(_offline.Store, JsonOptions);
        await File.WriteAllTextAsync(filePath, payload);
        return filePath;
    }

    public async Task<string> ExportCsvBackupAsync()
    {
        var backupDir = Path.Combine(FileSystem.AppDataDirectory, "backups");
        Directory.CreateDirectory(backupDir);

        var filePath = Path.Combine(backupDir, $"wrenchwise-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        var sb = new StringBuilder();

        sb.AppendLine("Type,Vehicle,Date,Odometer,Amount,Notes");
        foreach (var fuel in _offline.Store.FuelRecords.OrderByDescending(x => x.FillDate))
        {
            sb.AppendLine($"Fuel,{FindVehicleName(fuel.VehicleId)},{fuel.FillDate:yyyy-MM-dd},{fuel.Odometer},{fuel.TotalCost},\"{Escape(fuel.Station)}\"");
        }

        foreach (var service in _offline.Store.MaintenanceRecords.OrderByDescending(x => x.ServiceDate))
        {
            sb.AppendLine($"Service,{FindVehicleName(service.VehicleId)},{service.ServiceDate:yyyy-MM-dd},{service.Odometer},{service.Cost},\"{Escape(service.ServiceType)}\"");
        }

        foreach (var tire in _offline.Store.TireRecords.OrderByDescending(x => x.InstalledDate))
        {
            sb.AppendLine($"Tire,{FindVehicleName(tire.VehicleId)},{tire.InstalledDate:yyyy-MM-dd},{tire.InstalledOdometer},{tire.PurchaseCost},\"{Escape(tire.BrandModel)}\"");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
        return filePath;
    }

    public async Task<ImportResult> ImportFromJsonAsync()
    {
        FileResult? file;
        try
        {
            file = await FilePicker.Default.PickAsync(new PickOptions
            {
                FileTypes = JsonFileType,
                PickerTitle = "Pick WrenchWise JSON Backup"
            });
        }
        catch (Exception ex)
        {
            return new ImportResult(false, $"Import failed: {ex.Message}");
        }

        if (file is null)
        {
            return new ImportResult(false, "No file selected.");
        }

        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        var imported = JsonSerializer.Deserialize<WrenchWiseStore>(json, JsonOptions);
        if (imported is null)
        {
            return new ImportResult(false, "JSON file did not contain valid WrenchWise data.");
        }

        _offline.Store = imported;
        EnsureActiveVehicle();
        RebuildPendingUpsertsFromStore();
        await PersistAndNotifyAsync();
        return new ImportResult(true, "JSON import complete. Sync to push imported data to backend.");
    }

    public async Task<ImportResult> ImportFromCsvAsync()
    {
        FileResult? file;
        try
        {
            file = await FilePicker.Default.PickAsync(new PickOptions
            {
                FileTypes = CsvFileType,
                PickerTitle = "Pick WrenchWise CSV Backup"
            });
        }
        catch (Exception ex)
        {
            return new ImportResult(false, $"Import failed: {ex.Message}");
        }

        if (file is null)
        {
            return new ImportResult(false, "No file selected.");
        }

        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 1)
        {
            return new ImportResult(false, "CSV was empty.");
        }

        var importedCount = 0;
        foreach (var raw in lines.Skip(1))
        {
            var columns = ParseCsvLine(raw);
            if (columns.Count < 6)
            {
                continue;
            }

            var type = columns[0].Trim();
            var vehicleName = columns[1].Trim();
            var dateValue = columns[2].Trim();
            var odometer = int.TryParse(columns[3], out var odo) ? odo : 0;
            var amount = decimal.TryParse(columns[4], out var amt) ? amt : 0;
            var notes = columns[5];

            var vehicle = ResolveOrCreateVehicle(vehicleName, odometer);
            if (vehicle is null)
            {
                continue;
            }

            if (type.Equals("Fuel", StringComparison.OrdinalIgnoreCase))
            {
                _offline.Store.FuelRecords.Add(new FuelRecord
                {
                    VehicleId = vehicle.Id,
                    FillDate = TryParseDate(dateValue),
                    Odometer = odometer,
                    TotalCost = amount,
                    Gallons = 0,
                    Station = notes,
                    FuelGrade = "Regular",
                    UpdatedUtc = DateTime.UtcNow
                });
                importedCount++;
            }
            else if (type.Equals("Service", StringComparison.OrdinalIgnoreCase))
            {
                _offline.Store.MaintenanceRecords.Add(new MaintenanceRecord
                {
                    VehicleId = vehicle.Id,
                    ServiceDate = TryParseDate(dateValue),
                    Odometer = odometer,
                    Cost = amount,
                    ServiceType = string.IsNullOrWhiteSpace(notes) ? "Imported Service" : notes,
                    UpdatedUtc = DateTime.UtcNow
                });
                importedCount++;
            }
            else if (type.Equals("Tire", StringComparison.OrdinalIgnoreCase))
            {
                _offline.Store.TireRecords.Add(new TireRecord
                {
                    VehicleId = vehicle.Id,
                    Position = "Unknown",
                    BrandModel = string.IsNullOrWhiteSpace(notes) ? "Imported Tire" : notes,
                    InstalledDate = TryParseDate(dateValue),
                    InstalledOdometer = odometer,
                    PurchaseCost = amount,
                    UpdatedUtc = DateTime.UtcNow
                });
                importedCount++;
            }
        }

        EnsureActiveVehicle();
        RebuildPendingUpsertsFromStore();
        await PersistAndNotifyAsync();
        return new ImportResult(true, $"CSV import complete. Imported {importedCount} rows.");
    }

    private static string Escape(string value) => value.Replace("\"", "\"\"");

    private string FindVehicleName(Guid vehicleId) => _offline.Store.Vehicles.FirstOrDefault(x => x.Id == vehicleId)?.Nickname ?? "Unknown";

    private static decimal? TryParseDecimal(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return decimal.TryParse(match.Groups[1].Value, out var parsed) ? parsed : null;
    }

    private List<FuelSegment> BuildFuelSegments(Guid vehicleId)
    {
        var fullRecords = _offline.Store.FuelRecords
            .Where(x => x.VehicleId == vehicleId && x.FullTank && x.Gallons > 0)
            .OrderBy(x => x.Odometer)
            .ToList();

        var segments = new List<FuelSegment>();
        for (var i = 1; i < fullRecords.Count; i++)
        {
            var current = fullRecords[i];
            var previous = fullRecords[i - 1];
            var miles = current.Odometer - previous.Odometer;
            if (miles <= 0 || current.Gallons <= 0)
            {
                continue;
            }

            segments.Add(new FuelSegment(miles, current.Gallons, current.FuelGrade));
        }

        return segments;
    }

    private void QueueUpsert<T>(SyncOperationType type, Guid entityId, T entity)
    {
        _offline.PendingOperations.Add(new SyncOperation
        {
            Type = type,
            EntityId = entityId,
            PayloadJson = JsonSerializer.Serialize(entity, JsonOptions),
            CreatedUtc = DateTime.UtcNow
        });
    }

    private void QueueDelete(SyncOperationType type, Guid entityId)
    {
        _offline.PendingOperations.Add(new SyncOperation
        {
            Type = type,
            EntityId = entityId,
            PayloadJson = string.Empty,
            CreatedUtc = DateTime.UtcNow
        });
    }

    private async Task PersistAndNotifyAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await PersistUnsafeAsync();
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke();
    }

    private Task PersistUnsafeAsync()
    {
        var json = JsonSerializer.Serialize(_offline, JsonOptions);
        return File.WriteAllTextAsync(_dataPath, json);
    }

    private void SeedDemoData()
    {
        // Production: start with a clean slate
    }

    private void EnsureActiveVehicle()
    {
        if (_offline.Store.Vehicles.Count == 0)
        {
            _offline.ActiveVehicleId = Guid.Empty;
            return;
        }

        if (_offline.ActiveVehicleId == Guid.Empty || _offline.Store.Vehicles.All(v => v.Id != _offline.ActiveVehicleId))
        {
            _offline.ActiveVehicleId = _offline.Store.Vehicles[0].Id;
        }
    }

    private void RebuildPendingUpsertsFromStore()
    {
        _offline.PendingOperations.Clear();
        foreach (var v in _offline.Store.Vehicles)
        {
            QueueUpsert(SyncOperationType.UpsertVehicle, v.Id, v);
        }

        foreach (var m in _offline.Store.MaintenanceRecords)
        {
            QueueUpsert(SyncOperationType.UpsertMaintenance, m.Id, m);
        }

        foreach (var f in _offline.Store.FuelRecords)
        {
            QueueUpsert(SyncOperationType.UpsertFuel, f.Id, f);
        }

        foreach (var t in _offline.Store.TireRecords)
        {
            QueueUpsert(SyncOperationType.UpsertTire, t.Id, t);
        }

        foreach (var r in _offline.Store.ServiceReminders)
        {
            QueueUpsert(SyncOperationType.UpsertReminder, r.Id, r);
        }
    }

    private Vehicle? ResolveOrCreateVehicle(string vehicleName, int odometer)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
        {
            vehicleName = "Imported Vehicle";
        }

        var existing = _offline.Store.Vehicles.FirstOrDefault(v => v.Nickname.Equals(vehicleName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (odometer > existing.CurrentOdometer)
            {
                existing.CurrentOdometer = odometer;
                existing.UpdatedUtc = DateTime.UtcNow;
            }

            return existing;
        }

        var created = new Vehicle
        {
            Nickname = vehicleName,
            Make = "Imported",
            Model = "Vehicle",
            Year = DateTime.Today.Year,
            CurrentOdometer = odometer,
            UpdatedUtc = DateTime.UtcNow
        };
        _offline.Store.Vehicles.Add(created);
        return created;
    }

    private static DateOnly TryParseDate(string value)
    {
        return DateOnly.TryParse(value, out var parsed) ? parsed : DateOnly.FromDateTime(DateTime.Today);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                values.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        values.Add(sb.ToString());
        return values;
    }
}

public class OfflineStore
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string ApiBaseUrl { get; set; } = "http://192.168.1.10:18080";
    public Guid ActiveVehicleId { get; set; }
    public TripState? ActiveTrip { get; set; }
    public DateTime LastSyncUtc { get; set; }
    public WrenchWiseStore Store { get; set; } = new();
    public List<SyncOperation> PendingOperations { get; set; } = [];
}

public record MonthlySpend(string Month, decimal Fuel, decimal Maintenance, decimal Tires);
public record GradeMpg(string Grade, decimal Mpg);
public record FuelSegment(decimal Miles, decimal Gallons, string Grade);
public record ImportResult(bool Success, string Message);
public class TripState
{
    public Guid VehicleId { get; set; }
    public string TripName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int StartOdometer { get; set; }
}
