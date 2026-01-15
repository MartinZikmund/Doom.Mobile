using Windows.Storage;

namespace UnoDoom.Services;

/// <summary>
/// Information about a WAD file.
/// </summary>
public class WadInfo
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public bool IsBundled { get; init; }
    public long FileSize { get; init; }

    public string FileSizeText => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:F1} KB",
        _ => $"{FileSize / (1024.0 * 1024.0):F1} MB"
    };
}

/// <summary>
/// Manages WAD file discovery and storage.
/// </summary>
public class WadManager
{
    private static readonly string[] SupportedIwadNames = new[]
    {
        "doom2.wad",
        "plutonia.wad",
        "tnt.wad",
        "doom.wad",
        "doom1.wad",
        "freedoom2.wad",
        "freedoom1.wad"
    };

    private const string UserWadsFolderName = "UserWads";
    private const string BundledWadsFolderName = "Wads";

    /// <summary>
    /// Gets all available WAD files (bundled + user-added).
    /// </summary>
    public async Task<List<WadInfo>> GetAvailableWadsAsync()
    {
        var wads = new List<WadInfo>();

        // Get bundled WADs
        var bundledWads = await GetBundledWadsAsync();
        wads.AddRange(bundledWads);

        // Get user WADs
        var userWads = await GetUserWadsAsync();
        wads.AddRange(userWads);

        return wads;
    }

    /// <summary>
    /// Gets bundled WAD files from the app assets.
    /// </summary>
    private async Task<List<WadInfo>> GetBundledWadsAsync()
    {
        var wads = new List<WadInfo>();

#if __WASM__ || __ANDROID__ || __IOS__
        // On mobile/WASM, bundled WADs are copied to LocalFolder/Wads
        try
        {
            var localFolder = ApplicationData.Current.LocalFolder;
            var wadsFolder = await localFolder.TryGetItemAsync(BundledWadsFolderName) as StorageFolder;

            if (wadsFolder != null)
            {
                var files = await wadsFolder.GetFilesAsync();
                foreach (var file in files)
                {
                    if (IsWadFile(file.Name))
                    {
                        var props = await file.GetBasicPropertiesAsync();
                        wads.Add(new WadInfo
                        {
                            FileName = file.Name,
                            FullPath = file.Path,
                            IsBundled = true,
                            FileSize = (long)props.Size
                        });
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }
#else
        // On desktop, check Assets folder
        try
        {
            var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            if (Directory.Exists(assetsPath))
            {
                foreach (var name in SupportedIwadNames)
                {
                    var path = Path.Combine(assetsPath, name);
                    if (File.Exists(path))
                    {
                        var fileInfo = new FileInfo(path);
                        wads.Add(new WadInfo
                        {
                            FileName = name,
                            FullPath = path,
                            IsBundled = true,
                            FileSize = fileInfo.Length
                        });
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }
#endif

        return wads;
    }

    /// <summary>
    /// Gets user-added WAD files from LocalFolder/UserWads.
    /// </summary>
    private async Task<List<WadInfo>> GetUserWadsAsync()
    {
        var wads = new List<WadInfo>();

        try
        {
            var userWadsFolder = await GetOrCreateUserWadsFolderAsync();
            var files = await userWadsFolder.GetFilesAsync();

            foreach (var file in files)
            {
                if (IsWadFile(file.Name))
                {
                    var props = await file.GetBasicPropertiesAsync();
                    wads.Add(new WadInfo
                    {
                        FileName = file.Name,
                        FullPath = file.Path,
                        IsBundled = false,
                        FileSize = (long)props.Size
                    });
                }
            }
        }
        catch
        {
            // Ignore errors - folder may not exist yet
        }

        return wads;
    }

    /// <summary>
    /// Copies a WAD file to the user WADs folder.
    /// </summary>
    /// <returns>The path to the copied file.</returns>
    public async Task<string> CopyWadToLocalStorageAsync(StorageFile file)
    {
        var userWadsFolder = await GetOrCreateUserWadsFolderAsync();
        var copiedFile = await file.CopyAsync(userWadsFolder, file.Name, NameCollisionOption.ReplaceExisting);
        return copiedFile.Path;
    }

    /// <summary>
    /// Deletes a user-added WAD file.
    /// </summary>
    /// <returns>True if the file was deleted successfully.</returns>
    public async Task<bool> DeleteUserWadAsync(string fileName)
    {
        try
        {
            var userWadsFolder = await GetOrCreateUserWadsFolderAsync();
            var file = await userWadsFolder.TryGetItemAsync(fileName) as StorageFile;

            if (file != null)
            {
                await file.DeleteAsync();
                return true;
            }
        }
        catch
        {
            // Ignore errors
        }

        return false;
    }

    /// <summary>
    /// Gets or creates the user WADs folder.
    /// </summary>
    private async Task<StorageFolder> GetOrCreateUserWadsFolderAsync()
    {
        var localFolder = ApplicationData.Current.LocalFolder;
        return await localFolder.CreateFolderAsync(UserWadsFolderName, CreationCollisionOption.OpenIfExists);
    }

    /// <summary>
    /// Checks if a file is a WAD file based on extension.
    /// </summary>
    private static bool IsWadFile(string fileName)
    {
        return fileName.EndsWith(".wad", StringComparison.OrdinalIgnoreCase);
    }
}
