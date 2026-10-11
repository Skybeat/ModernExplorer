using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentFTP;
using ModernExplorer.Core.FileSystem;
using Renci.SshNet;

namespace ModernExplorer.Core.Remote;

/// <summary>
/// Supported remote file transfer protocols.
/// </summary>
public enum RemoteProtocol
{
    Sftp = 0,
    Ftp = 1,
    Ftps = 2,
}

/// <summary>
/// Represents a saved FTP / FTPS / SFTP connection profile.
/// </summary>
public sealed record RemoteConnectionProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Mein Server";
    public RemoteProtocol Protocol { get; init; } = RemoteProtocol.Sftp;
    public string Host { get; init; } = "";
    public int Port { get; init; } = 22;
    public string Username { get; init; } = "root";
    public string EncryptedPassword { get; init; } = "";
    public string PrivateKeyPath { get; init; } = "";
    public string InitialRemotePath { get; init; } = "/";

    [JsonIgnore]
    public string DisplaySummary =>
        $"{Protocol.ToString().ToUpperInvariant()} • {Username}@{Host}:{Port}{NormalizeRemotePath(InitialRemotePath)}";

    public string GetDecryptedPassword() => RemoteCredentialProtector.Unprotect(EncryptedPassword);

    public RemoteConnectionProfile WithPlainPassword(string? plainPassword) =>
        this with { EncryptedPassword = RemoteCredentialProtector.Protect(plainPassword ?? "") };

    public string BuildRootUri(string? overrideRemotePath = null)
    {
        string scheme = Protocol switch
        {
            RemoteProtocol.Ftp => "ftp",
            RemoteProtocol.Ftps => "ftps",
            _ => "sftp"
        };
        string path = NormalizeRemotePath(overrideRemotePath ?? InitialRemotePath);
        string userPart = string.IsNullOrWhiteSpace(Username) ? "" : $"{Uri.EscapeDataString(Username)}@";
        return $"{scheme}://{userPart}{Host}:{Port}{path}";
    }

    public static string NormalizeRemotePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";
        var norm = path.Replace('\\', '/').Trim();
        if (!norm.StartsWith('/'))
            norm = "/" + norm;
        if (norm.Length > 1)
            norm = norm.TrimEnd('/');
        return norm;
    }
}

/// <summary>
/// Encrypts and decrypts saved server passwords locally.
/// </summary>
public static class RemoteCredentialProtector
{
    private static readonly byte[] EntropyBytes = Encoding.UTF8.GetBytes("ModernExplorer.RemoteProfile.v1");

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return "";

        var raw = Encoding.UTF8.GetBytes(plainText);
        for (int i = 0; i < raw.Length; i++)
            raw[i] ^= EntropyBytes[i % EntropyBytes.Length];

        return Convert.ToBase64String(raw);
    }

    public static string Unprotect(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64))
            return "";

        try
        {
            var raw = Convert.FromBase64String(encryptedBase64);
            for (int i = 0; i < raw.Length; i++)
                raw[i] ^= EntropyBytes[i % EntropyBytes.Length];

            return Encoding.UTF8.GetString(raw);
        }
        catch
        {
            return "";
        }
    }
}

/// <summary>
/// Persists saved FTP / FTPS / SFTP server profiles in %APPDATA%/ModernExplorer/remote-profiles.json.
/// </summary>
public sealed class RemoteProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;

    public static RemoteProfileStore Shared { get; } = new();

    public ObservableCollection<RemoteConnectionProfile> Profiles { get; } = [];

    public RemoteProfileStore(string? filePath = null)
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModernExplorer");
        Directory.CreateDirectory(baseDir);
        _filePath = filePath ?? Path.Combine(baseDir, "remote-profiles.json");
        Load();
    }

    public void AddOrUpdate(RemoteConnectionProfile profile)
    {
        int existingIdx = -1;
        for (int i = 0; i < Profiles.Count; i++)
        {
            if (Profiles[i].Id == profile.Id)
            {
                existingIdx = i;
                break;
            }
        }

        if (existingIdx >= 0)
            Profiles[existingIdx] = profile;
        else
            Profiles.Add(profile);

        Save();
    }

    public void Remove(Guid id)
    {
        var found = Profiles.FirstOrDefault(p => p.Id == id);
        if (found is not null)
        {
            Profiles.Remove(found);
            Save();
        }
    }

    public RemoteConnectionProfile? ResolveProfileForUri(string remoteUri)
    {
        if (!RemoteFileSystemService.TryParseRemoteUri(remoteUri, out var protocol, out var host, out var port, out var user, out _))
            return null;

        return Profiles.FirstOrDefault(p =>
            p.Protocol == protocol &&
            string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase) &&
            p.Port == port &&
            (string.IsNullOrEmpty(user) || string.Equals(p.Username, user, StringComparison.OrdinalIgnoreCase)))
            ?? Profiles.FirstOrDefault(p =>
                string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase));
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Profiles.ToList(), JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<RemoteConnectionProfile>>(json, JsonOptions);
                if (list is not null)
                {
                    Profiles.Clear();
                    foreach (var p in list)
                        Profiles.Add(p);
                }
            }
        }
        catch { }
    }
}

/// <summary>
/// Provides cross-platform SFTP (SSH.NET) and FTP/FTPS (FluentFTP) directory listing,
/// file upload/download, remote folder creation, deletion, rename, and Unix chmod.
/// </summary>
public static class RemoteFileSystemService
{
    public static bool IsRemoteUri(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return path.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParseRemoteUri(
        string? rawUri,
        out RemoteProtocol protocol,
        out string host,
        out int port,
        out string username,
        out string remotePath)
    {
        protocol = RemoteProtocol.Sftp;
        host = "";
        port = 22;
        username = "anonymous";
        remotePath = "/";

        if (!IsRemoteUri(rawUri))
            return false;

        try
        {
            var uri = new Uri(rawUri!);
            protocol = uri.Scheme.ToLowerInvariant() switch
            {
                "ftp" => RemoteProtocol.Ftp,
                "ftps" => RemoteProtocol.Ftps,
                _ => RemoteProtocol.Sftp,
            };
            host = uri.Host;
            port = uri.Port > 0
                ? uri.Port
                : (protocol == RemoteProtocol.Sftp ? 22 : 21);

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                username = Uri.UnescapeDataString(parts[0]);
            }

            remotePath = RemoteConnectionProfile.NormalizeRemotePath(Uri.UnescapeDataString(uri.AbsolutePath));
            return !string.IsNullOrWhiteSpace(host);
        }
        catch
        {
            return false;
        }
    }

    public static string GetParentRemoteUri(string remoteUri)
    {
        if (!TryParseRemoteUri(remoteUri, out var protocol, out var host, out var port, out var user, out var remotePath))
            return "";

        if (remotePath == "/")
            return "";

        int slash = remotePath.LastIndexOf('/');
        string parentPath = slash <= 0 ? "/" : remotePath[..slash];
        return BuildRemoteUri(protocol, host, port, user, parentPath);
    }

    public static string CombineRemoteUri(string baseRemoteUri, string childName)
    {
        if (!TryParseRemoteUri(baseRemoteUri, out var protocol, out var host, out var port, out var user, out var remotePath))
            return baseRemoteUri;

        string nextPath = remotePath == "/"
            ? "/" + childName.Trim('/')
            : $"{remotePath}/{childName.Trim('/')}";

        return BuildRemoteUri(protocol, host, port, user, nextPath);
    }

    public static string BuildRemoteUri(RemoteProtocol protocol, string host, int port, string username, string remotePath)
    {
        string scheme = protocol switch
        {
            RemoteProtocol.Ftp => "ftp",
            RemoteProtocol.Ftps => "ftps",
            _ => "sftp"
        };
        string userPart = string.IsNullOrWhiteSpace(username) ? "" : $"{Uri.EscapeDataString(username)}@";
        string normPath = RemoteConnectionProfile.NormalizeRemotePath(remotePath);
        return $"{scheme}://{userPart}{host}:{port}{normPath}";
    }

    public static async Task<IReadOnlyList<FileEntry>> ListDirectoryAsync(string remoteUri, CancellationToken ct = default)
    {
        if (!TryParseRemoteUri(remoteUri, out var protocol, out var host, out var port, out var user, out var remotePath))
            return [];

        var profile = ResolveEffectiveProfile(remoteUri, protocol, host, port, user);

        if (protocol == RemoteProtocol.Sftp)
        {
            return await Task.Run(() => ListSftpDirectory(profile, remoteUri, remotePath), ct);
        }

        return await ListFtpDirectoryAsync(profile, remoteUri, remotePath, ct);
    }

    public static async Task<string> DownloadToLocalFileAsync(
        string remoteFileUri,
        string localTargetFilePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!TryParseRemoteUri(remoteFileUri, out var protocol, out var host, out var port, out var user, out var remotePath))
            throw new InvalidOperationException($"Ungültige Remote-URI: {remoteFileUri}");

        var profile = ResolveEffectiveProfile(remoteFileUri, protocol, host, port, user);
        var parentDir = Path.GetDirectoryName(localTargetFilePath);
        if (!string.IsNullOrEmpty(parentDir))
            Directory.CreateDirectory(parentDir);

        if (protocol == RemoteProtocol.Sftp)
        {
            await Task.Run(() =>
            {
                using var client = CreateConnectedSftpClient(profile);
                using var fs = new FileStream(localTargetFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                client.DownloadFile(remotePath, fs);
                progress?.Report(1.0);
                client.Disconnect();
            }, ct);
            return localTargetFilePath;
        }

        await using var ftp = await CreateConnectedFtpClientAsync(profile, ct);
        await ftp.DownloadFile(localTargetFilePath, remotePath, FtpLocalExists.Overwrite, FtpVerify.None,
            new Progress<FtpProgress>(p => progress?.Report(Math.Clamp(p.Progress / 100.0, 0, 1))), ct);
        return localTargetFilePath;
    }

    public static async Task UploadLocalFileAsync(
        string localFilePath,
        string remoteTargetFileUri,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!TryParseRemoteUri(remoteTargetFileUri, out var protocol, out var host, out var port, out var user, out var remotePath))
            throw new InvalidOperationException($"Ungültige Remote-URI: {remoteTargetFileUri}");

        var profile = ResolveEffectiveProfile(remoteTargetFileUri, protocol, host, port, user);

        if (protocol == RemoteProtocol.Sftp)
        {
            await Task.Run(() =>
            {
                using var client = CreateConnectedSftpClient(profile);
                using var fs = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                client.UploadFile(fs, remotePath, canOverride: true);
                progress?.Report(1.0);
                client.Disconnect();
            }, ct);
            return;
        }

        await using var ftp = await CreateConnectedFtpClientAsync(profile, ct);
        await ftp.UploadFile(localFilePath, remotePath, FtpRemoteExists.Overwrite, createRemoteDir: true, FtpVerify.None,
            new Progress<FtpProgress>(p => progress?.Report(Math.Clamp(p.Progress / 100.0, 0, 1))), ct);
    }

    public static async Task CreateRemoteDirectoryAsync(string remoteDirUri, CancellationToken ct = default)
    {
        if (!TryParseRemoteUri(remoteDirUri, out var protocol, out var host, out var port, out var user, out var remotePath))
            return;

        var profile = ResolveEffectiveProfile(remoteDirUri, protocol, host, port, user);
        if (protocol == RemoteProtocol.Sftp)
        {
            await Task.Run(() =>
            {
                using var client = CreateConnectedSftpClient(profile);
                if (!client.Exists(remotePath))
                    client.CreateDirectory(remotePath);
                client.Disconnect();
            }, ct);
            return;
        }

        await using var ftp = await CreateConnectedFtpClientAsync(profile, ct);
        await ftp.CreateDirectory(remotePath, ct);
    }

    public static async Task DeleteRemoteEntryAsync(string remoteUri, bool isDirectory, CancellationToken ct = default)
    {
        if (!TryParseRemoteUri(remoteUri, out var protocol, out var host, out var port, out var user, out var remotePath))
            return;

        var profile = ResolveEffectiveProfile(remoteUri, protocol, host, port, user);
        if (protocol == RemoteProtocol.Sftp)
        {
            await Task.Run(() =>
            {
                using var client = CreateConnectedSftpClient(profile);
                if (isDirectory)
                    client.DeleteDirectory(remotePath);
                else
                    client.DeleteFile(remotePath);
                client.Disconnect();
            }, ct);
            return;
        }

        await using var ftp = await CreateConnectedFtpClientAsync(profile, ct);
        if (isDirectory)
            await ftp.DeleteDirectory(remotePath, ct);
        else
            await ftp.DeleteFile(remotePath, ct);
    }

    public static async Task RenameRemoteEntryAsync(string remoteUri, string newName, CancellationToken ct = default)
    {
        if (!TryParseRemoteUri(remoteUri, out var protocol, out var host, out var port, out var user, out var remotePath))
            return;

        int slash = remotePath.LastIndexOf('/');
        string parent = slash <= 0 ? "/" : remotePath[..slash];
        string newRemotePath = parent == "/" ? "/" + newName : $"{parent}/{newName}";

        var profile = ResolveEffectiveProfile(remoteUri, protocol, host, port, user);
        if (protocol == RemoteProtocol.Sftp)
        {
            await Task.Run(() =>
            {
                using var client = CreateConnectedSftpClient(profile);
                client.RenameFile(remotePath, newRemotePath);
                client.Disconnect();
            }, ct);
            return;
        }

        await using var ftp = await CreateConnectedFtpClientAsync(profile, ct);
        await ftp.MoveFile(remotePath, newRemotePath, FtpRemoteExists.Overwrite, ct);
    }

    public static async Task ChangePermissionsAsync(string remoteUri, short octalMode, CancellationToken ct = default)
    {
        if (!TryParseRemoteUri(remoteUri, out var protocol, out var host, out var port, out var user, out var remotePath))
            return;

        var profile = ResolveEffectiveProfile(remoteUri, protocol, host, port, user);
        if (protocol == RemoteProtocol.Sftp)
        {
            await Task.Run(() =>
            {
                using var client = CreateConnectedSftpClient(profile);
                client.ChangePermissions(remotePath, octalMode);
                client.Disconnect();
            }, ct);
        }
        else
        {
            await using var ftp = await CreateConnectedFtpClientAsync(profile, ct);
            await ftp.Chmod(remotePath, octalMode, ct);
        }
    }

    public static async Task<string> DownloadToTempFileAsync(string remoteFileUri, CancellationToken ct = default)
    {
        TryParseRemoteUri(remoteFileUri, out _, out _, out _, out _, out var remotePath);
        string fileName = Path.GetFileName(remotePath);
        if (string.IsNullOrEmpty(fileName))
            fileName = "remote_file.tmp";

        var tempDir = Path.Combine(Path.GetTempPath(), "ModernExplorer_RemotePreview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string localTarget = Path.Combine(tempDir, fileName);
        return await DownloadToLocalFileAsync(remoteFileUri, localTarget, null, ct);
    }

    public static Task UploadSingleFileAsync(string localFilePath, string remoteTargetFileUri, CancellationToken ct = default) =>
        UploadLocalFileAsync(localFilePath, remoteTargetFileUri, null, ct);

    public static Task CreateDirectoryAsync(string baseRemoteUri, string folderName, CancellationToken ct = default) =>
        CreateRemoteDirectoryAsync(CombineRemoteUri(baseRemoteUri, folderName), ct);

    public static Task RenameAsync(string remoteUri, string newName, CancellationToken ct = default) =>
        RenameRemoteEntryAsync(remoteUri, newName, ct);

    public static Task DeleteAsync(string remoteUri, bool isDirectory, CancellationToken ct = default) =>
        DeleteRemoteEntryAsync(remoteUri, isDirectory, ct);

    public static async Task<int> DownloadEntriesAsync(
        IReadOnlyList<string> remoteUris,
        string localDestinationDir,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(localDestinationDir);
        int count = 0;
        foreach (var uri in remoteUris)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryParseRemoteUri(uri, out _, out _, out _, out _, out var remotePath))
                continue;

            string fileName = Path.GetFileName(remotePath.TrimEnd('/'));
            if (string.IsNullOrEmpty(fileName)) continue;

            string targetLocal = Path.Combine(localDestinationDir, fileName);
            await DownloadToLocalFileAsync(uri, targetLocal, null, ct);
            count++;
        }
        return count;
    }

    public static async Task<int> UploadEntriesAsync(
        IReadOnlyList<string> localPaths,
        string remoteDestinationDirUri,
        CancellationToken ct = default)
    {
        int count = 0;
        foreach (var localPath in localPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(localPath))
            {
                string fileName = Path.GetFileName(localPath);
                string targetRemote = CombineRemoteUri(remoteDestinationDirUri, fileName);
                await UploadLocalFileAsync(localPath, targetRemote, null, ct);
                count++;
            }
            else if (Directory.Exists(localPath))
            {
                string dirName = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                string remoteSubDir = CombineRemoteUri(remoteDestinationDirUri, dirName);
                await CreateRemoteDirectoryAsync(remoteSubDir, ct);
                var children = Directory.EnumerateFileSystemEntries(localPath).ToList();
                count += await UploadEntriesAsync(children, remoteSubDir, ct);
            }
        }
        return count;
    }

    private static RemoteConnectionProfile ResolveEffectiveProfile(
        string remoteUri,
        RemoteProtocol protocol,
        string host,
        int port,
        string username)
    {
        var saved = RemoteProfileStore.Shared.ResolveProfileForUri(remoteUri);
        if (saved is not null)
            return saved;

        return new RemoteConnectionProfile
        {
            Name = host,
            Protocol = protocol,
            Host = host,
            Port = port,
            Username = username,
            InitialRemotePath = "/"
        };
    }

    private static IReadOnlyList<FileEntry> ListSftpDirectory(
        RemoteConnectionProfile profile,
        string currentUri,
        string remotePath)
    {
        var entries = new List<FileEntry>();
        string parentUri = GetParentRemoteUri(currentUri);
        if (!string.IsNullOrEmpty(parentUri))
            entries.Add(FileEntry.ParentLink(parentUri));

        using var client = CreateConnectedSftpClient(profile);
        foreach (var item in client.ListDirectory(remotePath))
        {
            if (item.Name is "." or "..")
                continue;

            string childUri = CombineRemoteUri(currentUri, item.Name);
            string perms = FormatUnixPermissions(item.Attributes.OwnerCanRead, item.Attributes.OwnerCanWrite, item.Attributes.OwnerCanExecute,
                                                 item.Attributes.GroupCanRead, item.Attributes.GroupCanWrite, item.Attributes.GroupCanExecute,
                                                 item.Attributes.OthersCanRead, item.Attributes.OthersCanWrite, item.Attributes.OthersCanExecute);
            entries.Add(new FileEntry(
                item.Name,
                childUri,
                item.IsDirectory,
                item.IsDirectory ? 0 : item.Length,
                item.LastWriteTime,
                item.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal)
            {
                IsRemoteEntry = true,
                PermissionsText = perms
            });
        }
        client.Disconnect();
        return entries;
    }

    private static async Task<IReadOnlyList<FileEntry>> ListFtpDirectoryAsync(
        RemoteConnectionProfile profile,
        string currentUri,
        string remotePath,
        CancellationToken ct)
    {
        var entries = new List<FileEntry>();
        string parentUri = GetParentRemoteUri(currentUri);
        if (!string.IsNullOrEmpty(parentUri))
            entries.Add(FileEntry.ParentLink(parentUri));

        await using var ftp = await CreateConnectedFtpClientAsync(profile, ct);
        var items = await ftp.GetListing(remotePath, ct);
        foreach (var item in items)
        {
            if (item.Name is "." or "..")
                continue;

            bool isDir = item.Type == FtpObjectType.Directory;
            string childUri = CombineRemoteUri(currentUri, item.Name);
            entries.Add(new FileEntry(
                item.Name,
                childUri,
                isDir,
                isDir ? 0 : Math.Max(0, item.Size),
                item.Modified == default ? DateTime.Now : item.Modified,
                isDir ? FileAttributes.Directory : FileAttributes.Normal)
            {
                IsRemoteEntry = true,
                PermissionsText = item.RawPermissions
            });
        }

        return entries;
    }

    private static SftpClient CreateConnectedSftpClient(RemoteConnectionProfile profile)
    {
        var authMethods = new List<AuthenticationMethod>();
        string password = profile.GetDecryptedPassword();

        if (!string.IsNullOrWhiteSpace(profile.PrivateKeyPath) && File.Exists(profile.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrEmpty(password)
                ? new PrivateKeyFile(profile.PrivateKeyPath)
                : new PrivateKeyFile(profile.PrivateKeyPath, password);
            authMethods.Add(new PrivateKeyAuthenticationMethod(profile.Username, keyFile));
        }

        if (!string.IsNullOrEmpty(password) || authMethods.Count == 0)
        {
            authMethods.Add(new PasswordAuthenticationMethod(profile.Username, password));
        }

        var connInfo = new ConnectionInfo(profile.Host, profile.Port, profile.Username, authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        var client = new SftpClient(connInfo);
        client.Connect();
        return client;
    }

    private static async Task<AsyncFtpClient> CreateConnectedFtpClientAsync(RemoteConnectionProfile profile, CancellationToken ct)
    {
        string password = profile.GetDecryptedPassword();
        var config = new FtpConfig
        {
            ConnectTimeout = 12000,
            EncryptionMode = profile.Protocol == RemoteProtocol.Ftps
                ? FtpEncryptionMode.Explicit
                : FtpEncryptionMode.None,
            ValidateAnyCertificate = true,
        };

        var client = new AsyncFtpClient(profile.Host, new NetworkCredential(profile.Username, password), profile.Port, config);
        await client.AutoConnect(ct);
        return client;
    }

    public static string FormatUnixPermissions(
        bool ur, bool uw, bool ux,
        bool gr, bool gw, bool gx,
        bool or, bool ow, bool ox)
    {
        int u = (ur ? 4 : 0) + (uw ? 2 : 0) + (ux ? 1 : 0);
        int g = (gr ? 4 : 0) + (gw ? 2 : 0) + (gx ? 1 : 0);
        int o = (or ? 4 : 0) + (ow ? 2 : 0) + (ox ? 1 : 0);
        return $"{(ur ? 'r' : '-')}{(uw ? 'w' : '-')}{(ux ? 'x' : '-')}" +
               $"{(gr ? 'r' : '-')}{(gw ? 'w' : '-')}{(gx ? 'x' : '-')}" +
               $"{(or ? 'r' : '-')}{(ow ? 'w' : '-')}{(ox ? 'x' : '-')} ({u}{g}{o})";
    }
}
