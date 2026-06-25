'/**
' * Insert Process Module (mod_InsertProcess.bas)
' *
' * Handles the Insert button click logic
' * Functionality:
' * 1. Find first empty row in F11:F39 (check D column)
' * 2. Copy U column process text to target F row
' * 3. Fill D column with code (converted from U9 description)
' * 4. Trigger row number update
' */

Option Explicit

' ============================================================================
' CONSTANTS
' ============================================================================

Private Const PROCESS_START_ROW As Long = 11
Private Const PROCESS_END_ROW As Long = 39
Private Const DISPLAY_START_ROW As Long = 12
Private Const DISPLAY_END_ROW As Long = 39
Private Const U9_CELL As String = "U9"
Private Const D_COLUMN As String = "D"
Private Const E_COLUMN As String = "E"
Private Const F_COLUMN As String = "F"
Private Const U_COLUMN As String = "U"
Private Const X_COLUMN As String = "X"
Private Const Y_COLUMN As String = "Y"
Private Const Z_COLUMN As String = "Z"
Private Const PLACEHOLDER_MARKER As String = "________"

' ============================================================================
' PUBLIC FUNCTIONS - BUTTON CLICK HANDLER
' ============================================================================

' /**
'  * Handle Insert button click
'  * Called when user clicks the Insert button for a specific process row
'  *
'  * @param buttonRow: The row number where the button is located (12-39 in U column)
'  */
Public Sub OnInsertButtonClick(buttonRow As Long)
    Dim ws As Worksheet
    Dim displayText As String
    Dim finalText As String
    Dim u9Description As String
    Dim code As String
    Dim targetRow As Long
    Dim xValue As String
    Dim yValue As String
    Dim zValue As String
    
    On Error GoTo ErrorHandler
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    
    ' Get process text from U column (already has placeholders replaced with "_____")
    displayText = CStr(ws.Cells(buttonRow, U_COLUMN).value)
    
    ' Skip if this is the "Relative Process" title row
    If Trim(displayText) = "Relative Process" Then
        MsgBox "This is a section title. Please click on an actual process row below.", vbInformation, "Insert Process"
        Exit Sub
    End If
    
    If Trim(displayText) = "" Then
        Call lib_logger.LogError("OnInsertButtonClick: Empty process text in U" & buttonRow)
        MsgBox "Process text is empty. Nothing to insert.", vbExclamation, "Insert Process"
        Exit Sub
    End If
    
    ' Get U9 description to convert to code
    u9Description = CStr(ws.Range(U9_CELL).value)
    
    If Trim(u9Description) = "" Then
        Call lib_logger.LogError("OnInsertButtonClick: U9 is empty")
        MsgBox "Please select a code in U9 first.", vbExclamation, "Insert Process"
        Exit Sub
    End If
    
    ' Convert description to code
    code = GetCodeFromDescription(u9Description)
    
    If code = "" Then
        Call lib_logger.LogError("OnInsertButtonClick: Failed to convert description [" & u9Description & "] to code")
        MsgBox "Invalid code description. Cannot convert.", vbExclamation, "Insert Process"
        Exit Sub
    End If
    
    ' Get placeholder values from X/Y/Z columns
    xValue = CStr(ws.Cells(buttonRow, X_COLUMN).value)
    yValue = CStr(ws.Cells(buttonRow, Y_COLUMN).value)
    zValue = CStr(ws.Cells(buttonRow, Z_COLUMN).value)
    
    ' Merge placeholders with user-provided values
    finalText = MergePlaceholdersWithValues(displayText, xValue, yValue, zValue)
    
    ' Find first empty row (check D column)
    targetRow = FindFirstEmptyRow(ws)
    
    If targetRow = -1 Then
        Call lib_logger.LogError("OnInsertButtonClick: No empty rows found in F11:F39")
        MsgBox "No empty rows available in the process area (F11:F39).", vbExclamation, "Insert Process"
        Exit Sub
    End If
    
    ' Disable events to prevent recursive calls
    Application.EnableEvents = False
    
    ' Only fill D column with code if D is empty
    Dim dValue As String
    dValue = Trim(ws.Cells(targetRow, D_COLUMN).value)
    
    If dValue = "" Then
        ws.Cells(targetRow, D_COLUMN).value = code
    End If
    
    ' Fill F column with merged process text using Rich Text formatting
    ' This converts **text** markers to true Excel bold formatting
    Call mod_RichTextFormatter.SetCellWithBoldFormat(ws.Cells(targetRow, F_COLUMN), finalText)
    
    ' Re-enable events
    Application.EnableEvents = True
    
    ' Trigger row number update (which also triggers E column update and U9 content update via events)
    Call sheet1.update_row_number
    
    Call lib_logger.LogInfo("OnInsertButtonClick: Process inserted successfully at row " & targetRow)    
    Exit Sub
    
ErrorHandler:
    Application.EnableEvents = True
    Call lib_logger.LogError("OnInsertButtonClick failed: " & Err.Description)
    MsgBox "Error: " & Err.Description, vbCritical, "Insert Process Error"
End Sub

' ============================================================================
' PRIVATE FUNCTIONS - HELPER FUNCTIONS
' ============================================================================

' /**
'  * Find first empty row in the process area (F11:F39)
'  * Empty row is determined by checking if F column is empty
'  *
'  * @return: Row number of first empty row, or -1 if no empty row found
'  */
Private Function FindFirstEmptyRow(ws As Worksheet) As Long
    Dim row As Long
    Dim fValue As String
    
    For row = PROCESS_START_ROW To PROCESS_END_ROW
        fValue = Trim(ws.Cells(row, F_COLUMN).value)
        
        If fValue = "" Then
            FindFirstEmptyRow = row
            Exit Function
        End If
    Next row
    
    ' No empty row found
    FindFirstEmptyRow = -1
End Function

' /**
'  * Merge placeholder underscores with user-provided values
'  * Replaces underscores in display text with actual values from X/Y/Z columns
'  *
'  * @param displayText: the display text with underscores (e.g., "_____ as per BOM")
'  * @param xValue: value from X column (corresponds to {1})
'  * @param yValue: value from Y column (corresponds to {2})
'  * @param zValue: value from Z column (corresponds to {3})
'  * @return: merged text with actual values replacing underscores
'  */
Private Function MergePlaceholdersWithValues(displayText As String, xValue As String, yValue As String, zValue As String) As String
    Dim result As String
    Dim placeholderValues(1 To 3) As String
    Dim i As Long
    Dim j As Long
    Dim currentUnderscoreIndex As Long
    Dim char As String
    Dim currentUnderscoreCount As Long
    
    ' Initialize placeholder values
    ' Trim and use actual value only if it's not the placeholder marker or empty
    placeholderValues(1) = IIf(Trim(xValue) <> "" And Trim(xValue) <> PLACEHOLDER_MARKER, Trim(xValue), "")
    placeholderValues(2) = IIf(Trim(yValue) <> "" And Trim(yValue) <> PLACEHOLDER_MARKER, Trim(yValue), "")
    placeholderValues(3) = IIf(Trim(zValue) <> "" And Trim(zValue) <> PLACEHOLDER_MARKER, Trim(zValue), "")
    
    result = ""
    currentUnderscoreIndex = 0  ' Which "_____" group we're currently at
    currentUnderscoreCount = 0  ' How many consecutive underscores we've seen
    
    ' Iterate through each character
    For i = 1 To Len(displayText)
        char = Mid(displayText, i, 1)
        
        If char = "_" Then
            currentUnderscoreCount = currentUnderscoreCount + 1
            
            ' Check if we've completed a 5-underscore group
            If currentUnderscoreCount = 5 Then
                currentUnderscoreIndex = currentUnderscoreIndex + 1
                
                ' Get the replacement value
                If currentUnderscoreIndex <= 3 And placeholderValues(currentUnderscoreIndex) <> "" Then
                    ' Replace with actual value
                    result = result & placeholderValues(currentUnderscoreIndex)
                Else
                    ' Keep the underscores
                    result = result & "_____"
                End If
                
                currentUnderscoreCount = 0
            End If
        Else
            ' Not an underscore character
            ' If we had some underscores but not 5, add them back
            If currentUnderscoreCount > 0 Then
                For j = 1 To currentUnderscoreCount
                    result = result & "_"
                Next j
                currentUnderscoreCount = 0
            End If
            result = result & char
        End If
    Next i
    
    ' Handle remaining underscores at the end
    If currentUnderscoreCount > 0 Then
        For j = 1 To currentUnderscoreCount
            result = result & "_"
        Next j
    End If
    
    MergePlaceholdersWithValues = result
End Function
