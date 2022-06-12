using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Logging;
using Avalonia.Platform.Storage;
using UIKit;
using MobileCoreServices;
using Foundation;

#nullable enable

namespace Avalonia.iOS.Storage;

internal class IOSStorageProvider : IStorageProvider
{
    private readonly UIViewController _uiViewController;
    public IOSStorageProvider(UIViewController uiViewController)
    {
        _uiViewController = uiViewController;
    }

    public bool CanOpen => true;

    public bool CanSave => false;

    public bool CanPickFolder => true;

    public async Task<IReadOnlyList<IStorageFile>> OpenFilePickerAsync(FilePickerOpenOptions options)
    {
        var allowedUtis = options.FileTypeFilter?.SelectMany(f => f.AppleUniformTypeIdentifiers ?? Array.Empty<string>())
            .ToArray() ?? new[]
        {
            UTType.Content,
            UTType.Item,
            "public.data"
        };

        // Use Open instead of Import so that we can attempt to use the original file.
        // If the file is from an external provider, then it will be downloaded.
        using var documentPicker = new UIDocumentPickerViewController(allowedUtis, UIDocumentPickerMode.Open)
        {
            DirectoryUrl = GetUrlFromFolder(options.SuggestedStartLocation)
        };

        if (OperatingSystem.IsIOSVersionAtLeast(11, 0))
        {
            documentPicker.AllowsMultipleSelection = options.AllowMultiple;
        }

        var urls = await ShowPicker(documentPicker);
        return urls.Select(u => new IOSStorageFile(u)).ToArray();
    }

    public Task<IStorageBookmarkFile?> OpenFileBookmarkAsync(string bookmark)
    {
        return Task.FromResult<IStorageBookmarkFile?>(GetBookmarkedUrl(bookmark) is { } url
            ? new IOSStorageFile(url) : null);
    }

    public Task<IStorageBookmarkFolder?> OpenFolderBookmarkAsync(string bookmark)
    {
        return Task.FromResult<IStorageBookmarkFolder?>(GetBookmarkedUrl(bookmark) is { } url
            ? new IOSStorageFolder(url) : null);
    }

    public Task<IStorageFile?> SaveFilePickerAsync(FilePickerSaveOptions options)
    {
        return Task.FromException<IStorageFile?>(
            new PlatformNotSupportedException("Save file picker is not supported by iOS"));
    }

    public async Task<IStorageFolder?> OpenFolderPickerAsync(FolderPickerOpenOptions options)
    {
        NSUrl? folderUrl;
        using (var documentPicker = new UIDocumentPickerViewController(new string[] { UTType.Folder }, UIDocumentPickerMode.Open)
               {
                   DirectoryUrl = GetUrlFromFolder(options.SuggestedStartLocation)
               })
        {
            var urls = await ShowPicker(documentPicker);
            folderUrl = urls.FirstOrDefault();
        }

        return folderUrl is null ? null : new IOSStorageFolder(folderUrl);
    }

    private static NSUrl? GetUrlFromFolder(IStorageFolder? folder)
    {
        return folder is IOSStorageFolder iosFolder
            ? iosFolder.Url
            : folder?.TryGetUri(out var fullPath) == true ? fullPath : null;
    }

    private Task<NSUrl[]> ShowPicker(UIDocumentPickerViewController documentPicker)
    {
        var tcs = new TaskCompletionSource<NSUrl[]>();
        documentPicker.Delegate = new PickerDelegate(urls => tcs.TrySetResult(urls));

        if (documentPicker.PresentationController != null)
        {
            documentPicker.PresentationController.Delegate =
                new UIPresentationControllerDelegate(() => tcs.TrySetResult(Array.Empty<NSUrl>()));
        }

        _uiViewController.PresentViewController(documentPicker, true, null);
        return tcs.Task;
    }

    private NSUrl? GetBookmarkedUrl(string bookmark)
    {
        var url = NSUrl.FromBookmarkData(new NSData(bookmark, NSDataBase64DecodingOptions.None),
            NSUrlBookmarkResolutionOptions.WithoutUI, null, out var isStale, out var error);
        if (isStale)
        {
            Logger.TryGet(LogEventLevel.Warning, LogArea.IOSPlatform)?.Log(this, "Stale bookmark detected");
        }
            
        if (error != null)
        {
            throw new NSErrorException(error);
        }
        return url;
    }
        
    private class PickerDelegate : UIDocumentPickerDelegate
    {
        private readonly Action<NSUrl[]>? _pickHandler;

        internal PickerDelegate(Action<NSUrl[]> pickHandler)
            => _pickHandler = pickHandler;

        public override void WasCancelled(UIDocumentPickerViewController controller)
            => _pickHandler?.Invoke(Array.Empty<NSUrl>());

        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
            => _pickHandler?.Invoke(urls);

        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl url)
            => _pickHandler?.Invoke(new[] { url });
    }

    private class UIPresentationControllerDelegate : UIAdaptivePresentationControllerDelegate
    {
        private Action? _dismissHandler;

        internal UIPresentationControllerDelegate(Action dismissHandler)
            => this._dismissHandler = dismissHandler;

        public override void DidDismiss(UIPresentationController presentationController)
        {
            _dismissHandler?.Invoke();
            _dismissHandler = null;
        }

        protected override void Dispose(bool disposing)
        {
            _dismissHandler?.Invoke();
            base.Dispose(disposing);
        }
    }
}