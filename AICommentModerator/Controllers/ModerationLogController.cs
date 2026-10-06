using AICommentModerator.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace AICommentModerator.Controllers;

[ApiController]
[Route("api/moderation")]
public class ModerationLogController : ControllerBase
{
    private readonly IAuditLog _auditLog;

    public ModerationLogController(IAuditLog auditLog) => _auditLog = auditLog;

    /// <summary>The most recent decisions, newest first.</summary>
    [HttpGet("recent")]
    public async Task<ActionResult<IReadOnlyList<ModerationRecord>>> Recent(
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        return Ok(await _auditLog.RecentAsync(take, cancellationToken));
    }
}
