using System.Text.Json;
using WrenchWise.Backend.Data;
using WrenchWise.Shared.Models;
using WrenchWise.Shared.Sync;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<WrenchWiseDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("WrenchWiseDb")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WrenchWiseDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapGet("/api/health", () => Results.Ok(new
{
    service = "wrenchwise-backend",
    utc = DateTime.UtcNow
}));

app.MapGet("/api/store", async (WrenchWiseDbContext db) =>
{
    return Results.Ok(await BuildStoreAsync(db));
});

app.MapPost("/api/sync", async (SyncRequest request, WrenchWiseDbContext db) =>
{
    var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    foreach (var operation in request.PendingOperations.OrderBy(x => x.CreatedUtc))
    {
        var alreadyApplied = await db.AppliedSyncOperations.AnyAsync(x => x.OperationId == operation.OperationId);
        if (alreadyApplied)
        {
            continue;
        }

        await ApplyOperationAsync(operation, db, serializerOptions);
        db.AppliedSyncOperations.Add(new AppliedSyncOperation
        {
            OperationId = operation.OperationId,
            AppliedUtc = DateTime.UtcNow
        });
    }

    if (request.PendingOperations.Count == 0 && request.FullStoreFallback is not null)
    {
        await ReplaceStoreFromFallbackAsync(request.FullStoreFallback, db);
    }

    await db.SaveChangesAsync();

    return Results.Ok(new SyncResponse
    {
        Success = true,
        Message = "Sync complete.",
        ServerUtc = DateTime.UtcNow,
        Store = await BuildStoreAsync(db)
    });
});

app.MapDelete("/api/seed/reset", async (WrenchWiseDbContext db) =>
{
    db.TireRecords.RemoveRange(db.TireRecords);
    db.FuelRecords.RemoveRange(db.FuelRecords);
    db.MaintenanceRecords.RemoveRange(db.MaintenanceRecords);
    db.ServiceReminders.RemoveRange(db.ServiceReminders);
    db.Vehicles.RemoveRange(db.Vehicles);
    db.AppliedSyncOperations.RemoveRange(db.AppliedSyncOperations);
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "All data wiped. Ready for real data." });
});

app.Run();

static async Task<WrenchWiseStore> BuildStoreAsync(WrenchWiseDbContext db)
{
    return new WrenchWiseStore
    {
        Vehicles = await db.Vehicles.AsNoTracking().OrderByDescending(x => x.Year).ThenBy(x => x.Make).ToListAsync(),
        MaintenanceRecords = await db.MaintenanceRecords.AsNoTracking().OrderByDescending(x => x.ServiceDate).ToListAsync(),
        FuelRecords = await db.FuelRecords.AsNoTracking().OrderByDescending(x => x.FillDate).ToListAsync(),
        ServiceReminders = await db.ServiceReminders.AsNoTracking().OrderBy(x => x.IsCompleted).ThenBy(x => x.Title).ToListAsync(),
        TireRecords = await db.TireRecords.AsNoTracking().OrderByDescending(x => x.InstalledDate).ToListAsync()
    };
}

static async Task ReplaceStoreFromFallbackAsync(WrenchWiseStore fallback, WrenchWiseDbContext db)
{
    db.Vehicles.RemoveRange(db.Vehicles);
    db.MaintenanceRecords.RemoveRange(db.MaintenanceRecords);
    db.FuelRecords.RemoveRange(db.FuelRecords);
    db.ServiceReminders.RemoveRange(db.ServiceReminders);
    db.TireRecords.RemoveRange(db.TireRecords);
    await db.SaveChangesAsync();

    db.Vehicles.AddRange(fallback.Vehicles);
    db.MaintenanceRecords.AddRange(fallback.MaintenanceRecords);
    db.FuelRecords.AddRange(fallback.FuelRecords);
    db.ServiceReminders.AddRange(fallback.ServiceReminders);
    db.TireRecords.AddRange(fallback.TireRecords);
}

static Task ApplyOperationAsync(SyncOperation operation, WrenchWiseDbContext db, JsonSerializerOptions serializerOptions)
{
    switch (operation.Type)
    {
        case SyncOperationType.UpsertVehicle:
            return UpsertVehicleAsync(operation.PayloadJson, db, serializerOptions);
        case SyncOperationType.DeleteVehicle:
            return DeleteVehicleAsync(operation.EntityId, db);
        case SyncOperationType.UpsertMaintenance:
            return UpsertMaintenanceAsync(operation.PayloadJson, db, serializerOptions);
        case SyncOperationType.DeleteMaintenance:
            return DeleteMaintenanceAsync(operation.EntityId, db);
        case SyncOperationType.UpsertFuel:
            return UpsertFuelAsync(operation.PayloadJson, db, serializerOptions);
        case SyncOperationType.DeleteFuel:
            return DeleteFuelAsync(operation.EntityId, db);
        case SyncOperationType.UpsertTire:
            return UpsertTireAsync(operation.PayloadJson, db, serializerOptions);
        case SyncOperationType.DeleteTire:
            return DeleteTireAsync(operation.EntityId, db);
        case SyncOperationType.UpsertReminder:
            return UpsertReminderAsync(operation.PayloadJson, db, serializerOptions);
        case SyncOperationType.DeleteReminder:
            return DeleteReminderAsync(operation.EntityId, db);
        default:
            return Task.CompletedTask;
    }
}

static async Task UpsertVehicleAsync(string payloadJson, WrenchWiseDbContext db, JsonSerializerOptions serializerOptions)
{
    var incoming = JsonSerializer.Deserialize<Vehicle>(payloadJson, serializerOptions);
    if (incoming is null)
    {
        return;
    }

    var existing = await db.Vehicles.FindAsync(incoming.Id);
    if (existing is null)
    {
        db.Vehicles.Add(incoming);
        return;
    }

    db.Entry(existing).CurrentValues.SetValues(incoming);
}

static async Task UpsertMaintenanceAsync(string payloadJson, WrenchWiseDbContext db, JsonSerializerOptions serializerOptions)
{
    var incoming = JsonSerializer.Deserialize<MaintenanceRecord>(payloadJson, serializerOptions);
    if (incoming is null)
    {
        return;
    }

    var existing = await db.MaintenanceRecords.FindAsync(incoming.Id);
    if (existing is null)
    {
        db.MaintenanceRecords.Add(incoming);
        return;
    }

    db.Entry(existing).CurrentValues.SetValues(incoming);
}

static async Task UpsertFuelAsync(string payloadJson, WrenchWiseDbContext db, JsonSerializerOptions serializerOptions)
{
    var incoming = JsonSerializer.Deserialize<FuelRecord>(payloadJson, serializerOptions);
    if (incoming is null)
    {
        return;
    }

    var existing = await db.FuelRecords.FindAsync(incoming.Id);
    if (existing is null)
    {
        db.FuelRecords.Add(incoming);
        return;
    }

    db.Entry(existing).CurrentValues.SetValues(incoming);
}

static async Task UpsertReminderAsync(string payloadJson, WrenchWiseDbContext db, JsonSerializerOptions serializerOptions)
{
    var incoming = JsonSerializer.Deserialize<ServiceReminder>(payloadJson, serializerOptions);
    if (incoming is null)
    {
        return;
    }

    var existing = await db.ServiceReminders.FindAsync(incoming.Id);
    if (existing is null)
    {
        db.ServiceReminders.Add(incoming);
        return;
    }

    db.Entry(existing).CurrentValues.SetValues(incoming);
}

static async Task UpsertTireAsync(string payloadJson, WrenchWiseDbContext db, JsonSerializerOptions serializerOptions)
{
    var incoming = JsonSerializer.Deserialize<TireRecord>(payloadJson, serializerOptions);
    if (incoming is null)
    {
        return;
    }

    var existing = await db.TireRecords.FindAsync(incoming.Id);
    if (existing is null)
    {
        db.TireRecords.Add(incoming);
        return;
    }

    db.Entry(existing).CurrentValues.SetValues(incoming);
}

static async Task DeleteVehicleAsync(Guid id, WrenchWiseDbContext db)
{
    var vehicle = await db.Vehicles.FindAsync(id);
    if (vehicle is null)
    {
        return;
    }

    db.Vehicles.Remove(vehicle);
    db.MaintenanceRecords.RemoveRange(db.MaintenanceRecords.Where(x => x.VehicleId == id));
    db.FuelRecords.RemoveRange(db.FuelRecords.Where(x => x.VehicleId == id));
    db.ServiceReminders.RemoveRange(db.ServiceReminders.Where(x => x.VehicleId == id));
    db.TireRecords.RemoveRange(db.TireRecords.Where(x => x.VehicleId == id));
}

static async Task DeleteMaintenanceAsync(Guid id, WrenchWiseDbContext db)
{
    var record = await db.MaintenanceRecords.FindAsync(id);
    if (record is not null)
    {
        db.MaintenanceRecords.Remove(record);
    }
}

static async Task DeleteFuelAsync(Guid id, WrenchWiseDbContext db)
{
    var record = await db.FuelRecords.FindAsync(id);
    if (record is not null)
    {
        db.FuelRecords.Remove(record);
    }
}

static async Task DeleteReminderAsync(Guid id, WrenchWiseDbContext db)
{
    var reminder = await db.ServiceReminders.FindAsync(id);
    if (reminder is not null)
    {
        db.ServiceReminders.Remove(reminder);
    }
}

static async Task DeleteTireAsync(Guid id, WrenchWiseDbContext db)
{
    var tire = await db.TireRecords.FindAsync(id);
    if (tire is not null)
    {
        db.TireRecords.Remove(tire);
    }
}

static async Task SeedDemoDataAsync(WrenchWiseDbContext db)
{
    // Only seed when the DB is completely empty
    if (await db.Vehicles.AnyAsync()) return;

    var now = DateTime.UtcNow;
    var today = DateOnly.FromDateTime(DateTime.Today);

    // ── Vehicles ────────────────────────────────────────────────────────────
    var tacoma = new Vehicle { Id = Guid.NewGuid(), Nickname = "Big Red", Make = "Toyota", Model = "Tacoma", Year = 2022, Vin = "3TMCZ5AN6NM123456", CurrentOdometer = 47250, UpdatedUtc = now };
    var civic   = new Vehicle { Id = Guid.NewGuid(), Nickname = "Daily Driver", Make = "Honda",  Model = "Civic",  Year = 2019, Vin = "2HGFC2F69KH234567", CurrentOdometer = 82100, UpdatedUtc = now };
    var f150    = new Vehicle { Id = Guid.NewGuid(), Nickname = "The Truck",    Make = "Ford",   Model = "F-150",  Year = 2017, Vin = "1FTEW1EP6HKD90345", CurrentOdometer = 118400, UpdatedUtc = now };
    db.Vehicles.AddRange(tacoma, civic, f150);

    // ── Maintenance Records ──────────────────────────────────────────────────
    db.MaintenanceRecords.AddRange(
        // Tacoma
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, ServiceType = "Oil Change",        ServiceDate = today.AddDays(-14),  Odometer = 47100, Cost = 79.99m,  ShopName = "Jiffy Lube",       Notes = "Full synthetic 5W-30", UpdatedUtc = now },
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, ServiceType = "Tire Rotation",     ServiceDate = today.AddDays(-14),  Odometer = 47100, Cost = 29.99m,  ShopName = "Jiffy Lube",       Notes = "",                     UpdatedUtc = now },
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, ServiceType = "Air Filter",        ServiceDate = today.AddDays(-90),  Odometer = 44800, Cost = 34.50m,  ShopName = "AutoZone (DIY)",   Notes = "K&N replacement",      UpdatedUtc = now },
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, ServiceType = "Brake Fluid Flush", ServiceDate = today.AddDays(-180), Odometer = 41200, Cost = 89.00m,  ShopName = "Toyota Dealer",    Notes = "DOT 3",                UpdatedUtc = now },
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, ServiceType = "Wiper Blades",      ServiceDate = today.AddDays(-45),  Odometer = 46500, Cost = 22.00m,  ShopName = "Walmart",          Notes = "Bosch Icon",           UpdatedUtc = now },

        // Civic
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, ServiceType = "Oil Change",         ServiceDate = today.AddDays(-7),   Odometer = 81900, Cost = 64.99m,  ShopName = "Honda Dealer",     Notes = "0W-20 full synthetic", UpdatedUtc = now },
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, ServiceType = "Tire Rotation",      ServiceDate = today.AddDays(-7),   Odometer = 81900, Cost = 0m,      ShopName = "Honda Dealer",     Notes = "Complimentary with oil change", UpdatedUtc = now },
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, ServiceType = "Brake Pads (Front)", ServiceDate = today.AddDays(-120), Odometer = 78400, Cost = 245.00m, ShopName = "Midas",            Notes = "TRW premium pads",     UpdatedUtc = now },
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, ServiceType = "Cabin Air Filter",   ServiceDate = today.AddDays(-60),  Odometer = 80100, Cost = 19.99m,  ShopName = "DIY",              Notes = "",                     UpdatedUtc = now },
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, ServiceType = "Spark Plugs",        ServiceDate = today.AddDays(-365), Odometer = 72000, Cost = 120.00m, ShopName = "Honda Dealer",     Notes = "NGK iridium",          UpdatedUtc = now },

        // F-150
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, ServiceType = "Oil Change",          ServiceDate = today.AddDays(-30),  Odometer = 117800, Cost = 99.99m,  ShopName = "Ford Dealer",     Notes = "5W-30 synthetic blend",  UpdatedUtc = now },
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, ServiceType = "Transmission Service",ServiceDate = today.AddDays(-180), Odometer = 114000, Cost = 289.00m, ShopName = "AAMCO",           Notes = "Flush + new filter",     UpdatedUtc = now },
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, ServiceType = "Brake Pads (All)",    ServiceDate = today.AddDays(-270), Odometer = 110500, Cost = 420.00m, ShopName = "Ford Dealer",     Notes = "OEM pads and rotors",    UpdatedUtc = now },
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, ServiceType = "Battery Replacement", ServiceDate = today.AddDays(-90),  Odometer = 116200, Cost = 145.00m, ShopName = "AutoZone",        Notes = "Optima Red Top",         UpdatedUtc = now },
        new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, ServiceType = "Coolant Flush",       ServiceDate = today.AddDays(-365), Odometer = 108000, Cost = 119.00m, ShopName = "Jiffy Lube",      Notes = "",                       UpdatedUtc = now }
    );

    // ── Fuel Records ────────────────────────────────────────────────────────
    // Helper: date = today minus N days
    static DateOnly D(DateOnly t, int days) => t.AddDays(-days);

    db.FuelRecords.AddRange(
        // Tacoma (fills every ~12 days)
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, FillDate = D(today, 3),   Odometer = 47245, TripMiles = 312, Gallons = 20.1m, TotalCost = 64.32m, Station = "Shell",    FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, FillDate = D(today, 15),  Odometer = 46933, TripMiles = 318, Gallons = 20.4m, TotalCost = 65.89m, Station = "Costco",  FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, FillDate = D(today, 28),  Odometer = 46615, TripMiles = 305, Gallons = 19.8m, TotalCost = 62.14m, Station = "Chevron", FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, FillDate = D(today, 42),  Odometer = 46310, TripMiles = 298, Gallons = 19.5m, TotalCost = 59.72m, Station = "Shell",    FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, FillDate = D(today, 55),  Odometer = 46012, TripMiles = 310, Gallons = 20.2m, TotalCost = 63.43m, Station = "Costco",  FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, FillDate = D(today, 70),  Odometer = 45702, TripMiles = 322, Gallons = 20.8m, TotalCost = 67.18m, Station = "BP",      FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, FillDate = D(today, 84),  Odometer = 45380, TripMiles = 308, Gallons = 20.0m, TotalCost = 62.00m, Station = "Chevron", FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, FillDate = D(today, 97),  Odometer = 45072, TripMiles = 315, Gallons = 20.3m, TotalCost = 64.96m, Station = "Shell",    FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, FillDate = D(today, 111), Odometer = 44757, TripMiles = 302, Gallons = 19.6m, TotalCost = 61.65m, Station = "Costco",  FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, FillDate = D(today, 125), Odometer = 44455, TripMiles = 320, Gallons = 20.6m, TotalCost = 66.32m, Station = "Exxon",   FuelGrade = "Regular", UpdatedUtc = now },

        // Civic (fills every ~9 days, more frequent, smaller tank)
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, FillDate = D(today, 2),    Odometer = 82090, TripMiles = 360, Gallons = 10.8m, TotalCost = 34.56m, Station = "Shell",    FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, FillDate = D(today, 11),   Odometer = 81730, TripMiles = 355, Gallons = 10.6m, TotalCost = 33.92m, Station = "Meijer",  FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, FillDate = D(today, 21),   Odometer = 81375, TripMiles = 363, Gallons = 10.9m, TotalCost = 35.18m, Station = "Costco",  FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, FillDate = D(today, 31),   Odometer = 81012, TripMiles = 358, Gallons = 10.7m, TotalCost = 34.24m, Station = "BP",      FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, FillDate = D(today, 41),   Odometer = 80654, TripMiles = 361, Gallons = 10.8m, TotalCost = 33.80m, Station = "Shell",    FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, FillDate = D(today, 51),   Odometer = 80293, TripMiles = 354, Gallons = 10.6m, TotalCost = 33.28m, Station = "Meijer",  FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, FillDate = D(today, 61),   Odometer = 79939, TripMiles = 360, Gallons = 10.8m, TotalCost = 34.56m, Station = "Costco",  FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, FillDate = D(today, 72),   Odometer = 79579, TripMiles = 362, Gallons = 10.9m, TotalCost = 35.49m, Station = "BP",      FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, FillDate = D(today, 82),   Odometer = 79217, TripMiles = 356, Gallons = 10.7m, TotalCost = 33.60m, Station = "Speedway",FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, FillDate = D(today, 93),   Odometer = 78861, TripMiles = 358, Gallons = 10.7m, TotalCost = 34.88m, Station = "Shell",    FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, FillDate = D(today, 103),  Odometer = 78503, TripMiles = 360, Gallons = 10.8m, TotalCost = 35.10m, Station = "Costco",  FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = civic.Id, FillDate = D(today, 114),  Odometer = 78143, TripMiles = 355, Gallons = 10.6m, TotalCost = 33.92m, Station = "Meijer",  FuelGrade = "Regular", UpdatedUtc = now },

        // F-150 (large tank, fills every ~16 days)
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, FillDate = D(today, 5),     Odometer = 118380, TripMiles = 380, Gallons = 28.4m, TotalCost = 91.17m, Station = "Pilot",  FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, FillDate = D(today, 22),    Odometer = 118000, TripMiles = 375, Gallons = 28.1m, TotalCost = 90.20m, Station = "Shell",   FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, FillDate = D(today, 38),    Odometer = 117625, TripMiles = 390, Gallons = 29.2m, TotalCost = 94.57m, Station = "Loves",  FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, FillDate = D(today, 55),    Odometer = 117235, TripMiles = 382, Gallons = 28.6m, TotalCost = 91.73m, Station = "Costco", FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, FillDate = D(today, 72),    Odometer = 116853, TripMiles = 377, Gallons = 28.2m, TotalCost = 89.96m, Station = "Pilot",  FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, FillDate = D(today, 90),    Odometer = 116476, TripMiles = 388, Gallons = 29.0m, TotalCost = 93.37m, Station = "BP",     FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, FillDate = D(today, 107),   Odometer = 116088, TripMiles = 374, Gallons = 28.0m, TotalCost = 89.60m, Station = "Shell",   FuelGrade = "Regular", UpdatedUtc = now },
        new FuelRecord { Id = Guid.NewGuid(), VehicleId = f150.Id, FillDate = D(today, 124),   Odometer = 115714, TripMiles = 385, Gallons = 28.8m, TotalCost = 94.18m, Station = "Loves",  FuelGrade = "Regular", UpdatedUtc = now }
    );

    // ── Tire Records ────────────────────────────────────────────────────────
    db.TireRecords.AddRange(
        new TireRecord { Id = Guid.NewGuid(), VehicleId = tacoma.Id, Position = "All Four",   BrandModel = "Falken Wildpeak AT3W 265/70R16",   InstalledDate = today.AddDays(-300), InstalledOdometer = 37000, PurchaseCost = 820.00m, Notes = "Installed at Discount Tire", UpdatedUtc = now },
        new TireRecord { Id = Guid.NewGuid(), VehicleId = civic.Id,  Position = "All Four",   BrandModel = "Michelin CrossClimate2 215/55R17", InstalledDate = today.AddDays(-180), InstalledOdometer = 75000, PurchaseCost = 680.00m, Notes = "Installed at Costco",         UpdatedUtc = now },
        new TireRecord { Id = Guid.NewGuid(), VehicleId = f150.Id,   Position = "All Four",   BrandModel = "BF Goodrich KO2 275/65R18",        InstalledDate = today.AddDays(-420), InstalledOdometer = 104000, PurchaseCost = 1100.00m,Notes = "Truck Outfitters",            UpdatedUtc = now }
    );

    // ── Service Reminders ────────────────────────────────────────────────────
    db.ServiceReminders.AddRange(
        new ServiceReminder { Id = Guid.NewGuid(), VehicleId = tacoma.Id, Title = "Oil Change",       DueOdometer = 52000, DueDate = today.AddDays(45),  RepeatEveryMiles = 5000, RepeatEveryDays = 90,  Notes = "Full synthetic only", IsCompleted = false, UpdatedUtc = now },
        new ServiceReminder { Id = Guid.NewGuid(), VehicleId = tacoma.Id, Title = "Tire Rotation",    DueOdometer = 50000, DueDate = today.AddDays(-10), RepeatEveryMiles = 5000, RepeatEveryDays = 0,   Notes = "",                    IsCompleted = false, UpdatedUtc = now },
        new ServiceReminder { Id = Guid.NewGuid(), VehicleId = civic.Id,  Title = "Oil Change",       DueOdometer = 87000, DueDate = today.AddDays(30),  RepeatEveryMiles = 5000, RepeatEveryDays = 90,  Notes = "0W-20 only",          IsCompleted = false, UpdatedUtc = now },
        new ServiceReminder { Id = Guid.NewGuid(), VehicleId = civic.Id,  Title = "Cabin Air Filter", DueOdometer = 90000, DueDate = today.AddDays(-25), RepeatEveryMiles = 15000, RepeatEveryDays = 365, Notes = "",                   IsCompleted = false, UpdatedUtc = now },
        new ServiceReminder { Id = Guid.NewGuid(), VehicleId = f150.Id,   Title = "Oil Change",       DueOdometer = 120000, DueDate = today.AddDays(15), RepeatEveryMiles = 5000, RepeatEveryDays = 90,  Notes = "5W-30 synth blend",   IsCompleted = false, UpdatedUtc = now }
    );

    await db.SaveChangesAsync();
}
