'/**
' * Button Manager Module (mod_ButtonManager.bas)
' *
' * Manages dynamic creation of Insert buttons using Form Controls
' * Form Control buttons are Excel native objects, no type library required
' * 
' * Functionality:
' * 1. Create Insert buttons in T12:T39 as Form Control buttons
' * 2. Bind each button to handler using OnAction property
' * 3. Store handler instances for lifetime management
' */

Option Explicit

' ============================================================================
' CONSTANTS
' ============================================================================

Private Const BUTTON_COLUMN As String = "T"
Private Const BUTTON_START_ROW As Long = 12
Private Const BUTTON_END_ROW As Long = 39
Private Const BUTTON_WIDTH As Double = 50
Private Const BUTTON_HEIGHT As Double = 20
Private Const BUTTON_LABEL As String = "Insert"
Private Const HANDLER_FUNCTION As String = "mod_ButtonManager.HandleButtonClick"

' ============================================================================
' GLOBAL VARIABLES - BUTTON EVENT HANDLERS
' ============================================================================

' Dictionary to store handler instances by row number
' Keeps references alive to prevent garbage collection
Private handlersByRow As Object ' Dictionary[rowNumber] -> cls_InsertButtonHandler


' ============================================================================
' PUBLIC FUNCTIONS - BUTTON MANAGEMENT
' ============================================================================

' /**
'  * Initialize Insert buttons (deprecated - use CreateDynamicButtons instead)
'  * Kept for compatibility but should be removed after verification
'  */
Public Sub InitializeInsertButtons()
    ' Buttons are now created dynamically by DisplayProcesses module
End Sub

' /**
'  * Create Insert buttons dynamically based on number of displayed processes
'  * Only creates buttons for rows that have actual process text
'  * Skips empty rows and "Relative Process" title row
'  * 
'  * @param displayCount: Number of processes displayed (determines button count)
'  */
Public Sub CreateDynamicButtons(displayCount As Long)
    Dim ws As Worksheet
    Dim row As Long
    Dim shape As Object
    Dim btnName As String
    Dim handler As Object
    Dim cellText As String
    Dim buttonCount As Long
    
    On Error GoTo ErrorHandler
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    
    ' Initialize handler dictionary if needed
    If handlersByRow Is Nothing Then
        Set handlersByRow = CreateObject("Scripting.Dictionary")
    Else
        ' Clear old handlers
        handlersByRow.RemoveAll
    End If
    
    ' Remove all existing buttons first
    Call RemoveAllButtons
    
    ' Create buttons for rows with actual process text (skip empty rows and titles)
    buttonCount = 0
    For row = BUTTON_START_ROW To BUTTON_START_ROW + displayCount - 1
        ' Get text from U column
        cellText = CStr(ws.Cells(row, "U").value)
        
        ' Skip empty rows and "Relative Process" title row
        If Trim(cellText) = "" Or Trim(cellText) = "Relative Process" Then
            GoTo NextRow
        End If
        
        btnName = "btnInsert_" & row
        
        ' Create button
        Set shape = CreateInsertButton(ws, row, btnName)
        
        If Not shape Is Nothing Then
            ' Create and store handler
            Set handler = New cls_InsertButtonHandler
            handler.Initialize row
            handlersByRow.Add row, handler
            
            buttonCount = buttonCount + 1
        End If
        
NextRow:
    Next row
    
    Call lib_logger.LogInfo("CreateDynamicButtons: " & buttonCount & " buttons created successfully (out of " & displayCount & " displayed items)")
    
    Exit Sub
    
ErrorHandler:
    Call lib_logger.LogError("CreateDynamicButtons failed: " & Err.Description)
    MsgBox "Error creating buttons: " & Err.Description, vbCritical, "Button Creation Error"
End Sub

' /**
'  * Remove all Insert buttons from the sheet
'  */
Public Sub RemoveAllButtons()
    Dim ws As Worksheet
    Dim btnName As String
    Dim row As Long
    
    On Error Resume Next
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    
    ' Remove buttons by name pattern (T12:T39)
    For row = BUTTON_START_ROW To BUTTON_END_ROW
        btnName = "btnInsert_" & row
        ws.Shapes(btnName).Delete
    Next row
    
    On Error GoTo 0
    
End Sub

' ============================================================================
' PRIVATE FUNCTIONS - BUTTON CREATION
' ============================================================================

' /**
'  * Create a single Insert button as a Form Control
'  * 
'  * @param ws: Worksheet object
'  * @param row: Row number for button placement
'  * @param btnName: Button name (for referencing)
'  * @return: Shape object (OLE wrapper), or Nothing on failure
'  */
Private Function CreateInsertButton(ws As Worksheet, row As Long, btnName As String) As Object
    Dim shape As Object
    Dim button As Object
    Dim columnRange As Object
    Dim left As Double
    Dim top As Double
    Dim width As Double
    Dim height As Double
    
    On Error GoTo ErrorHandler
    
    ' Get column T position for this row
    Set columnRange = ws.Range(BUTTON_COLUMN & row)
    
    ' Calculate button position (T column, same row)
    left = columnRange.left
    top = columnRange.top + 5
    width = BUTTON_WIDTH
    height = BUTTON_HEIGHT
    
    ' Create Form Control button using xlButtonControl constant
    ' This ensures the correct push button type is created
    Set shape = ws.Shapes.AddFormControl(xlButtonControl, left, top, width, height)
    
    ' Configure button
    Set button = shape.OLEFormat.Object
    With button
        .Caption = BUTTON_LABEL
        .Font.Name = "Calibri"
        .Font.Size = 9
        .Font.Bold = True
        .AutoSize = False
    End With
    
    ' Set button name for identification
    shape.Name = btnName
    
    ' Bind to handler via OnAction
    shape.OnAction = HANDLER_FUNCTION
    
    Set CreateInsertButton = shape
    
    Exit Function
    
ErrorHandler:
    Call lib_logger.LogError("CreateInsertButton failed for row " & row & ": " & Err.Description)
    Set CreateInsertButton = Nothing
End Function

' ============================================================================
' PUBLIC FUNCTIONS - EVENT HANDLER
' ============================================================================

' /**
'  * Unified button click handler
'  * Called when any Insert button is clicked (via OnAction property)
'  * 
'  * Uses Application.Caller to identify which button was clicked
'  * Extracts row number from the button's name and dispatches to handler
'  */
Public Sub HandleButtonClick()
    Dim ws As Worksheet
    Dim callerName As String
    Dim buttonName As String
    Dim rowNumber As Long
    Dim handler As Object
    Dim shape As Object
    
    On Error GoTo ErrorHandler
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    
    ' Get the caller (button that triggered this macro)
    ' Application.Caller returns the name of the shape/range that called this macro
    callerName = Application.Caller
    
    If callerName = "Error" Then
        Call lib_logger.LogError("HandleButtonClick: Could not determine caller")
        Exit Sub
    End If
    
    buttonName = callerName
    
    ' Validate button exists
    On Error Resume Next
    Set shape = ws.Shapes(buttonName)
    On Error GoTo ErrorHandler
    
    If shape Is Nothing Then
        Call lib_logger.LogError("HandleButtonClick: Shape [" & buttonName & "] not found")
        Exit Sub
    End If
    
    ' Extract row number from button name (format: btnInsert_12)
    If InStr(buttonName, "btnInsert_") > 0 Then
        rowNumber = CLng(Replace(buttonName, "btnInsert_", ""))
        
        ' Get handler for this row
        If handlersByRow.Exists(rowNumber) Then
            Set handler = handlersByRow(rowNumber)
            Call handler.HandleButtonClick()
        Else
            Call lib_logger.LogError("HandleButtonClick: No handler found for row " & rowNumber)
            MsgBox "Error: No handler for button row " & rowNumber, vbExclamation, "Button Handler"
        End If
    Else
        Call lib_logger.LogError("HandleButtonClick: Invalid button name [" & buttonName & "]")
        MsgBox "Error: Invalid button name [" & buttonName & "]", vbExclamation, "Button Handler"
    End If
    
    Exit Sub
    
ErrorHandler:
    Call lib_logger.LogError("HandleButtonClick failed: " & Err.Description)
    MsgBox "Button click error: " & Err.Description, vbCritical, "Button Click Error"
End Sub
