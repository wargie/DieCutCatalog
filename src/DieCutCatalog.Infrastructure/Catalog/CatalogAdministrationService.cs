using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace DieCutCatalog.Infrastructure.Catalog;

public sealed class CatalogAdministrationService(CatalogDbContext dbContext) : ICatalogAdministrationService
{
    public async Task<CatalogReferences> GetReferencesAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.EnsureCatalogReferencesAsync(cancellationToken);
        var entries = await dbContext.CatalogReferenceEntries.AsNoTracking()
            .OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var equipment = await dbContext.Equipment.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new CatalogReferenceItem(x.Id, CatalogReferenceType.Equipment, x.Name))
            .ToListAsync(cancellationToken);

        return new CatalogReferences(
            entries.Where(x => x.Kind == CatalogReferenceKind.Material)
                .Select(x => new CatalogReferenceItem(x.Id, CatalogReferenceType.Material, x.Name)).ToArray(),
            entries.Where(x => x.Kind == CatalogReferenceKind.Figure)
                .Select(x => new CatalogReferenceItem(x.Id, CatalogReferenceType.Figure, x.Name)).ToArray(),
            equipment);
    }

    public async Task<CatalogReferenceItem> AddReferenceAsync(
        CatalogReferenceType type, string name, CancellationToken cancellationToken = default)
    {
        var clean = ValidateName(name, type);
        var normalized = Normalize(clean);
        if (type == CatalogReferenceType.Equipment)
        {
            if (await dbContext.Equipment.AnyAsync(x => x.NormalizedName == normalized, cancellationToken))
                throw new ValidationException("Такое оборудование уже есть в справочнике.");
            var equipment = new Equipment { Name = clean, NormalizedName = normalized };
            dbContext.Equipment.Add(equipment);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CatalogReferenceItem(equipment.Id, type, equipment.Name);
        }

        var kind = ToKind(type);
        if (await dbContext.CatalogReferenceEntries.AnyAsync(
                x => x.Kind == kind && x.NormalizedName == normalized, cancellationToken))
            throw new ValidationException("Такое значение уже есть в справочнике.");
        var entry = new CatalogReferenceEntry { Kind = kind, Name = clean, NormalizedName = normalized };
        dbContext.CatalogReferenceEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CatalogReferenceItem(entry.Id, type, entry.Name);
    }

    public async Task<CatalogReferenceItem?> RenameReferenceAsync(
        CatalogReferenceType type, Guid id, string name, CancellationToken cancellationToken = default)
    {
        var clean = ValidateName(name, type);
        var normalized = Normalize(clean);
        if (type == CatalogReferenceType.Equipment)
        {
            var equipment = await dbContext.Equipment.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (equipment is null) return null;
            if (await dbContext.Equipment.AnyAsync(x => x.Id != id && x.NormalizedName == normalized, cancellationToken))
                throw new ValidationException("Такое оборудование уже есть в справочнике.");
            equipment.Name = clean;
            equipment.NormalizedName = normalized;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CatalogReferenceItem(equipment.Id, type, equipment.Name);
        }

        var kind = ToKind(type);
        var entry = await dbContext.CatalogReferenceEntries.SingleOrDefaultAsync(
            x => x.Id == id && x.Kind == kind, cancellationToken);
        if (entry is null) return null;
        if (await dbContext.CatalogReferenceEntries.AnyAsync(
                x => x.Id != id && x.Kind == kind && x.NormalizedName == normalized, cancellationToken))
            throw new ValidationException("Такое значение уже есть в справочнике.");

        var oldName = entry.Name;
        entry.Name = clean;
        entry.NormalizedName = normalized;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        var affected = type == CatalogReferenceType.Material
            ? await dbContext.DieCuts.Where(x => x.Material.ToLower() == oldName.ToLower()).ToListAsync(cancellationToken)
            : await dbContext.DieCuts.Where(x => x.Figure.ToLower() == oldName.ToLower()).ToListAsync(cancellationToken);
        foreach (var dieCut in affected)
        {
            if (type == CatalogReferenceType.Material) dieCut.Material = clean;
            else dieCut.Figure = clean;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CatalogReferenceItem(entry.Id, type, entry.Name);
    }

    public async Task<bool> DeleteReferenceAsync(
        CatalogReferenceType type, Guid id, CancellationToken cancellationToken = default)
    {
        if (type == CatalogReferenceType.Equipment)
        {
            var equipment = await dbContext.Equipment.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (equipment is null) return false;
            if (await dbContext.DieCuts.AnyAsync(x => x.EquipmentId == id, cancellationToken))
                throw new ValidationException("Нельзя удалить оборудование: оно используется в карточках ножей.");
            dbContext.Equipment.Remove(equipment);
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var kind = ToKind(type);
        var entry = await dbContext.CatalogReferenceEntries.SingleOrDefaultAsync(
            x => x.Id == id && x.Kind == kind, cancellationToken);
        if (entry is null) return false;
        var name = entry.Name.ToLower();
        var isUsed = type == CatalogReferenceType.Material
            ? await dbContext.DieCuts.AnyAsync(x => x.Material.ToLower() == name, cancellationToken)
            : await dbContext.DieCuts.AnyAsync(x => x.Figure.ToLower() == name, cancellationToken);
        if (isUsed)
            throw new ValidationException("Нельзя удалить значение: оно используется в карточках ножей.");

        dbContext.CatalogReferenceEntries.Remove(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
    public async Task<PagedResult<AuditLogEntry>> SearchAuditLogAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var query = AuditQuery(search);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.OccurredAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new AuditLogEntry(
                x.Id, x.DieCutId, x.DieCut.Number, x.DieCut.Equipment.Name, x.Type, x.Quantity,
                x.MileageBefore, x.MileageAfter, x.RunLengthMetersBefore, x.RunLengthMetersAfter,
                x.RevolutionsBefore, x.RevolutionsAfter, x.OccurredAt,
                (x.Employee.FirstName + " " + x.Employee.LastName).Trim()))
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditLogEntry>(items, total, page, pageSize);
    }

    public async Task<ExportedFile> ExportAuditLogAsync(
        string? search, bool pdf, CancellationToken cancellationToken = default)
    {
        var entries = await AuditQuery(search).OrderByDescending(x => x.OccurredAt)
            .Select(x => new AuditLogEntry(
                x.Id, x.DieCutId, x.DieCut.Number, x.DieCut.Equipment.Name, x.Type, x.Quantity,
                x.MileageBefore, x.MileageAfter, x.RunLengthMetersBefore, x.RunLengthMetersAfter,
                x.RevolutionsBefore, x.RevolutionsAfter, x.OccurredAt,
                (x.Employee.FirstName + " " + x.Employee.LastName).Trim()))
            .ToListAsync(cancellationToken);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return pdf
            ? new ExportedFile($"knife-audit-{stamp}.pdf", "application/pdf", BuildPdf(entries))
            : new ExportedFile($"knife-audit-{stamp}.csv", "text/csv; charset=utf-8", BuildCsv(entries));
    }

    private IQueryable<DieCutEvent> AuditQuery(string? search)
    {
        var query = dbContext.DieCutEvents.AsNoTracking();
        if (string.IsNullOrWhiteSpace(search)) return query;
        var term = search.Trim().ToLower();
        return query.Where(x =>
            x.DieCut.Number.ToLower().Contains(term) ||
            x.DieCut.Equipment.Name.ToLower().Contains(term) ||
            x.Employee.FirstName.ToLower().Contains(term) ||
            x.Employee.LastName.ToLower().Contains(term));
    }

    private static byte[] BuildCsv(IEnumerable<AuditLogEntry> entries)
    {
        var csv = new StringBuilder("\uFEFFДата;Нож;Оборудование;Действие;Тираж;Пробег до;Пробег после;Метры до;Метры после;Обороты до;Обороты после;Сотрудник\r\n");
        foreach (var x in entries)
        {
            csv.Append(Csv(x.OccurredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"))).Append(';')
                .Append(Csv(x.DieCutNumber)).Append(';').Append(Csv(x.Equipment)).Append(';')
                .Append(Csv(EventName(x.Type))).Append(';').Append(x.Quantity?.ToString(CultureInfo.InvariantCulture)).Append(';')
                .Append(x.MileageBefore).Append(';').Append(x.MileageAfter).Append(';')
                .Append(x.RunLengthMetersBefore.ToString("0.###", CultureInfo.InvariantCulture)).Append(';')
                .Append(x.RunLengthMetersAfter.ToString("0.###", CultureInfo.InvariantCulture)).Append(';')
                .Append(x.RevolutionsBefore).Append(';').Append(x.RevolutionsAfter).Append(';')
                .Append(Csv(x.EmployeeName)).Append("\r\n");
        }
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static byte[] BuildPdf(IReadOnlyList<AuditLogEntry> entries)
    {
        DieCutDrawingPdfGenerator.EnsureFontResolver();
        using var document = new PdfDocument();
        document.Info.Title = "Журнал действий по ножам";
        const double margin = 28;
        const double rowHeight = 18;
        PdfPage? page = null;
        XGraphics? graphics = null;
        var y = 0d;
        var regular = new XFont("Arial", 6.5, XFontStyleEx.Regular);
        var bold = new XFont("Arial", 7, XFontStyleEx.Bold);
        var columns = new[] { 82d, 45d, 80d, 88d, 58d, 98d, 94d, 98d, 85d };

        void NewPage()
        {
            graphics?.Dispose();
            page = document.AddPage();
            page.Orientation = PdfSharp.PageOrientation.Landscape;
            graphics = XGraphics.FromPdfPage(page);
            graphics.DrawString("Журнал действий по ножам", new XFont("Arial", 14, XFontStyleEx.Bold),
                XBrushes.Black, new XRect(margin, 10, page.Width.Point - margin * 2, 24), XStringFormats.TopLeft);
            y = 45;
            graphics.DrawString($"Страница {document.PageCount}", regular, XBrushes.Gray,
                new XPoint(page.Width.Point - margin - 48, page.Height.Point - 12));
            DrawRow(new[] { "Дата", "Нож", "Оборудование", "Действие", "Тираж", "Пробег", "Метры", "Обороты", "Сотрудник" }, bold);
        }

        void DrawRow(string[] values, XFont font)
        {
            var x = margin;
            for (var i = 0; i < columns.Length; i++)
            {
                graphics!.DrawRectangle(XPens.LightGray, x, y, columns[i], rowHeight);
                graphics.DrawString(values[i], font, XBrushes.Black,
                    new XRect(x + 3, y + 3, columns[i] - 6, rowHeight - 4), XStringFormats.TopLeft);
                x += columns[i];
            }
            y += rowHeight;
        }

        NewPage();
        foreach (var entry in entries)
        {
            if (y + rowHeight > page!.Height.Point - margin) NewPage();
            DrawRow(new[]
            {
                entry.OccurredAt.ToLocalTime().ToString("dd.MM.yy HH:mm"),
                entry.DieCutNumber, entry.Equipment, EventName(entry.Type),
                entry.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "",
                $"{entry.MileageBefore} -> {entry.MileageAfter}",
                $"{entry.RunLengthMetersBefore.ToString("0.##", CultureInfo.InvariantCulture)} -> {entry.RunLengthMetersAfter.ToString("0.##", CultureInfo.InvariantCulture)}",
                $"{entry.RevolutionsBefore} -> {entry.RevolutionsAfter}",
                entry.EmployeeName
            }, regular);
        }
        graphics?.Dispose();
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static CatalogReferenceKind ToKind(CatalogReferenceType type) => type switch
    {
        CatalogReferenceType.Material => CatalogReferenceKind.Material,
        CatalogReferenceType.Figure => CatalogReferenceKind.Figure,
        _ => throw new ValidationException("Некорректный тип справочника.")
    };
    private static string ValidateName(string name, CatalogReferenceType type)
    {
        var clean = name?.Trim() ?? "";
        var max = type == CatalogReferenceType.Equipment ? 150 : 200;
        if (clean.Length == 0 || clean.Length > max)
            throw new ValidationException($"Название должно содержать от 1 до {max} символов.");
        return clean;
    }
    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static string EventName(DieCutEventType type) => type switch
    {
        DieCutEventType.Created => "Нож создан",
        DieCutEventType.Updated => "Параметры изменены",
        DieCutEventType.CirculationAdded => "Добавлен тираж",
        DieCutEventType.MileageReset => "Пробег сброшен",
            DieCutEventType.ReplacementInstalled => "Установлен новый нож",
        DieCutEventType.Retired => "Нож списан",
        DieCutEventType.DrawingGenerated => "PDF сформирован",
        DieCutEventType.Deleted => "Нож удалён",
        _ => type.ToString()
    };
}
