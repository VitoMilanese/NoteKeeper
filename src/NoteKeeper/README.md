# NoteKeeper

A modern web-based note keeper built with **ASP.NET Core 8, EF Core, and SQLite**.

## Features

- home page with a note list and server-side pagination shown only when more than one page is needed, with the top pagination aligned in the notes heading row and the second pager below the grid;
- search by title, text, comments, links, and image captions;
- filtering by one or more comma-separated `#tags` extracted automatically from text blocks and link comments;
- highlight `#active` and `#done` tags with stronger emphasis, with higher-contrast tag chips in the light theme;
- clear the search and tag filter fields from a dedicated button while preserving the selected sort and direction;
- sorting by title, creation date, or last update;
- rich text blocks with bold, italic, underline, strikethrough, inline code, hyperlinks on selected rich text, quotes, expandable containers, dividers, text sizing, custom text colors, numbered lists, bullet lists, dash lists, and a quick symbol palette;
- show a duplicate floating rich-text toolbar near the bottom of the viewport while editing long text blocks after the original toolbar scrolls out of view;
- rich-text keyboard shortcuts for `Ctrl+B`, `Ctrl+I`, `Ctrl+U`, and `Ctrl+K` for hyperlinks;
- link blocks with a title, URL, and optional comment;
- image blocks with captions, compact thumbnails, and click-to-open full-size previews;
- automatically convert opaque uploaded images to JPEG while preserving transparency and animated images;
- paste images from the clipboard with `Ctrl+V`;
- upload PNG, JPEG, WEBP, and GIF images up to 12 MB;
- reorder blocks with drag and drop or the ↑ / ↓ buttons;
- new text, link, and image blocks are always appended to the end of the note before any manual reordering;
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
- the relative SQLite database path is resolved from the application content root, so launching the executable from another working directory does not break database startup;
- English, Italian, and Ukrainian UI localization;
- English as the default language;
- persist the selected language in a cookie;
- switchable light and dark themes with the selected theme stored in a cookie;
- Windows executable icon and system tray integration through an isolated Windows Forms helper process, so tray failures cannot terminate the web application;
- double-click the tray icon or choose `Open NoteKeeper` to open the home page, use `Hide` / `Show` to hide or restore the NoteKeeper console window, and choose `Exit` to stop the application;
- tray integration is intentionally skipped in the Development environment used by the Visual Studio F5 profile; run the built executable directly when testing the tray;
- the shared Visual Studio `NoteKeeper.slnLaunch` profile starts only the web application; the tray helper project is explicitly set to `None` so F5 cannot accidentally run the helper by itself;
- direct launches from the build output use the executable directory as the content root and copy `wwwroot` into the build output, so CSS, JavaScript, favicon, and other static resources are available reliably outside the Development profile;
- direct executable launches use the same default URLs as the Visual Studio profile: `https://localhost:7147` and `http://localhost:5147`;
- the tray home-page URL is resolved from the actual server addresses, with the configured URL list as a fallback, instead of falling back to port 5000;
- responsive interface that uses the available viewport width;
- show at least 30 notes per page and automatically round the page size up to a complete final grid row for the current card-column count;
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

After saving the note, `work` and `idea` become available as filters on the home page. Enter multiple tags separated by commas to require all of them, for example `work, idea`. Hashtags inside inline code or quote blocks are treated as content and are not indexed as tags.

## Rich text notes

Text blocks support formatting from the toolbar as well as `Ctrl+B`, `Ctrl+I`, `Ctrl+U`, and `Ctrl+K`. Expandable-container titles behave as editable text: clicking the title places the caret, spaces type normally, and the container toggles only from the disclosure arrow or the unused header area to the right. New expandable containers start with an empty body, include an editor-only × delete control, insert without adding real blank rows, and inserting a container participates in `Ctrl+Z` / `Cmd+Z` undo. When an expander has no normal text before or after it, the editor keeps a compact editor-only caret zone on that side so the user can always click outside the container and continue typing; these caret zones are not saved as note content. Hold `Ctrl` (or `Cmd` on macOS) and click a rich-text hyperlink to open it in a new tab. Press `Enter` in the hyperlink URL field to apply the link. Hyperlink application does not depend on native form submission, so keyboard and button application share the same selection logic. Creating, editing, and removing a rich-text hyperlink uses the browser's native contenteditable link commands, so the operation is recorded in the editing history and can be undone with `Ctrl+Z` / `Cmd+Z`. Surround inline text with backticks, for example `` `code` ``, to convert it to inline code automatically. Text-size and text-color changes keep the formatted selection active. For long text blocks, once the normal toolbar scrolls above the visible editor area, a duplicate toolbar appears near the bottom of the viewport while that text block remains active and visible, then disappears again near the block end or when returning to the top. Click a divider and press `Delete` or `Backspace` to remove it. Inline code and quote insertion leave the caret in normal text after the inserted content so typing can continue normally.

The symbol palette includes an in-app emoji picker rendered above the editor with category tabs and search. It closes on outside click or `Esc` without leaving the note editor.
