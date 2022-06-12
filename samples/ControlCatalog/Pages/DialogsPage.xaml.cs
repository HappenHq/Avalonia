using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Dialogs;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Platform.Storage.FileIO;

#pragma warning disable CS0618 // Type or member is obsolete
#nullable enable

namespace ControlCatalog.Pages
{
    public class DialogsPage : UserControl
    {
        public DialogsPage()
        {
            this.InitializeComponent();
            
            var results = this.Get<ItemsPresenter>("PickerLastResults");
            var resultsVisible = this.Get<TextBlock>("PickerLastResultsVisible");
            var bookmarkContainer = this.Get<TextBox>("BookmarkContainer");
            var openedFileContent = this.Get<TextBox>("OpenedFileContent");

            IStorageFolder? lastSelectedDirectory = null;

            List<FileDialogFilter> GetFilters()
            {
                if (this.Get<CheckBox>("UseFilters").IsChecked != true)
                    return new List<FileDialogFilter>();
                return  new List<FileDialogFilter>
                {
                    new FileDialogFilter
                    {
                        Name = "Text files (.txt)", Extensions = new List<string> {"txt"}
                    },
                    new FileDialogFilter
                    {
                        Name = "All files",
                        Extensions = new List<string> {"*"}
                    }
                };
            }

            List<FilePickerFileType>? GetFileTypes()
            {
                if (this.Get<CheckBox>("UseFilters").IsChecked != true)
                    return null;
                return new List<FilePickerFileType>
                {
                    FilePickerFileTypes.All,
                    FilePickerFileTypes.TextPlain
                };
            }

            this.Get<Button>("OpenFile").Click += async delegate
            {
                // Almost guaranteed to exist
                var uri = Assembly.GetEntryAssembly()?.GetModules().FirstOrDefault()?.FullyQualifiedName;
                var initialFileName = uri == null ? null : System.IO.Path.GetFileName(uri);
                var initialDirectory = uri == null ? null : System.IO.Path.GetDirectoryName(uri);

                var result = await new OpenFileDialog()
                {
                    Title = "Open file",
                    Filters = GetFilters(),
                    Directory = initialDirectory,
                    InitialFileName = initialFileName
                }.ShowAsync(GetWindow());
                results.Items = result;
                resultsVisible.IsVisible = result?.Any() == true;
            };
            this.Get<Button>("OpenMultipleFiles").Click += async delegate
            {
                var result = await new OpenFileDialog()
                {
                    Title = "Open multiple files",
                    Filters = GetFilters(),
                    Directory = lastSelectedDirectory?.TryGetUri(out var path) == true ? path.LocalPath : null,
                    AllowMultiple = true
                }.ShowAsync(GetWindow());
                results.Items = result;
                resultsVisible.IsVisible = result?.Any() == true;
            };
            this.Get<Button>("SaveFile").Click += async delegate
            {
                var result = await new SaveFileDialog()
                {
                    Title = "Save file",
                    Filters = GetFilters(),
                    Directory = lastSelectedDirectory?.TryGetUri(out var path) == true ? path.LocalPath : null,
                    InitialFileName = "test.txt"
                }.ShowAsync(GetWindow());
                results.Items = new[] { result };
                resultsVisible.IsVisible = result != null;
            };
            this.Get<Button>("SelectFolder").Click += async delegate
            {
                var result = await new OpenFolderDialog()
                {
                    Title = "Select folder",
                    Directory = lastSelectedDirectory?.TryGetUri(out var path) == true ? path.LocalPath : null
                }.ShowAsync(GetWindow());
                lastSelectedDirectory = new BclStorageFolder(new System.IO.DirectoryInfo(result));
                results.Items = new [] { result };
                resultsVisible.IsVisible = result != null;
            };
            this.Get<Button>("OpenBoth").Click += async delegate
            {
                var result = await new OpenFileDialog()
                {
                    Title = "Select both",
                    Directory = lastSelectedDirectory?.TryGetUri(out var path) == true ? path.LocalPath : null,
                    AllowMultiple = true
                }.ShowManagedAsync(GetWindow(), new ManagedFileDialogOptions
                {
                    AllowDirectorySelection = true
                });
                results.Items = result;
                resultsVisible.IsVisible = result?.Any() == true;
            };
            this.Get<Button>("DecoratedWindow").Click += delegate
            {
                new DecoratedWindow().Show();
            };
            this.Get<Button>("DecoratedWindowDialog").Click += delegate
            {
                _ = new DecoratedWindow().ShowDialog(GetWindow());
            };
            this.Get<Button>("Dialog").Click += delegate
            {
                var window = CreateSampleWindow();
                window.Height = 200;
                _ = window.ShowDialog(GetWindow());
            };
            this.Get<Button>("DialogNoTaskbar").Click += delegate
            {
                var window = CreateSampleWindow();
                window.Height = 200;
                window.ShowInTaskbar = false;
                _ = window.ShowDialog(GetWindow());
            };
            this.Get<Button>("OwnedWindow").Click += delegate
            {
                var window = CreateSampleWindow();

                window.Show(GetWindow());
            };

            this.Get<Button>("OwnedWindowNoTaskbar").Click += delegate
            {
                var window = CreateSampleWindow();

                window.ShowInTaskbar = false;

                window.Show(GetWindow());
            };

            this.Get<Button>("OpenFilePicker").Click += async delegate
            {
                var result = await GetStorageProvider().OpenFilePickerAsync(new FilePickerOpenOptions()
                {
                    Title = "Open file",
                    FileTypeFilter = GetFileTypes(),
                    SuggestedStartLocation = lastSelectedDirectory
                });
                var mappedResults = result.Select(FullPathOrName).ToList();
                resultsVisible.IsVisible = result.Any();
                bookmarkContainer.Text = result.FirstOrDefault(f => f.CanBookmark) is { } f ? await f.SaveBookmark() : "Can't bookmark";
                
                if (result.FirstOrDefault() is { } file)
                {
                    var props = await file.GetBasicPropertiesAsync();
                    openedFileContent.Text = @$"Single file
CanOpenRead: {file.CanOpenRead}
CanOpenWrite: {file.CanOpenWrite}
CanBookmark: {file.CanBookmark}
Size: {props.Size}
DateCreated: {props.DateCreated}
DateModified: {props.DateModified}
Content:
";
                    if (file.CanOpenRead)
                    {
                        using var stream = await file.OpenRead();
                        using var reader = new System.IO.StreamReader(stream);

                        openedFileContent.Text += reader.ReadToEnd();
                    }

                    lastSelectedDirectory = await file.GetParentAsync();
                    if (lastSelectedDirectory is not null)
                    {
                        mappedResults.Insert(0, FullPathOrName(lastSelectedDirectory));
                    }
                }
                results.Items = mappedResults;
            };
            this.Get<Button>("OpenMultipleFilesPicker").Click += async delegate
            {
                var result = await GetStorageProvider().OpenFilePickerAsync(new FilePickerOpenOptions()
                {
                    Title = "Open multiple file",
                    FileTypeFilter = GetFileTypes(),
                    AllowMultiple = true,
                    SuggestedStartLocation = lastSelectedDirectory
                });
                var mappedResults = result.Select(FullPathOrName).ToList();
                resultsVisible.IsVisible = result?.Any() == true;
                bookmarkContainer.Text = string.Empty;

                if (result.FirstOrDefault() is { CanOpenRead: true } file)
                {
                    using var stream = await file.OpenRead();
                    using var reader = new System.IO.StreamReader(stream);
                    openedFileContent.Text = reader.ReadToEnd();
                    
                    lastSelectedDirectory = await file.GetParentAsync();
                    if (lastSelectedDirectory is not null)
                    {
                        mappedResults.Insert(0, FullPathOrName(lastSelectedDirectory));
                    }
                }
                results.Items = mappedResults;
            };
            this.Get<Button>("SaveFilePicker").Click += async delegate
            {
                var file = await GetStorageProvider().SaveFilePickerAsync(new FilePickerSaveOptions()
                {
                    Title = "Save file",
                    FileTypeChoices = GetFileTypes(),
                    SuggestedStartLocation = lastSelectedDirectory
                });
                results.Items = new[] { file }.Where(f => f != null).Select(FullPathOrName).ToArray();
                resultsVisible.IsVisible = file is not null;
                bookmarkContainer.Text = file?.CanBookmark == true ? await file.SaveBookmark() : "Can't bookmark";

                if (file is not null && file.CanOpenWrite)
                {
                    using var stream = await file.OpenWrite();
                    using var reader = new System.IO.StreamWriter(stream);
                    reader.WriteLine(openedFileContent.Text);
                    
                    lastSelectedDirectory = await file.GetParentAsync();
                }
            };
            this.Get<Button>("OpenFolderPicker").Click += async delegate
            {
                var folder = await GetStorageProvider().OpenFolderPickerAsync(new FolderPickerOpenOptions()
                {
                    Title = "Folder file",
                    SuggestedStartLocation = lastSelectedDirectory
                });
                lastSelectedDirectory = folder;
                results.Items = new[] { folder }.Where(f => f != null).Select(FullPathOrName).ToArray();
                resultsVisible.IsVisible = folder is not null;
                bookmarkContainer.Text = folder?.CanBookmark == true ? await folder.SaveBookmark() : "Can't bookmark";

                if (folder is not null)
                {
                    var props = await folder.GetBasicPropertiesAsync();
                    openedFileContent.Text = @$"Folder
CanBookmark: {folder.CanBookmark}
Size: {props.Size}
DateCreated: {props.DateCreated}
DateModified: {props.DateModified}";
                }
            };
            this.Get<Button>("OpenFileFromBookmark").Click += async delegate
            {
                var file = bookmarkContainer.Text is not null
                    ? await GetStorageProvider().OpenFileBookmarkAsync(bookmarkContainer.Text)
                    : null;
                results.Items = new[] { file }.Where(f => f != null).Select(FullPathOrName).ToArray();
                resultsVisible.IsVisible = file is not null;

                if (file?.CanOpenRead == true)
                {
                    using var stream = await file.OpenRead();
                    using var reader = new System.IO.StreamReader(stream);
                    openedFileContent.Text = reader.ReadToEnd();
                }
            };
            this.Get<Button>("OpenFolderFromBookmark").Click += async delegate
            {
                var folder = bookmarkContainer.Text is not null
                    ? await GetStorageProvider().OpenFolderBookmarkAsync(bookmarkContainer.Text)
                    : null;
                lastSelectedDirectory = folder;
                results.Items = new[] { folder }.Where(f => f != null).Select(FullPathOrName).ToArray();
                resultsVisible.IsVisible = folder is not null;

                openedFileContent.Text = string.Empty;
            };
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            var openedFileContent = this.Get<TextBox>("OpenedFileContent");
            try
            {
                var storageProvider = GetStorageProvider();
                openedFileContent.Text = $@"CanOpen: {storageProvider.CanOpen}
CanSave: {storageProvider.CanSave}
CanPickFolder: {storageProvider.CanPickFolder}";
            }
            catch (Exception ex)
            {
                openedFileContent.Text = "Storage provider is not available: " + ex.Message;
            }
        }

        private Window CreateSampleWindow()
        {
            Button button;
            Button dialogButton;
            
            var window = new Window
            {
                Height = 200,
                Width = 200,
                Content = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = "Hello world!" },
                        (button = new Button
                        {
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Content = "Click to close",
                            IsDefault = true
                        }),
                        (dialogButton = new Button
                        {
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Content = "Dialog",
                            IsDefault = false
                        })
                    }
                },
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            button.Click += (_, __) => window.Close();
            dialogButton.Click += (_, __) =>
            {
                var dialog = CreateSampleWindow();
                dialog.Height = 200;
                dialog.ShowDialog(window);
            };

            return window;
        }

        private IStorageProvider GetStorageProvider()
        {
            var forceManaged = this.Get<CheckBox>("ForceManaged").IsChecked ?? false;
            return forceManaged
                ? new ManagedStorageProvider<Window>(GetWindow(), null)
                : GetTopLevel().StorageProvider;
        }

        private static string FullPathOrName(IStorageItem? item)
        {
            if (item is null) return "(null)";
            return item.TryGetUri(out var uri) ? uri.ToString() : item.Name;
        }
        
        Window GetWindow() => this.VisualRoot as Window ?? throw new NullReferenceException("Invalid Owner");
        TopLevel GetTopLevel() => this.VisualRoot as TopLevel ?? throw new NullReferenceException("Invalid Owner");

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
