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
                || x.Figure.ToLower().Contains(search)
                || (x.Comments != null && x.Comments.ToLower().Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(query.Equipment))
            dieCuts = dieCuts.Where(x => x.Equipment.NormalizedName == Normalize(query.Equipment));
        if (!string.IsNullOrWhiteSpace(query.Material))
            dieCuts = dieCuts.Where(x => x.Material.ToLower() == query.Material.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(query.Figure))
            dieCuts = dieCuts.Where(x => x.Figure.ToLower() == query.Figure.Trim().ToLower());
        if (query.Status is not null) dieCuts = dieCuts.Where(x => x.Status == query.Status);
        if (query.MinX is not null) dieCuts = dieCuts.Where(x => x.X >= query.MinX);
        if (query.MaxX is not null) dieCuts = dieCuts.Where(x => x.X <= query.MaxX);
        if (query.MinY is not null) dieCuts = dieCuts.Where(x => x.Y >= query.MinY);
        if (query.MaxY is not null) dieCuts = dieCuts.Where(x => x.Y <= query.MaxY);
        if (query.Shaft is not null) dieCuts = dieCuts.Where(x => x.Shaft == query.Shaft);

        var total = await dieCuts.CountAsync(cancellationToken);
        var items = await dieCuts
            .OrderBy(x => x.Equipment.Name)
            .ThenBy(x => x.NormalizedNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new DieCutSummary(
                x.Id, x.Number, x.Equipment.Name, x.Shaft, x.X, x.Y, x.Streams, x.Repeats,
                x.GapX, x.GapY, x.Material, x.H, x.Figure, x.Comments, x.Date, x.Status, x.UpdatedAt))
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
        await dbContext.DieCuts.AsNoTracking().Select(x => x.Figure).Distinct().OrderBy(x => x).ToListAsync(cancellationToken));

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
        var (gapX, gapY) = DieCutCalculations.Calculate(source.Shaft, source.X, source.Y, source.Streams, source.Repeats, source.H);
        target.Number = source.Number.Trim();
        target.NormalizedNumber = Normalize(source.Number);
        target.Shaft = source.Shaft;
        target.X = source.X;
        target.Y = source.Y;
        target.Streams = source.Streams;
        target.Repeats = source.Repeats;
        target.GapX = gapX;
        target.GapY = gapY;
        target.Material = source.Material.Trim();
        target.H = source.H;
        target.Figure = source.Figure.Trim();
        target.Comments = string.IsNullOrWhiteSpace(source.Comments) ? null : source.Comments.Trim();
        target.Date = source.Date;
        target.Status = source.Status;
        target.UpdatedByEmployeeId = employeeId;
        target.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void Validate(SaveDieCutCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Number) || command.Number.Length > 50) throw new ValidationException("Укажите номер ножа длиной до 50 символов.");
        if (string.IsNullOrWhiteSpace(command.Equipment) || command.Equipment.Length > 150) throw new ValidationException("Укажите оборудование.");
        if (string.IsNullOrWhiteSpace(command.Material) || command.Material.Length > 200) throw new ValidationException("Укажите материал.");
        if (string.IsNullOrWhiteSpace(command.Figure) || command.Figure.Length > 100) throw new ValidationException("Укажите форму ножа.");
        if (command.Shaft <= 0 || command.X <= 0 || command.Y <= 0) throw new ValidationException("Раппорт вала и размеры ножа должны быть больше нуля.");
        if (command.Streams <= 0 || command.Repeats <= 0) throw new ValidationException("Количество ручьёв и повторов должно быть больше нуля.");
        if (command.H <= 0) throw new ValidationException("H должно быть больше нуля.");
        var (gapX, gapY) = DieCutCalculations.Calculate(command.Shaft, command.X, command.Y, command.Streams, command.Repeats, command.H);
        if (gapX < 0) throw new ValidationException("H не может быть меньше X × streams.");
        if (gapY < 0) throw new ValidationException("Длина окружности shaft не вмещает Y × repeats.");
        if (command.Comments?.Length > 2000) throw new ValidationException("Комментарий не должен превышать 2000 символов.");
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static DieCutDetails Map(DieCut x, string equipment) => new(x.Id, x.Number, equipment, x.Shaft, x.X, x.Y, x.Streams, x.Repeats, x.GapX, x.GapY, x.Material, x.H, x.Figure, x.Comments, x.Date, x.Status, x.CreatedAt, x.UpdatedAt);
    private static System.Linq.Expressions.Expression<Func<DieCut, DieCutDetails>> ToDetails() => x => new DieCutDetails(x.Id, x.Number, x.Equipment.Name, x.Shaft, x.X, x.Y, x.Streams, x.Repeats, x.GapX, x.GapY, x.Material, x.H, x.Figure, x.Comments, x.Date, x.Status, x.CreatedAt, x.UpdatedAt);
}
