using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
ApplicationConfiguration.Initialize();

EmulatorOptions options = EmulatorOptions.Parse(args);

if (options.InstallPrinter)
{
    WindowsPrinterQueueInstaller.Install(options.PrinterName, options.PrinterHost, options.Port);
}

string outputDirectory = Path.Combine(AppContext.BaseDirectory, "receipts");
Directory.CreateDirectory(outputDirectory);

using var server = new ReceiptTcpServer(options.Port, outputDirectory);
using var preview = new ReceiptPreviewForm(options, outputDirectory);

server.ReceiptReceived += preview.AddReceipt;
server.StatusChanged += preview.SetStatus;
server.Start();

Application.Run(preview);

internal sealed class ReceiptPreviewForm : Form
{
    private readonly Label _status;
    private readonly ListBox _receipts;
    private readonly RichTextBox _preview;
    private readonly Button _openFolder;
    private readonly List<ReceiptPreview> _items = [];

    public ReceiptPreviewForm(EmulatorOptions options, string outputDirectory)
    {
        Text = "NepalHMS ESC/POS Emulator";
        Size = new Size(860, 720);
        MinimumSize = new Size(720, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 246, 250);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 76,
            BackColor = Color.FromArgb(30, 41, 59),
            Padding = new Padding(16, 10, 16, 10)
        };

        var title = new Label
        {
            Text = "NepalHMS ESC/POS Emulator",
            Dock = DockStyle.Top,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            Height = 28
        };

        _status = new Label
        {
            Text = $"Starting on 0.0.0.0:{options.Port}",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(203, 213, 225),
            Font = new Font("Segoe UI", 9.5F)
        };

        header.Controls.Add(_status);
        header.Controls.Add(title);

        _receipts = new ListBox
        {
            Dock = DockStyle.Left,
            Width = 250,
            Font = new Font("Segoe UI", 9F),
            BorderStyle = BorderStyle.None
        };
        _receipts.SelectedIndexChanged += (s, e) => ShowSelectedReceipt();

        _preview = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.White,
            ForeColor = Color.Black,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 12F),
            WordWrap = false,
            Padding = new Padding(16)
        };

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            BackColor = Color.White,
            Padding = new Padding(12)
        };

        _openFolder = new Button
        {
            Text = "Open receipts folder",
            Dock = DockStyle.Right,
            Width = 160
        };
        _openFolder.Click += (s, e) => Process.Start(new ProcessStartInfo
        {
            FileName = outputDirectory,
            UseShellExecute = true
        });

        footer.Controls.Add(_openFolder);

        Controls.Add(_preview);
        Controls.Add(_receipts);
        Controls.Add(footer);
        Controls.Add(header);
    }

    public void SetStatus(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(SetStatus), message);
            return;
        }

        _status.Text = message;
    }

    public void AddReceipt(ReceiptPreview receipt)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<ReceiptPreview>(AddReceipt), receipt);
            return;
        }

        _items.Insert(0, receipt);
        _receipts.Items.Insert(0, receipt.Title);
        _receipts.SelectedIndex = 0;
        _preview.Text = receipt.Text;
        _status.Text = $"Received {receipt.ByteCount} bytes. Saved: {Path.GetFileName(receipt.Path)}";
    }

    private void ShowSelectedReceipt()
    {
        int index = _receipts.SelectedIndex;

        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        ReceiptPreview item = _items[index];
        _preview.Text = item.Text;
        _status.Text = $"Viewing {item.Title}. Saved: {Path.GetFileName(item.Path)}";
    }
}

internal sealed class ReceiptTcpServer : IDisposable
{
    private readonly int _port;
    private readonly string _outputDirectory;
    private readonly CancellationTokenSource _cancellation = new();
    private TcpListener? _listener;
    private int _receiptNumber;

    public event Action<ReceiptPreview>? ReceiptReceived;

    public event Action<string>? StatusChanged;

    public ReceiptTcpServer(int port, string outputDirectory)
    {
        _port = port;
        _outputDirectory = outputDirectory;
    }

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Any, _port);

        try
        {
            _listener.Start();
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            StatusChanged?.Invoke($"Port {_port} is already in use. Try --port 9101 or stop the other emulator.");
            MessageBox.Show(
                $"Port {_port} is already in use.\n\nFind the process with:\nnetstat -ano | findstr :{_port}",
                "ESC/POS Emulator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        StatusChanged?.Invoke($"Listening on 0.0.0.0:{_port}");
        _ = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Accept failed: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            await using NetworkStream stream = client.GetStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);

            byte[] bytes = buffer.ToArray();
            string text = EscPosTextRenderer.Render(bytes);
            int currentReceipt = Interlocked.Increment(ref _receiptNumber);
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string path = Path.Combine(_outputDirectory, $"receipt-{timestamp}-{currentReceipt:000}.txt");

            await File.WriteAllTextAsync(path, text, Encoding.UTF8, cancellationToken);

            ReceiptReceived?.Invoke(new ReceiptPreview(
                $"#{currentReceipt} {DateTime.Now:HH:mm:ss}",
                text,
                path,
                bytes.Length));
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _listener?.Stop();
        _cancellation.Dispose();
    }
}

internal sealed record ReceiptPreview(string Title, string Text, string Path, int ByteCount);

internal static class EscPosTextRenderer
{
    private static readonly Encoding PrinterEncoding = Encoding.GetEncoding(437);

    public static string Render(byte[] bytes)
    {
        var output = new StringBuilder();

        for (int index = 0; index < bytes.Length; index++)
        {
            byte current = bytes[index];

            if (current == 0x1B)
            {
                index += EscCommandPayloadLength(bytes, index);
                continue;
            }

            if (current == 0x1D)
            {
                int skip = GsCommandPayloadLength(bytes, index);
                if (IsCutCommand(bytes, index))
                {
                    output.AppendLine();
                    output.AppendLine("[CUT]");
                }

                index += skip;
                continue;
            }

            if (current == 0x10)
            {
                index += 2;
                continue;
            }

            if (current == 0x0A)
            {
                output.AppendLine();
                continue;
            }

            if (current == 0x0D)
            {
                continue;
            }

            if (current == 0x09)
            {
                output.Append('\t');
                continue;
            }

            if (current >= 0x20)
            {
                output.Append(PrinterEncoding.GetString([current]));
            }
        }

        return output.ToString().TrimEnd() + Environment.NewLine;
    }

    private static int EscCommandPayloadLength(byte[] bytes, int index)
    {
        if (index + 1 >= bytes.Length)
        {
            return 0;
        }

        byte command = bytes[index + 1];

        return command switch
        {
            0x40 => 1,
            0x21 or 0x2D or 0x33 or 0x45 or 0x4D or 0x61 or 0x74 => 2,
            0x70 => 4,
            _ => 1,
        };
    }

    private static int GsCommandPayloadLength(byte[] bytes, int index)
    {
        if (index + 1 >= bytes.Length)
        {
            return 0;
        }

        byte command = bytes[index + 1];

        if (command == 0x56 && index + 2 < bytes.Length)
        {
            byte mode = bytes[index + 2];

            return mode is 0x41 or 0x42 or 65 or 66 ? 3 : 2;
        }

        return command switch
        {
            0x21 or 0x42 => 2,
            _ => 1,
        };
    }

    private static bool IsCutCommand(byte[] bytes, int index)
    {
        return index + 1 < bytes.Length && bytes[index + 1] == 0x56;
    }
}

internal sealed class EmulatorOptions
{
    public int Port { get; private init; } = 9100;

    public bool InstallPrinter { get; private init; }

    public string PrinterName { get; private init; } = "NepalHMS ESC POS Emulator";

    public string PrinterHost { get; private init; } = "127.0.0.1";

    public static EmulatorOptions Parse(string[] args)
    {
        int port = 9100;
        bool installPrinter = false;
        string printerName = "NepalHMS ESC POS Emulator";
        string printerHost = "127.0.0.1";

        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];

            if (index == 0 && int.TryParse(arg, out int firstPort))
            {
                port = firstPort;
                continue;
            }

            if (arg.Equals("--install-printer", StringComparison.OrdinalIgnoreCase))
            {
                installPrinter = true;
                continue;
            }

            if (arg.Equals("--port", StringComparison.OrdinalIgnoreCase) && TryReadNext(args, ref index, out string? portValue) && int.TryParse(portValue, out int parsedPort))
            {
                port = parsedPort;
                continue;
            }

            if (arg.Equals("--printer-name", StringComparison.OrdinalIgnoreCase) && TryReadNext(args, ref index, out string? nameValue))
            {
                printerName = nameValue ?? printerName;
                continue;
            }

            if (arg.Equals("--printer-host", StringComparison.OrdinalIgnoreCase) && TryReadNext(args, ref index, out string? hostValue))
            {
                printerHost = hostValue ?? printerHost;
            }
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "Port must be between 1 and 65535.");
        }

        return new EmulatorOptions
        {
            Port = port,
            InstallPrinter = installPrinter,
            PrinterName = printerName.Trim(),
            PrinterHost = printerHost.Trim()
        };
    }

    private static bool TryReadNext(string[] args, ref int index, out string? value)
    {
        value = null;

        if (index + 1 >= args.Length)
        {
            return false;
        }

        value = args[++index];
        return true;
    }
}

internal static class WindowsPrinterQueueInstaller
{
    public static void Install(string printerName, string host, int port)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!IsAdministrator())
        {
            MessageBox.Show(
                "Printer queue was not installed because this app is not running as Administrator.",
                "ESC/POS Emulator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        string portName = $"NEPALHMS_ESC_POS_EMULATOR_{port}";
        string script = $$"""
$printerName = '{{EscapePowerShellSingleQuoted(printerName)}}'
$portName = '{{EscapePowerShellSingleQuoted(portName)}}'
$hostAddress = '{{EscapePowerShellSingleQuoted(host)}}'
$portNumber = {{port}}

if (-not (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue)) {
    Add-PrinterPort -Name $portName -PrinterHostAddress $hostAddress -PortNumber $portNumber -SNMP 0
}

if (-not (Get-PrinterDriver -Name 'Generic / Text Only' -ErrorAction SilentlyContinue)) {
    Add-PrinterDriver -Name 'Generic / Text Only'
}

if (-not (Get-Printer -Name $printerName -ErrorAction SilentlyContinue)) {
    Add-Printer -Name $printerName -DriverName 'Generic / Text Only' -PortName $portName
} else {
    Set-Printer -Name $printerName -PortName $portName
}
""";

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start powershell.exe.");
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            MessageBox.Show(
                $"Failed to install Windows printer queue '{printerName}'.\n\n{error.Trim()}",
                "ESC/POS Emulator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
