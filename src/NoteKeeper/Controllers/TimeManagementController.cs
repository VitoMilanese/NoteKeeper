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
            .Select(x => new TimeTrackedNoteViewModel
            {
                Id = x.Id,
                Title = x.Title,
                EstimatedTime = JiraDuration.Format(x.EstimatedTimeMinutes),
                SpentTime = JiraDuration.Format(x.SpentTimeMinutes ?? 0),
                RemainingTime = x.EstimatedTimeMinutes.HasValue
                    ? JiraDuration.Format(
                        Math.Max(
                            0,
                            x.EstimatedTimeMinutes.Value -
                            (x.SpentTimeMinutes ?? 0)))
                    : string.Empty
            })
            .ToList();

        var noteOptions = notes
            .Select(x => new TimeNoteOptionViewModel
            {
                Id = x.Id,
                Title = x.Title
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
            Days = days.Select(day => new TimeManagementDayViewModel
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
            }).ToList()
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

        if (!existingId.HasValue)
        {
            db.TimeManagementDays.Add(new TimeManagementDay
            {
                ProjectId = projectId,
                Date = normalizedDate
            });

            await TouchProjectAsync(projectId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return RedirectToMonth(projectId, normalizedDate);
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

        return RedirectToMonth(
            projectId,
            ParseMonth(month ?? MonthKey(day.Date)));
    }

    [HttpPost("/projects/{projectId:int}/time/days/{dayId:int}/entries/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEntry(
        int projectId,
        int dayId,
        int noteId,
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

        var note = await db.Notes
            .FirstOrDefaultAsync(
                x => x.Id == noteId && x.ProjectId == projectId,
                cancellationToken);

        if (note is null)
        {
            SetError("Time_TaskRequired");
            return RedirectToMonth(projectId, ParseMonth(month), dayId);
        }

        if (!TryParseTimeSpent(timeSpent, out var minutes))
        {
            SetError("Time_InvalidTimeSpent");
            return RedirectToMonth(projectId, ParseMonth(month), dayId);
        }

        db.TimeManagementEntries.Add(new TimeManagementEntry
        {
            TimeManagementDayId = day.Id,
            NoteId = note.Id,
            TaskTitle = note.Title,
            TimeSpentMinutes = minutes,
            Comment = Limit(comment, 2000)
        });

        await db.SaveChangesAsync(cancellationToken);
        await RecalculateNoteSpentAsync([note.Id], cancellationToken);
        await TouchProjectAsync(projectId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return RedirectToMonth(projectId, day.Date, dayId);
    }

    [HttpPost("/projects/{projectId:int}/time/entries/{entryId:int}/update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateEntry(
        int projectId,
        int entryId,
        int noteId,
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

        var note = await db.Notes
            .FirstOrDefaultAsync(
                x => x.Id == noteId && x.ProjectId == projectId,
                cancellationToken);

        if (note is null)
        {
            SetError("Time_TaskRequired");
            return RedirectToMonth(
                projectId,
                ParseMonth(month),
                entry.TimeManagementDayId);
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

        entry.NoteId = note.Id;
        entry.TaskTitle = note.Title;
        entry.TimeSpentMinutes = minutes;
        entry.Comment = Limit(comment, 2000);

        await db.SaveChangesAsync(cancellationToken);

        var affected = new[] { oldNoteId, note.Id }
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

        return RedirectToMonth(projectId, ParseMonth(month));
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

        if (!JiraDuration.TryParse(value, out var parsed) ||
            !parsed.HasValue ||
            parsed.Value <= 0)
        {
            return false;
        }

        minutes = parsed.Value;
        return true;
    }

    private void SetError(string resourceKey)
    {
        TempData["TimeError"] = localizer[resourceKey].Value;
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
