using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
// using LibUsbDotNet; - Removed
// using LibUsbDotNet.Main; - Removed 
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RetroArr.Core.Switch
{
    public interface ISwitchUsbService
    {
        List<string> ScanDevices();
        Task InstallGameAsync(string filePath, string deviceId, IProgress<double> progress, CancellationToken ct);
        double CurrentProgress { get; }
        string CurrentStatus { get; }
        void CancelCurrentInstallation();
    }

    public class SwitchUsbService : ISwitchUsbService
    {
        private static readonly NLog.Logger _nlog = NLog.LogManager.GetLogger(Logging.AppLoggerService.Switch);
        private readonly ILogger<SwitchUsbService> _logger;
        private const int SWITCH_VENDOR_ID = 0x057E;
        private static readonly object _lock = new object();
        private bool _isBusy = false;
        private Process _installProcess;

        public double CurrentProgress { get; private set; } = 0;
        public string CurrentStatus { get; private set; } = "Idle";
        private CancellationTokenSource _cts;
        
        public SwitchUsbService(ILogger<SwitchUsbService> logger)
        {
            _logger = logger;
        }

        public List<string> ScanDevices()
        {
            var devices = new List<string>();
            try
            {
                string scriptPath = GetHelperPath();
                _logger.LogInformation($"[SwitchUsb] Using Python Script: {scriptPath}");

                // Diagnostic: Check Python Environment
                RunPythonDiagnostics();

                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/python3", // Force system python if available, fallback to PATH otherwise
                    Arguments = $"\"{scriptPath}\" --list",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (!File.Exists(psi.FileName))
                {
                     psi.FileName = "python3"; // Fallback to PATH
                }
                
                _logger.LogInformation($"[SwitchUsb] Executing: {psi.FileName} {psi.Arguments}");

                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                         _logger.LogWarning($"[SwitchUsb] Python Stderr: {error}");
                    }
                    else 
                    {
                         _logger.LogInformation($"[SwitchUsb] Python Stderr was empty.");
                    }

                    _logger.LogInformation($"[SwitchUsb] Python Stdout: {output}");

                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        try 
                        {
                            var json = JObject.Parse(output);
                            if (json["devices"] != null)
                            {
                                return json["devices"].ToObject<List<string>>();
                            }
                        }
                        catch (JsonException) 
                        { 
                             _logger.LogWarning($"[SwitchUsb] Invalid JSON: {output}");
                        }
                    }
                }
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning USB devices via python helper");
            }
            return devices;
        }

        private void RunPythonDiagnostics()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = "-c \"import sys; print('Python Executable: ' + sys.executable); print('Path: ' + str(sys.path))\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    _logger.LogInformation($"[SwitchUsb] Diagnostics Output: {output}");
                    if (!string.IsNullOrEmpty(error)) _logger.LogWarning($"[SwitchUsb] Diagnostics Error: {error}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SwitchUsb] Failed to run diagnostics");
            }
        }

        public void CancelCurrentInstallation()
        {
            lock (_lock)
            {
                if (_isBusy && _cts != null)
                {
                    _nlog.Info("[SwitchUsb] Cancellation requested by user.");
                    _cts.Cancel();
                    
                    // Kill the process if it's running
                    if (_installProcess != null && !_installProcess.HasExited)
                    {
                        try 
                        { 
                            _installProcess.Kill(); 
                            _nlog.Info("[SwitchUsb] Helper process killed.");
                        } 
                        catch (Exception ex) 
                        {
                            _nlog.Error($"[SwitchUsb] Failed to kill process: {ex.Message}");
                        }
                    }
                }
            }
        }

        public async Task InstallGameAsync(string filePath, string deviceId, IProgress<double> progress, CancellationToken ct)
        {
            lock (_lock)
            {
                if (_isBusy) throw new Exception("An installation is already in progress.");
                _isBusy = true;
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            }

            var internalCt = _cts.Token;
            CurrentProgress = 0;
            CurrentStatus = "Starting Helper Process...";

            try
            {
                if (!File.Exists(filePath)) throw new FileNotFoundException("NSP file not found", filePath);

                string scriptPath = GetHelperPath();
                if (!File.Exists(scriptPath)) throw new FileNotFoundException($"Python script not found at {scriptPath}");

                _logger.LogInformation($"[SwitchUsb] Launching Python Helper: {scriptPath} --install \"{filePath}\"");

                var psi = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"\"{scriptPath}\" --install \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _installProcess = new Process
                {
                    StartInfo = psi,
                    EnableRaisingEvents = true
                };

                _installProcess.OutputDataReceived += (sender, args) => 
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        try 
                        {
                            var json = JObject.Parse(args.Data);
                            
                            if (json["status"] != null)
                            {
                                 CurrentStatus = json["status"].ToString();
                            }

                            if (json["progress"] != null)
                            {
                                double p = json["progress"].ToObject<double>();
                                CurrentProgress = p;
                                progress.Report(p);
                            }
                            
                             if (json["error"] != null)
                            {
                                 _logger.LogError($"[Helper] Error: {json["error"]}");
                                 CurrentStatus = $"Error: {json["error"]}";
                            }
                        }
                        catch 
                        {
                             // Raw output
                        }
                    }
                };
                
                _installProcess.ErrorDataReceived += (sender, args) =>
                {
                     if (!string.IsNullOrEmpty(args.Data))
                    {
                        _logger.LogError($"[Helper Error] {args.Data}");
                    }
                };

                _installProcess.Start();
                _installProcess.BeginOutputReadLine();
                _installProcess.BeginErrorReadLine();

                await _installProcess.WaitForExitAsync(internalCt);

                if (_installProcess.ExitCode != 0)
                {
                    throw new Exception($"Helper process exited with code {_installProcess.ExitCode}. See logs.");
                }
                CurrentStatus = "Installation Complete";
            }
            catch (OperationCanceledException)
            {
                CurrentStatus = "Cancelled";
                throw;
            }
            catch (Exception ex)
            {
                CurrentStatus = $"Error: {ex.Message}";
                _logger.LogError(ex, "Installation failed");
                throw;
            }
            finally
            {
                _installProcess = null;
                lock (_lock)
                {
                    _isBusy = false;
                }
            }
        }

        private string GetHelperPath()
        {
            // Python script location
            string scriptName = "SwitchUsbHelper.py";
            
            // 1. Check in same directory as DLL (published/dev)
            string assemblyDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string path = Path.Combine(assemblyDir, scriptName);
            if (File.Exists(path)) return path;
            
            // 2. Check source location (dev fallback)
            path = Path.Combine(Directory.GetCurrentDirectory(), "src/RetroArr.Core/Switch", scriptName);
             if (File.Exists(path)) return path;

            // 3. MacOS Bundle output location
            path = Path.Combine(Directory.GetCurrentDirectory(), "_output/UI", scriptName); // Not likely
             if (File.Exists(path)) return path;

            return Path.Combine(assemblyDir, scriptName); // Default
        }
    }
}
