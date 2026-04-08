using System.Text;
using WrenchWise.Shared.Models;

namespace WrenchWise.Shared.Export;

public static class CsvExporter
{
    public static string ExportStoreToCsv(WrenchWiseStore store)
    {
        var sb = new StringBuilder();

        // Vehicles
        sb.AppendLine("=== VEHICLES ===");
        sb.AppendLine("Id,Year,Make,Model,Nickname,ColorHex");
        foreach (var v in store.Vehicles)
        {
            sb.AppendLine($"{v.Id},{v.Year},\"{EscapeCsv(v.Make)}\",\"{EscapeCsv(v.Model)}\",\"{EscapeCsv(v.Nickname)}\",{v.ColorHex}");
        }
        sb.AppendLine();

        // Maintenance
        sb.AppendLine("=== MAINTENANCE ===");
        sb.AppendLine("Id,VehicleId,ServiceDate,Odometer,ServiceType,Cost,ShopName,Notes,PartsCount");
        foreach (var m in store.MaintenanceRecords.OrderByDescending(x => x.ServiceDate))
        {
            var partsCount = m.Parts?.Count ?? 0;
            sb.AppendLine($"{m.Id},{m.VehicleId},{m.ServiceDate:yyyy-MM-dd},{m.Odometer},\"{EscapeCsv(m.ServiceType)}\",{m.Cost},\"{EscapeCsv(m.ShopName)}\",\"{EscapeCsv(m.Notes)}\",{partsCount}");
        }
        sb.AppendLine();

        // Fuel
        sb.AppendLine("=== FUEL LOGS ===");
        sb.AppendLine("Id,VehicleId,FillDate,Odometer,Gallons,TotalCost,FullTank,Station");
        foreach (var f in store.FuelRecords.OrderByDescending(x => x.FillDate))
        {
            sb.AppendLine($"{f.Id},{f.VehicleId},{f.FillDate:yyyy-MM-dd},{f.Odometer},{f.Gallons},{f.TotalCost},{f.FullTank},\"{EscapeCsv(f.Station)}\"");
        }
        sb.AppendLine();

        // Projects
        sb.AppendLine("=== PROJECTS ===");
        sb.AppendLine("Id,VehicleId,Title,Description,Status,TargetDate,EstimatedCost,ActualCost");
        foreach (var p in store.VehicleProjects.OrderByDescending(x => x.UpdatedUtc))
        {
            var target = p.TargetDate.HasValue ? p.TargetDate.Value.ToString("yyyy-MM-dd") : "";
            sb.AppendLine($"{p.Id},{p.VehicleId},\"{EscapeCsv(p.Title)}\",\"{EscapeCsv(p.Description)}\",{p.Status},{target},{p.EstimatedCost},{p.ActualCost}");
        }
        sb.AppendLine();
        
        // Documents
        sb.AppendLine("=== DOCUMENTS ===");
        sb.AppendLine("Id,VehicleId,DocumentType,Provider,PolicyNumber,EffectiveDate,ExpirationDate,PremiumCost");
        foreach (var d in store.VehicleDocuments.OrderByDescending(x => x.UpdatedUtc))
        {
            var eff = d.EffectiveDate.HasValue ? d.EffectiveDate.Value.ToString("yyyy-MM-dd") : "";
            var exp = d.ExpirationDate.HasValue ? d.ExpirationDate.Value.ToString("yyyy-MM-dd") : "";
            sb.AppendLine($"{d.Id},{d.VehicleId},{d.DocumentType},\"{EscapeCsv(d.Provider)}\",\"{EscapeCsv(d.PolicyNumber)}\",{eff},{exp},{d.PremiumCost}");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    private static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field))
            return "";
        return field.Replace("\"", "\"\"");
    }
}
