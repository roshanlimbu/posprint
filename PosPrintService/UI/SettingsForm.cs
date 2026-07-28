using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PosPrintService.Models;
using PosPrintService.Services;

namespace PosPrintService.UI
{
    public class SettingsForm : Form
    {
        private readonly ComboBox _printerName;
        private readonly NumericUpDown _listenPort;
        private readonly NumericUpDown _receiptWidth;
        private readonly NumericUpDown _characterEncoding;
        private readonly NumericUpDown _idempotencyWindowMinutes;
        private readonly CheckBox _autoCut;
        private readonly CheckBox _openCashDrawer;
        private readonly CheckBox _logRequests;
        private readonly TextBox _apiToken;
        private readonly TextBox _allowedOrigins;

        public SettingsForm(Config config)
        {
            Text = "NepalHMS POS Print Service Settings";
            Size = new Size(620, 610);
            MinimumSize = new Size(560, 560);
            StartPosition = FormStartPosition.CenterScreen;
            ShowIcon = false;
            ShowInTaskbar = true;
            BackColor = Color.FromArgb(245, 246, 250);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 12,
                Padding = new Padding(18),
                AutoScroll = true
            };

            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _printerName = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 340 };
            foreach (string printer in RawPrinterHelper.GetInstalledPrinters())
            {
                _printerName.Items.Add(printer);
            }

            _printerName.Text = config.PrinterName;

            _listenPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Clamp(config.ListenPort, 1, 65535), Width = 120 };
            _receiptWidth = new NumericUpDown { Minimum = 24, Maximum = 64, Value = Clamp(config.ReceiptWidth, 24, 64), Width = 120 };
            _characterEncoding = new NumericUpDown { Minimum = 1, Maximum = 99999, Value = Clamp(config.CharacterEncoding, 1, 99999), Width = 120 };
            _idempotencyWindowMinutes = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = Clamp(config.IdempotencyWindowMinutes, 1, 1440), Width = 120 };
            _autoCut = new CheckBox { Checked = config.AutoCut };
            _openCashDrawer = new CheckBox { Checked = config.OpenCashDrawer };
            _logRequests = new CheckBox { Checked = config.LogRequests };
            _apiToken = new TextBox { Text = config.ApiToken, Width = 340 };
            _allowedOrigins = new TextBox
            {
                Text = string.Join(Environment.NewLine, config.AllowedOrigins ?? []),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Height = 90,
                Width = 340
            };

            AddRow(root, 0, "Printer name", _printerName);
            AddRow(root, 1, "Listen port", _listenPort);
            AddRow(root, 2, "Receipt width", _receiptWidth);
            AddRow(root, 3, "Character encoding", _characterEncoding);
            AddRow(root, 4, "Duplicate window minutes", _idempotencyWindowMinutes);
            AddRow(root, 5, "Auto cut", _autoCut);
            AddRow(root, 6, "Open cash drawer", _openCashDrawer);
            AddRow(root, 7, "Log requests", _logRequests);

            var tokenPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            var generateToken = new Button { Text = "Generate", Width = 90, Height = 28 };
            generateToken.Click += (s, e) => _apiToken.Text = Config.CreateApiToken();
            tokenPanel.Controls.Add(_apiToken);
            tokenPanel.Controls.Add(generateToken);
            AddRow(root, 8, "API token", tokenPanel);

            AddRow(root, 9, "Allowed origins", _allowedOrigins);

            var help = new Label
            {
                Text = "Allowed origins must match the website exactly, for example https://nepalihms.needtechnosoft.com. Port changes require restarting the tray service.",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(71, 85, 105),
                AutoSize = true
            };
            root.Controls.Add(help, 1, 10);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            var save = new Button { Text = "Save", Width = 95, Height = 32, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Width = 95, Height = 32, DialogResult = DialogResult.Cancel };
            save.Click += OnSaveClicked;
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 1, 11);

            Controls.Add(root);
            AcceptButton = save;
            CancelButton = cancel;
        }

        public void ApplyTo(Config config)
        {
            config.PrinterName = _printerName.Text.Trim();
            config.ListenPort = (int)_listenPort.Value;
            config.AutoCut = _autoCut.Checked;
            config.OpenCashDrawer = _openCashDrawer.Checked;
            config.CharacterEncoding = (int)_characterEncoding.Value;
            config.ReceiptWidth = (int)_receiptWidth.Value;
            config.LogRequests = _logRequests.Checked;
            config.ApiToken = _apiToken.Text.Trim();
            config.AllowedOrigins = ParseOrigins();
            config.IdempotencyWindowMinutes = (int)_idempotencyWindowMinutes.Value;
        }

        private void OnSaveClicked(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_printerName.Text))
            {
                ShowValidationError("Printer name is required.");
                DialogResult = DialogResult.None;
                return;
            }

            if (string.IsNullOrWhiteSpace(_apiToken.Text))
            {
                ShowValidationError("API token is required. Click Generate if you do not have one yet.");
                DialogResult = DialogResult.None;
                return;
            }

            try
            {
                ParseOrigins();
            }
            catch (InvalidOperationException ex)
            {
                ShowValidationError(ex.Message);
                DialogResult = DialogResult.None;
                return;
            }
        }

        private List<string> ParseOrigins()
        {
            var origins = _allowedOrigins.Lines
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string origin in origins)
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                    uri.AbsolutePath != "/" ||
                    !string.IsNullOrEmpty(uri.Query) ||
                    !string.IsNullOrEmpty(uri.Fragment))
                {
                    throw new InvalidOperationException($"Invalid allowed origin: {origin}");
                }
            }

            return origins;
        }

        private static void AddRow(TableLayoutPanel root, int row, string labelText, Control control)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = new Label
            {
                Text = labelText,
                Dock = DockStyle.Fill,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(30, 41, 59),
                Padding = new Padding(0, 6, 8, 6)
            };

            control.Margin = new Padding(0, 4, 0, 4);
            root.Controls.Add(label, 0, row);
            root.Controls.Add(control, 1, row);
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Min(Math.Max(value, min), max);
        }

        private static void ShowValidationError(string message)
        {
            MessageBox.Show(message, "Invalid Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
