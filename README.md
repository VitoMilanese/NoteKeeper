# NoteKeeper

A local-first personal knowledge and time-management application built with **ASP.NET Core 8**, **Entity Framework Core**, and **SQLite**.

NoteKeeper combines structured notes, a rich block-based editor, project organization, advanced tag filtering, and time tracking in a single lightweight application. It is designed to run locally, keep data under your control, and remain practical for everyday use.

## Highlights

- Project-based note organization
- Rich block editor with text, links, and images
- Rich-text formatting, hyperlinks, lists, quotes, expandable sections, dividers, symbols, emoji, text colors, and fixed text sizes
- Drag-and-drop block reordering
- Tags extracted automatically from note content and link comments
- Advanced tag filtering, grouping, autocomplete, and priority tags
- Full-text search across note content
- Note statuses with status-implied tags
- Estimated and spent time tracking
- Monthly time-management view with linked tasks and tracked notes
- Per-note and whole-project import/export
- Light and dark themes
- English, Italian, and Ukrainian localization
- Responsive full-width UI
- Windows system-tray integration
- Local SQLite storage with automatic schema upgrades

## Projects and notes

The application opens on a project dashboard. Each project has its own note workspace, filters, tags, pagination, and time-management data.

Projects can be:

- created, renamed, deleted, searched, and sorted;
- exported and imported as portable project files;
- used to keep notes, time entries, and related metadata isolated from other projects.

Existing databases created before project support are upgraded automatically and their notes are migrated into a default project.

## Note editor

Notes are composed from reusable content blocks:

- **Text**
- **Link**
- **Image**

Text blocks support rich formatting including:

- bold, italic, underline, and strikethrough;
- inline code and automatic backtick conversion;
- ordered and unordered lists;
- quotes;
- expandable containers;
- dividers;
- hyperlinks with keyboard support;
- fixed text sizes and custom text colors;
- quick symbols and an emoji picker.

Additional editor features include:

- drag-and-drop block reordering;
- image paste from the clipboard;
- image thumbnails with full-size preview;
- `Ctrl+S` / `Cmd+S` saving;
- unsaved-change protection;
- undo-friendly rich-text operations;
- a floating toolbar for long notes;
- portable note export/import with embedded images.

## Tags, search, and filtering

Tags are indexed from note content and link comments and are available for filtering, autocomplete, grouping, and note-card display.

Priority tags are configured in `priority-tags.txt` and are emphasized and sorted first.

NoteKeeper supports both a simple tag-filter syntax and an explicit logical syntax for more complex queries.

Examples:

```text
active project-x
-active project-x
work & !done
(work | personal) & !suspended
```

The note list also supports:

- text search;
- tag autocomplete;
- grouping by tags present in the current result set;
- configurable sorting and direction;
- adaptive pagination;
- responsive note cards with compact tag overflow.

## Note statuses

Notes can use the following statuses:

- Backlog
- Selected
- Active
- Test
- Done
- Released
- Rejected
- Suspended
- Simple note

Statuses participate in filtering through effective tags. For example, Active contributes `#active`, Released contributes `#done` and `#released`, and Simple note contributes `#note`.

## Time management

Each project has a dedicated **Time** page.

Features include:

- one calendar month per page;
- date containers sorted newest first;
- Jira-style duration input such as `1w 2d 3h 15m`;
- optional task linking to project notes;
- free-text activities that are not linked to a note;
- comments for individual time entries;
- direct links back to linked notes;
- editable calendar dates;
- AJAX add/edit/delete flows that preserve the current page position;
- automatic recalculation of note spent time;
- tracked notes with Estimated, Spent, and Remaining values;
- overtime display when spent time exceeds the estimate;
- bulk removal from the tracked-note list;
- collapsible tracked-note panel with persisted state.

## Localization and themes

The interface is available in:

- English
- Italian
- Ukrainian

English is the default language. Language and light/dark theme preferences are persisted in cookies.

## Windows desktop integration

On Windows, NoteKeeper can run together with a small isolated tray helper.

The tray integration provides:

- Open NoteKeeper
- Hide / Show
- Exit
- application and tray icons
- clean shutdown through a named Windows event

The tray helper is a separate process so tray/UI failures do not terminate the ASP.NET Core application. It is skipped in the Development environment used by Visual Studio debugging.

## Technology

- C#
- .NET 8
- ASP.NET Core MVC
- Entity Framework Core 8
- SQLite
- Razor
- JavaScript
- CSS
- SixLabors.ImageSharp
- Windows Forms tray helper on Windows

## Getting started

### Requirements

- .NET 8 SDK
- Windows is recommended if you want the tray integration

### Run

```bash
cd src/NoteKeeper
dotnet restore
dotnet run
```

The default local endpoints are:

```text
https://localhost:7147
http://localhost:5147
```

The HTTP endpoint redirects to HTTPS.

You can also open `src/NoteKeeper.sln` in Visual Studio 2022.

## Data storage

By default, application data is stored locally:

- SQLite database: `src/NoteKeeper/App_Data/notekeeper.db`
- uploaded images: `src/NoteKeeper/wwwroot/uploads/`

For a practical backup, keep the database and uploaded-image directory together.

## Repository structure

```text
src/
├── NoteKeeper/        ASP.NET Core application
├── NoteKeeper.Tray/   Windows tray helper
└── NoteKeeper.sln
```

## Design goals

NoteKeeper is intentionally focused on practical personal use:

- keep data local;
- make notes fast to create and easy to retrieve;
- support structured workflows without forcing every note into a task-management model;
- keep the UI usable for both short notes and large, heavily formatted documents;
- combine knowledge management and time tracking without requiring external services.
