using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PosPrintService.Models;

namespace PosPrintService.Services
{
    /// <summary>
    /// Asynchronous HTTP Server listening for incoming print jobs from web application tabs.
    /// Supports CORS preflight headers and status monitoring.
    /// </summary>
    public class PrintServer : IDisposable
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Config _config;
        private bool _isRunning;

        public event Action<string, bool>? OnLog;

        public PrintServer(Config config)
        {
            _config = config;
        }

        public void UpdateConfig(Config newConfig)
        {
            _config = newConfig;
        }

        public async Task StartAsync()
        {
            if (_isRunning) return;

            try
            {
                _listener = new HttpListener();
                string prefix127 = $"http://127.0.0.1:{_config.ListenPort}/";
                string prefixLocal = $"http://localhost:{_config.ListenPort}/";
                
                _listener.Prefixes.Add(prefix127);
                _listener.Prefixes.Add(prefixLocal);

                _listener.Start();
                _isRunning = true;
                _cts = new CancellationTokenSource();

                Log($"HTTP Print Server listening on port {_config.ListenPort}...", false);

                // Run listen loop asynchronously in the background
                _ = Task.Run(() => ListenLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                Log($"Failed to start HTTP server: {ex.Message}. Make sure port {_config.ListenPort} is not occupied by another app.", true);
                throw;
            }
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context), token);
                }
                catch (HttpListenerException) when (token.IsCancellationRequested || !_listener.IsListening)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Error accepting client connection: {ex.Message}", true);
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // 1. Set standard CORS headers for cross-origin browser fetch
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Requested-With, Accept");

            try
            {
                // Handle preflight OPTIONS request
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                string path = request.Url?.AbsolutePath.ToLowerInvariant().TrimEnd('/') ?? "";

                if (request.HttpMethod == "GET" && (path == "" || path == "/status"))
                {
                    await HandleStatusRequest(response);
                }
                else if (request.HttpMethod == "POST" && (path == "/print" || path == "/api/print"))
                {
                    await HandlePrintRequest(request, response);
                }
                else
                {
                    response.StatusCode = 404;
                    await WriteJsonResponse(response, new { success = false, error = $"Endpoint not found: {request.HttpMethod} {path}" });
                }
            }
            catch (Exception ex)
            {
                Log($"Unhandled error processing request: {ex.Message}", true);
                try
                {
                    response.StatusCode = 500;
                    await WriteJsonResponse(response, new { success = false, error = "Internal server error: " + ex.Message });
                }
                catch { }
            }
            finally
            {
                try { response.Close(); } catch { }
            }
        }

        private async Task HandleStatusRequest(HttpListenerResponse response)
        {
            var statusObj = new
            {
                status = "online",
                service = "NepalHMS POS Print Service",
                version = "1.0.0",
                active_printer = _config.PrinterName,
                port = _config.ListenPort,
                auto_cut = _config.AutoCut,
                open_cash_drawer = _config.OpenCashDrawer,
                installed_printers = RawPrinterHelper.GetInstalledPrinters()
            };

            response.StatusCode = 200;
            await WriteJsonResponse(response, statusObj);
        }

        private async Task HandlePrintRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            string jsonBody = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(jsonBody))
            {
                response.StatusCode = 400;
                await WriteJsonResponse(response, new { success = false, error = "Request body was empty." });
                return;
            }

            Invoice? invoice;
            try
            {
                var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true };
                invoice = JsonSerializer.Deserialize<Invoice>(jsonBody, options);
            }
            catch (Exception ex)
            {
                Log($"JSON Deserialization failed: {ex.Message}", true);
                response.StatusCode = 400;
                await WriteJsonResponse(response, new { success = false, error = "Invalid JSON invoice payload." });
                return;
            }

            if (invoice == null)
            {
                response.StatusCode = 400;
                await WriteJsonResponse(response, new { success = false, error = "Could not parse invoice payload." });
                return;
            }

            string invNum = !string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? invoice.InvoiceNumber : "N/A";
            Log($"Received print job for Invoice #{invNum} (Total: {invoice.GrandTotal:0.00})...", false);

            // Translate invoice to ESC/POS binary stream
            byte[] receiptBytes = EscPosBuilder.BuildReceipt(invoice, _config);
            string docTitle = $"Invoice {invNum}";

            // Transmit binary payload directly to printer hardware queue
            bool sent = RawPrinterHelper.SendBytesToPrinter(_config.PrinterName, receiptBytes, docTitle, out string errorMsg);

            if (sent)
            {
                Log($"Successfully transmitted {receiptBytes.Length} bytes to printer '{_config.PrinterName}'.", false);
                response.StatusCode = 200;
                await WriteJsonResponse(response, new
                {
                    success = true,
                    message = $"Receipt printed successfully on '{_config.PrinterName}'.",
                    bytes_sent = receiptBytes.Length,
                    invoice_number = invNum
                });
            }
            else
            {
                Log($"Printing failed on '{_config.PrinterName}': {errorMsg}", true);
                response.StatusCode = 500;
                await WriteJsonResponse(response, new
                {
                    success = false,
                    error = errorMsg,
                    printer = _config.PrinterName
                });
            }
        }

        private static async Task WriteJsonResponse(HttpListenerResponse response, object data)
        {
            response.ContentType = "application/json; charset=utf-8";
            string json = JsonSerializer.Serialize(data);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private void Log(string message, bool isError)
        {
            string prefix = isError ? "[ERROR]" : "[INFO]";
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {prefix} {message}");
            OnLog?.Invoke(message, isError);
        }

        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener?.Close();
                _isRunning = false;
                Log("HTTP Print Server stopped.", false);
            }
            catch (Exception ex)
            {
                Log($"Error stopping server: {ex.Message}", true);
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
