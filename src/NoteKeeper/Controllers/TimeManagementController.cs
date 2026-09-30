using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NoteKeeper.Data;
using NoteKeeper.Models;
using NoteKeeper.Services;
using NoteKeeper.ViewModels;

namespace NoteKeeper.Controllers;

public sealed class TimeManagementController(
    AppDbContext db,
    IStringLocalizer<AppResources> localizer) : Controller
{
    [HttpGet("/projects/{projectId:int}/time")]
    public async Task<IActionResult> Index(
        int projectId,
        string? month,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var monthStart = ParseMonth(month);
        var monthEnd = monthStart.AddMonths(1);

        var notes = await db.Notes
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.Title)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.EstimatedTimeMinutes,
                x.SpentTimeMinutes,
                x.IsTimeManagementPinned
            })
            .ToListAsync(cancellationToken);

        var trackedNotes = notes
            .Where(x => x.IsTimeManagementPinned)
            .Select(x =>
            {
                var spentMinutes = x.SpentTimeMinutes ?? 0;
                var remainingMinutes = x.EstimatedTimeMinutes.HasValue
                    ? x.EstimatedTimeMinutes.Value - spentMinutes
                    : (int?)null;
                var isOvertime = remainingMinutes < 0;

                return new TimeTrackedNoteViewModel
                {
                    Id = x.Id,
                    Title = x.Title,
                    EstimatedTime = JiraDuration.Format(
                        x.EstimatedTimeMinutes),
                    SpentTime = JiraDuration.Format(spentMinutes),
                    RemainingTime = remainingMinutes.HasValue
                        ? isOvertime
                            ? $"+{JiraDuration.Format(-remainingMinutes.Value)}"
                            : JiraDuration.Format(remainingMinutes.Value)
                        : string.Empty,
                    IsOvertime = isOvertime
                };
            })
            .ToList();

        var noteOptions = notes
            .OrderByDescending(x => x.IsTimeManagementPinned)
            .ThenBy(x => x.Title)
            .ThenBy(x => x.Id)
            .Select(x => new TimeNoteOptionViewModel
            {
                Id = x.Id,
                Title = x.Title,
                IsPinned = x.IsTimeManagementPinned
            })
            .ToList();

        var availableNotes = notes
            .Where(x => !x.IsTimeManagementPinned)
            .Select(x => new TimeNoteOptionViewModel
            {
                Id = x.Id,
                Title = x.Title
            })
            .ToList();

        var days = await db.TimeManagementDays
            .AsNoTracking()
            .Where(day =>
                day.ProjectId == projectId &&
                day.Date >= monthStart &&
                day.Date < monthEnd)
            .Include(day => day.Entries)
                .ThenInclude(entry => entry.Note)
            .AsSplitQuery()
            .OrderByDescending(day => day.Date)
            .ThenByDescending(day => day.Id)
            .ToListAsync(cancellationToken);

        var culture = CultureInfo.CurrentUICulture;
        var currentMonth = new DateTime(
            DateTime.Today.Year,
            DateTime.Today.Month,
            1);

        var model = new TimeManagementViewModel
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            MonthStart = monthStart,
            MonthKey = MonthKey(monthStart),
            MonthLabel = monthStart.ToString("MMMM yyyy", culture),
            PreviousMonthKey = MonthKey(monthStart.AddMonths(-1)),
            PreviousMonthLabel = monthStart.AddMonths(-1).ToString("MMMM yyyy", culture),
            NextMonthKey = MonthKey(monthStart.AddMonths(1)),
            NextMonthLabel = monthStart.AddMonths(1).ToString("MMMM yyyy", culture),
            CurrentMonthKey = MonthKey(currentMonth),
            DefaultNewDayDate =
                monthStart.Year == DateTime.Today.Year &&
                monthStart.Month == DateTime.Today.Month
                    ? DateTime.Today
                    : monthStart,
            ErrorMessage = TempData["TimeError"] as string ?? string.Empty,
            TrackedNotes = trackedNotes,
            NoteOptions = noteOptions,
            AvailableNotes = availableNotes,
            Days = days.Select(MapDay).ToList()
        };

        return View(model);
    }

    [HttpPost("/projects/{projectId:int}/time/days/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDay(
        int projectId,
        string? date,
        string? month,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects
            .AsNoTracking()
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
        {
            return NotFound();
        }

        if (!TryParseCalendarDate(date, out var normalizedDate))
        {
            if (IsAjaxRequest())
            {
                return BadRequest(localizer["Time_InvalidDate"].Value);
            }

            SetError("Time_InvalidDate");
            return RedirectToMonth(projectId, ParseMonth(month));
        }

        var existingId = await db.TimeManagementDays
            .AsNoTracking()
            .Where(day =>
                day.ProjectId == projectId &&
                day.Date == normalizedDate)
            .Select(day => (int?)day.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var dayId = existingId;

        if (!dayId.HasValue)
        {
            var newDay = new TimeManagementDay
            {
                ProjectId = projectId,
                Date = normalizedDate
            };

            db.TimeManagementDays.Add(newDay);
            await TouchProjectAsync(projectId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            dayId = newDay.Id;
        }

        if (IsAjaxRequest())
        {
            var day = await db.TimeManagementDays
                .AsNoTracking()
                .Include(x => x.Entries)
                    .ThenInclude(entry => entry.Note)
                .AsSplitQuery()
                .FirstAsync(
                    x => x.Id == dayId.Value,
                    cancellationToken);

            return PartialView(
                "_TimeDay",
                new TimeDayPartialViewModel
                {
                    ProjectId = projectId,
                    MonthKey = month ?? MonthKey(normalizedDate),
                    Day = MapDay(day)
                });
        }

        return RedirectToMonth(projectId, normalizedDate);
    }

    [HttpPost("/projects/{projectId:int}/time/days/{dayId:int}/date")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDayDate(
        int projectId,
        int dayId,
        string? date,
        string? month,
        CancellationToken cancellationToken)
    {
        var day = await db.TimeManagementDays
            .FirstOrDefaultAsync(
                x => x.Id == dayId && x.ProjectId == projectId,
                cancellationToken);

        if (day is null)
        {
            return NotFound();
        }

        if (!TryParseCalendarDate(date, out var normalizedDate))
        {
            if (IsAjaxRequest())
            {
                return BadRequest(localizer["Time_InvalidDate"].Value);
            }

            SetError("Time_InvalidDate");
            return RedirectToMonth(
                projectId,
                ParseMonth(month),
                dayId);
        }

        var duplicateExists = await db.TimeManagementDays
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.ProjectId == projectId &&
                    x.Id != dayId &&
                    x.Date == normalizedDate,
                cancellationToken);

        if (duplicateExists)
        {
            if (IsAjaxRequest())
            {
                return Conflict(
                    localizer["Time_DateAlreadyExists"].Value);
            }

            SetError("Time_DateAlreadyExists");
            return RedirectToMonth(
                projectId,
                ParseMonth(month),
                dayId);
        }

        day.Date = normalizedDate;
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            var updatedDay = await db.TimeManagementDays
                .AsNoTracking()
                .Include(x => x.Entries)
                    .ThenInclude(entry => entry.Note)
                .AsSplitQuery()
                .FirstAsync(x => x.Id == dayId, cancellationToken);

            Response.Headers["X-Time-Day-Month"] =
                MonthKey(normalizedDate);

            return PartialView(
                "_TimeDay",
                new TimeDayPartialViewModel
                {
                    ProjectId = projectId,
                    MonthKey = MonthKey(normalizedDate),
                    Day = MapDay(updatedDay)
                });
        }

        return RedirectToMonth(
            projectId,
            normalizedDate,
            dayId);
    }

    [HttpPost("/projects/{projectId:int}/time/days/{dayId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDay(
        int projectId,
        int dayId,
        string? month,
        CancellationToken cancellationToken)
    {
        var day = await db.TimeManagementDays
            .Include(x => x.Entries)
            .FirstOrDefaultAsync(
                x => x.Id == dayId && x.ProjectId == projectId,
                cancellationToken);

        if (day is null)
        {
            return NotFound();
        }

        var affectedNoteIds = day.Entries
            .Where(x => x.NoteId.HasValue)
            .Select(x => x.NoteId!.Value)
            .Distinct()
            .ToArray();

        db.TimeManagementDays.Remove(day);
        await db.SaveChangesAsync(cancellationToken);

        await RecalculateNoteSpentAsync(
            affectedNoteIds,
            cancellationToken);
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            var affectedNotes = new List<Note>();

            if (affectedNoteIds.Length > 0)
            {
                affectedNotes = await db.Notes
                    .AsNoTracking()
                    .Where(x => affectedNoteIds.Contains(x.Id))
                    .ToListAsync(cancellationToken);
            }

            return Ok(new
            {
                noteSummaries = affectedNotes.Select(note => new
                {
                    id = note.Id,
                    spentTime = JiraDuration.Format(
                        note.SpentTimeMinutes ?? 0),
                    remainingTime = GetRemainingTime(
                        note.EstimatedTimeMinutes,
                        note.SpentTimeMinutes),
                    isOvertime = IsOvertime(
                        note.EstimatedTimeMinutes,
                        note.SpentTimeMinutes)
                })
            });
        }

        return RedirectToMonth(
            projectId,
            ParseMonth(month ?? MonthKey(day.Date)));
    }

    [HttpPost("/projects/{projectId:int}/time/days/{dayId:int}/entries/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEntry(
        int projectId,
        int dayId,
        int? noteId,
        string? taskTitle,
        string? timeSpent,
        string? comment,
        string? month,
        CancellationToken cancellationToken)
    {
        var day = await db.TimeManagementDays
            .FirstOrDefaultAsync(
                x => x.Id == dayId && x.ProjectId == projectId,
                cancellationToken);

        if (day is null)
        {
            return NotFound();
        }

        Note? note = null;
        if (noteId is > 0)
        {
            note = await db.Notes
                .FirstOrDefaultAsync(
                    x =>
                        x.Id == noteId.Value &&
                        x.ProjectId == projectId,
                    cancellationToken);
        }

        if (!TryParseTimeSpent(timeSpent, out var minutes))
        {
            SetError("Time_InvalidTimeSpent");
            return RedirectToMonth(projectId, ParseMonth(month), dayId);
        }

        db.TimeManagementEntries.Add(new TimeManagementEntry
        {
            TimeManagementDayId = day.Id,
            NoteId = note?.Id,
            TaskTitle = note?.Title ?? Limit(taskTitle, 240) ?? string.Empty,
            TimeSpentMinutes = minutes,
            Comment = Limit(comment, 2000)
        });

        await db.SaveChangesAsync(cancellationToken);

        if (note is not null)
        {
            await RecalculateNoteSpentAsync([note.Id], cancellationToken);
        }
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return RedirectToMonth(projectId, day.Date, dayId);
    }

    [HttpPost("/projects/{projectId:int}/time/entries/{entryId:int}/update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateEntry(
        int projectId,
        int entryId,
        int? noteId,
        string? taskTitle,
        string? timeSpent,
        string? comment,
        string? month,
        CancellationToken cancellationToken)
    {
        var entry = await db.TimeManagementEntries
            .Include(x => x.Day)
            .FirstOrDefaultAsync(
                x =>
                    x.Id == entryId &&
                    x.Day.ProjectId == projectId,
                cancellationToken);

        if (entry is null)
        {
            return NotFound();
        }

        Note? note = null;
        if (noteId is > 0)
        {
            note = await db.Notes
                .FirstOrDefaultAsync(
                    x =>
                        x.Id == noteId.Value &&
                        x.ProjectId == projectId,
                    cancellationToken);
        }

        if (!TryParseTimeSpent(timeSpent, out var minutes))
        {
            SetError("Time_InvalidTimeSpent");
            return RedirectToMonth(
                projectId,
                ParseMonth(month),
                entry.TimeManagementDayId);
        }

        var oldNoteId = entry.NoteId;

        entry.NoteId = note?.Id;
        entry.TaskTitle = note?.Title ?? Limit(taskTitle, 240) ?? string.Empty;
        entry.TimeSpentMinutes = minutes;
        entry.Comment = Limit(comment, 2000);

        await db.SaveChangesAsync(cancellationToken);

        var affected = new[] { oldNoteId, note?.Id }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        await RecalculateNoteSpentAsync(affected, cancellationToken);
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return RedirectToMonth(
            projectId,
            entry.Day.Date,
            entry.TimeManagementDayId);
    }

    [HttpPost("/projects/{projectId:int}/time/entries/{entryId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEntry(
        int projectId,
        int entryId,
        string? month,
        CancellationToken cancellationToken)
    {
        var entry = await db.TimeManagementEntries
            .Include(x => x.Day)
            .FirstOrDefaultAsync(
                x =>
                    x.Id == entryId &&
                    x.Day.ProjectId == projectId,
                cancellationToken);

        if (entry is null)
        {
            return NotFound();
        }

        var dayId = entry.TimeManagementDayId;
        var date = entry.Day.Date;
        var noteId = entry.NoteId;

        db.TimeManagementEntries.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);

        if (noteId.HasValue)
        {
            await RecalculateNoteSpentAsync([noteId.Value], cancellationToken);
        }

        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            var spentTimeMinutes = noteId.HasValue
                ? await db.Notes
                    .AsNoTracking()
                    .Where(x => x.Id == noteId.Value)
                    .Select(x => x.SpentTimeMinutes)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            return Ok(new
            {
                deletedEntryId = entryId,
                spentTime = JiraDuration.Format(spentTimeMinutes ?? 0)
            });
        }

        return RedirectToMonth(projectId, date, dayId);
    }

    [HttpPost("/projects/{projectId:int}/time/notes/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTrackedNote(
        int projectId,
        int? noteId,
        string? noteTitle,
        string? month,
        CancellationToken cancellationToken)
    {
        Note? note = null;

        if (noteId is > 0)
        {
            note = await db.Notes
                .FirstOrDefaultAsync(
                    x =>
                        x.Id == noteId.Value &&
                        x.ProjectId == projectId,
                    cancellationToken);
        }

        if (note is null && !string.IsNullOrWhiteSpace(noteTitle))
        {
            var candidates = await db.Notes
                .Where(x => x.ProjectId == projectId)
                .OrderBy(x => x.Id)
                .ToListAsync(cancellationToken);

            note = candidates.FirstOrDefault(x =>
                string.Equals(
                    x.Title,
                    noteTitle.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }

        if (note is null)
        {
            SetError("Time_NoteSearchNotFound");
            return RedirectToMonth(projectId, ParseMonth(month));
        }

        note.IsTimeManagementPinned = true;
        await db.SaveChangesAsync(cancellationToken);

        return RedirectToMonth(projectId, ParseMonth(month));
    }

    [HttpPost("/projects/{projectId:int}/time/notes/{noteId:int}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTrackedNote(
        int projectId,
        int noteId,
        string? month,
        CancellationToken cancellationToken)
    {
        var note = await db.Notes
            .FirstOrDefaultAsync(
                x => x.Id == noteId && x.ProjectId == projectId,
                cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        note.IsTimeManagementPinned = false;
        await db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            return Ok(new
            {
                removedNoteId = note.Id
            });
        }

        return RedirectToMonth(projectId, ParseMonth(month));
    }

    [HttpPost("/projects/{projectId:int}/time/notes/remove-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAllTrackedNotes(
        int projectId,
        string? month,
        CancellationToken cancellationToken)
    {
        var notes = await db.Notes
            .Where(x =>
                x.ProjectId == projectId &&
                x.IsTimeManagementPinned)
            .ToListAsync(cancellationToken);

        var removedNoteIds = notes
            .Select(x => x.Id)
            .ToArray();

        foreach (var note in notes)
        {
            note.IsTimeManagementPinned = false;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            return Ok(new
            {
                removedNoteIds
            });
        }

        return RedirectToMonth(projectId, ParseMonth(month));
    }

    [HttpPost("/projects/{projectId:int}/time/notes/{noteId:int}/estimated")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateEstimatedTime(
        int projectId,
        int noteId,
        string? estimatedTime,
        string? month,
        CancellationToken cancellationToken)
    {
        var note = await db.Notes
            .FirstOrDefaultAsync(
                x => x.Id == noteId && x.ProjectId == projectId,
                cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        if (!JiraDuration.TryParse(estimatedTime, out var estimatedMinutes))
        {
            if (IsAjaxRequest())
            {
                return BadRequest(
                    localizer["Server_InvalidEstimatedTime"].Value);
            }

            SetError("Server_InvalidEstimatedTime");
            return RedirectToMonth(projectId, ParseMonth(month));
        }

        note.EstimatedTimeMinutes = estimatedMinutes;
        note.UpdatedAtUtc = DateTime.UtcNow;

        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            return Ok(new
            {
                estimatedTime = JiraDuration.Format(
                    note.EstimatedTimeMinutes),
                remainingTime = GetRemainingTime(
                    note.EstimatedTimeMinutes,
                    note.SpentTimeMinutes),
                isOvertime = IsOvertime(
                    note.EstimatedTimeMinutes,
                    note.SpentTimeMinutes)
            });
        }

        return RedirectToMonth(projectId, ParseMonth(month));
    }

    [HttpGet("/notes/{noteId:int}/time-entries")]
    public async Task<IActionResult> GetNoteTimeEntries(
        int noteId,
        CancellationToken cancellationToken)
    {
        var note = await db.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == noteId, cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        var entries = await db.TimeManagementEntries
            .AsNoTracking()
            .Where(x => x.NoteId == noteId)
            .Include(x => x.Day)
            .OrderByDescending(x => x.Day.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            entries = entries.Select(entry => new
            {
                id = entry.Id,
                dayId = entry.TimeManagementDayId,
                date = entry.Day.Date.ToString(
                    "dd/MM/yy",
                    CultureInfo.InvariantCulture),
                month = MonthKey(entry.Day.Date),
                timeSpent = JiraDuration.Format(
                    entry.TimeSpentMinutes),
                comment = entry.Comment ?? string.Empty
            })
        });
    }

    [HttpPost("/notes/{noteId:int}/time-pin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetNotePin(
        int noteId,
        [FromBody] TimePinRequest request,
        CancellationToken cancellationToken)
    {
        var note = await db.Notes
            .FirstOrDefaultAsync(x => x.Id == noteId, cancellationToken);

        if (note is null)
        {
            return NotFound(new
            {
                message = localizer["Server_NoteNotFound"].Value
            });
        }

        note.IsTimeManagementPinned = request.IsPinned;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            isPinned = note.IsTimeManagementPinned
        });
    }

    private static string GetRemainingTime(
        int? estimatedTimeMinutes,
        int? spentTimeMinutes)
    {
        if (!estimatedTimeMinutes.HasValue)
        {
            return string.Empty;
        }

        var remaining =
            estimatedTimeMinutes.Value -
            (spentTimeMinutes ?? 0);

        return remaining < 0
            ? $"+{JiraDuration.Format(-remaining)}"
            : JiraDuration.Format(remaining);
    }

    private static bool IsOvertime(
        int? estimatedTimeMinutes,
        int? spentTimeMinutes)
    {
        return estimatedTimeMinutes.HasValue &&
            (spentTimeMinutes ?? 0) > estimatedTimeMinutes.Value;
    }

    private async Task RecalculateNoteSpentAsync(
        IEnumerable<int> noteIds,
        CancellationToken cancellationToken)
    {
        foreach (var noteId in noteIds.Distinct())
        {
            var note = await db.Notes
                .FirstOrDefaultAsync(x => x.Id == noteId, cancellationToken);

            if (note is null)
            {
                continue;
            }

            var total = await db.TimeManagementEntries
                .Where(x => x.NoteId == noteId)
                .SumAsync(
                    x => (int?)x.TimeSpentMinutes,
                    cancellationToken);

            note.SpentTimeMinutes = total;
        }
    }

    private async Task TouchProjectAsync(
        int projectId,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);

        if (project is not null)
        {
            project.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private bool TryParseTimeSpent(
        string? value,
        out int minutes)
    {
        minutes = 0;

        if (!JiraDuration.TryParse(value, out var parsed))
        {
            return false;
        }

        minutes = parsed ?? 0;
        return minutes >= 0;
    }

    private void SetError(string resourceKey)
    {
        TempData["TimeError"] = localizer[resourceKey].Value;
    }

    private static TimeManagementDayViewModel MapDay(
        TimeManagementDay day)
    {
        return new TimeManagementDayViewModel
        {
            Id = day.Id,
            Date = day.Date,
            TotalTimeSpent = JiraDuration.Format(
                day.Entries.Sum(entry => entry.TimeSpentMinutes)),
            Entries = day.Entries
                .OrderBy(entry => entry.Id)
                .Select(entry => new TimeManagementEntryViewModel
                {
                    Id = entry.Id,
                    NoteId = entry.NoteId,
                    TaskTitle = entry.Note?.Title ?? entry.TaskTitle,
                    TimeSpent = JiraDuration.Format(entry.TimeSpentMinutes),
                    Comment = entry.Comment ?? string.Empty
                })
                .ToList()
        };
    }

    private bool IsAjaxRequest()
    {
        return string.Equals(
            Request.Headers["X-Requested-With"].ToString(),
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseCalendarDate(
        string? value,
        out DateTime date)
    {
        if (!DateTime.TryParseExact(
                value?.Trim(),
                "dd/MM/yy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            return false;
        }

        if (date.Year < 2000)
        {
            date = date.AddYears(100);
        }

        return true;
    }

    private static DateTime ParseMonth(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTime.TryParseExact(
                value,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return new DateTime(parsed.Year, parsed.Month, 1);
        }

        var today = DateTime.Today;
        return new DateTime(today.Year, today.Month, 1);
    }

    private static string MonthKey(DateTime value) =>
        value.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private RedirectResult RedirectToMonth(
        int projectId,
        DateTime monthDate,
        int? dayId = null)
    {
        var anchor = dayId.HasValue
            ? $"#time-day-{dayId.Value}"
            : string.Empty;

        return Redirect(
            $"/projects/{projectId}/time?month={MonthKey(monthDate)}{anchor}");
    }

    private static string? Limit(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}
