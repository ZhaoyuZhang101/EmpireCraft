using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
    private const long MaximumExtractedBytes = 512L * 1024L * 1024L;
    private const int MaximumArchiveEntries = 20000;
    private const string PendingInstallFileName = "pending-install.json";
    private const string InstallResultFileName = "install-result.json";
    private const string UpdateSettingsFileName = "update-settings.json";
    private static readonly object Sync = new();
    private static bool _initialized;
    private static bool _busy;
    private static bool _manualPackageMode;
    private static bool _archiveCurrentVersion;
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
        EnableTls12();
        (bool manualPackageMode, bool archiveCurrentVersion) = ReadUpdateSettings();
        lock (Sync)
        {
            if (_initialized) return;
            _initialized = true;
            _manualPackageMode = manualPackageMode;
            _archiveCurrentVersion = archiveCurrentVersion;
            _snapshot.CurrentVersion = ReadInstalledVersion();
        }

        if (!RestoreInstallState()) CheckForUpdates();
    }

    // 有新版本可更新(用于更新按钮上的小红点)
    public static bool HasUpdateAvailable
    {
        get
        {
            lock (Sync) return _snapshot.Status == EmpireCraftUpdateStatus.UpdateAvailable;
        }
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

        SaveUpdateSettings();
    }

    public static bool ArchiveCurrentVersion
    {
        get
        {
            lock (Sync) return _archiveCurrentVersion;
        }
    }

    public static void SetArchiveCurrentVersion(bool enabled)
    {
        lock (Sync)
        {
            if (_archiveCurrentVersion == enabled) return;
            _archiveCurrentVersion = enabled;
            _snapshot.Revision++;
        }

        SaveUpdateSettings();
    }

    public static void CheckForUpdates()
    {
        if (!TryBegin(EmpireCraftUpdateStatus.Checking)) return;
        ThreadPool.QueueUserWorkItem(_ => CheckWorker());
    }

    public static void DownloadAndInstall()
    {
        UpdateManifest manifest;
        bool manualPackageMode;
        bool archiveCurrentVersion;
        lock (Sync)
        {
            if (_busy || _manifest == null || _snapshot.Status != EmpireCraftUpdateStatus.UpdateAvailable)
                return;

            _busy = true;
            manifest = _manifest;
            manualPackageMode = _manualPackageMode;
            archiveCurrentVersion = _archiveCurrentVersion;
            PublishLocked(EmpireCraftUpdateStatus.Downloading, progress: 0);
        }

        ThreadPool.QueueUserWorkItem(_ => DownloadWorker(manifest, manualPackageMode, archiveCurrentVersion));
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

    public static bool OpenArchivedVersionsFolder()
    {
        try
        {
            string path = GetArchivedVersionsRoot();
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft could not open the archive directory: " + error.Message);
            return false;
        }
    }

    // 打开 TLS 1.2。手机版(安卓)上 ServicePointManager 的静态构造会抛异常(2026-10-22 手机版读模组时报
    // "The type initializer for 'System.Net.ServicePointManager' threw an exception"，整个模组加载失败)：设不上就用默认协议
    internal static void EnableTls12()
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        catch (Exception)
        {
            // 设不上照常走默认协议；真连不上由检查更新、上传报错各自报失败
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

    private static void DownloadWorker(UpdateManifest manifest, bool manualPackageMode,
        bool archiveCurrentVersion)
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

            string gitMarker = Path.Combine(modFolder, ".git");
            bool developerCheckout = Directory.Exists(gitMarker) || File.Exists(gitMarker);

            if (developerCheckout)
            {
                Complete(EmpireCraftUpdateStatus.ManualInstallRequired, packagePath,
                    "Developer checkout detected; automatic replacement is disabled.");
                return;
            }

            InstallNow(packageRoot, modFolder, updateRoot, manifest.Version, packagePath, archiveCurrentVersion);
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
            if (_busy || _snapshot.Status is EmpireCraftUpdateStatus.InstallScheduled or
                EmpireCraftUpdateStatus.InstallPending) return false;
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
        if (archive.Entries.Count > MaximumArchiveEntries)
            throw new Exception("Update package contains too many entries.");
        long extracted = 0;
        var buffer = new byte[64 * 1024];
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string outputPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!outputPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Update package contains an unsafe path.");
            if (entry.Length < 0 || entry.Length > MaximumExtractedBytes - extracted)
                throw new Exception("Update package exceeds the extracted size limit.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(outputPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            using Stream input = entry.Open();
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (read > MaximumExtractedBytes - extracted)
                    throw new Exception("Update package exceeds the extracted size limit.");
                output.Write(buffer, 0, read);
                extracted += read;
            }
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
        if (File.Exists(Path.Combine(packageRoot, ".git")) ||
            Directory.Exists(Path.Combine(packageRoot, ".git")))
            throw new Exception("Update package contains a Git checkout marker.");
    }

    // 当场换上新版(用户 2026-10-22 转"自动更新程序有问题，更新完重启没生效"，选"游戏里当场替换")：以前等游戏退出后由
    // 隐藏窗口、绕过执行策略的 PowerShell 脚本替换，常被杀毒软件拦下、或随游戏进程一起被结束，文件根本没换上。
    // 现在下载校验完就在这个后台线程里换：
    //   · 候选：旧目录整个拷一份，新版的各目录整个换掉、新版的顶层文件覆盖。玩家的设置不在模组目录里(NML 存在
//     mods_config/<模组>.config，文化物种配对、派系、官职等存在 Mods 目录下)，一概不动；模组自带的 default_config.json
//     只是设置项与默认值，换成新版的，新加的设置项才出得来(以前留旧的，新开关在玩家那里缺项)；
    //   · 对调：旧目录改名成备份、候选改名成模组目录(都在 Mods 外面的同一层，只是改名)；
    //   · 对调不了(正在放的曲子等文件被占用)：旧目录先整拷成备份，再把候选逐个文件同步进去(内容一样的跳过、多余的删掉)，
    //     被占用又不同的记下(deferred-install.json)，下次启动一开始补上；
    //   · 动手前记下进度(install-state.json)：游戏中途被关、没换完的，下次启动从备份恢复旧版；
    //   · 换完写安装结果，下次启动核对加载的版本(见 RestoreInstallResult)。正在运行的这局照旧用已经编译好的旧版，
    //     重启后 NML 发现文件变了重新编译。
    private const string DeferredInstallFileName = "deferred-install.json";
    private const string InstallStateFileName = "install-state.json";

    private static void InstallNow(string source, string target, string updateRoot, string version, string packagePath,
        bool archiveCurrentVersion)
    {
        string targetPath = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string modsPath = Path.GetDirectoryName(targetPath) ??
                          throw new InvalidOperationException("The mod folder has no parent directory.");
        string workRoot = Path.GetDirectoryName(modsPath) ??
                          throw new InvalidOperationException("The Mods folder has no parent directory.");
        string installId = Guid.NewGuid().ToString("N");
        string name = Path.GetFileName(targetPath);
        string candidate = Path.Combine(workRoot, "." + name + ".update-" + installId);
        string backup = Path.Combine(workRoot, "." + name + ".backup-" + installId);
        string stateRoot = GetUpdatesRoot();
        Directory.CreateDirectory(stateRoot);
        string statePath = Path.Combine(stateRoot, InstallStateFileName);
        string resultPath = Path.Combine(stateRoot, InstallResultFileName);
        TryDeleteFile(resultPath);

        BuildCandidate(source, targetPath, candidate, version);
        List<string> deferred = null;
        var state = new JObject
        {
            ["target"] = targetPath, ["backup"] = backup, ["candidate"] = candidate, ["version"] = version, ["phase"] = "moving"
        };
        WriteJson(statePath, state);
        bool swapped = TryMoveDirectory(targetPath, backup);
        try
        {
            if (swapped)
            {
                SetPhase(statePath, state, "swapped");
                Directory.Move(candidate, targetPath);
            }
            else
            {
                SetPhase(statePath, state, "backup");
                CopyDirectory(targetPath, backup);
                SetPhase(statePath, state, "syncing");
                deferred = SyncDirectory(candidate, targetPath);
            }
            string installed = ReadModVersion(targetPath);
            if (!string.Equals(installed, version, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Installed version mismatch. Expected " + version + ", found " + installed);
        }
        catch (Exception error)
        {
            string restored = RestoreFromBackup(targetPath, backup, swapped) ? " Previous installation restored." : "";
            TryDeleteFile(statePath);
            TryDeleteDirectory(candidate);
            throw new Exception(error.Message + restored, error);
        }

        if (deferred != null && deferred.Count > 0)
            WriteJson(Path.Combine(stateRoot, DeferredInstallFileName), new JObject
            {
                ["target"] = targetPath, ["candidate"] = candidate, ["entries"] = new JArray(deferred)
            });
        else TryDeleteDirectory(candidate);
        TryDeleteFile(statePath);

        string archivePath = null;
        if (archiveCurrentVersion)
        {
            try
            {
                archivePath = ArchiveBackup(backup, modsPath, installId);
            }
            catch (Exception error)
            {
                LogService.LogWarning("EmpireCraft could not archive the replaced version: " + error.Message);
            }
        }
        TryDeleteDirectory(backup);
        try
        {
            File.WriteAllText(Path.Combine(updateRoot, "install.log"),
                "Installed " + version + " at " + DateTime.Now.ToString("o", CultureInfo.InvariantCulture) + " into " + targetPath +
                (swapped ? "" : "; synced file by file, deferred " + (deferred?.Count ?? 0)) +
                (archivePath != null ? "; archive: " + archivePath : ""), new UTF8Encoding(false));
        }
        catch
        {
            // 只是记录
        }
        WriteJson(resultPath, new JObject
        {
            ["status"] = "success", ["version"] = version, ["packagePath"] = packagePath, ["targetPath"] = targetPath,
            ["detail"] = archivePath != null ? "Archive: " + archivePath : "",
            ["completedAt"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        });
        LogService.LogInfo("EmpireCraft update " + version + " installed into " + targetPath +
                           (swapped ? "" : " (file by file, deferred " + (deferred?.Count ?? 0) + ")") + "; restart to load it");
    }

    // 候选目录：旧目录整拷一份，新版的各目录整个换掉，新版的顶层文件覆盖(旧版独有的顶层文件留着)
    private static void BuildCandidate(string source, string target, string candidate, string version)
    {
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException("Installed mod folder is missing.");
        if (Directory.Exists(Path.Combine(source, ".git")) || File.Exists(Path.Combine(source, ".git")))
            throw new Exception("Update package contains a Git checkout marker.");
        TryDeleteDirectory(candidate);
        CopyDirectory(target, candidate);
        foreach (string directory in Directory.GetDirectories(source))
        {
            string destination = Path.Combine(candidate, Path.GetFileName(directory));
            TryDeleteDirectory(destination);
            if (Directory.Exists(destination)) throw new IOException("Could not clear " + destination);
            CopyDirectory(directory, destination);
        }
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(candidate, Path.GetFileName(file)), true);
        if (!Directory.Exists(Path.Combine(candidate, "Scripts")))
            throw new Exception("Candidate update is missing Scripts.");
        if (!string.Equals(ReadModVersion(candidate), version, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Candidate update version mismatch.");
    }

    private static void SetPhase(string statePath, JObject state, string phase)
    {
        state["phase"] = phase;
        WriteJson(statePath, state);
    }

    private static bool TryMoveDirectory(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (string directory in Directory.GetDirectories(from))
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
    }

    // 把 from 逐个文件同步进 to：内容一样的跳过，不同的覆盖，to 里多出来的删掉；被占用的记下("copy|相对路径"/"delete|相对路径")
    private static List<string> SyncDirectory(string from, string to)
    {
        var deferred = new List<string>();
        string fromRoot = Path.GetFullPath(from).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string toRoot = Path.GetFullPath(to).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string file in Directory.GetFiles(fromRoot, "*", SearchOption.AllDirectories))
        {
            string relative = file.Substring(fromRoot.Length);
            string destination = Path.Combine(toRoot, relative);
            if (File.Exists(destination) && SameContent(file, destination)) continue;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination, true);
            }
            catch (IOException)
            {
                deferred.Add("copy|" + relative);
            }
        }
        foreach (string file in Directory.GetFiles(toRoot, "*", SearchOption.AllDirectories))
        {
            string relative = file.Substring(toRoot.Length);
            if (File.Exists(Path.Combine(fromRoot, relative))) continue;
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                deferred.Add("delete|" + relative);
            }
        }
        foreach (string directory in Directory.GetDirectories(toRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            if (Directory.Exists(Path.Combine(fromRoot, directory.Substring(toRoot.Length)))) continue;
            try
            {
                if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
            }
            catch (IOException)
            {
                // 空目录删不掉无妨
            }
        }
        return deferred;
    }

    private static bool SameContent(string left, string right)
    {
        var a = new FileInfo(left);
        var b = new FileInfo(right);
        if (a.Length != b.Length) return false;
        using FileStream x = File.OpenRead(left);
        using FileStream y = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bufferA = new byte[64 * 1024];
        var bufferB = new byte[64 * 1024];
        while (true)
        {
            int read = x.Read(bufferA, 0, bufferA.Length);
            if (read == 0) return true;
            int offset = 0;
            while (offset < read)
            {
                int got = y.Read(bufferB, offset, read - offset);
                if (got == 0) return false;
                offset += got;
            }
            for (int i = 0; i < read; i++)
                if (bufferA[i] != bufferB[i]) return false;
        }
    }

    // 换坏了：换回备份(整夹对调的把备份改名回来；逐个同步的把备份同步回去)
    private static bool RestoreFromBackup(string target, string backup, bool swapped)
    {
        try
        {
            if (!Directory.Exists(backup)) return false;
            if (swapped)
            {
                if (Directory.Exists(target))
                {
                    string failed = backup + ".failed";
                    TryDeleteDirectory(failed);
                    Directory.Move(target, failed);
                    TryDeleteDirectory(failed);
                }
                Directory.Move(backup, target);
                return true;
            }
            SyncDirectory(backup, target);
            TryDeleteDirectory(backup);
            return true;
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft could not restore the previous installation from " + backup + ": " + error.Message);
            return false;
        }
    }

    private static string ArchiveBackup(string backup, string modsPath, string installId)
    {
        string archiveRoot = Path.GetFullPath(GetArchivedVersionsRoot());
        string modsRoot = modsPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (PathsEqual(archiveRoot, modsPath) || archiveRoot.StartsWith(modsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The update archive must be outside the Mods directory.");
        Directory.CreateDirectory(archiveRoot);
        string oldVersion = SanitizeFileName(ReadModVersion(backup) ?? "unknown");
        string archivePath = Path.Combine(archiveRoot, "EmpireCraft_" + oldVersion + "_" +
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + "_" + installId + ".ecbackup");
        string temporary = archivePath + ".tmp";
        TryDeleteFile(temporary);
        ZipFile.CreateFromDirectory(backup, temporary, CompressionLevel.Optimal, false);
        File.Move(temporary, archivePath);
        return archivePath;
    }

    private static string ReadModVersion(string folder)
    {
        string path = Path.Combine(folder, "mod.json");
        if (!File.Exists(path)) return null;
        return JObject.Parse(File.ReadAllText(path)).Value<string>("version")?.Trim();
    }

    private static void WriteJson(string path, JObject json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json.ToString(), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
            // 清理失败无妨(在 Mods 外面、以点开头，不会被当成模组)
        }
    }

    // 启动时：上次当场替换中途被打断的从备份恢复旧版；被占用没换上的文件补上。旧版本(脚本安装)留下的待安装记录清掉
    private static void FinishInterruptedInstall()
    {
        string stateRoot = GetUpdatesRoot();
        TryDeleteFile(Path.Combine(stateRoot, PendingInstallFileName));
        string statePath = Path.Combine(stateRoot, InstallStateFileName);
        if (File.Exists(statePath))
        {
            try
            {
                JObject state = JObject.Parse(File.ReadAllText(statePath));
                string target = state.Value<string>("target"), backup = state.Value<string>("backup");
                string version = state.Value<string>("version"), phase = state.Value<string>("phase");
                bool installed = Directory.Exists(target) &&
                                 string.Equals(ReadModVersion(target), version, StringComparison.OrdinalIgnoreCase);
                // 整夹对调已经完成、只差收尾：算装好了
                if (phase == "swapped" && installed)
                {
                    TryDeleteDirectory(backup);
                    WriteJson(Path.Combine(stateRoot, InstallResultFileName), new JObject
                    {
                        ["status"] = "success", ["version"] = version, ["targetPath"] = target, ["detail"] = ""
                    });
                }
                // 还在整拷备份：旧目录没动过，丢掉半份备份
                else if (phase == "backup") TryDeleteDirectory(backup);
                else
                {
                    bool restored = !Directory.Exists(target)
                        ? RestoreFromBackup(target, backup, true)
                        : phase == "syncing" && RestoreFromBackup(target, backup, false);
                    WriteJson(Path.Combine(stateRoot, InstallResultFileName), new JObject
                    {
                        ["status"] = "failure", ["version"] = version, ["targetPath"] = target,
                        ["detail"] = "The game closed while the update was being installed." +
                                     (restored ? " Previous installation restored." : "")
                    });
                }
                TryDeleteDirectory(state.Value<string>("candidate"));
            }
            catch (Exception error)
            {
                LogService.LogWarning("EmpireCraft could not recover an interrupted update: " + error.Message);
            }
            TryDeleteFile(statePath);
        }
        string deferredPath = Path.Combine(stateRoot, DeferredInstallFileName);
        if (!File.Exists(deferredPath)) return;
        try
        {
            JObject deferred = JObject.Parse(File.ReadAllText(deferredPath));
            string target = deferred.Value<string>("target"), candidate = deferred.Value<string>("candidate");
            if (!PathsEqual(target, ModClass._declare?.FolderPath ?? ""))
            {
                TryDeleteFile(deferredPath);
                return;
            }
            var left = new JArray();
            foreach (string entry in deferred.Value<JArray>("entries")?.Values<string>() ?? Array.Empty<string>())
            {
                int cut = entry.IndexOf('|');
                if (cut <= 0) continue;
                string relative = entry.Substring(cut + 1), destination = Path.Combine(target, relative);
                try
                {
                    if (entry.StartsWith("copy|", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        File.Copy(Path.Combine(candidate, relative), destination, true);
                    }
                    else if (File.Exists(destination)) File.Delete(destination);
                }
                catch (IOException)
                {
                    left.Add(entry);
                }
            }
            if (left.Count > 0)
            {
                deferred["entries"] = left;
                WriteJson(deferredPath, deferred);
                return;
            }
            TryDeleteDirectory(candidate);
            TryDeleteFile(deferredPath);
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft could not finish deferred update files: " + error.Message);
        }
    }

    private static bool RestoreInstallState()
    {
        FinishInterruptedInstall();
        string stateRoot = GetUpdatesRoot();
        string resultPath = Path.Combine(stateRoot, InstallResultFileName);
        string pendingPath = Path.Combine(stateRoot, PendingInstallFileName);
        return File.Exists(resultPath) && RestoreInstallResult(resultPath, pendingPath);
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

    private static string GetArchivedVersionsRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EmpireCraft",
            "ArchivedVersions");
    }

    private static (bool manualPackageMode, bool archiveCurrentVersion) ReadUpdateSettings()
    {
        try
        {
            string path = Path.Combine(GetUpdatesRoot(), UpdateSettingsFileName);
            if (!File.Exists(path)) return (false, false);
            JObject settings = JObject.Parse(File.ReadAllText(path));
            return (settings.Value<bool?>("manualPackageMode") ?? false,
                    settings.Value<bool?>("archiveCurrentVersion") ?? false);
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft update preference could not be read: " + error.Message);
            return (false, false);
        }
    }

    private static void SaveUpdateSettings()
    {
        try
        {
            lock (Sync) WriteUpdateSettings(_manualPackageMode, _archiveCurrentVersion);
        }
        catch (Exception error)
        {
            LogService.LogWarning("EmpireCraft update preference could not be saved: " + error.Message);
        }
    }

    private static void WriteUpdateSettings(bool manualPackageMode, bool archiveCurrentVersion)
    {
        string root = GetUpdatesRoot();
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, UpdateSettingsFileName);
        string temporaryPath = path + ".tmp";
        var settings = new JObject
        {
            ["manualPackageMode"] = manualPackageMode,
            ["archiveCurrentVersion"] = archiveCurrentVersion
        };
        File.WriteAllText(temporaryPath, settings.ToString(), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temporaryPath, path, null);
        else File.Move(temporaryPath, path);
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

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value;
    }
}
