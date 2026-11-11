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
        private static readonly object PendingLock = new object();
        private static readonly HashSet<string> PendingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _processingPending;

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
            // the full path, as that is what the watcher reports a change of the config file with
            _configPath = new FileInfo(configPath).FullName;
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

        private static void FilesChanged(object sender, FileSystemEventArgs e)
        {
            // The watcher is never switched off while a change is handled, that dropped every file saved in the
            // meantime. The changed files are collected instead and handled by a single worker, batch after batch.
            lock (PendingLock)
            {
                PendingFiles.Add(e.FullPath);

                if (_processingPending)
                    return;

                _processingPending = true;
            }

            Task.Run(ProcessPendingFiles);
        }

        private static async Task ProcessPendingFiles()
        {
            while (true)
            {
                // lets the writes finish, and puts the files that were saved together into the same batch
                await Task.Delay(100);

                bool configChanged;
                List<string> files;

                lock (PendingLock)
                {
                    if (PendingFiles.Count == 0)
                    {
                        _processingPending = false;
                        return;
                    }

                    configChanged = PendingFiles.Remove(_configPath);
                    files = PendingFiles.Where(ChangeHandler.IsFileValid).ToList();
                    PendingFiles.Clear();
                }

                bool showMessage = false;

                // the config file goes first, so the files are matched against the bundles as they are now
                if (configChanged)
                    showMessage |= await Retry(ReloadConfigAndReport);

                if (files.Count > 0)
                    showMessage |= await Retry(() => ProcessFiles(files));

                if (showMessage)
                {
                    Console.WriteLine("Watching... Press [Enter] to stop".LightGray().Bright());
                }
            }
        }

        /// <summary>
        /// Runs the action, trying again for a while when it fails as a file may still be being written.
        /// </summary>
        /// <returns>True when something was reported, be it by the action or as the error it kept failing with.</returns>
        private static async Task<bool> Retry(Func<bool> action)
        {
            const int maxRetries = 10;
            string error = null;

            for (int retries = 0; retries < maxRetries; retries++)
            {
                if (retries > 0)
                    await Task.Delay(100);

                try
                {
                    return action();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }

            Console.WriteLine($"An error occurred while processing: {error}".Red().Bright());

            return true;
        }

        private static bool ReloadConfigAndReport()
        {
            bool changed = ReloadConfig();

            if (changed)
            {
                Console.WriteLine("Configuration reloaded".Green().Bright());
            }

            return changed;
        }

        private static bool ProcessFiles(List<string> files)
        {
            bool anyRan = false;

            foreach (ChangeHandler handler in ChangeHandlers)
            {
                anyRan |= handler.FilesChanged(files);
            }

            return anyRan;
        }

        private static bool ReloadConfig()
        {
            bool anyChanges = false;
            IEnumerable<Bundle> bundles;
            string error;

            if (!BundleHandler.TryGetBundles(_configPath, out bundles, out error))
            {
                throw new Exception(string.IsNullOrEmpty(error) ? "Unable to load bundles." : "Unable to load bundles. " + error);
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