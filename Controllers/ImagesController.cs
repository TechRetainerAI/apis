using MeDan.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeDan.Api.Controllers;

/// <summary>
/// Serves images stored in the database (hostel photos, avatars, posters) —
/// see <see cref="Models.StoredImage"/>. Private rows (payment proofs) are
/// refused here; those stream only through the authorized proof endpoint.
/// </summary>
[ApiController]
[Route("api/images")]
[AllowAnonymous]
public class ImagesController : ControllerBase
{
    private readonly AppDbContext _db;

    public ImagesController(AppDbContext db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var image = await _db.StoredImages.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.IsPublic, ct);
        if (image is null) return NotFound();

        // A stored image never changes under its id — replacements get a new
        // row — so browsers and the CDN may cache it for as long as they like.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(image.Data, image.ContentType);
    }
}
