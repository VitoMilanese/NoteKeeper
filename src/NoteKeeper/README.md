# NoteKeeper

A modern web-based note keeper built with **ASP.NET Core 8, EF Core, and SQLite**.

## Features

- home page with a note list;
- search by title, text, comments, links, and image captions;
- filtering by one or more comma-separated `#tags` extracted automatically from text blocks and link comments;
- sorting by title, creation date, or last update;
- rich text blocks with bold, italic, underline, strikethrough, inline code, quotes, expandable containers, dividers, text sizing, numbered lists, bullet lists, dash lists, and a quick symbol palette;
- rich-text keyboard shortcuts for `Ctrl+B`, `Ctrl+I`, and `Ctrl+U`;
- link blocks with a title, URL, and optional comment;
- image blocks with captions;
- automatically convert opaque uploaded images to JPEG while preserving transparency and animated images;
- paste images from the clipboard with `Ctrl+V`;
- upload PNG, JPEG, WEBP, and GIF images up to 12 MB;
- reorder blocks with drag and drop or the ↑ / ↓ buttons;
- save notes with `Ctrl+S`;
- return to the note list with `Esc`;
- styled in-app confirmation dialogs for destructive actions and unsaved internal navigation;
- browser-level protection when closing a tab with unsaved changes;
- preserve line breaks in text previews on note cards;
- show both the title and URL when a link block is used as the note preview;
- open link-block URLs directly from a button next to the URL field;
- export individual notes to readable portable `.notekeeper.json` files and import them again, including embedded images;
- permanently delete notes from SQLite, including related blocks and tags, with unused uploaded images cleaned up;
- automatically create the SQLite database at `App_Data/notekeeper.db`;
- English, Italian, and Ukrainian UI localization;
- English as the default language;
- persist the selected language in a cookie;
- switchable light and dark themes with the selected theme stored in a cookie;
- responsive interface that uses the available viewport width;
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

Add tags directly to a text block or a link comment, for example:

```text
Review this idea later. #work #idea
```

After saving the note, `work` and `idea` become available as filters on the home page. Enter multiple tags separated by commas to require all of them, for example `work, idea`.

## Rich text notes

Text blocks support formatting from the toolbar as well as `Ctrl+B`, `Ctrl+I`, and `Ctrl+U`. Click a divider and press `Delete` or `Backspace` to remove it. Inline code and quote insertion leave the caret in normal text after the inserted content so typing can continue normally.

The symbol palette includes an in-app emoji picker rendered with the system emoji font. Native operating-system emoji panels cannot be opened programmatically by a normal web page.
