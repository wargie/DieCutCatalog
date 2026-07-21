using System.Security.Cryptography;
using DieCutCatalog.Application.Catalog;
using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Infrastructure.Employees;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DieCutCatalog.Infrastructure.Catalog;

public sealed class DieCutPdfService(
    CatalogDbContext dbContext,
    IOptions<StorageOptions> storageOptions) : IDieCutPdfService
{
    private const long MaximumPdfSize = 20 * 1024 * 1024;
    private readonly string _storageRoot = Path.GetFullPath(storageOptions.Value.RootPath);

    public async Task<PdfImportPreview> PreviewAsync(
        Stream content,
        long size,
        CancellationToken cancellationToken = default)
    {
        ValidateSize(size);
        var bytes = await ReadPdfAsync(content, size, cancellationToken);
        await using var stream = new MemoryStream(bytes, writable: false);
        return PdfImportParser.Parse(stream);
    }

    public async Task<DieCutDocumentDetails?> UploadAsync(
        Guid dieCutId,
        string fileName,
        Stream content,
        long size,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        ValidateSize(size);
        if (!await dbContext.DieCuts.AnyAsync(x => x.Id == dieCutId, cancellationToken)) return null;
        var bytes = await ReadPdfAsync(content, size, cancellationToken);
        return await StoreAsync(
            dieCutId,
            SafeFileName(fileName),
            bytes,
            DieCutDocumentSource.Uploaded,
            employeeId,
            cancellationToken);
    }

    public async Task<DieCutDocumentDetails?> GenerateAsync(
        Guid dieCutId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var dieCut = await dbContext.DieCuts
            .Include(x => x.Equipment)
            .SingleOrDefaultAsync(x => x.Id == dieCutId, cancellationToken);
        if (dieCut is null) return null;

        var bytes = DieCutDrawingPdfGenerator.Generate(dieCut);
        var fileName = $"{SafeNamePart(dieCut.Number)}_{DateTime.UtcNow:yyyyMMdd-HHmmss}.pdf";
        return await StoreAsync(
            dieCutId,
            fileName,
            bytes,
            DieCutDocumentSource.Generated,
            employeeId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<DieCutDocumentDetails>?> ListAsync(
        Guid dieCutId,
        CancellationToken cancellationToken = default)
    {
        if (!await dbContext.DieCuts.AsNoTracking().AnyAsync(x => x.Id == dieCutId, cancellationToken)) return null;
        return await dbContext.DieCutDocuments
            .AsNoTracking()
            .Where(x => x.DieCutId == dieCutId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new DieCutDocumentDetails(x.Id, x.OriginalFileName, x.Source, x.Size, x.Sha256, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<StoredPdf?> OpenAsync(
        Guid dieCutId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await dbContext.DieCutDocuments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == documentId && x.DieCutId == dieCutId, cancellationToken);
        if (document is null) return null;

        var fullPath = ResolveStoragePath(document.StoragePath);
        if (!File.Exists(fullPath)) return null;
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return new StoredPdf(document.OriginalFileName, document.ContentType, stream);
    }

    private async Task<DieCutDocumentDetails> StoreAsync(
        Guid dieCutId,
        string fileName,
        byte[] bytes,
        DieCutDocumentSource source,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var relativePath = Path.Combine("die-cuts", dieCutId.ToString("N"), $"{id:N}.pdf");
        var fullPath = ResolveStoragePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);

        var document = new DieCutDocument
        {
            Id = id,
            DieCutId = dieCutId,
            OriginalFileName = fileName,
            StoragePath = relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            Size = bytes.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Source = source,
            CreatedByEmployeeId = employeeId
        };

        if (source == DieCutDocumentSource.Generated)
        {
            var dieCut = await dbContext.DieCuts.SingleAsync(x => x.Id == dieCutId, cancellationToken);
            dbContext.DieCutEvents.Add(new DieCutEvent
            {
                DieCutId = dieCutId,
                EmployeeId = employeeId,
                Type = DieCutEventType.DrawingGenerated,
                MileageBefore = dieCut.Mileage,
                MileageAfter = dieCut.Mileage,
                RunLengthMetersBefore = dieCut.RunLengthMeters,
                RunLengthMetersAfter = dieCut.RunLengthMeters,
                RevolutionsBefore = dieCut.Revolutions,
                RevolutionsAfter = dieCut.Revolutions,
                OccurredAt = document.CreatedAt
            });
        }

        try
        {
            dbContext.DieCutDocuments.Add(document);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            File.Delete(fullPath);
            throw;
        }

        return Map(document);
    }

    private static async Task<byte[]> ReadPdfAsync(Stream content, long size, CancellationToken cancellationToken)
    {
        using var output = size > 0 ? new MemoryStream((int)size) : new MemoryStream();
        await content.CopyToAsync(output, cancellationToken);
        if (output.Length > MaximumPdfSize) throw new InvalidDataException("Размер PDF не должен превышать 20 МБ.");
        var bytes = output.ToArray();
        if (bytes.Length < 5 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F' || bytes[4] != '-')
            throw new InvalidDataException("Выбранный файл не является PDF.");
        return bytes;
    }

    private static void ValidateSize(long size)
    {
        if (size <= 0) throw new InvalidDataException("PDF-файл пуст.");
        if (size > MaximumPdfSize) throw new InvalidDataException("Размер PDF не должен превышать 20 МБ.");
    }

    private string ResolveStoragePath(string relativePath)
    {
        var normalizedRoot = _storageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Недопустимый путь документа.");
        return fullPath;
    }

    private static string SafeFileName(string fileName)
    {
        var name = SafeNamePart(Path.GetFileNameWithoutExtension(fileName));
        if (name.Length > 240) name = name[..240];
        return $"{name}.pdf";
    }

    private static string SafeNamePart(string value)
    {
        const string invalidOnWindows = "<>:/|?*";
        var safe = new string(value
            .Select(character => character < 32
                || character == 34
                || character == 92
                || invalidOnWindows.Contains(character)
                || Path.GetInvalidFileNameChars().Contains(character)
                    ? '_'
                    : character)
            .ToArray())
            .Trim(' ', '.');
        return string.IsNullOrWhiteSpace(safe) ? "die-cut" : safe;
    }

    private static DieCutDocumentDetails Map(DieCutDocument document) =>
        new(document.Id, document.OriginalFileName, document.Source, document.Size, document.Sha256, document.CreatedAt);
}