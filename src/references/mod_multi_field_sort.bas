'/**
' * Multi-Field Sorting Module
' * 
' * This module provides functionality to sort text entries with multiple
' * "-" delimited fields, using left-to-right field priority.
' * 
' * Dependencies:
' * - dat_global_variable.bas (Global variables and DEBUG_MODE)
' * - lib_logger.bas (Logging functionality)
' * 
' * Usage:
' * - Call TestMultiFieldSort() to test the functionality
' * - Call SortByMultipleFields(inputArray) to sort an array
' */

Option Explicit

'/**
' * Parses a delimited string into an array of fields
' * 
' * Splits the input string by "-" delimiter and returns an array of fields.
' * Returns an empty array if input is empty.
' * 
' * Examples:
' * - "as-15111-r001" -> ["as", "15111", "r001"]
' * - "cs-11111-r001" -> ["cs", "11111", "r001"]
' * 
' * @param {String} inputStr - The delimited string to parse
' * @param {String} delimiter - The delimiter character (default: "-")
' * @return {Variant} Array of fields, or empty array if input is empty
' */
Function ParseFields(inputStr As String, Optional delimiter As String = "-") As Variant
    Dim fields() As String
    Dim parts As Variant
    Dim i As Long
    Dim fieldCount As Long
    Dim currentField As String
    Dim charIndex As Long
    
    On Error GoTo ErrorHandler
    
    ' Check if input is empty
    If Len(inputStr) = 0 Then
        ReDim fields(0)
        ParseFields = fields
        Exit Function
    End If
    
    ' Simple string split logic without using built-in Split function
    ' to ensure compatibility
    fieldCount = 0
    currentField = ""
    
    For charIndex = 1 To Len(inputStr)
        Dim currentChar As String
        currentChar = Mid(inputStr, charIndex, 1)
        
        If currentChar = delimiter Then
            ReDim Preserve fields(fieldCount)
            fields(fieldCount) = currentField
            fieldCount = fieldCount + 1
            currentField = ""
        Else
            currentField = currentField & currentChar
        End If
    Next charIndex
    
    ' Add the last field
    ReDim Preserve fields(fieldCount)
    fields(fieldCount) = currentField
    
    ParseFields = fields
    Exit Function
    
ErrorHandler:
    Call LogError("ParseFields failed for input: " & inputStr & ", Error: " & Err.Description)
    Dim emptyArray() As String
    ParseFields = emptyArray
End Function

'/**
' * Compares two field arrays using left-to-right priority
' * 
' * Returns:
' * - -1 if arr1 < arr2 (arr1 should come first)
' * - 0 if arr1 = arr2 (equal)
' * - 1 if arr1 > arr2 (arr1 should come after)
' * 
' * Comparison is case-insensitive and uses standard text comparison.
' * 
' * Examples:
' * - CompareFields(["as", "11111"], ["bs", "11111"]) -> -1 (as < bs)
' * - CompareFields(["as", "15111"], ["as", "13111"]) -> 1 (15111 > 13111)
' * - CompareFields(["as", "11111", "r001"], ["as", "11111", "r004"]) -> -1 (r001 < r004)
' * 
' * @param {Variant} fieldsArr1 - First field array
' * @param {Variant} fieldsArr2 - Second field array
' * @return {Integer} -1, 0, or 1 based on comparison
' */
Function CompareFields(fieldsArr1 As Variant, fieldsArr2 As Variant) As Integer
    Dim i As Long
    Dim maxFields As Long
    Dim field1 As String
    Dim field2 As String
    Dim len1 As Long
    Dim len2 As Long
    
    On Error GoTo ErrorHandler
    
    ' Determine the maximum number of fields
    maxFields = UBound(fieldsArr1)
    If UBound(fieldsArr2) > maxFields Then
        maxFields = UBound(fieldsArr2)
    End If
    
    ' Compare field by field from left to right
    For i = 0 To maxFields
        ' Get current fields, use empty string if field doesn't exist
        If i <= UBound(fieldsArr1) Then
            field1 = LCase(fieldsArr1(i))
        Else
            field1 = ""
        End If
        
        If i <= UBound(fieldsArr2) Then
            field2 = LCase(fieldsArr2(i))
        Else
            field2 = ""
        End If
        
        ' If fields are different, return comparison result
        If field1 <> field2 Then
            If field1 < field2 Then
                CompareFields = -1
                Exit Function
            Else
                CompareFields = 1
                Exit Function
            End If
        End If
    Next i
    
    ' All fields are equal
    CompareFields = 0
    Exit Function
    
ErrorHandler:
    Call LogError("CompareFields failed: " & Err.Description)
    CompareFields = 0
End Function

'/**
' * Sorts an array of delimited strings by multiple fields
' * 
' * Pre-parses all fields before sorting (optimization for large datasets)
' * Uses bubble sort algorithm with pre-parsed fields to avoid repeated parsing
' * 
' * @param {Variant} inputArray - Array of delimited strings to sort
' * @param {String} delimiter - The delimiter character (default: "-")
' * @return {Boolean} True if successful, False otherwise
' */
Function SortByMultipleFields(inputArray As Variant, Optional delimiter As String = "-") As Boolean
    Dim i As Long
    Dim j As Long
    Dim arrayLength As Long
    Dim compareResult As Integer
    Dim temp As String
    Dim tempFields As Variant
    Dim swapped As Boolean
    Dim parsedFieldsArray() As Variant
    
    On Error GoTo ErrorHandler
    
    arrayLength = UBound(inputArray)
    Call LogInfo("Starting multi-field sort with " & (arrayLength + 1) & " items (pre-parsing all fields)", "SortByMultipleFields")
    
    ' Pre-parse all fields to avoid repeated parsing during sort
    ReDim parsedFieldsArray(0 To arrayLength)
    For i = 0 To arrayLength
        parsedFieldsArray(i) = ParseFields(CStr(inputArray(i)), delimiter)
    Next i
    
    Call LogInfo("Field parsing completed, starting bubble sort", "SortByMultipleFields")
    
    ' Bubble sort with pre-parsed fields (no repeated parsing)
    For i = 0 To arrayLength - 1
        swapped = False
        
        For j = 0 To arrayLength - i - 1
            ' Compare using pre-parsed fields
            compareResult = CompareFields(parsedFieldsArray(j), parsedFieldsArray(j + 1))
            
            If compareResult > 0 Then
                ' Swap both original string and parsed fields
                temp = inputArray(j)
                inputArray(j) = inputArray(j + 1)
                inputArray(j + 1) = temp
                
                tempFields = parsedFieldsArray(j)
                parsedFieldsArray(j) = parsedFieldsArray(j + 1)
                parsedFieldsArray(j + 1) = tempFields
                swapped = True
            End If
        Next j
        
        ' If no swaps occurred, array is sorted
        If Not swapped Then
            Exit For
        End If
    Next i
    
    Call LogInfo("Multi-field sort completed successfully", "SortByMultipleFields")
    SortByMultipleFields = True
    Exit Function
    
ErrorHandler:
    Call LogError("SortByMultipleFields failed: " & Err.Description)
    SortByMultipleFields = False
End Function

'/**
' * Test function to validate multi-field sorting functionality
' * 
' * Tests various sorting scenarios including:
' * - Single field comparison
' * - Multi-field sorting
' * - Mixed alphanumeric fields
' * - Duplicate field values with different subsequent fields
' * 
' * Usage: Call TestMultiFieldSort() from Excel VBA editor
' */
Sub TestMultiFieldSort()
    Dim testData() As String
    Dim sortedData() As String
    Dim i As Long
    Dim result As Boolean
    Dim msg As String
    
    On Error GoTo ErrorHandler
    
    Call StartLogBlock()
    Call LogInfo("=== TestMultiFieldSort: Starting Test ===", "TestMultiFieldSort")
    
    ' Initialize test data (unsorted)
    ReDim testData(7)
    testData(0) = "cs-11111-r001"
    testData(1) = "as-15111-r001"
    testData(2) = "as-13111-r001"
    testData(3) = "as-12111-r001"
    testData(4) = "as-15111-r001"
    testData(5) = "bs-11111-r001"
    testData(6) = "as-11111-r004"
    testData(7) = "as-11111-r001"
    
    Call LogInfo("Test data (unsorted):", "TestMultiFieldSort")
    For i = 0 To UBound(testData)
        Call LogInfo("  [" & i & "] " & testData(i), "TestMultiFieldSort")
    Next i
    
    ' Copy to sorted array
    ReDim sortedData(UBound(testData))
    For i = 0 To UBound(testData)
        sortedData(i) = testData(i)
    Next i
    
    ' Perform sort
    result = SortByMultipleFields(sortedData, "-")
    
    Call LogInfo("Test data (sorted):", "TestMultiFieldSort")
    For i = 0 To UBound(sortedData)
        Call LogInfo("  [" & i & "] " & sortedData(i), "TestMultiFieldSort")
    Next i
    
    ' Build result message
    msg = "Test completed. Sort result: " & IIf(result, "SUCCESS", "FAILED") & vbCrLf & vbCrLf & "Sorted data:" & vbCrLf
    For i = 0 To UBound(sortedData)
        msg = msg & sortedData(i) & vbCrLf
    Next i
    
    MsgBox msg, vbInformation, "Multi-Field Sort Test"
    
    Call LogInfo("=== TestMultiFieldSort: Test Complete ===", "TestMultiFieldSort")
    Call FlushLogBlock()
    Exit Sub
    
ErrorHandler:
    Call LogError("TestMultiFieldSort failed: " & Err.Description)
    Call FlushLogBlock()
    MsgBox "Test failed: " & Err.Description, vbCritical, "Error"
End Sub
