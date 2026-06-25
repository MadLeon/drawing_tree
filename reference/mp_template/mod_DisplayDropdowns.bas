'/**
' * Display Dropdowns Module (mod_DisplayDropdowns.bas)
' * 
' * Manages U9 and W9 dropdown lists for code and type selection
' * 
' * Functionality:
' * 1. Create U9 dropdown (code selection): P, FI, RT, SC, I, H, W, PI
' * 2. Monitor U9 changes
' * 3. Generate W9 dropdown dynamically based on U9 selection
' * 4. Extract all unique types for the selected code
' */

Option Explicit

' ============================================================================
' CONSTANTS
' ============================================================================

Private Const U9_CELL As String = "U9"
Private Const W9_CELL As String = "W9"

' ============================================================================
' PUBLIC FUNCTIONS - INITIALIZATION
' ============================================================================

' /**
'  * Initialize U9 and W9 dropdowns on workbook startup
'  * Call this from mod_DataInitialization after loading ProcessData
'  * 
'  * Steps:
'  * U9 dropdown is already manually set in Excel
'  * Only need to clear W9 initially (will be populated by U9 change)
'  */
Public Sub InitializeDropdowns()
    On Error GoTo ErrorHandler
    
    ' Clear W9 initially (will be populated when U9 is selected)
    Call ClearW9Dropdown
    
    Exit Sub
    
ErrorHandler:
    Call lib_logger.LogError("InitializeDropdowns failed: " & Err.Description)
End Sub

' /**
'  * Handle U9 change event
'  * When user selects a code in U9, update W9 dropdown with corresponding types
'  * 
'  * @param u9Value: the new value selected in U9 (e.g., "P  = Purchase")
'  */
Public Sub OnU9Changed(u9Value As String)
    Dim code As String
    Dim types As Collection
    
    On Error GoTo ErrorHandler
    
    Call lib_logger.StartLogBlock
    
    ' If U9 is empty, clear W9
    If Trim(u9Value) = "" Then
        Call ClearW9Dropdown
        Call lib_logger.FlushLogBlock
        Exit Sub
    End If
    
    ' Extract code from U9 description
    ' (e.g., "P  = Purchase" -> "P")
    code = GetCodeFromDescription(u9Value)
    
    If code = "" Then
        Call lib_logger.LogError("Invalid code description in U9: " & u9Value)
        Call lib_logger.FlushLogBlock
        Exit Sub
    End If
    
    ' Get all types for this code
    Set types = GetTypesForCode(code)
    
    ' Update W9 dropdown with these types
    Call SetupW9Dropdown(types)
    
    Call lib_logger.FlushLogBlock
    
    Exit Sub
    
ErrorHandler:
    Call lib_logger.LogError("OnU9Changed failed: " & Err.Description)
    Call lib_logger.FlushLogBlock
End Sub

' ============================================================================
' PRIVATE FUNCTIONS - DROPDOWN SETUP
' ============================================================================

' /**
'  * Setup W9 dropdown with types from a collection
'  * 
'  * @param typesCollection: Collection of type strings
'  */
Private Sub SetupW9Dropdown(typesCollection As Collection)
    Dim ws As Worksheet
    Dim w9Cell As Range
    Dim listString As String
    Dim i As Long
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    Set w9Cell = ws.Range(W9_CELL)
    
    ' Clear existing validation
    On Error Resume Next
    w9Cell.Validation.Delete
    On Error GoTo 0
    
    ' Build list string from collection
    If typesCollection.Count = 0 Then
        Exit Sub
    End If
    
    ' Build comma-separated list
    listString = ""
    For i = 1 To typesCollection.Count
        If listString = "" Then
            listString = typesCollection(i)
        Else
            listString = listString & "," & typesCollection(i)
        End If
    Next i
    
    ' Add data validation (dropdown) using standard syntax
    On Error Resume Next
    With w9Cell.Validation
        .Add Type:=3, AlertStyle:=1, Formula1:=listString
        .IgnoreBlank = True
        .InCellDropdown = True
    End With
    On Error GoTo 0
    
    ' Set W9 value to first type option
    w9Cell.value = typesCollection(1)
    
    ' Trigger display update with the new W9 value
    Call mod_DisplayProcesses.OnW9Changed(typesCollection(1))
    
End Sub

' /**
'  * Clear W9 dropdown (remove data validation)
'  */
Private Sub ClearW9Dropdown()
    Dim ws As Worksheet
    Dim w9Cell As Range
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    Set w9Cell = ws.Range(W9_CELL)
    
    w9Cell.Validation.Delete
    w9Cell.value = ""
    
End Sub

' ============================================================================
' PRIVATE FUNCTIONS - DATA EXTRACTION
' ============================================================================

' /**
'  * Get all unique types for a specific code
'  * 
'  * @param code: the code (e.g., "P", "FI", etc.)
'  * @return: Collection of unique type strings
'  */
Private Function GetTypesForCode(code As String) As Collection
    Dim types As Collection
    Dim processEntries As Collection
    Dim i As Long
    Dim processType As String
    Dim isDuplicate As Boolean
    Dim j As Long
    
    Set types = New Collection
    
    ' Get all process entries for this code
    If Not ProcessData.Exists(code) Then
        Call lib_logger.LogError("Code [" & code & "] not found in ProcessData")
        Set GetTypesForCode = types
        Exit Function
    End If
    
    Set processEntries = ProcessData(code)
    
    ' Extract unique types
    For i = 1 To processEntries.Count
        processType = Trim(processEntries(i)("type"))
        
        ' Skip empty types
        If processType = "" Then
            GoTo NextEntry
        End If
        
        ' Check if this type already exists in collection
        isDuplicate = False
        For j = 1 To types.Count
            If types(j) = processType Then
                isDuplicate = True
                Exit For
            End If
        Next j
        
        ' Add if not duplicate
        If Not isDuplicate Then
            types.Add processType
        End If
        
NextEntry:
    Next i
    
    Set GetTypesForCode = types
End Function
