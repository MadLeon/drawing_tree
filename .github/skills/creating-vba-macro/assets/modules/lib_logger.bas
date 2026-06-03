'/**
' * Logger Module (lib_logger.bas)
' * 
' * Description:
' *  - Write all debug, error, and info messages to mp_log.txt file
' *  - File location: Same directory as Manufacturing Process.xlsm
' *  - Log format: [YYYY-MM-DD HH:MM:SS] [Category] Message content
' *  - Block-based logging: logs are buffered and written as complete blocks
' *  - New blocks are inserted at the beginning of file (newest first)
' *  - Lines within a block are in chronological order (earliest first)
' *  - Blocks are separated by blank lines
' * 
' * Usage:
' *  1. Call StartLogBlock() at the beginning of an operation
' *  2. Call LogDebug/LogError/LogInfo() during operation (buffered)
' *  3. Call FlushLogBlock() at end of operation to write entire block to file
' */

Option Explicit

' ============== Private Constants ==============
Private Const LOG_FILE_NAME As String = "mp_log.txt"
Private Const LOG_CATEGORY_DEBUG As String = "DEBUG"
Private Const LOG_CATEGORY_ERROR As String = "ERROR"
Private Const LOG_CATEGORY_INFO As String = "INFO"

' ============== Private Global Variables ==============
Private logFilePath As String
Private fileSystemObj As Object
Private logBuffer As Collection ' Buffer for current block

' ============== Initialization Functions ==============

' Get or initialize FileSystemObject
Private Function GetFileSystemObject() As Object
    If fileSystemObj Is Nothing Then
        Set fileSystemObj = CreateObject("Scripting.FileSystemObject")
    End If
    Set GetFileSystemObject = fileSystemObj
End Function

' Get log file path
Private Function GetLogFilePath() As String
    If logFilePath = "" Then
        logFilePath = ThisWorkbook.Path & "\" & LOG_FILE_NAME
    End If
    GetLogFilePath = logFilePath
End Function

' ============== Block Management Functions ==============

' /**
' * Start a new log block (initialize buffer)
' * Call this at the beginning of a major operation (e.g., LinkDrawingFile_Main, FetchDrawingFiles_Main)
' * If DISABLE_LOGGER is True, this function returns immediately without action
' */
Public Sub StartLogBlock()
    If DISABLE_LOGGER Then Exit Sub
    Set logBuffer = New Collection
End Sub

' /**
' * Check if a log block is currently active
' * Returns True if buffer is not nothing and not empty
' */
Public Function IsLogBlockActive() As Boolean
    IsLogBlockActive = (Not (logBuffer Is Nothing) And logBuffer.Count > 0)
End Function

' /**
' * Flush buffered logs as a complete block to the file
' * New blocks are inserted at the beginning of the file
' * Call this at the end of a major operation or in error handler
' * If DISABLE_LOGGER is True, this function returns immediately without action
' */
Public Sub FlushLogBlock()
    If DISABLE_LOGGER Then Exit Sub
    
    Dim fso As Object
    Dim logPath As String
    Dim fileObj As Object
    Dim existingContent As String
    Dim i As Long
    Dim newBlock As String
    
    ' Guard: if buffer is empty, don't write
    If logBuffer Is Nothing Or logBuffer.Count = 0 Then
        Exit Sub
    End If
    
    Set fso = GetFileSystemObject()
    logPath = GetLogFilePath()
    
    ' Read existing content
    existingContent = ReadLogFile()
    
    ' Build the new block from buffered entries (preserving order)
    newBlock = ""
    For i = 1 To logBuffer.Count
        newBlock = newBlock & logBuffer(i) & vbCrLf
    Next i
    
    ' Create or overwrite file with: new block + blank line + existing content
    Set fileObj = fso.CreateTextFile(logPath, True) ' True = overwrite
    
    ' Write new block
    fileObj.Write newBlock
    
    ' Add separator blank line if there is existing content
    If existingContent <> "" Then
        fileObj.WriteLine ""
        fileObj.Write existingContent
    End If
    
    fileObj.Close
    
    ' Clear buffer after writing
    Set logBuffer = New Collection
End Sub

' ============== File Operation Functions ==============

' Get current timestamp (format: YYYY-MM-DD HH:MM:SS)
Private Function GetTimeStamp() As String
    GetTimeStamp = Format(Now(), "yyyy-mm-dd hh:mm:ss")
End Function

' Generate formatted log entry
Private Function FormatLogEntry(category As String, message As String) As String
    FormatLogEntry = "[" & GetTimeStamp() & "] [" & category & "] " & message
End Function

' Read entire content of log file
Private Function ReadLogFile() As String
    Dim fso As Object
    Dim logPath As String
    Dim fileObj As Object
    Dim content As String
    
    Set fso = GetFileSystemObject()
    logPath = GetLogFilePath()
    
    ' If file does not exist, return empty string
    If Not fso.FileExists(logPath) Then
        ReadLogFile = ""
        Exit Function
    End If
    
    ' Read file content
    Set fileObj = fso.OpenTextFile(logPath, 1) ' 1 = ForReading
    If Not fileObj.AtEndOfStream Then
        content = fileObj.ReadAll()
    End If
    fileObj.Close
    
    ReadLogFile = content
End Function

' ============== Public Logger Functions ==============

' /**
' * Log debug information (buffered)
' * Actual write happens when FlushLogBlock() is called
' * If DISABLE_LOGGER is True, this function returns immediately without action
' */
Public Sub LogDebug(message As String)
    If DISABLE_LOGGER Then Exit Sub
    
    Dim logEntry As String
    
    ' Initialize buffer if needed
    If logBuffer Is Nothing Then
        Set logBuffer = New Collection
    End If
    
    logEntry = FormatLogEntry(LOG_CATEGORY_DEBUG, message)
    logBuffer.Add logEntry
End Sub

' /**
' * Log error information (buffered)
' * Actual write happens when FlushLogBlock() is called
' * If DISABLE_LOGGER is True, this function returns immediately without action
' */
Public Sub LogError(message As String)
    If DISABLE_LOGGER Then Exit Sub
    
    Dim logEntry As String
    
    ' Initialize buffer if needed
    If logBuffer Is Nothing Then
        Set logBuffer = New Collection
    End If
    
    logEntry = FormatLogEntry(LOG_CATEGORY_ERROR, message)
    logBuffer.Add logEntry
End Sub

' /**
' * Generic info logging function (buffered)
' * Actual write happens when FlushLogBlock() is called
' * If DISABLE_LOGGER is True, this function returns immediately without action
' */
Public Sub LogInfo(message As String, Optional category As String = "INFO")
    If DISABLE_LOGGER Then Exit Sub
    
    Dim logEntry As String
    
    ' Initialize buffer if needed
    If logBuffer Is Nothing Then
        Set logBuffer = New Collection
    End If
    
    logEntry = FormatLogEntry(category, message)
    logBuffer.Add logEntry
End Sub

' ============== File Management Functions ==============

' /**
' * Clear log file
' * If DISABLE_LOGGER is True, this function returns True but performs no action
' */
Public Function ClearLog() As Boolean
    If DISABLE_LOGGER Then
        ClearLog = True
        Exit Function
    End If
    
    Dim fso As Object
    Dim logPath As String
    
    On Error GoTo ErrorHandler
    
    Set fso = GetFileSystemObject()
    logPath = GetLogFilePath()
    
    If fso.FileExists(logPath) Then
        fso.DeleteFile logPath
    End If
    
    ' Also clear buffer
    Set logBuffer = New Collection
    
    ClearLog = True
    Exit Function
    
ErrorHandler:
    ClearLog = False
End Function

' ============== Utility Functions ==============

' /**
' * Get the full path of the log file
' */
Public Function GetLogFileFullPath() As String
    GetLogFileFullPath = GetLogFilePath()
End Function

' /**
' * Check if log file exists
' */
Public Function LogFileExists() As Boolean
    Dim fso As Object
    Set fso = GetFileSystemObject()
    LogFileExists = fso.FileExists(GetLogFilePath())
End Function

' /**
' * Get current buffer size (for testing)
' */
Public Function GetBufferSize() As Long
    If logBuffer Is Nothing Then
        GetBufferSize = 0
    Else
        GetBufferSize = logBuffer.Count
    End If
End Function

' ============== Test Functions ==============

' /**
' * Test logger module with block-based logging
' */
Public Sub TestLogger()
    Dim timestamp As String
    
    On Error GoTo ErrorHandler
    
    ' Clear old log file
    ClearLog
    
    ' Test Block 1: Fetch operation
    StartLogBlock
    LogInfo "FetchDrawingFiles_Main started - Drawing: ABC123, PO: PO-001"
    LogInfo "Fetch completed successfully"
    FlushLogBlock
    
    ' Small delay to ensure different timestamps
    Application.Wait (Now + TimeValue("0:00:01"))
    
    ' Test Block 2: Link operation
    StartLogBlock
    LogInfo "LinkDrawingFile_Main started - Drawing: XYZ789, PO: PO-002"
    LogError "Warning: Some files had NULL part_id"
    LogInfo "Link completed with warnings"
    FlushLogBlock
    
    MsgBox "Test completed. Please check mp_log.txt at:" & vbCrLf & GetLogFileFullPath() & vbCrLf & vbCrLf & _
           "Expected format:" & vbCrLf & _
           "- Newest block first (Link operation)" & vbCrLf & _
           "- Blank line separator" & vbCrLf & _
           "- Older block (Fetch operation)" & vbCrLf & _
           "- Logs within each block in chronological order", _
           vbInformation, "Block Logger Test Complete"
    
    Exit Sub
    
ErrorHandler:
    MsgBox "Error occurred during test: " & Err.Description, vbCritical, "Test Error"
End Sub
