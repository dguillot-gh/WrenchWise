using WrenchWise.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace WrenchWise.Backend.Data;

public class WrenchWiseDbContext(DbContextOptions<WrenchWiseDbContext> options) : DbContext(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<FuelRecord> FuelRecords => Set<FuelRecord>();
    public DbSet<TireRecord> TireRecords => Set<TireRecord>();
    public DbSet<ServiceReminder> ServiceReminders => Set<ServiceReminder>();
    public DbSet<AppliedSyncOperation> AppliedSyncOperations => Set<AppliedSyncOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Vehicle>().HasKey(x => x.Id);
        modelBuilder.Entity<MaintenanceRecord>().HasKey(x => x.Id);
        modelBuilder.Entity<FuelRecord>().HasKey(x => x.Id);
        modelBuilder.Entity<TireRecord>().HasKey(x => x.Id);
        modelBuilder.Entity<ServiceReminder>().HasKey(x => x.Id);

        modelBuilder.Entity<AppliedSyncOperation>().HasKey(x => x.OperationId);
        modelBuilder.Entity<AppliedSyncOperation>()
            .Property(x => x.OperationId)
            .ValueGeneratedNever();
    }
}

public class AppliedSyncOperation
{
    public Guid OperationId { get; set; }
    public DateTime AppliedUtc { get; set; } = DateTime.UtcNow;
}
