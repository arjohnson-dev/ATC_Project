Public Class OrderFilters
    Inherits FlowLayoutPanel
    Public ReadOnly Department As ComboBox = Ui.Combo(170)
    Public ReadOnly Status As ComboBox = Ui.Combo(145)
    Public ReadOnly DueFrom As DateTimePicker = Ui.DatePicker()
    Public ReadOnly DueTo As DateTimePicker = Ui.DatePicker()
    Public ReadOnly Search As New TextBox With {.Width = 160, .MaxLength = 100, .Margin = New Padding(5)}
    Public Sub New()
        Dock = DockStyle.Fill
        AutoScroll = True
        WrapContents = True
        Controls.Add(Ui.Field("Department", Department))
        Controls.Add(Ui.Field("Status", Status))
        Controls.Add(Ui.Field("Due from (optional)", DueFrom))
        Controls.Add(Ui.Field("Due to (optional)", DueTo))
        Controls.Add(Ui.Field("Part / description", Search))
        Status.Items.Add("All statuses")
        Status.Items.AddRange(Ui.Statuses)
        Status.SelectedIndex = 0
        Department.DataSource = New List(Of DepartmentDto) From {New DepartmentDto With {.Name = "All departments"}}
        Department.DisplayMember = "Name"
        Department.ValueMember = "DepartmentId"
    End Sub
    Public Sub SetDepartments(values As List(Of DepartmentDto))
        Department.DataSource = (New List(Of DepartmentDto) From {New DepartmentDto With {.Name = "All departments"}}).Concat(values).ToList()
    End Sub
    Public Function Query() As String
        If DueFrom.Checked AndAlso DueTo.Checked AndAlso DueFrom.Value.Date > DueTo.Value.Date Then
            Throw New InvalidOperationException("Due from must be on or before due to.")
        End If
        Dim result As New List(Of String)
        Dim selected = TryCast(Department.SelectedItem, DepartmentDto)
        If selected IsNot Nothing AndAlso selected.DepartmentId > 0 Then result.Add("departmentId=" & selected.DepartmentId.ToString(Globalization.CultureInfo.InvariantCulture))
        If Status.SelectedIndex > 0 Then result.Add("status=" & Uri.EscapeDataString(CStr(Status.SelectedItem)))
        If DueFrom.Checked Then result.Add("dueFrom=" & DueFrom.Value.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture))
        If DueTo.Checked Then result.Add("dueTo=" & DueTo.Value.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture))
        If Not String.IsNullOrWhiteSpace(Search.Text) Then result.Add("search=" & Uri.EscapeDataString(Search.Text.Trim()))
        Return String.Join("&", result)
    End Function
    Public Function Description() As String
        Return $"Department: {Department.Text}; Status: {Status.Text}; Due from: {If(DueFrom.Checked, DueFrom.Value.ToString("yyyy-MM-dd"), "Any")}; Due to: {If(DueTo.Checked, DueTo.Value.ToString("yyyy-MM-dd"), "Any")}; Search: {If(String.IsNullOrWhiteSpace(Search.Text), "Any", Search.Text.Trim())}"
    End Function
    Public Sub ResetFilters()
        Department.SelectedIndex = 0
        Status.SelectedIndex = 0
        DueFrom.Checked = False
        DueTo.Checked = False
        Search.Clear()
    End Sub
End Class
