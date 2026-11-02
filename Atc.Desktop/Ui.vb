Public Module Ui
    Public ReadOnly Ink As Color = Color.FromArgb(23, 42, 62)
    Public ReadOnly Accent As Color = Color.FromArgb(0, 105, 112)
    Public ReadOnly Canvas As Color = Color.FromArgb(244, 247, 250)
    Public ReadOnly Statuses As String() = {"Planned", "Released", "In Progress", "Completed", "Cancelled"}
    Public Sub Style(form As Form, title As String, size As Size)
        form.Text = title & " | ATC Production Tracker"
        form.BackColor = Canvas
        form.ForeColor = Ink
        form.Font = New Font("Segoe UI", 10)
        form.Size = size
        form.MinimumSize = New Size(Math.Min(size.Width, 1050), Math.Min(size.Height, 700))
        form.StartPosition = FormStartPosition.CenterScreen
        form.AutoScaleMode = AutoScaleMode.Dpi
    End Sub
    Public Function Heading(title As String, subtitle As String) As Control
        Dim panel As New Panel With {.Dock = DockStyle.Top, .Height = 92, .BackColor = Ink}
        panel.Controls.Add(New Label With {.Text = title, .Font = New Font("Segoe UI Semibold", 22), .ForeColor = Color.White, .Location = New Point(24, 13), .AutoSize = True})
        panel.Controls.Add(New Label With {.Text = subtitle, .ForeColor = Color.FromArgb(186, 205, 219), .Location = New Point(26, 56), .AutoSize = True})
        Return panel
    End Function
    Public Function Button(text As String, Optional primary As Boolean = False) As Button
        Return New Button With {.Text = text, .AutoSize = True, .Height = 36, .MinimumSize = New Size(90, 36), .Padding = New Padding(10, 3, 10, 3), .FlatStyle = FlatStyle.Flat,
            .BackColor = If(primary, Accent, Color.White), .ForeColor = If(primary, Color.White, Ink), .Margin = New Padding(5)}
    End Function
    Public Function Combo(Optional width As Integer = 160) As ComboBox
        Return New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = width, .Margin = New Padding(5)}
    End Function
    Public Function DatePicker(Optional optionalDate As Boolean = True) As DateTimePicker
        Return New DateTimePicker With {.Format = DateTimePickerFormat.Custom, .CustomFormat = "MMM dd, yyyy", .Width = 165, .ShowCheckBox = optionalDate, .Checked = Not optionalDate, .Margin = New Padding(5)}
    End Function
    Public Function Field(label As String, control As Control) As Control
        Dim panel As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .AutoSize = True, .WrapContents = False, .Margin = New Padding(4)}
        panel.Controls.Add(New Label With {.Text = label, .AutoSize = True, .Margin = New Padding(5, 0, 5, 1)})
        panel.Controls.Add(control)
        Return panel
    End Function
    Public Function Grid() As DataGridView
        Dim value As New DataGridView With {.Dock = DockStyle.Fill, .ReadOnly = True, .AutoGenerateColumns = False, .AllowUserToAddRows = False,
            .AllowUserToDeleteRows = False, .AllowUserToResizeRows = False, .RowHeadersVisible = False, .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            .MultiSelect = False, .BackgroundColor = Color.White, .BorderStyle = BorderStyle.None, .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            .EnableHeadersVisualStyles = False, .ColumnHeadersHeight = 42, .RowTemplate = New DataGridViewRow With {.Height = 36}}
        value.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(228, 235, 241)
        value.ColumnHeadersDefaultCellStyle.ForeColor = Ink
        value.ColumnHeadersDefaultCellStyle.SelectionBackColor = value.ColumnHeadersDefaultCellStyle.BackColor
        value.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink
        value.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 252)
        value.DefaultCellStyle.SelectionBackColor = Color.FromArgb(216, 237, 237)
        value.DefaultCellStyle.SelectionForeColor = Ink
        Return value
    End Function
    Public Sub Column(grid As DataGridView, propertyName As String, title As String, Optional weight As Single = 100, Optional format As String = Nothing)
        Dim col As New DataGridViewTextBoxColumn With {.DataPropertyName = propertyName, .HeaderText = title, .Name = propertyName, .FillWeight = weight, .MinimumWidth = 65, .SortMode = DataGridViewColumnSortMode.NotSortable}
        If format IsNot Nothing Then col.DefaultCellStyle.Format = format
        grid.Columns.Add(col)
    End Sub
    Public Sub OrderColumns(grid As DataGridView, Optional report As Boolean = False)
        Column(grid, "OrderNumber", "Order", 70)
        Column(grid, "PartNumber", "Part", 85)
        Column(grid, "DepartmentName", "Department", 115)
        Column(grid, "DueDate", "Due date", 95)
        If Not report Then
            Column(grid, "QuantityOrdered", "Ordered", 70, "N0")
            Column(grid, "QuantityCompleted", "Completed", 80, "N0")
        End If
        Column(grid, "QuantityRemaining", "Remaining", 80, "N0")
        If report Then Column(grid, "DaysOverdue", "Days overdue", 85, "N0")
        Column(grid, "Status", "Status", 95)
        AddHandler grid.CellFormatting, Sub(sender, e)
            If e.RowIndex < 0 Then Return
            Dim item = TryCast(grid.Rows(e.RowIndex).DataBoundItem, OrderDto)
            If item Is Nothing Then Return
            If grid.Columns(e.ColumnIndex).DataPropertyName = "DueDate" Then e.Value = item.DueDate.ToString("MMM dd, yyyy")
            If item.IsOverdue Then grid.Rows(e.RowIndex).DefaultCellStyle.ForeColor = Color.FromArgb(157, 59, 24)
        End Sub
    End Sub
    Public Sub ErrorMessage(owner As IWin32Window, ex As Exception)
        MessageBox.Show(owner, ex.Message, "Unable to complete request", MessageBoxButtons.OK, MessageBoxIcon.Warning)
    End Sub
End Module
