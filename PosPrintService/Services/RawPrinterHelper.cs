using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace PosPrintService.Services
{
    /// <summary>
    /// Wraps Windows Native Spooler APIs (winspool.drv) via P/Invoke to send raw bytes
    /// directly to printer hardware queue without Windows driver raster transformation.
    /// </summary>
    public static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public string pDocName = null!;
            [MarshalAs(UnmanagedType.LPStr)]
            public string pOutputFile = null!;
            [MarshalAs(UnmanagedType.LPStr)]
            public string pDataType = "RAW";
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern int StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        /// <summary>
        /// Retrieves a list of installed Windows printer queue names.
        /// </summary>
        public static List<string> GetInstalledPrinters()
        {
            var printerNames = new List<string>();
            if (!OperatingSystem.IsWindows())
            {
                // Return dummy names if evaluated on a non-Windows development workstation
                printerNames.Add("pos76 (Virtual / Dev Mode)");
                return printerNames;
            }

            try
            {
                foreach (string? printer in PrinterSettings.InstalledPrinters)
                {
                    if (!string.IsNullOrWhiteSpace(printer))
                        printerNames.Add(printer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] Could not enum installed printers: {ex.Message}");
            }
            return printerNames;
        }

        /// <summary>
        /// Sends raw binary data directly to the specified Windows printer queue.
        /// </summary>
        public static bool SendBytesToPrinter(string printerName, byte[] bytes, string documentTitle, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!OperatingSystem.IsWindows())
            {
                errorMessage = "[Dev Mode] Non-Windows OS detected. Raw spooler commands simulated successfully.";
                Console.WriteLine($"[RawPrinterHelper] Simulating send of {bytes.Length} bytes to printer '{printerName}' for document '{documentTitle}'.");
                return true;
            }

            IntPtr pBytes = IntPtr.Zero;
            IntPtr hPrinter = IntPtr.Zero;

            try
            {
                // Open printer queue
                if (!OpenPrinter(printerName.Trim(), out hPrinter, IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    errorMessage = $"OpenPrinter failed for printer '{printerName}'. Win32 Error: {err}. Verify that the printer queue name matches exactly and is online.";
                    return false;
                }

                DOCINFOA di = new DOCINFOA
                {
                    pDocName = string.IsNullOrWhiteSpace(documentTitle) ? "POS Silent Receipt" : documentTitle,
                    pDataType = "RAW"
                };

                // Start document
                if (StartDocPrinter(hPrinter, 1, di) == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    errorMessage = $"StartDocPrinter failed. Win32 Error: {err}";
                    ClosePrinter(hPrinter);
                    return false;
                }

                if (!StartPagePrinter(hPrinter))
                {
                    int err = Marshal.GetLastWin32Error();
                    errorMessage = $"StartPagePrinter failed. Win32 Error: {err}";
                    EndDocPrinter(hPrinter);
                    ClosePrinter(hPrinter);
                    return false;
                }

                // Allocate native memory for the binary buffer
                pBytes = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, pBytes, bytes.Length);

                bool success = WritePrinter(hPrinter, pBytes, bytes.Length, out int dwWritten);
                bool completeWrite = success && dwWritten == bytes.Length;
                if (!completeWrite)
                {
                    int err = Marshal.GetLastWin32Error();
                    errorMessage = $"WritePrinter wrote {dwWritten} of {bytes.Length} bytes. Win32 Error: {err}";
                }

                EndPagePrinter(hPrinter);
                EndDocPrinter(hPrinter);
                ClosePrinter(hPrinter);

                return completeWrite;
            }
            catch (Exception ex)
            {
                errorMessage = $"Exception in RawPrinterHelper: {ex.Message}";
                if (hPrinter != IntPtr.Zero)
                {
                    try { ClosePrinter(hPrinter); } catch { }
                }
                return false;
            }
            finally
            {
                if (pBytes != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pBytes);
                }
            }
        }
    }
}
