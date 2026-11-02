Imports System.Threading

Public Class AsyncForm
    Inherits Form
    Protected ReadOnly Lifetime As New CancellationTokenSource
    Protected ReadOnly Content As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(20)}
    Protected ReadOnly Feedback As New Label With {.Dock = DockStyle.Bottom, .Height = 36, .Padding = New Padding(24, 7, 0, 0), .Text = "Ready"}
    Public Sub New()
        Controls.Add(Content)
        Controls.Add(Feedback)
        AddHandler FormClosing, Sub() Lifetime.Cancel()
    End Sub
    Protected Async Function RunAsync(action As Func(Of Task), Optional message As String = "Loading…") As Task
        If Not Content.Enabled Then Return
        Content.Enabled = False
        UseWaitCursor = True
        Feedback.Text = message
        Try
            Await action()
            If Not IsDisposed Then Feedback.Text = "Ready"
        Catch ex As OperationCanceledException When Lifetime.IsCancellationRequested
        Catch ex As Exception
            If Not IsDisposed Then
                Feedback.Text = "Request failed. Please retry."
                Ui.ErrorMessage(Me, ex)
            End If
        Finally
            If Not IsDisposed Then
                Content.Enabled = True
                UseWaitCursor = False
            End If
        End Try
    End Function
End Class
