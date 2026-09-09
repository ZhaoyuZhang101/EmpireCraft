using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Diagnostics;

public enum EmpireCraftUpdateStatus
{
    Idle,
    Checking,
    UpToDate,
    UpdateAvailable,
    Downloading,
    InstallScheduled,
    ManualInstallRequired,
    Failed
}

public sealed class EmpireCraftUpdateSnapshot
{
    public long Revision;
    public EmpireCraftUpdateStatus Status;
    public string CurrentVersion;
    public string AvailableVersion;
    public string ReleaseNotes;
    public string LocalPackagePath;
    public int ProgressPercent;
    public string Error;
}

public static class EmpireCraftUpdateService
{
    private static readonly string[] ManifestEndpoints =
    {
        "https://update.cmhgamedev.dpdns.org/releases/latest.json",
        "https://empirecraft-update.zhangzhaoyu101.workers.dev/releases/latest.json"
    };

    private static readonly HashSet<string> TrustedDownloadHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "update.cmhgamedev.dpdns.org",
            "empirecraft-update.zhangzhaoyu101.workers.dev"
        };

    private const int NetworkTimeoutMilliseconds = 20000;
    private const long MaximumPackageBytes = 128L * 1024L * 1024L;
    private static readonly object Sync = new();
    private static bool _initialized;
    private static bool _busy;
    private static UpdateManifest _manifest;
    private static EmpireCraftUpdateSnapshot _snapshot = new()
    {
        Status = EmpireCraftUpdateStatus.Idle,
        CurrentVersion = "unknown"
    };

    private sealed class UpdateManifest
    {
        public string Version;
        public string DownloadUrl;
        public string Sha256;
        public long Size;
        public string ReleaseNotes;
    }

    public static void Initialize()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        lock (Sync)
        {
            if (_initialized) return;
            _initialized = true;
            _snapshot.CurrentVersion = ReadInstalledVersion();
        }

        CheckForUpdates();
    }

    public static EmpireCraftUpdateSnapshot GetSnapshot()
    {
        lock (Sync)
        {
            return new EmpireCraftUpdateSnapshot
            {
                Revision = _snapshot.Revision,
                Status = _snapshot.Status,
                CurrentVersion = _snapshot.CurrentVersion,
                AvailableVersion = _snapshot.AvailableVersion,
                ReleaseNotes = _snapshot.ReleaseNotes,
                LocalPackagePath = _snapshot.LocalPackagePath,
                ProgressPercent = _snapshot.ProgressPercent,
                Error = _snapshot.Error
            };
        }
    }

    public static void CheckForUpdates()
    {
        if (!TryBegin(EmpireCraftUpdateStatus.Checking)) return;
        ThreadPool.QueueUserWorkItem(_ => CheckWorker());
    }

    public static void DownloadAndInstallOnExit()
    {
        UpdateManifest manifest;
        lock (Sync)
        {
            if (_busy || _manifest == null ||
                _snapshot.Status != EmpireCraftUpdateStatus.UpdateAvailable)
                return;

            _busy = true;
            manifest = _manifest;
            PublishLocked(EmpireCraftUpdateStatus.Downloading, progress: 0);
        }

        ThreadPool.QueueUserWorkItem(_ => DownloadWorker(manifest));
    }

    internal static int CompareVersions(string left, string right)
    {
        List<string> leftParts = TokenizeVersion(left);
        List<string> rightParts = TokenizeVersion(right);
        int count = Math.Max(leftParts.Count, rightParts.Count);

        for (int i = 0; i < count; i++)
        {
            string l = i < leftParts.Count ? leftParts[i] : "0";
            string r = i < rightParts.Count ? rightParts[i] : "0";
            bool lNumber = long.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out long ln);
            bool rNumber = long.TryParse(r, NumberStyles.None, CultureInfo.InvariantCulture, out long rn);

            int comparison;
            if (lNumber && rNumber)
                comparison = ln.CompareTo(rn);
            else if (lNumber != rNumber)
                comparison = lNumber ? 1 : -1;
            else
                comparison = CompareVersionLabels(l, r);

            if (comparison != 0) return comparison;
        }

        return 0;
    }

    private static void CheckWorker()
    {
        var failures = new List<string>();

        foreach (string endpoint in ManifestEndpoints)
        {
            try
            {
                UpdateManifest manifest = ParseManifest(DownloadText(endpoint));
                string currentVersion = ReadInstalledVersion();
                bool available = CompareVersions(manifest.Version, currentVersion) > 0;

                lock (Sync)
                {
                    _manifest = available ? manifest : null;
                    _busy = false;
                    _snapshot.CurrentVersion = currentVersion;
                    _snapshot.AvailableVersion = manifest.Version;
                    _snapshot.ReleaseNotes = manifest.ReleaseNotes;
                    _snapshot.Error = null;
                    PublishLocked(available
                        ? EmpireCraftUpdateStatus.UpdateAvailable
                        : EmpireCraftUpdateStatus.UpToDate);
                }

                if (available)
                    LogService.LogInfo("EmpireCraft update available: " + manifest.Version);
                return;
            }
            catch (Exception error)
            {
                failures.Add(endpoint + " -> " + error.Message);
            }
        }

        Fail(string.Join("\n", failures));
    }

    private static void DownloadWorker(UpdateManifest manifest)
    {
        try
        {
            Uri downloadUri = ValidateDownloadUri(manifest.DownloadUrl);
            string updateRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EmpireCraft",
                "Updates",
                SanitizeFileName(manifest.Version));
            Directory.CreateDirectory(updateRoot);

            string packagePath = Path.Combine(updateRoot, "EmpireCraft.zip");
            DownloadPackage(downloadUri, packagePath, manifest.Size);
            VerifySha256(packagePath, manifest.Sha256);

            string stagePath = Path.Combine(updateRoot, "staged");
            RecreateDirectory(stagePath);
            ExtractArchiveSafely(packagePath, stagePath);

            string packageRoot = ResolvePackageRoot(stagePath);
            ValidateStagedPackage(packageRoot, manifest.Version);

            string modFolder = Path.GetFullPath(ModClass._declare.FolderPath);
            bool developerCheckout = Directory.Exists(Path.Combine(modFolder, ".git"));

            if (developerCheckout || !IsWindows())
            {
                Complete(
                    EmpireCraftUpdateStatus.ManualInstallRequired,
                    packagePath,
                    developerCheckout
                        ? "Developer checkout detected; automatic replacement is disabled."
                        : "Automatic installation is currently available on Windows only.");
                return;
            }

            ScheduleInstallAfterExit(packageRoot, modFolder, updateRoot);
            Complete(EmpireCraftUpdateStatus.InstallScheduled, packagePath, null);
        }
        catch (Exception error)
        {
            Fail(error.Message);
        }
    }

    private static bool TryBegin(EmpireCraftUpdateStatus status)
    {
        lock (Sync)
        {
            if (_busy) return false;
            _busy = true;
            _snapshot.Error = null;
            _snapshot.ProgressPercent = 0;
            PublishLocked(status);
            return true;
        }
    }

    private static void Complete(EmpireCraftUpdateStatus status, string packagePath, string detail)
    {
        lock (Sync)
        {
            _busy = false;
            _snapshot.LocalPackagePath = packagePath;
            _snapshot.Error = detail;
            _snapshot.ProgressPercent = 100;
            PublishLocked(status);
        }
    }

    private static void Fail(string error)
    {
        lock (Sync)
        {
            _busy = false;
            _snapshot.Error = error;
            PublishLocked(EmpireCraftUpdateStatus.Failed);
        }
        LogService.LogWarning("EmpireCraft update failed: " + error);
    }

    private static void PublishLocked(EmpireCraftUpdateStatus status, int? progress = null)
    {
        _snapshot.Status = status;
        if (progress.HasValue) _snapshot.ProgressPercent = progress.Value;
        _snapshot.Revision++;
    }

    private static string DownloadText(string url)
    {
        Uri uri = new(url);
        if (uri.Scheme != Uri.UriSchemeHttps) throw new Exception("Update manifest must use HTTPS.");

        HttpWebRequest request = CreateRequest(uri);
        using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception("HTTP " + (int)response.StatusCode);

        using Stream stream = response.GetResponseStream();
        using var memory = new MemoryStream();
        CopyLimited(stream, memory, 1024L * 1024L);
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static UpdateManifest ParseManifest(string json)
    {
        JObject root = JObject.Parse(json);
        var manifest = new UpdateManifest
        {
            Version = root.Value<string>("version")?.Trim(),
            DownloadUrl = root.Value<string>("downloadUrl")?.Trim(),
            Sha256 = root.Value<string>("sha256")?.Trim(),
            Size = root.Value<long?>("size") ?? 0L,
            ReleaseNotes = root.Value<string>("releaseNotes")?.Trim()
        };

        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.DownloadUrl) ||
            string.IsNullOrWhiteSpace(manifest.Sha256) ||
            manifest.Sha256.Length != 64 ||
            manifest.Size <= 0 || manifest.Size > MaximumPackageBytes)
            throw new Exception("Update manifest is incomplete or invalid.");

        ValidateDownloadUri(manifest.DownloadUrl);
        return manifest;
    }

    private static Uri ValidateDownloadUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !TrustedDownloadHosts.Contains(uri.Host))
            throw new Exception("Update download host is not trusted.");
        return uri;
    }

    private static HttpWebRequest CreateRequest(Uri uri)
    {
#pragma warning disable SYSLIB0014
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
#pragma warning restore SYSLIB0014
        request.Method = "GET";
        request.UserAgent = "EmpireCraft-Updater/1.0";
        request.Timeout = NetworkTimeoutMilliseconds;
        request.ReadWriteTimeout = NetworkTimeoutMilliseconds;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        request.KeepAlive = false;
        return request;
    }

    private static void DownloadPackage(Uri uri, string path, long expectedSize)
    {
        HttpWebRequest request = CreateRequest(uri);
        using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception("Package download returned HTTP " + (int)response.StatusCode + ".");
        if (response.ContentLength > MaximumPackageBytes)
            throw new Exception("Update package exceeds the safety size limit.");

        using Stream input = response.GetResponseStream();
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaximumPackageBytes)
                throw new Exception("Update package exceeds the safety size limit.");
            output.Write(buffer, 0, read);

            long denominator = expectedSize > 0 ? expectedSize : response.ContentLength;
            int progress = denominator > 0
                ? Math.Min(99, (int)(total * 100L / denominator))
                : 0;
            lock (Sync) PublishLocked(EmpireCraftUpdateStatus.Downloading, progress);
        }

        if (expectedSize > 0 && total != expectedSize)
            throw new Exception("Downloaded package size does not match the manifest.");
    }

    private static void VerifySha256(string path, string expectedHash)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Update package SHA-256 verification failed.");
    }

    private static void ExtractArchiveSafely(string archivePath, string destination)
    {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string outputPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!outputPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Update package contains an unsafe path.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(outputPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            using Stream input = entry.Open();
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static string ResolvePackageRoot(string stagePath)
    {
        string nested = Path.Combine(stagePath, "EmpireCraft");
        if (File.Exists(Path.Combine(nested, "mod.json"))) return nested;
        if (File.Exists(Path.Combine(stagePath, "mod.json"))) return stagePath;
        throw new Exception("Update package does not contain EmpireCraft/mod.json.");
    }

    private static void ValidateStagedPackage(string packageRoot, string expectedVersion)
    {
        JObject mod = JObject.Parse(File.ReadAllText(Path.Combine(packageRoot, "mod.json")));
        string stagedVersion = mod.Value<string>("version")?.Trim();
        if (!string.Equals(stagedVersion, expectedVersion, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Update package version does not match its manifest.");
        if (!Directory.Exists(Path.Combine(packageRoot, "Scripts")))
            throw new Exception("Update package is missing the Scripts directory.");
    }

    private static void ScheduleInstallAfterExit(string source, string target, string updateRoot)
    {
        string scriptPath = Path.Combine(updateRoot, "install-after-exit.ps1");
        string logPath = Path.Combine(updateRoot, "install.log");
        int processId = Process.GetCurrentProcess().Id;
        string script = BuildInstallScript(processId, source, target, logPath);
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + QuoteArgument(scriptPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process.Start(start);
    }

    private static string BuildInstallScript(int processId, string source, string target, string logPath)
    {
        string s = EscapePowerShellLiteral(source);
        string t = EscapePowerShellLiteral(target);
        string l = EscapePowerShellLiteral(logPath);
        return "$ErrorActionPreference = 'Stop'\r\n" +
               "$source = '" + s + "'\r\n" +
               "$target = '" + t + "'\r\n" +
               "$log = '" + l + "'\r\n" +
               "try {\r\n" +
               "  Wait-Process -Id " + processId + " -ErrorAction SilentlyContinue\r\n" +
               "  Start-Sleep -Seconds 2\r\n" +
               "  if (-not (Test-Path -LiteralPath (Join-Path $source 'mod.json'))) { throw 'Invalid staged update.' }\r\n" +
               "  Get-ChildItem -LiteralPath $source -Directory | ForEach-Object {\r\n" +
               "    $destination = Join-Path $target $_.Name\r\n" +
               "    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }\r\n" +
               "    Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force\r\n" +
               "  }\r\n" +
               "  Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -ne 'default_config.json' } | ForEach-Object {\r\n" +
               "    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target $_.Name) -Force\r\n" +
               "  }\r\n" +
               "  if (-not (Test-Path -LiteralPath (Join-Path $target 'default_config.json'))) { Copy-Item -LiteralPath (Join-Path $source 'default_config.json') -Destination $target -Force }\r\n" +
               "  ('Installed at ' + (Get-Date -Format o)) | Set-Content -LiteralPath $log -Encoding UTF8\r\n" +
               "} catch { $_.Exception.ToString() | Set-Content -LiteralPath $log -Encoding UTF8 }\r\n";
    }

    private static string ReadInstalledVersion()
    {
        try
        {
            string path = Path.Combine(ModClass._declare?.FolderPath ?? "", "mod.json");
            if (File.Exists(path))
                return JObject.Parse(File.ReadAllText(path)).Value<string>("version")?.Trim() ?? "unknown";
        }
        catch
        {
            // A malformed local manifest is surfaced as an unknown version.
        }
        return "unknown";
    }

    private static void CopyLimited(Stream input, Stream output, long maximumBytes)
    {
        var buffer = new byte[16 * 1024];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maximumBytes) throw new Exception("Response exceeds the safety size limit.");
            output.Write(buffer, 0, read);
        }
    }

    private static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
    }

    private static List<string> TokenizeVersion(string version)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(version)) return parts;
        var token = new StringBuilder();
        bool? numeric = null;
        foreach (char c in version.Trim().ToLowerInvariant())
        {
            if (!char.IsLetterOrDigit(c))
            {
                FlushToken(parts, token);
                numeric = null;
                continue;
            }
            bool isNumeric = char.IsDigit(c);
            if (numeric.HasValue && numeric.Value != isNumeric) FlushToken(parts, token);
            token.Append(c);
            numeric = isNumeric;
        }
        FlushToken(parts, token);
        return parts;
    }

    private static void FlushToken(List<string> parts, StringBuilder token)
    {
        if (token.Length == 0) return;
        parts.Add(token.ToString());
        token.Clear();
    }

    private static int CompareVersionLabels(string left, string right)
    {
        int leftRank = VersionLabelRank(left);
        int rightRank = VersionLabelRank(right);
        return leftRank != rightRank
            ? leftRank.CompareTo(rightRank)
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static int VersionLabelRank(string label)
    {
        return label switch
        {
            "dev" => 0,
            "alpha" or "a" => 1,
            "beta" or "b" => 2,
            "preview" or "pre" => 3,
            "rc" => 4,
            "stable" or "release" => 5,
            _ => 2
        };
    }

    private static bool IsWindows()
    {
        PlatformID platform = Environment.OSVersion.Platform;
        return platform == PlatformID.Win32NT || platform == PlatformID.Win32Windows;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value;
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''");

    private static string QuoteArgument(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
