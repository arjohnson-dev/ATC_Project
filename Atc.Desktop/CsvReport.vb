Imports System.IO
Imports System.Text
Imports System.Threading

Public Module CsvReport
    Private Function Cell(value As Object) As String
        Dim text = Convert.ToString(value, Globalization.CultureInfo.InvariantCulture)
        ' Keep exported text from being interpreted as a spreadsheet formula.
        If text.Length > 0 AndAlso ("=+-@".Contains(text.TrimStart().FirstOrDefault()) OrElse text(0) = ChrW(9) OrElse text(0) = ChrW(13) OrElse text(0) = ChrW(10)) Then text = "'" & text
        Return """" & text.Replace("""", """""") & """"
    End Function
    Public Function Render(report As ExceptionReport, filters As String) As String
        Dim csv As New StringBuilder
        Dim row As Action(Of Object()) = Sub(values) csv.AppendLine(String.Join(",", values.Select(AddressOf Cell)))
        row({"ATC Production Order Exceptions — fictional manufacturing data"})
        row({"Reporting date", report.AsOfDate.ToString("yyyy-MM-dd")})
        row({"Generated at (UTC)", report.GeneratedAt.ToUniversalTime().ToString("O")})
        row({"Filters", filters})
        row({"Definition", "Due before reporting date, remaining units > 0; excludes Completed and Cancelled. Days overdue = reporting date minus due date in calendar days."})
        row({"Open orders", report.Totals.OpenOrderCount})
        row({"Open units remaining", report.Totals.OpenQuantityRemaining})
        row({"Overdue orders", report.Totals.OrderCount})
        row({"Overdue units remaining", report.Totals.QuantityRemaining})
        row({})
        row({"Department", "Overdue orders", "Units remaining"})
        For Each department In report.Departments
            row({department.DepartmentName, department.OrderCount, department.QuantityRemaining})
        Next
        row({})
        row({"Order", "Part", "Description", "Department", "Due date", "Ordered", "Completed", "Remaining units", "Days overdue", "Status"})
        For Each order In report.Items
            row({order.OrderNumber, order.PartNumber, order.Description, order.DepartmentName, order.DueDate.ToString("yyyy-MM-dd"),
                order.QuantityOrdered, order.QuantityCompleted, order.QuantityRemaining, order.DaysOverdue, order.Status})
        Next
        Return csv.ToString()
    End Function
    Public Function WriteAsync(path As String, report As ExceptionReport, filters As String, Optional ct As CancellationToken = Nothing) As Task
        Return File.WriteAllTextAsync(path, Render(report, filters), New UTF8Encoding(True), ct)
    End Function
End Module
