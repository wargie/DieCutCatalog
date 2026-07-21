using System.ComponentModel.DataAnnotations;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DieCutCatalog.Infrastructure.Catalog;

public sealed class DieCutCatalogService(CatalogDbContext dbContext) : IDieCutCatalogService
{
    public async Task<PagedResult<DieCutSummary>> SearchAsync(DieCutQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var dieCuts = dbContext.DieCuts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            dieCuts = dieCuts.Where(x =>
                x.Number.ToLower().Contains(search)
                || x.Material.ToLower().Contains(search)
                || x.Shape.ToLower().Contains(search)
                || (x.Comments != null && x.Comments.ToLower().Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(query.Equipment))
            dieCuts = dieCuts.Where(x => x.Equipment.NormalizedName == Normalize(query.Equipment));
        if (!string.IsNullOrWhiteSpace(query.Material))
            dieCuts = dieCuts.Where(x => x.Material.ToLower() == query.Material.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(query.Shape))
            dieCuts = dieCuts.Where(x => x.Shape.ToLower() == query.Shape.Trim().ToLower());
        if (query.Status is not null) dieCuts = dieCuts.Where(x => x.Status == query.Status);
        if (query.MinWidthMm is not null) dieCuts = dieCuts.Where(x => x.WidthMm >= query.MinWidthMm);
        if (query.MaxWidthMm is not null) dieCuts = dieCuts.Where(x => x.WidthMm <= query.MaxWidthMm);
        if (query.MinLengthMm is not null) dieCuts = dieCuts.Where(x => x.LengthMm >= query.MinLengthMm);
        if (query.MaxLengthMm is not null) dieCuts = dieCuts.Where(x => x.LengthMm <= query.MaxLengthMm);
        if (query.ShaftRepeatMm is not null) dieCuts = dieCuts.Where(x => x.ShaftRepeatMm == query.ShaftRepeatMm);

        var total = await dieCuts.CountAsync(cancellationToken);
        var items = await dieCuts
            .OrderBy(x => x.Equipment.Name)
            .ThenBy(x => x.NormalizedNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new DieCutSummary(
                x.Id, x.Number, x.Equipment.Name, x.ShaftRepeatMm, x.WidthMm, x.LengthMm,
                x.Streams, x.Repeats, x.Material, x.MaterialWidthMm, x.KnifeHeightMicrons, x.Shape, x.Status, x.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<DieCutSummary>(items, total, page, pageSize);
    }

    public Task<DieCutDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.DieCuts.AsNoTracking().Where(x => x.Id == id).Select(ToDetails()).SingleOrDefaultAsync(cancellationToken);

    public async Task<DieCutDetails> CreateAsync(SaveDieCutCommand command, Guid employeeId, CancellationToken cancellationToken = default)
    {
        Validate(command);
        var equipment = await GetOrCreateEquipmentAsync(command.Equipment, cancellationToken);
        var normalizedNumber = Normalize(command.Number);
        if (await dbContext.DieCuts.AnyAsync(x => x.EquipmentId == equipment.Id && x.NormalizedNumber == normalizedNumber, cancellationToken))
            throw new ValidationException("Нож с таким номером уже существует для выбранного оборудования.");

        var dieCut = new DieCut { Equipment = equipment, EquipmentId = equipment.Id, CreatedByEmployeeId = employeeId };
        Apply(dieCut, command, employeeId);
        dbContext.DieCuts.Add(dieCut);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(dieCut, equipment.Name);
    }

    public async Task<DieCutDetails?> UpdateAsync(Guid id, SaveDieCutCommand command, Guid employeeId, CancellationToken cancellationToken = default)
    {
        Validate(command);
        var dieCut = await dbContext.DieCuts.Include(x => x.Equipment).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dieCut is null) return null;
        var equipment = await GetOrCreateEquipmentAsync(command.Equipment, cancellationToken);
        var normalizedNumber = Normalize(command.Number);
        if (await dbContext.DieCuts.AnyAsync(x => x.Id != id && x.EquipmentId == equipment.Id && x.NormalizedNumber == normalizedNumber, cancellationToken))
            throw new ValidationException("Нож с таким номером уже существует для выбранного оборудования.");

        dieCut.Equipment = equipment;
        dieCut.EquipmentId = equipment.Id;
        Apply(dieCut, command, employeeId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(dieCut, equipment.Name);
    }

    public async Task<CatalogFacets> GetFacetsAsync(CancellationToken cancellationToken = default) => new(
        await dbContext.Equipment.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => x.Name).ToListAsync(cancellationToken),
        await dbContext.DieCuts.AsNoTracking().Select(x => x.Material).Distinct().OrderBy(x => x).ToListAsync(cancellationToken),
        await dbContext.DieCuts.AsNoTracking().Select(x => x.Shape).Distinct().OrderBy(x => x).ToListAsync(cancellationToken));

    private async Task<Equipment> GetOrCreateEquipmentAsync(string name, CancellationToken cancellationToken)
    {
        var normalized = Normalize(name);
        var equipment = await dbContext.Equipment.SingleOrDefaultAsync(x => x.NormalizedName == normalized, cancellationToken);
        if (equipment is not null) return equipment;
        equipment = new Equipment { Name = name.Trim(), NormalizedName = normalized };
        dbContext.Equipment.Add(equipment);
        return equipment;
    }

    private static void Apply(DieCut target, SaveDieCutCommand source, Guid employeeId)
    {
        target.Number = source.Number.Trim();
        target.NormalizedNumber = Normalize(source.Number);
        target.ShaftRepeatMm = source.ShaftRepeatMm;
        target.WidthMm = source.WidthMm;
        target.LengthMm = source.LengthMm;
        target.Streams = source.Streams;
        target.Repeats = source.Repeats;
        target.GapAcrossMm = source.GapAcrossMm;
        target.GapAlongMm = source.GapAlongMm;
        target.Material = source.Material.Trim();
        target.MaterialWidthMm = source.MaterialWidthMm;
        target.KnifeHeightMicrons = source.KnifeHeightMicrons;
        target.Shape = source.Shape.Trim();
        target.Comments = string.IsNullOrWhiteSpace(source.Comments) ? null : source.Comments.Trim();
        target.CommissionedOn = source.CommissionedOn;
        target.Status = source.Status;
        target.UpdatedByEmployeeId = employeeId;
        target.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void Validate(SaveDieCutCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Number) || command.Number.Length > 50) throw new ValidationException("Укажите номер ножа длиной до 50 символов.");
        if (string.IsNullOrWhiteSpace(command.Equipment) || command.Equipment.Length > 150) throw new ValidationException("Укажите оборудование.");
        if (string.IsNullOrWhiteSpace(command.Material) || command.Material.Length > 200) throw new ValidationException("Укажите материал.");
        if (string.IsNullOrWhiteSpace(command.Shape) || command.Shape.Length > 100) throw new ValidationException("Укажите форму ножа.");
        if (command.ShaftRepeatMm <= 0 || command.WidthMm <= 0 || command.LengthMm <= 0) throw new ValidationException("Раппорт вала и размеры ножа должны быть больше нуля.");
        if (command.Streams <= 0 || command.Repeats <= 0) throw new ValidationException("Количество ручьёв и повторов должно быть больше нуля.");
        if (command.GapAcrossMm < 0 || command.GapAlongMm < 0 || command.MaterialWidthMm <= 0) throw new ValidationException("Зазоры не могут быть отрицательными, а ширина материала должна быть больше нуля.");
        if (command.KnifeHeightMicrons is <= 0) throw new ValidationException("Высота ножа должна быть больше нуля.");
        if (command.Comments?.Length > 2000) throw new ValidationException("Комментарий не должен превышать 2000 символов.");
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static DieCutDetails Map(DieCut x, string equipment) => new(x.Id, x.Number, equipment, x.ShaftRepeatMm, x.WidthMm, x.LengthMm, x.Streams, x.Repeats, x.GapAcrossMm, x.GapAlongMm, x.Material, x.MaterialWidthMm, x.KnifeHeightMicrons, x.Shape, x.Comments, x.CommissionedOn, x.Status, x.CreatedAt, x.UpdatedAt);
    private static System.Linq.Expressions.Expression<Func<DieCut, DieCutDetails>> ToDetails() => x => new DieCutDetails(x.Id, x.Number, x.Equipment.Name, x.ShaftRepeatMm, x.WidthMm, x.LengthMm, x.Streams, x.Repeats, x.GapAcrossMm, x.GapAlongMm, x.Material, x.MaterialWidthMm, x.KnifeHeightMicrons, x.Shape, x.Comments, x.CommissionedOn, x.Status, x.CreatedAt, x.UpdatedAt);
}
