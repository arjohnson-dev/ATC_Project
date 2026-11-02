Public Class OrderDto
    Public Property OrderId As Long
    Public Property ProductId As Long
    Public Property PartNumber As String = ""
    Public Property Description As String = ""
    Public Property DepartmentId As Long
    Public Property DepartmentName As String = ""
    Public Property QuantityOrdered As Long
    Public Property QuantityCompleted As Long
    Public Property DueDate As DateOnly
    Public Property Status As String = ""
    Public Property QuantityRemaining As Long
    Public Property IsOverdue As Boolean
    Public Property DaysOverdue As Integer
    Public ReadOnly Property OrderNumber As String
        Get
            Return $"PO-{OrderId:0000}"
        End Get
    End Property
End Class
Public Class DepartmentDto
    Public Property DepartmentId As Long
    Public Property Name As String = ""
End Class
Public Class ProductDto
    Public Property ProductId As Long
    Public Property PartNumber As String = ""
    Public Property Description As String = ""
    Public ReadOnly Property DisplayName As String
        Get
            Return $"{PartNumber}  |  {Description}"
        End Get
    End Property
End Class
Public Class OrderPage
    Public Property Items As New List(Of OrderDto)
    Public Property TotalCount As Long
    Public Property Page As Integer
    Public Property PageSize As Integer
End Class
Public Class OrderTotals
    Public Property OrderCount As Long
    Public Property QuantityOrdered As Long
    Public Property QuantityCompleted As Long
    Public Property QuantityRemaining As Long
    Public Property OpenOrderCount As Long
    Public Property OpenQuantityRemaining As Long
End Class
Public Class DepartmentExceptionDto
    Public Property DepartmentId As Long
    Public Property DepartmentName As String = ""
    Public Property OrderCount As Long
    Public Property QuantityRemaining As Long
End Class
Public Class ExceptionReport
    Public Property AsOfDate As DateOnly
    Public Property Totals As New OrderTotals
    Public Property Items As New List(Of OrderDto)
    Public Property Departments As New List(Of DepartmentExceptionDto)
    Public Property GeneratedAt As DateTimeOffset
    Public Property AllRows As Boolean
End Class
Public Class StatusHistoryDto
    Public Property HistoryId As Long
    Public Property PreviousStatus As String
    Public Property NewStatus As String = ""
    Public Property ChangedAt As DateTimeOffset
End Class
Public Class CreateOrderRequest
    Public Property ProductId As Long
    Public Property DepartmentId As Long
    Public Property QuantityOrdered As Long
    Public Property QuantityCompleted As Long
    Public Property DueDate As DateOnly
    Public Property Status As String = "Planned"
End Class
Public Class UpdateOrderRequest
    Public Property QuantityCompleted As Long
    Public Property Status As String = ""
End Class
