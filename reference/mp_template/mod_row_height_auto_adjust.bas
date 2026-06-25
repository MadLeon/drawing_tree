'/**
' * Row Height Auto Adjust Module (mod_row_height_auto_adjust.bas)
' *
' * Manages automatic row height adjustment for process content cells (F11:F39)
' * based on the number of manual line breaks (Alt+Enter) in the cell content.
' *
' * Functionality:
' * 1. Count manual line breaks (Char(10)) in cell content
' * 2. Map line count to predefined row heights
' * 3. Adjust individual row heights when cells are edited
' */

Option Explicit

' ============================================================================
' CONSTANTS - ROW HEIGHT MAPPING
' ============================================================================

' Customer-provided row height values based on line count
' lineCount = number of Alt+Enter line breaks + 1
Private Const ROW_HEIGHT_1_LINE As Double = 24     ' 0 line breaks
Private Const ROW_HEIGHT_2_LINES As Double = 40    ' 1 line break
Private Const ROW_HEIGHT_3_LINES As Double = 55    ' 2 line breaks
Private Const ROW_HEIGHT_4_LINES As Double = 70    ' 3 line breaks
Private Const ROW_HEIGHT_5_LINES As Double = 90    ' 4 line breaks
Private Const ROW_HEIGHT_6_LINES As Double = 100   ' 5 line breaks
Private Const ROW_HEIGHT_7_LINES As Double = 120   ' 6 line breaks

' For lines beyond 7, use increment of ~20 per line
Private Const ROW_HEIGHT_INCREMENT As Double = 20
Private Const MAX_MAPPED_LINES As Long = 7

' Process content area
Private Const PROCESS_COLUMN As String = "F"
Private Const PROCESS_START_ROW As Long = 11
Private Const PROCESS_END_ROW As Long = 39

' ============================================================================
' PUBLIC FUNCTIONS
' ============================================================================

' /**
'  * Count the number of line breaks (Alt+Enter / Char(10)) in a cell
'  *
'  * @param cellContent: The text content to analyze
'  * @return: Number of line breaks (e.g., "line1" & Char(10) & "line2" = 1)
'  */
Public Function CountLineBreaks(cellContent As String) As Long
    Dim count As Long
    Dim i As Long
    
    count = 0
    For i = 1 To Len(cellContent)
        If Mid(cellContent, i, 1) = Chr(10) Then
            count = count + 1
        End If
    Next i
    
    CountLineBreaks = count
End Function

' /**
'  * Get row height based on line count
'  *
'  * Line count is calculated as: number of line breaks + 1
'  * Uses customer-provided values for lines 1-7
'  * For lines > 7, uses increment calculation
'  *
'  * @param lineCount: Number of lines (1, 2, 3, ..., 7+)
'  * @return: Row height in points
'  */
Public Function GetRowHeightByLineCount(lineCount As Long) As Double
    Select Case lineCount
        Case 1
            GetRowHeightByLineCount = ROW_HEIGHT_1_LINE
        Case 2
            GetRowHeightByLineCount = ROW_HEIGHT_2_LINES
        Case 3
            GetRowHeightByLineCount = ROW_HEIGHT_3_LINES
        Case 4
            GetRowHeightByLineCount = ROW_HEIGHT_4_LINES
        Case 5
            GetRowHeightByLineCount = ROW_HEIGHT_5_LINES
        Case 6
            GetRowHeightByLineCount = ROW_HEIGHT_6_LINES
        Case 7
            GetRowHeightByLineCount = ROW_HEIGHT_7_LINES
        Case Else
            ' For lines beyond 7, use increment calculation
            ' height = 120 + (lineCount - 7) * 20
            GetRowHeightByLineCount = ROW_HEIGHT_7_LINES + (lineCount - MAX_MAPPED_LINES) * ROW_HEIGHT_INCREMENT
    End Select
End Function

' /**
'  * Adjust row height for a specific row based on its process content
'  * Called when a cell in F11:F39 is edited
'  *
'  * @param rowNumber: The row number to adjust (e.g., 11, 12, ..., 39)
'  * @param ws: Worksheet reference (optional, defaults to MP_SHEET_NAME sheet)
'  */
Public Sub AdjustRowHeightForCell(rowNumber As Long, Optional ws As Worksheet)
    Dim cellContent As String
    Dim lineCount As Long
    Dim newRowHeight As Double
    Dim targetCell As Range
    
    ' Use default sheet if not provided
    If ws Is Nothing Then
        Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    End If
    
    ' Validate row number
    If rowNumber < PROCESS_START_ROW Or rowNumber > PROCESS_END_ROW Then
        LogError "AdjustRowHeightForCell: Row number " & rowNumber & " is outside valid range (" & PROCESS_START_ROW & "-" & PROCESS_END_ROW & ")"
        Exit Sub
    End If
    
    ' Get the cell content from column F
    Set targetCell = ws.Range(PROCESS_COLUMN & rowNumber)
    cellContent = targetCell.Value
    
    ' If cell is empty, set to minimum height (1 line)
    If Len(cellContent) = 0 Then
        ws.Rows(rowNumber).RowHeight = ROW_HEIGHT_1_LINE
        LogDebug "AdjustRowHeightForCell: Row " & rowNumber & " is empty, set to 1-line height (" & ROW_HEIGHT_1_LINE & ")"
        Exit Sub
    End If
    
    ' Count line breaks to determine line count
    lineCount = CountLineBreaks(cellContent) + 1
    
    ' Get corresponding row height
    newRowHeight = GetRowHeightByLineCount(lineCount)
    
    ' Apply row height
    ws.Rows(rowNumber).RowHeight = newRowHeight
    
    ' Log the adjustment
    LogDebug "AdjustRowHeightForCell: Row " & rowNumber & " adjusted to " & lineCount & " lines, height=" & newRowHeight
End Sub

' /**
'  * Check if a cell is within the process content range (F11:F39)
'  *
'  * @param targetCell: The cell to check
'  * @return: True if cell is in F11:F39, False otherwise
'  */
Public Function IsProcessContentCell(targetCell As Range) As Boolean
    Dim cellColumn As Long
    Dim cellRow As Long
    
    cellColumn = targetCell.Column
    cellRow = targetCell.Row
    
    ' Column F = 6
    If cellColumn = 6 And cellRow >= PROCESS_START_ROW And cellRow <= PROCESS_END_ROW Then
        IsProcessContentCell = True
    Else
        IsProcessContentCell = False
    End If
End Function
