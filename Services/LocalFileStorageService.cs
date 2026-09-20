namespace AiEmailer.Api.Services;

public class LocalFileStorageService : IFileStorageService
{
    private const string AttachmentsFolder = "attachments";
    // Fixed-width prefix: yyyyMMddHHmmssfff (17 digits) + "_" = 18 chars.
    private const int PrefixLength = 18;

    private readonly string _attachmentsRoot;

    public LocalFileStorageService(IWebHostEnvironment env)
    {
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        _attachmentsRoot = Path.Combine(webRoot, AttachmentsFolder);
        Directory.CreateDirectory(_attachmentsRoot);
    }

    public async Task<StoredFile> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default)
    {
        var safeName = Path.GetFileName(originalFileName); // strips any path segments
        var storedFileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{safeName}";
        var physicalPath = Path.Combine(_attachmentsRoot, storedFileName);

        await using (var fileStream = new FileStream(physicalPath, FileMode.CreateNew, FileAccess.Write))
        {
            if (content.CanSeek) content.Position = 0;
            await content.CopyToAsync(fileStream, ct);
        }

        return new StoredFile(storedFileName, $"/{AttachmentsFolder}/{storedFileName}");
    }

    public string GetPhysicalPath(string storedFileName) => Path.Combine(_attachmentsRoot, storedFileName);

    public string GetOriginalFileName(string storedFileName)
    {
        if (storedFileName.Length <= PrefixLength) return storedFileName;
        var prefix = storedFileName[..(PrefixLength - 1)];
        return storedFileName[PrefixLength - 1] == '_' && prefix.All(char.IsDigit)
            ? storedFileName[PrefixLength..]
            : storedFileName; // defensive: don't mangle unexpected formats
    }

    public bool Exists(string storedFileName) => File.Exists(GetPhysicalPath(storedFileName));
    public void Delete(string storedFileName)
    {
        var path = GetPhysicalPath(storedFileName);
        if (File.Exists(path)) File.Delete(path);
    }
}