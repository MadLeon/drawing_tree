using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrawingTree.Logging;
using DrawingTree.Models;

namespace DrawingTree.Services;

/// <summary>
/// Service class for extracting drawing information from PDF files
/// Scans folders, extracts drawing numbers from filenames, and deduplicates results
/// </summary>
public class DrawingExtractor
{
    /// <summary>
    /// Extract drawing number from PDF filename
    /// Drawing number is the text before the first space in the filename
    /// Only processes filenames containing "rt-" (case-insensitive)
    /// Handles both space-separated and underscore-separated revision formats
    /// Returns the drawing number in uppercase
    /// </summary>
    /// <param name="fileName">PDF filename without extension</param>
    /// <returns>Extracted drawing number in uppercase, or empty string if no "rt-" found</returns>
    public string ExtractDrawingNumber(string fileName)
    {
        try
        {
            // Check if filename contains "rt-" (case-insensitive)
            if (!fileName.Contains("rt-", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Instance.Debug($"Skipped file (no 'rt-' found): {fileName}");
                return string.Empty;
            }

            // Find the position of the first space
            int spacePosition = fileName.IndexOf(' ');

            // If no space found, use the entire filename
            string drawingNumber = spacePosition == -1 
                ? fileName 
                : fileName.Substring(0, spacePosition);

            // Check if there's an underscore in the drawing number
            int underscorePosition = drawingNumber.IndexOf('_');
            if (underscorePosition > 0)
            {
                // Get text after underscore
                string afterUnderscore = drawingNumber.Substring(underscorePosition + 1);

                // Check if it matches revision pattern: Rev/rev followed by optional dot and digits
                if (IsRevisionFormat(afterUnderscore))
                {
                    // Remove the underscore and revision part
                    string original = drawingNumber;
                    drawingNumber = drawingNumber.Substring(0, underscorePosition);
                    Logger.Instance.Debug($"Removed underscore revision format from: {original} -> {drawingNumber}");
                }
            }

            // Convert to uppercase
            drawingNumber = drawingNumber.ToUpper();

            return drawingNumber;
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ExtractDrawingNumber failed for file: {fileName}, Error: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Check if a string matches the revision format
    /// Matches patterns like: Rev1, rev3, Rev.1, rev.2, etc.
    /// </summary>
    /// <param name="text">Text to check</param>
    /// <returns>True if matches revision format, false otherwise</returns>
    private bool IsRevisionFormat(string text)
    {
        // Must start with "Rev" or "rev"
        if (text.Length < 4)
            return false;

        if (!text.StartsWith("rev", StringComparison.OrdinalIgnoreCase))
            return false;

        // Check character after "rev"
        char charAfterRev = text[3];

        // Can be a dot or a digit
        if (charAfterRev == '.')
        {
            // If it's a dot, must have at least one digit after it
            return text.Length > 4 && char.IsDigit(text[4]);
        }
        else
        {
            // Otherwise must be a digit
            return char.IsDigit(charAfterRev);
        }
    }

    /// <summary>
    /// Scan a folder for PDF files and extract drawing information
    /// Automatically deduplicates entries (keeps first occurrence)
    /// </summary>
    /// <param name="folderPath">Path to folder containing PDF files</param>
    /// <returns>List of unique DrawingInfo objects</returns>
    public List<DrawingInfo> ScanFolder(string folderPath)
    {
        var results = new List<DrawingInfo>();
        var seenDrawingNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!Directory.Exists(folderPath))
            {
                Logger.Instance.Error($"Folder does not exist: {folderPath}");
                return results;
            }

            Logger.Instance.Info($"Scanning folder: {folderPath} for PDF files");

            // Get all PDF files in the folder
            string[] pdfFiles = Directory.GetFiles(folderPath, "*.pdf", SearchOption.TopDirectoryOnly);

            foreach (string pdfPath in pdfFiles)
            {
                // Get filename without extension
                string fileName = Path.GetFileNameWithoutExtension(pdfPath);

                // Extract drawing number
                string drawingNumber = ExtractDrawingNumber(fileName);

                // Add to results if not empty and not already present
                if (!string.IsNullOrEmpty(drawingNumber))
                {
                    if (!seenDrawingNumbers.Contains(drawingNumber))
                    {
                        seenDrawingNumbers.Add(drawingNumber);
                        results.Add(new DrawingInfo
                        {
                            DrawingNumber = drawingNumber,
                            PdfPath = pdfPath
                        });
                        Logger.Instance.Debug($"Found drawing: {drawingNumber} at {pdfPath}");
                    }
                    else
                    {
                        Logger.Instance.Debug($"Skipped duplicate drawing: {drawingNumber} at {pdfPath}");
                    }
                }
            }

            Logger.Instance.Info($"Scan completed with {results.Count} unique drawing entries");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"ScanFolder failed for path: {folderPath}, Error: {ex.Message}");
        }

        return results;
    }
}
