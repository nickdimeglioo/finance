using FinanceTracker.Api.Features.Storage;
using FinanceTracker.Api.Infrastructure.Storage;
using PipelineRunner.Services;

namespace FinanceTracker.Api.Services;

public sealed class StorageFileService
{
    private const long MaxStorageFileBytes = 25 * 1024 * 1024;

    private readonly ICurrentUserContext _currentUser;
    private readonly IOrmMapperService _db;
    private readonly IObjectStorageService _storage;

    public StorageFileService(ICurrentUserContext currentUser, IOrmMapperService db, IObjectStorageService storage)
    {
        _currentUser = currentUser;
        _db = db;
        _storage = storage;
    }

    public async Task<IReadOnlyList<StorageFileDto>> ListAsync(CancellationToken cancellationToken)
    {
        var files = await _db.QuerySelect<StorageFile>()
            .From<StorageFile>()
            .SelectAllFrom<StorageFile>()
            .Where(file => file.UserId == _currentUser.UserId)
            .ToListAsync();
        return files.OrderByDescending(file => file.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<StorageFileDto> UploadAsync(string? storedFileName, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length <= 0 || file.Length > MaxStorageFileBytes)
        {
            throw new ArgumentException("Storage room files must be between 1 byte and 25 MB.");
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new StorageFile
        {
            UserId = _currentUser.UserId,
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFileName = string.IsNullOrWhiteSpace(storedFileName) ? Path.GetFileName(file.FileName) : Path.GetFileName(storedFileName.Trim()),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
            Purpose = "import",
            CreatedAt = now,
            UpdatedAt = now
        };
        entity.S3ObjectKey = $"storage/files/{_currentUser.UserId:D}/{entity.Id:D}/{entity.StoredFileName}";

        await using (var stream = file.OpenReadStream())
        {
            await _storage.PutAsync(entity.S3ObjectKey, stream, entity.ContentType, cancellationToken);
        }

        await _db.SaveAsync(entity, auditUserId: _currentUser.UserId.ToString());
        return ToDto(entity);
    }

    internal async Task<StorageFile?> FindByNameAsync(string fileName, CancellationToken cancellationToken)
    {
        var files = await _db.QuerySelect<StorageFile>()
            .From<StorageFile>()
            .SelectAllFrom<StorageFile>()
            .Where(file => file.UserId == _currentUser.UserId)
            .ToListAsync();
        return files
            .Where(file => string.Equals(file.StoredFileName, fileName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.CreatedAt)
            .FirstOrDefault();
    }

    private static StorageFileDto ToDto(StorageFile file)
    {
        return new StorageFileDto(
            file.Id,
            file.OriginalFileName,
            file.StoredFileName,
            file.ContentType,
            file.S3ObjectKey,
            file.SizeBytes,
            file.Purpose,
            file.CreatedAt,
            file.UpdatedAt);
    }
}
