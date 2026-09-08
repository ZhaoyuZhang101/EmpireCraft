using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Diagnostics;

public enum BugReportSendStatus
{
    Sent,
    DraftOpened,
    Cancelled,
    PlayerLogMissing,
    Failed
}

public sealed class BugReportSendResult
{
    public BugReportSendStatus Status;
    public string ReportDirectory;
    public string Error;
}

public static class BugReportService
{
    public const string Recipient = "zhangzhaoyu101@gmail.com";

    // Primary first, workers.dev fallback second.
    // If the primary endpoint fails, the same report is automatically
    // retried against the fallback endpoint.
    private static readonly string[] BugReportEndpoints =
    {
        "https://bug.cmhgamedev.dpdns.org/",
        "https://empirecraft-bug-report.zhangzhaoyu101.workers.dev/"
    };

    private const int UploadTimeoutMilliseconds = 20000;
    private const long MaximumCombinedUploadBytes = 20L * 1024L * 1024L;

    private sealed class InMemoryReportFile
    {
        public string Name;
        public string ContentType;
        public byte[] Data;
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

    public static BugReportSendResult Send()
    {
        // Remove report folders left behind by older versions of the service.
        // The new implementation does not create local bug-report copies.
        CleanupLegacyReportFiles();

        string playerLog = FindPlayerLog();

        if (!File.Exists(playerLog))
        {
            return new BugReportSendResult
            {
                Status = BugReportSendStatus.PlayerLogMissing
            };
        }

        try
        {
            List<InMemoryReportFile> files =
                BuildReportInMemory(playerLog);

            UploadWithFallback(files);

            return new BugReportSendResult
            {
                Status = BugReportSendStatus.Sent,
                ReportDirectory = null
            };
        }
        catch (Exception error)
        {
            return new BugReportSendResult
            {
                Status = BugReportSendStatus.Failed,
                ReportDirectory = null,
                Error = error.Message
            };
        }
    }

    public static bool OpenPlayerLogFolder()
    {
        string path = FindPlayerLog();

        if (!File.Exists(path))
            return false;

        RevealFile(path);
        return true;
    }

    private static List<InMemoryReportFile> BuildReportInMemory(
        string playerLog
    )
    {
        var files = new List<InMemoryReportFile>();

        byte[] playerLogBytes = ReadOpenFile(playerLog);

        files.Add(
            new InMemoryReportFile
            {
                Name = "Player.log",
                ContentType = "text/plain",
                Data = playerLogBytes
            }
        );

        byte[] summaryBytes =
            new UTF8Encoding(false).GetBytes(BuildSummary());

        files.Add(
            new InMemoryReportFile
            {
                Name = "EmpireCraft-report.txt",
                ContentType = "text/plain; charset=utf-8",
                Data = summaryBytes
            }
        );

        string debugLog = Path.Combine(
            ModClass._declare?.FolderPath ?? "",
            "EmpireCraft.debug.log"
        );

        if (File.Exists(debugLog))
        {
            files.Add(
                new InMemoryReportFile
                {
                    Name = "EmpireCraft.debug.log",
                    ContentType = "text/plain",
                    Data = ReadOpenFile(debugLog)
                }
            );
        }

        long totalBytes = 0;

        foreach (InMemoryReportFile file in files)
        {
            if (file.Data != null)
                totalBytes += file.Data.LongLength;
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

    private static byte[] ReadOpenFile(string path)
    {
        using (var input = new FileStream(
                   path,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        using (var memory = new MemoryStream())
        {
            input.CopyTo(memory);
            return memory.ToArray();
        }
    }

    private static string BuildSummary()
    {
        var text = new StringBuilder();

        text.AppendLine("EmpireCraft automatic bug report");
        text.AppendLine($"Created: {DateTime.Now:O}");
        text.AppendLine($"EmpireCraft: {GetModVersion()}");
        text.AppendLine($"WorldBox: {Application.version}");
        text.AppendLine($"Operating system: {SystemInfo.operatingSystem}");
        text.AppendLine(
            $"CPU: {SystemInfo.processorType} " +
            $"({SystemInfo.processorCount} threads)"
        );
        text.AppendLine(
            $"Memory: {SystemInfo.systemMemorySize} MB"
        );
        text.AppendLine(
            $"GPU: {SystemInfo.graphicsDeviceName}"
        );
        text.AppendLine();
        text.AppendLine(
            "Player.log is included automatically."
        );

        return text.ToString();
    }

    private static string GetModVersion()
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
                    .Value<string>("version")
                    ?? "unknown";
            }
        }
        catch
        {
            // A malformed manifest should not prevent collecting a report.
        }

        return "unknown";
    }

    private static void UploadWithFallback(
        IReadOnlyList<InMemoryReportFile> files
    )
    {
        string boundary =
            "----------------EmpireCraft" +
            DateTime.UtcNow.Ticks.ToString(
                "x",
                CultureInfo.InvariantCulture
            );

        byte[] requestBody =
            BuildMultipartBody(files, boundary);

        var failures = new List<string>();

        foreach (string endpoint in BugReportEndpoints)
        {
            try
            {
                UploadToEndpoint(
                    endpoint,
                    boundary,
                    requestBody
                );

                return;
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

    private static byte[] BuildMultipartBody(
        IReadOnlyList<InMemoryReportFile> files,
        string boundary
    )
    {
        using (var body = new MemoryStream())
        {
            WriteFormField(
                body,
                boundary,
                "empirecraftVersion",
                GetModVersion()
            );

            WriteFormField(
                body,
                boundary,
                "worldboxVersion",
                Application.version
            );

            foreach (InMemoryReportFile file in files)
            {
                WriteFilePart(
                    body,
                    boundary,
                    "files",
                    file
                );
            }

            WriteUtf8(
                body,
                "--" + boundary + "--\r\n"
            );

            return body.ToArray();
        }
    }

    private static void UploadToEndpoint(
        string endpoint,
        string boundary,
        byte[] requestBody
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
        request.ContentLength = requestBody.LongLength;

        request.Headers["X-EmpireCraft-Version"] =
            GetModVersion();

        request.Headers["X-WorldBox-Version"] =
            Application.version;

        request.Headers["X-EmpireCraft-Report-Time"] =
            DateTime.UtcNow.ToString("O");

        try
        {
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(
                    requestBody,
                    0,
                    requestBody.Length
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

        WriteUtf8(stream, builder.ToString());
    }

    private static void WriteFilePart(
        Stream stream,
        string boundary,
        string fieldName,
        InMemoryReportFile file
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

        WriteUtf8(stream, header.ToString());

        if (file.Data != null && file.Data.Length > 0)
        {
            stream.Write(
                file.Data,
                0,
                file.Data.Length
            );
        }

        WriteUtf8(stream, "\r\n");
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

    private static void CleanupLegacyReportFiles()
    {
        try
        {
            string legacyRoot = Path.Combine(
                Application.persistentDataPath,
                "EmpireCraftBugReports"
            );

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
