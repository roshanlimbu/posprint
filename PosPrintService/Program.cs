using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using PosPrintService.UI;

namespace PosPrintService
{
    internal static class Program
    {
        private static Mutex? _mutex;

        /// <summary>
        /// The main entry point for the silent POS print tray service.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Register Code Pages provider for OEM DOS character encoding (Code Page 437 / 850 support in .NET 8)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Prevent duplicate service instances using an OS Mutex
            const string mutexName = "Global\\NepalHmsPosPrintServiceMutex_9111";
            _mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("NepalHMS POS Print Service is already running in the background.\n\nPlease look for the green printer icon in your Windows System Tray (near the clock at the bottom right).",
                                "Service Already Active",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                return;
            }

            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new TrayApplicationContext());
            }
            finally
            {
                if (_mutex != null)
                {
                    try { _mutex.ReleaseMutex(); } catch { }
                    _mutex.Dispose();
                }
            }
        }
    }
}
