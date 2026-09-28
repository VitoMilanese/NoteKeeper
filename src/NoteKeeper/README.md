# NoteKeeper

A modern web-based note keeper built with **ASP.NET Core 8, EF Core, and SQLite**.

## Features

- home page with a note list;
- search by title, text, comments, links, and image captions;
- filtering by `#tags` extracted automatically from text blocks;
- sorting by title, creation date, or last update;
- text blocks;
- link blocks with a title, URL, and optional comment;
- image blocks with captions;
- paste images from the clipboard with `Ctrl+V`;
- upload PNG, JPEG, WEBP, and GIF images up to 12 MB;
- reorder blocks with drag and drop or the ↑ / ↓ buttons;
- save notes with `Ctrl+S`;
- return to the note list with `Esc`;
- warn before leaving the editor when there are unsaved changes;
- preserve line breaks in text previews on note cards;
- show both the title and URL when a link block is used as the note preview;
- automatically create the SQLite database at `App_Data/notekeeper.db`;
- English, Italian, and Ukrainian UI localization;
- English as the default language;
- persist the selected language in a cookie;
- responsive dark interface that uses the available viewport width;
- no external JavaScript or CSS dependencies.

## Running the application

.NET 8 SDK is required.

```bash
dotnet restore
dotnet run
```

After startup, open the address printed by ASP.NET Core, for example `https://localhost:5001`.

## Data storage

- Database: `App_Data/notekeeper.db`
- Images: `wwwroot/uploads/`

To create a backup, save the database file and the `wwwroot/uploads` directory.

## Tags

Add tags directly to a text block, for example:

```text
Review this idea later. #work #idea
```

After saving the note, `work` and `idea` become available as filters on the home page.
