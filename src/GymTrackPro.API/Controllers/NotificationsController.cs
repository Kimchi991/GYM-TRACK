using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Authorization;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = Policies.BackOffice)]
public class NotificationsController : ControllerBase
{
    private readonly GymDbContext _context;

    public NotificationsController(GymDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Retrieve list of notifications. Optionally filter by member ID.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Notification>>> GetNotifications([FromQuery] int? memberId)
    {
        IQueryable<Notification> query = _context.Notifications.OrderByDescending(n => n.ScheduledTime);

        if (memberId.HasValue)
        {
            query = query.Where(n => n.MemberID == memberId.Value);
        }

        var result = await query.ToListAsync();
        return Ok(result);
    }

    /// <summary>
    /// Mark a notification as read.
    /// </summary>
    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var notification = await _context.Notifications.FindAsync(id);
        if (notification == null)
        {
            return NotFound();
        }

        notification.Status = NotificationStatus.Read;
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
