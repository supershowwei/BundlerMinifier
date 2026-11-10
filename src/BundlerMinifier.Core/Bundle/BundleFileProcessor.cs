using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace BundlerMinifier
{
    public class BundleFileProcessor
    {
        private static string[] _supported = new[] { ".JS", ".CSS", ".HTML", ".HTM" };

        public static bool IsSupported(params string[] files)
        {
            files = files.Where(f => !string.IsNullOrEmpty(f)).ToArray();

            if (!files.Any()) return false;

            string ext = Path.GetExtension(files.First()).ToUpperInvariant();

            foreach (string file in files)
            {
                string fileExt = Path.GetExtension(file).ToUpperInvariant();

                if (!_supported.Contains(fileExt) || !fileExt.Equals(ext, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        public bool Process(string fileName, IEnumerable<Bundle> bundles = null)
        {
            FileInfo info = new FileInfo(fileName);
            bool result = false;

            if (bundles == null)
            {
                bundles = BundleHandler.GetBundles(fileName);

                // With the whole config file at hand, a bundle remembered from an earlier read that is gone was removed from it
                if (bundles != null)
                    result |= DeleteMinFilesOfRemovedBundles(info.FullName, bundles);
            }

            foreach (Bundle bundle in bundles)
            {
                result |= ProcessBundle(info.Directory.FullName, bundle);
            }

            return result;
        }

        private bool DeleteMinFilesOfRemovedBundles(string configFile, IEnumerable<Bundle> bundles)
        {
            List<string> removed = BundleInputTracker.TakeRemoved(configFile, bundles);

            if (removed == null)
                return false;

            bool deleted = false;

            foreach (string outputFileName in removed)
            {
                deleted |= DeleteMinFilesOfRemovedBundle(configFile, outputFileName, bundles);
            }

            return deleted;
        }

        /// <summary>
        /// Deletes the min file of a bundle that was removed from the config file, along with its source map
        /// and gzip file, unless a bundle that is still in the config file produces that very min file.
        /// </summary>
        /// <param name="bundles">The bundles that are in the config file now.</param>
        internal bool DeleteMinFilesOfRemovedBundle(string configFile, string outputFileName, IEnumerable<Bundle> bundles)
        {
            string folder = new FileInfo(configFile).DirectoryName;
            string minFile = BundleMinifier.GetMinFileName(Path.Combine(folder, outputFileName.NormalizePath()));

            foreach (Bundle bundle in bundles)
            {
                if (minFile.Equals(BundleMinifier.GetMinFileName(bundle.GetAbsoluteOutputFile()), StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return DeleteMinFiles(minFile);
        }

        private bool DeleteMinFiles(string minFile)
        {
            bool deleted = DeleteFile(minFile, minFile);

            deleted |= DeleteFile(minFile, minFile + ".map");
            deleted |= DeleteFile(minFile, minFile + ".gz");

            return deleted;
        }

        private bool DeleteFile(string minFile, string file)
        {
            if (!File.Exists(file))
                return false;

            FileHelpers.RemoveReadonlyFlagFromFile(file);
            File.Delete(file);
            OnAfterDeletingMinFile(minFile, file);

            return true;
        }

        public void Clean(string fileName, IEnumerable<Bundle> bundles = null)
        {
            FileInfo info = new FileInfo(fileName);
            bundles = bundles ?? BundleHandler.GetBundles(fileName);

            foreach (Bundle bundle in bundles)
            {
                CleanBundle(info.Directory.FullName, bundle);
            }
        }

        public void SourceFileChanged(string bundleFile, string sourceFile)
        {
            var bundles = BundleHandler.GetBundles(bundleFile);
            string bundleFileFolder = Path.GetDirectoryName(bundleFile),
                   sourceFileFolder = Path.GetDirectoryName(sourceFile);

            foreach (Bundle bundle in bundles)
            {
                foreach (string input in bundle.GetAbsoluteInputFiles())
                {
                    if (input.Equals(sourceFile, StringComparison.OrdinalIgnoreCase) || input.Equals(sourceFileFolder, StringComparison.OrdinalIgnoreCase))
                        ProcessBundle(bundleFileFolder, bundle);
                }
            }
        }

        /// <summary>
        /// Remembers the inputFiles of the bundles as the config file has them now, so that a later
        /// <see cref="Process"/> in this process can tell which bundles had their inputFiles changed.
        /// Bundles that are already remembered are left untouched.
        /// </summary>
        public static void TrackInputFiles(string configFile)
        {
            IEnumerable<Bundle> bundles;

            if (BundleHandler.TryGetBundles(configFile, out bundles))
                BundleInputTracker.Prime(bundles);
        }

        public static IEnumerable<Bundle> IsFileConfigured(string configFile, string sourceFile)
        {
            List<Bundle> list = new List<Bundle>();

            try
            {
                var configs = BundleHandler.GetBundles(configFile);
                string folder = Path.GetDirectoryName(configFile);

                foreach (Bundle bundle in configs)
                {
                    foreach (string input in bundle.GetAbsoluteInputFiles())
                    {
                        if (input.Equals(sourceFile, StringComparison.OrdinalIgnoreCase) && !list.Contains(bundle))
                            list.Add(bundle);
                    }
                }

                return list;
            }
            catch (Exception)
            {
                return list;
            }
        }

        private bool ProcessBundle(string baseFolder, Bundle bundle)
        {
            OnProcessing(bundle, baseFolder);
            var inputs = bundle.GetAbsoluteInputFiles();
            bool changed = false;

            // Only hashes the inputFiles entries when the config file was written since this bundle was last processed
            ulong inputFilesHash;
            bool inputFilesChanged;
            bool trackInputFiles = BundleInputTracker.Check(bundle, out inputFilesHash, out inputFilesChanged);

            if (bundle.GetAbsoluteInputFiles(true).Count > 1 || bundle.InputFiles.FirstOrDefault() != bundle.OutputFileName)
            {
                BundleHandler.ProcessBundle(baseFolder, bundle);

                if (!bundle.IsMinificationEnabled || !bundle.OutputIsMinFile)
                {
                    string outputFile = bundle.GetAbsoluteOutputFile();
                    bool containsChanges = FileHelpers.HasFileContentChanged(outputFile, bundle.Output);

                    if (containsChanges)
                    {
                        OnBeforeBundling(bundle, baseFolder, containsChanges);
                        DirectoryInfo outputFileDirectory = Directory.GetParent(outputFile);
                        outputFileDirectory.Create();

                        File.WriteAllText(outputFile, bundle.Output, new UTF8Encoding(false));
                        OnAfterBundling(bundle, baseFolder, containsChanges);
                        changed = true;
                    }
                }
            }

            MinificationResult minResult = null;
            var minFile = BundleMinifier.GetMinFileName(bundle.GetAbsoluteOutputFile());

            // Without any input file there is nothing to minify, so what an earlier run left behind
            // is deleted instead of being kept with its outdated content
            if (bundle.IsMinificationEnabled && !bundle.HasInputFiles)
            {
                changed |= DeleteMinFiles(minFile);

                if (trackInputFiles)
                    BundleInputTracker.Commit(bundle, inputFilesHash);

                return changed;
            }

            if (bundle.IsMinificationEnabled)
            {
                var outputWriteTime = File.GetLastWriteTimeUtc(minFile);

                // Removing or reordering entries leaves every input file older than the min file,
                // so a changed inputFiles list has to force the minification
                var minifyChanged = inputFilesChanged || bundle.MostRecentWrite >= outputWriteTime;

                if (minifyChanged)
                {
                    minResult = BundleMinifier.MinifyBundle(bundle);

                    // If no change is detected, then the minFile is not modified, so we need to update the write time manually
                    if (!minResult.Changed && File.Exists(minFile))
                        File.SetLastWriteTimeUtc(minFile, DateTime.UtcNow);
                    changed |= minResult.Changed;

                    if (bundle.SourceMap && !string.IsNullOrEmpty(minResult.SourceMap))
                    {
                        string mapFile = minFile + ".map";
                        bool smChanges = FileHelpers.HasFileContentChanged(mapFile, minResult.SourceMap);

                        if (smChanges)
                        {
                            OnBeforeWritingSourceMap(minFile, mapFile, smChanges);
                            File.WriteAllText(mapFile, minResult.SourceMap, new UTF8Encoding(false));
                            OnAfterWritingSourceMap(minFile, mapFile, smChanges);
                            changed = true;
                        }
                    }
                }
                else
                {
                    OnMinificationSkipped(bundle, baseFolder, false);
                }
            }

            if (bundle.IsGzipEnabled)
            {
                var fileToGzip = bundle.IsMinificationEnabled ?
                    minFile : bundle.GetAbsoluteOutputFile();

                if (minResult == null)
                    BundleMinifier.GzipFile(fileToGzip, bundle, false, File.ReadAllText(fileToGzip));
                else
                    BundleMinifier.GzipFile(fileToGzip, bundle, minResult.Changed, minResult.MinifiedContent);
            }

            if (trackInputFiles)
                BundleInputTracker.Commit(bundle, inputFilesHash);

            return changed;
        }

        private void CleanBundle(string baseFolder, Bundle bundle)
        {
            string outputFile = bundle.GetAbsoluteOutputFile();
            baseFolder = baseFolder.DemandTrailingPathSeparatorChar();
            if (!bundle.GetAbsoluteInputFiles().Contains(outputFile, StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(outputFile))
                {
                    FileHelpers.RemoveReadonlyFlagFromFile(outputFile);
                    File.Delete(outputFile);
                    Console.WriteLine($"Deleted {FileHelpers.MakeRelative(baseFolder, outputFile).Cyan().Bright()}");
                }
            }

            string minFile = BundleMinifier.GetMinFileName(bundle.GetAbsoluteOutputFile());
            string mapFile = minFile + ".map";
            string gzFile = minFile + ".gz";

            if (File.Exists(minFile))
            {
                FileHelpers.RemoveReadonlyFlagFromFile(minFile);
                File.Delete(minFile);
                Console.WriteLine($"Deleted {FileHelpers.MakeRelative(baseFolder, minFile).Cyan().Bright()}");
            }

            if (File.Exists(mapFile))
            {
                FileHelpers.RemoveReadonlyFlagFromFile(mapFile);
                File.Delete(mapFile);
                Console.WriteLine($"Deleted {mapFile.Cyan().Bright()}");
            }

            if (File.Exists(gzFile))
            {
                FileHelpers.RemoveReadonlyFlagFromFile(gzFile);
                File.Delete(gzFile);
                Console.WriteLine($"Deleted {gzFile.Cyan().Bright()}");
            }
        }

        public event EventHandler<BundleFileEventArgs> Processing;
        protected void OnProcessing(Bundle bundle, string baseFolder)
        {
            Processing?.Invoke(this, new BundleFileEventArgs(bundle.GetAbsoluteOutputFile(), bundle, baseFolder, false));
        }

        public event EventHandler<BundleFileEventArgs> BeforeBundling;
        protected void OnBeforeBundling(Bundle bundle, string baseFolder, bool containsChanges)
        {
            BeforeBundling?.Invoke(this, new BundleFileEventArgs(bundle.GetAbsoluteOutputFile(), bundle, baseFolder, containsChanges));
        }


        public event EventHandler<BundleFileEventArgs> AfterBundling;
        protected void OnAfterBundling(Bundle bundle, string baseFolder, bool containsChanges)
        {
            AfterBundling?.Invoke(this, new BundleFileEventArgs(bundle.GetAbsoluteOutputFile(), bundle, baseFolder, containsChanges));
        }

        public event EventHandler<MinifyFileEventArgs> BeforeWritingSourceMap;
        protected void OnBeforeWritingSourceMap(string file, string mapFile, bool containsChanges)
        {
            BeforeWritingSourceMap?.Invoke(this, new MinifyFileEventArgs(file, mapFile, containsChanges));
        }

        public event EventHandler<MinifyFileEventArgs> AfterWritingSourceMap;
        protected void OnAfterWritingSourceMap(string file, string mapFile, bool containsChanges)
        {
            AfterWritingSourceMap?.Invoke(this, new MinifyFileEventArgs(file, mapFile, containsChanges));
        }

        public event EventHandler<BundleFileEventArgs> MinificationSkipped;
        protected void OnMinificationSkipped(Bundle bundle, string baseFolder, bool containsChanges)
        {
            MinificationSkipped?.Invoke(this, new BundleFileEventArgs(bundle.GetAbsoluteOutputFile(), bundle, baseFolder, containsChanges));
        }

        /// <summary>
        /// Raised for a min file, its source map and its gzip file after each of them is deleted because
        /// the bundle was removed from the config file or no longer has any input file.
        /// </summary>
        public event EventHandler<MinifyFileEventArgs> AfterDeletingMinFile;
        protected void OnAfterDeletingMinFile(string minFile, string deletedFile)
        {
            AfterDeletingMinFile?.Invoke(this, new MinifyFileEventArgs(minFile, deletedFile, true));
        }


    }
}
