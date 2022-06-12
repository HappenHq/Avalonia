using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Logging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Platform.Storage.FileIO;

using Tmds.DBus;

namespace Avalonia.FreeDesktop
{
    internal class DBusStorageProvider : BclStorageProvider
    {
        private static readonly Lazy<IFileChooser?> s_fileChooser = new(() =>
        {
            var fileChooser = DBusHelper.Connection?.CreateProxy<IFileChooser>("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop");
            if (fileChooser is null)
                return null;
            try
            {
                _ = fileChooser.GetVersionAsync().GetAwaiter().GetResult();
                return fileChooser;
            }
            catch (Exception e)
            {
                Logger.TryGet(LogEventLevel.Error, LogArea.X11Platform)?.Log(null, $"Unable to connect to org.freedesktop.portal.Desktop: {e.Message}");
                return null;
            }
        });

        internal static DBusStorageProvider? TryCreate(IPlatformHandle handle)
        {
            return handle.HandleDescriptor == "XID" && s_fileChooser.Value is { } fileChooser
                ? new DBusStorageProvider(fileChooser, handle) : null;
        }

        private readonly IFileChooser _fileChooser;
        private readonly IPlatformHandle _handle;

        private DBusStorageProvider(IFileChooser fileChooser, IPlatformHandle handle)
        {
            _fileChooser = fileChooser;
            _handle = handle;
        }

        public override bool CanOpen => true;

        public override bool CanSave => true;

        public override bool CanPickFolder => true;
        
        public override async Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options)
        {
            var parentWindow = $"x11:{_handle.Handle:X}";
            ObjectPath objectPath;
            var chooserOptions = new Dictionary<string, object>();
            var filters = ParseFilters(options.FileTypeFilter);
            if (filters.Any())
            {
                chooserOptions.Add("filters", filters);
            }

            chooserOptions.Add("multiple", options.AllowMultiple);

            objectPath = await _fileChooser.OpenFileAsync(parentWindow, options.Title ?? string.Empty, chooserOptions);

            var request = DBusHelper.Connection!.CreateProxy<IRequest>("org.freedesktop.portal.Request", objectPath);
            var tsc = new TaskCompletionSource<string[]?>();
            using var disposable = await request.WatchResponseAsync(x => tsc.SetResult(x.results["uris"] as string[]), tsc.SetException);
            var uris = await tsc.Task ?? Array.Empty<string>();

            return uris.Select(path => new BclStorageFile(new FileInfo(new Uri(path).AbsolutePath))).ToList();
        }

        public override async Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options)
        {
            var parentWindow = $"x11:{_handle.Handle:X}";
            ObjectPath objectPath;
            var chooserOptions = new Dictionary<string, object>();
            var filters = ParseFilters(options.FileTypeChoices);
            if (filters.Any())
            {
                chooserOptions.Add("filters", filters);
            }

            if (options.SuggestedFileName is { } currentName)
                chooserOptions.Add("current_name", currentName);
            if (options.SuggestedStartLocation?.TryGetUri(out var currentFolder) == true)
                chooserOptions.Add("current_folder", Encoding.UTF8.GetBytes(currentFolder.LocalPath));
            objectPath = await _fileChooser.SaveFileAsync(parentWindow, options.Title ?? string.Empty, chooserOptions);

            var request = DBusHelper.Connection!.CreateProxy<IRequest>("org.freedesktop.portal.Request", objectPath);
            var tsc = new TaskCompletionSource<string[]?>();
            using var disposable = await request.WatchResponseAsync(x => tsc.SetResult(x.results["uris"] as string[]), tsc.SetException);
            var uris = await tsc.Task;
            var path = uris?.FirstOrDefault() is { } filePath ? new Uri(filePath).AbsolutePath : null;

            return path is null ? null : new BclStorageFile(new FileInfo(path));
        }

        public override async Task<IStorageFolder?> OpenFolderPickerAsync(FolderPickerOpenOptions options)
        {
            var parentWindow = $"x11:{_handle.Handle:X}";
            var chooserOptions = new Dictionary<string, object>
            {
                { "directory", true }
            };
            var objectPath = await _fileChooser.OpenFileAsync(parentWindow, options.Title ?? string.Empty, chooserOptions);
            var request = DBusHelper.Connection!.CreateProxy<IRequest>("org.freedesktop.portal.Request", objectPath);
            var tsc = new TaskCompletionSource<string[]?>();
            using var disposable = await request.WatchResponseAsync(x => tsc.SetResult(x.results["uris"] as string[]), tsc.SetException);
            var uris = await tsc.Task;
            var path = uris is null || uris.Length != 1 ? null : new Uri(uris[0]).AbsolutePath;

            return path is null ? null : (IStorageFolder)new BclStorageFolder(new DirectoryInfo(path));
        }

        private static (string name, (uint style, string extension)[])[] ParseFilters(IReadOnlyList<FilePickerFileType>? fileTypes)
        {
            // Example: [('Images', [(0, '*.ico'), (1, 'image/png')]), ('Text', [(0, '*.txt')])]

            if (fileTypes is null)
            {
                return Array.Empty<(string name, (uint style, string extension)[])>();
            }

            var filters = new List<(string name, (uint style, string extension)[])>();
            foreach (var fileType in fileTypes)
            {
                const uint globStyle = 0u;
                const uint mimeStyle = 1u;

                if (fileType.Extensions is { } extensions)
                {
                    filters.Add((
                        fileType.Name,
                        extensions.Select(static x => (globStyle, x)).ToArray()
                    ));
                }
                else if (fileType.MimeTypes is { } mimeTypes)
                {
                    filters.Add((
                        fileType.Name,
                        mimeTypes.Select(static x => (mimeStyle, x)).ToArray()
                    ));
                }
            }

            return filters.ToArray();
        }
    }
}
