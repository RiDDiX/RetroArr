using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibUsbDotNet;
using LibUsbDotNet.Main;
using LibUsbDotNet.Descriptors; // Missing directive added
using Newtonsoft.Json;

namespace RetroArr.UsbHelper
{
    class Program
    {
        private const int SWITCH_VENDOR_ID = 0x057E;
        private const int CMD_TYPE_REQUEST = 0;
        private const int CMD_TYPE_RESPONSE = 1;
        private const int CMD_TYPE_ACK = 2;
        private const int CMD_ID_EXIT = 0;
        private const int CMD_ID_FILE_RANGE = 2;
        private const int CMD_ID_LIST = 3;
        private const int BUFFER_SEGMENT_DATA_SIZE = 0x100000; // 1MB

        static async Task<int> Main(string[] args)
        {
            string filePath = null;
            bool listMode = false;

            // Simple arg parsing
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--install" && i + 1 < args.Length)
                {
                    filePath = args[i + 1];
                }
                else if (args[i] == "--list")
                {
                    listMode = true;
                }
            }

            if (listMode)
            {
                ListDevices();
                return 0;
            }

            if (string.IsNullOrEmpty(filePath))
            {
                ReportError("No file path provided. Use --install <path> or --list");
                return 1;
            }

            if (!File.Exists(filePath))
            {
                ReportError($"File not found: {filePath}");
                return 1;
            }

            try
            {
                ReportStatus("Searching for Switch...");
                
                // 1. Find and Open Device
                IUsbDevice usbDevice = OpenDevice();
                if (usbDevice == null)
                {
                    ReportError("Switch not found or could not be opened. Ensure DBI Backend is running.");
                    return 1;
                }

                try
                {
                    // 2. Setup Endpoints
                    UsbEndpointReader reader;
                    UsbEndpointWriter writer;
                    if (!SetupEndpoints(usbDevice, out reader, out writer))
                    {
                        ReportError("Failed to configure USB endpoints.");
                        return 1;
                    }

                    // 3. Run Install Loop
                    ReportStatus("Connected. Waiting for DBI request...");
                    
                    // Since this is a dedicated process, we can just block/await until done
                    bool finished = await PerformInstall(reader, writer, filePath);
                    
                    if (finished)
                    {
                        ReportProgress(100, "Installation Complete");
                        return 0;
                    }
                    else
                    {
                        // "Normally" aborted by console is considered a 'success' in terms of protocol flow
                        // but we might want to flag it differently. For now, 0 is fine.
                        ReportStatus("Installation Stopped by Console");
                        return 0;
                    }
                }
                finally
                {
                    if (usbDevice != null && usbDevice.IsOpen)
                        usbDevice.Close();
                    
                    // Explicitly free libusb context to be proper, though process exit handles it.
                    UsbDevice.Exit();
                }
            }
            catch (Exception ex)
            {
                ReportError($"Critical Error: {ex.Message}");
                return 1;
            }
        }

        private static void ListDevices()
        {
            var devices = new List<string>();
            try
            {
                var allDevices = UsbDevice.AllDevices;
                foreach (UsbRegistry registry in allDevices)
                {
                    if (registry.Vid == SWITCH_VENDOR_ID)
                    {
                        string modeName = registry.Pid == 0x3000 ? "DBI Backend" : "Regular/MTP";
                        devices.Add($"Nintendo Switch ({registry.Pid:X4} - {modeName})");
                    }
                }
                
                // Output raw list wrapped in object or just array? 
                // The consumer expects list of strings.
                var result = new { devices = devices };
                Console.WriteLine(JsonConvert.SerializeObject(result));
            }
            catch (Exception ex)
            {
                ReportError($"Scan Error: {ex.Message}");
            }
        }

        private static IUsbDevice OpenDevice()
        {
            // Similar logic to v58/v59: Scan AllDevices to ensure fresh list
            var allDevices = UsbDevice.AllDevices;
            foreach (UsbRegistry registry in allDevices)
            {
                if (registry.Vid == SWITCH_VENDOR_ID && registry.Pid == 0x3000)
                {
                    UsbDevice device;
                    if (registry.Open(out device))
                    {
                        return (IUsbDevice)device; // Explicit cast added
                    }
                }
            }
            return null;
        }

        private static bool SetupEndpoints(IUsbDevice device, out UsbEndpointReader reader, out UsbEndpointWriter writer)
        {
            reader = null;
            writer = null;

            try 
            {
                // Ensure Config 1
                try { device.SetConfiguration(1); } catch {}

                if (device.ClaimInterface(0)) // Assuming Interface 0 like before
                {
                    // Find Endpoints
                    UsbEndpointDescriptor readEp = null;
                    UsbEndpointDescriptor writeEp = null;

                    var config = device.Configs[0]; 
                    // Note: If configs are empty, libusb might not have parsed descriptors. 
                    // Assuming standard Switch behavior here.
                    
                    foreach (var interfaceInfo in config.InterfaceInfoList)
                    {
                         foreach (var ep in interfaceInfo.EndpointInfoList)
                         {
                             if ((ep.Descriptor.EndpointID & 0x80) == 0x80) readEp = ep.Descriptor; // IN
                             else writeEp = ep.Descriptor; // OUT
                         }
                    }

                    if (readEp != null && writeEp != null)
                    {
                        reader = device.OpenEndpointReader((ReadEndpointID)readEp.EndpointID);
                        writer = device.OpenEndpointWriter((WriteEndpointID)writeEp.EndpointID);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Endpoint setup failed: {ex.Message}");
            }
            return false;
        }

        private static async Task<bool> PerformInstall(UsbEndpointReader reader, UsbEndpointWriter writer, string filePath)
        {
            byte[] buffer = new byte[0x200];
            long totalSize = new FileInfo(filePath).Length;
            
            while (true)
            {
                int bytesRead;
                ErrorCode ec = reader.Read(buffer, 100, out bytesRead);
                
                if (ec != ErrorCode.None && ec != ErrorCode.IoTimedOut)
                {
                    throw new Exception($"USB Read Error: {ec}");
                }

                if (bytesRead > 0)
                {
                    string magic = Encoding.ASCII.GetString(buffer, 0, 4);
                    if (magic == "DBI0")
                    {
                        int cmdType = BitConverter.ToInt32(buffer, 4);
                        int cmdId = BitConverter.ToInt32(buffer, 8);
                        int dataSize = BitConverter.ToInt32(buffer, 12);
                        
                        if (cmdType == CMD_TYPE_REQUEST)
                        {
                            switch (cmdId)
                            {
                                case CMD_ID_EXIT:
                                    SendExitResponse(writer);
                                    return false; // Exit signal

                                case CMD_ID_LIST:
                                    HandleDbiList(reader, writer, filePath);
                                    break;

                                case CMD_ID_FILE_RANGE:
                                    await HandleDbiFileRange(reader, writer, filePath, dataSize, totalSize, buffer, bytesRead);
                                    break;
                            }
                        }
                    }
                }
                
                await Task.Delay(5);
            }
        }

        private static void SendExitResponse(UsbEndpointWriter writer)
        {
            var exitResp = new List<byte>();
            exitResp.AddRange(Encoding.ASCII.GetBytes("DBI0"));
            exitResp.AddRange(BitConverter.GetBytes(CMD_TYPE_RESPONSE));
            exitResp.AddRange(BitConverter.GetBytes(CMD_ID_EXIT));
            exitResp.AddRange(BitConverter.GetBytes(0));
            writer.Write(exitResp.ToArray(), 1000, out _);
        }

        private static void HandleDbiList(UsbEndpointReader reader, UsbEndpointWriter writer, string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var fileBytes = Encoding.UTF8.GetBytes(fileName + "\n");
            var listLen = fileBytes.Length;

            var header = new List<byte>();
            header.AddRange(Encoding.ASCII.GetBytes("DBI0"));
            header.AddRange(BitConverter.GetBytes(CMD_TYPE_RESPONSE));
            header.AddRange(BitConverter.GetBytes(CMD_ID_LIST));
            header.AddRange(BitConverter.GetBytes(listLen));
            
            writer.Write(header.ToArray(), 1000, out _);

            if (listLen > 0)
            {
                byte[] ackBuf = new byte[16];
                int ackRead;
                reader.Read(ackBuf, 5000, out ackRead); // Wait for ACK
                writer.Write(fileBytes, 2000, out _); // Send Data
            }
        }

        private static async Task HandleDbiFileRange(UsbEndpointReader reader, UsbEndpointWriter writer, string filePath, int headerSize, long totalSize, byte[] initialBuffer, int initialRead)
        {
            // 1. Send ACK
            var ack = new List<byte>();
            ack.AddRange(Encoding.ASCII.GetBytes("DBI0"));
            ack.AddRange(BitConverter.GetBytes(CMD_TYPE_ACK));
            ack.AddRange(BitConverter.GetBytes(CMD_ID_FILE_RANGE));
            ack.AddRange(BitConverter.GetBytes(headerSize));
            writer.Write(ack.ToArray(), 1000, out _);

            // 2. Read Range Header
            byte[] rangeHeader = new byte[headerSize];
            int alreadyBuffered = Math.Max(0, initialRead - 16);
            if (alreadyBuffered > 0) Array.Copy(initialBuffer, 16, rangeHeader, 0, Math.Min(alreadyBuffered, headerSize));
            
            if (alreadyBuffered < headerSize)
            {
                byte[] extra = new byte[headerSize - alreadyBuffered];
                int read;
                reader.Read(extra, 2000, out read);
                Array.Copy(extra, 0, rangeHeader, alreadyBuffered, read);
            }

            // 3. Parse
            uint rangeSize = BitConverter.ToUInt32(rangeHeader, 0); 
            long rangeOffset = BitConverter.ToInt64(rangeHeader, 4);

            // 4. Send Response Header
            var responseHeader = new List<byte>();
            responseHeader.AddRange(Encoding.ASCII.GetBytes("DBI0"));
            responseHeader.AddRange(BitConverter.GetBytes(CMD_TYPE_RESPONSE));
            responseHeader.AddRange(BitConverter.GetBytes(CMD_ID_FILE_RANGE));
            responseHeader.AddRange(BitConverter.GetBytes((int)rangeSize));
            writer.Write(responseHeader.ToArray(), 1000, out _);

            // 5. Read ACK
            byte[] finalAck = new byte[16];
            reader.Read(finalAck, 5000, out _);

            // 6. Send Content
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(rangeOffset, SeekOrigin.Begin);
                byte[] chunk = new byte[BUFFER_SEGMENT_DATA_SIZE];
                long remaining = rangeSize;
                
                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(chunk.Length, remaining);
                    int actuallyRead = await fs.ReadAsync(chunk, 0, toRead);
                    
                    ErrorCode ec = writer.Write(chunk, 0, actuallyRead, 10000, out int sent);
                    if (ec != ErrorCode.None) throw new Exception($"Write Failed: {ec}");
                    
                    remaining -= sent;
                    
                    // Report Progress
                    long uploaded = rangeOffset + (rangeSize - remaining);
                    double p = (double)uploaded / totalSize * 100.0;
                    ReportProgress(p, $"Transferring: {p:F1}%");
                }
            }
        }

        // --- JSON Reporting Helper ---
        private static void ReportStatus(string status)
        {
            var msg = new { status };
            Console.WriteLine(JsonConvert.SerializeObject(msg));
        }

        private static void ReportError(string error)
        {
            var msg = new { error };
            Console.WriteLine(JsonConvert.SerializeObject(msg));
        }

        private static void ReportProgress(double progress, string status = null)
        {
            var msg = new { progress, status };
            Console.WriteLine(JsonConvert.SerializeObject(msg));
        }
    }
}
