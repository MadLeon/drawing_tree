'/**
' * Scanning Script Module for PDF Drawing Extraction
' * 
' * This module provides functionality to scan a network path for PDF files,
' * extract drawing numbers from file names, deduplicate entries, sort by 
' * multi-field rules, and populate Excel worksheet columns.
' * 
' * Dependencies:
' * - dat_global_variable.bas (Global variables and DEBUG_MODE)
' * - lib_logger.bas (Logging functionality)
' * - mod_multi_field_sort.bas (ParseFields, CompareFields functions for sorting)
' * 
' * Usage:
' * - Call TestScanDrawings() to test the functionality
' * - Call ScanAndFillDrawings(networkPath) to scan, deduplicate, sort, and populate data
' * 
' * The scanning process includes:
' * 1. Scan network path for PDF files and extract drawing numbers
' * 2. Automatic deduplication during scan (using dictionary for efficiency)
' * 3. Sort by multi-field rules (left-to-right priority on "-" delimited fields)
' * 4. Populate Excel worksheet with sorted, unique drawing numbers and hyperlinks
' */

Option Explicit

'/**
' * Extracts the drawing number from a PDF filename
' * 
' * The drawing number is the text before the first space in the filename.
' * Only processes filenames containing "rt-" (case-insensitive).
' * Handles both space-separated and underscore-separated revision formats.
' * Returns the drawing number in uppercase.
' * 
' * Examples:
' * - "RT-87900-0408 rev1" -> "RT-87900-0408"
' * - "RT-87900-0408 Bushing Details" -> "RT-87900-0408"
' * - "RT-88000-70099-002-1-DD-B_Rev3 windows detail" -> "RT-88000-70099-002-1-DD-B"
' * - "invalid-file name" -> "" (ignored, no "rt-")
' * 
' * @param {String} fileName - The PDF filename (without extension)
' * @return {String} The extracted drawing number in uppercase, or empty string if no "rt-" found
' */
Function ExtractDrawingNumber(fileName As String) As String
    Dim spacePosition As Long
    Dim underscorePosition As Long
    Dim drawingNumber As String
    Dim afterUnderscore As String
    Dim fileNameLower As String
    
    On Error GoTo ErrorHandler
    
    ' Check if filename contains "rt-" (case-insensitive)
    fileNameLower = LCase(fileName)
    If InStr(1, fileNameLower, "rt-") = 0 Then
        Call LogDebug("Skipped file (no 'rt-' found): " & fileName)
        ExtractDrawingNumber = ""
        Exit Function
    End If
    
    ' Find the position of the first space
    spacePosition = InStr(1, fileName, " ")
    
    ' If no space found, use the entire filename
    If spacePosition = 0 Then
        drawingNumber = fileName
    Else
        ' Extract text before the first space
        drawingNumber = Left(fileName, spacePosition - 1)
    End If
    
    ' Check if there's an underscore in the drawing number
    underscorePosition = InStr(1, drawingNumber, "_")
    If underscorePosition > 0 Then
        ' Get text after underscore
        afterUnderscore = Mid(drawingNumber, underscorePosition + 1)
        
        ' Check if it matches revision pattern: Rev/rev followed by optional dot and digits
        If IsRevisionFormat(afterUnderscore) Then
            ' Remove the underscore and revision part
            drawingNumber = Left(drawingNumber, underscorePosition - 1)
            Call LogDebug("Removed underscore revision format from: " & Left(fileName, spacePosition - 1) & " -> " & drawingNumber)
        End If
    End If
    
    ' Convert to uppercase
    drawingNumber = UCase(drawingNumber)
    
    ExtractDrawingNumber = drawingNumber
    Exit Function
    
ErrorHandler:
    Call LogError("ExtractDrawingNumber failed for file: " & fileName & ", Error: " & Err.Description)
    ExtractDrawingNumber = ""
End Function

'/**
' * Helper function to check if a string matches the revision format
' * Matches patterns like: Rev1, rev3, Rev.1, rev.2, etc.
' * 
' * @param {String} textToCheck - The text to check
' * @return {Boolean} True if matches revision format, False otherwise
' */
Function IsRevisionFormat(textToCheck As String) As Boolean
    Dim char As String
    
    ' Must start with "Rev" or "rev"
    If Len(textToCheck) < 4 Then
        IsRevisionFormat = False
        Exit Function
    End If
    
    If LCase(Left(textToCheck, 3)) <> "rev" Then
        IsRevisionFormat = False
        Exit Function
    End If
    
    ' Check character after "rev"
    char = Mid(textToCheck, 4, 1)
    
    ' Can be a dot or a digit
    If char = "." Then
        ' If it's a dot, must have at least one digit after it
        IsRevisionFormat = (Len(textToCheck) > 4) And IsNumeric(Mid(textToCheck, 5, 1))
    Else
        ' Otherwise must be a digit
        IsRevisionFormat = IsNumeric(char)
    End If
End Function

'/**
' * Helper function to check if a single character is numeric
' * 
' * @param {String} char - The character to check
' * @return {Boolean} True if numeric, False otherwise
' */
Function IsNumeric(char As String) As Boolean
    IsNumeric = (char >= "0" And char <= "9")
End Function

'/**
' * Scans a network path for PDF files and extracts drawing numbers with file paths
' * 
' * Uses a dictionary for deduplication during the scan process to ensure each
' * drawing number appears only once in the results. If the same drawing number
' * is found multiple times, only the first occurrence is kept.
' * 
' * Returns a Collection where each element is an array [drawingNumber, pdfPath]
' * 
' * @param {String} networkPath - The network path to scan (e.g., "\\server\folder")
' * @return {Collection} Collection of deduplicated [drawingNumber, pdfPath] pairs
' */
Function ScanNetworkPath(networkPath As String) As Collection
    Dim fso As Object
    Dim folder As Object
    Dim file As Object
    Dim dict As Object
    Dim results As Collection
    Dim drawingNumber As String
    Dim fileName As String
    Dim pdfPath As String
    Dim drawingInfo As Variant
    Dim key As Variant
    
    On Error GoTo ErrorHandler
    
    ' Create FileSystemObject and Dictionary for deduplication
    Set fso = CreateObject("Scripting.FileSystemObject")
    Set dict = CreateObject("Scripting.Dictionary")
    Set results = New Collection
    
    ' Check if path exists
    If Not fso.FolderExists(networkPath) Then
        Call LogError("Network path does not exist: " & networkPath)
        Set ScanNetworkPath = results
        Exit Function
    End If
    
    Set folder = fso.GetFolder(networkPath)
    Call LogInfo("Scanning path: " & networkPath & " for PDF files with deduplication", "ScanNetworkPath")
    
    ' Iterate through all files in the folder
    For Each file In folder.Files
        ' Check if file is a PDF
        If LCase(Right(file.Name, 4)) = ".pdf" Then
            ' Remove extension from filename
            fileName = Left(file.Name, Len(file.Name) - 4)
            
            ' Extract drawing number
            drawingNumber = ExtractDrawingNumber(fileName)
            
            ' Add to dictionary if not empty and not already present
            If Len(drawingNumber) > 0 Then
                pdfPath = file.Path
                If Not dict.Exists(drawingNumber) Then
                    dict.Add drawingNumber, pdfPath
                    Call LogDebug("Found drawing: " & drawingNumber & " at " & pdfPath)
                Else
                    Call LogDebug("Skipped duplicate drawing: " & drawingNumber & " at " & pdfPath)
                End If
            End If
        End If
    Next file
    
    ' Convert dictionary to collection
    For Each key In dict.Keys
        ReDim drawingInfo(1 To 2)
        drawingInfo(1) = CStr(key)
        drawingInfo(2) = dict(key)
        results.Add drawingInfo
    Next key
    
    Call LogInfo("Scan completed with " & results.Count & " unique drawing entries", "ScanNetworkPath")
    Set ScanNetworkPath = results
    Set fso = Nothing
    Set dict = Nothing
    Exit Function
    
ErrorHandler:
    Call LogError("ScanNetworkPath failed for path: " & networkPath & ", Error: " & Err.Description)
    Set ScanNetworkPath = results
    Set fso = Nothing
    Set dict = Nothing
End Function

'/**
' * Deduplicates a collection of drawing data by drawing number
' * 
' * Removes duplicate entries based on drawing number, keeping only the first
' * occurrence of each unique drawing number. This function serves as a generic
' * deduplication tool that can be applied to any collection of drawing data.
' * 
' * Returns a new Collection with deduplicated [drawingNumber, pdfPath] pairs.
' * 
' * @param {Collection} drawingData - Collection of [drawingNumber, pdfPath] arrays
' * @return {Collection} Deduplicated collection with unique drawing numbers
' */
Function DeduplicateDrawings(drawingData As Collection) As Collection
    Dim dict As Object
    Dim results As Collection
    Dim i As Long
    Dim drawingInfo As Variant
    Dim drawingNumber As String
    Dim pdfPath As String
    Dim key As Variant
    Dim inputCount As Long
    
    On Error GoTo ErrorHandler
    
    inputCount = drawingData.Count
    Call LogInfo("Starting deduplication of " & inputCount & " drawing entries", "DeduplicateDrawings")
    
    ' Create Dictionary for tracking unique drawing numbers
    Set dict = CreateObject("Scripting.Dictionary")
    Set results = New Collection
    
    ' Process each drawing entry
    For i = 1 To inputCount
        drawingInfo = drawingData(i)
        drawingNumber = drawingInfo(1)
        pdfPath = drawingInfo(2)
        
        ' Add to dictionary if not already present
        If Not dict.Exists(drawingNumber) Then
            dict.Add drawingNumber, pdfPath
            ' Create array and add to results collection
            ReDim drawingInfo(1 To 2)
            drawingInfo(1) = drawingNumber
            drawingInfo(2) = pdfPath
            results.Add drawingInfo
            Call LogDebug("Kept drawing: " & drawingNumber)
        Else
            Call LogDebug("Removed duplicate drawing: " & drawingNumber)
        End If
    Next i
    
    Call LogInfo("Deduplication completed: " & inputCount & " entries reduced to " & results.Count & " unique entries", "DeduplicateDrawings")
    Set DeduplicateDrawings = results
    Set dict = Nothing
    Exit Function
    
ErrorHandler:
    Call LogError("DeduplicateDrawings failed: " & Err.Description)
    Set DeduplicateDrawings = drawingData
    Set dict = Nothing
End Function

'/**
' * Sorts a collection of drawing data by drawing numbers using multi-field rules
' * 
' * Converts Collection to arrays, sorts by drawing number fields (left-to-right priority),
' * and returns a new Collection with sorted data. Input data should be deduplicated
' * for optimal results (see DeduplicateDrawings function).
' * 
' * @param {Collection} drawingData - Collection of [drawingNumber, pdfPath] arrays (should be deduplicated)
' * @return {Collection} Sorted collection of [drawingNumber, pdfPath] arrays
' */
Function SortDrawingsByMultipleFields(drawingData As Collection) As Collection
    Dim i As Long
    Dim drawingNumbers() As String
    Dim pdfPaths() As String
    Dim count As Long
    Dim drawingInfo As Variant
    Dim tempDrawing As String
    Dim tempPath As String
    Dim tempFields As Variant
    Dim j As Long
    Dim compareResult As Integer
    Dim sortedCollection As Collection
    Dim swapped As Boolean
    Dim parsedFieldsArray() As Variant
    
    On Error GoTo ErrorHandler
    
    count = drawingData.Count
    Call LogInfo("Starting to sort " & count & " drawing entries by multi-field rules (pre-parsing)", "SortDrawingsByMultipleFields")
    
    ' Convert collection to arrays for sorting
    ReDim drawingNumbers(0 To count - 1)
    ReDim pdfPaths(0 To count - 1)
    ReDim parsedFieldsArray(0 To count - 1)
    
    For i = 1 To count
        drawingInfo = drawingData(i)
        drawingNumbers(i - 1) = drawingInfo(1)
        pdfPaths(i - 1) = drawingInfo(2)
    Next i
    
    ' Pre-parse all drawing number fields
    For i = 0 To count - 1
        parsedFieldsArray(i) = ParseFields(CStr(drawingNumbers(i)), "-")
    Next i
    
    Call LogInfo("Drawing field parsing completed, starting bubble sort", "SortDrawingsByMultipleFields")
    
    ' Bubble sort with pre-parsed fields (no repeated parsing)
    For i = 0 To count - 2
        swapped = False
        
        For j = 0 To count - i - 2
            ' Compare using pre-parsed fields
            compareResult = CompareFields(parsedFieldsArray(j), parsedFieldsArray(j + 1))
            
            If compareResult > 0 Then
                ' Swap drawing number, path, and parsed fields
                tempDrawing = drawingNumbers(j)
                tempPath = pdfPaths(j)
                tempFields = parsedFieldsArray(j)
                
                drawingNumbers(j) = drawingNumbers(j + 1)
                pdfPaths(j) = pdfPaths(j + 1)
                parsedFieldsArray(j) = parsedFieldsArray(j + 1)
                
                drawingNumbers(j + 1) = tempDrawing
                pdfPaths(j + 1) = tempPath
                parsedFieldsArray(j + 1) = tempFields
                swapped = True
            End If
        Next j
        
        If Not swapped Then
            Exit For
        End If
    Next i
    
    ' Convert sorted arrays back to collection
    Set sortedCollection = New Collection
    For i = 0 To count - 1
        Dim sortedInfo As Variant
        ReDim sortedInfo(1 To 2)
        sortedInfo(1) = drawingNumbers(i)
        sortedInfo(2) = pdfPaths(i)
        sortedCollection.Add sortedInfo
    Next i
    
    Call LogInfo("Drawing entries sorted successfully", "SortDrawingsByMultipleFields")
    Set SortDrawingsByMultipleFields = sortedCollection
    Exit Function
    
ErrorHandler:
    Call LogError("SortDrawingsByMultipleFields failed: " & Err.Description)
    Set SortDrawingsByMultipleFields = drawingData
End Function

'/**
' * Fills the worksheet with drawing numbers in a two-column layout with hyperlinks
' * Also adds data validation dropdowns in adjacent columns
' * 
' * Layout:
' * - Each page has 44 rows of content (rows 4-47 for page 1, rows 48-91 for page 2, etc.)
' * - Column A: drawing numbers 1-44 on page 1, 45-88 on page 2, etc.
' * - Columns B, C: data validation dropdowns (options from PO Status sheet H1:H2)
' * - Column E: drawing numbers 45-88 on page 1, 89-132 on page 2, etc.
' * - Columns F, G: data validation dropdowns (options from PO Status sheet H1:H2)
' * - All drawing number entries are created as hyperlinks to the corresponding PDF files
' * 
' * @param {Collection} drawingData - Collection of [drawingNumber, pdfPath] arrays
' * @return {Boolean} True if successful, False otherwise
' */
Function FillExcelColumn(drawingData As Collection) As Boolean
    Dim ws As Worksheet
    Dim poStatusSheet As Worksheet
    Dim targetCell As Range
    Dim validationCell As Range
    Dim totalCount As Long
    Dim rowsPerPage As Long
    Dim dataIndex As Long
    Dim pageIndex As Long
    Dim colIndex As Long
    Dim startRow As Long
    Dim endRow As Long
    Dim currentRow As Long
    Dim colLetter As String
    Dim drawingInfo As Variant
    Dim drawingNumber As String
    Dim pdfPath As String
    Dim validationValue As String
    
    On Error GoTo ErrorHandler
    
    ' Get the active worksheet
    Set ws = ActiveSheet
    
    ' Try to get PO Status sheet for validation options
    On Error Resume Next
    Set poStatusSheet = ws.Parent.Sheets("PO Status")
    On Error GoTo ErrorHandler
    
    Call LogInfo("Filling data in worksheet: " & ws.Name & " with hyperlinks and validations", "FillExcelColumn")
    
    totalCount = drawingData.Count
    rowsPerPage = 44
    dataIndex = 1
    pageIndex = 0        ' Page index starts at 0
    colIndex = 1         ' Column index: 1 for A, 2 for E
    
    ' Loop to fill all data
    While dataIndex <= totalCount
        ' Calculate start and end rows for current page/column
        If colIndex = 1 Then        ' Column A (with B, C validations)
            startRow = 4 + pageIndex * rowsPerPage
            endRow = startRow + rowsPerPage - 1
            colLetter = "A"
        Else                         ' Column E (with F, G validations)
            startRow = 4 + pageIndex * rowsPerPage
            endRow = startRow + rowsPerPage - 1
            colLetter = "E"
        End If
        
        ' Fill current column
        For currentRow = startRow To endRow
            If dataIndex <= totalCount Then
                ' Get drawing data
                drawingInfo = drawingData(dataIndex)
                drawingNumber = drawingInfo(1)
                pdfPath = drawingInfo(2)
                
                Set targetCell = ws.Range(colLetter & currentRow)
                targetCell.Value = drawingNumber
                
                ' Create hyperlink to PDF
                On Error Resume Next
                ws.Hyperlinks.Add Anchor:=targetCell, Address:=pdfPath, TextToDisplay:=drawingNumber
                On Error GoTo ErrorHandler
                
                ' Add data validation to adjacent columns
                If colIndex = 1 Then
                    ' Add validation to columns B and C
                    Call AddValidationToCell(ws, poStatusSheet, "B" & currentRow)
                    Call AddValidationToCell(ws, poStatusSheet, "C" & currentRow)
                Else
                    ' Add validation to columns F and G
                    Call AddValidationToCell(ws, poStatusSheet, "F" & currentRow)
                    Call AddValidationToCell(ws, poStatusSheet, "G" & currentRow)
                End If
                
                dataIndex = dataIndex + 1
            End If
        Next currentRow
        
        ' Move to next column or page
        If colIndex = 1 Then
            colIndex = 2
        Else
            colIndex = 1
            pageIndex = pageIndex + 1
        End If
    Wend
    
    Call LogInfo("Successfully filled " & totalCount & " drawing numbers with hyperlinks and validations", "FillExcelColumn")
    FillExcelColumn = True
    Exit Function
    
ErrorHandler:
    Call LogError("FillExcelColumn failed: " & Err.Description)
    FillExcelColumn = False
End Function

'/**
' * Helper function to add data validation dropdown to a cell
' * 
' * Retrieves validation options from PO Status sheet cells H1 and H2
' * 
' * @param {Worksheet} ws - The worksheet to add validation to
' * @param {Worksheet} poStatusSheet - The PO Status sheet (may be Nothing)
' * @param {String} cellAddress - The cell address (e.g., "B4")
' */
Sub AddValidationToCell(ws As Worksheet, poStatusSheet As Worksheet, cellAddress As String)
    Dim validationCell As Range
    Dim h1Value As String
    Dim h2Value As String
    Dim validationFormula As String
    
    On Error GoTo ErrorHandler
    
    Set validationCell = ws.Range(cellAddress)
    
    ' Clear any existing validation
    On Error Resume Next
    validationCell.Validation.Delete
    On Error GoTo ErrorHandler
    
    ' Try to get values from PO Status sheet
    If poStatusSheet Is Nothing Then
        Call LogDebug("PO Status sheet not found, skipping validation for " & cellAddress)
        Exit Sub
    End If
    
    On Error Resume Next
    h1Value = poStatusSheet.Range("H1").Value
    h2Value = poStatusSheet.Range("H2").Value
    On Error GoTo ErrorHandler
    
    ' Check if we have any values
    If Len(h1Value) = 0 And Len(h2Value) = 0 Then
        Call LogDebug("PO Status sheet H1 and H2 are empty")
        Exit Sub
    End If
    
    ' Build validation formula using sheet reference
    validationFormula = "='" & poStatusSheet.Name & "'!H1:H2"
    
    ' Add data validation with the range formula
    On Error Resume Next
    With validationCell.Validation
        .Add Type:=xlValidateList, AlertStyle:=xlValidAlertStop, Formula1:=validationFormula
        .IgnoreBlank = True
        .InCellDropdown = True
    End With
    On Error GoTo ErrorHandler
    
    Call LogDebug("Added validation to " & cellAddress & " with formula: " & validationFormula)
    Exit Sub
    
ErrorHandler:
    Call LogError("AddValidationToCell failed for " & cellAddress & ": " & Err.Description)
End Sub

'/**
' * Main function: Scans network path for unique drawings, sorts by multi-field rules, and populates Excel worksheet
' * 
' * Process:
' * 1. Scans network path and automatically deduplicates drawing numbers (first occurrence kept)
' * 2. Sorts deduplicated entries by multi-field rules (left-to-right priority on "-" delimited fields)
' * 3. Fills Excel worksheet with sorted, unique drawing numbers and hyperlinks
' * 
' * @param {String} networkPath - The network path to scan
' * @return {Boolean} True if successful, False otherwise
' */
Function ScanAndFillDrawings(networkPath As String) As Boolean
    Dim drawingNumbers As Collection
    Dim sortedDrawings As Collection
    Dim fillResult As Boolean
    
    On Error GoTo ErrorHandler
    
    Call StartLogBlock()
    Call LogInfo("Starting scan, deduplicate, and fill process for path: " & networkPath, "ScanAndFillDrawings")
    
    ' Scan network path (deduplication happens during scan)
    Set drawingNumbers = ScanNetworkPath(networkPath)
    Call LogInfo("Scan complete with " & drawingNumbers.Count & " unique drawing entries", "ScanAndFillDrawings")
    
    ' Sort drawing numbers by multi-field rules
    Call LogInfo("Sorting " & drawingNumbers.Count & " drawing entries by multi-field rules", "ScanAndFillDrawings")
    Set sortedDrawings = SortDrawingsByMultipleFields(drawingNumbers)
    
    ' Fill Excel column with sorted data
    fillResult = FillExcelColumn(sortedDrawings)
    
    Call FlushLogBlock()
    
    ScanAndFillDrawings = fillResult
    Exit Function
    
ErrorHandler:
    Call LogError("ScanAndFillDrawings failed: " & Err.Description)
    Call FlushLogBlock()
    ScanAndFillDrawings = False
End Function

'/**
' * Public Interface: Scan and Fill Drawing Numbers
' * 
' * This subroutine is designed to be called from Excel buttons or other UI elements.
' * Prompts the user for a network path, scans for PDF files, extracts drawing numbers,
' * and fills them into column A of the current worksheet starting from row 4.
' * 
' * Usage: Bind this subroutine directly to a button in Excel
' */
Sub ScanDrawingsUI()
    Dim customPath As String
    Dim result As Boolean
    Dim msg As String
    
    ' Prompt user for network path
    customPath = InputBox("Enter the network path to scan:", "Scan Path Input")
    
    ' If user cancelled, exit
    If Len(customPath) = 0 Then
        MsgBox "Operation cancelled.", vbInformation, "Cancelled"
        Exit Sub
    End If
    
    Call StartLogBlock()
    Call LogInfo("=== ScanDrawingsUI: Starting Scan ===", "ScanDrawingsUI")
    Call LogInfo("Network path: " & customPath, "ScanDrawingsUI")
    
    ' Execute scan and fill
    result = ScanAndFillDrawings(customPath)
    
    Call FlushLogBlock()
End Sub

'/**
' * Test function to validate scanning, deduplication, and sorting workflow
' * 
' * This function tests the complete workflow by creating a mock collection
' * with duplicate drawing numbers and verifying that:
' * 1. DeduplicateDrawings removes duplicates
' * 2. SortDrawingsByMultipleFields sorts correctly
' * 3. The final result contains only unique, sorted entries
' * 
' * Usage: Call TestScanDrawingsWorkflow() from Excel VBA editor
' */
Sub TestScanDrawingsWorkflow()
    Dim testData As Collection
    Dim deduplicatedData As Collection
    Dim sortedData As Collection
    Dim i As Long
    Dim msg As String
    Dim drawingInfo As Variant
    
    On Error GoTo ErrorHandler
    
    Call StartLogBlock()
    Call LogInfo("=== TestScanDrawingsWorkflow: Starting Workflow Test ===", "TestScanDrawingsWorkflow")
    
    ' Create test collection with duplicate drawing numbers
    Set testData = New Collection
    Dim testPairs() As Variant
    testPairs = Array( _
        Array("RT-88000-0001", "\\path\to\RT-88000-0001.pdf"), _
        Array("RT-87900-0408", "\\path\to\RT-87900-0408.pdf"), _
        Array("RT-88000-0001", "\\path\to\RT-88000-0001-duplicate.pdf"), _
        Array("RT-87900-0409", "\\path\to\RT-87900-0409.pdf"), _
        Array("RT-87900-0408", "\\path\to\RT-87900-0408-duplicate.pdf"), _
        Array("RT-88000-0002", "\\path\to\RT-88000-0002.pdf") _
    )
    
    ' Populate test collection
    For i = LBound(testPairs) To UBound(testPairs)
        Dim pair As Variant
        pair = testPairs(i)
        Dim info As Variant
        ReDim info(1 To 2)
        info(1) = pair(0)
        info(2) = pair(1)
        testData.Add info
    Next i
    
    Call LogInfo("Test data created with " & testData.Count & " entries (including duplicates)", "TestScanDrawingsWorkflow")
    Call LogInfo("Test entries:", "TestScanDrawingsWorkflow")
    For i = 1 To testData.Count
        drawingInfo = testData(i)
        Call LogInfo("  [" & i & "] " & drawingInfo(1), "TestScanDrawingsWorkflow")
    Next i
    
    ' Test deduplication
    Call LogInfo("Testing DeduplicateDrawings function...", "TestScanDrawingsWorkflow")
    Set deduplicatedData = DeduplicateDrawings(testData)
    Call LogInfo("Deduplication result: " & testData.Count & " entries reduced to " & deduplicatedData.Count & " unique entries", "TestScanDrawingsWorkflow")
    Call LogInfo("Deduplicated entries:", "TestScanDrawingsWorkflow")
    For i = 1 To deduplicatedData.Count
        drawingInfo = deduplicatedData(i)
        Call LogInfo("  [" & i & "] " & drawingInfo(1), "TestScanDrawingsWorkflow")
    Next i
    
    ' Test sorting
    Call LogInfo("Testing SortDrawingsByMultipleFields function...", "TestScanDrawingsWorkflow")
    Set sortedData = SortDrawingsByMultipleFields(deduplicatedData)
    Call LogInfo("Sorted entries:", "TestScanDrawingsWorkflow")
    For i = 1 To sortedData.Count
        drawingInfo = sortedData(i)
        Call LogInfo("  [" & i & "] " & drawingInfo(1), "TestScanDrawingsWorkflow")
    Next i
    
    ' Build result message
    msg = "Workflow Test Completed Successfully!" & vbCrLf & vbCrLf
    msg = msg & "Input: " & testData.Count & " entries (with duplicates)" & vbCrLf
    msg = msg & "After Deduplication: " & deduplicatedData.Count & " unique entries" & vbCrLf
    msg = msg & "After Sorting: " & sortedData.Count & " entries (sorted by multi-field rules)" & vbCrLf & vbCrLf
    msg = msg & "Final sorted order:" & vbCrLf
    For i = 1 To sortedData.Count
        drawingInfo = sortedData(i)
        msg = msg & drawingInfo(1) & vbCrLf
    Next i
    
    MsgBox msg, vbInformation, "Scan Workflow Test Complete"
    
    Call LogInfo("=== TestScanDrawingsWorkflow: Test Complete ===", "TestScanDrawingsWorkflow")
    Call FlushLogBlock()
    Exit Sub
    
ErrorHandler:
    Call LogError("TestScanDrawingsWorkflow failed: " & Err.Description)
    Call FlushLogBlock()
    MsgBox "Test failed: " & Err.Description, vbCritical, "Error"
End Sub
