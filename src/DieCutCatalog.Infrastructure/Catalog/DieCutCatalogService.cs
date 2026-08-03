using System.ComponentModel.DataAnnotations;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DieCutCatalog.Infrastructure.Catalog;

public sealed class DieCutCatalogService(CatalogDbContext dbContext) : IDieCutCatalogService
{
    public const long InspectionIntervalRevolutions = 1_000_000;
    public const long WarningRevolutions = 900_000;
    public async Task<PagedResult<DieCutSummary>> SearchAsync(DieCutQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var dieCuts = dbContext.DieCuts.AsNoTracking().Where(x => x.Status != DieCutStatus.Deleted);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            dieCuts = dieCuts.Where(x =>
                x.Number.ToLower().Contains(search)
                || (x.JcOrderNumber != null && x.JcOrderNumber.ToLower().Contains(search))
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
        var ordered = query.SortBy switch
        {
            DieCutSortField.LabelWidth when query.SortDescending => dieCuts
                .OrderByDescending(x => x.X).ThenBy(x => x.Equipment.Name).ThenBy(x => x.NormalizedNumber),
            DieCutSortField.LabelWidth => dieCuts
                .OrderBy(x => x.X).ThenBy(x => x.Equipment.Name).ThenBy(x => x.NormalizedNumber),
            DieCutSortField.LabelLength when query.SortDescending => dieCuts
                .OrderByDescending(x => x.Y).ThenBy(x => x.Equipment.Name).ThenBy(x => x.NormalizedNumber),
            DieCutSortField.LabelLength => dieCuts
                .OrderBy(x => x.Y).ThenBy(x => x.Equipment.Name).ThenBy(x => x.NormalizedNumber),
            _ => dieCuts.OrderBy(x => x.Equipment.Name).ThenBy(x => x.NormalizedNumber)
        };
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new DieCutSummary(
                x.Id, x.Number, x.JcOrderNumber, x.Equipment.Name, x.Shaft, x.X, x.Y, x.Streams, x.Repeats,
                x.GapX, x.GapY, x.Material, x.H, x.Figure, x.Comments, x.Date, x.Mileage,
                x.RunLengthMeters, x.Revolutions, x.LifetimeMileage, x.LifetimeRunLengthMeters, x.LifetimeRevolutions,
                x.Generation, x.NextInspectionRevolutions, x.Status, x.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<DieCutSummary>(items, total, page, pageSize);
    }

    public Task<DieCutDetails?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.DieCuts.AsNoTracking().Where(x => x.Id == id).Select(ToDetails()).SingleOrDefaultAsync(cancellationToken);

    public async Task<DieCutDetails> CreateAsync(SaveDieCutCommand command, Guid employeeId, CancellationToken cancellationToken = default)
    {
        Validate(command);
        if (command.Status is DieCutStatus.Retired or DieCutStatus.Deleted)
            throw new ValidationException("Новый нож нельзя сразу списать или удалить.");

        var references = await ResolveReferencesAsync(command, cancellationToken);
        var equipment = references.Equipment;
        var normalizedNumber = Normalize(command.Number);
        if (await dbContext.DieCuts.AnyAsync(x => x.EquipmentId == equipment.Id && x.NormalizedNumber == normalizedNumber && x.Status != DieCutStatus.Deleted, cancellationToken))
            throw new ValidationException("Нож с таким номером уже существует для выбранного оборудования.");

        var dieCut = new DieCut { Equipment = equipment, EquipmentId = equipment.Id, CreatedByEmployeeId = employeeId };
        Apply(dieCut, command, employeeId, references.Material, references.Figure);
        dbContext.DieCuts.Add(dieCut);
        var usage = UsageSnapshot.From(dieCut);
        dbContext.DieCutEvents.Add(NewEvent(
            dieCut.Id, employeeId, DieCutEventType.Created, null, usage, usage, dieCut.CreatedAt));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(dieCut, equipment.Name);
    }

    public async Task<DieCutDetails?> UpdateAsync(Guid id, SaveDieCutCommand command, Guid employeeId, CancellationToken cancellationToken = default)
    {
        Validate(command);
        var dieCut = await dbContext.DieCuts.Include(x => x.Equipment).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dieCut is null) return null;
        if (dieCut.Status is DieCutStatus.Retired or DieCutStatus.Deleted)
            throw new ValidationException("Списанный или удалённый нож нельзя изменять.");
        if (command.Status is DieCutStatus.Retired or DieCutStatus.Deleted)
            throw new ValidationException("Используйте защищённую операцию и подтвердите её паролем суперпользователя.");

        var references = await ResolveReferencesAsync(command, cancellationToken);
        var equipment = references.Equipment;
        var normalizedNumber = Normalize(command.Number);
        if (await dbContext.DieCuts.AnyAsync(x => x.Id != id && x.EquipmentId == equipment.Id && x.NormalizedNumber == normalizedNumber && x.Status != DieCutStatus.Deleted, cancellationToken))
            throw new ValidationException("Нож с таким номером уже существует для выбранного оборудования.");

        dieCut.Equipment = equipment;
        dieCut.EquipmentId = equipment.Id;
        var previousStatus = dieCut.Status;
        var usage = UsageSnapshot.From(dieCut);
        Apply(dieCut, command, employeeId, references.Material, references.Figure);
        if (previousStatus == DieCutStatus.NeedsInspection && command.Status == DieCutStatus.Active)
            dieCut.NextInspectionRevolutions = checked(dieCut.Revolutions + InspectionIntervalRevolutions);
        else if (dieCut.Status == DieCutStatus.Active && dieCut.Revolutions >= dieCut.NextInspectionRevolutions)
            dieCut.Status = DieCutStatus.NeedsInspection;
        dbContext.DieCutEvents.Add(NewEvent(dieCut.Id, employeeId, DieCutEventType.Updated, null, usage, usage, dieCut.UpdatedAt));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(dieCut, equipment.Name);
    }

    public Task<DieCutDetails?> AddCirculationAsync(
        Guid id,
        long quantity,
        Guid employeeId,
        CancellationToken cancellationToken = default) =>
        AddCirculationAsync(id, quantity, null, employeeId, cancellationToken);

    public async Task<DieCutDetails?> AddCirculationAsync(
        Guid id,
        long? quantity,
        decimal? runLengthMeters,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (quantity.HasValue == runLengthMeters.HasValue)
            throw new ValidationException("Укажите либо тираж в штуках, либо пробег в метрах.");
        if (quantity is <= 0)
            throw new ValidationException("Тираж должен быть целым числом больше нуля.");
        if (runLengthMeters is <= 0)
            throw new ValidationException("Пробег должен быть числом больше нуля.");

        if (dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM die_cuts WHERE \"Id\" = {id} FOR UPDATE",
                cancellationToken);

            var result = await AddCirculationCoreAsync(id, quantity, runLengthMeters, employeeId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        return await AddCirculationCoreAsync(id, quantity, runLengthMeters, employeeId, cancellationToken);
    }

    private async Task<DieCutDetails?> AddCirculationCoreAsync(
        Guid id,
        long? quantity,
        decimal? runLengthMeters,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var dieCut = await dbContext.DieCuts.Include(x => x.Equipment).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dieCut is null) return null;
        EnsureOperational(dieCut);

        long addedQuantity;
        decimal addedRunLengthMeters;
        long addedRevolutions;
        try
        {
            if (quantity.HasValue)
            {
                addedQuantity = quantity.Value;
                (addedRunLengthMeters, addedRevolutions) = DieCutCalculations.CalculateRunMetrics(
                    addedQuantity, dieCut.Streams, dieCut.Y, dieCut.GapY, dieCut.Shaft);
            }
            else
            {
                addedRunLengthMeters = decimal.Round(runLengthMeters!.Value, 6, MidpointRounding.AwayFromZero);
                (addedQuantity, addedRevolutions) = DieCutCalculations.CalculateRunMetricsFromMeters(
                    addedRunLengthMeters, dieCut.Streams, dieCut.Y, dieCut.GapY, dieCut.Shaft);
                if (addedQuantity <= 0)
                    throw new ValidationException("Указанный пробег слишком мал для расчёта одной этикетки.");
            }

            if (addedQuantity > long.MaxValue - dieCut.Mileage
                || addedQuantity > long.MaxValue - dieCut.LifetimeMileage)
                throw new ValidationException("Итоговый тираж превышает допустимое значение.");
            if (addedRevolutions > long.MaxValue - dieCut.Revolutions
                || addedRevolutions > long.MaxValue - dieCut.LifetimeRevolutions)
                throw new ValidationException("Итоговое количество оборотов превышает допустимое значение.");

            var before = UsageSnapshot.From(dieCut);
            var now = DateTimeOffset.UtcNow;
            dieCut.Mileage += addedQuantity;
            dieCut.RunLengthMeters += addedRunLengthMeters;
            dieCut.Revolutions += addedRevolutions;
            dieCut.LifetimeMileage += addedQuantity;
            dieCut.LifetimeRunLengthMeters += addedRunLengthMeters;
            dieCut.LifetimeRevolutions += addedRevolutions;
            if (dieCut.Status == DieCutStatus.Active && dieCut.Revolutions >= dieCut.NextInspectionRevolutions)
                dieCut.Status = DieCutStatus.NeedsInspection;
            var after = UsageSnapshot.From(dieCut);
            Touch(dieCut, employeeId, now);
            dbContext.DieCutEvents.Add(NewEvent(
                dieCut.Id, employeeId, DieCutEventType.CirculationAdded, addedQuantity, before, after, now));
            await dbContext.SaveChangesAsync(cancellationToken);
            return Map(dieCut, dieCut.Equipment.Name);
        }
        catch (OverflowException)
        {
            throw new ValidationException("Введённое значение превышает допустимый диапазон.");
        }
    }

    public async Task<DieCutDetails?> InstallReplacementAsync(
        Guid id,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var dieCut = await dbContext.DieCuts.Include(x => x.Equipment).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dieCut is null) return null;
        if (dieCut.Status != DieCutStatus.OrderNew)
            throw new ValidationException("Сначала установите статус «Заказать новый» и сохраните карточку.");

        var before = UsageSnapshot.From(dieCut);
        var now = DateTimeOffset.UtcNow;
        dieCut.Mileage = 0;
        dieCut.RunLengthMeters = 0;
        dieCut.Revolutions = 0;
        dieCut.Generation = checked(dieCut.Generation + 1);
        dieCut.NextInspectionRevolutions = InspectionIntervalRevolutions;
        dieCut.Status = DieCutStatus.Active;
        var after = UsageSnapshot.From(dieCut);
        Touch(dieCut, employeeId, now);
        dbContext.DieCutEvents.Add(NewEvent(
            dieCut.Id, employeeId, DieCutEventType.ReplacementInstalled, null, before, after, now));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(dieCut, dieCut.Equipment.Name);
    }
    public async Task<DieCutDetails?> RetireAsync(
        Guid id,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var dieCut = await dbContext.DieCuts.Include(x => x.Equipment).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dieCut is null) return null;
        EnsureOperational(dieCut);

        var now = DateTimeOffset.UtcNow;
        dieCut.Status = DieCutStatus.Retired;
        Touch(dieCut, employeeId, now);
        var usage = UsageSnapshot.From(dieCut);
        dbContext.DieCutEvents.Add(NewEvent(
            dieCut.Id, employeeId, DieCutEventType.Retired, null, usage, usage, now));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(dieCut, dieCut.Equipment.Name);
    }

    public async Task<DieCutDetails?> DeleteAsync(
        Guid id,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var dieCut = await dbContext.DieCuts.Include(x => x.Equipment)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dieCut is null || dieCut.Status == DieCutStatus.Deleted) return null;

        var now = DateTimeOffset.UtcNow;
        dieCut.Status = DieCutStatus.Deleted;
        Touch(dieCut, employeeId, now);
        var usage = UsageSnapshot.From(dieCut);
        dbContext.DieCutEvents.Add(NewEvent(
            dieCut.Id, employeeId, DieCutEventType.Deleted, null, usage, usage, now));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(dieCut, dieCut.Equipment.Name);
    }
    public async Task<IReadOnlyList<DieCutEventDetails>?> GetEventsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!await dbContext.DieCuts.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken)) return null;

        return await dbContext.DieCutEvents
            .AsNoTracking()
            .Where(x => x.DieCutId == id)
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => new DieCutEventDetails(
                x.Id,
                x.Type,
                x.Quantity,
                x.MileageBefore,
                x.MileageAfter,
                x.RunLengthMetersBefore,
                x.RunLengthMetersAfter,
                x.RevolutionsBefore,
                x.RevolutionsAfter,
                x.OccurredAt,
                x.EmployeeId,
                (x.Employee.FirstName + " " + x.Employee.LastName).Trim()))
            .ToListAsync(cancellationToken);
    }

    public async Task<CatalogFacets> GetFacetsAsync(CancellationToken cancellationToken = default) => new(
        await dbContext.DieCuts.AsNoTracking().Where(x => x.Status != DieCutStatus.Deleted)
            .Select(x => x.Equipment.Name).Distinct().OrderBy(x => x).ToListAsync(cancellationToken),
        await dbContext.DieCuts.AsNoTracking().Where(x => x.Status != DieCutStatus.Deleted)
            .Select(x => x.Material).Distinct().OrderBy(x => x).ToListAsync(cancellationToken),
        await dbContext.DieCuts.AsNoTracking().Where(x => x.Status != DieCutStatus.Deleted)
            .Select(x => x.Figure).Distinct().OrderBy(x => x).ToListAsync(cancellationToken),
        await dbContext.DieCuts.AsNoTracking().Where(x => x.Status != DieCutStatus.Deleted)
            .Select(x => x.X).Distinct().OrderBy(x => x).ToListAsync(cancellationToken),
        await dbContext.DieCuts.AsNoTracking().Where(x => x.Status != DieCutStatus.Deleted)
            .Select(x => x.Y).Distinct().OrderBy(x => x).ToListAsync(cancellationToken),
        await dbContext.DieCuts.AsNoTracking().Where(x => x.Status != DieCutStatus.Deleted)
            .Select(x => x.Shaft).Distinct().OrderBy(x => x).ToListAsync(cancellationToken));

    private async Task<(Equipment Equipment, string Material, string Figure)> ResolveReferencesAsync(
        SaveDieCutCommand command, CancellationToken cancellationToken)
    {
        var equipment = await dbContext.Equipment.SingleOrDefaultAsync(
            x => x.IsActive && x.NormalizedName == Normalize(command.Equipment), cancellationToken);
        if (equipment is null) throw new ValidationException("Выберите оборудование из справочника.");
        var material = await dbContext.CatalogReferenceEntries.SingleOrDefaultAsync(
            x => x.Kind == CatalogReferenceKind.Material && x.NormalizedName == Normalize(command.Material), cancellationToken);
        if (material is null) throw new ValidationException("Выберите материал из справочника.");
        var figure = await dbContext.CatalogReferenceEntries.SingleOrDefaultAsync(
            x => x.Kind == CatalogReferenceKind.Figure && x.NormalizedName == Normalize(command.Figure), cancellationToken);
        if (figure is null) throw new ValidationException("Выберите фигуру из справочника.");
        return (equipment, material.Name, figure.Name);
    }

    private static void Apply(DieCut target, SaveDieCutCommand source, Guid employeeId, string material, string figure)
    {
        var (gapX, gapY) = DieCutCalculations.Calculate(source.Shaft, source.X, source.Y, source.Streams, source.Repeats, source.H, source.GrooveSpacing);
        target.Number = source.Number.Trim();
        target.NormalizedNumber = Normalize(source.Number);
        target.JcOrderNumber = TrimToNull(source.JcOrderNumber);
        target.Shaft = source.Shaft;
        target.X = source.X;
        target.Y = source.Y;
        target.Streams = source.Streams;
        target.Repeats = source.Repeats;
        target.GrooveSpacing = source.GrooveSpacing;
        target.LabelCornerRadius = source.LabelCornerRadius;
        target.GapX = gapX;
        target.GapY = gapY;
        target.Material = material;
        target.H = source.H;
        target.Figure = figure;
        target.Comments = TrimToNull(source.Comments);
        target.Date = source.Date;
        target.Status = source.Status;
        target.UpdatedByEmployeeId = employeeId;
        target.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void Validate(SaveDieCutCommand command)
    {
        if (!Enum.IsDefined(command.Status)) throw new ValidationException("Выберите статус ножа из списка.");
        if (string.IsNullOrWhiteSpace(command.Number) || command.Number.Length > 50) throw new ValidationException("Укажите номер ножа длиной до 50 символов.");
        if (command.JcOrderNumber?.Trim().Length > 100) throw new ValidationException("Номер заказа JC не должен превышать 100 символов.");
        if (string.IsNullOrWhiteSpace(command.Equipment) || command.Equipment.Trim().Length > 150)
            throw new ValidationException("Выберите оборудование из справочника.");
        if (string.IsNullOrWhiteSpace(command.Material) || command.Material.Length > 200) throw new ValidationException("Укажите материал.");
        if (string.IsNullOrWhiteSpace(command.Figure) || command.Figure.Trim().Length > 100)
            throw new ValidationException("Выберите фигуру из справочника.");
        var parameterViolation = DieCutParameterLimits.FindViolation(
            command.Shaft, command.X, command.Y, command.Streams, command.Repeats,
            command.H, command.GrooveSpacing, command.LabelCornerRadius);
        if (parameterViolation is not null) throw new ValidationException(parameterViolation);
        var (gapX, gapY) = DieCutCalculations.Calculate(command.Shaft, command.X, command.Y, command.Streams, command.Repeats, command.H, command.GrooveSpacing);
        if (gapX < 0) throw new ValidationException("Ширина материала не вмещает L × ручьи и расстояния между ручьями.");
        if (gapY < 0) throw new ValidationException("Длина окружности вала не вмещает B × количество этикеток в ручье.");
        if (command.Comments?.Length > 2000) throw new ValidationException("Комментарий не должен превышать 2000 символов.");
    }

    private static void EnsureOperational(DieCut dieCut)
    {
        if (dieCut.Status is DieCutStatus.Retired or DieCutStatus.Deleted)
            throw new ValidationException("Операция недоступна: нож списан или удалён.");
    }

    private static void Touch(DieCut dieCut, Guid employeeId, DateTimeOffset now)
    {
        dieCut.UpdatedByEmployeeId = employeeId;
        dieCut.UpdatedAt = now;
    }

    private static DieCutEvent NewEvent(
        Guid dieCutId,
        Guid employeeId,
        DieCutEventType type,
        long? quantity,
        UsageSnapshot before,
        UsageSnapshot after,
        DateTimeOffset occurredAt) => new()
        {
            DieCutId = dieCutId,
            EmployeeId = employeeId,
            Type = type,
            Quantity = quantity,
            MileageBefore = before.Mileage,
            MileageAfter = after.Mileage,
            RunLengthMetersBefore = before.RunLengthMeters,
            RunLengthMetersAfter = after.RunLengthMeters,
            RevolutionsBefore = before.Revolutions,
            RevolutionsAfter = after.Revolutions,
            OccurredAt = occurredAt
        };

    private readonly record struct UsageSnapshot(long Mileage, decimal RunLengthMeters, long Revolutions)
    {
        public static UsageSnapshot From(DieCut dieCut) =>
            new(dieCut.Mileage, dieCut.RunLengthMeters, dieCut.Revolutions);
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DieCutDetails Map(DieCut x, string equipment) => new(
        x.Id, x.Number, x.JcOrderNumber, equipment, x.Shaft, x.X, x.Y, x.Streams, x.Repeats,
        x.GrooveSpacing, x.LabelCornerRadius, x.GapX, x.GapY, x.Material, x.H, x.Figure, x.Comments, x.Date, x.Mileage,
        x.RunLengthMeters, x.Revolutions, x.LifetimeMileage, x.LifetimeRunLengthMeters, x.LifetimeRevolutions,
        x.Generation, x.NextInspectionRevolutions, x.Status, x.CreatedAt, x.UpdatedAt);

    private static System.Linq.Expressions.Expression<Func<DieCut, DieCutDetails>> ToDetails() => x => new DieCutDetails(
        x.Id, x.Number, x.JcOrderNumber, x.Equipment.Name, x.Shaft, x.X, x.Y, x.Streams, x.Repeats,
        x.GrooveSpacing, x.LabelCornerRadius, x.GapX, x.GapY, x.Material, x.H, x.Figure, x.Comments, x.Date, x.Mileage,
        x.RunLengthMeters, x.Revolutions, x.LifetimeMileage, x.LifetimeRunLengthMeters, x.LifetimeRevolutions,
        x.Generation, x.NextInspectionRevolutions, x.Status, x.CreatedAt, x.UpdatedAt);
}