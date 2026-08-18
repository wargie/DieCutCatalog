using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Auditing;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Domain.Employees;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace DieCutCatalog.Infrastructure.Catalog;

public sealed class CatalogAdministrationService(CatalogDbContext dbContext) : ICatalogAdministrationService
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<CatalogReferences> GetReferencesAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.EnsureCatalogReferencesAsync(cancellationToken);
        var entries = await dbContext.CatalogReferenceEntries.AsNoTracking()
            .OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var equipment = await dbContext.Equipment.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new CatalogReferenceItem(x.Id, CatalogReferenceType.Equipment, x.Name, x.ArticleRtf))
            .ToListAsync(cancellationToken);

        return new CatalogReferences(
            entries.Where(x => x.Kind == CatalogReferenceKind.Material)
                .Select(x => new CatalogReferenceItem(x.Id, CatalogReferenceType.Material, x.Name, x.ArticleRtf)).ToArray(),
            entries.Where(x => x.Kind == CatalogReferenceKind.Figure)
                .Select(x => new CatalogReferenceItem(x.Id, CatalogReferenceType.Figure, x.Name, x.ArticleRtf)).ToArray(),
            equipment);
    }

    public async Task<CatalogReferenceItem> AddReferenceAsync(
        CatalogReferenceType type, string name, AuditIdentity audit,
        CancellationToken cancellationToken = default)
    {
        var clean = ValidateName(name, type);
        var normalized = Normalize(clean);
        if (type == CatalogReferenceType.Equipment)
        {
            if (await dbContext.Equipment.AnyAsync(x => x.NormalizedName == normalized, cancellationToken))
                throw new ValidationException("Такое оборудование уже есть в справочнике.");
            var equipment = new Equipment { Name = clean, NormalizedName = normalized };
            dbContext.Equipment.Add(equipment);
            AddAudit(audit, AuditEntityType.Equipment, equipment.Id, AuditAction.Created,
                null, ReferenceSnapshot(equipment.Id, type, equipment.Name, equipment.ArticleRtf));
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CatalogReferenceItem(equipment.Id, type, equipment.Name);
        }

        var kind = ToKind(type);
        if (await dbContext.CatalogReferenceEntries.AnyAsync(
                x => x.Kind == kind && x.NormalizedName == normalized, cancellationToken))
            throw new ValidationException("Такое значение уже есть в справочнике.");
        var entry = new CatalogReferenceEntry { Kind = kind, Name = clean, NormalizedName = normalized };
        dbContext.CatalogReferenceEntries.Add(entry);
        AddAudit(audit, ToAuditEntityType(type), entry.Id, AuditAction.Created,
            null, ReferenceSnapshot(entry.Id, type, entry.Name, entry.ArticleRtf));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CatalogReferenceItem(entry.Id, type, entry.Name);
    }

    public async Task<ReferenceImportResult> ImportReferencesAsync(
        CatalogReferenceType type, IReadOnlyList<string> names, AuditIdentity audit,
        CancellationToken cancellationToken = default)
    {
        var maxLength = type == CatalogReferenceType.Equipment ? 150 : 200;
        var candidates = PrepareImportNames(names, maxLength);
        if (candidates.Count == 0) return new ReferenceImportResult(0, names.Count);

        HashSet<string> existing;
        var imported = new List<object>();
        if (type == CatalogReferenceType.Equipment)
        {
            existing = (await dbContext.Equipment.AsNoTracking()
                .Select(x => x.NormalizedName).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
            foreach (var candidate in candidates.Where(x => !existing.Contains(x.Key)))
            {
                var equipment = new Equipment { Name = candidate.Value, NormalizedName = candidate.Key };
                dbContext.Equipment.Add(equipment);
                imported.Add(new { equipment.Id, equipment.Name });
            }
        }
        else
        {
            var kind = ToKind(type);
            existing = (await dbContext.CatalogReferenceEntries.AsNoTracking()
                .Where(x => x.Kind == kind).Select(x => x.NormalizedName).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var candidate in candidates.Where(x => !existing.Contains(x.Key)))
            {
                var entry = new CatalogReferenceEntry
                    { Kind = kind, Name = candidate.Value, NormalizedName = candidate.Key };
                dbContext.CatalogReferenceEntries.Add(entry);
                imported.Add(new { entry.Id, entry.Name });
            }
        }

        var added = candidates.Count(x => !existing.Contains(x.Key));
        if (added > 0)
        {
            var importId = audit.CorrelationId ?? Guid.NewGuid();
            AddAudit(audit with { CorrelationId = importId }, ToAuditEntityType(type), importId,
                AuditAction.Imported, null, new { Added = imported, Skipped = names.Count - added });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return new ReferenceImportResult(added, names.Count - added);
    }

    public async Task<CatalogReferenceItem?> RenameReferenceAsync(
        CatalogReferenceType type, Guid id, string name, AuditIdentity audit,
        CancellationToken cancellationToken = default)
    {
        var clean = ValidateName(name, type);
        var normalized = Normalize(clean);
        if (type == CatalogReferenceType.Equipment)
        {
            var equipment = await dbContext.Equipment.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (equipment is null) return null;
            if (await dbContext.Equipment.AnyAsync(x => x.Id != id && x.NormalizedName == normalized, cancellationToken))
                throw new ValidationException("Такое оборудование уже есть в справочнике.");
            var before = ReferenceSnapshot(equipment.Id, type, equipment.Name, equipment.ArticleRtf);
            equipment.Name = clean;
            equipment.NormalizedName = normalized;
            AddAudit(audit, AuditEntityType.Equipment, equipment.Id, AuditAction.Updated,
                before, ReferenceSnapshot(equipment.Id, type, equipment.Name, equipment.ArticleRtf));
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

        var beforeEntry = ReferenceSnapshot(entry.Id, type, entry.Name, entry.ArticleRtf);
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
        AddAudit(audit, ToAuditEntityType(type), entry.Id, AuditAction.Updated,
            beforeEntry, new
            {
                Entry = ReferenceSnapshot(entry.Id, type, entry.Name, entry.ArticleRtf),
                UpdatedDieCuts = affected.Select(x => x.Id).ToArray()
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CatalogReferenceItem(entry.Id, type, entry.Name);
    }

    public async Task<bool> DeleteReferenceAsync(
        CatalogReferenceType type, Guid id, AuditIdentity audit,
        CancellationToken cancellationToken = default)
    {
        if (type == CatalogReferenceType.Equipment)
        {
            var equipment = await dbContext.Equipment.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (equipment is null) return false;
            if (await dbContext.DieCuts.AnyAsync(x => x.EquipmentId == id, cancellationToken))
                throw new ValidationException("Нельзя удалить оборудование: оно используется в карточках ножей.");
            var before = ReferenceSnapshot(equipment.Id, type, equipment.Name, equipment.ArticleRtf);
            dbContext.Equipment.Remove(equipment);
            AddAudit(audit, AuditEntityType.Equipment, equipment.Id, AuditAction.Deleted,
                before, null);
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

        var beforeEntry = ReferenceSnapshot(entry.Id, type, entry.Name, entry.ArticleRtf);
        dbContext.CatalogReferenceEntries.Remove(entry);
        AddAudit(audit, ToAuditEntityType(type), entry.Id, AuditAction.Deleted,
            beforeEntry, null);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateReferenceArticleAsync(
        CatalogReferenceType type, Guid id, string? articleRtf, AuditIdentity audit,
        CancellationToken cancellationToken = default)
    {
        var clean = CleanArticle(articleRtf);
        if (type == CatalogReferenceType.Equipment)
        {
            var equipment = await dbContext.Equipment.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (equipment is null) return false;
            var before = ReferenceSnapshot(equipment.Id, type, equipment.Name, equipment.ArticleRtf);
            equipment.ArticleRtf = clean;
            AddAudit(audit, AuditEntityType.Equipment, equipment.Id, AuditAction.ArticleUpdated,
                before, ReferenceSnapshot(equipment.Id, type, equipment.Name, equipment.ArticleRtf));
        }
        else
        {
            var kind = ToKind(type);
            var entry = await dbContext.CatalogReferenceEntries.SingleOrDefaultAsync(
                x => x.Id == id && x.Kind == kind, cancellationToken);
            if (entry is null) return false;
            var before = ReferenceSnapshot(entry.Id, type, entry.Name, entry.ArticleRtf);
            entry.ArticleRtf = clean;
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            AddAudit(audit, ToAuditEntityType(type), entry.Id, AuditAction.ArticleUpdated,
                before, ReferenceSnapshot(entry.Id, type, entry.Name, entry.ArticleRtf));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ReferenceDirectoryOverview> GetDirectoryOverviewAsync(CancellationToken cancellationToken = default)
    {
        var groups = await dbContext.ReferenceDirectoryGroups.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new ReferenceDirectoryGroupItem(x.Id, x.Name, x.SortOrder))
            .ToListAsync(cancellationToken);
        var directories = await dbContext.ReferenceDirectories.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new ReferenceDirectoryItem(
                x.Id, x.GroupId, x.Name, x.Description, x.SortOrder, x.IsArchived, x.Values.Count))
            .ToListAsync(cancellationToken);
        return new ReferenceDirectoryOverview(groups, directories);
    }

    public async Task<ReferenceDirectoryGroupItem> AddDirectoryGroupAsync(
        string name, AuditIdentity audit, CancellationToken cancellationToken = default)
    {
        var clean = ValidateDirectoryName(name, 120);
        var normalized = Normalize(clean);
        if (await dbContext.ReferenceDirectoryGroups.AnyAsync(x => x.NormalizedName == normalized, cancellationToken))
            throw new ValidationException("Группа с таким названием уже существует.");
        var sortOrder = (await dbContext.ReferenceDirectoryGroups.MaxAsync(x => (int?)x.SortOrder, cancellationToken) ?? -1) + 1;
        var group = new ReferenceDirectoryGroup { Name = clean, NormalizedName = normalized, SortOrder = sortOrder };
        dbContext.ReferenceDirectoryGroups.Add(group);
        AddAudit(audit, AuditEntityType.ReferenceGroup, group.Id, AuditAction.Created,
            null, GroupSnapshot(group));
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ReferenceDirectoryGroupItem(group.Id, group.Name, group.SortOrder);
    }

    public async Task<bool> DeleteDirectoryGroupAsync(
        Guid id, AuditIdentity audit, CancellationToken cancellationToken = default)
    {
        var group = await dbContext.ReferenceDirectoryGroups.Include(x => x.Directories)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (group is null) return false;
        var before = new
        {
            Group = GroupSnapshot(group),
            DirectoryIds = group.Directories.Select(x => x.Id).ToArray()
        };
        foreach (var directory in group.Directories) directory.GroupId = null;
        dbContext.ReferenceDirectoryGroups.Remove(group);
        AddAudit(audit, AuditEntityType.ReferenceGroup, group.Id, AuditAction.Deleted,
            before, null);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ReferenceDirectoryItem> AddDirectoryAsync(
        CreateReferenceDirectoryCommand command, AuditIdentity audit,
        CancellationToken cancellationToken = default)
    {
        await ValidateGroupAsync(command.GroupId, cancellationToken);
        var clean = ValidateDirectoryName(command.Name, 120);
        var normalized = Normalize(clean);
        if (await dbContext.ReferenceDirectories.AnyAsync(x => x.NormalizedName == normalized, cancellationToken))
            throw new ValidationException("Справочник с таким названием уже существует.");
        var sortOrder = (await dbContext.ReferenceDirectories
            .Where(x => x.GroupId == command.GroupId).MaxAsync(x => (int?)x.SortOrder, cancellationToken) ?? -1) + 1;
        var directory = new ReferenceDirectory
        {
            GroupId = command.GroupId, Name = clean, NormalizedName = normalized,
            Description = CleanDescription(command.Description), SortOrder = sortOrder
        };
        dbContext.ReferenceDirectories.Add(directory);
        AddAudit(audit, AuditEntityType.ReferenceDirectory, directory.Id, AuditAction.Created,
            null, DirectorySnapshot(directory));
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToItem(directory, 0);
    }

    public async Task<ReferenceDirectoryItem?> UpdateDirectoryAsync(
        Guid id, UpdateReferenceDirectoryCommand command, AuditIdentity audit,
        CancellationToken cancellationToken = default)
    {
        await ValidateGroupAsync(command.GroupId, cancellationToken);
        var directory = await dbContext.ReferenceDirectories.Include(x => x.Values)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (directory is null) return null;
        var clean = ValidateDirectoryName(command.Name, 120);
        var normalized = Normalize(clean);
        if (await dbContext.ReferenceDirectories.AnyAsync(
                x => x.Id != id && x.NormalizedName == normalized, cancellationToken))
            throw new ValidationException("Справочник с таким названием уже существует.");
        var before = DirectorySnapshot(directory);
        directory.GroupId = command.GroupId;
        directory.Name = clean;
        directory.NormalizedName = normalized;
        directory.Description = CleanDescription(command.Description);
        directory.IsArchived = command.IsArchived;
        directory.UpdatedAt = DateTimeOffset.UtcNow;
        var action = before.IsArchived == directory.IsArchived
            ? AuditAction.Updated
            : directory.IsArchived ? AuditAction.Archived : AuditAction.Restored;
        AddAudit(audit, AuditEntityType.ReferenceDirectory, directory.Id, action,
            before, DirectorySnapshot(directory));
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToItem(directory, directory.Values.Count);
    }

    public async Task<bool> DeleteDirectoryAsync(
        Guid id, AuditIdentity audit, CancellationToken cancellationToken = default)
    {
        var directory = await dbContext.ReferenceDirectories.Include(x => x.Values)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (directory is null) return false;
        if (directory.Values.Count != 0)
            throw new ValidationException("Нельзя удалить непустой справочник. Сначала архивируйте его или удалите значения.");
        var before = DirectorySnapshot(directory);
        dbContext.ReferenceDirectories.Remove(directory);
        AddAudit(audit, AuditEntityType.ReferenceDirectory, directory.Id, AuditAction.Deleted,
            before, null);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ReferenceDirectoryValueItem>> GetDirectoryValuesAsync(
        Guid directoryId, bool includeArchived, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.ReferenceDirectories.AnyAsync(x => x.Id == directoryId, cancellationToken))
            return [];
        return await dbContext.ReferenceDirectoryValues.AsNoTracking()
            .Where(x => x.DirectoryId == directoryId && (includeArchived || !x.IsArchived))
            .OrderBy(x => x.IsArchived).ThenBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new ReferenceDirectoryValueItem(
                x.Id, x.DirectoryId, x.Name, x.SortOrder, x.IsArchived, x.UpdatedAt, x.ArticleRtf))
            .ToListAsync(cancellationToken);
    }

    public async Task<ReferenceDirectoryValueItem> AddDirectoryValueAsync(
        Guid directoryId, string name, AuditIdentity audit,
        CancellationToken cancellationToken = default)
    {
        var directory = await GetMutableDirectoryAsync(directoryId, cancellationToken);
        var clean = ValidateDirectoryName(name, 200);
        var normalized = Normalize(clean);
        if (await dbContext.ReferenceDirectoryValues.AnyAsync(
                x => x.DirectoryId == directoryId && x.NormalizedName == normalized, cancellationToken))
            throw new ValidationException("Такое значение уже существует в справочнике.");
        var sortOrder = (await dbContext.ReferenceDirectoryValues.Where(x => x.DirectoryId == directoryId)
            .MaxAsync(x => (int?)x.SortOrder, cancellationToken) ?? -1) + 1;
        var value = new ReferenceDirectoryValue
        {
            DirectoryId = directoryId, Name = clean, NormalizedName = normalized, SortOrder = sortOrder
        };
        dbContext.ReferenceDirectoryValues.Add(value);
        directory.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(audit, AuditEntityType.ReferenceValue, value.Id, AuditAction.Created,
            null, ValueSnapshot(value, directory.Name));
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToItem(value);
    }

    public async Task<ReferenceImportResult> ImportDirectoryValuesAsync(
        Guid directoryId, IReadOnlyList<string> names, AuditIdentity audit,
        CancellationToken cancellationToken = default)
    {
        var directory = await GetMutableDirectoryAsync(directoryId, cancellationToken);

        var candidates = PrepareImportNames(names, 200);
        if (candidates.Count == 0) return new ReferenceImportResult(0, names.Count);
        var existing = (await dbContext.ReferenceDirectoryValues.AsNoTracking()
                .Where(x => x.DirectoryId == directoryId)
                .Select(x => x.NormalizedName).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var nextSortOrder = (await dbContext.ReferenceDirectoryValues
            .Where(x => x.DirectoryId == directoryId).MaxAsync(x => (int?)x.SortOrder, cancellationToken) ?? -1) + 1;
        var added = 0;
        var imported = new List<object>();
        foreach (var candidate in candidates.Where(x => !existing.Contains(x.Key)))
        {
            var value = new ReferenceDirectoryValue
            {
                DirectoryId = directoryId,
                Name = candidate.Value,
                NormalizedName = candidate.Key,
                SortOrder = nextSortOrder++
            };
            dbContext.ReferenceDirectoryValues.Add(value);
            imported.Add(new { value.Id, value.Name });
            added++;
        }
        if (added > 0)
        {
            directory.UpdatedAt = DateTimeOffset.UtcNow;
            var importId = audit.CorrelationId ?? Guid.NewGuid();
            AddAudit(audit with { CorrelationId = importId }, AuditEntityType.ReferenceDirectory,
                directory.Id, AuditAction.Imported, null,
                new { directory.Id, directory.Name, Added = imported, Skipped = names.Count - added });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return new ReferenceImportResult(added, names.Count - added);
    }

    public async Task<ReferenceDirectoryValueItem?> UpdateDirectoryValueAsync(
        Guid directoryId, Guid id, string name, bool isArchived, AuditIdentity audit,
        CancellationToken cancellationToken = default)
    {
        var directory = await GetMutableDirectoryAsync(directoryId, cancellationToken);
        var value = await dbContext.ReferenceDirectoryValues.SingleOrDefaultAsync(
            x => x.Id == id && x.DirectoryId == directoryId, cancellationToken);
        if (value is null) return null;
        var clean = ValidateDirectoryName(name, 200);
        var normalized = Normalize(clean);
        if (await dbContext.ReferenceDirectoryValues.AnyAsync(
                x => x.Id != id && x.DirectoryId == directoryId && x.NormalizedName == normalized, cancellationToken))
            throw new ValidationException("Такое значение уже существует в справочнике.");
        var before = ValueSnapshot(value, directory.Name);
        value.Name = clean;
        value.NormalizedName = normalized;
        value.IsArchived = isArchived;
        value.UpdatedAt = DateTimeOffset.UtcNow;
        var action = before.IsArchived == value.IsArchived
            ? AuditAction.Updated
            : value.IsArchived ? AuditAction.Archived : AuditAction.Restored;
        AddAudit(audit, AuditEntityType.ReferenceValue, value.Id, action,
            before, ValueSnapshot(value, directory.Name));
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToItem(value);
    }

    public async Task<bool> DeleteDirectoryValueAsync(
        Guid directoryId, Guid id, AuditIdentity audit,
        CancellationToken cancellationToken = default)
    {
        var directory = await GetMutableDirectoryAsync(directoryId, cancellationToken);
        var value = await dbContext.ReferenceDirectoryValues.SingleOrDefaultAsync(
            x => x.Id == id && x.DirectoryId == directoryId, cancellationToken);
        if (value is null) return false;
        var before = ValueSnapshot(value, directory.Name);
        dbContext.ReferenceDirectoryValues.Remove(value);
        directory.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(audit, AuditEntityType.ReferenceValue, value.Id, AuditAction.Deleted,
            before, null);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateDirectoryValueArticleAsync(
        Guid directoryId, Guid id, string? articleRtf, AuditIdentity audit,
        CancellationToken cancellationToken = default)
    {
        var directory = await GetMutableDirectoryAsync(directoryId, cancellationToken);
        var value = await dbContext.ReferenceDirectoryValues.SingleOrDefaultAsync(
            x => x.Id == id && x.DirectoryId == directoryId, cancellationToken);
        if (value is null) return false;
        var before = ValueSnapshot(value, directory.Name);
        value.ArticleRtf = CleanArticle(articleRtf);
        value.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(audit, AuditEntityType.ReferenceValue, value.Id, AuditAction.ArticleUpdated,
            before, ValueSnapshot(value, directory.Name));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ReferencePositionTransferResult?> TransferPositionAsync(
        ReferencePositionTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidatePositionReference(command.Source.SystemType, command.Source.DirectoryId, "исходной позиции");
        ValidatePositionReference(command.Destination.SystemType, command.Destination.DirectoryId, "целевого раздела");
        if (command.Move && IsSameSection(command.Source, command.Destination))
            throw new ValidationException("Нельзя перенести позицию в тот же раздел.");

        var source = await LoadTransferSourceAsync(command.Source, cancellationToken);
        if (source is null) return null;
        if (command.Move) await EnsureTransferSourceCanBeDeletedAsync(source, cancellationToken);

        await using var transaction = command.Move && dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var destination = await CreateTransferDestinationAsync(
            command.Destination, command.Name, source.ArticleRtf, source.IsArchived, cancellationToken);
        AddAudit(
            command.Audit,
            ToAuditEntityType(command.Destination),
            destination.Result.Id,
            command.Move ? AuditAction.Moved : AuditAction.Copied,
            new
            {
                PositionId = command.Source.Id,
                source.Name,
                Section = source.SectionName,
                source.ArticleRtf,
                source.IsArchived
            },
            new
            {
                PositionId = destination.Result.Id,
                destination.Result.Name,
                Section = destination.SectionName,
                destination.Result.ArticleRtf,
                destination.Result.IsArchived
            });
        await dbContext.SaveChangesAsync(cancellationToken);

        if (command.Move)
        {
            await RemoveTransferSourceAsync(source, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }

        return destination.Result;
    }

    public async Task<PagedResult<AuditLogEntry>> SearchAuditLogAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var entries = await LoadAuditEntriesAsync(search, cancellationToken);
        var items = entries.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return new PagedResult<AuditLogEntry>(items, entries.Count, page, pageSize);
    }

    public async Task<ExportedFile> ExportAuditLogAsync(
        string? search, bool pdf, CancellationToken cancellationToken = default)
    {
        var entries = await LoadAuditEntriesAsync(search, cancellationToken);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return pdf
            ? new ExportedFile($"knife-audit-{stamp}.pdf", "application/pdf", BuildPdf(entries))
            : new ExportedFile($"knife-audit-{stamp}.csv", "text/csv; charset=utf-8", BuildCsv(entries));
    }

    private async Task<IReadOnlyList<AuditLogEntry>> LoadAuditEntriesAsync(
        string? search, CancellationToken cancellationToken)
    {
        var term = search?.Trim().ToLowerInvariant();
        var knifeQuery = dbContext.DieCutEvents.AsNoTracking();
        var accessQuery = dbContext.EmployeeAccessEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(term))
        {
            knifeQuery = knifeQuery.Where(x =>
                x.DieCut.Number.ToLower().Contains(term) ||
                x.DieCut.Equipment.Name.ToLower().Contains(term) ||
                x.Employee.FirstName.ToLower().Contains(term) ||
                x.Employee.LastName.ToLower().Contains(term) ||
                x.Employee.Email.ToLower().Contains(term));
            accessQuery = accessQuery.Where(x =>
                x.Employee.FirstName.ToLower().Contains(term) ||
                x.Employee.LastName.ToLower().Contains(term) ||
                x.Employee.Email.ToLower().Contains(term));
        }

        var knifeEntries = await knifeQuery.Select(x => new AuditLogEntry(
            x.Id, x.DieCutId, x.DieCut.Number, x.DieCut.Equipment.Name, x.Type, x.Quantity,
            x.MileageBefore, x.MileageAfter, x.RunLengthMetersBefore, x.RunLengthMetersAfter,
            x.RevolutionsBefore, x.RevolutionsAfter, x.OccurredAt,
            (x.Employee.FirstName + " " + x.Employee.LastName).Trim(),
            null, null, null, null, null, null, null, null, null, null, null))
            .ToListAsync(cancellationToken);
        var accessEntries = await accessQuery.Select(x => new AuditLogEntry(
            x.Id, Guid.Empty, "", "", DieCutEventType.Updated, null,
            0, 0, 0, 0, 0, 0, x.OccurredAt,
            (x.Employee.FirstName + " " + x.Employee.LastName).Trim(),
            x.Type, null, null, null, null, null, null, null, null, null, null))
            .ToListAsync(cancellationToken);
        var universalEntries = await dbContext.AuditEvents.AsNoTracking().Select(x => new AuditLogEntry(
            x.Id, Guid.Empty, "", "", DieCutEventType.Updated, null,
            0, 0, 0, 0, 0, 0, x.OccurredAt,
            (x.ActorEmployee.FirstName + " " + x.ActorEmployee.LastName).Trim(),
            null, x.EntityType, x.Action, x.EntityId, x.ApproverEmployeeId,
            x.ApproverEmployee == null
                ? null
                : (x.ApproverEmployee.FirstName + " " + x.ApproverEmployee.LastName).Trim(),
            x.BeforeJson, x.AfterJson, x.CorrelationId, null, null))
            .ToListAsync(cancellationToken);
        universalEntries = universalEntries.Select(x => x with
        {
            DisplayObject = AuditObject(x),
            DisplayContext = AuditContext(x)
        }).ToList();
        if (!string.IsNullOrWhiteSpace(term))
            universalEntries = universalEntries.Where(x => AuditEntryMatches(x, term)).ToList();

        return knifeEntries.Concat(accessEntries).Concat(universalEntries)
            .OrderByDescending(x => x.OccurredAt)
            .ToArray();
    }

    private async Task<TransferSource?> LoadTransferSourceAsync(
        ReferencePositionLocator source,
        CancellationToken cancellationToken)
    {
        if (source.SystemType == CatalogReferenceType.Equipment)
        {
            var equipment = await dbContext.Equipment.SingleOrDefaultAsync(
                x => x.Id == source.Id, cancellationToken);
            return equipment is null
                ? null
                : new TransferSource(
                    equipment, source.SystemType, null, equipment.Name,
                    SystemReferenceName(source.SystemType.Value), equipment.ArticleRtf, !equipment.IsActive);
        }

        if (source.SystemType.HasValue)
        {
            var kind = ToKind(source.SystemType.Value);
            var entry = await dbContext.CatalogReferenceEntries.SingleOrDefaultAsync(
                x => x.Id == source.Id && x.Kind == kind, cancellationToken);
            return entry is null
                ? null
                : new TransferSource(
                    entry, source.SystemType, null, entry.Name,
                    SystemReferenceName(source.SystemType.Value), entry.ArticleRtf, false);
        }

        var value = await dbContext.ReferenceDirectoryValues.Include(x => x.Directory).SingleOrDefaultAsync(
            x => x.Id == source.Id && x.DirectoryId == source.DirectoryId, cancellationToken);
        return value is null
            ? null
            : new TransferSource(
                value, null, value.DirectoryId, value.Name, value.Directory.Name,
                value.ArticleRtf, value.IsArchived);
    }

    private async Task EnsureTransferSourceCanBeDeletedAsync(
        TransferSource source,
        CancellationToken cancellationToken)
    {
        if (source.Entity is ReferenceDirectoryValue value)
        {
            await GetMutableDirectoryAsync(value.DirectoryId, cancellationToken);
            return;
        }

        if (source.Entity is Equipment equipment)
        {
            if (await dbContext.DieCuts.AnyAsync(x => x.EquipmentId == equipment.Id, cancellationToken))
                throw new ValidationException("Нельзя перенести оборудование: оно используется в карточках ножей.");
            return;
        }

        if (source.Entity is not CatalogReferenceEntry entry) return;
        var name = entry.Name.ToLower();
        var isUsed = source.SystemType == CatalogReferenceType.Material
            ? await dbContext.DieCuts.AnyAsync(x => x.Material.ToLower() == name, cancellationToken)
            : await dbContext.DieCuts.AnyAsync(x => x.Figure.ToLower() == name, cancellationToken);
        if (isUsed)
            throw new ValidationException("Нельзя перенести значение: оно используется в карточках ножей.");
    }

    private async Task<TransferDestination> CreateTransferDestinationAsync(
        ReferencePositionTarget destination,
        string name,
        string? articleRtf,
        bool isArchived,
        CancellationToken cancellationToken)
    {
        var article = CleanArticle(articleRtf);
        if (destination.SystemType == CatalogReferenceType.Equipment)
        {
            var clean = ValidateName(name, CatalogReferenceType.Equipment);
            var normalized = Normalize(clean);
            if (await dbContext.Equipment.AnyAsync(x => x.NormalizedName == normalized, cancellationToken))
                throw new ValidationException("Такое оборудование уже есть в справочнике.");
            var equipment = new Equipment
            {
                Name = clean,
                NormalizedName = normalized,
                ArticleRtf = article
            };
            dbContext.Equipment.Add(equipment);
            return new TransferDestination(
                new ReferencePositionTransferResult(equipment.Id, equipment.Name, article, false),
                SystemReferenceName(CatalogReferenceType.Equipment));
        }

        if (destination.SystemType.HasValue)
        {
            var type = destination.SystemType.Value;
            var clean = ValidateName(name, type);
            var normalized = Normalize(clean);
            var kind = ToKind(type);
            if (await dbContext.CatalogReferenceEntries.AnyAsync(
                    x => x.Kind == kind && x.NormalizedName == normalized, cancellationToken))
                throw new ValidationException("Такое значение уже есть в справочнике.");
            var entry = new CatalogReferenceEntry
            {
                Kind = kind,
                Name = clean,
                NormalizedName = normalized,
                ArticleRtf = article
            };
            dbContext.CatalogReferenceEntries.Add(entry);
            return new TransferDestination(
                new ReferencePositionTransferResult(entry.Id, entry.Name, article, false),
                SystemReferenceName(type));
        }

        var directoryId = destination.DirectoryId!.Value;
        var directory = await GetMutableDirectoryAsync(directoryId, cancellationToken);
        var valueName = ValidateDirectoryName(name, 200);
        var valueNormalizedName = Normalize(valueName);
        if (await dbContext.ReferenceDirectoryValues.AnyAsync(
                x => x.DirectoryId == directoryId && x.NormalizedName == valueNormalizedName, cancellationToken))
            throw new ValidationException("Такое значение уже существует в справочнике.");
        var sortOrder = (await dbContext.ReferenceDirectoryValues.Where(x => x.DirectoryId == directoryId)
            .MaxAsync(x => (int?)x.SortOrder, cancellationToken) ?? -1) + 1;
        var value = new ReferenceDirectoryValue
        {
            DirectoryId = directoryId,
            Name = valueName,
            NormalizedName = valueNormalizedName,
            ArticleRtf = article,
            IsArchived = isArchived,
            SortOrder = sortOrder
        };
        dbContext.ReferenceDirectoryValues.Add(value);
        directory.UpdatedAt = DateTimeOffset.UtcNow;
        return new TransferDestination(
            new ReferencePositionTransferResult(value.Id, value.Name, article, value.IsArchived),
            directory.Name);
    }

    private async Task RemoveTransferSourceAsync(TransferSource source, CancellationToken cancellationToken)
    {
        switch (source.Entity)
        {
            case Equipment equipment:
                dbContext.Equipment.Remove(equipment);
                break;
            case CatalogReferenceEntry entry:
                dbContext.CatalogReferenceEntries.Remove(entry);
                break;
            case ReferenceDirectoryValue value:
                dbContext.ReferenceDirectoryValues.Remove(value);
                var directory = await dbContext.ReferenceDirectories.SingleOrDefaultAsync(
                    x => x.Id == value.DirectoryId, cancellationToken);
                if (directory is not null) directory.UpdatedAt = DateTimeOffset.UtcNow;
                break;
        }
    }

    private void AddAudit(
        AuditIdentity identity,
        AuditEntityType entityType,
        Guid entityId,
        AuditAction action,
        object? before,
        object? after)
    {
        if (identity.ActorEmployeeId == Guid.Empty)
            throw new ValidationException("Для записи аудита не указан сотрудник.");

        dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorEmployeeId = identity.ActorEmployeeId,
            ApproverEmployeeId = identity.ApproverEmployeeId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            BeforeJson = SerializeAuditSnapshot(before),
            AfterJson = SerializeAuditSnapshot(after),
            CorrelationId = identity.CorrelationId ?? Guid.NewGuid()
        });
    }

    private static string? SerializeAuditSnapshot(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, AuditJsonOptions);

    private static ReferenceSnapshotData ReferenceSnapshot(
        Guid id, CatalogReferenceType type, string name, string? articleRtf) =>
        new(id, type, name, articleRtf);

    private static GroupSnapshotData GroupSnapshot(ReferenceDirectoryGroup group) =>
        new(group.Id, group.Name, group.SortOrder);

    private static DirectorySnapshotData DirectorySnapshot(ReferenceDirectory directory) =>
        new(directory.Id, directory.GroupId, directory.Name, directory.Description,
            directory.SortOrder, directory.IsArchived);

    private static ValueSnapshotData ValueSnapshot(
        ReferenceDirectoryValue value, string? directoryName) =>
        new(value.Id, value.DirectoryId, directoryName, value.Name, value.SortOrder,
            value.IsArchived, value.ArticleRtf);

    private static AuditEntityType ToAuditEntityType(CatalogReferenceType type) => type switch
    {
        CatalogReferenceType.Material => AuditEntityType.Material,
        CatalogReferenceType.Figure => AuditEntityType.Figure,
        CatalogReferenceType.Equipment => AuditEntityType.Equipment,
        _ => throw new ValidationException("Некорректный тип справочника.")
    };

    private static AuditEntityType ToAuditEntityType(ReferencePositionTarget target) =>
        target.SystemType.HasValue
            ? ToAuditEntityType(target.SystemType.Value)
            : AuditEntityType.ReferenceValue;

    private static bool IsSameSection(ReferencePositionLocator source, ReferencePositionTarget destination) =>
        source.SystemType == destination.SystemType && source.DirectoryId == destination.DirectoryId;

    private static void ValidatePositionReference(
        CatalogReferenceType? systemType,
        Guid? directoryId,
        string field)
    {
        if (systemType.HasValue == directoryId.HasValue)
            throw new ValidationException($"Нужно указать ровно один тип {field}.");
    }

    private sealed record TransferSource(
        object Entity,
        CatalogReferenceType? SystemType,
        Guid? DirectoryId,
        string Name,
        string SectionName,
        string? ArticleRtf,
        bool IsArchived);

    private sealed record TransferDestination(
        ReferencePositionTransferResult Result,
        string SectionName);

    private sealed record ReferenceSnapshotData(
        Guid Id,
        CatalogReferenceType Type,
        string Name,
        string? ArticleRtf);

    private sealed record GroupSnapshotData(Guid Id, string Name, int SortOrder);

    private sealed record DirectorySnapshotData(
        Guid Id,
        Guid? GroupId,
        string Name,
        string? Description,
        int SortOrder,
        bool IsArchived);

    private sealed record ValueSnapshotData(
        Guid Id,
        Guid DirectoryId,
        string? DirectoryName,
        string Name,
        int SortOrder,
        bool IsArchived,
        string? ArticleRtf);

    private static byte[] BuildCsv(IEnumerable<AuditLogEntry> entries)
    {
        var csv = new StringBuilder("\uFEFFДата;Объект;Раздел / оборудование;Действие;Тираж;Пробег до;Пробег после;Метры до;Метры после;Обороты до;Обороты после;Инициатор;Подтвердил;Тип объекта;ID объекта;Correlation ID;До (JSON);После (JSON)\r\n");
        foreach (var x in entries)
        {
            var hasKnifeUsage = x.DieCutId != Guid.Empty;
            csv.Append(Csv(x.OccurredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"))).Append(';')
                .Append(Csv(AuditObject(x))).Append(';').Append(Csv(AuditContext(x))).Append(';')
                .Append(Csv(EventName(x))).Append(';').Append(x.Quantity?.ToString(CultureInfo.InvariantCulture)).Append(';')
                .Append(hasKnifeUsage ? x.MileageBefore : "").Append(';')
                .Append(hasKnifeUsage ? x.MileageAfter : "").Append(';')
                .Append(hasKnifeUsage ? x.RunLengthMetersBefore.ToString("0.###", CultureInfo.InvariantCulture) : "").Append(';')
                .Append(hasKnifeUsage ? x.RunLengthMetersAfter.ToString("0.###", CultureInfo.InvariantCulture) : "").Append(';')
                .Append(hasKnifeUsage ? x.RevolutionsBefore : "").Append(';')
                .Append(hasKnifeUsage ? x.RevolutionsAfter : "").Append(';')
                .Append(Csv(x.EmployeeName)).Append(';')
                .Append(Csv(x.ApproverName)).Append(';')
                .Append(Csv(x.EntityType?.ToString())).Append(';')
                .Append(Csv(x.EntityId?.ToString())).Append(';')
                .Append(Csv(x.CorrelationId?.ToString())).Append(';')
                .Append(Csv(x.BeforeJson)).Append(';')
                .Append(Csv(x.AfterJson)).Append("\r\n");
        }
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static byte[] BuildPdf(IReadOnlyList<AuditLogEntry> entries)
    {
        DieCutDrawingPdfGenerator.EnsureFontResolver();
        using var document = new PdfDocument();
        document.Info.Title = "Журнал действий";
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
            graphics.DrawString("Журнал действий", new XFont("Arial", 14, XFontStyleEx.Bold),
                XBrushes.Black, new XRect(margin, 10, page.Width.Point - margin * 2, 24), XStringFormats.TopLeft);
            y = 45;
            graphics.DrawString($"Страница {document.PageCount}", regular, XBrushes.Gray,
                new XPoint(page.Width.Point - margin - 48, page.Height.Point - 12));
            DrawRow(new[] { "Дата", "Объект", "Раздел / оборудование", "Действие", "Тираж", "Пробег", "Метры", "Обороты", "Сотрудник" }, bold);
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
            var hasKnifeUsage = entry.DieCutId != Guid.Empty;
            DrawRow(new[]
            {
                entry.OccurredAt.ToLocalTime().ToString("dd.MM.yy HH:mm"),
                AuditObject(entry), AuditContext(entry), EventName(entry),
                entry.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "",
                hasKnifeUsage ? $"{entry.MileageBefore} -> {entry.MileageAfter}" : "",
                hasKnifeUsage ? $"{entry.RunLengthMetersBefore.ToString("0.##", CultureInfo.InvariantCulture)} -> {entry.RunLengthMetersAfter.ToString("0.##", CultureInfo.InvariantCulture)}" : "",
                hasKnifeUsage ? $"{entry.RevolutionsBefore} -> {entry.RevolutionsAfter}" : "",
                string.IsNullOrWhiteSpace(entry.ApproverName)
                    ? entry.EmployeeName
                    : $"{entry.EmployeeName} / {entry.ApproverName}"
            }, regular);
        }
        graphics?.Dispose();
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    private static CatalogReferenceKind ToKind(CatalogReferenceType type) => type switch
    {
        CatalogReferenceType.Material => CatalogReferenceKind.Material,
        CatalogReferenceType.Figure => CatalogReferenceKind.Figure,
        _ => throw new ValidationException("Некорректный тип справочника.")
    };
    private static string SystemReferenceName(CatalogReferenceType type) => type switch
    {
        CatalogReferenceType.Material => "Материалы",
        CatalogReferenceType.Figure => "Фигуры",
        CatalogReferenceType.Equipment => "Оборудование",
        _ => type.ToString()
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
    private static IReadOnlyDictionary<string, string> PrepareImportNames(IReadOnlyList<string> names, int maxLength)
    {
        if (names is null) throw new ValidationException("Файл импорта не содержит значений.");
        if (names.Count > 10_000) throw new ValidationException("За один раз можно импортировать не более 10 000 строк.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in names)
        {
            var clean = value?.Trim() ?? string.Empty;
            if (clean.Length == 0 || clean.Length > maxLength) continue;
            result.TryAdd(Normalize(clean), clean);
        }
        return result;
    }
    private async Task ValidateGroupAsync(Guid? groupId, CancellationToken cancellationToken)
    {
        if (groupId.HasValue && !await dbContext.ReferenceDirectoryGroups.AnyAsync(x => x.Id == groupId, cancellationToken))
            throw new ValidationException("Выбранная группа не найдена.");
    }
    private async Task<ReferenceDirectory> GetMutableDirectoryAsync(
        Guid directoryId,
        CancellationToken cancellationToken)
    {
        var directory = await dbContext.ReferenceDirectories.SingleOrDefaultAsync(
            x => x.Id == directoryId, cancellationToken)
            ?? throw new ValidationException("Справочник не найден.");
        if (directory.IsArchived)
            throw new ValidationException("Нельзя изменить архивный справочник. Сначала восстановите его.");
        return directory;
    }
    private static string ValidateDirectoryName(string? name, int max)
    {
        var clean = name?.Trim() ?? "";
        if (clean.Length == 0 || clean.Length > max)
            throw new ValidationException($"Название должно содержать от 1 до {max} символов.");
        return clean;
    }
    private static string? CleanDescription(string? description)
    {
        var clean = description?.Trim();
        if (clean?.Length > 500) throw new ValidationException("Описание не должно превышать 500 символов.");
        return string.IsNullOrEmpty(clean) ? null : clean;
    }
    private static string? CleanArticle(string? articleRtf)
    {
        if (articleRtf?.Length > 1_000_000)
            throw new ValidationException("Текст карточки не должен превышать 1 МБ.");
        return string.IsNullOrWhiteSpace(articleRtf) ? null : articleRtf;
    }
    private static ReferenceDirectoryItem ToItem(ReferenceDirectory x, int count) =>
        new(x.Id, x.GroupId, x.Name, x.Description, x.SortOrder, x.IsArchived, count);
    private static ReferenceDirectoryValueItem ToItem(ReferenceDirectoryValue x) =>
        new(x.Id, x.DirectoryId, x.Name, x.SortOrder, x.IsArchived, x.UpdatedAt, x.ArticleRtf);
    private static string AuditObject(AuditLogEntry entry)
    {
        if (!entry.EntityType.HasValue) return entry.DieCutNumber;
        var beforeName = SnapshotValue(entry.BeforeJson, "name");
        var afterName = SnapshotValue(entry.AfterJson, "name");
        if (!string.IsNullOrWhiteSpace(beforeName) && !string.IsNullOrWhiteSpace(afterName) &&
            !string.Equals(beforeName, afterName, StringComparison.Ordinal))
            return $"{beforeName} -> {afterName}";
        return afterName ?? beforeName ?? $"{EntityTypeName(entry.EntityType.Value)} {entry.EntityId}";
    }
    private static string AuditContext(AuditLogEntry entry)
    {
        if (!entry.EntityType.HasValue) return entry.Equipment;
        var beforeSection = SnapshotValue(entry.BeforeJson, "section") ??
                            SnapshotValue(entry.BeforeJson, "directoryName");
        var afterSection = SnapshotValue(entry.AfterJson, "section") ??
                           SnapshotValue(entry.AfterJson, "directoryName");
        if (!string.IsNullOrWhiteSpace(beforeSection) && !string.IsNullOrWhiteSpace(afterSection) &&
            !string.Equals(beforeSection, afterSection, StringComparison.Ordinal))
            return $"{beforeSection} -> {afterSection}";
        return afterSection ?? beforeSection ?? EntityTypeName(entry.EntityType.Value);
    }

    private static string EventName(AuditLogEntry entry)
    {
        if (entry.AuditAction.HasValue)
            return entry.AuditAction.Value switch
            {
                AuditAction.Created => "Объект справочника создан",
                AuditAction.Updated => "Объект справочника изменён",
                AuditAction.Deleted => "Объект справочника удалён",
                AuditAction.Imported => "Выполнен CSV import",
                AuditAction.ArticleUpdated => "Технологическая статья изменена",
                AuditAction.Archived => "Объект справочника архивирован",
                AuditAction.Restored => "Объект справочника восстановлен",
                AuditAction.Copied => "Позиция справочника скопирована",
                AuditAction.Moved => "Позиция справочника перенесена",
                _ => entry.AuditAction.Value.ToString()
            };

        return entry.AccessType switch
        {
            EmployeeAccessEventType.LoggedIn => "Вход в систему",
            EmployeeAccessEventType.LoggedOut => "Выход из системы",
            _ => EventName(entry.Type)
        };
    }

    private static bool AuditEntryMatches(AuditLogEntry entry, string term) =>
        Contains(entry.EmployeeName, term) ||
        Contains(entry.ApproverName, term) ||
        Contains(entry.EntityType?.ToString(), term) ||
        Contains(entry.AuditAction?.ToString(), term) ||
        Contains(entry.EntityId?.ToString(), term) ||
        Contains(entry.CorrelationId?.ToString(), term) ||
        Contains(entry.DisplayObject, term) ||
        Contains(entry.DisplayContext, term) ||
        Contains(entry.BeforeJson, term) ||
        Contains(entry.AfterJson, term);

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

    private static string EntityTypeName(AuditEntityType type) => type switch
    {
        AuditEntityType.ReferenceGroup => "Группа справочников",
        AuditEntityType.ReferenceDirectory => "Справочник",
        AuditEntityType.ReferenceValue => "Значение справочника",
        AuditEntityType.Material => "Материал",
        AuditEntityType.Figure => "Фигура",
        AuditEntityType.Equipment => "Оборудование",
        AuditEntityType.Employee => "Сотрудник",
        AuditEntityType.DieCut => "Нож",
        _ => type.ToString()
    };

    private static string? SnapshotValue(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return FindSnapshotValue(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindSnapshotValue(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
                var nested = FindSnapshotValue(property.Value, propertyName);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindSnapshotValue(item, propertyName);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

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
