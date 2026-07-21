using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace ClothingRentalPrintAgent
{
    public class PrintAgentContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private HttpListener listener;
        private Thread listenerThread;
        private string selectedPrinter = "";
        private readonly string configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public PrintAgentContext()
        {
            LoadSettings();
            InitializeTray();
            StartHttpServer();
        }

        private void InitializeTray()
        {
            var contextMenu = new ContextMenuStrip();
            
            var statusItem = new ToolStripMenuItem("Trạng thái: Đang hoạt động");
            statusItem.Enabled = false;

            var printerMenuItem = new ToolStripMenuItem("Chọn máy in");
            PopulatePrinters(printerMenuItem);

            var exitItem = new ToolStripMenuItem("Thoát", null, Exit);

            contextMenu.Items.Add(statusItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(printerMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            trayIcon = new NotifyIcon()
            {
                Icon = CreateTrayIcon(),
                ContextMenuStrip = contextMenu,
                Text = "Dịch vụ in nhãn Clothing Rental",
                Visible = true
            };
        }

        private void PopulatePrinters(ToolStripMenuItem parentMenu)
        {
            parentMenu.DropDownItems.Clear();
            
            // Add Default Printer option
            var defaultItem = new ToolStripMenuItem("Mặc định hệ thống", null, (s, e) => SelectPrinter(""));
            defaultItem.Checked = string.IsNullOrEmpty(selectedPrinter);
            parentMenu.DropDownItems.Add(defaultItem);

            parentMenu.DropDownItems.Add(new ToolStripSeparator());

            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                var item = new ToolStripMenuItem(printer, null, (s, e) => SelectPrinter(printer));
                item.Checked = (printer == selectedPrinter);
                parentMenu.DropDownItems.Add(item);
            }
        }

        private void SelectPrinter(string printerName)
        {
            selectedPrinter = printerName;
            SaveSettings();
            
            // Refresh menu checkmarks
            if (trayIcon.ContextMenuStrip.Items[2] is ToolStripMenuItem printerMenu)
            {
                PopulatePrinters(printerMenu);
            }

            trayIcon.ShowBalloonTip(2000, "Chọn máy in", 
                string.IsNullOrEmpty(printerName) ? "Đã chuyển về máy in mặc định." : $"Đã chọn máy in: {printerName}", 
                ToolTipIcon.Info);
        }

        private Icon CreateTrayIcon()
        {
            using (Bitmap bmp = new Bitmap(16, 16))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.FillEllipse(Brushes.Crimson, 0, 0, 16, 16);
                g.DrawEllipse(Pens.White, 0, 0, 15, 15);
                using (Font f = new Font("Segoe UI", 8, FontStyle.Bold))
                {
                    g.DrawString("P", f, Brushes.White, 2, 1);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        private void StartHttpServer()
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:9999/");
            listener.Start();

            listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true
            };
            listenerThread.Start();
        }

        private void ListenLoop()
        {
            while (listener.IsListening)
            {
                try
                {
                    var context = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(HandleRequest, context);
                }
                catch (HttpListenerException)
                {
                    // Listener stopped
                }
            }
        }

        private void HandleRequest(object state)
        {
            var context = (HttpListenerContext)state;
            var request = context.Request;
            var response = context.Response;

            // Handle CORS Preflight
            string origin = request.Headers["Origin"] ?? "*";
            response.Headers.Add("Access-Control-Allow-Origin", origin);
            response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
            response.Headers.Add("Access-Control-Max-Age", "86400");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = (int)HttpStatusCode.NoContent;
                response.Close();
                return;
            }

            if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/print")
            {
                try
                {
                    using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        string body = reader.ReadToEnd();
                        var payload = JsonSerializer.Deserialize<PrintPayload>(body);

                        if (payload != null && !string.IsNullOrEmpty(payload.image))
                        {
                            string base64Data = payload.image;
                            if (base64Data.Contains(","))
                            {
                                base64Data = base64Data.Substring(base64Data.IndexOf(",") + 1);
                            }

                            byte[] imageBytes = Convert.FromBase64String(base64Data);
                            using (var ms = new MemoryStream(imageBytes))
                            using (var img = Image.FromStream(ms))
                            {
                                PrintImageDirectly(img, payload.width, payload.height, payload.orientation);
                            }

                            SendJsonResponse(response, HttpStatusCode.OK, new { success = true, message = "In thành công" });
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    SendJsonResponse(response, HttpStatusCode.InternalServerError, new { success = false, error = ex.Message });
                    return;
                }
            }

            SendJsonResponse(response, HttpStatusCode.NotFound, new { success = false, message = "Endpoint không tồn tại" });
        }

        private void SendJsonResponse(HttpListenerResponse response, HttpStatusCode statusCode, object data)
        {
            try
            {
                response.StatusCode = (int)statusCode;
                response.ContentType = "application/json; charset=utf-8";
                string json = JsonSerializer.Serialize(data);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch
            {
                // Connection closed
            }
            finally
            {
                response.Close();
            }
        }

        private double ParseMm(string input, double defaultValue)
        {
            if (string.IsNullOrEmpty(input)) return defaultValue;
            string numPart = "";
            foreach (char c in input)
            {
                if (char.IsDigit(c) || c == '.' || c == ',')
                {
                    numPart += c;
                }
            }
            double val;
            if (double.TryParse(numPart.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val))
            {
                return val;
            }
            return defaultValue;
        }

        private void PrintImageDirectly(Image img, string widthStr, string heightStr, string orientation)
        {
            PrintDocument pd = new PrintDocument();
            
            // Apply selected printer
            if (!string.IsNullOrEmpty(selectedPrinter))
            {
                pd.PrinterSettings.PrinterName = selectedPrinter;
            }

            double widthInMm = ParseMm(widthStr, 75);
            double heightInMm = ParseMm(heightStr, 20);

            // Convert to hundredths of an inch (1 mm = 3.93701 hundredths of an inch)
            int wInHundredths = (int)Math.Round(widthInMm * 3.93701);
            int hInHundredths = (int)Math.Round(heightInMm * 3.93701);

            PaperSize customSize = new PaperSize("Custom Label", wInHundredths, hInHundredths);
            pd.DefaultPageSettings.PaperSize = customSize;
            pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            pd.OriginAtMargins = true;

            pd.PrintPage += (sender, e) =>
            {
                // Draw image to fit the page bounds exactly
                e.Graphics.DrawImage(img, 0, 0, e.PageBounds.Width, e.PageBounds.Height);
            };

            // Silent print: disable print progress dialog
            pd.PrintController = new StandardPrintController();
            pd.Print();
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(configFile))
                {
                    string json = File.ReadAllText(configFile);
                    var settings = JsonSerializer.Deserialize<AgentSettings>(json);
                    if (settings != null)
                    {
                        selectedPrinter = settings.SelectedPrinter ?? "";
                    }
                }
            }
            catch
            {
                // Fallback to default
            }
        }

        private void SaveSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(new AgentSettings { SelectedPrinter = selectedPrinter });
                File.WriteAllText(configFile, json);
            }
            catch
            {
                // Fail silently
            }
        }

        private void Exit(object sender, EventArgs e)
        {
            // Clean up HttpListener
            if (listener != null && listener.IsListening)
            {
                listener.Stop();
            }

            // Hide tray icon
            trayIcon.Visible = false;
            trayIcon.Dispose();

            Application.Exit();
        }

        private class PrintPayload
        {
            public string image { get; set; }
            public string width { get; set; }
            public string height { get; set; }
            public string orientation { get; set; }
        }

        private class AgentSettings
        {
            public string SelectedPrinter { get; set; }
        }
    }
}
