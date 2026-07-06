Private Sub Worksheet_SelectionChange(ByVal Target As Range)

    Dim ws As Worksheet
    Dim newSheet As Worksheet
    Dim templateSheet As Worksheet
    Dim sheetName As String
    Dim fallbackName As String
    Dim i As Long
    Dim found As Boolean
    
    ' Only trigger for column C, starting from row 2
    If Target.Column <> 3 Or Target.Row < 2 Then Exit Sub
    If Target.Cells.count > 1 Then Exit Sub
    
    sheetName = Trim(Me.Cells(Target.Row, 1).Value)
    
    ' If A column is empty, exit
    If sheetName = "" Then Exit Sub
    
    ' Try direct match
    On Error Resume Next
    Set ws = Worksheets(sheetName)
    On Error GoTo 0
    
    If Not ws Is Nothing Then
        ws.Activate
        Exit Sub
    End If
    
    ' Search upward from current row to row 2
    found = False
    For i = Target.Row - 1 To 2 Step -1
        fallbackName = Trim(Me.Cells(i, 1).Value)
        
        If fallbackName <> "" Then
            On Error Resume Next
            Set ws = Worksheets(fallbackName)
            On Error GoTo 0
            
            If Not ws Is Nothing Then
                found = True
                Exit For
            End If
        End If
    Next i
    
    ' If nothing found, use Template
    If Not found Then
        fallbackName = "Template"
        Set ws = Worksheets(fallbackName)
    End If
    
    ' Create new sheet from Template
    Set templateSheet = Worksheets("Template")
    templateSheet.Copy After:=ws
    Set newSheet = ActiveSheet
    
    ' Rename new sheet to current A value
    On Error Resume Next
    newSheet.Name = sheetName
    On Error GoTo 0
    
    ' Go to new sheet
    newSheet.Activate

End Sub
