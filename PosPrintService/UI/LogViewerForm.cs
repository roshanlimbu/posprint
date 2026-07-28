using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace PosPrintService.UI
{
    /// <summary>
    /// Lightweight floating WinForms window displaying real-time POS print service logs and error diagnostics.
    /// </summary>
    public class LogViewerForm : Form
    {
        private RichTextBox _txtLogs;
        private Button _btnClear;
        private Button _btnClose;

        public LogViewerForm()
        {
            Text = "NepalHMS POS Print Service - Activity Log";
            Size = new Size(650, 420);
            MinimumSize = new Size(450, 300);
            StartPosition = FormStartPosition.CenterScreen;
            ShowIcon = false;
            ShowInTaskbar = true;
            BackColor = Color.FromArgb(245, 246, 250);

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 45,
                BackColor = Color.FromArgb(30, 41, 59), // Sleek dark Slate blue header
                Padding = new Padding(15, 0, 15, 0)
            };

            var lblTitle = new Label
            {
                Text = "Print Service Live Logs & Diagnostics",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true
            };
            topPanel.Controls.Add(lblTitle);

            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 55,
                BackColor = Color.White,
                Padding = new Padding(10)
            };

            _btnClose = new Button
            {
                Text = "Close",
                Size = new Size(90, 32),
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderSize = 0;
            _btnClose.Click += (s, e) => this.Hide();

            _btnClear = new Button
            {
                Text = "Clear Logs",
                Size = new Size(100, 32),
                Dock = DockStyle.Left,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            _btnClear.FlatAppearance.BorderSize = 0;
            _btnClear.Click += (s, e) => _txtLogs.Clear();

            bottomPanel.Controls.Add(_btnClose);
            bottomPanel.Controls.Add(new Panel { Width = 10, Dock = DockStyle.Right });
            bottomPanel.Controls.Add(_btnClear);

            _txtLogs = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(15, 23, 42), // Deep navy terminal aesthetic
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 10F, FontStyle.Regular),
                BorderStyle = BorderStyle.None,
                Padding = new Padding(10)
            };

            Controls.Add(_txtLogs);
            Controls.Add(topPanel);
            Controls.Add(bottomPanel);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // Hide window instead of terminating object when cashier clicks X button
                e.Cancel = true;
                Hide();
            }
            else
            {
                base.OnFormClosing(e);
            }
        }

        public void AppendLog(string message, bool isError)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, bool>(AppendLog), message, isError);
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string prefix = isError ? "[ERROR] " : "[OK]    ";
            Color textColor = isError ? Color.FromArgb(248, 113, 113) : Color.FromArgb(134, 239, 172); // Pastel red/green

            int start = _txtLogs.TextLength;
            _txtLogs.AppendText($"[{timestamp}] {prefix}{message}\n");
            int end = _txtLogs.TextLength;

            _txtLogs.Select(start, end - start);
            _txtLogs.SelectionColor = textColor;
            _txtLogs.SelectionStart = _txtLogs.TextLength;
            _txtLogs.ScrollToCaret();
        }
    }
}
