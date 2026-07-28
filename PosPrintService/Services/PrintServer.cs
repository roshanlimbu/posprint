using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
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
        private const long MaxRequestBytes = 1024 * 1024;

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Config _config;
        private bool _isRunning;
        private readonly ConcurrentDictionary<string, DateTimeOffset> _acceptedJobs = new(StringComparer.Ordinal);

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

            try
            {
                if (!ApplyCorsHeaders(request, response))
                {
                    Log($"Blocked request from unconfigured origin: {request.Headers["Origin"] ?? "N/A"}", true);
                    response.StatusCode = 403;
                    await WriteJsonResponse(response, new { success = false, error = "This web application origin is not allowed to use the POS print service." });
                    return;
                }

                // Handle preflight OPTIONS request
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 204;
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
            if (_config.LogRequests)
            {
                Log("Status probe received.", false);
            }

            var statusObj = new
            {
                status = "online",
                service = "NepalHMS POS Print Service",
                version = "1.2.0",
                active_printer = _config.PrinterName,
                port = _config.ListenPort,
                auto_cut = _config.AutoCut,
                open_cash_drawer = _config.OpenCashDrawer,
                authentication_required = true,
                installed_printers = RawPrinterHelper.GetInstalledPrinters()
            };

            response.StatusCode = 200;
            await WriteJsonResponse(response, statusObj);
        }

        private async Task HandlePrintRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!IsAuthorized(request))
            {
                response.StatusCode = 401;
                await WriteJsonResponse(response, new { success = false, error = "A valid X-PosPrint-Token header is required." });
                return;
            }

            if (request.ContentLength64 > MaxRequestBytes)
            {
                response.StatusCode = 413;
                await WriteJsonResponse(response, new { success = false, error = "The print payload exceeds the 1 MB limit." });
                return;
            }

            using var payloadStream = new MemoryStream();
            byte[] payloadBuffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await request.InputStream.ReadAsync(payloadBuffer, 0, payloadBuffer.Length)) > 0)
            {
                if (payloadStream.Length + bytesRead > MaxRequestBytes)
                {
                    response.StatusCode = 413;
                    await WriteJsonResponse(response, new { success = false, error = "The print payload exceeds the 1 MB limit." });
                    return;
                }

                await payloadStream.WriteAsync(payloadBuffer, 0, bytesRead);
            }

            string jsonBody = request.ContentEncoding.GetString(payloadStream.ToArray());

            if (string.IsNullOrWhiteSpace(jsonBody))
            {
                response.StatusCode = 400;
                await WriteJsonResponse(response, new { success = false, error = "Request body was empty." });
                return;
            }

            var options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                PropertyNameCaseInsensitive = true
            };

            PrintRequestEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<PrintRequestEnvelope>(jsonBody, options);
            }
            catch (Exception ex)
            {
                Log($"JSON Deserialization failed: {ex.Message}", true);
                response.StatusCode = 400;
                await WriteJsonResponse(response, new { success = false, error = "Invalid JSON print payload." });
                return;
            }

            if (envelope == null)
            {
                response.StatusCode = 400;
                await WriteJsonResponse(response, new { success = false, error = "Could not parse print payload." });
                return;
            }

            if (string.IsNullOrWhiteSpace(envelope.JobId))
            {
                response.StatusCode = 422;
                await WriteJsonResponse(response, new { success = false, error = "JobId is required for duplicate-print protection." });
                return;
            }

            byte[] printBytes;
            string documentTitle;
            string reference;

            try
            {
                if (string.Equals(envelope.DocumentType, "invoice", StringComparison.OrdinalIgnoreCase))
                {
                    Invoice? invoice = JsonSerializer.Deserialize<Invoice>(jsonBody, options);

                    if (invoice == null)
                    {
                        response.StatusCode = 400;
                        await WriteJsonResponse(response, new { success = false, error = "Could not parse invoice payload." });
                        return;
                    }

                    reference = !string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? invoice.InvoiceNumber : "N/A";
                    printBytes = EscPosBuilder.BuildReceipt(invoice, _config);
                    documentTitle = $"Invoice {reference}";
                    Log($"Received print job for Invoice #{reference} (Total: {invoice.GrandTotal:0.00})...", false);
                }
                else if (string.Equals(envelope.DocumentType, "master_bill", StringComparison.OrdinalIgnoreCase))
                {
                    MasterBillReport? report = JsonSerializer.Deserialize<MasterBillReport>(jsonBody, options);

                    if (report == null)
                    {
                        response.StatusCode = 400;
                        await WriteJsonResponse(response, new { success = false, error = "Could not parse master bill payload." });
                        return;
                    }

                    reference = report.Period ?? "All dates";
                    printBytes = EscPosBuilder.BuildMasterBill(report, _config);
                    documentTitle = "Master Bill Report";
                    Log($"Received Master Bill print job with {report.Bills.Count} bill(s)...", false);
                }
                else
                {
                    response.StatusCode = 422;
                    await WriteJsonResponse(response, new { success = false, error = "DocumentType must be 'invoice' or 'master_bill'." });
                    return;
                }
            }
            catch (JsonException ex)
            {
                Log($"Print payload validation failed: {ex.Message}", true);
                response.StatusCode = 400;
                await WriteJsonResponse(response, new { success = false, error = "The print payload contains invalid field values." });
                return;
            }

            if (!TryReserveJob(envelope.JobId))
            {
                Log($"Ignored duplicate print job '{envelope.JobId}' ({envelope.DocumentType}: {reference}).", false);
                response.StatusCode = 200;
                await WriteJsonResponse(response, new
                {
                    success = true,
                    duplicate = true,
                    message = "This print job was already accepted and was not printed again.",
                    document_type = envelope.DocumentType,
                    reference,
                    job_id = envelope.JobId
                });
                return;
            }

            // Transmit binary payload directly to printer hardware queue
            bool sent = RawPrinterHelper.SendBytesToPrinter(_config.PrinterName, printBytes, documentTitle, out string errorMsg);

            if (sent)
            {
                Log($"Successfully transmitted {printBytes.Length} bytes to printer '{_config.PrinterName}'.", false);
                response.StatusCode = 200;
                await WriteJsonResponse(response, new
                {
                    success = true,
                    message = $"Document printed successfully on '{_config.PrinterName}'.",
                    bytes_sent = printBytes.Length,
                    document_type = envelope.DocumentType,
                    reference,
                    job_id = envelope.JobId
                });
            }
            else
            {
                _acceptedJobs.TryRemove(envelope.JobId, out _);
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

        private bool ApplyCorsHeaders(HttpListenerRequest request, HttpListenerResponse response)
        {
            string? origin = request.Headers["Origin"];

            if (!string.IsNullOrWhiteSpace(origin))
            {
                bool allowed = _config.AllowedOrigins.Any(configuredOrigin =>
                    string.Equals(
                        configuredOrigin.TrimEnd('/'),
                        origin.TrimEnd('/'),
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                if (!allowed)
                {
                    return false;
                }

                response.AddHeader("Access-Control-Allow-Origin", origin);
                response.AddHeader("Vary", "Origin");
            }

            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Accept, X-PosPrint-Token");
            response.AddHeader("Access-Control-Allow-Private-Network", "true");
            response.AddHeader("Access-Control-Max-Age", "600");

            return true;
        }

        private bool IsAuthorized(HttpListenerRequest request)
        {
            string suppliedToken = request.Headers["X-PosPrint-Token"] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_config.ApiToken) || string.IsNullOrWhiteSpace(suppliedToken))
                return false;

            byte[] expected = Encoding.UTF8.GetBytes(_config.ApiToken);
            byte[] supplied = Encoding.UTF8.GetBytes(suppliedToken);

            return expected.Length == supplied.Length &&
                   CryptographicOperations.FixedTimeEquals(expected, supplied);
        }

        private bool TryReserveJob(string jobId)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset cutoff = now.AddMinutes(-Math.Max(1, _config.IdempotencyWindowMinutes));

            foreach (var acceptedJob in _acceptedJobs)
            {
                if (acceptedJob.Value < cutoff)
                    _acceptedJobs.TryRemove(acceptedJob.Key, out _);
            }

            while (true)
            {
                if (!_acceptedJobs.TryGetValue(jobId, out DateTimeOffset acceptedAt))
                    return _acceptedJobs.TryAdd(jobId, now);

                if (acceptedAt >= cutoff)
                    return false;

                if (_acceptedJobs.TryUpdate(jobId, now, acceptedAt))
                    return true;
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
