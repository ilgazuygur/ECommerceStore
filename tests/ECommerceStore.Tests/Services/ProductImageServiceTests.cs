using ECommerceStore.Web.Services.Common;
using ECommerceStore.Web.Services.Images;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ECommerceStore.Tests.Services;

public sealed class ProductImageServiceTests
{
    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".webp", "image/webp")]
    public async Task Valid_rasters_are_reencoded_under_random_contained_names(string extension, string mime)
    {
        using var root = new TemporaryWebRoot();
        var service = root.CreateService();
        var result = await service.ValidateAndSaveAsync(File(CreateImage(extension), "photo" + extension, mime));

        Assert.True(result.Succeeded, result.Error);
        Assert.Matches($"^uploads/products/[a-f0-9]{{32}}\\{extension}$", result.RelativePath!);
        var fullPath = root.FullPath(result.RelativePath!);
        Assert.True(System.IO.File.Exists(fullPath));
        using var decoded = Image.Load(fullPath);
        Assert.Equal(4, decoded.Width);
        Assert.Equal(3, decoded.Height);
    }

    [Theory]
    [InlineData("photo.svg", "image/svg+xml")]
    [InlineData("photo.exe.png", "image/png")]
    [InlineData("../photo.png", "image/png")]
    [InlineData("..\\photo.png", "image/png")]
    public async Task Unsafe_extensions_and_names_are_rejected(string filename, string mime)
    {
        using var root = new TemporaryWebRoot();
        var result = await root.CreateService().ValidateAndSaveAsync(File(CreateImage(".png"), filename, mime));
        Assert.False(result.Succeeded);
        Assert.Empty(root.ManagedFiles());
    }

    [Fact]
    public async Task Empty_oversized_and_invalid_bytes_are_rejected()
    {
        using var root = new TemporaryWebRoot(maximumBytes: 128);
        var service = root.CreateService();
        Assert.False((await service.ValidateAndSaveAsync(File([], "empty.png", "image/png"))).Succeeded);
        Assert.False((await service.ValidateAndSaveAsync(File(new byte[129], "large.png", "image/png"))).Succeeded);
        Assert.False((await service.ValidateAndSaveAsync(File("not an image"u8.ToArray(), "fake.png", "image/png"))).Succeeded);
        Assert.Empty(root.ManagedFiles());
    }

    [Fact]
    public async Task Mime_extension_and_decoded_format_mismatches_are_rejected()
    {
        using var root = new TemporaryWebRoot();
        var service = root.CreateService();
        var png = CreateImage(".png");
        Assert.False((await service.ValidateAndSaveAsync(File(png, "photo.png", "image/jpeg"))).Succeeded);
        Assert.False((await service.ValidateAndSaveAsync(File(png, "photo.jpg", "image/jpeg"))).Succeeded);
        Assert.Empty(root.ManagedFiles());
    }

    [Fact]
    public async Task Trailing_polyglot_payload_is_rejected()
    {
        using var root = new TemporaryWebRoot();
        var bytes = CreateImage(".png").Concat("<script>payload</script>"u8.ToArray()).ToArray();
        var result = await root.CreateService().ValidateAndSaveAsync(File(bytes, "photo.png", "image/png"));
        Assert.False(result.Succeeded);
        Assert.Empty(root.ManagedFiles());
    }

    [Fact]
    public async Task Excessive_pixel_count_is_rejected_before_storage()
    {
        using var root = new TemporaryWebRoot(maximumPixels: 4);
        var result = await root.CreateService().ValidateAndSaveAsync(File(CreateImage(".png", 3, 3), "large-dimensions.png", "image/png"));
        Assert.False(result.Succeeded);
        Assert.Empty(root.ManagedFiles());
    }

    [Fact]
    public async Task Excessive_width_is_rejected_before_storage()
    {
        using var root = new TemporaryWebRoot(maximumWidth: 5);
        var result = await root.CreateService().ValidateAndSaveAsync(File(CreateImage(".png", 8, 2), "too-wide.png", "image/png"));
        Assert.False(result.Succeeded);
        Assert.Empty(root.ManagedFiles());
    }

    [Fact]
    public async Task Excessive_height_is_rejected_before_storage()
    {
        using var root = new TemporaryWebRoot(maximumHeight: 5);
        var result = await root.CreateService().ValidateAndSaveAsync(File(CreateImage(".png", 2, 8), "too-tall.png", "image/png"));
        Assert.False(result.Succeeded);
        Assert.Empty(root.ManagedFiles());
    }

    [Fact]
    public async Task Delete_only_removes_generated_files_inside_managed_root()
    {
        using var root = new TemporaryWebRoot();
        var service = root.CreateService();
        var saved = await service.ValidateAndSaveAsync(File(CreateImage(".png"), "photo.png", "image/png"));
        var unrelated = root.FullPath("uploads/products/keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelated)!);
        await System.IO.File.WriteAllTextAsync(unrelated, "keep");

        Assert.False(await service.TryDeleteAsync("uploads/products/keep.txt"));
        Assert.False(await service.TryDeleteAsync("uploads/products/../../keep.txt"));
        Assert.True(System.IO.File.Exists(unrelated));
        Assert.True(await service.TryDeleteAsync(saved.RelativePath!));
        Assert.False(System.IO.File.Exists(root.FullPath(saved.RelativePath!)));
        Assert.True(System.IO.File.Exists(unrelated));
    }

    private static IFormFile File(byte[] bytes, string filename, string contentType)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "ImageUpload", filename)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static byte[] CreateImage(string extension, int width = 4, int height = 3)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(32, 96, 64));
        using var stream = new MemoryStream();
        switch (extension)
        {
            case ".jpg": image.SaveAsJpeg(stream); break;
            case ".png": image.SaveAsPng(stream); break;
            case ".webp": image.SaveAsWebp(stream); break;
            default: image.SaveAsPng(stream); break;
        }
        return stream.ToArray();
    }

    private sealed class TemporaryWebRoot : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"ecommerce-images-{Guid.NewGuid():N}");
        private readonly UploadOptions _options;

        public TemporaryWebRoot(long maximumBytes = 1024 * 1024, long maximumPixels = 1_000_000,
            int maximumWidth = 1000, int maximumHeight = 1000)
        {
            Directory.CreateDirectory(Path.Combine(_root, "wwwroot"));
            _options = new UploadOptions
            {
                MaximumBytes = maximumBytes,
                MaximumWidth = maximumWidth,
                MaximumHeight = maximumHeight,
                MaximumPixels = maximumPixels,
                RelativeRoot = "uploads/products"
            };
        }

        public ProductImageService CreateService() => new(Options.Create(_options), new TestEnvironment
        {
            ContentRootPath = _root,
            WebRootPath = Path.Combine(_root, "wwwroot")
        });

        public string FullPath(string relativePath) => Path.Combine(_root, "wwwroot", relativePath.Replace('/', Path.DirectorySeparatorChar));

        public string[] ManagedFiles()
        {
            var directory = FullPath("uploads/products");
            return Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ECommerceStore.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
