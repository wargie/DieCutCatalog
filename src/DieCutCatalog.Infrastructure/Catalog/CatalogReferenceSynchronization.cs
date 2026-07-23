using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DieCutCatalog.Infrastructure.Catalog;

internal static class CatalogReferenceSynchronization
{
    public static async Task EnsureCatalogReferencesAsync(
        this CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var existing = (await dbContext.CatalogReferenceEntries
                .Select(x => new { x.Kind, x.NormalizedName })
                .ToListAsync(cancellationToken))
            .Select(x => (x.Kind, x.NormalizedName))
            .ToHashSet();

        var materials = await dbContext.DieCuts.AsNoTracking()
            .Select(x => x.Material)
            .Distinct()
            .ToListAsync(cancellationToken);
        var figures = await dbContext.DieCuts.AsNoTracking()
            .Select(x => x.Figure)
            .Distinct()
            .ToListAsync(cancellationToken);

        AddMissing(dbContext, existing, CatalogReferenceKind.Material, materials);
        AddMissing(dbContext, existing, CatalogReferenceKind.Figure, figures);
        if (dbContext.ChangeTracker.HasChanges())
            await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void AddMissing(
        CatalogDbContext dbContext,
        HashSet<(CatalogReferenceKind Kind, string NormalizedName)> existing,
        CatalogReferenceKind kind,
        IEnumerable<string> names)
    {
        foreach (var name in names.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var clean = name.Trim();
            var normalized = clean.ToUpperInvariant();
            if (!existing.Add((kind, normalized))) continue;
            dbContext.CatalogReferenceEntries.Add(new CatalogReferenceEntry
            {
                Kind = kind,
                Name = clean,
                NormalizedName = normalized
            });
        }
    }
}
