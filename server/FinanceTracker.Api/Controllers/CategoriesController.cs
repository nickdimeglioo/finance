using FinanceTracker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/categories")]
public sealed class CategoriesController : ControllerBase
{
    private readonly CategoryService _categories;

    public CategoriesController(CategoryService categories)
    {
        _categories = categories;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List(CancellationToken cancellationToken)
        => Ok(await _categories.ListAsync(cancellationToken));

    [HttpPatch("rename")]
    public async Task<ActionResult<object>> Rename(RenameCategoryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _categories.RenameAsync(request, cancellationToken);
            return Ok(new { updated });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

