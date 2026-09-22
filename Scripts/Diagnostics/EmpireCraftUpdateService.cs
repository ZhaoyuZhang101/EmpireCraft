using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Cache;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using EmpireCraft.Scripts.HelperFunc;
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
    InstallPending,
    InstallSucceeded,
    InstallFailed,
    ManualPackageReady,
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
    private const string PendingInstallFileName = "pending-install.json";
    private const string InstallResultFileName = "install-result.json";
    private const string UpdateSettingsFileName = "update-settings.json";
    private static readonly object Sync = new();
    private static bool _initialized;
    private static bool _busy;
    private static bool _manualPackageMode;
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

    private sealed class PendingInstall
    {
        public string Version;
        public string PackagePath;
        public string ScriptPath;
        public string TargetPath;
        public int HelperProcessId;
        public int ResumeAttempts;
    }

    public static void Initialize()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        bool manualPackageMode = ReadManualPackageMode();
        lock (Sync)
        {
            if (_initialized) return;
            _initialized = true;
            _manualPackageMode = manualPackageMode;
            _snapshot.CurrentVersion = ReadInstalledVersion();
        }

        if (!RestoreInstallState()) CheckForUpdates();
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

    public static bool ManualPackageMode
    {
        get
        {
            lock (Sync) return _manualPackageMode;
        }
    }

    public static void SetManualPackageMode(bool enabled)
    {
        lock (Sync)
        {
            if (_manualPackageMode == enabled) return;
            _manualPackageMode = enabled;
            _snapshot.Revision++;
        }

        try
        {
            WriteUpdateSettings(enabled);
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft update preference could not be saved: " + error.Message);
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
        bool manualPackageMode;
        lock (Sync)
        {
            if (_busy || _manifest == null ||
                _snapshot.Status != EmpireCraftUpdateStatus.UpdateAvailable)
                return;

            _busy = true;
            manifest = _manifest;
            manualPackageMode = _manualPackageMode;
            PublishLocked(EmpireCraftUpdateStatus.Downloading, progress: 0);
        }

        ThreadPool.QueueUserWorkItem(_ => DownloadWorker(manifest, manualPackageMode));
    }

    public static bool OpenModsFolder()
    {
        try
        {
            DirectoryInfo modsDirectory = GetModsDirectory();
            if (modsDirectory == null || !modsDirectory.Exists)
                return false;

            Process.Start(new ProcessStartInfo(modsDirectory.FullName)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft could not open the Mods directory: " + error.Message);
            return false;
        }
    }

    internal static int CompareVersions(string left, string right)
    {
        return EmpireCraftVersionRules.Compare(left, right);
    }

    private static void CheckWorker()
    {
        var failures = new List<string>();
        UpdateManifest newestManifest = null;

        foreach (string endpoint in ManifestEndpoints)
        {
            try
            {
                UpdateManifest manifest = ParseManifest(DownloadText(endpoint));
                if (newestManifest == null || CompareVersions(manifest.Version, newestManifest.Version) > 0)
                    newestManifest = manifest;
            }
            catch (Exception error)
            {
                failures.Add(endpoint + " -> " + error.Message);
            }
        }

        if (newestManifest == null)
        {
            Fail(string.Join("\n", failures));
            return;
        }

        string currentVersion = ReadInstalledVersion();
        bool available = CompareVersions(newestManifest.Version, currentVersion) > 0;
        lock (Sync)
        {
            _manifest = available ? newestManifest : null;
            _busy = false;
            _snapshot.CurrentVersion = currentVersion;
            _snapshot.AvailableVersion = newestManifest.Version;
            _snapshot.ReleaseNotes = newestManifest.ReleaseNotes;
            _snapshot.Error = null;
            PublishLocked(available
                ? EmpireCraftUpdateStatus.UpdateAvailable
                : EmpireCraftUpdateStatus.UpToDate);
        }

        if (available)
            LogService.LogInfo("EmpireCraft update available: " + newestManifest.Version);
    }

    private static void DownloadWorker(UpdateManifest manifest, bool manualPackageMode)
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
            if (manualPackageMode)
            {
                string manualPackagePath = CopyPackageToMods(packagePath, modFolder, manifest);
                Complete(EmpireCraftUpdateStatus.ManualPackageReady, manualPackagePath, null);
                return;
            }

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

            ScheduleInstallAfterExit(packageRoot, modFolder, updateRoot, manifest.Version, packagePath);
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
        var builder = new UriBuilder(url);
        string cacheBust = "ec_check=" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? cacheBust
            : builder.Query.TrimStart('?') + "&" + cacheBust;
        Uri uri = builder.Uri;
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
        request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.BypassCache);
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

    private static string CopyPackageToMods(string packagePath, string modFolder, UpdateManifest manifest)
    {
        DirectoryInfo modsDirectory = GetModsDirectory(modFolder);
        if (modsDirectory == null || !modsDirectory.Exists)
            throw new DirectoryNotFoundException("The WorldBox Mods directory could not be located.");

        string fileName = "EmpireCraft_" + SanitizeFileName(manifest.Version) + ".zip";
        string destination = Path.Combine(modsDirectory.FullName, fileName);
        string temporaryDestination = destination + ".download";
        TryDeleteFile(temporaryDestination);
        try
        {
            File.Copy(packagePath, temporaryDestination, true);
            if (manifest.Size > 0 && new FileInfo(temporaryDestination).Length != manifest.Size)
                throw new Exception("The copied update package size does not match the manifest.");
            VerifySha256(temporaryDestination, manifest.Sha256);
            TryDeleteFile(destination);
            File.Move(temporaryDestination, destination);
            return destination;
        }
        catch
        {
            TryDeleteFile(temporaryDestination);
            throw;
        }
    }

    private static DirectoryInfo GetModsDirectory(string modFolder = null)
    {
        string path = modFolder ?? ModClass._declare?.FolderPath;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return new DirectoryInfo(Path.GetFullPath(path)).Parent;
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

    private static void ScheduleInstallAfterExit(string source, string target, string updateRoot,
        string version, string packagePath)
    {
        string scriptPath = Path.Combine(updateRoot, "install-after-exit.ps1");
        string logPath = Path.Combine(updateRoot, "install.log");
        string stateRoot = GetUpdatesRoot();
        string pendingPath = Path.Combine(stateRoot, PendingInstallFileName);
        string resultPath = Path.Combine(stateRoot, InstallResultFileName);
        int processId = Process.GetCurrentProcess().Id;
        string processName = Process.GetCurrentProcess().ProcessName;
        string script = BuildInstallScript(processId, processName, source, target, version,
            packagePath, logPath, pendingPath, resultPath);
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

        var pending = new PendingInstall
        {
            Version = version,
            PackagePath = packagePath,
            ScriptPath = scriptPath,
            TargetPath = target
        };
        TryDeleteFile(resultPath);
        WritePendingInstall(pendingPath, pending);
        try
        {
            pending.HelperProcessId = StartInstallHelper(scriptPath);
            WritePendingInstall(pendingPath, pending);
        }
        catch
        {
            TryDeleteFile(pendingPath);
            throw;
        }
    }

    private static string BuildInstallScript(int processId, string processName, string source, string target,
        string version, string packagePath, string logPath, string pendingPath, string resultPath)
    {
        string n = EscapePowerShellLiteral(processName);
        string s = EscapePowerShellLiteral(source);
        string t = EscapePowerShellLiteral(target);
        string v = EscapePowerShellLiteral(version);
        string p = EscapePowerShellLiteral(packagePath);
        string l = EscapePowerShellLiteral(logPath);
        string q = EscapePowerShellLiteral(pendingPath);
        string r = EscapePowerShellLiteral(resultPath);
        return "$ErrorActionPreference = 'Stop'\r\n" +
               "$processName = '" + n + "'\r\n" +
               "$source = '" + s + "'\r\n" +
               "$target = '" + t + "'\r\n" +
               "$version = '" + v + "'\r\n" +
               "$package = '" + p + "'\r\n" +
               "$log = '" + l + "'\r\n" +
               "$pending = '" + q + "'\r\n" +
               "$result = '" + r + "'\r\n" +
               "$resultTemp = $result + '.tmp'\r\n" +
               "function Write-InstallResult([string]$status, [string]$detail) {\r\n" +
               "  $payload = [ordered]@{ status = $status; version = $version; packagePath = $package; targetPath = $target; detail = $detail; completedAt = (Get-Date).ToUniversalTime().ToString('o') } | ConvertTo-Json -Compress\r\n" +
               "  [System.IO.File]::WriteAllText($resultTemp, $payload, (New-Object System.Text.UTF8Encoding($false)))\r\n" +
               "  Move-Item -LiteralPath $resultTemp -Destination $result -Force\r\n" +
               "  Remove-Item -LiteralPath $pending -Force -ErrorAction SilentlyContinue\r\n" +
               "}\r\n" +
               "try {\r\n" +
               "  Wait-Process -Id " + processId + " -ErrorAction SilentlyContinue\r\n" +
               "  $quietChecks = 0\r\n" +
               "  while ($quietChecks -lt 4) {\r\n" +
               "    if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -eq 0) { $quietChecks++ } else { $quietChecks = 0 }\r\n" +
               "    if ($quietChecks -lt 4) { Start-Sleep -Milliseconds 250 }\r\n" +
               "  }\r\n" +
               "  if (-not (Test-Path -LiteralPath (Join-Path $source 'mod.json'))) { throw 'Invalid staged update.' }\r\n" +
               "  New-Item -ItemType Directory -Path $target -Force | Out-Null\r\n" +
               "  $installed = $false\r\n" +
               "  $lastError = $null\r\n" +
               "  for ($attempt = 1; $attempt -le 6; $attempt++) {\r\n" +
               "    try {\r\n" +
               "      Get-ChildItem -LiteralPath $source -Directory | ForEach-Object {\r\n" +
               "        $destination = Join-Path $target $_.Name\r\n" +
               "        if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }\r\n" +
               "        Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force\r\n" +
               "      }\r\n" +
               "      Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -ne 'default_config.json' } | ForEach-Object {\r\n" +
               "        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target $_.Name) -Force\r\n" +
               "      }\r\n" +
               "      if (-not (Test-Path -LiteralPath (Join-Path $target 'default_config.json'))) { Copy-Item -LiteralPath (Join-Path $source 'default_config.json') -Destination $target -Force }\r\n" +
               "      $installed = $true\r\n" +
               "      break\r\n" +
               "    } catch {\r\n" +
               "      $lastError = $_.Exception\r\n" +
               "      if ($attempt -lt 6) { Start-Sleep -Seconds 1 }\r\n" +
               "    }\r\n" +
               "  }\r\n" +
               "  if (-not $installed) { throw $lastError }\r\n" +
               "  $installedManifest = Get-Content -Raw -LiteralPath (Join-Path $target 'mod.json') | ConvertFrom-Json\r\n" +
               "  if ([string]$installedManifest.version -ne $version) { throw ('Installed version mismatch. Expected ' + $version + ', found ' + [string]$installedManifest.version) }\r\n" +
               "  ('Installed ' + $version + ' at ' + (Get-Date -Format o) + ' into ' + $target) | Set-Content -LiteralPath $log -Encoding UTF8\r\n" +
               "  Write-InstallResult 'success' ''\r\n" +
               "} catch {\r\n" +
               "  $detail = $_.Exception.ToString()\r\n" +
               "  $detail | Set-Content -LiteralPath $log -Encoding UTF8\r\n" +
               "  Write-InstallResult 'failure' $detail\r\n" +
               "}\r\n";
    }

    private static bool RestoreInstallState()
    {
        string stateRoot = GetUpdatesRoot();
        string resultPath = Path.Combine(stateRoot, InstallResultFileName);
        string pendingPath = Path.Combine(stateRoot, PendingInstallFileName);

        if (File.Exists(resultPath)) return RestoreInstallResult(resultPath, pendingPath);
        if (!File.Exists(pendingPath)) return false;

        PendingInstall pending = null;
        try
        {
            pending = ReadPendingInstall(pendingPath);
            if (!IsInstallHelperRunning(pending.HelperProcessId))
            {
                if (string.IsNullOrWhiteSpace(pending.ScriptPath) || !File.Exists(pending.ScriptPath))
                    throw new FileNotFoundException("The pending update installer is missing.", pending.ScriptPath);
                pending.ResumeAttempts++;
                if (pending.ResumeAttempts > 2)
                    throw new InvalidOperationException(
                        "The update installer stopped repeatedly. Security software may be blocking PowerShell.");
                pending.HelperProcessId = StartInstallHelper(pending.ScriptPath);
                WritePendingInstall(pendingPath, pending);
            }

            lock (Sync)
            {
                _snapshot.AvailableVersion = pending.Version;
                _snapshot.LocalPackagePath = pending.PackagePath;
                _snapshot.Error = null;
                PublishLocked(EmpireCraftUpdateStatus.InstallPending);
            }
            LogService.LogInfo("EmpireCraft update is still waiting for the game to exit: " + pending.Version);
            return true;
        }
        catch (Exception error)
        {
            TryDeleteFile(pendingPath);
            lock (Sync)
            {
                _snapshot.AvailableVersion = pending?.Version;
                _snapshot.LocalPackagePath = pending?.PackagePath;
                _snapshot.Error = error.Message;
                PublishLocked(EmpireCraftUpdateStatus.InstallFailed);
            }
            LogService.LogWarning("EmpireCraft pending update could not be resumed: " + error);
            return true;
        }
    }

    private static bool RestoreInstallResult(string resultPath, string pendingPath)
    {
        try
        {
            JObject result = JObject.Parse(File.ReadAllText(resultPath));
            string status = result.Value<string>("status")?.Trim();
            string version = result.Value<string>("version")?.Trim();
            string packagePath = result.Value<string>("packagePath")?.Trim();
            string targetPath = result.Value<string>("targetPath")?.Trim();
            string detail = result.Value<string>("detail")?.Trim();
            string currentVersion = ReadInstalledVersion();
            string currentTarget = Path.GetFullPath(ModClass._declare?.FolderPath ?? "");
            bool helperReportedSuccess = string.Equals(status, "success", StringComparison.OrdinalIgnoreCase);
            bool versionMatches = !string.IsNullOrWhiteSpace(version) &&
                                  CompareVersions(currentVersion, version) >= 0;
            bool targetMatches = !string.IsNullOrWhiteSpace(targetPath) && PathsEqual(currentTarget, targetPath);

            EmpireCraftUpdateStatus restoredStatus;
            if (helperReportedSuccess && versionMatches && targetMatches)
            {
                restoredStatus = EmpireCraftUpdateStatus.InstallSucceeded;
                detail = null;
            }
            else
            {
                restoredStatus = EmpireCraftUpdateStatus.InstallFailed;
                if (helperReportedSuccess)
                {
                    detail = "The updater replaced " + targetPath + " with " + version +
                             ", but the game loaded " + currentVersion + " from " + currentTarget +
                             ". Remove duplicate EmpireCraft installations or restart once more.";
                }
                else if (string.IsNullOrWhiteSpace(detail))
                {
                    detail = "The external installer reported a failure without an error message.";
                }
            }

            lock (Sync)
            {
                _snapshot.CurrentVersion = currentVersion;
                _snapshot.AvailableVersion = version;
                _snapshot.LocalPackagePath = packagePath;
                _snapshot.Error = detail;
                _snapshot.ProgressPercent = helperReportedSuccess ? 100 : 0;
                PublishLocked(restoredStatus);
            }

            if (restoredStatus == EmpireCraftUpdateStatus.InstallSucceeded)
                LogService.LogInfo("EmpireCraft update installation verified: " + version);
            else
                LogService.LogWarning("EmpireCraft update installation failed: " + detail);
        }
        catch (Exception error)
        {
            lock (Sync)
            {
                _snapshot.Error = "Could not read the update installation result: " + error.Message;
                PublishLocked(EmpireCraftUpdateStatus.InstallFailed);
            }
            LogService.LogWarning("EmpireCraft update result is invalid: " + error);
        }
        finally
        {
            TryDeleteFile(resultPath);
            TryDeleteFile(pendingPath);
        }
        return true;
    }

    private static string GetUpdatesRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EmpireCraft",
            "Updates");
    }

    private static bool ReadManualPackageMode()
    {
        try
        {
            string path = Path.Combine(GetUpdatesRoot(), UpdateSettingsFileName);
            if (!File.Exists(path)) return false;
            JObject settings = JObject.Parse(File.ReadAllText(path));
            return settings.Value<bool?>("manualPackageMode") ?? false;
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft update preference could not be read: " + error.Message);
            return false;
        }
    }

    private static void WriteUpdateSettings(bool manualPackageMode)
    {
        string root = GetUpdatesRoot();
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, UpdateSettingsFileName);
        string temporaryPath = path + ".tmp";
        var settings = new JObject
        {
            ["manualPackageMode"] = manualPackageMode
        };
        File.WriteAllText(temporaryPath, settings.ToString(), new UTF8Encoding(false));
        TryDeleteFile(path);
        File.Move(temporaryPath, path);
    }

    private static void WritePendingInstall(string path, PendingInstall pending)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var json = new JObject
        {
            ["version"] = pending.Version,
            ["packagePath"] = pending.PackagePath,
            ["scriptPath"] = pending.ScriptPath,
            ["targetPath"] = pending.TargetPath,
            ["helperProcessId"] = pending.HelperProcessId,
            ["resumeAttempts"] = pending.ResumeAttempts,
            ["scheduledAt"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, json.ToString(), new UTF8Encoding(false));
        TryDeleteFile(path);
        File.Move(temporaryPath, path);
    }

    private static PendingInstall ReadPendingInstall(string path)
    {
        JObject json = JObject.Parse(File.ReadAllText(path));
        var pending = new PendingInstall
        {
            Version = json.Value<string>("version")?.Trim(),
            PackagePath = json.Value<string>("packagePath")?.Trim(),
            ScriptPath = json.Value<string>("scriptPath")?.Trim(),
            TargetPath = json.Value<string>("targetPath")?.Trim(),
            HelperProcessId = json.Value<int?>("helperProcessId") ?? 0,
            ResumeAttempts = json.Value<int?>("resumeAttempts") ?? 0
        };
        if (string.IsNullOrWhiteSpace(pending.Version) || string.IsNullOrWhiteSpace(pending.ScriptPath))
            throw new InvalidDataException("The pending update record is incomplete.");
        return pending;
    }

    private static int StartInstallHelper(string scriptPath)
    {
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + QuoteArgument(scriptPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        using Process process = Process.Start(start) ??
                                throw new InvalidOperationException("The update installer process did not start.");
        return process.Id;
    }

    private static bool IsInstallHelperRunning(int processId)
    {
        if (processId <= 0) return false;
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited) return false;
            return process.ProcessName.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
                   process.ProcessName.Equals("pwsh", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Cleanup failures must not hide the actual update result.
        }
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
