/// <summary>
/// DirFileService.cs
/// Handles DIR file operations: path generation, template copy,
/// Excel cell population via COM Interop (requires Excel installed).
/// </summary>

using System.IO;
using System.Runtime.InteropServices;
using DrawingTree.Data;
using DrawingTree.Logging;

namespace DrawingTree.Services;

public static class DirFileService
{
    public const string TemplatePath = @"\\rtdnas2\QCReports\FINAL REPORTS\DIR Template.xlsm";

    /// <summary>
    /// Returns whether the DIR template file is reachable.
    /// </summary>
    public static bool TemplateExists() => File.Exists(TemplatePath);

    /// <summary>Only this customer archives DIR files in a per-PO subfolder; everyone else uses the root.</summary>
    private const string PoSubfolderCustomer = "Candu";

    /// <summary>
    /// Resolves the DIR target folder for the given context without creating any files.
    /// For <see cref="PoSubfolderCustomer"/> the folder is {DIR Folder}\{PO number}; for every other
    /// customer it is the configured {DIR Folder} itself.
    /// The PO revision suffix (e.g. "-R.1", "-R01") is stripped via <see cref="OeNormalization.GetPoBase"/>
    /// so that different revisions of the same order share one target folder.
    /// Throws <see cref="DirFolderNotConfiguredException"/> when the customer has no configured folder.
    /// </summary>
    public static string ResolveFolder(MpContext ctx)
    {
        var customerName = ctx.CustomerName ?? "Unknown";
        var baseFolder = DirConfig.GetDirFolder(customerName)
            ?? throw new DirFolderNotConfiguredException(customerName, DirConfig.ConfigFilePath);

        if (!customerName.Trim().Equals(PoSubfolderCustomer, StringComparison.OrdinalIgnoreCase))
            return baseFolder;

        var po = SanitizeFolderName(OeNormalization.GetPoBase(ctx.PoNumber));
        return string.IsNullOrEmpty(po) ? baseFolder : Path.Combine(baseFolder, po);
    }

    /// <summary>
    /// Generates the DIR file name: {DrawingNumber} REV. {Revision} @{JobNumber}.xlsm
    /// (base name uppercase, extension lowercase).
    /// </summary>
    public static string BuildFileName(string? drawingNumber, string? revision, string jobNumber)
        => $"{(drawingNumber ?? string.Empty).Trim()} REV. {(revision ?? string.Empty).Trim()} @{jobNumber.Trim()}"
            .ToUpperInvariant() + ".xlsm";

    /// <summary>
    /// Extracts the job number embedded in a DIR file name, i.e. the part after the last '@'
    /// in "{DrawingNumber} REV. {Revision} @{JobNumber}.xlsm". Returns null when absent.
    /// </summary>
    /// <param name="fileName">DIR file name or full path</param>
    /// <returns>Job number without extension, or null when the name carries no '@' marker</returns>
    public static string? ExtractJobNumber(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var at = baseName.LastIndexOf('@');
        if (at < 0) return null;

        var job = baseName[(at + 1)..].Trim();
        return job.Length > 0 ? job : null;
    }

    /// <summary>
    /// Returns whether a DIR file belongs to the given job, based on the job number in its file name.
    /// A file whose name carries no job number is treated as not belonging to the job, so that
    /// associating an older order's DIR never marks it as the current order's file.
    /// </summary>
    /// <param name="fileName">DIR file name or full path</param>
    /// <param name="jobNumber">Job number of the current order item</param>
    /// <returns>True when both job numbers are present and equal</returns>
    public static bool BelongsToJob(string? fileName, string? jobNumber)
    {
        var fileJob = ExtractJobNumber(fileName);
        return fileJob != null && !string.IsNullOrWhiteSpace(jobNumber) &&
               fileJob.Equals(jobNumber.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Workbook extensions accepted as DIR files.</summary>
    private static readonly string[] DirExtensions = { ".xlsm", ".xlsx" };

    /// <summary>
    /// Returns DIR workbooks in the folder whose names start with the given drawing number,
    /// newest first. Returns an empty list if the folder does not exist.
    /// </summary>
    /// <param name="folder">Folder to scan</param>
    /// <param name="drawingNumber">Drawing number the file name must start with; null matches all</param>
    public static List<string> GetExistingFiles(string folder, string? drawingNumber)
    {
        if (!Directory.Exists(folder)) return new List<string>();

        return Directory.GetFiles(folder)
            .Where(f => DirExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => string.IsNullOrWhiteSpace(drawingNumber) ||
                        Path.GetFileName(f).StartsWith(drawingNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();
    }

    /// <summary>
    /// Creates a new DIR file from the template into <paramref name="confirmedFolder"/>, pre-fills cells,
    /// records it in the database, and opens it in Excel. The caller must have already verified/created
    /// the target folder — this method never creates directories.
    /// Throws <see cref="InvalidOperationException"/> when a DIR file with the same name already exists.
    /// Throws <see cref="FileNotFoundException"/> when the template is missing.
    /// </summary>
    /// <param name="ctx">Order item context used to populate the cells.</param>
    /// <param name="confirmedFolder">Target folder, already confirmed to exist.</param>
    /// <returns>Full path of the created file.</returns>
    public static string CreateAndOpen(MpContext ctx, string confirmedFolder)
    {
        if (!TemplateExists())
            throw new FileNotFoundException($"DIR template not found: {TemplatePath}");

        var fileName = BuildFileName(ctx.DrawingNumber, ctx.Revision, ctx.JobNumber);
        var target   = Path.Combine(confirmedFolder, fileName);

        // Never overwrite an existing DIR file
        if (File.Exists(target))
            throw new InvalidOperationException($"DIR file already exists:\n{target}");

        File.Copy(TemplatePath, target);

        OpenAndFillCells(target, ctx);

        // Record in database
        new PartRepository().AddDirAttachment(ctx.PartId, ctx.OrderItemId, fileName, target);

        Logger.Instance.Info($"DirFileService: created and opened '{target}'");
        return target;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Opens the copied file in a visible Excel, fills the required cells (uppercased) and leaves
    /// the workbook open for the user. Reuses an already running Excel when there is one, which is
    /// what keeps this fast: no hidden instance is started, saved, closed and then re-launched.
    /// The workbook is intentionally left unsaved so the user owns the first save.
    /// </summary>
    /// <param name="filePath">Full path of the freshly copied DIR file</param>
    /// <param name="ctx">Order item context used to populate the cells</param>
    private static void OpenAndFillCells(string filePath, MpContext ctx)
    {
        dynamic? app = null;
        dynamic? wb  = null;
        dynamic? ws  = null;

        try
        {
            app = GetOrCreateExcel();
            app.Visible     = true;
            // Keep Excel alive once the COM references below are released.
            app.UserControl = true;

            wb = app.Workbooks.Open(
                filePath,
                UpdateLinks: false,
                ReadOnly: false,
                IgnoreReadOnlyRecommended: true,
                Notify: false);

            ws = wb.Sheets[1];

            SetCell(ws, "C6",  ctx.CustomerName);
            SetCell(ws, "C7",  $"{ctx.DrawingNumber} REV. {ctx.Revision}");
            SetCell(ws, "C8",  DescriptionFormatter.LastSegment(ctx.Description));
            SetCell(ws, "C11", "INCH");
            SetCell(ws, "H6",  ctx.JobNumber);
            SetCell(ws, "H7",  ctx.OeNumber);
            SetCell(ws, "H8",  OeNormalization.GetPoBase(ctx.PoNumber));

            try { app.ActiveWindow.Activate(); } catch { /* window focus is best effort */ }
        }
        finally
        {
            // Release our references only — the workbook and Excel stay open for the user.
            if (ws  != null) Marshal.ReleaseComObject(ws);
            if (wb  != null) Marshal.ReleaseComObject(wb);
            if (app != null) Marshal.ReleaseComObject(app);
        }
    }

    /// <summary>
    /// Returns the running Excel instance, or starts a new one when Excel is not running.
    /// </summary>
    /// <returns>Excel.Application COM object</returns>
    /// <exception cref="InvalidOperationException">Excel is not installed or not registered.</exception>
    private static dynamic GetOrCreateExcel()
    {
        try
        {
            CLSIDFromProgID("Excel.Application", out Guid clsid);
            GetActiveObject(ref clsid, IntPtr.Zero, out object running);
            return running;
        }
        catch (Exception ex)
        {
            Logger.Instance.Debug($"DirFileService: no running Excel to reuse ({ex.Message}), starting a new one");
        }

        var excelType = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException("Excel is not installed or not registered.");
        return Activator.CreateInstance(excelType)!;
    }

    [DllImport("ole32.dll", PreserveSig = false)]
    private static extern void CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object unknown);

    private static void SetCell(dynamic ws, string address, string? value)
        => ws.Range[address].Value2 = (value ?? string.Empty).ToUpperInvariant();

    private static string? SanitizeFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
