using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BundlerMinifier;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace BundlerMinifierVsix
{
    static class BundleService
    {
        private static BundleFileProcessor _processor;

        /// <summary>
        /// Read every time instead of being copied once: this class can be first used before the package
        /// has finished loading in the background, e.g. by a bundleconfig.json restored in the editor,
        /// and a copy taken then would stay null for the whole session.
        /// </summary>
        private static DTE2 _dte
        {
            get { return BundlerMinifierPackage._dte; }
        }

        static BundleService()
        {
            BundleMinifier.BeforeWritingMinFile += CheckFileOutOfSourceControl;
            BundleMinifier.AfterWritingMinFile += AfterWritingFile;
            BundleMinifier.BeforeWritingGzipFile += CheckFileOutOfSourceControl;
            BundleMinifier.AfterWritingGzipFile += AfterWritingFile;
            BundleMinifier.ErrorMinifyingFile += ErrorMinifyingFile;
        }

        private static void AfterWritingFile(object sender, MinifyFileEventArgs e)
        {
            if (e.Bundle != null)
            {
                var sourceFile = e.OriginalFile;

                if (e.Bundle.OutputIsMinFile)
                {
                    string ext = Path.GetExtension(sourceFile);
                    var unMinFile = sourceFile.Replace(".min" + ext, ext);
                    if (File.Exists(unMinFile))
                        sourceFile = unMinFile;
                }

                // Bundle file minification
                if (e.Bundle.IncludeInProject)
                    ProjectHelpers.AddNestedFile(sourceFile, e.ResultFile, true);
            }
            else
            {
                // Single file minification
                ProjectHelpers.AddNestedFile(e.OriginalFile, e.ResultFile);
            }
        }

        private static BundleFileProcessor Processor
        {
            get
            {
                if (_processor == null)
                {
                    _processor = new BundleFileProcessor();
                    _processor.BeforeBundling += (s, e) => { if (e.ContainsChanges) { ProjectHelpers.CheckFileOutOfSourceControl(e.OutputFileName); } };
                    _processor.AfterBundling += AfterProcess;
                    _processor.AfterWritingSourceMap += AfterWritingSourceMap;
                    _processor.Processing += (s, e) => { ErrorList.CleanErrors(e.OutputFileName); };
                    _processor.BeforeWritingSourceMap += CheckFileOutOfSourceControl;
                }

                return _processor;
            }
        }

        internal static IEnumerable<Bundle> IsOutputConfigered(string configFile, string sourceFile)
        {
            List<Bundle> list = new List<Bundle>();

            if (string.IsNullOrEmpty(configFile))
                return list;

            try
            {
                var bundles = BundleHandler.GetBundles(configFile);

                foreach (Bundle bundle in bundles)
                {
                    if (bundle.GetAbsoluteOutputFile().Equals(sourceFile, StringComparison.OrdinalIgnoreCase))
                        list.Add(bundle);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }

            return list;
        }

        public static void Process(string configFile)
        {
            Process(configFile, null);
        }

        public static void Process(string configFile, IEnumerable<Bundle> bundles)
        {
            if (!IsOutputProduced(configFile))
                return;

            ThreadPool.QueueUserWorkItem((o) =>
            {
                try
                {
                    Processor.Process(configFile, bundles);
                    ConfigFileRead(configFile);
                }
                catch (Exception ex)
                {
                    HandleProcessingException(configFile, ex);
                }
            });
        }

        /// <summary>
        /// Remembers the inputFiles of every bundle in the config file, so saving it only forces
        /// the bundles whose inputFiles were changed.
        /// </summary>
        public static void TrackInputFiles(string configFile)
        {
            if (string.IsNullOrEmpty(configFile))
                return;

            ThreadPool.QueueUserWorkItem((o) =>
            {
                try
                {
                    // an invalid config file is tracked too, so that once it is fixed the bundles changed
                    // or added in it are told apart; opening it says why it is invalid right away
                    string error;

                    if (!BundleFileProcessor.TrackInputFiles(configFile, out error) && error != null)
                    {
                        RunOnUIThread(() => ReportInvalidConfigFile(configFile, error));
                        return;
                    }

                    ConfigFileRead(configFile);
                }
                catch (Exception ex)
                {
                    Logger.Log(ex);
                }
            });
        }

        public static bool IsOutputProduced(string configFile)
        {
            if (string.IsNullOrEmpty(configFile))
                return false;

            string bindings = configFile + ".bindings";

            if (File.Exists(bindings))
            {
                var lines = File.ReadAllLines(bindings);
                return !lines.Any(l => l.TrimStart().StartsWith("produceoutput=false", StringComparison.OrdinalIgnoreCase));
            }

            if (BundlerMinifierPackage.Options != null)
                return BundlerMinifierPackage.Options.ProduceOutput;
            else
                return false;
        }

        public static void ToggleOutputProduction(string configFile, bool produceOutput)
        {
            string bindings = configFile + ".bindings";
            var sb = new StringBuilder();

            if (File.Exists(bindings))
            {
                var lines = File.ReadAllLines(bindings);

                foreach (var line in lines)
                {
                    if (!line.TrimStart().StartsWith("produceoutput", StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine(line);
                }
            }

            sb.AppendLine($"produceoutput={produceOutput.ToString().ToLowerInvariant()}");

            ProjectHelpers.CheckFileOutOfSourceControl(bindings);
            File.WriteAllText(bindings, sb.ToString().Trim());
            ProjectHelpers.AddNestedFile(configFile, bindings);
        }

        public static void SourceFileChanged(string configFile, string sourceFile)
        {
            if (!IsOutputProduced(configFile))
                return;

            ThreadPool.QueueUserWorkItem((o) =>
            {
                try
                {
                    Processor.SourceFileChanged(configFile, sourceFile);
                    ConfigFileRead(configFile);
                }
                catch (Exception ex)
                {
                    HandleProcessingException(configFile, ex);
                }
            });
        }

        /// <summary>The config file was read, so an error reported earlier for it is gone.</summary>
        private static void ConfigFileRead(string configFile)
        {
            RunOnUIThread(() => ErrorList.CleanErrors(configFile));
        }

        /// <summary>
        /// Reports an exception thrown while processing a config file. An invalid config file goes to the
        /// Error List, where it can be seen and navigated to; anything else goes to the output window.
        /// </summary>
        private static void HandleProcessingException(string configFile, Exception ex)
        {
            if (ex is InvalidDataException)
            {
                // the message starts with the path of the config file, which the Error List already shows
                const string prefix = " could not be read: ";
                int index = ex.Message.IndexOf(prefix, StringComparison.Ordinal);
                string reason = index > -1 ? ex.Message.Substring(index + prefix.Length) : ex.Message;

                RunOnUIThread(() => ReportInvalidConfigFile(configFile, reason));
                return;
            }

            RunOnUIThread(() =>
            {
                Logger.Log(Resources.Text.ErrorExceptionThrown.AddParams(configFile, ex));
                Logger.ShowPane();
            });
        }

        private static void ReportInvalidConfigFile(string configFile, string reason)
        {
            Logger.Log($"{configFile} could not be read: {reason}");

            // "(line 12)" added by the config reader, or "line 12, position 5." from the JSON parser
            Match line = Regex.Match(reason, @"\bline (\d+)");
            int lineNumber = line.Success ? int.Parse(line.Groups[1].Value) : 0;

            ErrorList.AddConfigError(configFile, reason, lineNumber);

            if (_dte != null)
                _dte.StatusBar.Text = $"{Path.GetFileName(configFile)} is invalid. See Error List for details";
        }

        /// <summary>
        /// Runs the action on the UI thread once the package has loaded. This is async void, so anything
        /// escaping it terminates Visual Studio: it must never throw.
        /// </summary>
        private static async void RunOnUIThread(Action action)
        {
            try
            {
                await BundlerMinifierPackage.IsPackageInitialized;

                BundlerMinifierPackage._dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception error)
                    {
                        System.Diagnostics.Debug.Write(error);
                    }
                }));
            }
            catch (Exception error)
            {
                System.Diagnostics.Debug.Write(error);
            }
        }

        private static void CheckFileOutOfSourceControl(object sender, MinifyFileEventArgs e)
        {
            if (e.ContainsChanges)
                ProjectHelpers.CheckFileOutOfSourceControl(e.ResultFile);
        }

        private static void AfterProcess(object sender, BundleFileEventArgs e)
        {
            if (!e.Bundle.IncludeInProject)
                return;

            var item = _dte?.Solution?.FindProjectItem(e.Bundle.FileName);

            if (item == null || item.ContainingProject == null)
                return;

            item.ContainingProject.AddFileToProject(e.OutputFileName);
            _dte.StatusBar.Text = Resources.Text.statusBundleUpdated;
        }

        private static void AfterWritingSourceMap(object sender, MinifyFileEventArgs e)
        {
            var item = _dte?.Solution?.FindProjectItem(e.OriginalFile);

            if (item == null || item.ContainingProject == null)
                return;

            ProjectHelpers.AddNestedFile(e.OriginalFile, e.ResultFile);
        }

        private static void ErrorMinifyingFile(object sender, MinifyFileEventArgs e)
        {
            ErrorListService.ProcessCompilerResults(e.Result);
            if (_dte != null)
                _dte.StatusBar.Text = Resources.Text.ErrorMinifying.AddParams(Path.GetFileName(e.OriginalFile));
        }
    }
}
