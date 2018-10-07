using KaoriYa.Migemo;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Wox.Infrastructure;
using Wox.Infrastructure.Storage;
using Wox.Plugin.MigemoSearch.Everything;
using Wox.Infrastructure.Logger;
using System.Threading.Tasks;
using System.Threading;

namespace Wox.Plugin.MigemoSearch
{
    public class Main : IPlugin, ISettingProvider, IPluginI18n, IContextMenu, ISavable
    {
        private readonly EverythingAPI _everything_api = new EverythingAPI();

        public const string EVERYTHING_DLL = "Everything.dll";

        private Migemo _migemo_api;

        public const string MIGEMO_DLL = "migemo.dll";

        private PluginInitContext _context;

        private Settings _settings;
        private PluginJsonStorage<Settings> _storage;

        private Query _savedQuery;

        public void Save()
        {
            _storage.Save();
        }

        public List<Result> QueryAsync(Query query)
        {
            lock (_everything_api)
            {
                _savedQuery = query;
            }

            var task = Task.Run(() => {
                Thread.Sleep(200);
                Query saved = null;
                lock (_everything_api)
                {
                    saved = _savedQuery;
                }
                return (saved == query) ? QuerySync(saved) : new List<Result>();
            });

            return task.Result;
        }

        private List<Result> QuerySync(Query query)
        { 
            var results = new List<Result>();
            if (!string.IsNullOrEmpty(query.Search))
            {
                var keyword = query.Search;
                

                if (_settings.MaxSearchCount <= 0)
                {
                    _settings.MaxSearchCount = 50;
                }

                try
                {
                    // Migemo Search
                    if (keyword.Length >= 3)
                    {
                        //Log.Info($"|Wox.Plugin.MigemoSearch|Query beafore=<{ keyword }>");
                        Regex regex = _migemo_api.GetRegex(keyword);
                        keyword = "@" + regex.ToString(); // @ is regex prefix
                        //Log.Info($"|Wox.Plugin.MigemoSearch|Query after=<{ keyword }>");
                    }

                    var searchList = _everything_api.Search(keyword, maxCount: _settings.MaxSearchCount).ToList();
                    foreach (var s in searchList)
                    {
                        var path = s.FullPath;

                        string workingDir = null;
                        if (_settings.UseLocationAsWorkingDir)
                            workingDir = Path.GetDirectoryName(path);

                        Result r = new Result();
                        r.Title = Path.GetFileName(path);
                        r.SubTitle = keyword;
                        r.IcoPath = path;
                        r.Action = c =>
                        {
                            bool hide;
                            try
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = path,
                                    UseShellExecute = true,
                                    WorkingDirectory = workingDir
                                });
                                hide = true;
                            }
                            catch (Win32Exception)
                            {
                                var name = $"Plugin: {_context.CurrentPluginMetadata.Name}";
                                var message = "Can't open this file";
                                _context.API.ShowMsg(name, message, string.Empty);
                                hide = false;
                            }
                            return hide;
                        };
                        r.ContextData = s;
                        results.Add(r);
                    }
                }
                catch (IPCErrorException)
                {
                    results.Add(new Result
                    {
                        Title = _context.API.GetTranslation("wox_plugin_migemosearch_is_not_running"),
                        IcoPath = "Images\\warning.png"
                    });
                }
                catch (Exception e)
                {
                    results.Add(new Result
                    {
                        Title = _context.API.GetTranslation("wox_plugin_migemosearch_query_error"),
                        SubTitle = e.Message,
                        Action = _ =>
                        {
                            Clipboard.SetText(e.Message + "\r\n" + e.StackTrace);
                            _context.API.ShowMsg(_context.API.GetTranslation("wox_plugin_migemosearch_copied"), null, string.Empty);
                            return false;
                        },
                        IcoPath = "Images\\error.png"
                    });
                }
            }

            _everything_api.Reset();

            return results;
        }

        [DllImport("kernel32.dll")]
        private static extern int LoadLibrary(string name);

        private List<ContextMenu> GetDefaultContextMenu()
        {
            List<ContextMenu> defaultContextMenus = new List<ContextMenu>();
            ContextMenu openFolderContextMenu = new ContextMenu
            {
                Name = _context.API.GetTranslation("wox_plugin_migemosearch_open_containing_folder"),
                Command = "explorer.exe",
                Argument = " /select,\"{path}\"",
                ImagePath = "Images\\folder.png"
            };

            defaultContextMenus.Add(openFolderContextMenu);
            return defaultContextMenus;
        }

        public void Init(PluginInitContext context)
        {
            Log.Info($"|Wox.Plugin.MigemoSearch|Init started.");
            _context = context;
            _storage = new PluginJsonStorage<Settings>();
            _settings = _storage.Load();

            var pluginDirectory = context.CurrentPluginMetadata.PluginDirectory;
            const string sdk = "EverythingSDK";
            var bundledSDKDirectory = Path.Combine(pluginDirectory, sdk, CpuType());
            var sdkDirectory = Path.Combine(_storage.DirectoryPath, sdk, CpuType());
            Helper.ValidateDataDirectory(bundledSDKDirectory, sdkDirectory);

            var sdkPath = Path.Combine(sdkDirectory, EVERYTHING_DLL);
            Constant.EverythingSDKPath = sdkPath;
            LoadLibrary(sdkPath);

            // Migemo
            pluginDirectory = context.CurrentPluginMetadata.PluginDirectory;
            const string migemoSdk = "MigemoSDK";
            bundledSDKDirectory = Path.Combine(pluginDirectory, migemoSdk, CpuType());
            sdkDirectory = Path.Combine(_storage.DirectoryPath, migemoSdk, CpuType());
            Helper.ValidateDataDirectory(bundledSDKDirectory, sdkDirectory);
            sdkPath = Path.Combine(sdkDirectory, "migemo.dll");
            Log.Info($"|Wox.Plugin.MigemoSearch|pluginDirectory=<{pluginDirectory}>, bundledSDKDirectory=<{bundledSDKDirectory}>, sdkDirectory=<{sdkDirectory}>, sdkPath=<{sdkPath}>");
            LoadLibrary(sdkPath);

            bundledSDKDirectory = Path.Combine(pluginDirectory, migemoSdk, "dict/cp932");
            sdkDirectory = Path.Combine(_storage.DirectoryPath, migemoSdk, "dict/cp932");
            Helper.ValidateDataDirectory(bundledSDKDirectory, sdkDirectory);
            Log.Info($"|Wox.Plugin.MigemoSearch|dict=<{Path.Combine(_storage.DirectoryPath, migemoSdk, "dict/cp932/migemo-dict")}>");

            _migemo_api = new Migemo(Path.Combine(_storage.DirectoryPath, migemoSdk, "dict/cp932/migemo-dict"));
            //_migemo_api.OperatorNewLine = @"\s*";
            Log.Info($"|Wox.Plugin.MigemoSearch|Init complated.");
        }

        private static string CpuType()
        {
            return Environment.Is64BitOperatingSystem ? "x64" : "x86";
        }

        public string GetTranslatedPluginTitle()
        {
            return _context.API.GetTranslation("wox_plugin_migemosearch_plugin_name");
        }

        public string GetTranslatedPluginDescription()
        {
            return _context.API.GetTranslation("wox_plugin_migemosearch_plugin_description");
        }

        public List<Result> LoadContextMenus(Result selectedResult)
        {
            SearchResult record = selectedResult.ContextData as SearchResult;
            List<Result> contextMenus = new List<Result>();
            if (record == null) return contextMenus;

            List<ContextMenu> availableContextMenus = new List<ContextMenu>();
            availableContextMenus.AddRange(GetDefaultContextMenu());
            availableContextMenus.AddRange(_settings.ContextMenus);

            if (record.Type == ResultType.File)
            {
                foreach (ContextMenu contextMenu in availableContextMenus)
                {
                    var menu = contextMenu;
                    contextMenus.Add(new Result
                    {
                        Title = contextMenu.Name,
                        Action = _ =>
                        {
                            string argument = menu.Argument.Replace("{path}", record.FullPath);
                            try
                            {
                                Process.Start(menu.Command, argument);
                            }
                            catch
                            {
                                _context.API.ShowMsg(string.Format(_context.API.GetTranslation("wox_plugin_migemosearch_canot_start"), record.FullPath), string.Empty, string.Empty);
                                return false;
                            }
                            return true;
                        },
                        IcoPath = contextMenu.ImagePath
                    });
                }
            }

            return contextMenus;
        }

        public Control CreateSettingPanel()
        {
            return new MigemoSearchSettings(_settings);
        }
    }
}
