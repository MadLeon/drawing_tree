Option Explicit

'/**
' * Update PO Status Ratio Module
' *
' * Purpose: Calculate and update the PO completion ratio for each sheet
' *
' * Main Function:
' *   - Update_PO_Status_Ratio: Calculates completion percentage for PO statuses
' *
' * Test Function:
' *   - Test_Update_PO_Status_Ratio: Quick verification of the update process
' */

'/**
' * Update_PO_Status_Ratio
' *
' * Description: Iterates through all sheets listed in the "PO Status" worksheet
' * and calculates the completion ratio based on non-blank cells in specific columns.
' *
' * Layout: Auto-detects old vs new format per sheet:
' *   Old: A=drawing, B+C=completion | E=drawing, F+G=completion
' *   New: B=drawing, C+D=completion | G=drawing, H+I=completion (A/F = Job No, ignored)
' *
' * Calculation: Only counts rows where the drawing column has a value.
' * Empty drawing rows are excluded from the denominator.
' *
' * Formula: (Non-empty completion cells) / (Total drawing numbers) = Completion Ratio
' *
' * Process Flow:
' *   1. Read sheet names from "PO Status" column A (rows 2 onwards)
' *   2. For each sheet name, find the target worksheet
' *   3. For each row from 4 to last row:
' *      - If A has drawing, check if B or C has completion info
' *      - If E has drawing, check if F or G has completion info
' *   4. Calculate and format completion ratio in "PO Status" column B
' *
' * Parameters: None
' * Returns: None (updates "PO Status" worksheet in place)
' */
Sub Update_PO_Status_Ratio()

    Dim wsStatus As Worksheet
    Dim wsTarget As Worksheet
    Dim lastRowStatus As Long
    Dim lastRowTarget As Long
    Dim i As Long
    Dim rowIdx As Long
    Dim sheetName As String
    Dim nonBlank As Long
    Dim total As Long
    Dim isNewFmt As Boolean
    Dim colDraw1 As Long, colComp1A As Long, colComp1B As Long
    Dim colDraw2 As Long, colComp2A As Long, colComp2B As Long
    Dim colLastRow As Long
    
    ' Reference the "PO Status" worksheet
    Set wsStatus = Worksheets("PO Status")
    
    ' Find the last row with data in "PO Status" sheet
    lastRowStatus = wsStatus.Cells(wsStatus.Rows.count, "A").End(xlUp).Row
    
    ' Process each sheet name in the list
    For i = 2 To lastRowStatus
        
        sheetName = Trim(wsStatus.Cells(i, "A").Value)
        
        ' Skip if sheet name is empty
        If sheetName <> "" Then
            
            ' Attempt to reference the target worksheet
            Set wsTarget = Nothing
            On Error Resume Next
            Set wsTarget = Worksheets(sheetName)
            On Error GoTo 0
            
            ' If worksheet exists, calculate the completion ratio
            If Not wsTarget Is Nothing Then

                ' Detect format and set column indices accordingly
                ' Old: A=drawing(1), B+C=completion(2,3) | E=drawing(5), F+G=completion(6,7)
                ' New: B=drawing(2), C+D=completion(3,4) | G=drawing(7), H+I=completion(8,9)
                isNewFmt = DetectNewFormat(wsTarget)
                If isNewFmt Then
                    colDraw1 = 2: colComp1A = 3: colComp1B = 4
                    colDraw2 = 7: colComp2A = 8: colComp2B = 9
                    colLastRow = 2
                Else
                    colDraw1 = 1: colComp1A = 2: colComp1B = 3
                    colDraw2 = 5: colComp2A = 6: colComp2B = 7
                    colLastRow = 1
                End If

                ' Find the last row in the target worksheet
                lastRowTarget = wsTarget.Cells(wsTarget.Rows.Count, colLastRow).End(xlUp).Row
                If lastRowTarget < 4 Then lastRowTarget = 4

                ' Initialize counters
                nonBlank = 0
                total = 0

                ' Iterate through data rows (starting from row 4)
                For rowIdx = 4 To lastRowTarget

                    ' Check left drawing column
                    If Trim(wsTarget.Cells(rowIdx, colDraw1).Value) <> "" Then
                        total = total + 2
                        If Trim(wsTarget.Cells(rowIdx, colComp1A).Value) <> "" Then nonBlank = nonBlank + 1
                        If Trim(wsTarget.Cells(rowIdx, colComp1B).Value) <> "" Then nonBlank = nonBlank + 1
                    End If

                    ' Check right drawing column
                    If Trim(wsTarget.Cells(rowIdx, colDraw2).Value) <> "" Then
                        total = total + 2
                        If Trim(wsTarget.Cells(rowIdx, colComp2A).Value) <> "" Then nonBlank = nonBlank + 1
                        If Trim(wsTarget.Cells(rowIdx, colComp2B).Value) <> "" Then nonBlank = nonBlank + 1
                    End If

                Next rowIdx
                
                ' Calculate and format the ratio
                If total > 0 Then
                    wsStatus.Cells(i, "B").Value = Round(nonBlank / total, 3)
                    wsStatus.Cells(i, "B").NumberFormat = "0.0%"
                Else
                    wsStatus.Cells(i, "B").Value = ""
                End If
                
            Else
                ' Clear the cell if target sheet not found
                wsStatus.Cells(i, "B").Value = ""
            End If
            
            End If
        
    Next i
    
End Sub

'/**
' * Test_Update_PO_Status_Ratio
' *
' * Description: Test function to quickly verify the Update_PO_Status_Ratio macro
' *
' * Usage: Run this sub from the VBA editor to test the main function
' * Expected Result: Completion ratios are calculated and displayed in PO Status sheet
' */
Sub Test_Update_PO_Status_Ratio()
    
    MsgBox "Starting PO Status Ratio update test...", vbInformation, "Test: PO Status Update"
    
    ' Execute the main update function
    Call Update_PO_Status_Ratio
    
    MsgBox "PO Status Ratio update completed. Please check the PO Status worksheet.", vbInformation, "Test: Complete"
    
End Sub

