using System.Buffers.Binary;
using System.Text.RegularExpressions;
using ECommerceStore.Web.Services.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;

namespace ECommerceStore.Web.Services.Images;

public sealed record ProductImageSaveResult(bool Succeeded, string? RelativePath = null, string? Error = null)
{
    public static ProductImageSaveResult Invalid(string message) => new(false, Error: message);
    public static ProductImageSaveResult Saved(string path) => new(true, path);
}

public interface IProductImageService
{
    Task<ProductImageSaveResult> ValidateAndSaveAsync(IFormFile file, CancellationToken cancellationToken = default);
    Task<bool> TryDeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Validates three independent client signals (extension, declared MIME, and decoded format), bounds
/// decoded dimensions, then re-encodes the raster before a create-new write. Re-encoding strips any
/// ignored trailing/polyglot payload instead of serving the original untrusted bytes.
/// </summary>
public sealed partial class ProductImageService : IProductImageService
{
    private static readonly IReadOnlyDictionary<string, (string Mime, string Format, string CanonicalExtension)> Allowed =
        new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = ("image/jpeg", "JPEG", ".jpg"),
            [".jpeg"] = ("image/jpeg", "JPEG", ".jpg"),
            [".png"] = ("image/png", "PNG", ".png"),
            [".webp"] = ("image/webp", "WEBP", ".webp")
        };

    private readonly UploadOptions _options;
    private readonly string _relativeRoot;
    private readonly string _storageRoot;
    private readonly string _webRoot;

    public ProductImageService(IOptions<UploadOptions> options, IWebHostEnvironment environment)
    {
        _options = options.Value;
        var segments = _options.RelativeRoot.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(_options.RelativeRoot) || segments.Length == 0 ||
            segments.Any(segment => segment is "." or ".." || segment.Any(char.IsControl) || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidOperationException("Uploads:RelativeRoot must be a contained relative path.");
        }

        _relativeRoot = string.Join('/', segments);
        _webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        _storageRoot = Path.GetFullPath(Path.Combine(_webRoot, Path.Combine(segments)));
        if (!IsContained(_webRoot, _storageRoot))
        {
            throw new InvalidOperationException("The configured product upload root escapes wwwroot.");
        }
    }

    public async Task<ProductImageSaveResult> ValidateAndSaveAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
        {
            return ProductImageSaveResult.Invalid("Choose a non-empty JPEG, PNG, or WebP image.");
        }

        if (file.Length > _options.MaximumBytes)
        {
            return ProductImageSaveResult.Invalid($"The image cannot exceed {_options.MaximumBytes / (1024 * 1024)} MiB.");
        }

        var clientName = file.FileName;
        if (string.IsNullOrWhiteSpace(clientName) || clientName.Any(char.IsControl) ||
            clientName.Contains('/') || clientName.Contains('\\') || Path.IsPathRooted(clientName))
        {
            return ProductImageSaveResult.Invalid("The image filename is invalid.");
        }

        var extension = Path.GetExtension(clientName).ToLowerInvariant();
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(clientName);
        if (!Allowed.TryGetValue(extension, out var expected) || nameWithoutExtension.Contains('.'))
        {
            return ProductImageSaveResult.Invalid("Only single-extension .jpg, .jpeg, .png, and .webp files are allowed.");
        }

        if (!string.Equals(file.ContentType, expected.Mime, StringComparison.OrdinalIgnoreCase))
        {
            return ProductImageSaveResult.Invalid("The declared image type does not match its extension.");
        }

        byte[] bytes;
        try
        {
            await using var source = file.OpenReadStream();
            using var bounded = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                if (bounded.Length + read > _options.MaximumBytes)
                {
                    return ProductImageSaveResult.Invalid("The image is larger than the configured limit.");
                }
                await bounded.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            bytes = bounded.ToArray();
        }
        catch (IOException)
        {
            return ProductImageSaveResult.Invalid("The image could not be read.");
        }

        try
        {
            var detected = Image.DetectFormat(bytes);
            if (!string.Equals(detected.Name, expected.Format, StringComparison.OrdinalIgnoreCase))
            {
                return ProductImageSaveResult.Invalid("The decoded image format does not match its extension and content type.");
            }

            if (HasUnexpectedTrailingData(bytes, expected.Format))
            {
                return ProductImageSaveResult.Invalid("The image contains unexpected trailing data.");
            }

            var info = Image.Identify(bytes);
            var identifiedPixels = checked((long)info.Width * info.Height);
            if (info.Width > _options.MaximumWidth || info.Height > _options.MaximumHeight || identifiedPixels > _options.MaximumPixels)
            {
                return ProductImageSaveResult.Invalid("The image dimensions are too large.");
            }

            using var image = Image.Load(bytes);

            using var normalized = new MemoryStream();
            switch (expected.CanonicalExtension)
            {
                case ".jpg": await image.SaveAsJpegAsync(normalized, cancellationToken); break;
                case ".png": await image.SaveAsPngAsync(normalized, cancellationToken); break;
                case ".webp": await image.SaveAsWebpAsync(normalized, cancellationToken); break;
            }

            Directory.CreateDirectory(_storageRoot);
            var filename = $"{Guid.NewGuid():N}{expected.CanonicalExtension}";
            var destination = Path.GetFullPath(Path.Combine(_storageRoot, filename));
            if (!IsContained(_storageRoot, destination))
            {
                return ProductImageSaveResult.Invalid("The generated image path is invalid.");
            }

            normalized.Position = 0;
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await normalized.CopyToAsync(output, cancellationToken);
            }

            return ProductImageSaveResult.Saved($"{_relativeRoot}/{filename}");
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or NotSupportedException or OverflowException)
        {
            return ProductImageSaveResult.Invalid("The file is not a valid supported raster image.");
        }
        catch (IOException)
        {
            return ProductImageSaveResult.Invalid("The image storage location is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return ProductImageSaveResult.Invalid("The image storage location is unavailable.");
        }
    }

    public Task<bool> TryDeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith(_relativeRoot + "/", StringComparison.Ordinal) ||
            !ManagedFilename().IsMatch(Path.GetFileName(normalized)))
        {
            return Task.FromResult(false);
        }

        var fullPath = Path.GetFullPath(Path.Combine(
            _webRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsContained(_storageRoot, fullPath))
        {
            return Task.FromResult(false);
        }

        try
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
            return Task.FromResult(true);
        }
        catch (IOException)
        {
            return Task.FromResult(false);
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
    }

    private static bool IsContained(string root, string candidate)
    {
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool HasUnexpectedTrailingData(byte[] bytes, string format) => format switch
    {
        "JPEG" => bytes.Length < 2 || bytes[^2] != 0xff || bytes[^1] != 0xd9,
        "PNG" => bytes.Length < 12 ||
            !bytes.AsSpan(bytes.Length - 12, 12).SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }),
        "WEBP" => bytes.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) + 8 != bytes.Length,
        _ => true
    };

    [GeneratedRegex("^[a-f0-9]{32}\\.(jpg|png|webp)$", RegexOptions.CultureInvariant)]
    private static partial Regex ManagedFilename();
}
