'/**
' * Display Processes Module (mod_DisplayProcesses.bas)
' * 
' * Manages the display of process information in the Display area (U12:U39)
' * 
' * Functionality:
' * 1. Query processes based on code and type selection (U9 and W9)
' * 2. Display process list starting from U12
' * 3. Replace placeholders {1}, {2}, etc. with underscores
' * 4. Clear display area when no selection
' */

Option Explicit

' ============================================================================
' CONSTANTS
' ============================================================================

Private Const DISPLAY_START_ROW As Long = 12
Private Const DISPLAY_END_ROW As Long = 39
Private Const DISPLAY_COLUMN As String = "U"
Private Const U9_CELL As String = "U9"
Private Const W9_CELL As String = "W9"
Private Const X_COLUMN As String = "X"
Private Const Y_COLUMN As String = "Y"
Private Const Z_COLUMN As String = "Z"
Private Const PLACEHOLDER_MARKER As String = "________"

' ============================================================================
' PUBLIC FUNCTIONS - EVENT HANDLER
' ============================================================================

' /**
'  * Handle W9 change event
'  * When user selects a type in W9, update the display area with matching processes
'  * 
'  * @param w9Value: the type value selected in W9
'  */
Public Sub OnW9Changed(w9Value As String)
    Dim ws As Worksheet
    Dim u9Value As String
    Dim code As String
    Dim processes As Collection
    
    On Error GoTo ErrorHandler
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    u9Value = ws.Range(U9_CELL).value
    
    ' If W9 is empty, clear display
    If Trim(w9Value) = "" Then
        Call ClearDisplayArea
        Exit Sub
    End If
    
    ' If U9 is also empty, clear display
    If Trim(u9Value) = "" Then
        Call ClearDisplayArea
        Exit Sub
    End If
    
    ' Extract code from U9 description
    code = GetCodeFromDescription(u9Value)
    
    If code = "" Then
        Call lib_logger.LogError("Invalid code in U9: " & u9Value)
        Exit Sub
    End If
    
    ' Get processes for this code and type
    Set processes = GetProcessesForCodeAndType(code, w9Value)
    
    ' Display processes
    Call DisplayProcesses(processes)
    
    Exit Sub
    
ErrorHandler:
    Call lib_logger.LogError("OnW9Changed failed: " & Err.Description)
End Sub

' ============================================================================
' PRIVATE FUNCTIONS - DISPLAY LOGIC
' ============================================================================

' /**
'  * Display processes in the display area (U12:U39)
'  * Also fills X/Y/Z columns with underscores for placeholders
'  * 
'  * @param processes: Collection of process dictionaries
'  */
Private Sub DisplayProcesses(processes As Collection)
    Dim ws As Worksheet
    Dim currentRow As Long
    Dim i As Long
    Dim processText As String
    Dim originalText As String
    Dim displayCount As Long
    Dim placeholderNums As Collection
    Dim j As Long
    Dim placeholderNum As Long
    Dim relatedRows As Collection
    Dim relatedRowNum As Long
    Dim k As Long
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    Set relatedRows = New Collection
    
    Call lib_logger.LogInfo("DisplayProcesses: Starting with " & processes.Count & " main processes")
    
    ' Clear display area first
    Call ClearDisplayArea
    
    ' Display each process starting from U12
    currentRow = DISPLAY_START_ROW
    displayCount = 0
    For i = 1 To processes.Count
        If currentRow > DISPLAY_END_ROW Then
            Exit For
        End If
        
        ' Get original process text (with placeholders)
        originalText = CStr(processes(i)("process"))
        
        ' Detect placeholder numbers before replacement
        Set placeholderNums = DetectPlaceholders(originalText)
        
        ' Replace placeholders for display
        processText = ReplacePlaceholders(originalText)
        
        ' Write to display area (U column)
        ' Keep **text** markers as-is for now - they'll be converted to Rich Text format during final insert
        ws.Cells(currentRow, DISPLAY_COLUMN).value = processText
        
        ' Fill X/Y/Z columns based on detected placeholders
        ' X corresponds to {1}, Y to {2}, Z to {3}
        If placeholderNums.Count > 0 Then
            For j = 1 To placeholderNums.Count
                placeholderNum = CLng(placeholderNums(j))
                
                Select Case placeholderNum
                    Case 1
                        ws.Cells(currentRow, X_COLUMN).value = PLACEHOLDER_MARKER
                    Case 2
                        ws.Cells(currentRow, Y_COLUMN).value = PLACEHOLDER_MARKER
                    Case 3
                        ws.Cells(currentRow, Z_COLUMN).value = PLACEHOLDER_MARKER
                End Select
            Next j
        End If
        
        ' Collect related row numbers for combo display
        relatedRowNum = CLng(processes(i)("link"))
        
        If relatedRowNum > 0 Then
            ' Check if this row is already in collection (avoid duplicates)
            Dim found As Boolean
            found = False
            On Error Resume Next
            For k = 1 To relatedRows.Count
                If CLng(relatedRows(k)) = relatedRowNum Then
                    found = True
                    Exit For
                End If
            Next k
            On Error GoTo 0
            
            If Not found Then
                relatedRows.Add relatedRowNum
            End If
        End If
        
        currentRow = currentRow + 1
        displayCount = displayCount + 1
    Next i
    
    ' Display related processes (combo logic)
    If relatedRows.Count > 0 Then
        Call lib_logger.LogInfo("DisplayProcesses: Starting to display " & relatedRows.Count & " related processes")
        
        ' Add blank row for separation
        currentRow = currentRow + 1
        
        ' Add "Relative Process" title (no button for this row)
        If currentRow <= DISPLAY_END_ROW Then
            ws.Cells(currentRow, DISPLAY_COLUMN).value = "Relative Process"
            ' Format title: bold and center
            With ws.Cells(currentRow, DISPLAY_COLUMN)
                .Font.Bold = True
                .HorizontalAlignment = xlCenter
            End With
            Call lib_logger.LogInfo("Row " & currentRow & ": Relative Process (title)")
            currentRow = currentRow + 1
        End If
        
        ' Display each related process
        For i = 1 To relatedRows.Count
            If currentRow > DISPLAY_END_ROW Then
                Exit For
            End If
            
            relatedRowNum = CLng(relatedRows(i))
            
            ' Get the process from data sheet at this row
            Dim relatedProcess As String
            relatedProcess = GetProcessFromDataRow(relatedRowNum)
            
            If relatedProcess <> "" Then
                ' Detect placeholders in related process
                Set placeholderNums = DetectPlaceholders(relatedProcess)
                
                ' Replace placeholders for display
                processText = ReplacePlaceholders(relatedProcess)
                
                ' Write to display area
                ' Keep **text** markers as-is for now - they'll be converted to Rich Text format during final insert
                ws.Cells(currentRow, DISPLAY_COLUMN).value = processText
                Call lib_logger.LogInfo("Row " & currentRow & " (related from data row " & relatedRowNum & "): " & processText)
                
                ' Fill X/Y/Z columns based on detected placeholders
                If placeholderNums.Count > 0 Then
                    For j = 1 To placeholderNums.Count
                        placeholderNum = CLng(placeholderNums(j))
                        
                        Select Case placeholderNum
                            Case 1
                                ws.Cells(currentRow, X_COLUMN).value = PLACEHOLDER_MARKER
                            Case 2
                                ws.Cells(currentRow, Y_COLUMN).value = PLACEHOLDER_MARKER
                            Case 3
                                ws.Cells(currentRow, Z_COLUMN).value = PLACEHOLDER_MARKER
                        End Select
                    Next j
                End If
                
                currentRow = currentRow + 1
                displayCount = displayCount + 1
            Else
                Call lib_logger.LogError("Related process at data row " & relatedRowNum & " is empty!")
            End If
        Next i
    End If
    
    ' Calculate actual displayCount including blank row and title row
    displayCount = currentRow - DISPLAY_START_ROW
    Call lib_logger.LogInfo("DisplayProcesses: Total displayed (main + blank + title + related) = " & displayCount & " rows (from U" & DISPLAY_START_ROW & " to U" & (currentRow - 1) & ")")
    
    ' Create corresponding Insert buttons dynamically
    ' displayCount now includes: main processes + blank row + title row + related processes
    Call mod_ButtonManager.CreateDynamicButtons(displayCount)
    
End Sub

' /**
'  * Clear the display area (U12:U39) and corresponding buttons and placeholder markers
'  */
Private Sub ClearDisplayArea()
    Dim ws As Worksheet
    Dim displayRange As Range
    Dim xRange As Range
    Dim yRange As Range
    Dim zRange As Range
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    Set displayRange = ws.Range(DISPLAY_COLUMN & DISPLAY_START_ROW & ":" & DISPLAY_COLUMN & DISPLAY_END_ROW)
    Set xRange = ws.Range(X_COLUMN & DISPLAY_START_ROW & ":" & X_COLUMN & DISPLAY_END_ROW)
    Set yRange = ws.Range(Y_COLUMN & DISPLAY_START_ROW & ":" & Y_COLUMN & DISPLAY_END_ROW)
    Set zRange = ws.Range(Z_COLUMN & DISPLAY_START_ROW & ":" & Z_COLUMN & DISPLAY_END_ROW)
    
    ' Clear process text
    displayRange.ClearContents
    
    ' Reset bold and center alignment (only these, preserve other formatting)
    With displayRange
        .Font.Bold = False
        .HorizontalAlignment = xlGeneral
    End With
    
    ' Clear placeholder markers in X/Y/Z columns
    xRange.ClearContents
    yRange.ClearContents
    zRange.ClearContents
    
    ' Clear buttons created in T column
    Call mod_ButtonManager.RemoveAllButtons
    
End Sub

' ============================================================================
' PRIVATE FUNCTIONS - DATA RETRIEVAL
' ============================================================================

' /**
'  * Get all processes for a specific code and type
'  * 
'  * @param code: the code (e.g., "P", "FI", etc.)
'  * @param processType: the type to match
'  * @return: Collection of process dictionaries with fields: code, type, link, process, dataRow
'  *          where link is the related row number (0 if no link)
'  */
Private Function GetProcessesForCodeAndType(code As String, processType As String) As Collection
    Dim results As Collection
    Dim processEntries As Collection
    Dim i As Long
    Dim entry As Object
    
    Set results = New Collection
    
    ' Get all process entries for this code
    If Not ProcessData.Exists(code) Then
        Call lib_logger.LogError("Code [" & code & "] not found in ProcessData")
        Set GetProcessesForCodeAndType = results
        Exit Function
    End If
    
    Set processEntries = ProcessData(code)
    
    ' Filter entries by type
    For i = 1 To processEntries.Count
        Set entry = processEntries(i)
        
        If Trim(entry("type")) = Trim(processType) Then
            results.Add entry
        End If
    Next i
    
    Set GetProcessesForCodeAndType = results
End Function

' ============================================================================
' PRIVATE FUNCTIONS - TEXT PROCESSING
' ============================================================================

' /**
'  * Replace placeholders like {1}, {2}, etc. with underscores
'  * All placeholders are replaced with "_____" (5 underscores)
'  * 
'  * @param processText: the original process text
'  * @return: text with placeholders replaced
'  */
Private Function ReplacePlaceholders(processText As String) As String
    Dim result As String
    Dim i As Long
    Dim char As String
    Dim inBrace As Boolean
    Dim braceContent As String
    
    result = ""
    inBrace = False
    braceContent = ""
    
    ' Iterate through each character
    For i = 1 To Len(processText)
        char = Mid(processText, i, 1)
        
        If char = "{" Then
            inBrace = True
            braceContent = ""
        ElseIf char = "}" And inBrace Then
            ' End of placeholder
            ' Check if braceContent is numeric
            If IsNumeric(braceContent) Then
                result = result & "_____"
            Else
                ' Not a placeholder, keep original
                result = result & "{" & braceContent & "}"
            End If
            inBrace = False
        ElseIf inBrace Then
            braceContent = braceContent & char
        Else
            result = result & char
        End If
    Next i
    
    ' Handle incomplete braces
    If inBrace Then
        result = result & "{" & braceContent
    End If
    
    ReplacePlaceholders = result
End Function

' /**
'  * Check if a string is numeric
'  * 
'  * @param s: string to check
'  * @return: True if string contains only digits, False otherwise
'  */
Private Function IsNumeric(s As String) As Boolean
    Dim i As Long
    Dim char As String
    
    If Len(s) = 0 Then
        IsNumeric = False
        Exit Function
    End If
    
    For i = 1 To Len(s)
        char = Mid(s, i, 1)
        If char < "0" Or char > "9" Then
            IsNumeric = False
            Exit Function
        End If
    Next i
    
    IsNumeric = True
End Function

' /**
'  * Detect all placeholder numbers in a process text
'  * Returns a Collection of placeholder numbers found (e.g., [1, 2, 3])
'  * 
'  * @param processText: the original process text with placeholders like {1}, {2}, etc.
'  * @return: Collection of Long values representing placeholder numbers
'  */
Private Function DetectPlaceholders(processText As String) As Collection
    Dim results As Collection
    Dim i As Long
    Dim char As String
    Dim inBrace As Boolean
    Dim braceContent As String
    Dim placeholderNum As String
    Dim found As Boolean
    Dim j As Long
    
    Set results = New Collection
    inBrace = False
    braceContent = ""
    
    ' Iterate through each character
    For i = 1 To Len(processText)
        char = Mid(processText, i, 1)
        
        If char = "{" Then
            inBrace = True
            braceContent = ""
        ElseIf char = "}" And inBrace Then
            ' End of placeholder
            ' Check if braceContent is numeric
            If IsNumeric(braceContent) Then
                ' Check if this number is already in results
                found = False
                On Error Resume Next
                For j = 1 To results.Count
                    If CLng(results(j)) = CLng(braceContent) Then
                        found = True
                        Exit For
                    End If
                Next j
                On Error GoTo 0
                
                ' Add if not found
                If Not found Then
                    results.Add CLng(braceContent)
                End If
            End If
            inBrace = False
        ElseIf inBrace Then
            braceContent = braceContent & char
        End If
    Next i
    
    Set DetectPlaceholders = results
End Function

' /**
'  * Get process description from data sheet by row number
'  * 
'  * @param rowNum: the row number in data sheet
'  * @return: the process description text, or empty if row not found or invalid
'  */
Private Function GetProcessFromDataRow(rowNum As Long) As String
    Dim dataWs As Worksheet
    Dim processText As String
    
    On Error Resume Next
    Set dataWs = ThisWorkbook.Sheets("data")
    On Error GoTo 0
    
    If dataWs Is Nothing Then
        Call lib_logger.LogError("GetProcessFromDataRow: data sheet not found")
        GetProcessFromDataRow = ""
        Exit Function
    End If
    
    ' Column D (column 4) contains process description
    On Error Resume Next
    processText = CStr(dataWs.Cells(rowNum, 4).value)
    On Error GoTo 0
    
    processText = Trim(processText)
    
    GetProcessFromDataRow = processText
End Function
