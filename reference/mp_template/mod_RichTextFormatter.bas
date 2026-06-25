'/**
' * Rich Text Format Handler (mod_RichTextFormatter.bas)
' * 
' * Converts markdown-style bold markers (**text**) into Excel Rich Text format
' * Uses Excel's Characters property to apply font formatting
' */

Option Explicit

' /**
'  * Convert **text** markers to Rich Text bold formatting in a cell
'  * 
'  * Process:
'  * 1. Parse input text for **..** patterns
'  * 2. Create a list of (startPos, endPos, isBold) for each segment
'  * 3. Set cell value to text without ** markers
'  * 4. Apply formatting using Characters().Font.Bold
'  * 
'  * Example:
'  *   Input: "C-1018 HRS as per BOM c/w **Cert**"
'  *   Output cell: "C-1018 HRS as per BOM c/w Cert" with "Cert" in bold
'  * 
'  * @param targetCell: The cell to set (Range object)
'  * @param textWithMarkers: Text containing **bold** markers
'  */
Public Sub SetCellWithBoldFormat(targetCell As Range, textWithMarkers As String)
    Dim cleanText As String
    Dim formatSegments As Collection
    Dim i As Long
    Dim segment As Object
    
    On Error GoTo ErrorHandler
    
    ' Parse the text and create format segments
    Set formatSegments = ParseBoldMarkers(textWithMarkers, cleanText)
    
    ' Set the cell value to clean text (without ** markers)
    targetCell.value = cleanText
    
    ' Apply formatting to segments
    For i = 1 To formatSegments.Count
        Set segment = formatSegments(i)
        
        If segment("isBold") Then
            ' Apply bold to this character range
            With targetCell.Characters(segment("startPos"), segment("length")).Font
                .Bold = True
            End With
        End If
    Next i
    
    Exit Sub
    
ErrorHandler:
    ' If rich text fails, fall back to plain text with markers
    targetCell.value = textWithMarkers
    Call lib_logger.LogError("SetCellWithBoldFormat failed: " & Err.Description)
End Sub

' /**
'  * Parse **bold** markers and return format information
'  * 
'  * @param inputText: Text with **bold** markers
'  * @param cleanText: Output - text without ** markers
'  * @return: Collection of format segments {startPos, length, isBold}
'  */
Private Function ParseBoldMarkers(inputText As String, ByRef cleanText As String) As Collection
    Dim results As Collection
    Dim i As Long
    Dim char As String
    Dim cleanPos As Long
    Dim currentPos As Long
    Dim inBold As Boolean
    Dim boldStartClean As Long
    Dim boldLength As Long
    Dim segmentObj As Object
    
    Set results = New Collection
    
    cleanText = ""
    cleanPos = 1
    currentPos = 1
    inBold = False
    boldStartClean = 0
    
    ' Iterate through input text
    While currentPos <= Len(inputText)
        
        ' Check for ** marker
        If currentPos + 1 <= Len(inputText) And Mid(inputText, currentPos, 2) = "**" Then
            
            If inBold Then
                ' End of bold section
                boldLength = cleanPos - boldStartClean
                
                ' Add format segment
                Set segmentObj = CreateObject("Scripting.Dictionary")
                segmentObj.Add "startPos", boldStartClean
                segmentObj.Add "length", boldLength
                segmentObj.Add "isBold", True
                results.Add segmentObj
                
                inBold = False
            Else
                ' Start of bold section
                boldStartClean = cleanPos
                inBold = True
            End If
            
            ' Skip the ** marker
            currentPos = currentPos + 2
            
        Else
            ' Regular character
            char = Mid(inputText, currentPos, 1)
            cleanText = cleanText & char
            cleanPos = cleanPos + 1
            currentPos = currentPos + 1
            
        End If
    
    Wend
    
    ' Handle unclosed bold (edge case)
    If inBold Then
        boldLength = cleanPos - boldStartClean
        Set segmentObj = CreateObject("Scripting.Dictionary")
        segmentObj.Add "startPos", boldStartClean
        segmentObj.Add "length", boldLength
        segmentObj.Add "isBold", True
        results.Add segmentObj
    End If
    
    Set ParseBoldMarkers = results
End Function

' /**
'  * Test function - verify bold formatting works
'  * Output: Debug.Print statements (Ctrl+J to see)
'  */
Public Sub TestRichTextFormatting()
    Dim ws As Worksheet
    Dim testCell As Range
    Dim testText As String
    Dim cleanText As String
    Dim segments As Collection
    Dim i As Long
    
    On Error GoTo ErrorHandler
    
    Set ws = ThisWorkbook.Sheets(MP_SHEET_NAME)
    Set testCell = ws.Range("AA10")
    
    Debug.Print "========== RICH TEXT FORMATTING TEST =========="
    Debug.Print ""
    
    ' Test 1: Simple bold
    testText = "C-1018 HRS as per BOM c/w **Cert**"
    Debug.Print "Test 1: Simple bold"
    Debug.Print "  Input: [" & testText & "]"
    
    Set segments = ParseBoldMarkers(testText, cleanText)
    Debug.Print "  Clean: [" & cleanText & "]"
    Debug.Print "  Segments: " & segments.Count
    For i = 1 To segments.Count
        Debug.Print "    [" & segments(i)("startPos") & ", " & segments(i)("length") & "] = Bold"
    Next i
    
    Call SetCellWithBoldFormat(testCell, testText)
    Debug.Print "  ✓ Applied to cell AA10"
    Debug.Print ""
    
    ' Test 2: Multiple bold sections
    testText = "**Part1** as per BOM c/w **Cert**"
    Debug.Print "Test 2: Multiple bold"
    Debug.Print "  Input: [" & testText & "]"
    
    Set segments = ParseBoldMarkers(testText, cleanText)
    Debug.Print "  Clean: [" & cleanText & "]"
    Debug.Print "  Segments: " & segments.Count
    For i = 1 To segments.Count
        Debug.Print "    [" & segments(i)("startPos") & ", " & segments(i)("length") & "] = Bold"
    Next i
    
    Call SetCellWithBoldFormat(testCell, testText)
    Debug.Print "  ✓ Applied to cell AA11"
    Debug.Print ""
    
    ' Test 3: No bold
    testText = "Plain text without bold"
    Debug.Print "Test 3: No bold"
    Debug.Print "  Input: [" & testText & "]"
    
    Set segments = ParseBoldMarkers(testText, cleanText)
    Debug.Print "  Clean: [" & cleanText & "]"
    Debug.Print "  Segments: " & segments.Count
    
    testCell.value = testText
    Debug.Print "  ✓ Applied to cell AA12"
    Debug.Print ""
    
    Debug.Print "========== TEST COMPLETE =========="
    
    Exit Sub
    
ErrorHandler:
    Debug.Print "ERROR: " & Err.Description
End Sub
