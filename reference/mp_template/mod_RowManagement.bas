'/**
' * Row Management Module
' * Handles insert and remove row operations in the output area
' */

Option Explicit

' /**
'  * InsertRow - Insert a blank row in the output area
'  * 
'  * Logic:
'  * 1. Check if current cell is in output area (rows 11-39, columns A-N)
'  * 2. Check if last row (39) is empty
'  * 3. Find last non-empty row L in output area
'  * 4. Copy data from selected row S to row L (columns A-N)
'  * 5. Paste to S+1 (Ctrl+C/V behavior)
'  * 6. Clear row S (columns A-N) to create blank row
'  */
Public Sub InsertRow()
    On Error GoTo ErrorHandler
    
    Dim ws As Worksheet
    Dim selectedCell As Range
    Dim selectedRow As Long
    Dim lastContentRow As Long
    Dim sourceRange As Range
    Dim targetRange As Range
    Dim clearRange As Range
    
    ' Initialize log block
    Call StartLogBlock
    
    ' Get active worksheet (mp sheet)
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    Set selectedCell = Selection
    
    ' Validation 1: Check if selected cell is in output area
    selectedRow = selectedCell.Row
    If selectedRow < OUTPUT_START_ROW Or selectedRow > OUTPUT_END_ROW Then
        Call LogDebug("InsertRow: Selected cell is outside output area (rows " & OUTPUT_START_ROW & "-" & OUTPUT_END_ROW & ")")
        Call FlushLogBlock
        Exit Sub
    End If
    
    If selectedCell.Column < DATA_COLUMNS_START Or selectedCell.Column > DATA_COLUMNS_END Then
        Call LogDebug("InsertRow: Selected cell is outside data columns (A-N)")
        Call FlushLogBlock
        Exit Sub
    End If
    
    ' Validation 2: Check if last row is empty
    Set clearRange = ws.Range(ws.Cells(OUTPUT_END_ROW, DATA_COLUMNS_START), ws.Cells(OUTPUT_END_ROW, DATA_COLUMNS_END))
    If Not IsRowEmpty(clearRange) Then
        Call LogDebug("InsertRow: Last row (" & OUTPUT_END_ROW & ") is not empty. Cannot insert.")
        Call FlushLogBlock
        Exit Sub
    End If
    
    ' Find last non-empty row
    lastContentRow = FindLastContentRow(ws, OUTPUT_START_ROW, OUTPUT_END_ROW)
    
    ' If selected row is the last row, no need to insert
    If selectedRow > lastContentRow Then
        Call LogDebug("InsertRow: Selected row is already below all content. No action needed.")
        Call FlushLogBlock
        Exit Sub
    End If
    
    ' Copy data row by row from selected row to last content row (to preserve merged cells)
    ' Process from last row to first row to avoid overwriting source data
    Dim i As Long
    Application.DisplayAlerts = False
    
    For i = lastContentRow To selectedRow Step -1
        Set sourceRange = ws.Range(ws.Cells(i, DATA_COLUMNS_START), ws.Cells(i, DATA_COLUMNS_END))
        sourceRange.Copy
        Set targetRange = ws.Cells(i + 1, DATA_COLUMNS_START)
        targetRange.PasteSpecial Paste:=xlPasteAll
    Next i
    
    Application.DisplayAlerts = True
    Application.CutCopyMode = False
    
    ' Clear selected row (columns A-N) - only clear contents, preserve formatting
    Set clearRange = ws.Range(ws.Cells(selectedRow, DATA_COLUMNS_START), ws.Cells(selectedRow, DATA_COLUMNS_END))
    clearRange.ClearContents
    
    Call LogInfo("Row inserted at row " & selectedRow & ", content shifted down, last content now at row " & (lastContentRow + 1))
    Call FlushLogBlock
    
    Exit Sub
ErrorHandler:
    Application.DisplayAlerts = True
    Application.CutCopyMode = False
    Call LogError("InsertRow: " & Err.Description)
    Call FlushLogBlock
End Sub

' /**
'  * RemoveRow - Remove a row in the output area
'  * 
'  * Logic:
'  * 1. Check if current cell is in output area (rows 11-39, columns A-N)
'  * 2. Find last non-empty row L in output area
'  * 3. Copy data from S+1 to L (columns A-N)
'  * 4. Paste to S (Ctrl+C/V behavior)
'  * 5. Clear row L (columns A-N) to remove row
'  */
Public Sub RemoveRow()
    On Error GoTo ErrorHandler
    
    Dim ws As Worksheet
    Dim selectedCell As Range
    Dim selectedRow As Long
    Dim lastContentRow As Long
    Dim sourceRange As Range
    Dim targetRange As Range
    Dim clearRange As Range
    
    ' Initialize log block
    Call StartLogBlock
    
    ' Get active worksheet (mp sheet)
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    Set selectedCell = Selection
    
    ' Validation 1: Check if selected cell is in output area
    selectedRow = selectedCell.Row
    If selectedRow < OUTPUT_START_ROW Or selectedRow > OUTPUT_END_ROW Then
        Call LogDebug("RemoveRow: Selected cell is outside output area (rows " & OUTPUT_START_ROW & "-" & OUTPUT_END_ROW & ")")
        Call FlushLogBlock
        Exit Sub
    End If
    
    If selectedCell.Column < DATA_COLUMNS_START Or selectedCell.Column > DATA_COLUMNS_END Then
        Call LogDebug("RemoveRow: Selected cell is outside data columns (A-N)")
        Call FlushLogBlock
        Exit Sub
    End If
    
    ' Find last non-empty row
    lastContentRow = FindLastContentRow(ws, OUTPUT_START_ROW, OUTPUT_END_ROW)
    
    ' If selected row is beyond the last row, no need to remove
    If selectedRow > lastContentRow Then
        Call LogDebug("RemoveRow: Selected row (" & selectedRow & ") is beyond last content row (" & lastContentRow & "). Cannot remove.")
        Call FlushLogBlock
        Exit Sub
    End If
    
    ' Copy data row by row from S+1 to last content row (to preserve merged cells)
    ' Process from first row to last row to avoid overwriting source data
    Dim i As Long
    Application.DisplayAlerts = False
    
    For i = selectedRow + 1 To lastContentRow
        Set sourceRange = ws.Range(ws.Cells(i, DATA_COLUMNS_START), ws.Cells(i, DATA_COLUMNS_END))
        sourceRange.Copy
        Set targetRange = ws.Cells(i - 1, DATA_COLUMNS_START)
        targetRange.PasteSpecial Paste:=xlPasteAll
    Next i
    
    Application.DisplayAlerts = True
    Application.CutCopyMode = False
    
    ' Clear last content row (columns A-N) - only clear contents, preserve formatting
    Set clearRange = ws.Range(ws.Cells(lastContentRow, DATA_COLUMNS_START), ws.Cells(lastContentRow, DATA_COLUMNS_END))
    clearRange.ClearContents
    
    Call LogInfo("Row removed at row " & selectedRow & ", content shifted up, last content now at row " & (lastContentRow - 1))
    Call FlushLogBlock
    
    Exit Sub
ErrorHandler:
    Application.DisplayAlerts = True
    Application.CutCopyMode = False
    Call LogError("RemoveRow: " & Err.Description)
    Call FlushLogBlock
End Sub

' /**
'  * Helper: Check if a range is empty
'  * @param rng: The range to check
'  * @return: True if all cells in range are empty, False otherwise
'  */
Private Function IsRowEmpty(rng As Range) As Boolean
    Dim cell As Range
    IsRowEmpty = True
    
    For Each cell In rng
        If Not IsEmpty(cell.Value) Then
            IsRowEmpty = False
            Exit Function
        End If
    Next cell
End Function

' /**
'  * Helper: Find last non-empty row in output area
'  * @param ws: The worksheet
'  * @param startRow: Start row to search
'  * @param endRow: End row to search
'  * @return: Row number of last non-empty row, or startRow if all empty
'  */
Private Function FindLastContentRow(ws As Worksheet, startRow As Long, endRow As Long) As Long
    Dim row As Long
    Dim hasContent As Boolean
    Dim col As Long
    
    ' Search from end to start
    For row = endRow To startRow Step -1
        hasContent = False
        
        ' Check if any cell in columns A-N has content
        For col = DATA_COLUMNS_START To DATA_COLUMNS_END
            If Not IsEmpty(ws.Cells(row, col).Value) Then
                hasContent = True
                Exit For
            End If
        Next col
        
        If hasContent Then
            FindLastContentRow = row
            Exit Function
        End If
    Next row
    
    ' If no content found, return startRow
    FindLastContentRow = startRow
End Function
