using FinanceTracker.Api.Features.Storage;
using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/storage-files")]
public sealed class StorageFilesController : ControllerBase
{
    private readonly StorageFileService _storageFiles;

    public StorageFilesController(StorageFileService storageFiles)
    {
        _storageFiles = storageFiles;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StorageFileDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _storageFiles.ListAsync(cancellationToken));
    }

    [HttpPost]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<ActionResult<StorageFileDto>> Upload(
        [FromForm] string? storedFileName,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            var stored = await _storageFiles.UploadAsync(storedFileName, file, cancellationToken);
            return CreatedAtAction(nameof(List), new { id = stored.Id }, stored);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
