Imports System.Net.Http
Imports System.Net.Http.Json
Imports System.Text.Json
Imports System.Threading

Public NotInheritable Class ApiClient
    Implements IDisposable
    Private ReadOnly client As HttpClient
    Private ReadOnly options As New JsonSerializerOptions(JsonSerializerDefaults.Web)
    Public ReadOnly Property BaseUrl As String
        Get
            Return client.BaseAddress.AbsoluteUri
        End Get
    End Property
    Public Sub New(baseAddress As Uri)
        client = New HttpClient With {.BaseAddress = baseAddress, .Timeout = TimeSpan.FromSeconds(45)}
    End Sub
    Public Async Function SendAsync(Of T)(method As HttpMethod, path As String, Optional body As Object = Nothing, Optional ct As CancellationToken = Nothing) As Task(Of T)
        Try
            Using request As New HttpRequestMessage(method, path)
                If body IsNot Nothing Then request.Content = JsonContent.Create(body, options:=options)
                Using response = Await client.SendAsync(request, ct)
                    If Not response.IsSuccessStatusCode Then
                        Dim message = $"The API returned {CInt(response.StatusCode)}."
                        If response.StatusCode = Net.HttpStatusCode.NotFound Then message = "This order no longer exists. Refresh the list and try again."
                        Try
                            Using problem = JsonDocument.Parse(Await response.Content.ReadAsStringAsync(ct))
                                Dim element As JsonElement
                                If problem.RootElement.TryGetProperty("errors", element) Then
                                    message = String.Join(Environment.NewLine, element.EnumerateObject().SelectMany(Function(p) p.Value.EnumerateArray().Select(Function(v) v.GetString())))
                                ElseIf problem.RootElement.TryGetProperty("title", element) Then
                                    message = element.GetString()
                                End If
                            End Using
                        Catch ex As JsonException
                            ' Non-JSON server responses retain the friendly HTTP status message.
                        End Try
                        Throw New ApiException(message)
                    End If
                    Dim result = Await response.Content.ReadFromJsonAsync(Of T)(options, ct)
                    If result Is Nothing Then Throw New ApiException("The API returned an empty response. Please retry.")
                    Return result
                End Using
            End Using
        Catch ex As HttpRequestException
            Throw New ApiException($"Cannot connect to {BaseUrl}. Start Atc.Api and check your connection.", ex)
        Catch ex As OperationCanceledException When Not ct.IsCancellationRequested
            Throw New ApiException("The request timed out. Check the API and retry. If you were saving, refresh the order first to check whether the save succeeded.", ex)
        Catch ex As JsonException
            Throw New ApiException("The API returned an unexpected response. Check that the API and desktop app are the same version.", ex)
        End Try
    End Function
    Public Function GetAsync(Of T)(path As String, Optional ct As CancellationToken = Nothing) As Task(Of T)
        Return SendAsync(Of T)(HttpMethod.Get, path, ct:=ct)
    End Function
    Public Sub Dispose() Implements IDisposable.Dispose
        client.Dispose()
    End Sub
End Class
Public Class ApiException
    Inherits Exception
    Public Sub New(message As String, Optional inner As Exception = Nothing)
        MyBase.New(message, inner)
    End Sub
End Class
