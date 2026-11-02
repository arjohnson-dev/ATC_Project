Imports System.IO
Imports System.Text.Json

Friend Module Program
    <STAThread>
    Public Sub Main()
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Try
            Dim url = Environment.GetEnvironmentVariable("ATC_API_URL")
            If String.IsNullOrWhiteSpace(url) Then
                Using config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")))
                    url = config.RootElement.GetProperty("ApiBaseUrl").GetString()
                End Using
            End If
            Dim address As Uri = Nothing
            If Not Uri.TryCreate(url, UriKind.Absolute, address) OrElse (address.Scheme <> "http" AndAlso address.Scheme <> "https") Then
                Throw New InvalidOperationException("ApiBaseUrl must be an absolute HTTP or HTTPS URL.")
            End If
            Using api As New ApiClient(New Uri(address.AbsoluteUri.TrimEnd("/"c) & "/"))
                Dim main As New OrdersForm(api)
                If Environment.GetCommandLineArgs().Contains("--smoke-test") Then
                    AddHandler main.Shown, Async Sub() Await DesktopSmoke.RunAsync(api, main)
                End If
                Application.Run(main)
            End Using
        Catch ex As Exception
            MessageBox.Show(ex.Message, "Unable to start Production Tracker", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub
End Module
