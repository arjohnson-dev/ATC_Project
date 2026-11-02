Public Class OrdersForm
    Inherits AsyncForm
    Private ReadOnly api As ApiClient
    Friend ReadOnly Filters As New OrderFilters
    Friend ReadOnly OrdersGrid As DataGridView = Ui.Grid()
    Private ReadOnly counter As New Label With {.AutoSize = True, .Padding = New Padding(12)}
    Private ReadOnly previousButton As Button = Ui.Button("Previous")
    Private ReadOnly nextButton As Button = Ui.Button("Next")
    Private departments As New List(Of DepartmentDto)
    Private products As New List(Of ProductDto)
    Private page As Integer = 1
    Private appliedQuery As String = ""
    Public Sub New(client As ApiClient)
        api = client
        Ui.Style(Me, "Production orders", New Size(1260, 820))
        Controls.Add(Ui.Heading("Production orders", "ATC  /  OPERATIONS     •     Fictional manufacturing data"))
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4}
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 96))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 56))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 52))
        layout.Controls.Add(Filters, 0, 0)
        Dim tools As New FlowLayoutPanel With {.Dock = DockStyle.Fill}
        Dim apply = Ui.Button("Apply filters", True)
        Dim reset = Ui.Button("Reset")
        Dim create = Ui.Button("New order", True)
        Dim edit = Ui.Button("Open selected")
        Dim report = Ui.Button("Exceptions report")
        tools.Controls.AddRange({apply, reset, create, edit, report})
        layout.Controls.Add(tools, 0, 1)
        Ui.OrderColumns(OrdersGrid)
        layout.Controls.Add(OrdersGrid, 0, 2)
        Dim paging As New FlowLayoutPanel With {.Dock = DockStyle.Fill}
        paging.Controls.AddRange({previousButton, nextButton, counter})
        layout.Controls.Add(paging, 0, 3)
        Content.Controls.Add(layout)
        AddHandler Shown, Async Sub() Await RunAsync(AddressOf InitializeAsync)
        AddHandler apply.Click, Async Sub() Await RunAsync(Async Function()
            appliedQuery = Filters.Query()
            page = 1
            Await LoadOrdersAsync()
        End Function)
        AddHandler reset.Click, Async Sub()
            Filters.ResetFilters()
            Await RunAsync(Async Function()
                appliedQuery = ""
                page = 1
                Await LoadOrdersAsync()
            End Function)
        End Sub
        AddHandler previousButton.Click, Async Sub() Await RunAsync(Async Function()
            page -= 1
            Await LoadOrdersAsync()
        End Function)
        AddHandler nextButton.Click, Async Sub() Await RunAsync(Async Function()
            page += 1
            Await LoadOrdersAsync()
        End Function)
        AddHandler create.Click, Async Sub() Await EditAsync(Nothing)
        AddHandler edit.Click, Async Sub() Await OpenSelectedAsync()
        AddHandler OrdersGrid.CellDoubleClick, Async Sub(sender, e)
            If e.RowIndex >= 0 Then Await OpenSelectedAsync()
        End Sub
        AddHandler report.Click, Async Sub()
            Using form As New ReportForm(api)
                form.ShowDialog(Me)
            End Using
            Await RunAsync(AddressOf LoadOrdersAsync)
        End Sub
    End Sub
    Friend Async Function InitializeAsync() As Task
        departments = Await api.GetAsync(Of List(Of DepartmentDto))("api/departments", Lifetime.Token)
        products = Await api.GetAsync(Of List(Of ProductDto))("api/products", Lifetime.Token)
        If IsDisposed Then Return
        Filters.SetDepartments(departments)
        Await LoadOrdersAsync()
    End Function
    Friend Async Function LoadOrdersAsync() As Task
        Dim result = Await api.GetAsync(Of OrderPage)($"api/orders?{appliedQuery}&page={page}&pageSize=50", Lifetime.Token)
        If IsDisposed Then Return
        If result.Items.Count = 0 AndAlso result.TotalCount > 0 AndAlso page > 1 Then
            page = CInt(Math.Ceiling(result.TotalCount / 50.0))
            Await LoadOrdersAsync()
            Return
        End If
        OrdersGrid.DataSource = result.Items
        previousButton.Enabled = page > 1
        nextButton.Enabled = CLng(page) * 50 < result.TotalCount
        counter.Text = $"Page {page:N0}  •  {result.TotalCount:N0} orders{If(result.TotalCount = 0, " — no matching orders", "")}" 
    End Function
    Private Async Function OpenSelectedAsync() As Task
        Dim selected = TryCast(OrdersGrid.CurrentRow?.DataBoundItem, OrderDto)
        If selected Is Nothing Then Return
        Await EditAsync(selected.OrderId)
    End Function
    Private Async Function EditAsync(id As Long?) As Task
        Await RunAsync(Async Function()
            If products.Count = 0 OrElse departments.Count = 0 Then Await InitializeAsync()
            Dim order As OrderDto = Nothing
            If id.HasValue Then order = Await api.GetAsync(Of OrderDto)($"api/orders/{id.Value}", Lifetime.Token)
            If IsDisposed Then Return
            Using editor As New OrderEditorForm(api, departments, products, order)
                If editor.ShowDialog(Me) = DialogResult.OK Then Await LoadOrdersAsync()
            End Using
        End Function)
    End Function
End Class
