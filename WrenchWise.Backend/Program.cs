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
