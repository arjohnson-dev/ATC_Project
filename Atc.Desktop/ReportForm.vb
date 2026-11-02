Public Class ReportForm
    Inherits AsyncForm
    Private ReadOnly api As ApiClient
    Friend ReadOnly Filters As New OrderFilters
    Friend ReadOnly AsOf As DateTimePicker = Ui.DatePicker(False)
    Friend ReadOnly DetailGrid As DataGridView = Ui.Grid()
    Friend ReadOnly DepartmentGrid As DataGridView = Ui.Grid()
    Private ReadOnly exportButton As Button = Ui.Button("Export CSV")
    Private ReadOnly generated As New Label With {.Dock = DockStyle.Fill, .AutoEllipsis = True, .Padding = New Padding(4, 6, 4, 0)}
    Private ReadOnly metrics As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .WrapContents = False, .AutoScroll = True}
    Private ReadOnly metricValues As New List(Of Label)
    Friend Snapshot As ExceptionReport
    Friend SnapshotFilters As String = ""
    Public Sub New(client As ApiClient)
        api = client
        Ui.Style(Me, "Exceptions report", New Size(1320, 940))
        Controls.Add(Ui.Heading("Exceptions report", "Find late work. Resolve exceptions. Export a repeatable report."))
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 7}
        For Each rowHeight As Integer In {94, 62, 96, 32}
            layout.RowStyles.Add(New RowStyle(SizeType.Absolute, rowHeight))
        Next
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 35))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 32))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 65))
        layout.Controls.Add(Filters, 0, 0)
        Dim tools As New FlowLayoutPanel With {.Dock = DockStyle.Fill}
        Dim generateButton = Ui.Button("Generate report", True)
        Dim open = Ui.Button("Open selected order")
        tools.Controls.Add(Ui.Field("Reporting date", AsOf))
        tools.Controls.AddRange({generateButton, open, exportButton})
        layout.Controls.Add(tools, 0, 1)
        AddMetric("OPEN ORDERS", Color.FromArgb(23, 42, 62))
        AddMetric("OVERDUE ORDERS", Color.FromArgb(157, 59, 24))
        AddMetric("OPEN UNITS REMAINING", Ui.Accent)
        AddMetric("OVERDUE UNITS", Color.FromArgb(157, 59, 24))
        layout.Controls.Add(metrics, 0, 2)
        layout.Controls.Add(New Label With {.Text = "OVERDUE WORK BY DEPARTMENT", .AutoSize = True, .Padding = New Padding(4, 7, 0, 0)}, 0, 3)
        Ui.Column(DepartmentGrid, "DepartmentName", "Department", 150)
        Ui.Column(DepartmentGrid, "OrderCount", "Overdue orders", 100, "N0")
        Ui.Column(DepartmentGrid, "QuantityRemaining", "Units remaining", 100, "N0")
        layout.Controls.Add(DepartmentGrid, 0, 4)
        layout.Controls.Add(generated, 0, 5)
        Ui.OrderColumns(DetailGrid, True)
        layout.Controls.Add(DetailGrid, 0, 6)
        Content.Controls.Add(layout)
        exportButton.Enabled = False
        AddHandler Shown, Async Sub() Await RunAsync(Async Function()
            Dim departments = Await api.GetAsync(Of List(Of DepartmentDto))("api/departments", Lifetime.Token)
            If IsDisposed Then Return
            Filters.SetDepartments(departments)
            Await GenerateAsync()
        End Function, "Generating report…")
        AddHandler generateButton.Click, Async Sub() Await RunAsync(AddressOf GenerateAsync, "Generating report…")
        AddHandler open.Click, Async Sub() Await OpenSelectedAsync()
        AddHandler DetailGrid.CellDoubleClick, Async Sub(sender, e)
            If e.RowIndex >= 0 Then Await OpenSelectedAsync()
        End Sub
        AddHandler exportButton.Click, Async Sub()
            If Snapshot Is Nothing Then Return
            Using picker As New SaveFileDialog With {.Filter = "CSV report (*.csv)|*.csv", .DefaultExt = "csv", .AddExtension = True,
                .FileName = $"ATC-exceptions-{Snapshot.AsOfDate:yyyy-MM-dd}.csv"}
                If picker.ShowDialog(Me) <> DialogResult.OK Then Return
                Await RunAsync(Async Function()
                    Await CsvReport.WriteAsync(picker.FileName, Snapshot, SnapshotFilters, Lifetime.Token)
                    MessageBox.Show(Me, $"Exported all {Snapshot.Items.Count:N0} exception rows to:{Environment.NewLine}{picker.FileName}", "Report exported", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End Function, "Exporting report…")
            End Using
        End Sub
        AddHandler Filters.Department.SelectedIndexChanged, Sub() InvalidateSnapshot()
        AddHandler Filters.Status.SelectedIndexChanged, Sub() InvalidateSnapshot()
        AddHandler Filters.DueFrom.ValueChanged, Sub() InvalidateSnapshot()
        AddHandler Filters.DueTo.ValueChanged, Sub() InvalidateSnapshot()
        AddHandler Filters.Search.TextChanged, Sub() InvalidateSnapshot()
        AddHandler AsOf.ValueChanged, Sub() InvalidateSnapshot()
    End Sub
    Private Sub AddMetric(title As String, color As Color)
        Dim card As New Panel With {.Size = New Size(272, 82), .BackColor = Color.White, .Margin = New Padding(4, 7, 12, 7)}
        card.Controls.Add(New Label With {.Text = title, .AutoSize = True, .Location = New Point(14, 10), .ForeColor = Ui.Ink, .Font = New Font("Segoe UI", 9)})
        Dim value As New Label With {.Text = "—", .AutoSize = True, .Location = New Point(12, 30), .Font = New Font("Segoe UI Semibold", 25), .ForeColor = color}
        card.Controls.Add(value)
        metricValues.Add(value)
        metrics.Controls.Add(card)
    End Sub
    Private Sub InvalidateSnapshot()
        If Snapshot Is Nothing Then Return
        exportButton.Enabled = False
        generated.Text = "Filters changed. Generate the report to refresh the displayed snapshot and enable export."
    End Sub
    Friend Async Function GenerateAsync() As Task
        Dim query = Filters.Query()
        Dim description = Filters.Description()
        Dim dateText = AsOf.Value.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)
        ' Request every detail row once: grid, summaries, and CSV share this snapshot.
        Dim result = Await api.GetAsync(Of ExceptionReport)($"api/reports/order-exceptions?{query}&asOfDate={dateText}&allRows=true", Lifetime.Token)
        If IsDisposed Then Return
        Snapshot = result
        SnapshotFilters = description
        DetailGrid.DataSource = result.Items
        DepartmentGrid.DataSource = result.Departments
        metricValues(0).Text = result.Totals.OpenOrderCount.ToString("N0")
        metricValues(1).Text = result.Totals.OrderCount.ToString("N0")
        metricValues(2).Text = result.Totals.OpenQuantityRemaining.ToString("N0")
        metricValues(3).Text = result.Totals.QuantityRemaining.ToString("N0")
        generated.Text = $"EXCEPTION DETAIL  •  {result.Items.Count:N0} rows  •  As of {result.AsOfDate:MMM dd, yyyy}  •  Generated {result.GeneratedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}{If(result.Items.Count = 0, "  •  No exceptions match these filters", "")}" 
        exportButton.Enabled = True
    End Function
    Private Async Function OpenSelectedAsync() As Task
        Dim order = TryCast(DetailGrid.CurrentRow?.DataBoundItem, OrderDto)
        If order Is Nothing Then Return
        Await RunAsync(Async Function()
            Dim current = Await api.GetAsync(Of OrderDto)($"api/orders/{order.OrderId}", Lifetime.Token)
            Dim departments = Await api.GetAsync(Of List(Of DepartmentDto))("api/departments", Lifetime.Token)
            Dim products = Await api.GetAsync(Of List(Of ProductDto))("api/products", Lifetime.Token)
            If IsDisposed Then Return
            Using editor As New OrderEditorForm(api, departments, products, current)
                If editor.ShowDialog(Me) = DialogResult.OK Then
                    exportButton.Enabled = False
                    Await GenerateAsync()
                End If
            End Using
        End Function)
    End Function
End Class
