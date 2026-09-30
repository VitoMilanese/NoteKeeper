using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NoteKeeper.Data;
using NoteKeeper.Models;
using NoteKeeper.Services;
using NoteKeeper.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace NoteKeeper.Controllers;

public sealed class ProjectsController(
    AppDbContext db,
    IWebHostEnvironment environment,
    IStringLocalizer<AppResources> localizer) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index(
        string? q,
        string sort = "updated",
        string dir = "desc",
        CancellationToken cancellationToken = default)
    {
        q = q?.Trim();
        sort = sort is "name" or "created" or "updated" or "notes"
            ? sort
            : "updated";
        dir = dir == "asc" ? "asc" : "desc";

        var query = db.Projects
            .AsNoTracking()
            .Select(project => new ProjectListItemViewModel
            {
                Id = project.Id,
                Name = project.Name,
                NoteCount = project.Notes.Count,
                CreatedAtUtc = project.CreatedAtUtc,
                UpdatedAtUtc = project.UpdatedAtUtc
            });

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q}%";
            query = query.Where(project =>
                EF.Functions.Like(project.Name, pattern));
        }

        query = (sort, dir) switch
        {
            ("name", "asc") => query
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Id),
            ("name", _) => query
                .OrderByDescending(x => x.Name)
                .ThenByDescending(x => x.Id),
            ("created", "asc") => query
                .OrderBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id),
            ("created", _) => query
                .OrderByDescending(x => x.CreatedAtUtc)
                .ThenByDescending(x => x.Id),
            ("notes", "asc") => query
                .OrderBy(x => x.NoteCount)
                .ThenBy(x => x.Name),
            ("notes", _) => query
                .OrderByDescending(x => x.NoteCount)
                .ThenBy(x => x.Name),
            ("updated", "asc") => query
                .OrderBy(x => x.UpdatedAtUtc)
                .ThenBy(x => x.Id),
            _ => query
                .OrderByDescending(x => x.UpdatedAtUtc)
                .ThenByDescending(x => x.Id)
        };

        var projects = await query.ToListAsync(cancellationToken);

        return View(new ProjectDashboardViewModel
        {
            Query = q ?? string.Empty,
            Sort = sort,
            Direction = dir,
            TotalCount = projects.Count,
            Projects = projects
        });
    }

    [HttpPost("/projects/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string? name,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Name = NormalizeProjectName(name),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);

        return Redirect($"/projects/{project.Id}");
    }

    [HttpPost("/projects/{id:int}/rename")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(
        int id,
        string? name,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        project.Name = NormalizeProjectName(name);
        project.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Redirect("/");
    }

    [HttpPost("/projects/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .Include(x => x.Notes)
                .ThenInclude(note => note.Blocks)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (project is null)
        {
            return Redirect("/");
        }

        var imagePaths = project.Notes
            .SelectMany(note => note.Blocks)
            .Where(block =>
                block.Type == BlockType.Image &&
                !string.IsNullOrWhiteSpace(block.ImagePath))
            .Select(block => block.ImagePath!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (project.Notes.Count > 0)
        {
            db.Notes.RemoveRange(project.Notes);
        }

        db.Projects.Remove(project);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var imagePath in imagePaths)
        {
            var stillUsed = await db.NoteBlocks
                .AsNoTracking()
                .AnyAsync(
                    block => block.ImagePath == imagePath,
                    cancellationToken);

            if (!stillUsed)
            {
                DeleteStoredImageFile(imagePath);
            }
        }

        return Redirect("/");
    }

    [HttpGet("/projects/{id:int}/export")]
    public async Task<IActionResult> Export(
        int id,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Include(x => x.Notes)
                .ThenInclude(note => note.Blocks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var document = new ProjectTransferDocument
        {
            Name = project.Name,
            CreatedAtUtc = project.CreatedAtUtc,
            UpdatedAtUtc = project.UpdatedAtUtc,
            Notes = []
        };

        foreach (var note in project.Notes
                     .OrderBy(x => x.CreatedAtUtc)
                     .ThenBy(x => x.Id))
        {
            var transferNote = new ProjectTransferNote
            {
                Title = note.Title,
                Status = note.Status,
                EstimatedTimeMinutes = note.EstimatedTimeMinutes,
                SpentTimeMinutes = note.SpentTimeMinutes,
                CreatedAtUtc = note.CreatedAtUtc,
                UpdatedAtUtc = note.UpdatedAtUtc,
                Blocks = []
            };

            foreach (var block in note.Blocks.OrderBy(x => x.SortOrder))
            {
                var transferBlock = new NoteTransferBlock
                {
                    Type = block.Type,
                    TextContent = block.TextContent,
                    Url = block.Url,
                    LinkTitle = block.LinkTitle,
                    Caption = block.Caption
                };

                if (block.Type == BlockType.Image &&
                    !string.IsNullOrWhiteSpace(block.ImagePath))
                {
                    var fileName = Path.GetFileName(block.ImagePath);
                    var physicalPath = Path.Combine(
                        environment.WebRootPath,
                        "uploads",
                        fileName);

                    if (System.IO.File.Exists(physicalPath))
                    {
                        transferBlock.ImageMimeType =
                            GetImageMimeType(block.ImagePath);
                        transferBlock.ImageBase64 = Convert.ToBase64String(
                            await System.IO.File.ReadAllBytesAsync(
                                physicalPath,
                                cancellationToken));
                    }
                }

                transferNote.Blocks.Add(transferBlock);
            }

            document.Notes.Add(transferNote);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(
            document,
            CreateTransferJsonOptions());
        var downloadName =
            $"{SanitizeFileName(project.Name)}.notekeeper-project.json";

        return File(
            json,
            "application/json; charset=utf-8",
            downloadName);
    }

    [HttpPost("/projects/import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(500_000_000)]
    public async Task<IActionResult> Import(
        IFormFile? projectFile,
        CancellationToken cancellationToken)
    {
        if (projectFile is null ||
            projectFile.Length == 0 ||
            projectFile.Length > 450_000_000)
        {
            return BadRequest(
                localizer["Server_ProjectImportTooLarge"].Value);
        }

        var storedImagePaths = new List<string>();

        try
        {
            await using var input = projectFile.OpenReadStream();
            var document =
                await JsonSerializer.DeserializeAsync<ProjectTransferDocument>(
                    input,
                    CreateTransferJsonOptions(),
                    cancellationToken);

            if (document is null ||
                document.Notes is null ||
                !string.Equals(
                    document.Format,
                    ProjectTransferDocument.ExpectedFormat,
                    StringComparison.Ordinal) ||
                document.Version != ProjectTransferDocument.CurrentVersion ||
                document.Notes.Count > 5000 ||
                document.Notes.Any(note =>
                    note.Blocks is null ||
                    note.Blocks.Count > 500))
            {
                return BadRequest(
                    localizer["Server_ProjectImportInvalid"].Value);
            }

            var now = DateTime.UtcNow;
            var project = new Project
            {
                Name = NormalizeProjectName(document.Name),
                CreatedAtUtc = document.CreatedAtUtc ?? now,
                UpdatedAtUtc = document.UpdatedAtUtc ?? now,
                Notes = []
            };

            foreach (var transferNote in document.Notes)
            {
                var note = new Note
                {
                    Project = project,
                    Title = NormalizeNoteTitle(transferNote.Title),
                    Status = NormalizeStatus(transferNote.Status),
                    EstimatedTimeMinutes = JiraDuration.NormalizeMinutes(
                        transferNote.EstimatedTimeMinutes),
                    SpentTimeMinutes = JiraDuration.NormalizeMinutes(
                        transferNote.SpentTimeMinutes),
                    CreatedAtUtc = transferNote.CreatedAtUtc ?? now,
                    UpdatedAtUtc = transferNote.UpdatedAtUtc ?? now,
                    Blocks = []
                };

                for (var index = 0;
                     index < transferNote.Blocks.Count;
                     index++)
                {
                    var block = transferNote.Blocks[index];

                    switch (block.Type)
                    {
                        case BlockType.Text:
                            note.Blocks.Add(new NoteBlock
                            {
                                Type = BlockType.Text,
                                SortOrder = index,
                                TextContent =
                                    block.TextContent?.TrimEnd()
                            });
                            break;

                        case BlockType.Link:
                            note.Blocks.Add(new NoteBlock
                            {
                                Type = BlockType.Link,
                                SortOrder = index,
                                Url = NormalizeUrl(block.Url),
                                LinkTitle = Limit(
                                    block.LinkTitle?.Trim(),
                                    300),
                                TextContent =
                                    block.TextContent?.TrimEnd()
                            });
                            break;

                        case BlockType.Image when
                            !string.IsNullOrWhiteSpace(
                                block.ImageMimeType) &&
                            !string.IsNullOrWhiteSpace(
                                block.ImageBase64):
                        {
                            var bytes = Convert.FromBase64String(
                                block.ImageBase64);
                            var imagePath = await StoreImageBytesAsync(
                                bytes,
                                block.ImageMimeType,
                                cancellationToken);
                            storedImagePaths.Add(imagePath);

                            note.Blocks.Add(new NoteBlock
                            {
                                Type = BlockType.Image,
                                SortOrder = index,
                                ImagePath = imagePath,
                                Caption = Limit(
                                    block.Caption?.Trim(),
                                    500)
                            });
                            break;
                        }
                    }
                }

                var explicitTags = TagExtractor.Extract(
                    note.Blocks
                        .Where(block =>
                            block.Type is BlockType.Text or
                                BlockType.Link)
                        .Select(block =>
                            RichTextContent.ToTagSearchText(
                                block.TextContent)));
                var desiredTags = BuildDesiredTags(
                    note.Status,
                    explicitTags);

                note.Tags = desiredTags
                    .OrderBy(
                        tag => tag,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(tag => new NoteTag { Name = tag })
                    .ToList();

                project.Notes.Add(note);
            }

            if (project.Notes.Count > 0)
            {
                project.CreatedAtUtc = document.CreatedAtUtc ??
                    project.Notes.Min(note => note.CreatedAtUtc);
                project.UpdatedAtUtc = document.UpdatedAtUtc ??
                    project.Notes.Max(note => note.UpdatedAtUtc);
            }

            db.Projects.Add(project);
            await db.SaveChangesAsync(cancellationToken);

            return Redirect($"/projects/{project.Id}");
        }
        catch (Exception ex) when (
            ex is JsonException or
                FormatException or
                InvalidDataException)
        {
            foreach (var imagePath in storedImagePaths)
            {
                DeleteStoredImageFile(imagePath);
            }

            return BadRequest(
                localizer["Server_ProjectImportInvalid"].Value);
        }
        catch
        {
            foreach (var imagePath in storedImagePaths)
            {
                DeleteStoredImageFile(imagePath);
            }

            throw;
        }
    }

    private string NormalizeProjectName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return localizer["Server_UntitledProject"].Value;
        }

        return name.Length <= 180 ? name : name[..180];
    }

    private string NormalizeNoteTitle(string? value)
    {
        var title = (value ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return localizer["Server_UntitledNote"].Value;
        }

        return title.Length <= 240 ? title : title[..240];
    }

    private static NoteStatus NormalizeStatus(NoteStatus status)
    {
        return Enum.IsDefined(status)
            ? status
            : NoteStatus.Backlog;
    }

    private static HashSet<string> BuildDesiredTags(
        NoteStatus status,
        IEnumerable<string> explicitTags)
    {
        var result = explicitTags.ToHashSet(
            StringComparer.OrdinalIgnoreCase);

        foreach (var tag in NoteStatusTags.GetImplicitTags(status))
        {
            result.Add(tag);
        }

        return result;
    }

    private async Task<string> StoreImageBytesAsync(
        byte[] bytes,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (bytes.Length == 0 || bytes.Length > 12_000_000)
        {
            throw new InvalidDataException("Invalid image size.");
        }

        var originalExtension =
            GetExtensionForContentType(contentType);
        if (originalExtension is null)
        {
            throw new InvalidDataException(
                "Unsupported image type.");
        }

        Image<Rgba32> image;
        try
        {
            using var input =
                new MemoryStream(bytes, writable: false);
            image = Image.Load<Rgba32>(input);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "Invalid image data.",
                ex);
        }

        using (image)
        {
            var preserveOriginal =
                image.Frames.Count > 1 ||
                HasTransparency(image);
            var extension =
                preserveOriginal
                    ? originalExtension
                    : ".jpg";
            var fileName =
                $"{Guid.NewGuid():N}{extension}";
            var uploadsDirectory = Path.Combine(
                environment.WebRootPath,
                "uploads");
            Directory.CreateDirectory(uploadsDirectory);
            var physicalPath = Path.Combine(
                uploadsDirectory,
                fileName);

            if (preserveOriginal)
            {
                await System.IO.File.WriteAllBytesAsync(
                    physicalPath,
                    bytes,
                    cancellationToken);
            }
            else
            {
                image.SaveAsJpeg(
                    physicalPath,
                    new JpegEncoder { Quality = 90 });
            }

            return $"/uploads/{fileName}";
        }
    }

    private static bool HasTransparency(Image<Rgba32> image)
    {
        var hasTransparency = false;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0;
                 y < accessor.Height && !hasTransparency;
                 y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A < byte.MaxValue)
                    {
                        hasTransparency = true;
                        break;
                    }
                }
            }
        });

        return hasTransparency;
    }

    private static string? GetExtensionForContentType(
        string? contentType)
    {
        return contentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => null
        };
    }

    private static string GetImageMimeType(string imagePath)
    {
        return Path.GetExtension(imagePath)
            .ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
    }

    private static JsonSerializerOptions
        CreateTransferJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder =
                JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(
            new JsonStringEnumConverter());
        return options;
    }

    private static string? NormalizeUrl(string? value)
    {
        var url = value?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var parsed))
        {
            return null;
        }

        return parsed.Scheme is "http" or "https"
            ? parsed.ToString()
            : null;
    }

    private static string? Limit(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters =
            Path.GetInvalidFileNameChars();
        var safe = new string(
            value
                .Select(character =>
                    invalidCharacters.Contains(character)
                        ? '_'
                        : character)
                .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(safe)
            ? "project"
            : safe;
    }

    private void DeleteStoredImageFile(string imagePath)
    {
        var fileName = Path.GetFileName(imagePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var physicalPath = Path.Combine(
            environment.WebRootPath,
            "uploads",
            fileName);
        if (System.IO.File.Exists(physicalPath))
        {
            System.IO.File.Delete(physicalPath);
        }
    }
}
