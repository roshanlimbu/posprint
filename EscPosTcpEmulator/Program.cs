using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

EmulatorOptions options = EmulatorOptions.Parse(args);

if (options.InstallPrinter)
{
    WindowsPrinterQueueInstaller.Install(options.PrinterName, options.PrinterHost, options.Port);
}

int port = options.Port;
string outputDirectory = Path.Combine(AppContext.BaseDirectory, "receipts");
Directory.CreateDirectory(outputDirectory);

var listener = new TcpListener(IPAddress.Any, port);

try
{
    listener.Start();
}
catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
{
    Console.WriteLine($"Port {port} is already in use. Stop the other ESC/POS emulator or choose another port.");
    Console.WriteLine($"Find the process with: netstat -ano | findstr :{port}");
    Console.WriteLine($"Then stop it with: taskkill /PID <PID> /F");
    Environment.Exit(1);
}

Console.WriteLine($"NepalHMS ESC/POS TCP Emulator listening on 0.0.0.0:{port}");
Console.WriteLine($"Receipt text files will be saved to: {outputDirectory}");
Console.WriteLine($"Windows queue target, when installed: {options.PrinterName} -> {options.PrinterHost}:{port}");
Console.WriteLine("Press Ctrl+C to stop.");

int receiptNumber = 0;

while (true)
{
    using TcpClient client = await listener.AcceptTcpClientAsync();
    int currentReceipt = Interlocked.Increment(ref receiptNumber);

    await using NetworkStream stream = client.GetStream();
    using var buffer = new MemoryStream();
    await stream.CopyToAsync(buffer);

    byte[] bytes = buffer.ToArray();
    string text = EscPosTextRenderer.Render(bytes);
    string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    string path = Path.Combine(outputDirectory, $"receipt-{timestamp}-{currentReceipt:000}.txt");

    await File.WriteAllTextAsync(path, text, Encoding.UTF8);

    Console.WriteLine();
    Console.WriteLine($"--- Receipt #{currentReceipt} ({bytes.Length} bytes) ---");
    Console.WriteLine(text);
    Console.WriteLine($"Saved: {path}");
}

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
            Console.WriteLine("Printer queue installation is only supported on Windows.");
            return;
        }

        if (!IsAdministrator())
        {
            Console.WriteLine("Printer queue was not installed because this terminal is not running as Administrator.");
            Console.WriteLine("Restart PowerShell as Administrator and run with --install-printer.");
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
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(output))
        {
            Console.WriteLine(output.Trim());
        }

        if (process.ExitCode == 0)
        {
            Console.WriteLine($"Windows printer queue ready: {printerName} -> {host}:{port}");
            return;
        }

        Console.WriteLine($"Failed to install Windows printer queue '{printerName}'.");
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.WriteLine(error.Trim());
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
