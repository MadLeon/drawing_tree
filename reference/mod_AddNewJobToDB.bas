' mod_AddNewJobToDB.bas
' -------------------------------------------------------------------------------------------------
' Module Functionality:
'   - Insert new job entry with cascading updates to multiple tables in record.db
'   - Handles: purchase_order → job → order_item → part relationships
' -------------------------------------------------------------------------------------------------
Option Explicit

' Insert or update purchase_order, then create job and order_item entries
'
' This procedure performs cascading inserts:
' 1. Find or create customer_contact by contact name and customer name
' 2. Find or create purchase_order with po_number (linked to customer_contact)
' 3. Create job entry with job_number (linked to purchase_order)
' 4. Find or create part by drawing_number, then create order_item (linked to job and part)
'
' Workflow:
' - Read form inputs from Input Form sheet
' - Validate critical data (OE, JobNum)
' - Execute multi-level insert operations
' - Write order_item ID to DELIVERY SCHEDULE column AA (column 27)
Sub AddNewJobToDB(Optional rowNumber As Long = 0)
    Dim dbPath As String, db As Object
    Dim inputWS As Worksheet
    Dim lastRowDelivery As Long
    Dim deliveryWS As Worksheet
    Dim startTime As Double, stepTime As Double
    
    startTime = Timer

    ' ========== Step 1: Set Object Variables and Initialize DB ==========
    dbPath = mod_PublicData.DB_PATH ' Use public constant from mod_PublicData
    Set inputWS = ThisWorkbook.Sheets("Input Form")

    ' Only initialize if not already initialized (avoid duplicate initialization)
    ' Check by attempting a simple query
    Dim testResults As Variant
    testResults = mod_SQLite.ExecuteQuery("SELECT 1")
    If IsNull(testResults) Then
        If Not mod_SQLite.InitializeSQLite(dbPath) Then
            MsgBox "Failed to initialize database: " & dbPath, vbCritical
            Exit Sub
        End If
    End If

    ' ========== Step 2: Collect Input Data ==========
    Dim oeNumber As String, jobNumber As String, customerName As String
    Dim partNumber As String, revision As String, quantity As String
    Dim poNumber As String, lineNumber As String, actualPrice As String
    Dim deliveryRequiredDate As String, customerContact As String
    Dim description As String, drawingReleaseDate As String, todayDate As Date

    oeNumber = Trim(inputWS.Range("OE").Value)
    jobNumber = Trim(inputWS.Range("JobNum").Value)
    customerName = Trim(inputWS.Range("Customer").Value)
    partNumber = Trim(inputWS.Range("Parts").Value)
    revision = Trim(inputWS.Range("Revision").Value)
    quantity = Trim(inputWS.Range("qty").Value)
    poNumber = Trim(inputWS.Range("po").Value)
    lineNumber = Trim(inputWS.Range("poline").Value)
    actualPrice = Trim(inputWS.Range("price").Value)
    deliveryRequiredDate = Trim(inputWS.Range("date").Value)
    customerContact = Trim(inputWS.Range("contact").Value)
    description = Trim(inputWS.Range("desc").Value)

    ' Convert delivery date format from YYYY.M.D to YYYY-MM-DD
    If deliveryRequiredDate <> "" Then
        deliveryRequiredDate = FormatDateForDatabase(deliveryRequiredDate)
    End If

    ' Get current system date for drawing_release_date
    ' Format as M/D/YYYY for display in H column, and YYYY-MM-DD for database
    todayDate = Date()
    drawingReleaseDate = Format(todayDate, "YYYY-MM-DD")

    ' ========== Generate PO Number if Empty or NPO ==========
    ' If PO is empty or "NPO", generate a fixed format: NPO-{oe_number}
    ' One OE number corresponds to one PO, which can have multiple jobs and order items
    If poNumber = "" Or UCase(poNumber) = "NPO" Or UCase(poNumber) = "NPO#" Then
        poNumber = "NPO-" & oeNumber
    End If

    ' ========== Normalize PO Number ==========
    ' Apply standardization rules: uppercase, remove spaces, convert Rev. to R., add decimals, remove leading zeros
    poNumber = NormalizePONumber(poNumber)

    ' ========== Step 3: Validate Critical Input ==========
    If oeNumber = "" Or jobNumber = "" Or poNumber = "" Then
        MsgBox "Critical data missing: OE, JobNum, and PO are required!", vbCritical
        mod_SQLite.CloseSQLite
        Exit Sub
    End If

    On Error GoTo HandleError

    ' ========== Step 4: Cascading Insert Operations ==========

    ' 4.1 Find or Create Customer
    stepTime = Timer
    Dim customerId As Variant
    customerId = FindOrCreateCustomer(customerName)
    Debug.Print "Step 4.1 (Find/Create Customer): " & (Timer - stepTime) & "s"
    If IsNull(customerId) Then
        Debug.Print "Failed to find or create customer: " & customerName
        mod_SQLite.CloseSQLite
        Exit Sub
    End If

    ' 4.2 Find or Create Customer Contact
    stepTime = Timer
    Dim contactId As Variant
    contactId = FindOrCreateCustomerContact(CLng(customerId), customerContact)
    Debug.Print "Step 4.2 (Find/Create Contact): " & (Timer - stepTime) & "s"
    If IsNull(contactId) Then
        Debug.Print "Failed to find or create customer contact: " & customerContact
        mod_SQLite.CloseSQLite
        Exit Sub
    End If

    ' 4.3 Find or Create Purchase Order
    stepTime = Timer
    Dim poId As Variant
    poId = FindOrCreatePurchaseOrder(poNumber, oeNumber, CLng(contactId))
    Debug.Print "Step 4.3 (Find/Create PO): " & (Timer - stepTime) & "s"
    If IsNull(poId) Then
        Debug.Print "Failed to find or create purchase order: " & poNumber
        mod_SQLite.CloseSQLite
        Exit Sub
    End If

    ' 4.4 Find or Create Job
    stepTime = Timer
    Dim jobId As Variant
    jobId = FindOrCreateJob(jobNumber, CLng(poId))
    Debug.Print "Step 4.4 (Find/Create Job): " & (Timer - stepTime) & "s"
    If IsNull(jobId) Then
        Debug.Print "Failed to find or create job: " & jobNumber
        mod_SQLite.CloseSQLite
        Exit Sub
    End If

    ' 4.5 Find or Create Part
    stepTime = Timer
    Dim partId As Variant
    If partNumber <> "" Then
        partId = FindOrCreatePart(partNumber, revision, description)
    Else
        partId = Null
    End If
    Debug.Print "Step 4.5 (Find/Create Part): " & (Timer - stepTime) & "s"

    ' 4.6 Create Order Item
    stepTime = Timer
    Dim orderItemId As Variant
    orderItemId = CreateOrderItem(CLng(jobId), partId, lineNumber, quantity, actualPrice, deliveryRequiredDate, drawingReleaseDate)
    Debug.Print "Step 4.6 (Create Order Item): " & (Timer - stepTime) & "s"
    If IsNull(orderItemId) Or orderItemId = "" Or CLng(orderItemId) = 0 Then
        Debug.Print "ERROR: Failed to create or update order item - returned: " & IIf(IsNull(orderItemId), "Null", orderItemId)
        Exit Sub
    End If

    ' ========== Step 5: Write Order Item ID to Spreadsheet ==========
    ' If a row number was provided, write the order_item ID to column AA (column 27)
    ' and write the drawing_release_date to column H (column 8) in M/D/YYYY format
    If rowNumber > 0 Then
        On Error Resume Next
        Set deliveryWS = ThisWorkbook.Sheets("DELIVERY SCHEDULE")
        If Not deliveryWS Is Nothing Then
            ' Disable events temporarily to prevent Worksheet_Change from triggering
            Application.EnableEvents = False
            Dim orderItemIdValue As Long
            orderItemIdValue = CLng(orderItemId)
            
            ' Write order_item ID to column AA (column 27)
            deliveryWS.Cells(rowNumber, 27).Value = orderItemIdValue
            Debug.Print "Order Item ID " & orderItemIdValue & " written to DELIVERY SCHEDULE row " & rowNumber & " column AA"
            
            ' Write drawing_release_date to column H (column 8) in M/D/YYYY format
            ' Convert from YYYY-MM-DD to M/D/YYYY
            Dim displayDate As String
            displayDate = Format(todayDate, "M/D/YYYY")
            deliveryWS.Cells(rowNumber, 8).Value = displayDate
            Debug.Print "Drawing Release Date " & displayDate & " written to DELIVERY SCHEDULE row " & rowNumber & " column H"
            
            Application.EnableEvents = True
        End If
        On Error GoTo 0
    End If

    ' ========== Step 6: Complete ==========
    ' Note: DELIVERY SCHEDULE update and hyperlink are handled by AddNextNewRecord/EMT, not here
    ' This function only handles database operations (cascading inserts)

    ' NOTE: Do NOT close database here - let the caller manage the connection
    ' This allows AddHyperlink, AddNewJobToDB to share the same connection
    ' and be called sequentially without repeated initialization
    
    Debug.Print "Order item created successfully! Order Item ID: " & orderItemId
    Debug.Print "========== TOTAL TIME: " & (Timer - startTime) & " seconds =========="

    Exit Sub

HandleError:
    MsgBox "Error: " & Err.Description, vbCritical
    ' NOTE: Do NOT close database here either - let the caller handle cleanup
End Sub

' Normalize PO number according to standard rules
' Rules: remove spaces, uppercase, Rev.→R., ensure dash before R., -RN→-R.N, R.0N→R.N
Function NormalizePONumber(poNumber As String) As String
    Dim normalized As String
    Dim i As Long
    Dim char As String
    Dim result As String
    
    If Trim(poNumber) = "" Then
        NormalizePONumber = ""
        Exit Function
    End If
    
    normalized = poNumber
    
    ' Step 1: Remove all spaces
    normalized = Replace(normalized, " ", "")
    
    ' Step 2: Convert to uppercase
    normalized = UCase(normalized)
    
    ' Step 3: Replace REV. with R.
    normalized = Replace(normalized, "REV.", "R.")
    
    ' Step 4: Ensure dash before R.
    result = ""
    For i = 1 To Len(normalized)
        char = Mid(normalized, i, 1)
        If i < Len(normalized) - 1 Then
            If char Like "[A-Z0-9]" And Mid(normalized, i + 1, 2) = "R." Then
                result = result & char & "-"
            Else
                result = result & char
            End If
        Else
            result = result & char
        End If
    Next i
    normalized = result
    
    ' Step 5: Convert -RN to -R.N (single digit only)
    result = ""
    i = 1
    Do While i <= Len(normalized)
        If i <= Len(normalized) - 2 Then
            If Mid(normalized, i, 2) = "-R" And Mid(normalized, i + 2, 1) Like "[0-9]" Then
                If i + 3 > Len(normalized) Or Not (Mid(normalized, i + 3, 1) Like "[0-9]") Then
                    result = result & "-R." & Mid(normalized, i + 2, 1)
                    i = i + 3
                    GoTo continue_loop
                End If
            End If
        End If
        result = result & Mid(normalized, i, 1)
        i = i + 1
continue_loop:
    Loop
    normalized = result
    
    ' Step 6: Remove leading zeros from R.0N
    normalized = Replace(normalized, "R.0", "R.")
    
    ' Step 7: Clean up spaces around dashes
    result = ""
    For i = 1 To Len(normalized)
        char = Mid(normalized, i, 1)
        If char <> " " Then
            result = result & char
        End If
    Next i
    
    NormalizePONumber = result
End Function

' Find existing customer or create new one
' Parameters: customerName - Name of the customer
' Returns: Customer ID, or Null on failure
Function FindOrCreateCustomer(customerName As String) As Variant
    Dim results As Variant
    Dim insertSQL As String

    ' Try to find existing customer
    results = mod_SQLite.ExecuteQuery("SELECT id FROM customer WHERE customer_name = '" & Replace(customerName, "'", "''") & "' LIMIT 1")

    If Not IsNull(results) Then
        ' Customer exists
        FindOrCreateCustomer = results(0)(0)
        Exit Function
    End If

    ' Customer doesn't exist, create new one
    insertSQL = "INSERT INTO customer (customer_name, usage_count, created_at, updated_at) " & _
                "VALUES ('" & Replace(customerName, "'", "''") & "', 1, datetime('now', 'localtime'), datetime('now', 'localtime'))"

    If mod_SQLite.ExecuteNonQuery(insertSQL) Then
        ' Return last inserted ID
        results = mod_SQLite.ExecuteQuery("SELECT last_insert_rowid()")
        If Not IsNull(results) Then
            FindOrCreateCustomer = results(0)(0)
        End If
    End If
End Function

' Find existing customer contact or create new one
' Parameters: customerId - Customer ID
'             contactName - Contact person name
' Returns: Contact ID, or Null on failure
Function FindOrCreateCustomerContact(customerId As Long, contactName As String) As Variant
    Dim results As Variant
    Dim insertSQL As String

    ' Try to find existing contact for this customer
    results = mod_SQLite.ExecuteQuery("SELECT id FROM customer_contact " & _
                                      "WHERE customer_id = " & customerId & " " & _
                                      "AND contact_name = '" & Replace(contactName, "'", "''") & "' LIMIT 1")

    If Not IsNull(results) Then
        ' Contact exists
        FindOrCreateCustomerContact = results(0)(0)
        Exit Function
    End If

    ' Contact doesn't exist, create new one
    insertSQL = "INSERT INTO customer_contact (customer_id, contact_name, usage_count, created_at, updated_at) " & _
                "VALUES (" & customerId & ", '" & Replace(contactName, "'", "''") & "', 1, datetime('now', 'localtime'), datetime('now', 'localtime'))"

    If mod_SQLite.ExecuteNonQuery(insertSQL) Then
        ' Return last inserted ID
        results = mod_SQLite.ExecuteQuery("SELECT last_insert_rowid()")
        If Not IsNull(results) Then
            FindOrCreateCustomerContact = results(0)(0)
        End If
    End If
End Function

' Find existing purchase order or create new one
' Parameters: poNumber - PO number
'             oeNumber - OE number
'             contactId - Customer contact ID
' Returns: Purchase Order ID, or Null on failure
Function FindOrCreatePurchaseOrder(poNumber As String, oeNumber As String, contactId As Long) As Variant
    Dim results As Variant
    Dim insertSQL As String

    ' Try to find existing PO
    results = mod_SQLite.ExecuteQuery("SELECT id FROM purchase_order WHERE po_number = '" & Replace(poNumber, "'", "''") & "' LIMIT 1")

    If Not IsNull(results) Then
        ' PO exists
        FindOrCreatePurchaseOrder = results(0)(0)
        Exit Function
    End If

    ' PO doesn't exist, create new one
    insertSQL = "INSERT INTO purchase_order (po_number, oe_number, contact_id, is_active, created_at, updated_at) " & _
                "VALUES ('" & Replace(poNumber, "'", "''") & "', '" & Replace(oeNumber, "'", "''") & "', " & contactId & ", 1, datetime('now', 'localtime'), datetime('now', 'localtime'))"

    If mod_SQLite.ExecuteNonQuery(insertSQL) Then
        ' Return last inserted ID
        results = mod_SQLite.ExecuteQuery("SELECT last_insert_rowid()")
        If Not IsNull(results) Then
            FindOrCreatePurchaseOrder = results(0)(0)
        End If
    End If
End Function

' Find existing job or create new one
' If job exists, overwrite the po_id and priority with new values
' Parameters: jobNumber - Job number
'             poId - Purchase Order ID
' Returns: Job ID, or Null on failure
Function FindOrCreateJob(jobNumber As String, poId As Long) As Variant
    Dim results As Variant
    Dim insertSQL As String
    Dim updateSQL As String

    ' Try to find existing job with matching job_number
    results = mod_SQLite.ExecuteQuery("SELECT id FROM job WHERE job_number = '" & Replace(jobNumber, "'", "''") & "' LIMIT 1")

    If Not IsNull(results) Then
        ' Job exists, update it with new po_id and priority
        Dim jobId As Long
        jobId = results(0)(0)
        
        updateSQL = "UPDATE job SET po_id = " & poId & ", priority = 'Normal', updated_at = datetime('now', 'localtime') " & _
                    "WHERE id = " & jobId
        
        If mod_SQLite.ExecuteNonQuery(updateSQL) Then
            FindOrCreateJob = jobId
        End If
        Exit Function
    End If

    ' Job doesn't exist, create new one
    insertSQL = "INSERT INTO job (job_number, po_id, priority, created_at, updated_at) " & _
                "VALUES ('" & Replace(jobNumber, "'", "''") & "', " & poId & ", 'Normal', datetime('now', 'localtime'), datetime('now', 'localtime'))"

    If mod_SQLite.ExecuteNonQuery(insertSQL) Then
        ' Return last inserted ID
        results = mod_SQLite.ExecuteQuery("SELECT last_insert_rowid()")
        If Not IsNull(results) Then
            FindOrCreateJob = results(0)(0)
        End If
    End If
End Function

' Find existing part or create new one
' Parameters: drawingNumber - Drawing/Part number
'             revision - Part revision
'             description - Part description (optional)
' Returns: Part ID, or Null on failure
Function FindOrCreatePart(drawingNumber As String, revision As String, Optional description As String = "") As Variant
    Dim results As Variant
    Dim insertSQL As String

    ' Try to find existing part with matching drawing_number and revision
    results = mod_SQLite.ExecuteQuery("SELECT id FROM part " & _
                                      "WHERE drawing_number = '" & Replace(drawingNumber, "'", "''") & "' " & _
                                      "AND revision = '" & Replace(revision, "'", "''") & "' LIMIT 1")

    If Not IsNull(results) Then
        ' Part exists
        FindOrCreatePart = results(0)(0)
        Exit Function
    End If

    ' Part doesn't exist, create new one
    insertSQL = "INSERT INTO part (drawing_number, revision, description, created_at, updated_at) " & _
                "VALUES ('" & Replace(drawingNumber, "'", "''") & "', '" & Replace(revision, "'", "''") & "', '" & Replace(description, "'", "''") & "', datetime('now', 'localtime'), datetime('now', 'localtime'))"

    If mod_SQLite.ExecuteNonQuery(insertSQL) Then
        ' Return last inserted ID
        results = mod_SQLite.ExecuteQuery("SELECT last_insert_rowid()")
        If Not IsNull(results) Then
            FindOrCreatePart = results(0)(0)
        End If
    End If
End Function

' Create or update order item entry
' If order item with same job_id and line_number exists, update it (overwrite)
' Otherwise, create new one
' Parameters: jobId - Job ID
'             partId - Part ID (can be Null)
'             lineNumber - Line number within the job
'             quantity - Order quantity
'             actualPrice - Unit price
'             deliveryRequiredDate - Delivery deadline
'             drawingReleaseDate - Drawing release date (in YYYY-MM-DD format)
' Returns: Order Item ID, or Null on failure
Function CreateOrderItem(jobId As Long, partId As Variant, lineNumber As String, quantity As String, _
                         actualPrice As String, deliveryRequiredDate As String, drawingReleaseDate As String) As Variant
    Dim insertSQL As String
    Dim updateSQL As String
    Dim results As Variant
    Dim partIdClause As String

    ' Handle optional partId
    If IsNull(partId) Or partId = "" Then
        partIdClause = "NULL"
    Else
        partIdClause = CLng(partId)
    End If

    ' Convert price and quantity to numeric values
    Dim qtyValue As Long, priceValue As Double
    
    ' Use If/Else instead of IIf to avoid evaluating both branches
    ' IIf always evaluates both branches, which causes Type Mismatch on empty strings
    If quantity = "" Then
        qtyValue = 0
    Else
        qtyValue = CLng(quantity)
    End If
    
    If actualPrice = "" Then
        priceValue = 0
    Else
        priceValue = CDbl(actualPrice)
    End If

    ' Check if order item with same job_id and line_number already exists
    results = mod_SQLite.ExecuteQuery("SELECT id FROM order_item WHERE job_id = " & jobId & " AND line_number = '" & Replace(lineNumber, "'", "''") & "' LIMIT 1")

    If Not IsNull(results) Then
        ' Order item exists, update it (overwrite)
        Dim orderItemId As Long
        orderItemId = results(0)(0)
        
        Debug.Print "Order item exists (ID: " & orderItemId & "), updating..."
        
        updateSQL = "UPDATE order_item SET part_id = " & partIdClause & ", quantity = " & qtyValue & ", actual_price = " & priceValue & ", " & _
                    "delivery_required_date = '" & Replace(deliveryRequiredDate, "'", "''") & "', drawing_release_date = '" & Replace(drawingReleaseDate, "'", "''") & "', status = 'PENDING', updated_at = datetime('now', 'localtime') " & _
                    "WHERE id = " & orderItemId
        
        If mod_SQLite.ExecuteNonQuery(updateSQL) Then
            Debug.Print "Order item updated successfully. ID: " & orderItemId
            CreateOrderItem = orderItemId
        Else
            Debug.Print "ERROR: Failed to update order item. SQL: " & updateSQL
            CreateOrderItem = Null
        End If
        Exit Function
    End If

    ' Order item doesn't exist, create new one
    insertSQL = "INSERT INTO order_item (job_id, part_id, line_number, quantity, actual_price, delivery_required_date, drawing_release_date, status, created_at, updated_at) " & _
                "VALUES (" & jobId & ", " & partIdClause & ", '" & Replace(lineNumber, "'", "''") & "', " & qtyValue & ", " & priceValue & ", " & _
                "'" & Replace(deliveryRequiredDate, "'", "''") & "', '" & Replace(drawingReleaseDate, "'", "''") & "', 'PENDING', datetime('now', 'localtime'), datetime('now', 'localtime'))"

    If mod_SQLite.ExecuteNonQuery(insertSQL) Then
        ' Return last inserted ID
        results = mod_SQLite.ExecuteQuery("SELECT last_insert_rowid()")
        If Not IsNull(results) Then
            Dim newOrderItemId As Long
            newOrderItemId = results(0)(0)
            Debug.Print "Order item created successfully. New ID: " & newOrderItemId
            CreateOrderItem = newOrderItemId
        Else
            Debug.Print "ERROR: Failed to retrieve last inserted ID after insert"
            CreateOrderItem = Null
        End If
    Else
        Debug.Print "ERROR: Failed to insert order item. SQL: " & insertSQL
        CreateOrderItem = Null
    End If
End Function

' Convert date format from YYYY.M.D, YYYY/M/D, or YYYY-M-D to YYYY-MM-DD
' Handles various input formats and ensures consistent database format
Function FormatDateForDatabase(dateStr As String) As String
    Dim parts() As String
    Dim year As String, month As String, day As String
    
    If Trim(dateStr) = "" Then
        FormatDateForDatabase = ""
        Exit Function
    End If
    
    ' Replace dots and slashes with hyphens for consistency
    ' Handles: 2026.1.25, 2026/1/25, 2026-1-25
    dateStr = Replace(dateStr, ".", "-")
    dateStr = Replace(dateStr, "/", "-")
    
    ' Split by hyphen
    parts = Split(dateStr, "-")
    
    If UBound(parts) < 2 Then
        ' Invalid format, return as-is
        FormatDateForDatabase = dateStr
        Exit Function
    End If
    
    year = Trim(parts(0))
    month = Trim(parts(1))
    day = Trim(parts(2))
    
    ' Pad month and day with leading zeros if needed
    month = Right("0" & month, 2)
    day = Right("0" & day, 2)
    
    FormatDateForDatabase = year & "-" & month & "-" & day
End Function




