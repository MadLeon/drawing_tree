'/**
' * Module: mod_database_sync
' * Purpose: Database synchronization for Manufacturing Process
' *
' * This module handles:
' * 1. Reading form data from Excel worksheet
' * 2. Extracting and validating key information
' * 3. Writing process template data to SQLite database
' * 4. Error handling and logging
' */

Option Explicit

' ============================================================================
' CONSTANTS
' ============================================================================

Private Const DB_PATH As String = "C:\Users\ee\job_management\data\record.db"

' Column references
Private Const COL_PO_NUMBER As Long = 2          ' B7
Private Const COL_OE_NUMBER As Long = 14         ' N6
Private Const COL_JOB_NUMBER As Long = 17        ' Q6
Private Const COL_LINE_NUMBER As Long = 6        ' F7
Private Const COL_DRAWING_NUMBER As Long = 10   ' J7
Private Const COL_REVISION As Long = 18          ' R7
Private Const COL_DESCRIPTION As Long = 8       ' H8
Private Const COL_DRAWING_RELEASE_DATE As Long = 2  ' B8
Private Const COL_DELIVERY_DATE As Long = 4     ' D9
Private Const COL_QUANTITY As Long = 17         ' Q9

' Row references
Private Const ROW_HEADER_PO As Long = 7         ' B7
Private Const ROW_HEADER_OE As Long = 6         ' N6
Private Const ROW_HEADER_JOB As Long = 6        ' Q6
Private Const ROW_HEADER_DWG As Long = 8        ' H8
Private Const STEP_START_ROW As Long = 11       ' F11 - start of steps
Private Const STEP_END_ROW As Long = 39         ' F39 - end of steps

' ============================================================================
' DATA EXTRACTION FUNCTIONS
' ============================================================================

Private Sub ExtractHeaderData(ByRef formData As Object)
    '/**
    ' * Extract header information from the worksheet
    ' * Populates formData Dictionary with key fields
    ' */
    
    Dim ws As Worksheet
    Set ws = Sheets(MP_SHEET_NAME)
    
    ' Read key cells (as per user specification)
    Dim poNumber As String
    Dim oeNumber As String
    Dim jobNumber As String
    Dim lineNumber As String
    Dim drawingNumber As String
    Dim revision As String
    Dim description As String
    Dim drawingReleaseDate As String
    Dim deliveryDate As String
    Dim quantity As String
    
    On Error Resume Next
    
    ' B7: PO number
    poNumber = CStr(ws.Cells(ROW_HEADER_PO, COL_PO_NUMBER).value)
    If poNumber = "" Then poNumber = "N/A"
    
    ' N6: OE number
    oeNumber = CStr(ws.Cells(ROW_HEADER_OE, COL_OE_NUMBER).value)
    If oeNumber = "" Then oeNumber = "N/A"
    
    ' Q6: Job number
    jobNumber = CStr(ws.Cells(ROW_HEADER_JOB, COL_JOB_NUMBER).value)
    If jobNumber = "" Then jobNumber = "N/A"
    
    ' F7: Line number
    lineNumber = CStr(ws.Cells(ROW_HEADER_PO, COL_LINE_NUMBER).value)
    If lineNumber = "" Then lineNumber = "N/A"
    
    ' J7: Drawing number
    drawingNumber = CStr(ws.Cells(ROW_HEADER_PO, COL_DRAWING_NUMBER).value)
    If drawingNumber = "" Then drawingNumber = "N/A"
    
    ' R7: Revision
    revision = CStr(ws.Cells(ROW_HEADER_PO, COL_REVISION).value)
    If revision = "" Then revision = "-"
    
    ' H8: Description
    description = CStr(ws.Cells(ROW_HEADER_DWG, COL_DESCRIPTION).value)
    If description = "" Then description = "N/A"
    
    ' B8: Drawing Release Date
    drawingReleaseDate = CStr(ws.Cells(ROW_HEADER_DWG, COL_DRAWING_RELEASE_DATE).value)
    If drawingReleaseDate = "" Then drawingReleaseDate = "N/A"
    
    ' D9: Delivery Required Date
    deliveryDate = CStr(ws.Cells(ROW_HEADER_DWG + 1, COL_DELIVERY_DATE).value)
    If deliveryDate = "" Then deliveryDate = "N/A"
    
    ' Q9: Quantity
    quantity = CStr(ws.Cells(ROW_HEADER_DWG + 1, COL_QUANTITY).value)
    If quantity = "" Then quantity = "N/A"
    
    On Error GoTo 0
    
    ' Store in Dictionary
    formData.Add "po_number", poNumber
    formData.Add "oe_number", oeNumber
    formData.Add "job_number", jobNumber
    formData.Add "line_number", lineNumber
    formData.Add "drawing_number", drawingNumber
    formData.Add "revision", revision
    formData.Add "description", description
    formData.Add "drawing_release_date", drawingReleaseDate
    formData.Add "delivery_date", deliveryDate
    formData.Add "quantity", quantity
    
End Sub

' ============================================================================
' LOGGING FUNCTIONS
' ============================================================================

Private Sub LogExtractedData(ByRef formData As Object)
    '/**
    ' * Log all extracted header data for verification
    ' */
    
    LogDebug "--- Extracted Header Data ---"
    LogInfo "PO (B7): " & formData("po_number")
    LogInfo "OE (N6): " & formData("oe_number")
    LogInfo "Job (Q6): " & formData("job_number")
    LogInfo "Line (F7): " & formData("line_number")
    LogInfo "DWG (J7): " & formData("drawing_number")
    LogInfo "REV (R7): " & formData("revision")
    LogInfo "DESC (H8): " & formData("description")
    LogInfo "DRD (B8): " & formData("drawing_release_date")
    LogInfo "DUE (D9): " & formData("delivery_date")
    LogInfo "QTY (Q9): " & formData("quantity")
    LogDebug "--- End Header Data ---"
    
End Sub

Private Sub LogStepData()
    '/**
    ' * Log step data from rows F11-F39
    ' */
    
    Dim ws As Worksheet
    Set ws = Sheets(MP_SHEET_NAME)
    
    Dim i As Long
    Dim stepCount As Long
    stepCount = 0
    
    LogDebug "--- Step Data (F11-F39) ---"
    
    On Error Resume Next
    
    For i = STEP_START_ROW To STEP_END_ROW
        Dim stepContent As String
        stepContent = CStr(ws.Cells(i, COL_LINE_NUMBER).value)
        
        If stepContent <> "" And stepContent <> "0" Then
            stepCount = stepCount + 1
            LogInfo "Step " & stepCount & " (Row " & i & "): " & stepContent
        End If
    Next i
    
    On Error GoTo 0
    
    LogInfo "Total steps found: " & stepCount
    LogDebug "--- End Step Data ---"
    
End Sub

' ============================================================================
' DATABASE INTEGRATION - Main Entry Point
' ============================================================================

Public Sub SaveProcessSteps_ToDatabase_Main()
    '/**
    ' * Main entry point for saving process steps to database
    ' *
    ' * Workflow:
    ' * 1. Read drawing_number (J7), revision (R7), description (H8)
    ' * 2. Validate drawing_number is not empty
    ' * 3. Get or create part_id via INSERT OR IGNORE
    ' * 4. Loop through steps (F11-F39) and insert process_template records
    ' * 5. Log all operations
    ' */
    
    On Error GoTo ErrorHandler
    
    StartLogBlock
    LogInfo "========== SaveProcessSteps_ToDatabase_Main START =========="
    
    Dim ws As Worksheet
    Set ws = Sheets(MP_SHEET_NAME)
    
    ' ========== Step 0: Initialize SQLite database ==========
    Dim dbPath As String
    dbPath = DB_PATH
    
    If Not InitializeSQLite(dbPath) Then
        LogError "Failed to initialize SQLite database: " & dbPath
        FlushLogBlock
        Exit Sub
    End If
    
    LogDebug "SQLite database initialized: " & dbPath
    
    ' ========== Step 1: Read header data (reading only once) ==========
    Dim drawingNumber As String
    Dim revision As String
    Dim partDescription As String
    
    On Error Resume Next
    drawingNumber = CStr(ws.Cells(ROW_HEADER_PO, COL_DRAWING_NUMBER).value)
    revision = CStr(ws.Cells(ROW_HEADER_PO, COL_REVISION).value)
    partDescription = CStr(ws.Cells(ROW_HEADER_DWG, COL_DESCRIPTION).value)
    On Error GoTo ErrorHandler
    
    ' ========== Step 2: Validate drawing_number ==========
    If drawingNumber = "" Or drawingNumber = "0" Then
        LogError "Drawing number (J7) is empty - cannot proceed"
        CloseSQLite
        FlushLogBlock
        Exit Sub
    End If
    
    ' Revision: use value from R7 if present, otherwise use database default '-'
    If revision = "" Then
        revision = "-"
    End If
    
    LogDebug "Drawing: " & drawingNumber & " | Revision: " & revision & " | Description: " & partDescription
    
    ' ========== Step 3: Get or create part ==========
    Dim partID As Long
    partID = GetOrCreatePartID(drawingNumber, revision, partDescription)
    
    If partID <= 0 Then
        LogError "Failed to get or create part record - cannot proceed"
        CloseSQLite
        FlushLogBlock
        Exit Sub
    End If
    
    LogInfo "Part ID obtained: " & partID
    
    ' ========== Step 4: Insert process_template records ==========
    Call InsertProcessTemplateRecords(ws, partID)
    
    ' ========== Step 5: Close database ==========
    CloseSQLite
    
    LogInfo "========== SaveProcessSteps_ToDatabase_Main END =========="
    FlushLogBlock
    
    Exit Sub
    
ErrorHandler:
    LogError "SaveProcessSteps_ToDatabase_Main Error: " & Err.description
    CloseSQLite
    FlushLogBlock
End Sub

' ============================================================================
' DATABASE FUNCTIONS - Part Management
' ============================================================================

Private Function GetOrCreatePartID(drawingNum As String, rev As String, partDesc As String) As Long
    '/**
    ' * Get or create part record
    ' * Uses INSERT OR IGNORE to handle duplicates
    ' * Returns part.id, or 0 if failed
    ' */
    
    On Error GoTo ErrorHandler
    
    Dim sql As String
    Dim result As Variant
    Dim partID As Long
    
    LogDebug "GetOrCreatePartID: Processing " & drawingNum & " / " & rev
    
    ' Escape single quotes in SQL strings
    Dim safeDwgNum As String
    Dim safeRev As String
    Dim safeDesc As String
    
    safeDwgNum = Replace(drawingNum, "'", "''")
    safeRev = Replace(rev, "'", "''")
    safeDesc = Replace(partDesc, "'", "''")
    
    ' Step 1: INSERT OR IGNORE into part table
    ' Always include revision: use value from R7 if present, otherwise '-'
    sql = "INSERT OR IGNORE INTO part (drawing_number, revision, description) VALUES ('" & _
          safeDwgNum & "', '" & safeRev & "', '" & safeDesc & "')"
    
    LogDebug "Executing INSERT OR IGNORE: drawing_number=" & drawingNum & ", revision=" & rev
    
    If Not ExecuteNonQuery(sql) Then
        LogError "INSERT OR IGNORE failed for part: " & drawingNum & " / " & rev
        GetOrCreatePartID = 0
        Exit Function
    End If
    
    ' Step 2: SELECT part.id
    ' Query by both drawing_number AND revision to handle multiple revisions of same part
    sql = "SELECT id FROM part WHERE drawing_number = '" & safeDwgNum & "' AND revision = '" & safeRev & "' LIMIT 1"
    
    On Error Resume Next
    result = ExecuteQuery(sql)
    On Error GoTo ErrorHandler
    
    If IsNull(result) Or TypeName(result) = "Empty" Then
        LogError "SELECT failed to find part after INSERT: " & drawingNum & " / " & rev
        GetOrCreatePartID = 0
        Exit Function
    End If
    
    If Not IsArray(result) Then
        LogError "SELECT did not return an array: " & drawingNum & " / " & rev
        GetOrCreatePartID = 0
        Exit Function
    End If
    
    ' Check that exactly one record was found (using UBound to get row count)
    Dim recordCount As Long
    recordCount = UBound(result, 1) + 1  ' Convert 0-based index to count
    
    If recordCount > 1 Then
        LogError "ERROR: Multiple parts found for drawing " & drawingNum & " / " & rev & " - Expected exactly 1, found " & recordCount
        GetOrCreatePartID = 0
        Exit Function
    End If
    
    ' Extract part id from first row, first column
    partID = CLng(result(0, 0))
    LogDebug "Part ID found/created: " & partID
    GetOrCreatePartID = partID
    
    Exit Function
    
ErrorHandler:
    LogError "GetOrCreatePartID Error: " & Err.description
    GetOrCreatePartID = 0
End Function

' ============================================================================
' DATABASE FUNCTIONS - Process Template Insertion
' ============================================================================

Private Sub InsertProcessTemplateRecords(ws As Worksheet, partID As Long)
    '/**
    ' * Loop through rows 11-39 and insert process_template records
    ' * Only processes rows where D column (shop_code) has content
    ' */
    
    On Error GoTo ErrorHandler
    
    Dim i As Long
    Dim shopCode As String
    Dim rowNumber As String
    Dim stepDescription As String
    Dim remark As String
    Dim sql As String
    Dim insertCount As Long
    Dim errorCount As Long
    
    insertCount = 0
    errorCount = 0
    
    LogDebug "Starting process_template insertion loop"
    
    ' ========== Loop through steps (rows 11-39) ==========
    For i = STEP_START_ROW To STEP_END_ROW
        
        On Error Resume Next
        
        ' Read D column (shop_code)
        shopCode = CStr(ws.Cells(i, 4).value)  ' Column D
        shopCode = Trim(shopCode)
        
        ' Skip row if shop_code is empty
        If shopCode = "" Or shopCode = "0" Then
            On Error GoTo ErrorHandler
            GoTo NextIteration
        End If
        
        ' Read other columns for this row
        rowNumber = CStr(ws.Cells(i, 5).value)      ' Column E
        stepDescription = CStr(ws.Cells(i, 6).value) ' Column F
        remark = CStr(ws.Cells(i, 17).value)        ' Column Q
        
        ' Clean up values
        rowNumber = Trim(rowNumber)
        stepDescription = Trim(stepDescription)
        remark = Trim(remark)
        
        On Error GoTo ErrorHandler
        
        LogDebug "Row " & i & ": shop_code=" & shopCode & ", row_number=" & rowNumber
        
        ' Escape single quotes for SQL
        Dim safeShopCode As String
        Dim safeDescription As String
        Dim safeRemark As String
        
        safeShopCode = Replace(shopCode, "'", "''")
        safeDescription = Replace(stepDescription, "'", "''")
        safeRemark = Replace(remark, "'", "''")
        
        ' Build and execute INSERT statement
        sql = "INSERT INTO process_template (part_id, row_number, shop_code, description, remark) " & _
              "VALUES (" & partID & ", " & rowNumber & ", '" & safeShopCode & "', '" & safeDescription & "', '" & safeRemark & "')"
        
        If ExecuteNonQuery(sql) Then
            insertCount = insertCount + 1
            LogDebug "Inserted process_template row " & i
        Else
            errorCount = errorCount + 1
            LogError "Failed to insert process_template for row " & i & ": " & shopCode
        End If
        
NextIteration:
    Next i
    
    LogInfo "Process template insertion complete - Inserted: " & insertCount & ", Errors: " & errorCount
    
    Exit Sub
    
ErrorHandler:
    LogError "InsertProcessTemplateRecords Error: " & Err.description
End Sub