Imports System.IO
Imports System.Drawing.Imaging

' Opt-in integration runner for the actual VB forms and HTTP client.
' Started only with --smoke-test; the PowerShell wrapper removes its test order.
Friend Module DesktopSmoke
    Private checks As Integer
    Private Sub Check(condition As Boolean, message As String)
        If Not condition Then Throw New InvalidOperationException(message)
        checks += 1
    End Sub
    Private Async Function WaitUntilAsync(condition As Func(Of Boolean)) As Task
        Dim deadline = DateTime.UtcNow.AddSeconds(30)
        While Not condition()
            If DateTime.UtcNow > deadline Then Throw New TimeoutException("The form did not finish loading.")
            Await Task.Delay(100)
        End While
    End Function
    Private Sub Screenshot(form As Form, path As String)
        form.PerformLayout()
        form.Refresh()
        Using bitmap As New Bitmap(form.Width, form.Height)
            form.DrawToBitmap(bitmap, New Rectangle(Point.Empty, form.Size))
            bitmap.Save(path, ImageFormat.Png)
        End Using
    End Sub
    Public Async Function RunAsync(api As ApiClient, main As OrdersForm) As Task
        Dim output = Environment.GetEnvironmentVariable("ATC_TEST_OUTPUT")
        If String.IsNullOrWhiteSpace(output) Then output = Path.Combine(AppContext.BaseDirectory, "test-results")
        Directory.CreateDirectory(output)
        Try
            Await WaitUntilAsync(Function() main.OrdersGrid.Rows.Count > 0 AndAlso main.Controls.OfType(Of Panel)().Any(Function(p) p.Enabled))
            Check(main.OrdersGrid.Rows.Count > 0, "Orders form did not display API data")
            Screenshot(main, Path.Combine(output, "orders.png"))
            Dim departments = Await api.GetAsync(Of List(Of DepartmentDto))("api/departments")
            Dim products = Await api.GetAsync(Of List(Of ProductDto))("api/products")
            Using editor As New OrderEditorForm(api, departments, products)
                editor.Show(main)
                editor.Ordered.Value = 100
                editor.Completed.Value = 101
                Dim rejected As Boolean
                Try
                    Await editor.SaveOrderAsync()
                Catch ex As InvalidOperationException
                    rejected = True
                End Try
                Check(rejected, "Editor accepted completed quantity above ordered quantity")
                editor.Completed.Value = 0
                editor.Due.Value = New Date(2026, 11, 1)
                Screenshot(editor, Path.Combine(output, "new-order.png"))
                Await editor.SaveOrderAsync()
                Dim id = editor.SavedOrder.OrderId
                File.WriteAllText(Path.Combine(output, "created-order-id.txt"), id.ToString(Globalization.CultureInfo.InvariantCulture))
                Check(id > 0 AndAlso editor.SavedOrder.Status = "Planned", "Create via editor failed")
                editor.Close()

                Using report As New ReportForm(api)
                    report.Show(main)
                    Await WaitUntilAsync(Function() report.Snapshot IsNot Nothing)
                    report.AsOf.Value = New Date(2026, 11, 5)
                    Await report.GenerateAsync()
                    Check(report.Snapshot.Items.Any(Function(o) o.OrderId = id AndAlso o.DaysOverdue = 4), "New overdue order missing from report")
                    Check(report.Snapshot.AllRows AndAlso report.Snapshot.Items.Count = report.Snapshot.Totals.OrderCount, "Report snapshot lost detail rows")
                    Check(report.Snapshot.Departments.Sum(Function(d) d.OrderCount) = report.Snapshot.Totals.OrderCount, "Department totals do not reconcile")
                    Check(report.Snapshot.Departments.Sum(Function(d) d.QuantityRemaining) = report.Snapshot.Totals.QuantityRemaining, "Department quantities do not reconcile")
                    Check(report.Snapshot.Totals.OpenOrderCount >= report.Snapshot.Totals.OrderCount, "Open count is below exception count")
                    Await CsvReport.WriteAsync(Path.Combine(output, "exceptions.csv"), report.Snapshot, report.SnapshotFilters)
                    Dim csv = File.ReadAllText(Path.Combine(output, "exceptions.csv"))
                    Check(csv.Contains("Generated at (UTC)") AndAlso csv.Contains("Filters") AndAlso csv.Contains("Days overdue"), "CSV missing metadata/detail")
                    Check(csv.Contains($"PO-{id:0000}"), "CSV omitted test order")
                    Screenshot(report, Path.Combine(output, "report.png"))

                    Dim current = Await api.GetAsync(Of OrderDto)($"api/orders/{id}")
                    Using update As New OrderEditorForm(api, departments, products, current)
                        update.Show(report)
                        Await WaitUntilAsync(Function() update.HistoryGrid.Rows.Count > 0)
                        Check(Not update.Product.Enabled AndAlso Not update.Ordered.Enabled, "Fixed order fields are editable")
                        update.Completed.Value = 25
                        update.Status.SelectedItem = "In Progress"
                        Await update.SaveOrderAsync()
                        Check(update.SavedOrder.QuantityCompleted = 25, "Editor progress update failed")
                        Await update.LoadHistoryAsync()
                        Check(update.HistoryGrid.Rows.Count = 2, "Editor did not show status history")
                        Screenshot(update, Path.Combine(output, "order-editor.png"))
                        update.Completed.Value = 100
                        update.Status.SelectedItem = "Completed"
                        Await update.SaveOrderAsync()
                        Check(update.SavedOrder.QuantityRemaining = 0, "Editor completion failed")
                        update.Close()
                    End Using
                    Await report.GenerateAsync()
                    Check(Not report.Snapshot.Items.Any(Function(o) o.OrderId = id), "Completed order remained in exceptions")
                    Dim history = Await api.GetAsync(Of List(Of StatusHistoryDto))($"api/orders/{id}/history")
                    Check(history.Count = 3 AndAlso history.Last().NewStatus = "Completed", "History did not record completion")
                    report.Filters.Department.SelectedValue = departments(0).DepartmentId
                    Await report.GenerateAsync()
                    Check(report.Snapshot.Items.All(Function(o) o.DepartmentId = departments(0).DepartmentId), "Department report filter failed")
                    report.AsOf.Value = New Date(1900, 1, 1)
                    Await report.GenerateAsync()
                    Check(report.Snapshot.Items.Count = 0 AndAlso report.Snapshot.Totals.OrderCount = 0, "Empty report failed")
                    report.Close()
                End Using
            End Using
            Dim sample As New ExceptionReport With {.Items = New List(Of OrderDto) From {
                New OrderDto With {.PartNumber = "=FORMULA", .Description = "Contains ""quotes"", commas" & vbLf & "and newline"}}}
            Dim escaped = CsvReport.Render(sample, "All departments")
            Check(escaped.Contains("'=FORMULA") AndAlso escaped.Contains("""""quotes"""""), "CSV quoting/formula protection failed")
            Using offline As New ApiClient(New Uri("http://127.0.0.1:1/"))
                Dim friendly As Boolean
                Try
                    Await offline.GetAsync(Of OrderPage)("api/orders")
                Catch ex As ApiException
                    friendly = ex.Message.Contains("Cannot connect")
                End Try
                Check(friendly, "Connection failure was not explained")
            End Using
            File.WriteAllText(Path.Combine(output, "result.txt"), $"PASS: {checks} VB desktop integration checks.")
            Environment.ExitCode = 0
        Catch ex As Exception
            File.WriteAllText(Path.Combine(output, "result.txt"), "FAIL: " & ex.ToString())
            Environment.ExitCode = 1
        Finally
            main.Close()
        End Try
    End Function
End Module
