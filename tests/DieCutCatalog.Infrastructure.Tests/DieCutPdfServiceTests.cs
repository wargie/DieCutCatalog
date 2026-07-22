using DieCutCatalog.Domain.Catalog;
using DieCutCatalog.Infrastructure.Catalog;
using DieCutCatalog.Infrastructure.Employees;
using DieCutCatalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DieCutCatalog.Infrastructure.Tests;

public sealed class DieCutPdfServiceTests
{
    [Fact]
    public async Task Generated_drawing_is_stored_and_can_be_recognized()
    {
        using var storage = new TemporaryStorage();
        await using var db = CreateDatabase();
        var dieCut = CreateDieCut();
        db.DieCuts.Add(dieCut);
        await db.SaveChangesAsync();
        var service = CreateService(db, storage.Path);

        var generated = await service.GenerateAsync(dieCut.Id, Guid.NewGuid());
        Assert.NotNull(generated);
        Assert.Equal(DieCutDocumentSource.Generated, generated.Source);

        var generatedEvent = await db.DieCutEvents.SingleAsync(x => x.DieCutId == dieCut.Id);
        Assert.Equal(DieCutEventType.DrawingGenerated, generatedEvent.Type);

        var stored = await service.OpenAsync(dieCut.Id, generated.Id);
        Assert.NotNull(stored);
        using var copy = new MemoryStream();
        await stored.Content.CopyToAsync(copy);
        await stored.Content.DisposeAsync();
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(copy.ToArray(), 0, 5));

        copy.Position = 0;
        using (var pdf = UglyToad.PdfPig.PdfDocument.Open(copy))
        {
            var page = pdf.GetPage(1);
            var pageText = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page);
            Assert.Contains(
                "Corner radius = 2 mm, vertical break = 2.5 mm, horizontal break = 1.967 mm",
                pageText);

            var contours = page.Paths
                .Select(path => path.GetBoundingRectangle())
                .Where(rectangle => rectangle is not null)
                .Select(rectangle => rectangle!.Value)
                .Where(rectangle =>
                    Math.Abs(rectangle.Width * 25.4 / 72 - 50) < 0.001
                    && Math.Abs(rectangle.Height * 25.4 / 72 - 70) < 0.001)
                .ToArray();

            Assert.Equal(12, contours.Length);
        }

        copy.Position = 0;
        var preview = await service.PreviewAsync(copy, copy.Length);
        Assert.Equal("419R", preview.Number);
        Assert.Equal(68, preview.Shaft);
        Assert.Equal(50m, preview.LabelWidth);
        Assert.Equal(70m, preview.LabelLength);
        Assert.Equal(4, preview.Streams);
        Assert.Equal(3, preview.Repeats);
        Assert.Equal(2.5m, preview.GrooveSpacing);
        Assert.Equal(2m, preview.LabelCornerRadius);
        Assert.Equal(220m, preview.MaterialWidth);
        Assert.Equal("FENIX LABEL S Y422", preview.Material);
        Assert.Empty(preview.Warnings);
    }

    [Fact]
    public async Task Uploaded_pdf_is_kept_with_its_checksum()
    {
        using var storage = new TemporaryStorage();
        await using var db = CreateDatabase();
        var dieCut = CreateDieCut();
        db.DieCuts.Add(dieCut);
        await db.SaveChangesAsync();
        var service = CreateService(db, storage.Path);

        var generated = await service.GenerateAsync(dieCut.Id, Guid.NewGuid());
        var source = await service.OpenAsync(dieCut.Id, generated!.Id);
        using var bytes = new MemoryStream();
        await source!.Content.CopyToAsync(bytes);
        await source.Content.DisposeAsync();
        bytes.Position = 0;

        var uploaded = await service.UploadAsync(
            dieCut.Id, "original scheme.exe", bytes, bytes.Length, Guid.NewGuid());

        Assert.NotNull(uploaded);
        Assert.Equal(DieCutDocumentSource.Uploaded, uploaded.Source);
        Assert.Equal("original scheme.pdf", uploaded.FileName);
        Assert.Equal(generated.Sha256, uploaded.Sha256);
        Assert.Equal(2, await db.DieCutDocuments.CountAsync());
    }

    private static CatalogDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CatalogDbContext(options);
    }

    private static DieCutPdfService CreateService(CatalogDbContext db, string storagePath) =>
        new(db, Options.Create(new StorageOptions { RootPath = storagePath }));

    private static DieCut CreateDieCut()
    {
        var equipment = new Equipment { Id = Guid.NewGuid(), Name = "Nilpeter", NormalizedName = "NILPETER" };
        return new DieCut
        {
            Id = Guid.NewGuid(),
            Number = "419R",
            NormalizedNumber = "419R",
            Equipment = equipment,
            EquipmentId = equipment.Id,
            Shaft = 68,
            X = 50,
            Y = 70,
            Streams = 4,
            Repeats = 3,
            GrooveSpacing = 2.5m,
            LabelCornerRadius = 2,
            GapX = 0.02m,
            GapY = 0.001966667m,
            Material = "FENIX LABEL S Y422",
            H = 220,
            Figure = "прямоугольник",
            CreatedByEmployeeId = Guid.NewGuid(),
            UpdatedByEmployeeId = Guid.NewGuid()
        };
    }

    private sealed class TemporaryStorage : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "DieCutCatalogTests", Guid.NewGuid().ToString("N"));

        public TemporaryStorage() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}