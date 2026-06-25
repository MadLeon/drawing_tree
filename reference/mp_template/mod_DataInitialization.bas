'/**
' * Data Initialization Module (mod_DataInitialization.bas)
' * 
' * Handles data loading and structure initialization on workbook startup
' * This module should be called from ThisWorkbook_Open event
' */

Option Explicit

' ============================================================================
' CONSTANTS
' ============================================================================

Private Const DATA_SHEET_NAME As String = "data"
Private Const DATA_START_ROW As Long = 16
Private Const COL_CODE As Long = 1        ' Column A: code (P, FI, RT, etc.)
Private Const COL_TYPE As Long = 2        ' Column B: type
Private Const COL_LINK As Long = 3        ' Column C: link (Comb/Hint)
Private Const COL_PROCESS As Long = 4     ' Column D: process description

' ============================================================================
' PUBLIC FUNCTIONS - INITIALIZATION ENTRY POINT
' ============================================================================

' /**
'  * Main initialization function
'  * Call this from ThisWorkbook_Open event
'  * Sequence:
'  * 1. Initialize code-to-description mappings
'  * 2. Load data from data sheet
'  * 3. Initialize ProcessData structure
'  * 4. Log completion
'  */
Public Sub InitializeOnWorkbookOpen()
    On Error GoTo ErrorHandler
    
    ' Start logging
    Call lib_logger.StartLogBlock
    Call lib_logger.LogInfo("Workbook_Open: Initialization started")
    
    ' Step 1: Initialize code mappings
    Call InitializeCodeMappings
    
    ' Step 2: Initialize ProcessData structure
    Call InitializeProcessData
    
    ' Step 3: Load data from data sheet
    Call LoadDataSheet
    Call lib_logger.LogInfo("Data sheet loaded successfully")
    
    ' Step 4: Initialize U9/W9 dropdowns
    Call mod_DisplayDropdowns.InitializeDropdowns
    
    ' Step 5: Initialize Insert buttons
    Call mod_ButtonManager.InitializeInsertButtons
    
    ' Flush logs
    Call lib_logger.FlushLogBlock
    
    Exit Sub
    
ErrorHandler:
    Call lib_logger.LogError("InitializeOnWorkbookOpen failed: " & Err.Description)
    Call lib_logger.FlushLogBlock
    MsgBox "Initialization Error: " & Err.Description, vbCritical, "Workbook Startup Error"
End Sub

' ============================================================================
' PRIVATE FUNCTIONS - DATA LOADING
' ============================================================================

' /**
'  * Load data from data sheet into ProcessData structure
'  * 
'  * Algorithm:
'  * 1. Locate data sheet
'  * 2. Find the last edited row (last non-empty row from DATA_START_ROW onwards)
'  * 3. Read data from DATA_START_ROW to lastRow
'  * 4. For each row, extract: code, type, link, process
'  * 5. Add entry to ProcessData[code] collection
'  * 6. Log statistics
'  */
Private Sub LoadDataSheet()
    Dim ws As Worksheet
    Dim lastRow As Long
    Dim currentRow As Long
    Dim code As Variant
    Dim processType As String
    Dim link As String
    Dim process As String
    Dim entryCount As Long
    Dim codeCount As Object ' Dictionary to count entries per code
    Dim i As Long
    Dim uniqueLinks As Object
    Dim linkVal As Long
    
    ' Get data sheet
    On Error Resume Next
    Set ws = ThisWorkbook.Sheets(DATA_SHEET_NAME)
    On Error GoTo 0
    
    If ws Is Nothing Then
        Call lib_logger.LogError("Data sheet '" & DATA_SHEET_NAME & "' not found")
        Exit Sub
    End If
    
    ' Find last edited row (last non-empty row starting from DATA_START_ROW)
    lastRow = FindLastEditedRow(ws, DATA_START_ROW)
    
    If lastRow < DATA_START_ROW Then
        Call lib_logger.LogError("No data found in data sheet from row " & DATA_START_ROW)
        Exit Sub
    End If
    
    ' Initialize counter for statistics
    Set codeCount = CreateObject("Scripting.Dictionary")
    
    ' Load data into ProcessData
    entryCount = 0
    For currentRow = DATA_START_ROW To lastRow
        code = Trim(ws.Cells(currentRow, COL_CODE).value)
        processType = Trim(ws.Cells(currentRow, COL_TYPE).value)
        link = Trim(ws.Cells(currentRow, COL_LINK).value)
        process = Trim(ws.Cells(currentRow, COL_PROCESS).value)
        
        ' Skip empty rows (code is required)
        If code = "" Then
            GoTo NextRow
        End If
        
        ' Validate code
        If Not ProcessData.Exists(code) Then
            Call lib_logger.LogError("Invalid code '" & code & "' at row " & currentRow)
            GoTo NextRow
        End If
        
        ' Parse link field to extract related row number
        ' link format: "=ROW(D22)" -> extract 22, or "" if no link
        Dim relatedRow As Long
        relatedRow = ExtractRowNumberFromLink(link)
        
        ' Create entry dictionary
        Dim entry As Object
        Set entry = CreateObject("Scripting.Dictionary")
        entry.Add "code", code
        entry.Add "type", processType
        entry.Add "link", relatedRow
        entry.Add "process", process
        entry.Add "dataRow", currentRow
        
        ' Add to ProcessData[code] collection
        ProcessData(code).Add entry
        
        ' Update counter
        If codeCount.Exists(code) Then
            codeCount(code) = codeCount(code) + 1
        Else
            codeCount.Add code, 1
        End If
        
        entryCount = entryCount + 1
        
NextRow:
    Next currentRow
    
    ' Log statistics
    Call lib_logger.LogInfo("LoadDataSheet: Total entries loaded: " & entryCount)
    
    ' Count entries with link values
    Dim entriesWithLink As Long
    entriesWithLink = 0
    Set uniqueLinks = CreateObject("Scripting.Dictionary")
    
    For Each code In ProcessData.Keys
        For i = 1 To ProcessData(code).Count
            If CLng(ProcessData(code)(i)("link")) > 0 Then
                entriesWithLink = entriesWithLink + 1
                linkVal = CLng(ProcessData(code)(i)("link"))
                If Not uniqueLinks.Exists(linkVal) Then
                    uniqueLinks.Add linkVal, 1
                End If
            End If
        Next i
    Next code
    
    Call lib_logger.LogInfo("LoadDataSheet: Entries with link values: " & entriesWithLink)
    
End Sub

' ============================================================================
' PRIVATE FUNCTIONS - UTILITY
' ============================================================================

' /**
'  * Find the last edited row in a worksheet
'  * Starting from startRow, find the last non-empty row
'  * 
'  * @param ws: the worksheet to search
'  * @param startRow: the starting row
'  * @return: the row number of the last non-empty row (or startRow if empty)
'  */
Private Function FindLastEditedRow(ws As Worksheet, startRow As Long) As Long
    Dim lastRow As Long
    Dim searchRow As Long
    
    ' Use Cells.SpecialCells to find the last used cell
    On Error Resume Next
    lastRow = ws.Cells.SpecialCells(xlCellTypeLastCell).Row
    On Error GoTo 0
    
    If lastRow < startRow Then
        lastRow = startRow - 1
    End If
    
    FindLastEditedRow = lastRow
End Function

' /**
'  * Extract row number from link field
'  * C列的.value已经被Excel计算为数字，直接处理即可
'  * 
'  * @param linkField: the link field value (already calculated by Excel)
'  * @return: row number as Long (0 if empty or invalid)
'  */
Private Function ExtractRowNumberFromLink(linkField As String) As Long
    Dim link As String
    Dim result As Long
    
    link = Trim(linkField)
    
    ' If empty or None, return 0
    If link = "" Or link = "None" Then
        result = 0
        GoTo ExitFunction
    End If
    
    ' Try to convert to Long (handles numeric strings like "22")
    On Error Resume Next
    result = CLng(link)
    Dim errCode As Long
    errCode = Err.Number
    On Error GoTo 0
    
    If errCode <> 0 Then
        result = 0
    End If

ExitFunction:
    
    ExtractRowNumberFromLink = result
End Function
