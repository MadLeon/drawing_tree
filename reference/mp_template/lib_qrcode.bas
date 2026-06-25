'/**
' * lib_qrcode.bas
' * 
' * Module Functionality:
' *   - Generate QR codes containing manufacturing data (PO#, Job#, Line#, Drawing#)
' *   - Insert QR code images into Excel worksheets
' *   - Provide abstraction layer for API calls (easily switchable between providers)
' * 
' * Data Format:
' *   - Input: Four parameters (poNumber, jobNumber, lineNumber, drawingNumber)
' *   - Encoded format: "PO#,Job#,Line#,Drawing#"
' *   - Example: "RT79-79112-PN-R002,72422,1,59RT-79112-0105-01-DD-B"
' * 
' * API Provider:
' *   - Primary: QR-Server (api.qrserver.com) - Free, actively maintained
' * 
' * Usage:
' *   Dim qrImageUrl As String
' *   qrImageUrl = GenerateQRCode("RT79-79112-PN-R002", "72422", "1", "59RT-79112-0105-01-DD-B")
' *   Call InsertQRCodeImage(activeSheet, qrImageUrl, "A1", 100, 100)
' */

Option Explicit

' ============== Private Constants ==============
Private Const QR_API_PROVIDER As String = "QR_SERVER"
Private Const QR_SERVER_API_URL As String = "https://api.qrserver.com/v1/create-qr-code/"
Private Const QR_CODE_ENCODING As String = "UTF-8"
Private Const QR_CODE_SIZE_DEFAULT As Long = 100
Private Const HTTP_TIMEOUT_SECONDS As Long = 10

' ============== Public Functions ==============

'/**
' * Main public function to generate QR code
' * 
' * Parameters:
' *   - poNumber: Purchase Order number (e.g., "RT79-79112-PN-R002")
' *   - jobNumber: Job number (e.g., "72422")
' *   - lineNumber: Line number (e.g., "1")
' *   - drawingNumber: Drawing number (e.g., "59RT-79112-0105-01-DD-B")
' * 
' * Returns:
' *   - URL string of the generated QR code image
' *   - Empty string if generation fails
' * 
' * Example:
' *   url = GenerateQRCode("RT79-79112-PN-R002", "72422", "1", "59RT-79112-0105-01-DD-B")
' */
Public Function GenerateQRCode(poNumber As String, jobNumber As String, lineNumber As String, drawingNumber As String) As String
    Dim dataStr As String
    Dim qrUrl As String
    
    On Error GoTo ErrorHandler
    
    Call StartLogBlock
    Call LogInfo("QR Code generation started", "QR_GEN_START")
    Call LogDebug("PO: " & poNumber & " | Job: " & jobNumber & " | Line: " & lineNumber & " | Drawing: " & drawingNumber)
    
    ' Note: Empty fields are allowed and will be represented as empty strings in the encoded data
    ' Format: "PO,Job,Line,Drawing" (commas are always present even if fields are empty)
    
    ' Encode data in the specified format
    dataStr = EncodeQRData(poNumber, jobNumber, lineNumber, drawingNumber)
    Call LogDebug("Encoded data: " & dataStr)
    
    ' Generate QR code URL using the configured provider
    Select Case QR_API_PROVIDER
        Case "QR_SERVER"
            qrUrl = GenerateQRURL_QRServer(dataStr, QR_CODE_SIZE_DEFAULT)
        Case Else
            Call LogError("Unknown QR API provider: " & QR_API_PROVIDER)
            GenerateQRCode = ""
            Call FlushLogBlock
            Exit Function
    End Select
    
    If Len(qrUrl) = 0 Then
        Call LogError("Failed to generate QR code URL")
        GenerateQRCode = ""
        Call FlushLogBlock
        Exit Function
    End If
    
    Call LogInfo("QR Code URL generated successfully", "QR_GEN_SUCCESS")
    Call LogDebug("URL: " & qrUrl)
    Call FlushLogBlock
    
    GenerateQRCode = qrUrl
    Exit Function
    
ErrorHandler:
    Call LogError("GenerateQRCode error: " & Err.Description & " (Code: " & Err.Number & ")")
    Call FlushLogBlock
    GenerateQRCode = ""
End Function

'/**
' * Insert QR code image into worksheet at specified location
' * 
' * Parameters:
' *   - targetSheet: Worksheet object where image will be inserted
' *   - qrImageUrl: URL of QR code image
' *   - targetCell: Cell reference (e.g., "A1") or Range object
' *   - Optional width: Image width in points (default: 100)
' *   - Optional height: Image height in points (default: 100)
' * 
' * Returns:
' *   - True if insertion successful
' *   - False if operation fails
' * 
' * Example:
' *   Call InsertQRCodeImage(Sheet1, url, "A1", 150, 150)
' */
Public Function InsertQRCodeImage(targetSheet As Worksheet, qrImageUrl As String, targetCell As String, _
                                  Optional width As Long = 100, Optional height As Long = 100) As Boolean
    Dim cellRange As Range
    Dim pictureShape As Shape
    Dim imagePath As String
    
    On Error GoTo ErrorHandler
    
    Call StartLogBlock
    Call LogInfo("Inserting QR code image into worksheet", "IMAGE_INSERT_START")
    Call LogDebug("Target cell: " & targetCell & " | Size: " & width & "x" & height)
    
    ' Validate inputs
    If Len(qrImageUrl) = 0 Then
        Call LogError("QR image URL is empty")
        Call FlushLogBlock
        InsertQRCodeImage = False
        Exit Function
    End If
    
    ' Get target cell range
    On Error Resume Next
    Set cellRange = targetSheet.Range(targetCell)
    On Error GoTo ErrorHandler
    
    If cellRange Is Nothing Then
        Call LogError("Invalid target cell: " & targetCell)
        Call FlushLogBlock
        InsertQRCodeImage = False
        Exit Function
    End If
    
    ' Download and save temporary image file
    imagePath = DownloadQRImage(qrImageUrl)
    If Len(imagePath) = 0 Then
        Call LogError("Failed to download QR code image")
        Call FlushLogBlock
        InsertQRCodeImage = False
        Exit Function
    End If
    
    Call LogDebug("Image downloaded to: " & imagePath)
    
    ' Insert image at target location with 4-pixel offset (right and down)
    Set pictureShape = targetSheet.Shapes.AddPicture(imagePath, msoFalse, msoCTrue, _
                                                      cellRange.Left + 5, cellRange.Top + 5, width, height)
    
    If pictureShape Is Nothing Then
        Call LogError("Failed to insert picture shape")
        Call FlushLogBlock
        InsertQRCodeImage = False
        Exit Function
    End If
    
    Call LogInfo("QR code image inserted successfully", "IMAGE_INSERT_SUCCESS")
    Call LogDebug("Picture name: " & pictureShape.Name & " | Position: (" & pictureShape.Left & ", " & pictureShape.Top & ")")
    Call FlushLogBlock
    
    InsertQRCodeImage = True
    Exit Function
    
ErrorHandler:
    Call LogError("InsertQRCodeImage error: " & Err.Description & " (Code: " & Err.Number & ")")
    Call FlushLogBlock
    InsertQRCodeImage = False
End Function

' ============== Private Functions ==============

'/**
' * Encode data in the required format
' * 
' * Format: "PO#,Job#,Line#,Drawing#"
' * Example: "RT79-79112-PN-R002,72422,1,59RT-79112-0105-01-DD-B"
' */
Private Function EncodeQRData(poNumber As String, jobNumber As String, lineNumber As String, drawingNumber As String) As String
    EncodeQRData = poNumber & "," & jobNumber & "," & lineNumber & "," & drawingNumber
End Function

'/**
' * Generate QR code URL using QR-Server API
' * 
' * Parameters:
' *   - dataStr: Data to encode in QR code
' *   - size: QR code image size in pixels
' * 
' * Returns:
' *   - URL string for the QR code image
' * 
' * Note:
' *   - Uses qr-server.com which is actively maintained and free
' *   - Format: https://api.qrserver.com/v1/create-qr-code/?size=SIZExSIZE&data=DATA
' *   - QR-Server handles URL encoding automatically
' */
Private Function GenerateQRURL_QRServer(dataStr As String, size As Long) As String
    Dim sizeStr As String
    Dim apiUrl As String
    
    On Error GoTo ErrorHandler
    
    ' Build API URL (qr-server handles encoding automatically)
    sizeStr = CStr(size) & "x" & CStr(size)
    apiUrl = QR_SERVER_API_URL & "?size=" & sizeStr & "&data=" & dataStr
    
    Call LogDebug("QR-Server API URL constructed")
    GenerateQRURL_QRServer = apiUrl
    Exit Function
    
ErrorHandler:
    Call LogError("GenerateQRURL_QRServer error: " & Err.Description)
    GenerateQRURL_QRServer = ""
End Function

'/**
' * Download QR code image from URL and save to temporary file
' * 
' * Parameters:
' *   - imageUrl: URL of the QR code image
' * 
' * Returns:
' *   - Full path to temporary image file
' *   - Empty string if download fails
' * 
' * Note:
' *   - Creates temporary file in system temp folder
' *   - File name format: QRCode_TIMESTAMP.png
' *   - Caller is responsible for cleaning up temporary file
' */
Private Function DownloadQRImage(imageUrl As String) As String
    Dim xmlHttp As Object
    Dim fileStream As Object
    Dim fso As Object
    Dim tempPath As String
    Dim fileName As String
    Dim filePath As String
    
    On Error GoTo ErrorHandler
    
    ' Create MSXML HTTP request object
    ' Use generic ProgID for better compatibility across Windows versions
    Set xmlHttp = CreateObject("MSXML2.XMLHTTP")
    
    ' Open connection to URL
    xmlHttp.Open "GET", imageUrl, False
    xmlHttp.Send
    
    ' Check HTTP response status
    If xmlHttp.Status <> 200 Then
        Call LogError("HTTP request failed with status: " & xmlHttp.Status & " | URL: " & imageUrl)
        DownloadQRImage = ""
        Exit Function
    End If
    
    ' Get temp folder path
    Set fso = CreateObject("Scripting.FileSystemObject")
    tempPath = fso.GetSpecialFolder(2).Path ' Folder 2 = Temp folder
    
    ' Generate unique temp file name
    fileName = "QRCode_" & Format(Now(), "yyyyMMdd_HHmmss") & "_" & Int(Rnd() * 10000) & ".png"
    filePath = tempPath & "\" & fileName
    
    ' Create binary stream and write response data
    Set fileStream = CreateObject("ADODB.Stream")
    fileStream.Type = 1 ' adTypeBinary
    fileStream.Open
    fileStream.Write xmlHttp.ResponseBody
    fileStream.SaveToFile filePath, 2 ' adSaveCreateOverWrite
    fileStream.Close
    
    Call LogDebug("QR image downloaded and saved to: " & filePath)
    DownloadQRImage = filePath
    Exit Function
    
ErrorHandler:
    Call LogError("DownloadQRImage error: " & Err.Description & " (Code: " & Err.Number & ")")
    DownloadQRImage = ""
End Function
