using System.Linq.Expressions;
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

public sealed class NotesController(
    AppDbContext db,
    IWebHostEnvironment environment,
    IStringLocalizer<AppResources> localizer,
    PriorityTagService priorityTagService) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index(
        string? q,
        string? tag,
        string sort = "updated",
        string dir = "desc",
        int page = 1,
        int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        q = q?.Trim();
        var tagFilter = TagFilterParser.Parse(tag);
        var includedTags = tagFilter.IncludedTags;
        var priorityTags = priorityTagService.GetTags();
        var priorityRanks = priorityTags
            .Select((name, index) => new { name, index })
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        sort = sort is "title" or "created" or "updated" ? sort : "updated";
        dir = dir == "asc" ? "asc" : "desc";
        pageSize = Math.Clamp(pageSize, 30, 60);

        IQueryable<Note> query = db.Notes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q}%";
            query = query.Where(note =>
                EF.Functions.Like(note.Title, pattern) ||
                note.Blocks.Any(block =>
                    (block.TextContent != null && EF.Functions.Like(block.TextContent, pattern)) ||
                    (block.LinkTitle != null && EF.Functions.Like(block.LinkTitle, pattern)) ||
                    (block.Url != null && EF.Functions.Like(block.Url, pattern)) ||
                    (block.Caption != null && EF.Functions.Like(block.Caption, pattern))));
        }

        query = ApplyTagFilter(query, tagFilter.Expression);

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);
        page = totalPages == 0
            ? 1
            : Math.Clamp(page, 1, totalPages);

        query = (sort, dir) switch
        {
            ("title", "asc") => query.OrderBy(x => x.Title).ThenBy(x => x.Id),
            ("title", _) => query.OrderByDescending(x => x.Title).ThenByDescending(x => x.Id),
            ("created", "asc") => query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            ("created", _) => query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id),
            ("updated", "asc") => query.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id)
        };

        var notes = await query
            .AsSplitQuery()
            .Include(x => x.Blocks)
            .Include(x => x.Tags)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var tagCounts = (await db.NoteTags
            .AsNoTracking()
            .GroupBy(x => x.Name)
            .Select(group => new TagCountViewModel
            {
                Name = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken))
            .OrderBy(x => priorityRanks.ContainsKey(x.Name) ? 0 : 1)
            .ThenBy(x => priorityRanks.TryGetValue(x.Name, out var rank) ? rank : int.MaxValue)
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList();

        var model = new NoteListViewModel
        {
            Query = q ?? string.Empty,
            Tag = tag?.Trim() ?? string.Empty,
            IncludedTags = includedTags.ToList(),
            Sort = sort,
            Direction = dir,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            PriorityTags = priorityTags.ToList(),
            Tags = tagCounts,
            Notes = notes.Select(note => new NoteListItemViewModel
            {
                Id = note.Id,
                Title = note.Title,
                Preview = BuildPreview(note.Blocks),
                CreatedAtUtc = note.CreatedAtUtc,
                UpdatedAtUtc = note.UpdatedAtUtc,
                BlockCount = note.Blocks.Count,
                Tags = note.Tags
                    .Select(x => x.Name)
                    .OrderBy(x => priorityRanks.ContainsKey(x) ? 0 : 1)
                    .ThenBy(x => priorityRanks.TryGetValue(x, out var rank) ? rank : int.MaxValue)
                    .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            }).ToList()
        };

        return View(model);
    }

    [HttpGet("/notes/new")]
    public IActionResult New()
    {
        return View("Edit", new NoteEditorViewModel
        {
            Title = string.Empty,
            Blocks =
            [
                new NoteBlockViewModel { Type = BlockType.Text }
            ]
        });
    }

    [HttpGet("/notes/{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var note = await db.Notes
            .AsNoTracking()
            .Include(x => x.Blocks)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        return View(new NoteEditorViewModel
        {
            Id = note.Id,
            Title = note.Title,
            UpdatedAtUtc = note.UpdatedAtUtc,
            Blocks = note.Blocks
                .OrderBy(x => x.SortOrder)
                .Select(x => new NoteBlockViewModel
                {
                    Type = x.Type,
                    TextContent = x.TextContent,
                    Url = x.Url,
                    LinkTitle = x.LinkTitle,
                    ImagePath = x.ImagePath,
                    Caption = x.Caption
                })
                .ToList()
        });
    }

    [HttpGet("/notes/{id:int}/export")]
    public async Task<IActionResult> Export(int id, CancellationToken cancellationToken)
    {
        var note = await db.Notes
            .AsNoTracking()
            .Include(x => x.Blocks)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        var document = new NoteTransferDocument
        {
            Title = note.Title,
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

            if (block.Type == BlockType.Image && !string.IsNullOrWhiteSpace(block.ImagePath))
            {
                var fileName = Path.GetFileName(block.ImagePath);
                var physicalPath = Path.Combine(environment.WebRootPath, "uploads", fileName);

                if (System.IO.File.Exists(physicalPath))
                {
                    transferBlock.ImageMimeType = GetImageMimeType(block.ImagePath);
                    transferBlock.ImageBase64 = Convert.ToBase64String(
                        await System.IO.File.ReadAllBytesAsync(physicalPath, cancellationToken));
                }
            }

            document.Blocks.Add(transferBlock);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(document, CreateTransferJsonOptions());
        var downloadName = $"{SanitizeFileName(note.Title)}.notekeeper.json";
        return File(json, "application/json; charset=utf-8", downloadName);
    }

    [HttpPost("/notes/import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(80_000_000)]
    public async Task<IActionResult> Import(IFormFile? noteFile, CancellationToken cancellationToken)
    {
        if (noteFile is null || noteFile.Length == 0 || noteFile.Length > 70_000_000)
        {
            return BadRequest(localizer["Server_ImportTooLarge"].Value);
        }

        var storedImagePaths = new List<string>();

        try
        {
            await using var input = noteFile.OpenReadStream();
            var document = await JsonSerializer.DeserializeAsync<NoteTransferDocument>(
                input,
                CreateTransferJsonOptions(),
                cancellationToken);

            if (document is null ||
                document.Blocks is null ||
                !string.Equals(document.Format, NoteTransferDocument.ExpectedFormat, StringComparison.Ordinal) ||
                document.Version != NoteTransferDocument.CurrentVersion ||
                document.Blocks.Count > 500)
            {
                return BadRequest(localizer["Server_ImportInvalid"].Value);
            }

            var note = new Note
            {
                Title = NormalizeTitle(document.Title),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Blocks = []
            };

            for (var index = 0; index < document.Blocks.Count; index++)
            {
                var block = document.Blocks[index];

                switch (block.Type)
                {
                    case BlockType.Text:
                        note.Blocks.Add(new NoteBlock
                        {
                            Type = BlockType.Text,
                            SortOrder = index,
                            TextContent = block.TextContent?.TrimEnd()
                        });
                        break;

                    case BlockType.Link:
                        note.Blocks.Add(new NoteBlock
                        {
                            Type = BlockType.Link,
                            SortOrder = index,
                            Url = NormalizeUrl(block.Url),
                            LinkTitle = Limit(block.LinkTitle?.Trim(), 300),
                            TextContent = block.TextContent?.TrimEnd()
                        });
                        break;

                    case BlockType.Image when
                        !string.IsNullOrWhiteSpace(block.ImageMimeType) &&
                        !string.IsNullOrWhiteSpace(block.ImageBase64):
                    {
                        var bytes = Convert.FromBase64String(block.ImageBase64);
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
                            Caption = Limit(block.Caption?.Trim(), 500)
                        });
                        break;
                    }
                }
            }

            var tags = TagExtractor.Extract(note.Blocks
                .Where(x => x.Type is BlockType.Text or BlockType.Link)
                .Select(x => RichTextContent.ToTagSearchText(x.TextContent)));

            note.Tags = tags
                .Select(x => new NoteTag { Name = x })
                .ToList();

            db.Notes.Add(note);
            await db.SaveChangesAsync(cancellationToken);

            return RedirectToAction(nameof(Edit), new { id = note.Id });
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException)
        {
            foreach (var imagePath in storedImagePaths)
            {
                DeleteStoredImageFile(imagePath);
            }

            return BadRequest(localizer["Server_ImportInvalid"].Value);
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

    [HttpPost("/notes/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveNoteRequest request, CancellationToken cancellationToken)
    {
        if (request.Blocks.Count > 500)
        {
            return BadRequest(new { message = localizer["Server_TooManyBlocks"].Value });
        }

        Note note;
        if (request.Id is > 0)
        {
            var existing = await db.Notes
                .Include(x => x.Blocks)
                .Include(x => x.Tags)
                .FirstOrDefaultAsync(x => x.Id == request.Id.Value, cancellationToken);

            if (existing is null)
            {
                return NotFound(new { message = localizer["Server_NoteNotFound"].Value });
            }

            note = existing;
        }
        else
        {
            note = new Note
            {
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Notes.Add(note);
        }

        var oldImagePaths = note.Blocks
            .Where(x => x.Type == BlockType.Image && !string.IsNullOrWhiteSpace(x.ImagePath))
            .Select(x => x.ImagePath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        note.Title = NormalizeTitle(request.Title);
        note.UpdatedAtUtc = DateTime.UtcNow;

        if (note.Blocks.Count > 0)
        {
            db.NoteBlocks.RemoveRange(note.Blocks);
        }

        note.Blocks = request.Blocks
            .Select((block, index) => ToEntity(block, index))
            .Where(x => x is not null)
            .Cast<NoteBlock>()
            .ToList();

        var tags = TagExtractor.Extract(note.Blocks
            .Where(x => x.Type is BlockType.Text or BlockType.Link)
            .Select(x => RichTextContent.ToTagSearchText(x.TextContent)));
        var desiredTags = tags.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tagsToRemove = note.Tags
            .Where(x => !desiredTags.Contains(x.Name))
            .ToList();
        if (tagsToRemove.Count > 0)
        {
            db.NoteTags.RemoveRange(tagsToRemove);
        }

        var existingTagNames = note.Tags
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tagName in desiredTags.Except(existingTagNames, StringComparer.OrdinalIgnoreCase))
        {
            note.Tags.Add(new NoteTag { Name = tagName });
        }

        await db.SaveChangesAsync(cancellationToken);

        var currentImagePaths = note.Blocks
            .Where(x => x.Type == BlockType.Image && !string.IsNullOrWhiteSpace(x.ImagePath))
            .Select(x => x.ImagePath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var removedPath in oldImagePaths.Except(currentImagePaths, StringComparer.OrdinalIgnoreCase))
        {
            await DeleteImageIfUnusedAsync(removedPath, note.Id, cancellationToken);
        }

        return Ok(new
        {
            id = note.Id,
            updatedAtUtc = note.UpdatedAtUtc,
            title = note.Title,
            tags
        });
    }

    [HttpPost("/notes/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var note = await db.Notes
            .Include(x => x.Blocks)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (note is null)
        {
            return RedirectToAction(nameof(Index));
        }

        var imagePaths = note.Blocks
            .Where(x => x.Type == BlockType.Image && !string.IsNullOrWhiteSpace(x.ImagePath))
            .Select(x => x.ImagePath!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        db.Notes.Remove(note);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var imagePath in imagePaths)
        {
            await DeleteImageIfUnusedAsync(imagePath, id, cancellationToken);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/notes/upload-image")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(15_000_000)]
    public async Task<IActionResult> UploadImage(IFormFile? image, CancellationToken cancellationToken)
    {
        if (image is null || image.Length == 0 || image.Length > 12_000_000)
        {
            return BadRequest(new { message = localizer["Server_ImageTooLarge"].Value });
        }

        try
        {
            await using var source = image.OpenReadStream();
            var path = await StoreImageAsync(source, image.ContentType, cancellationToken);
            return Ok(new { path });
        }
        catch (InvalidDataException)
        {
            return BadRequest(new { message = localizer["Server_ImageTypeUnsupported"].Value });
        }
    }

    private async Task<string> StoreImageAsync(
        Stream source,
        string contentType,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        return await StoreImageBytesAsync(buffer.ToArray(), contentType, cancellationToken);
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

        var originalExtension = GetExtensionForContentType(contentType);
        if (originalExtension is null)
        {
            throw new InvalidDataException("Unsupported image type.");
        }

        Image<Rgba32> image;
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            image = Image.Load<Rgba32>(input);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Invalid image data.", ex);
        }

        using (image)
        {
            var preserveOriginal = image.Frames.Count > 1 || HasTransparency(image);
            var extension = preserveOriginal ? originalExtension : ".jpg";
            var fileName = $"{Guid.NewGuid():N}{extension}";
            var uploadsDirectory = Path.Combine(environment.WebRootPath, "uploads");
            Directory.CreateDirectory(uploadsDirectory);
            var physicalPath = Path.Combine(uploadsDirectory, fileName);

            if (preserveOriginal)
            {
                await System.IO.File.WriteAllBytesAsync(physicalPath, bytes, cancellationToken);
            }
            else
            {
                image.SaveAsJpeg(physicalPath, new JpegEncoder { Quality = 90 });
            }

            return $"/uploads/{fileName}";
        }
    }

    private static bool HasTransparency(Image<Rgba32> image)
    {
        var hasTransparency = false;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height && !hasTransparency; y++)
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

    private static string? GetExtensionForContentType(string? contentType)
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
        return Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
    }

    private static JsonSerializerOptions CreateTransferJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safe = new string(value
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(safe) ? "note" : safe;
    }

    private void DeleteStoredImageFile(string imagePath)
    {
        var fileName = Path.GetFileName(imagePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var physicalPath = Path.Combine(environment.WebRootPath, "uploads", fileName);
        if (System.IO.File.Exists(physicalPath))
        {
            System.IO.File.Delete(physicalPath);
        }
    }

    private static IQueryable<Note> ApplyTagFilter(
        IQueryable<Note> query,
        TagFilterExpression? expression)
    {
        if (expression is null)
        {
            return query;
        }

        var noteParameter = Expression.Parameter(typeof(Note), "note");
        var body = BuildTagFilterExpression(noteParameter, expression);
        var predicate = Expression.Lambda<Func<Note, bool>>(
            body,
            noteParameter);

        return query.Where(predicate);
    }

    private static Expression BuildTagFilterExpression(
        ParameterExpression noteParameter,
        TagFilterExpression expression)
    {
        if (expression is TagFilterTag tag)
        {
            var tagParameter = Expression.Parameter(typeof(NoteTag), "tag");
            var tagName = Expression.Property(
                tagParameter,
                nameof(NoteTag.Name));
            var equalsName = Expression.Equal(
                tagName,
                Expression.Constant(tag.Name));
            var tagPredicate = Expression.Lambda<Func<NoteTag, bool>>(
                equalsName,
                tagParameter);
            var noteTags = Expression.Property(
                noteParameter,
                nameof(Note.Tags));
            var anyTag = Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Any),
                [typeof(NoteTag)],
                noteTags,
                tagPredicate);

            return tag.Negated
                ? Expression.Not(anyTag)
                : anyTag;
        }

        var group = (TagFilterGroup)expression;
        var itemExpressions = group.Items
            .Select(item => BuildTagFilterExpression(noteParameter, item))
            .ToArray();

        if (itemExpressions.Length == 0)
        {
            return Expression.Constant(true);
        }

        return itemExpressions
            .Skip(1)
            .Aggregate(
                itemExpressions[0],
                (current, item) => group.MatchAny
                    ? Expression.OrElse(current, item)
                    : Expression.AndAlso(current, item));
    }

    private string NormalizeTitle(string? value)
    {
        var title = (value ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return localizer["Server_UntitledNote"].Value;
        }

        return title.Length <= 240 ? title : title[..240];
    }

    private static string? NormalizeUrl(string? value)
    {
        var url = value?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        return parsed.Scheme is "http" or "https" ? parsed.ToString() : null;
    }

    private static string? NormalizeImagePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var path = value.Trim();
        if (!path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fileName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName) ? null : $"/uploads/{fileName}";
    }

    private static NoteBlock? ToEntity(SaveBlockRequest block, int sortOrder)
    {
        return block.Type switch
        {
            BlockType.Text => new NoteBlock
            {
                Type = BlockType.Text,
                SortOrder = sortOrder,
                TextContent = block.TextContent?.TrimEnd()
            },
            BlockType.Link => new NoteBlock
            {
                Type = BlockType.Link,
                SortOrder = sortOrder,
                Url = NormalizeUrl(block.Url),
                LinkTitle = Limit(block.LinkTitle?.Trim(), 300),
                TextContent = block.TextContent?.TrimEnd()
            },
            BlockType.Image when NormalizeImagePath(block.ImagePath) is { } imagePath => new NoteBlock
            {
                Type = BlockType.Image,
                SortOrder = sortOrder,
                ImagePath = imagePath,
                Caption = Limit(block.Caption?.Trim(), 500)
            },
            _ => null
        };
    }

    private static string? Limit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private string BuildPreview(IEnumerable<NoteBlock> blocks)
    {
        var preview = blocks
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Type switch
            {
                BlockType.Text => NormalizePreviewText(RichTextContent.ToPlainText(x.TextContent)),
                BlockType.Link => BuildLinkPreview(x),
                BlockType.Image => x.Caption,
                _ => null
            })
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?.Trim() ?? localizer["Server_EmptyNote"].Value;

        return preview.Length <= 180 ? preview : preview[..177] + "…";
    }

    private static string? NormalizePreviewText(string? value)
    {
        return value?
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static string? BuildLinkPreview(NoteBlock block)
    {
        var title = block.LinkTitle?.Trim();
        var url = block.Url?.Trim();

        if (!string.IsNullOrWhiteSpace(title) &&
            !string.IsNullOrWhiteSpace(url) &&
            !string.Equals(title, url, StringComparison.OrdinalIgnoreCase))
        {
            return $"{title}\n{url}";
        }

        return title ?? url;
    }

    private async Task DeleteImageIfUnusedAsync(string imagePath, int excludedNoteId, CancellationToken cancellationToken)
    {
        var isUsedElsewhere = await db.NoteBlocks
            .AsNoTracking()
            .AnyAsync(x => x.NoteId != excludedNoteId && x.ImagePath == imagePath, cancellationToken);

        if (isUsedElsewhere)
        {
            return;
        }

        var fileName = Path.GetFileName(imagePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var physicalPath = Path.Combine(environment.WebRootPath, "uploads", fileName);
        if (System.IO.File.Exists(physicalPath))
        {
            System.IO.File.Delete(physicalPath);
        }
    }
}
