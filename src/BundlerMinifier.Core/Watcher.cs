using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BundlerMinifier
{
    public class Watcher
    {
        private static readonly List<ChangeHandler> ChangeHandlers = new List<ChangeHandler>();
        private static FileSystemWatcher _listener;
        private static string _configPath;
        private static bool _watchingAll;
        private static BundleFileProcessor _processor;

        public static bool Configure(BundleFileProcessor processor, List<string> configurations, string configPath)
        {
            _processor = processor;

            IEnumerable<Bundle> bundles;

            if (!BundleHandler.TryGetBundles(configPath, out bundles))
            {
                return false;
            }

            // the baseline a later config change is compared against
            BundleInputTracker.Prime(bundles);

            if (configurations.Count > 0)
            {
                foreach (string config in configurations)
                {
                    Bundle bundle = bundles.FirstOrDefault(x => string.Equals(x.OutputFileName, config, StringComparison.OrdinalIgnoreCase));

                    if (bundle != null)
                    {
                        ChangeHandlers.Add(new ChangeHandler(processor, configPath, bundle));
                    }
                }
            }
            else
            {
                foreach (Bundle bundle in bundles)
                {
                    ChangeHandlers.Add(new ChangeHandler(processor, configPath, bundle));
                }

                _watchingAll = true;
            }

            if (ChangeHandlers.Count > 0)
            {
                ConfigureWatcher(configPath);
            }

            return ChangeHandlers.Count > 0;
        }

        private static void ConfigureWatcher(string configPath)
        {
            _configPath = configPath;
            string basePath = new FileInfo(configPath).Directory.FullName;
            var fsw = new FileSystemWatcher(basePath);

            fsw.Changed += FilesChanged;
            fsw.Renamed += FilesChanged;

            fsw.IncludeSubdirectories = true;
            fsw.NotifyFilter = NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName;
            fsw.EnableRaisingEvents = true;
            _listener = fsw;
        }

        public static void Stop()
        {
            FileSystemWatcher fsw = _listener;

            if (fsw != null)
            {
                _listener = null;
                fsw.Changed -= FilesChanged;
                fsw.Changed -= FilesChanged;

                try
                {
                    fsw.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private static async void FilesChanged(object sender, FileSystemEventArgs e)
        {
            const int maxRetries = 10;
            var fsw = (FileSystemWatcher)sender;
            fsw.EnableRaisingEvents = false;
            int retries = 0;
            bool suppressOutputMessage = false;

            while (retries < maxRetries)
            {
                await Task.Delay(100);

                try
                {
                    if (string.Equals(e.FullPath, _configPath, StringComparison.OrdinalIgnoreCase))
                    {
                        bool changed = ReloadConfig();
                        suppressOutputMessage = !changed;

                        if (changed)
                        {
                            Console.WriteLine("Configuration reloaded".Green().Bright());
                        }
                    }
                    else
                    {
                        bool anyRan = false;

                        foreach (ChangeHandler handler in ChangeHandlers)
                        {
                            anyRan |= handler.FilesChanged(e);
                        }

                        if (!anyRan)
                        {
                            suppressOutputMessage = true;
                        }
                    }

                    break;
                }
                catch
                {
                    ++retries;
                }
            }

            if (retries >= maxRetries)
            {
                Console.WriteLine("An error occurred while processing".Red().Bright());
            }

            fsw.EnableRaisingEvents = true;

            if (!suppressOutputMessage)
            {
                Console.WriteLine("Watching... Press [Enter] to stop".LightGray().Bright());
            }
        }

        private static bool ReloadConfig()
        {
            bool anyChanges = false;
            IEnumerable<Bundle> bundles;

            if (!BundleHandler.TryGetBundles(_configPath, out bundles))
            {
                throw new Exception("Unable to load bundles.");
            }

            var oldHandlers = ChangeHandlers.ToList();

            if (!_watchingAll)
            {
                foreach (ChangeHandler handler in oldHandlers)
                {
                    Bundle bundle = bundles.FirstOrDefault(x => string.Equals(x.OutputFileName, handler.Bundle.OutputFileName, StringComparison.OrdinalIgnoreCase));

                    if (bundle != null)
                    {
                        ChangeHandler newHandler = new ChangeHandler(_processor, bundle.FileName, bundle);

                        if (!newHandler.Equals(handler))
                        {
                            ChangeHandlers.Remove(handler);
                            ChangeHandlers.Add(newHandler);
                            _processor.Process(_configPath, new[] { bundle });
                            anyChanges = true;
                        }
                    }
                    else
                    {
                        RemoveHandler(handler, bundles);
                        Console.WriteLine($"Cannot find configuration {handler.Bundle.OutputFileName}".Orange().Bright());
                    }
                }
            }
            else
            {
                HashSet<Bundle> bundlesToProcess = new HashSet<Bundle>(bundles);

                foreach (ChangeHandler handler in oldHandlers)
                {
                    Bundle bundle = bundles.FirstOrDefault(x => string.Equals(x.OutputFileName, handler.Bundle.OutputFileName, StringComparison.OrdinalIgnoreCase));

                    if (bundle != null)
                    {
                        bundlesToProcess.Remove(bundle);
                        ChangeHandler newHandler = new ChangeHandler(_processor, bundle.FileName, bundle);

                        if (!newHandler.Equals(handler))
                        {
                            ChangeHandlers.Remove(handler);
                            ChangeHandlers.Add(newHandler);
                            _processor.Process(_configPath, new[] { bundle });
                            anyChanges = true;
                        }
                    }
                    else
                    {
                        // the bundle was removed from the config file, without this its input files would keep rebuilding it
                        RemoveHandler(handler, bundles);
                        anyChanges = true;
                    }
                }

                foreach (Bundle bundle in bundlesToProcess)
                {
                    ChangeHandlers.Add(new ChangeHandler(_processor, _configPath, bundle));
                    _processor.Process(_configPath, new[] { bundle });
                    anyChanges = true;
                }
            }

            return anyChanges;
        }

        /// <summary>
        /// Stops watching a bundle that is no longer in the config file and deletes the min file it produced.
        /// </summary>
        /// <param name="bundles">The bundles that are in the config file now.</param>
        private static void RemoveHandler(ChangeHandler handler, IEnumerable<Bundle> bundles)
        {
            Bundle removed = handler.Bundle;

            ChangeHandlers.Remove(handler);
            BundleInputTracker.Forget(removed);

            if (removed.IsMinificationEnabled)
                _processor.DeleteMinFilesOfRemovedBundle(removed.FileName, removed.OutputFileName, bundles);
        }
    }
}