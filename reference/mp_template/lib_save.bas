'/**
' * Save Module for Manufacturing Process
' * Handles file saving with auto-generated filenames and network paths
' */

Option Explicit

' /**
'  * Reads save data from the worksheet
'  * Returns: Dictionary with keys: customerName, poNumber, description, jobNumber, drawingNumber
'  */
Public Function ReadSaveData() As Object
    Dim ws As Worksheet
    Dim saveData As Object
    Dim poNum As String
    Dim jobNum As String
    Dim dwgNum As String
    Dim customerName As String
    Dim description As String
    
    On Error GoTo ErrorHandler
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    Set saveData = CreateObject("Scripting.Dictionary")
    
    ' Read data from specified cells
    customerName = CStr(ws.Range("B6").Value)
    poNum = CStr(ws.Range("B7").Value)
    description = CStr(ws.Range("H8").Value)
    jobNum = CStr(ws.Range("Q6").Value)
    dwgNum = CStr(ws.Range("J7").Value)
    
    ' Handle empty values
    If customerName = "" Then customerName = "Unknown"
    If poNum = "" Then poNum = ""
    If description = "" Then description = "Unknown"
    If jobNum = "" Then jobNum = "Unknown"
    If dwgNum = "" Then dwgNum = "Unknown"
    
    saveData.Add "customerName", customerName
    saveData.Add "poNumber", poNum
    saveData.Add "description", description
    saveData.Add "jobNumber", jobNum
    saveData.Add "drawingNumber", dwgNum
    
    Set ReadSaveData = saveData
    Exit Function
    
ErrorHandler:
    Call LogError("ReadSaveData: " & Err.Description)
    Set ReadSaveData = Nothing
End Function

' /**
'  * Builds the save path based on customer name and PO number
'  * Returns: Full network path string
'  */
Public Function BuildSavePath(customerName As String, poNumber As String) As String
    Dim basePath As String
    Dim fullPath As String
    
    basePath = "\\rtdnas2\Manufacturing Process\"
    
    ' Handle empty customer name
    If Len(Trim(customerName)) = 0 Then
        customerName = "Unknown"
    End If
    
    ' Build path based on PO number existence
    If Len(Trim(poNumber)) > 0 Then
        fullPath = basePath & customerName & "\" & poNumber
    Else
        fullPath = basePath & customerName
    End If
    
    BuildSavePath = fullPath
End Function

' /**
'  * Generates the file name based on drawing number, description, and job number
'  * Format: {dwg#} {description} (J#{job#})
'  * Returns: Formatted filename string (without extension)
'  */
Public Function GenerateFileName(drawingNumber As String, description As String, jobNumber As String) As String
    Dim fileName As String
    Dim parts() As String
    Dim partCount As Integer
    
    partCount = 0
    ReDim parts(2)
    
    ' Collect non-empty parts
    If Len(Trim(drawingNumber)) > 0 Then
        parts(partCount) = Trim(drawingNumber)
        partCount = partCount + 1
    End If
    
    If Len(Trim(description)) > 0 Then
        parts(partCount) = Trim(description)
        partCount = partCount + 1
    End If
    
    ' Combine parts with spaces
    If partCount > 0 Then
        fileName = parts(0)
        If partCount > 1 Then
            fileName = fileName & " " & parts(1)
        End If
    Else
        fileName = "Document"
    End If
    
    ' Add job number in parentheses
    If Len(Trim(jobNumber)) > 0 Then
        fileName = fileName & " (J#" & Trim(jobNumber) & ")"
    End If
    
    GenerateFileName = fileName
End Function

' /**
'  * Main function to trigger Save As dialog with auto-generated filename
'  * Prepares path and filename for user to complete save action
'  */
Public Sub SaveWithAutoFilename()
    Dim saveData As Object
    Dim savePath As String
    Dim fileName As String
    
    ' Start logging block
    Call StartLogBlock()
    Call LogInfo("SaveWithAutoFilename invoked")
    
    ' Read data from worksheet
    Set saveData = ReadSaveData()
    
    If saveData Is Nothing Then
        Call LogError("SaveWithAutoFilename: Failed to read save data")
        Call FlushLogBlock()
        MsgBox "Error: Failed to read save data. Please check the log file.", vbCritical
        Exit Sub
    End If
    
    ' Build save path and filename
    savePath = BuildSavePath(saveData("customerName"), saveData("poNumber"))
    fileName = GenerateFileName(saveData("drawingNumber"), saveData("description"), saveData("jobNumber"))
    
    ' Log the operation
    Call LogDebug("Save Path: " & savePath)
    Call LogDebug("Auto-generated Filename: " & fileName)
    
    ' Open Save As dialog with pre-filled filename and adjusted path
    Call OpenSaveAsDialog(saveData("customerName"), saveData("poNumber"), fileName)
    
    ' Flush logs to file
    Call FlushLogBlock()
    
End Sub

' /**
'  * Opens the Save As dialog with pre-filled path and filename
'  * Parameters: customerName, poNumber, suggestedName
'  * Path logic: Check if PO folder exists, if not use customer folder only
'  * Uses GetSaveAsFilename to open the standard Save As dialog
'  */
Private Sub OpenSaveAsDialog(customerName As String, poNumber As String, suggestedName As String)
    Dim selectedPath As String
    Dim basePath As String
    Dim initialPath As String
    Dim fso As Object
    Dim fileName As String
    Dim fileExtension As String
    Dim fullPath As String
    
    On Error GoTo ErrorHandler
    
    ' Build base path
    basePath = "\\rtdnas2\Manufacturing Process\" & customerName
    
    ' Check if PO folder exists (if PO number is provided)
    Set fso = CreateObject("Scripting.FileSystemObject")
    If Len(Trim(poNumber)) > 0 Then
        ' Try with PO folder first
        Dim fullPathWithPO As String
        fullPathWithPO = basePath & "\" & poNumber
        
        If fso.FolderExists(fullPathWithPO) Then
            initialPath = fullPathWithPO
            Call LogDebug("PO folder exists: " & fullPathWithPO)
        Else
            initialPath = basePath
            Call LogDebug("PO folder does not exist, using customer path: " & basePath)
        End If
    Else
        initialPath = basePath
        Call LogDebug("No PO number provided, using customer path: " & basePath)
    End If
    
    ' Use GetSaveAsFilename to open the standard Save As dialog
    Dim suggestedFullPath As String
    suggestedFullPath = initialPath & "\" & suggestedName & ".xlsm"
    
    selectedPath = Application.GetSaveAsFilename( _
        InitialFilename:=suggestedFullPath, _
        FileFilter:="Excel Macro-Enabled Workbook (*.xlsm),*.xlsm,Excel Workbook (*.xlsx),*.xlsx,Excel 97-2003 (*.xls),*.xls", _
        Title:="Save Manufacturing Process File")
    
    ' If user cancelled, selectedPath will be False
    If selectedPath = False Then
        Call LogInfo("Save As dialog cancelled by user")
        Exit Sub
    End If
    
    Call LogDebug("User selected path: " & selectedPath)
    
    ' Extract filename and extension from selected path
    fileName = fso.GetBaseName(selectedPath)
    fileExtension = fso.GetExtensionName(selectedPath)
    
    ' If no extension, default to .xlsm
    If Len(fileExtension) = 0 Then
        fullPath = selectedPath & ".xlsm"
        fileExtension = "xlsm"
    Else
        fullPath = selectedPath
    End If
    
    ' Create directory if it doesn't exist
    Dim folderPath As String
    folderPath = fso.GetParentFolderName(fullPath)
    If Not fso.FolderExists(folderPath) Then
        Call LogDebug("Creating directory: " & folderPath)
        On Error Resume Next
        fso.CreateFolder folderPath
        On Error GoTo ErrorHandler
    End If
    
    ' Determine file format based on extension
    Dim saveFormat As Long
    Select Case LCase(fileExtension)
        Case "xlsm"
            saveFormat = 52 ' xlOpenXMLWorkbookMacroEnabled
        Case "xlsx"
            saveFormat = 51 ' xlOpenXMLWorkbook
        Case "xls"
            saveFormat = -4143 ' xlExcel8
        Case Else
            saveFormat = 52 ' Default to .xlsm
    End Select
    
    ' Save the workbook
    Call LogInfo("Saving workbook to: " & fullPath)
    On Error Resume Next
    ThisWorkbook.SaveAs Filename:=fullPath, FileFormat:=saveFormat
    If Err.Number <> 0 Then
        Call LogError("SaveAs error: " & Err.Description)
        Err.Clear
    Else
        Call LogInfo("File successfully saved to: " & fullPath)
    End If
    On Error GoTo ErrorHandler
    
    Exit Sub
ErrorHandler:
    Call LogError("OpenSaveAsDialog: " & Err.Description)
    MsgBox "Error during save operation: " & Err.Description, vbCritical
End Sub
