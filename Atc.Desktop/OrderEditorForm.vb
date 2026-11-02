Imports System.Net.Http

Public Class OrderEditorForm
    Inherits AsyncForm
    Private ReadOnly api As ApiClient
    Private ReadOnly existing As OrderDto
    Friend ReadOnly Product As ComboBox = Ui.Combo(440)
    Friend ReadOnly Department As ComboBox = Ui.Combo(260)
    Friend ReadOnly Ordered As New NumericUpDown With {.Minimum = 1, .Maximum = 9999999999D, .Value = 100, .ThousandsSeparator = True, .Width = 180}
    Friend ReadOnly Completed As New NumericUpDown With {.Minimum = 0, .Maximum = 9999999999D, .ThousandsSeparator = True, .Width = 180}
    Friend ReadOnly Due As DateTimePicker = Ui.DatePicker(False)
    Friend ReadOnly Status As ComboBox = Ui.Combo(200)
    Friend ReadOnly HistoryGrid As DataGridView = Ui.Grid()
    Friend SavedOrder As OrderDto
    Public Sub New(client As ApiClient, departments As List(Of DepartmentDto), products As List(Of ProductDto), Optional order As OrderDto = Nothing)
        api = client
        existing = order
        Ui.Style(Me, If(order Is Nothing, "New production order", $"Update {order.OrderNumber}"), New Size(840, 860))
        MinimumSize = New Size(780, 780)
        Controls.Add(Ui.Heading(If(order Is Nothing, "New production order", order.OrderNumber), If(order Is Nothing, "Create work with a product, department, quantity, and due date.", "Update completed quantity and status. Other order details are fixed.")))
        Product.DisplayMember = "DisplayName"
        Product.ValueMember = "ProductId"
        Product.DataSource = products.ToList()
        Department.DisplayMember = "Name"
        Department.ValueMember = "DepartmentId"
        Department.DataSource = departments.ToList()
        Status.Items.AddRange(Ui.Statuses)
        Status.SelectedIndex = 0
        Due.Value = Date.Today.AddDays(7)
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4}
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 285))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 32))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 56))
        Dim fields As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 6}
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 200))
        fields.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        Dim labels = {"Product", "Department", "Quantity ordered", "Quantity completed", "Due date", "Status"}
        Dim inputs As Control() = {Product, Department, Ordered, Completed, Due, Status}
        For i = 0 To labels.Length - 1
            fields.RowStyles.Add(New RowStyle(SizeType.Absolute, 46))
            fields.Controls.Add(New Label With {.Text = labels(i), .AutoSize = True, .Padding = New Padding(0, 5, 0, 0)}, 0, i)
            inputs(i).Margin = New Padding(3, 3, 3, 3)
            fields.Controls.Add(inputs(i), 1, i)
        Next
        layout.Controls.Add(fields, 0, 0)
        layout.Controls.Add(New Label With {.Text = "STATUS HISTORY  •  Recorded automatically with each status change", .AutoSize = True}, 0, 1)
        Ui.Column(HistoryGrid, "PreviousStatus", "Previous status")
        Ui.Column(HistoryGrid, "NewStatus", "New status")
        Ui.Column(HistoryGrid, "ChangedAt", "Changed at", 180, "yyyy-MM-dd HH:mm:ss zzz")
        layout.Controls.Add(HistoryGrid, 0, 2)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft}
        Dim save = Ui.Button("Save order", True)
        Dim cancel = Ui.Button("Cancel")
        Dim complete = Ui.Button("Mark complete")
        buttons.Controls.AddRange({save, cancel, complete})
        layout.Controls.Add(buttons, 0, 3)
        Content.Controls.Add(layout)
        CancelButton = cancel
        AcceptButton = save
        AddHandler cancel.Click, Sub() Close()
        AddHandler complete.Click, Sub()
            Completed.Value = Ordered.Value
            Status.SelectedItem = "Completed"
        End Sub
        AddHandler save.Click, Async Sub() Await RunAsync(Async Function()
            Await SaveOrderAsync()
            DialogResult = DialogResult.OK
            Close()
        End Function, "Saving order…")
        If order IsNot Nothing Then
            Product.SelectedValue = order.ProductId
            Department.SelectedValue = order.DepartmentId
            Ordered.Value = order.QuantityOrdered
            Completed.Value = order.QuantityCompleted
            Due.Value = order.DueDate.ToDateTime(TimeOnly.MinValue)
            Status.SelectedItem = order.Status
            Product.Enabled = False
            Department.Enabled = False
            Ordered.Enabled = False
            Due.Enabled = False
            AddHandler Shown, Async Sub() Await RunAsync(AddressOf LoadHistoryAsync)
        End If
    End Sub
    Friend Async Function LoadHistoryAsync() As Task
        If existing Is Nothing Then Return
        Dim rows = Await api.GetAsync(Of List(Of StatusHistoryDto))($"api/orders/{existing.OrderId}/history", Lifetime.Token)
        If Not IsDisposed Then HistoryGrid.DataSource = rows
    End Function
    Friend Async Function SaveOrderAsync() As Task
        If Product.SelectedItem Is Nothing OrElse Department.SelectedItem Is Nothing Then Throw New InvalidOperationException("Choose a product and department.")
        If Completed.Value > Ordered.Value Then Throw New InvalidOperationException("Completed quantity cannot exceed ordered quantity.")
        If CStr(Status.SelectedItem) = "Completed" AndAlso Completed.Value <> Ordered.Value Then Throw New InvalidOperationException("Complete the full ordered quantity before choosing Completed, or use Mark complete.")
        If existing Is Nothing Then
            SavedOrder = Await api.SendAsync(Of OrderDto)(HttpMethod.Post, "api/orders", New CreateOrderRequest With {
                .ProductId = DirectCast(Product.SelectedItem, ProductDto).ProductId,
                .DepartmentId = DirectCast(Department.SelectedItem, DepartmentDto).DepartmentId,
                .QuantityOrdered = CLng(Ordered.Value), .QuantityCompleted = CLng(Completed.Value),
                .DueDate = DateOnly.FromDateTime(Due.Value), .Status = CStr(Status.SelectedItem)}, Lifetime.Token)
        Else
            SavedOrder = Await api.SendAsync(Of OrderDto)(HttpMethod.Put, $"api/orders/{existing.OrderId}", New UpdateOrderRequest With {
                .QuantityCompleted = CLng(Completed.Value), .Status = CStr(Status.SelectedItem)}, Lifetime.Token)
        End If
    End Function
End Class
