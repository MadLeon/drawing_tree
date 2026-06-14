# Drawing Tree

A Windows desktop application (WPF / .NET 10) for building and browsing engineering drawing trees.

## Features

### Drawing Tree Builder
- **Import Drawings** — Select a PDF folder; drawing numbers are automatically extracted from filenames (supports `RT-` prefix convention).
- **Edit Drawing List** — Review, add, or remove extracted drawing entries before committing.
- **Build Tree** — Select a PO from saved import files and arrange drawings into a parent-child hierarchy via drag-and-drop.
- **Save to Database** — Persist the constructed tree into the SQLite database.

### Drawing Viewer
- **Search** — Locate drawings by drawing number, job number, or PO number.
- **Tree View** — Left panel shows the drawing hierarchy for the selected PO.
- **PDF Preview** — Right panel renders the drawing PDF inline.

## Tech Stack

| Component | Details |
|-----------|---------|
| Framework | .NET 10, WPF + Windows Forms |
| Database  | SQLite (`Microsoft.Data.Sqlite` 9.x) |
| Target OS | Windows 10 (10.0.19041+) |
| IDE       | Visual Studio 2022+ |

## Requirements

- Windows 10 version 2004 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 (or `dotnet` CLI)

## Getting Started

```powershell
# Clone
git clone <repo-url>
cd drawing_tree

# Build (Debug)
dotnet build DrawingTree.sln

# Run
dotnet run --project src/DrawingTree/DrawingTree.csproj
```

Or open `DrawingTree.sln` in Visual Studio and press **F5**.

## Project Structure

```
drawing_tree/
├── src/
│   └── DrawingTree/
│       ├── Controls/           # Reusable WPF UserControls
│       │   ├── DrawingEditorControl   # Drawing list editor
│       │   ├── TreeBuilderControl     # Drag-and-drop tree builder
│       │   └── DrawingViewerControl   # Split-pane viewer (tree + PDF)
│       ├── Data/               # Repository layer (SQLite)
│       ├── Dialogs/            # Modal dialogs (PO selection, add drawing)
│       ├── Logging/            # Singleton logger with daily rotation
│       ├── Models/             # Data models
│       ├── Services/           # Business logic
│       │   ├── DrawingExtractor.cs    # PDF filename parser
│       │   └── PoTreeService.cs       # Tree persistence service
│       ├── MainWindow.xaml
│       └── DrawingTree.csproj
├── sessions/                   # Development session notes
├── DrawingTree.sln
└── CLAUDE.md
```

## Database

Key tables:

| Table            | Purpose |
|------------------|---------|
| `part`           | Drawing records (number + revision) |
| `part_tree`      | Parent-child BOM relationships |
| `drawing_file`   | Scanned PDF file paths (137k+ records) |
| `purchase_order` | PO records |
| `job`            | Job numbers linked to POs |
| `order_item`     | Line items per job |

Full schema: [`.github/skills/database/reference/schema-reference.md`](.github/skills/database/reference/schema-reference.md)

### Database Backup

```powershell
Copy-Item data/record.db "data/record.db.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
```

## Logging

Logs are written to `Logs/log_yyyy-MM-dd.txt`, auto-rotated daily, 7-day retention.

Configure via `config.txt`:

```
MinimumLogLevel=INFO
LogRetentionDays=7
```

## Drawing Number Convention

The extractor processes PDF filenames following this convention:

```
RT-88000-70097-045-1-DD-C Rev3.pdf   →  RT-88000-70097-045-1-DD-C
RT-88000-70097-045-1-DD-C_Rev.1.pdf  →  RT-88000-70097-045-1-DD-C
```

- Only filenames containing `RT-` (case-insensitive) are processed.
- Drawing number is the text before the first space.
- Trailing `_RevN` / `_Rev.N` suffixes are stripped automatically.
- Duplicates within the same folder are deduplicated (first occurrence kept).
