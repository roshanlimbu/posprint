using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using PosPrintService.Models;
using PosPrintService.Services;

namespace PosPrintService.UI
{
    /// <summary>
    /// Manages the Windows System Tray icon, background life cycle, interactive menu,
    /// and dynamic printer selection for cashiers without cluttering the desktop taskbar.
    /// </summary>
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _menu;
        private Config _config;
        private PrintServer _server;
        private LogViewerForm _logViewer;

        private ToolStripMenuItem _lblStatus;
        private ToolStripMenuItem _lblPrinter;
        private ToolStripMenuItem _menuPrinters;

        public TrayApplicationContext()
        {
            _config = Config.Load();
            _logViewer = new LogViewerForm();
            _server = new PrintServer(_config);
            _server.OnLog += (msg, isErr) => _logViewer.AppendLog(msg, isErr);

            // Build dynamic context menu
            _menu = new ContextMenuStrip();
            
            var headerItem = new ToolStripMenuItem("NepalHMS POS Service") { Enabled = false };
            headerItem.Font = new Font(headerItem.Font, FontStyle.Bold);
            _menu.Items.Add(headerItem);
            _menu.Items.Add(new ToolStripSeparator());

            _lblStatus = new ToolStripMenuItem($"Port: {_config.ListenPort} (Starting...)") { Enabled = false };
            _lblPrinter = new ToolStripMenuItem($"Target: {_config.PrinterName}") { Enabled = false };
            _menu.Items.Add(_lblStatus);
            _menu.Items.Add(_lblPrinter);
            _menu.Items.Add(new ToolStripSeparator());

            // Submenu for switching printer hardware on the fly
            _menuPrinters = new ToolStripMenuItem("Change Target Printer...");
            _menuPrinters.DropDownOpening += (s, e) => PopulatePrintersSubmenu();
            _menu.Items.Add(_menuPrinters);

            var itemTestPrint = new ToolStripMenuItem("Perform Test Print (Hospital Sample)", null, OnTestPrintClicked);
            _menu.Items.Add(itemTestPrint);
            _menu.Items.Add(new ToolStripSeparator());

            var itemLogs = new ToolStripMenuItem("View Activity Logs", null, (s, e) => ShowLogViewer());
            var itemConfig = new ToolStripMenuItem("Open config.json in Notepad", null, OnOpenConfigClicked);
            var itemReload = new ToolStripMenuItem("Reload Configuration", null, OnReloadConfigClicked);
            
            _menu.Items.Add(itemLogs);
            _menu.Items.Add(itemConfig);
            _menu.Items.Add(itemReload);
            _menu.Items.Add(new ToolStripSeparator());

            var itemExit = new ToolStripMenuItem("Exit POS Service", null, OnExitClicked);
            _menu.Items.Add(itemExit);

            // Initialize System Tray Icon
            _notifyIcon = new NotifyIcon
            {
                Icon = GenerateTrayIcon(Color.FromArgb(34, 197, 94)), // Modern Emerald Green badge
                Text = $"NepalHMS POS Print Service (Port {_config.ListenPort})",
                ContextMenuStrip = _menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => ShowLogViewer();

            // Start HTTP Server asynchronously
            _ = InitializeServerAsync();
        }

        private async Task InitializeServerAsync()
        {
            try
            {
                await _server.StartAsync();
                _lblStatus.Text = $"Status: Online (Port {_config.ListenPort})";
                _notifyIcon.BalloonTipTitle = "NepalHMS POS Print Service";
                _notifyIcon.BalloonTipText = $"Service started on port {_config.ListenPort}.\nTarget Printer: {_config.PrinterName}";
                _notifyIcon.ShowBalloonTip(3000);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Status: ERROR (Port Conflict / Failure)";
                _notifyIcon.Icon = GenerateTrayIcon(Color.FromArgb(239, 68, 68)); // Red indicator
                _notifyIcon.BalloonTipTitle = "Service Startup Failed";
                _notifyIcon.BalloonTipText = $"Could not listen on port {_config.ListenPort}. Error: {ex.Message}";
                _notifyIcon.ShowBalloonTip(5000);
            }
        }

        private void PopulatePrintersSubmenu()
        {
            _menuPrinters.DropDownItems.Clear();
            var installed = RawPrinterHelper.GetInstalledPrinters();

            if (installed.Count == 0)
            {
                _menuPrinters.DropDownItems.Add(new ToolStripMenuItem("No printers discovered in Windows") { Enabled = false });
                return;
            }

            foreach (string printer in installed)
            {
                var item = new ToolStripMenuItem(printer);
                if (string.Equals(printer, _config.PrinterName, StringComparison.OrdinalIgnoreCase))
                {
                    item.Checked = true;
                    item.Font = new Font(item.Font, FontStyle.Bold);
                }

                item.Click += (s, e) =>
                {
                    _config.PrinterName = printer;
                    if (_config.Save())
                    {
                        _server.UpdateConfig(_config);
                        _lblPrinter.Text = $"Target: {_config.PrinterName}";
                        _notifyIcon.BalloonTipTitle = "Printer Changed";
                        _notifyIcon.BalloonTipText = $"Target billing printer switched to: {_config.PrinterName}";
                        _notifyIcon.ShowBalloonTip(2000);
                        _logViewer.AppendLog($"Active target printer changed to: {_config.PrinterName}", false);
                    }
                };

                _menuPrinters.DropDownItems.Add(item);
            }
        }

        private void OnTestPrintClicked(object? sender, EventArgs e)
        {
            _logViewer.AppendLog($"Initiating Test Print to '{_config.PrinterName}'...", false);
            
            // Reconstruct exact sample invoice layout from cashier billing screen
            var sampleInvoice = new Invoice
            {
                HospitalName = "Family Care Hospital",
                Address = "N/A",
                PanNumber = "N/A",
                InvoiceType = "NON-VAT INVOICE",
                CopyType = "COPY OF ORIGINAL",
                BillNo = "Billing-01-83-84-000001",
                TxnDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                IssueDate = DateTime.Now.ToString("yyyy-MM-dd"),
                DateBS = "20830407",
                Counter = "Billing-01",
                Payment = "Cash",
                PatientName = "test test",
                HospitalNo = "HN-2026-000001",
                AgeSex = "23 / unknown",
                CurrencyPrefix = "Rs.",
                SubTotal = 150.00m,
                VatExempt = 150.00m,
                Total = 150.00m,
                PaidByPatient = 150.00m,
                Paid = 150.00m,
                InWords = "In words: NPR One Hundred Fifty Rupees Only",
                FooterNotes = "Computer-generated receipt\nThank you"
            };

            sampleInvoice.Items.Add(new InvoiceItem
            {
                Item = "Flat OPD Consultation",
                Qty = "1.00",
                Rate = 150.00m,
                Amt = 150.00m
            });

            byte[] bytes = EscPosBuilder.BuildReceipt(sampleInvoice, _config);
            bool success = RawPrinterHelper.SendBytesToPrinter(_config.PrinterName, bytes, "POS Hospital Sample", out string errMsg);

            if (success)
            {
                _logViewer.AppendLog($"Test print sent successfully ({bytes.Length} bytes transmitted to {_config.PrinterName}).", false);
                _notifyIcon.ShowBalloonTip(2000, "Test Print Successful", $"Hospital sample receipt dispatched to {_config.PrinterName}.", ToolTipIcon.Info);
            }
            else
            {
                _logViewer.AppendLog($"Test print failed on '{_config.PrinterName}': {errMsg}", true);
                MessageBox.Show($"Could not transmit to printer '{_config.PrinterName}':\n\n{errMsg}\n\nPlease check if the POS-76 printer is turned on, online, and set as default.", 
                                "Printer Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnOpenConfigClicked(object? sender, EventArgs e)
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                if (!File.Exists(configPath))
                {
                    _config.Save();
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{configPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open Notepad: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnReloadConfigClicked(object? sender, EventArgs e)
        {
            _config = Config.Load();
            _server.UpdateConfig(_config);
            _lblPrinter.Text = $"Target: {_config.PrinterName}";
            _notifyIcon.Text = $"NepalHMS POS Print Service (Port {_config.ListenPort})";
            _logViewer.AppendLog("Configuration reloaded from config.json disk file.", false);
            _notifyIcon.ShowBalloonTip(2000, "Configuration Reloaded", $"Updated printer target: {_config.PrinterName}\nListening Port: {_config.ListenPort}", ToolTipIcon.Info);
        }

        private void ShowLogViewer()
        {
            _logViewer.Show();
            if (_logViewer.WindowState == FormWindowState.Minimized)
                _logViewer.WindowState = FormWindowState.Normal;
            _logViewer.BringToFront();
            _logViewer.Activate();
        }

        private void OnExitClicked(object? sender, EventArgs e)
        {
            _server.Stop();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _logViewer.Dispose();
            Application.Exit();
        }

        private static Icon GenerateTrayIcon(Color accentColor)
        {
            try
            {
                using var bitmap = new Bitmap(32, 32);
                using var g = Graphics.FromImage(bitmap);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using var brush = new SolidBrush(Color.FromArgb(30, 41, 59)); // Dark slate
                g.FillRoundedRectangle(brush, 1, 1, 30, 30, 6);

                using var whiteBrush = new SolidBrush(Color.White);
                g.FillRectangle(whiteBrush, 7, 5, 18, 10);

                using var accentBrush = new SolidBrush(accentColor);
                g.FillRoundedRectangle(accentBrush, 4, 13, 24, 13, 3);

                g.FillRectangle(whiteBrush, 8, 20, 16, 10);
                using var pen = new Pen(Color.FromArgb(203, 213, 225), 1.5f);
                g.DrawLine(pen, 11, 23, 21, 23);
                g.DrawLine(pen, 11, 26, 21, 26);

                IntPtr hIcon = bitmap.GetHicon();
                return Icon.FromHandle(hIcon);
            }
            catch
            {
                return SystemIcons.Information;
            }
        }
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush brush, int x, int y, int width, int height, int radius)
        {
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }
    }
}
