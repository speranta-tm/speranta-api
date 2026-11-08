using Speranta.Domain.Entities;
using Speranta.Infrastructure;

namespace Speranta.Api.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Folder for uploaded files. Relative paths resolve against the content root.</summary>
    public string Path { get; set; } = "App_Data/files";
}

public enum UploadKind
{
    /// <summary>Announcement covers: JPG, PNG or WebP, up to 5 MB (the editor says so).</summary>
    Image,

    /// <summary>The monthly flyer: PDF, up to 20 MB.</summary>
    Pdf,
}

/// <summary>
/// Keeps uploads on local disk. The file type is decided by the bytes, not by the
/// name or the Content-Type the browser claimed: both are whatever the client says.
/// </summary>
public sealed class FileStorage(
    Microsoft.Extensions.Options.IOptions<StorageOptions> options,
    IWebHostEnvironment env,
    SperantaDbContext db,
    TimeProvider clock)
{
    public const long MaxImageBytes = 5 * 1024 * 1024;
    public const long MaxPdfBytes = 20 * 1024 * 1024;

    private string Root => System.IO.Path.GetFullPath(
        System.IO.Path.Combine(env.ContentRootPath, options.Value.Path));

    public static string UrlFor(Guid id) => $"/files/{id}";

    /// <summary>Saves the upload, or returns the Romanian reason it was refused.</summary>
    public async Task<(StoredFile? File, string? Error)> SaveAsync(
        IFormFile upload, UploadKind kind, CancellationToken ct)
    {
        var max = kind == UploadKind.Image ? MaxImageBytes : MaxPdfBytes;

        if (upload.Length == 0)
        {
            return (null, "Fișierul este gol.");
        }

        if (upload.Length > max)
        {
            return (null, $"Fișierul depășește {max / 1024 / 1024} MB.");
        }

        var header = new byte[12];
        await using (var peek = upload.OpenReadStream())
        {
            _ = await peek.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        }

        var contentType = Sniff(header);
        var allowed = kind == UploadKind.Image
            ? contentType is "image/jpeg" or "image/png" or "image/webp"
            : contentType is "application/pdf";

        if (!allowed)
        {
            return (null, kind == UploadKind.Image
                ? "Sunt acceptate doar imagini JPG, PNG sau WebP."
                : "Pliantul trebuie să fie un fișier PDF.");
        }

        var file = new StoredFile
        {
            Id = Guid.NewGuid(),
            FileName = CleanName(upload.FileName, contentType!),
            ContentType = contentType!,
            SizeBytes = upload.Length,
            CreatedAt = clock.GetUtcNow(),
        };

        Directory.CreateDirectory(Root);
        await using (var target = File.Create(PathOf(file.Id)))
        await using (var source = upload.OpenReadStream())
        {
            await source.CopyToAsync(target, ct);
        }

        db.Files.Add(file);
        await db.SaveChangesAsync(ct);
        return (file, null);
    }

    public Stream? Open(Guid id)
    {
        var path = PathOf(id);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    private string PathOf(Guid id) => System.IO.Path.Combine(Root, id.ToString("N"));

    internal static string? Sniff(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 4 && h[0] == 0x25 && h[1] == 0x50 && h[2] == 0x44 && h[3] == 0x46)
        {
            return "application/pdf"; // %PDF
        }

        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (h.Length >= 8 && h[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    /// <summary>Keeps a readable name for downloads ("pliant-noiembrie-2026.pdf")
    /// without trusting any path or odd characters the browser sent.</summary>
    private static string CleanName(string? name, string contentType)
    {
        var bare = System.IO.Path.GetFileName(name ?? "");
        var safe = new string(bare.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ').ToArray()).Trim();
        if (safe.Length > 120)
        {
            safe = safe[^120..];
        }

        var ext = contentType switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => ".webp",
        };

        if (safe.Length == 0)
        {
            return "fisier" + ext;
        }

        return safe.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
            || (ext == ".jpg" && safe.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            ? safe
            : safe + ext;
    }
}
