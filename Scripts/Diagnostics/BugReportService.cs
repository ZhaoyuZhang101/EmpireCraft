using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Threading;
using EmpireCraft.Scripts.Data;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Diagnostics;

public enum BugReportSendStatus
{
    Idle,
    Sending,
    Sent,
    DraftOpened,
    Cancelled,
    PlayerLogMissing,
    SaveDataMissing,
    PayloadTooLarge,
    Failed
}

public sealed class BugReportSendResult
{
    public BugReportSendStatus Status;
    public string ReportDirectory;
    public string Error;
    public string ReportId;
    public bool SaveDataIncluded;
    public bool EmailSent;
    public int SavedFileCount;
    public long Revision;
}

public static class BugReportService
{
    // Primary first, workers.dev fallback second.
    // If the primary endpoint fails, the same report is automatically
    // retried against the fallback endpoint.
    private static readonly string[] BugReportEndpoints =
    {
        "https://bug.cmhgamedev.dpdns.org/",
        "https://empirecraft-bug-report.zhangzhaoyu101.workers.dev/"
    };

    private const int UploadTimeoutMilliseconds = 120000;
    private const long MaximumCombinedUploadBytes = 90L * 1024L * 1024L;
    private const long SummarySizeReserveBytes = 256L * 1024L;
    private static readonly object SendSync = new();
    private static bool _sendBusy;
    private static BugReportSendResult _sendSnapshot = new()
    {
        Status = BugReportSendStatus.Idle
    };

    private sealed class ReportFile
    {
        public string Name;
        public string ContentType;
        public string SourcePath;
        public long Length;
        public byte[] Data;
    }

    private sealed class UploadReceipt
    {
        public string ReportId;
        public int SavedFileCount;
        public bool EmailSent;
    }

    private sealed class ReportEnvironment
    {
        public string PlayerLogPath;
        public string SaveDataPath;
        public string DebugLogPath;
        public string LegacyReportRoot;
        public string ModVersion;
        public string WorldBoxVersion;
        public string OperatingSystem;
        public string ProcessorType;
        public int ProcessorCount;
        public int SystemMemorySize;
        public string GraphicsDeviceName;
    }

    public static string FindPlayerLog()
    {
        try
        {
            var property = typeof(Application).GetProperty("consoleLogPath");
            string unityPath = property?.GetValue(null, null) as string;

            if (!string.IsNullOrWhiteSpace(unityPath) && File.Exists(unityPath))
                return unityPath;
        }
        catch
        {
            // Older Unity versions do not expose consoleLogPath.
        }

        var candidates = new List<string>();

        string home =
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string local =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string localRoot = string.IsNullOrWhiteSpace(local)
            ? null
            : Directory.GetParent(local)?.FullName;

        if (!string.IsNullOrWhiteSpace(localRoot))
        {
            candidates.Add(
                Path.Combine(
                    localRoot,
                    "LocalLow",
                    "mkarpenko",
                    "WorldBox",
                    "Player.log"
                )
            );
        }

        if (!string.IsNullOrWhiteSpace(home))
        {
            candidates.Add(
                Path.Combine(
                    home,
                    "Library",
                    "Logs",
                    "mkarpenko",
                    "WorldBox",
                    "Player.log"
                )
            );

            candidates.Add(
                Path.Combine(
                    home,
                    ".config",
                    "unity3d",
                    "mkarpenko",
                    "WorldBox",
                    "Player.log"
                )
            );
        }

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return candidates.Count > 0
            ? candidates[0]
            : "Player.log";
    }

    public static BugReportSendResult Send(bool includeSaveData = false)
    {
        return Send(CaptureEnvironment(), includeSaveData);
    }

    public static BugReportSendResult BeginSend(bool includeSaveData = false)
    {
        ReportEnvironment environment = CaptureEnvironment();

        lock (SendSync)
        {
            if (_sendBusy)
                return CloneResult(_sendSnapshot);

            BugReportSendResult validation;
            try
            {
                validation = ValidateEnvironment(
                    environment,
                    includeSaveData
                );
            }
            catch (Exception error)
            {
                validation = new BugReportSendResult
                {
                    Status = BugReportSendStatus.Failed,
                    SaveDataIncluded = includeSaveData,
                    Error = error.Message
                };
            }
            if (validation != null)
            {
                PublishLocked(validation);
                return CloneResult(_sendSnapshot);
            }

            _sendBusy = true;
            PublishLocked(new BugReportSendResult
            {
                Status = BugReportSendStatus.Sending,
                SaveDataIncluded = includeSaveData
            });
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            BugReportSendResult result;
            try
            {
                result = Send(environment, includeSaveData);
            }
            catch (Exception error)
            {
                result = new BugReportSendResult
                {
                    Status = BugReportSendStatus.Failed,
                    SaveDataIncluded = includeSaveData,
                    Error = error.Message
                };
            }

            lock (SendSync)
            {
                _sendBusy = false;
                PublishLocked(result);
            }
        });

        return GetSendSnapshot();
    }

    public static BugReportSendResult GetSendSnapshot()
    {
        lock (SendSync)
        {
            return CloneResult(_sendSnapshot);
        }
    }

    private static BugReportSendResult Send(
        ReportEnvironment environment,
        bool includeSaveData
    )
    {
        try
        {
            CleanupLegacyReportFiles(environment.LegacyReportRoot);

            BugReportSendResult validation = ValidateEnvironment(
                environment,
                includeSaveData
            );
            if (validation != null)
                return validation;

            List<ReportFile> files = BuildReportFiles(
                environment,
                includeSaveData,
                out bool saveDataIncluded
            );

            UploadReceipt receipt = UploadWithFallback(
                files,
                environment.ModVersion,
                environment.WorldBoxVersion
            );

            return new BugReportSendResult
            {
                Status = BugReportSendStatus.Sent,
                ReportDirectory = null,
                ReportId = receipt.ReportId,
                SaveDataIncluded = saveDataIncluded,
                EmailSent = receipt.EmailSent,
                SavedFileCount = receipt.SavedFileCount
            };
        }
        catch (Exception error)
        {
            return new BugReportSendResult
            {
                Status = BugReportSendStatus.Failed,
                ReportDirectory = null,
                SaveDataIncluded = includeSaveData,
                Error = error.Message
            };
        }
    }

    public static string FindEmpireCraftSaveData()
    {
        string path = DataManager.CurrentSaveDataPath;
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    public static string GetModAuthor()
    {
        return GetManifestValue("author", "EmpireCraft");
    }

    private static ReportEnvironment CaptureEnvironment()
    {
        return new ReportEnvironment
        {
            PlayerLogPath = FindPlayerLog(),
            SaveDataPath = FindEmpireCraftSaveData(),
            DebugLogPath = Path.Combine(
                ModClass._declare?.FolderPath ?? "",
                "EmpireCraft.debug.log"
            ),
            LegacyReportRoot = Path.Combine(
                Application.persistentDataPath,
                "EmpireCraftBugReports"
            ),
            ModVersion = GetModVersion(),
            WorldBoxVersion = Application.version,
            OperatingSystem = SystemInfo.operatingSystem,
            ProcessorType = SystemInfo.processorType,
            ProcessorCount = SystemInfo.processorCount,
            SystemMemorySize = SystemInfo.systemMemorySize,
            GraphicsDeviceName = SystemInfo.graphicsDeviceName
        };
    }

    private static BugReportSendResult ValidateEnvironment(
        ReportEnvironment environment,
        bool includeSaveData
    )
    {
        if (!File.Exists(environment.PlayerLogPath))
        {
            return new BugReportSendResult
            {
                Status = BugReportSendStatus.PlayerLogMissing
            };
        }

        if (includeSaveData &&
            (string.IsNullOrWhiteSpace(environment.SaveDataPath) ||
             !File.Exists(environment.SaveDataPath)))
        {
            return new BugReportSendResult
            {
                Status = BugReportSendStatus.SaveDataMissing
            };
        }

        long payloadBytes = new FileInfo(environment.PlayerLogPath).Length;
        if (File.Exists(environment.DebugLogPath))
            payloadBytes += new FileInfo(environment.DebugLogPath).Length;
        if (includeSaveData)
            payloadBytes += new FileInfo(environment.SaveDataPath).Length;

        if (payloadBytes + SummarySizeReserveBytes > MaximumCombinedUploadBytes)
        {
            return new BugReportSendResult
            {
                Status = BugReportSendStatus.PayloadTooLarge,
                SaveDataIncluded = includeSaveData,
                Error = string.Format(
                    CultureInfo.InvariantCulture,
                    "Report size is {0:0.0} MB; the upload limit is {1} MB.",
                    payloadBytes / 1024d / 1024d,
                    MaximumCombinedUploadBytes / 1024L / 1024L
                )
            };
        }

        return null;
    }

    private static void PublishLocked(BugReportSendResult result)
    {
        result ??= new BugReportSendResult
        {
            Status = BugReportSendStatus.Failed,
            Error = "Unknown bug report error."
        };
        result.Revision = _sendSnapshot.Revision + 1;
        _sendSnapshot = result;
    }

    private static BugReportSendResult CloneResult(BugReportSendResult source)
    {
        return new BugReportSendResult
        {
            Status = source.Status,
            ReportDirectory = source.ReportDirectory,
            Error = source.Error,
            ReportId = source.ReportId,
            SaveDataIncluded = source.SaveDataIncluded,
            EmailSent = source.EmailSent,
            SavedFileCount = source.SavedFileCount,
            Revision = source.Revision
        };
    }

    public static bool OpenPlayerLogFolder()
    {
        string path = FindPlayerLog();

        if (!File.Exists(path))
            return false;

        RevealFile(path);
        return true;
    }

    public static bool OpenEmpireCraftSaveFolder()
    {
        string path = FindEmpireCraftSaveData();

        if (!File.Exists(path))
            return false;

        RevealFile(path);
        return true;
    }

    private static List<ReportFile> BuildReportFiles(
        ReportEnvironment environment,
        bool includeSaveData,
        out bool saveDataIncluded
    )
    {
        var files = new List<ReportFile>();

        files.Add(
            new ReportFile
            {
                Name = "Player.log",
                ContentType = "text/plain",
                SourcePath = environment.PlayerLogPath,
                Length = new FileInfo(environment.PlayerLogPath).Length
            }
        );

        if (File.Exists(environment.DebugLogPath))
        {
            files.Add(
                new ReportFile
                {
                    Name = "EmpireCraft.debug.log",
                    ContentType = "text/plain",
                    SourcePath = environment.DebugLogPath,
                    Length = new FileInfo(environment.DebugLogPath).Length
                }
            );
        }

        saveDataIncluded = false;
        string saveDataStatus = includeSaveData
            ? "included as " + DataManager.EmpireCraftSaveFileName
            : "not requested by the user";

        if (includeSaveData)
        {
            files.Add(
                new ReportFile
                {
                    Name = DataManager.EmpireCraftSaveFileName,
                    ContentType = "application/json; charset=utf-8",
                    SourcePath = environment.SaveDataPath,
                    Length = new FileInfo(environment.SaveDataPath).Length
                }
            );
            saveDataIncluded = true;
        }

        byte[] summaryBytes = new UTF8Encoding(false).GetBytes(
            BuildSummary(environment, saveDataIncluded, saveDataStatus)
        );

        files.Add(
            new ReportFile
            {
                Name = "EmpireCraft-report.txt",
                ContentType = "text/plain; charset=utf-8",
                Length = summaryBytes.LongLength,
                Data = summaryBytes
            }
        );

        long totalBytes = 0;

        foreach (ReportFile file in files)
        {
            totalBytes += file.Length;
        }

        if (totalBytes <= 0)
            throw new Exception("No bug report data was collected.");

        if (totalBytes > MaximumCombinedUploadBytes)
        {
            throw new Exception(
                "Bug report files are too large to upload."
            );
        }

        return files;
    }

    private static string BuildSummary(
        ReportEnvironment environment,
        bool saveDataIncluded,
        string saveDataStatus
    )
    {
        var text = new StringBuilder();

        text.AppendLine("EmpireCraft automatic bug report");
        text.AppendLine($"Created: {DateTime.Now:O}");
        text.AppendLine($"EmpireCraft: {environment.ModVersion}");
        text.AppendLine($"WorldBox: {environment.WorldBoxVersion}");
        text.AppendLine($"Operating system: {environment.OperatingSystem}");
        text.AppendLine(
            $"CPU: {environment.ProcessorType} " +
            $"({environment.ProcessorCount} threads)"
        );
        text.AppendLine(
            $"Memory: {environment.SystemMemorySize} MB"
        );
        text.AppendLine(
            $"GPU: {environment.GraphicsDeviceName}"
        );
        text.AppendLine();
        text.AppendLine(
            "Player.log is included automatically."
        );
        text.AppendLine(
            "EmpireCraft save data: " +
            (saveDataIncluded
                ? saveDataStatus
                : saveDataStatus + ".")
        );

        return text.ToString();
    }

    private static string GetModVersion()
    {
        return GetManifestValue("version", "unknown");
    }

    private static string GetManifestValue(string key, string fallback)
    {
        try
        {
            string path = Path.Combine(
                ModClass._declare?.FolderPath ?? "",
                "mod.json"
            );

            if (File.Exists(path))
            {
                return JObject
                    .Parse(File.ReadAllText(path))
                    .Value<string>(key)
                    ?? fallback;
            }
        }
        catch
        {
            // A malformed manifest should not prevent collecting a report.
        }

        return fallback;
    }

    private static UploadReceipt UploadWithFallback(
        IReadOnlyList<ReportFile> files,
        string modVersion,
        string worldBoxVersion
    )
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

        string boundary =
            "----------------EmpireCraft" +
            DateTime.UtcNow.Ticks.ToString(
                "x",
                CultureInfo.InvariantCulture
            );

        var failures = new List<string>();

        foreach (string endpoint in BugReportEndpoints)
        {
            try
            {
                UploadReceipt receipt = UploadToEndpoint(
                    endpoint,
                    boundary,
                    files,
                    modVersion,
                    worldBoxVersion
                );

                if (receipt.SavedFileCount < files.Count)
                {
                    throw new Exception(
                        $"Server saved {receipt.SavedFileCount} of {files.Count} report files."
                    );
                }

                return receipt;
            }
            catch (Exception error)
            {
                failures.Add(
                    endpoint + " -> " + error.Message
                );
            }
        }

        throw new Exception(
            "All bug report servers failed.\n" +
            string.Join("\n", failures)
        );
    }

    private static void WriteMultipartBody(
        Stream body,
        IReadOnlyList<ReportFile> files,
        string boundary,
        string modVersion,
        string worldBoxVersion
    )
    {
        WriteFormField(
            body,
            boundary,
            "empirecraftVersion",
            modVersion
        );

        WriteFormField(
            body,
            boundary,
            "worldboxVersion",
            worldBoxVersion
        );

        foreach (ReportFile file in files)
        {
            WriteFilePart(
                body,
                boundary,
                "files",
                file
            );
        }

        WriteUtf8(body, "--" + boundary + "--\r\n");
    }

    private static UploadReceipt UploadToEndpoint(
        string endpoint,
        string boundary,
        IReadOnlyList<ReportFile> files,
        string modVersion,
        string worldBoxVersion
    )
    {
#pragma warning disable SYSLIB0014
        HttpWebRequest request =
            (HttpWebRequest)WebRequest.Create(endpoint);
#pragma warning restore SYSLIB0014

        request.Method = "POST";
        request.ContentType =
            "multipart/form-data; boundary=" + boundary;
        request.Timeout = UploadTimeoutMilliseconds;
        request.ReadWriteTimeout = UploadTimeoutMilliseconds;
        request.KeepAlive = false;
        request.ContentLength = CalculateMultipartLength(
            files,
            boundary,
            modVersion,
            worldBoxVersion
        );
        request.AllowWriteStreamBuffering = false;
        request.Accept = "application/json";
        request.UserAgent = "EmpireCraft-BugReporter/2.0";
        request.AutomaticDecompression =
            DecompressionMethods.GZip | DecompressionMethods.Deflate;
        request.CachePolicy = new RequestCachePolicy(
            RequestCacheLevel.BypassCache
        );

        request.Headers["X-EmpireCraft-Version"] =
            modVersion;

        request.Headers["X-WorldBox-Version"] =
            worldBoxVersion;

        request.Headers["X-EmpireCraft-Report-Time"] =
            DateTime.UtcNow.ToString("O");

        try
        {
            using (Stream requestStream = request.GetRequestStream())
            {
                WriteMultipartBody(
                    requestStream,
                    files,
                    boundary,
                    modVersion,
                    worldBoxVersion
                );
            }

            using (HttpWebResponse response =
                   (HttpWebResponse)request.GetResponse())
            {
                int statusCode = (int)response.StatusCode;

                if (statusCode < 200 || statusCode >= 300)
                {
                    throw new Exception(
                        $"HTTP {statusCode}"
                    );
                }

                string responseText;
                using (Stream stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    responseText = reader.ReadToEnd();
                }

                JObject payload;
                try
                {
                    payload = JObject.Parse(responseText);
                }
                catch (Exception error)
                {
                    throw new Exception(
                        "Bug report server returned an invalid response: " +
                        error.Message
                    );
                }

                if (payload.Value<bool?>("success") != true)
                {
                    throw new Exception(
                        payload.Value<string>("error") ??
                        "Bug report server did not confirm the upload."
                    );
                }

                bool emailSent = payload.Value<bool?>("emailSent") == true;
                if (!emailSent)
                {
                    throw new Exception(
                        "Report was stored, but the author notification failed: " +
                        (payload.Value<string>("emailError") ?? "unknown error")
                    );
                }

                return new UploadReceipt
                {
                    ReportId = payload.Value<string>("reportId"),
                    SavedFileCount = payload.Value<int?>("savedFiles") ?? 0,
                    EmailSent = true
                };
            }
        }
        catch (WebException webError)
        {
            HttpWebResponse response =
                webError.Response as HttpWebResponse;

            if (response != null)
            {
                int statusCode = (int)response.StatusCode;
                string responseText = "";

                try
                {
                    using (Stream stream = response.GetResponseStream())
                    using (var reader = new StreamReader(stream))
                    {
                        responseText = reader.ReadToEnd();
                    }
                }
                catch
                {
                    // Ignore secondary response-reading errors.
                }

                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    throw new Exception(
                        $"HTTP {statusCode} - {responseText}"
                    );
                }

                throw new Exception(
                    $"HTTP {statusCode}"
                );
            }

            throw new Exception(webError.Message);
        }
    }

    private static void WriteFormField(
        Stream stream,
        string boundary,
        string name,
        string value
    )
    {
        WriteUtf8(stream, BuildFormFieldText(boundary, name, value));
    }

    private static string BuildFormFieldText(
        string boundary,
        string name,
        string value
    )
    {
        var builder = new StringBuilder();

        builder.Append("--");
        builder.Append(boundary);
        builder.Append("\r\n");
        builder.Append(
            "Content-Disposition: form-data; name=\""
        );
        builder.Append(name);
        builder.Append("\"\r\n\r\n");
        builder.Append(value ?? "");
        builder.Append("\r\n");

        return builder.ToString();
    }

    private static void WriteFilePart(
        Stream stream,
        string boundary,
        string fieldName,
        ReportFile file
    )
    {
        WriteUtf8(
            stream,
            BuildFilePartHeader(boundary, fieldName, file)
        );

        if (file.Data != null)
        {
            if (file.Data.LongLength != file.Length)
                throw new Exception("Bug report data length changed before upload.");
            stream.Write(file.Data, 0, file.Data.Length);
        }
        else
        {
            CopyFileToRequest(file, stream);
        }

        WriteUtf8(stream, "\r\n");
    }

    private static string BuildFilePartHeader(
        string boundary,
        string fieldName,
        ReportFile file
    )
    {
        var header = new StringBuilder();

        header.Append("--");
        header.Append(boundary);
        header.Append("\r\n");
        header.Append(
            "Content-Disposition: form-data; name=\""
        );
        header.Append(fieldName);
        header.Append("\"; filename=\"");
        header.Append(EscapeHeaderValue(file.Name));
        header.Append("\"\r\n");
        header.Append("Content-Type: ");
        header.Append(
            string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType
        );
        header.Append("\r\n\r\n");

        return header.ToString();
    }

    private static void CopyFileToRequest(
        ReportFile file,
        Stream destination
    )
    {
        using var input = new FileStream(
            file.SourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );

        var buffer = new byte[64 * 1024];
        long remaining = file.Length;
        while (remaining > 0)
        {
            int requested = (int)Math.Min(buffer.Length, remaining);
            int read = input.Read(buffer, 0, requested);
            if (read <= 0)
            {
                throw new Exception(
                    "A bug report file changed while it was being uploaded: " +
                    file.Name
                );
            }

            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static long CalculateMultipartLength(
        IReadOnlyList<ReportFile> files,
        string boundary,
        string modVersion,
        string worldBoxVersion
    )
    {
        long length = Encoding.UTF8.GetByteCount(
            BuildFormFieldText(
                boundary,
                "empirecraftVersion",
                modVersion
            )
        );
        length += Encoding.UTF8.GetByteCount(
            BuildFormFieldText(
                boundary,
                "worldboxVersion",
                worldBoxVersion
            )
        );

        foreach (ReportFile file in files)
        {
            length += Encoding.UTF8.GetByteCount(
                BuildFilePartHeader(boundary, "files", file)
            );
            length += file.Length;
            length += 2;
        }

        length += Encoding.UTF8.GetByteCount(
            "--" + boundary + "--\r\n"
        );
        return length;
    }

    private static string EscapeHeaderValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "file";

        return value
            .Replace("\\", "_")
            .Replace("\"", "_")
            .Replace("\r", "_")
            .Replace("\n", "_");
    }

    private static void WriteUtf8(
        Stream stream,
        string text
    )
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void CleanupLegacyReportFiles(string legacyRoot)
    {
        try
        {
            if (Directory.Exists(legacyRoot))
                Directory.Delete(legacyRoot, true);
        }
        catch
        {
            // Cleanup failure should never block sending a report.
        }
    }

    private static void RevealFile(string path)
    {
        if (
            Environment.OSVersion.Platform ==
            PlatformID.Win32NT
        )
        {
            Process.Start(
                new ProcessStartInfo(
                    "explorer.exe",
                    $"/select,\"{path}\""
                )
                {
                    UseShellExecute = true
                }
            );

            return;
        }

        OpenDirectory(Path.GetDirectoryName(path));
    }

    private static void OpenDirectory(string directory)
    {
        if (
            string.IsNullOrWhiteSpace(directory) ||
            !Directory.Exists(directory)
        )
        {
            return;
        }

        Process.Start(
            new ProcessStartInfo(directory)
            {
                UseShellExecute = true
            }
        );
    }
}
