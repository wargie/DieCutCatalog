using System.ComponentModel.DataAnnotations;
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

    [Fact]
    public async Task Generated_circle_uses_elliptic_contours_in_exact_label_dimensions()
    {
        using var storage = new TemporaryStorage();
        await using var db = CreateDatabase();
        var dieCut = CreateDieCut();
        dieCut.Figure = "КРУГ";
        dieCut.X = 50;
        dieCut.Y = 50;
        dieCut.Streams = 2;
        dieCut.Repeats = 3;
        db.DieCuts.Add(dieCut);
        await db.SaveChangesAsync();
        var service = CreateService(db, storage.Path);

        var generated = await service.GenerateAsync(dieCut.Id, Guid.NewGuid());
        var stored = await service.OpenAsync(dieCut.Id, generated!.Id);
        using var copy = new MemoryStream();
        await stored!.Content.CopyToAsync(copy);
        await stored.Content.DisposeAsync();
        copy.Position = 0;

        using var pdf = UglyToad.PdfPig.PdfDocument.Open(copy);
        var contours = pdf.GetPage(1).Paths
            .Where(path =>
            {
                var rectangle = path.GetBoundingRectangle();
                return rectangle is not null
                    && Math.Abs(rectangle.Value.Width * 25.4 / 72 - 50) < 0.001
                    && Math.Abs(rectangle.Value.Height * 25.4 / 72 - 50) < 0.001;
            })
            .ToArray();

        Assert.Equal(6, contours.Length);
        Assert.All(contours, contour =>
        {
            var subpath = Assert.Single(contour);
            Assert.Equal(4, subpath.Commands.Count(
                command => command is UglyToad.PdfPig.Core.PdfSubpath.CubicBezierCurve));
            Assert.DoesNotContain(subpath.Commands,
                command => command is UglyToad.PdfPig.Core.PdfSubpath.Line);
        });
    }

    [Fact]
    public async Task Generated_square_uses_exact_equal_label_dimensions()
    {
        using var storage = new TemporaryStorage();
        await using var db = CreateDatabase();
        var dieCut = CreateDieCut();
        dieCut.Figure = "квадрат";
        dieCut.X = 50;
        dieCut.Y = 50;
        dieCut.LabelCornerRadius = 0;
        db.DieCuts.Add(dieCut);
        await db.SaveChangesAsync();
        var service = CreateService(db, storage.Path);

        var generated = await service.GenerateAsync(dieCut.Id, Guid.NewGuid());
        var stored = await service.OpenAsync(dieCut.Id, generated!.Id);
        using var copy = new MemoryStream();
        await stored!.Content.CopyToAsync(copy);
        await stored.Content.DisposeAsync();
        copy.Position = 0;

        using var pdf = UglyToad.PdfPig.PdfDocument.Open(copy);
        var contours = pdf.GetPage(1).Paths
            .Select(path => path.GetBoundingRectangle())
            .Where(rectangle => rectangle is not null)
            .Select(rectangle => rectangle!.Value)
            .Where(rectangle =>
                Math.Abs(rectangle.Width * 25.4 / 72 - 50) < 0.001
                && Math.Abs(rectangle.Height * 25.4 / 72 - 50) < 0.001)
            .ToArray();

        Assert.Equal(dieCut.Streams * dieCut.Repeats, contours.Length);
    }

    [Theory]
    [InlineData("специальная форма")]
    [InlineData("перфорация")]
    [InlineData("фигурный")]
    [InlineData("произвольный контур")]
    public async Task Generate_rejects_figures_without_a_parametric_contour(string figure)
    {
        using var storage = new TemporaryStorage();
        await using var db = CreateDatabase();
        var dieCut = CreateDieCut();
        dieCut.Figure = figure;
        db.DieCuts.Add(dieCut);
        await db.SaveChangesAsync();
        var service = CreateService(db, storage.Path);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.GenerateAsync(dieCut.Id, Guid.NewGuid()));

        Assert.Contains("загрузите утверждённый PDF", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.DieCutDocuments.ToListAsync());
        Assert.Empty(await db.DieCutEvents.ToListAsync());
        Assert.False(Directory.Exists(Path.Combine(storage.Path, "die-cuts")));
    }

    [Theory]
    [InlineData("круг")]
    [InlineData("квадрат")]
    public async Task Generate_rejects_circle_or_square_with_different_dimensions(string figure)
    {
        using var storage = new TemporaryStorage();
        await using var db = CreateDatabase();
        var dieCut = CreateDieCut();
        dieCut.Figure = figure;
        dieCut.X = 50;
        dieCut.Y = 70;
        db.DieCuts.Add(dieCut);
        await db.SaveChangesAsync();
        var service = CreateService(db, storage.Path);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.GenerateAsync(dieCut.Id, Guid.NewGuid()));

        Assert.Contains("размеры L и B должны совпадать", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.DieCutDocuments.ToListAsync());
        Assert.Empty(await db.DieCutEvents.ToListAsync());
    }

    [Fact]
    public async Task Generate_rejects_layout_that_exceeds_material_width()
    {
        using var storage = new TemporaryStorage();
        await using var db = CreateDatabase();
        var dieCut = CreateDieCut();
        dieCut.Number = "01010";
        dieCut.NormalizedNumber = "01010";
        dieCut.X = 33;
        dieCut.Y = 33;
        dieCut.Streams = 6;
        dieCut.Repeats = 8;
        dieCut.GrooveSpacing = 3;
        dieCut.H = 200;
        dieCut.Figure = "круг";
        db.DieCuts.Add(dieCut);
        await db.SaveChangesAsync();
        var service = CreateService(db, storage.Path);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.GenerateAsync(dieCut.Id, Guid.NewGuid()));

        Assert.Contains("расстояния между ручьями", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.DieCutDocuments.ToListAsync());
    }
    [Fact]
    public async Task Generate_rejects_legacy_record_above_production_limits()
    {
        using var storage = new TemporaryStorage();
        await using var db = CreateDatabase();
        var dieCut = CreateDieCut();
        dieCut.Streams = DieCutParameterLimits.MaximumStreams + 1;
        db.DieCuts.Add(dieCut);
        await db.SaveChangesAsync();
        var service = CreateService(db, storage.Path);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            service.GenerateAsync(dieCut.Id, Guid.NewGuid()));

        Assert.Contains(DieCutParameterLimits.MaximumStreams.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Empty(await db.DieCutDocuments.ToListAsync());
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
