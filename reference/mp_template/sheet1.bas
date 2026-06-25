' /**
'  * Sheet1 Event Handlers & Row Number Update Logic
'  * 
'  * Functionality:
'  * 1. Row number updates (column E based on column D & F content)
'  * 2. U9 content updates (description text based on D column selection)
'  * 3. Worksheet_Change event monitoring for D11:D39
'  */

' ============================================================================
' PUBLIC FUNCTIONS
' ============================================================================

' /**
'  * Update row numbers from D11:D39
'  * Only assigns row numbers to rows where D column has a valid dropdown option
'  * Allows gaps (rows without valid D values will not get row numbers)
'  * Algorithm:
'  * - Read the starting value from E11 (first row of output area)
'  * - If E11 has a value, use it as the starting counter; otherwise start at 0
'  * - For each row: check if D has a valid option (P/FI/RT/SC/I/H/W/PI)
'  * - If valid: increment counter by 10, assign to E
'  * - If invalid: skip (clear E)
'  * This supports multi-page scenarios where row numbers continue from the previous page
'  */
Public Sub update_row_number()
    Dim ws As Worksheet
    Dim counter As Integer
    Dim i As Integer
    Dim dValue As String
    Dim validOptions As Variant
    Dim isValid As Boolean
    Dim j As Integer
    Dim firstRowValue As Variant
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    
    ' Read the starting value from E11 (first row of output area)
    ' If E11 has a value, use it as the starting counter; otherwise start at 0
    firstRowValue = ws.Range("E11").value
    If firstRowValue <> "" And IsNumeric(firstRowValue) Then
        counter = CLng(firstRowValue)
    Else
        counter = 0
    End If
    
    ' Define valid options for D column dropdown
    validOptions = Array("P", "FI", "RT", "SC", "I", "H", "W", "PI")
    
    ' Iterate through rows 11 to 39
    For i = 11 To 39
        dValue = ws.Range("D" & i).value
        isValid = False
        
        ' Check if D value is in valid options
        For j = LBound(validOptions) To UBound(validOptions)
            If dValue = validOptions(j) Then
                isValid = True
                Exit For
            End If
        Next j
        
        ' If D has valid value, increment counter and assign to E
        If isValid Then
            ' For the first row (E11), only update if it's empty
            If i = 11 Then
                If firstRowValue = "" Or Not IsNumeric(firstRowValue) Then
                    counter = counter + 10
                    ws.Range("E" & i).value = counter
                End If
                ' If E11 already has a user-defined value, keep it unchanged
            Else
                ' For subsequent rows, increment counter and assign
                counter = counter + 10
                ws.Range("E" & i).value = counter
            End If
        Else
            ' Clear E if D doesn't have valid value
            ws.Range("E" & i).value = ""
        End If
    Next i
    
End Sub

' /**
'  * Helper function to check if a value is numeric
'  * @param value - The value to check
'  * @return Boolean - True if the value is numeric, False otherwise
'  */
Private Function IsNumeric(value As Variant) As Boolean
    IsNumeric = Not IsError(value + 0)
End Function

' /**
'  * Update U9 content based on D column selection in a specific row
'  * Maps code values to their descriptions using global code mappings
'  * @param rowNum - The row number to read D column value from
'  */
Public Sub update_u9_content(rowNum As Integer)
    Dim ws As Worksheet
    Dim dValue As String
    Dim u9Value As String
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    
    ' Get value from D column of the modified row
    dValue = Trim(ws.Range("D" & rowNum).value)
    
    ' Map code to description using global function
    If dValue <> "" Then
        u9Value = GetCodeDescription(dValue)
        If u9Value <> "" Then
            ws.Range("U9").value = u9Value
            ' Trigger W9 dropdown update after U9 change
            Call mod_DisplayDropdowns.OnU9Changed(u9Value)
        End If
    End If
    
End Sub

' ============================================================================
' EVENT HANDLERS
' ============================================================================

' /**
' * Worksheet_Change event handler
' * Monitors:
' * 1. D11:D39 range for changes (triggers row number and U9 content updates)
' * 2. U9 cell for changes (triggers W9 dropdown update)
' * 3. W9 cell for changes (triggers display area update)
' */
Private Sub Worksheet_Change(ByVal Target As Range)
    Dim ws As Worksheet
    Dim monitorRange As Range
    Dim changedRange As Range
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    
    ' Disable events to prevent recursive calls
    Application.EnableEvents = False
    
    On Error GoTo ErrorHandler
    
    ' Monitor D11:D39 for changes
    Set monitorRange = ws.Range("D11:D39")
    Set changedRange = Intersect(Target, monitorRange)
    
    If Not changedRange Is Nothing Then
        Call update_row_number
        Call update_u9_content(Target.Row)
    End If
    
    ' Monitor U9 for changes
    If Not Intersect(Target, ws.Range("U9")) Is Nothing Then
        If Target.Cells.Count = 1 Then
            Call mod_DisplayDropdowns.OnU9Changed(Target.value)
        End If
    End If
    
    ' Monitor W9 for changes
    If Not Intersect(Target, ws.Range("W9")) Is Nothing Then
        If Target.Cells.Count = 1 Then
            Call mod_DisplayProcesses.OnW9Changed(Target.value)
        End If
    End If
    
    ' Monitor F11:F39 for process content changes (row height auto-adjust)
    Set monitorRange = ws.Range("F11:F39")
    Set changedRange = Intersect(Target, monitorRange)
    
    If Not changedRange Is Nothing Then
        If Target.Cells.Count = 1 Then
            Call mod_row_height_auto_adjust.AdjustRowHeightForCell(Target.Row, ws)
        End If
    End If
    
    Application.EnableEvents = True
    Exit Sub
    
ErrorHandler:
    Application.EnableEvents = True
    Call lib_logger.StartLogBlock
    Call lib_logger.LogError("Worksheet_Change error: " & Err.Description)
    Call lib_logger.FlushLogBlock
End Sub


