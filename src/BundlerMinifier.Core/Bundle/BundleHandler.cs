using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BundlerMinifier
{
    public static class BundleHandler
    {
        public static void AddBundle(string configFile, Bundle newBundle)
        {
            List<object> entries = ReadConfigEntries(configFile);
            entries.RemoveAll(x => x is Bundle bundle && bundle.OutputFileName.Equals(newBundle.OutputFileName));
            newBundle.FileName = configFile;

            JsonSerializerSettings settings = new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                DefaultValueHandling = DefaultValueHandling.Ignore,
            };

            // 同名的 bundle 已經放在某個 layout 裡時就地取代，讓它留在原本的 layout 下，
            // 不會被搬到最上層而默默少掉 layout 的檔案
            List<JObject> nested = GetNestedBundles(entries, newBundle.OutputFileName);

            if (nested.Count > 0)
            {
                nested[0].Replace(JObject.FromObject(newBundle, JsonSerializer.Create(settings)));

                foreach (JObject duplicate in nested.Skip(1))
                    duplicate.Remove();
            }
            else
            {
                entries.Add(newBundle);
            }

            string content = JsonConvert.SerializeObject(entries, settings);
            File.WriteAllText(configFile, content + Environment.NewLine);
        }

        public static void RemoveBundle(string configFile, Bundle bundleToRemove)
        {
            List<object> entries = ReadConfigEntries(configFile);
            int removed = entries.RemoveAll(e => bundleToRemove.Equals(e));

            // 放在 layout 裡的 bundle 從原本的位置移除，layout 的 bundles 因此變成空陣列也沒關係
            foreach (JObject nested in GetNestedBundles(entries, bundleToRemove.OutputFileName))
            {
                nested.Remove();
                removed++;
            }

            if (removed > 0)
            {
                string content = JsonConvert.SerializeObject(entries, Formatting.Indented);
                File.WriteAllText(configFile, content);
            }
        }

        /// <summary>依文件順序列出各個 layout 項目裡，輸出檔為 <paramref name="outputFileName"/> 的巢狀 bundle。</summary>
        private static List<JObject> GetNestedBundles(List<object> entries, string outputFileName)
        {
            return entries
                .OfType<JToken>()
                .SelectMany(BundleLayout.GetNestedBundles)
                .Where(b => string.Equals((string)b["outputFileName"], outputFileName, StringComparison.Ordinal))
                .ToList();
        }

        /// <summary>
        /// Reads the config as bundles, keeping layout entries (and the bundles nested in them) as raw JSON
        /// so rewriting the file preserves them.
        /// </summary>
        private static List<object> ReadConfigEntries(string configFile)
        {
            List<object> entries = new List<object>();

            if (!File.Exists(configFile))
                return entries;

            foreach (JToken entry in JArray.Parse(File.ReadAllText(configFile)))
            {
                entries.Add(BundleLayout.IsLayoutEntry(entry) ? (object)entry : entry.ToObject<Bundle>());
            }

            return entries;
        }

        public static bool TryGetBundles(string configFile, out IEnumerable<Bundle> bundles)
        {
            string error;
            return TryGetBundles(configFile, out bundles, out error);
        }

        /// <param name="error">Why the config file could not be read. Null when it was read, or when there is no such file.</param>
        public static bool TryGetBundles(string configFile, out IEnumerable<Bundle> bundles, out string error)
        {
            error = null;

            try
            {
                FileInfo info = string.IsNullOrEmpty(configFile) ? null : new FileInfo(configFile);

                if (info == null || !info.Exists)
                {
                    bundles = Enumerable.Empty<Bundle>();
                    return false;
                }

                configFile = info.FullName;

                // taken before the content is read, so an edit in between only makes the time look older
                // and the next run checks the input files again
                long configWriteTicks = info.LastWriteTimeUtc.Ticks;
                string content = File.ReadAllText(configFile);
                List<Bundle> list = BundleLayout.ReadBundles(JArray.Parse(content));

                foreach (Bundle bundle in list)
                {
                    bundle.FileName = configFile;
                    bundle.ConfigWriteTicks = configWriteTicks;
                    BundleLayout.Apply(bundle);
                }

                bundles = list.ToArray();

                return true;
            }
            catch (Exception ex)
            {
                bundles = null;
                error = ex.Message;
                return false;
            }
        }

        public static IEnumerable<Bundle> GetBundles(string configFile)
        {
            IEnumerable<Bundle> bundles;
            TryGetBundles(configFile, out bundles);
            return bundles;
        }

        public static void ProcessBundle(string baseFolder, Bundle bundle)
        {
            DateTime mostRecentWrite = default(DateTime);
            StringBuilder sb = new StringBuilder();
            List<string> inputFiles = bundle.GetAbsoluteInputFiles();

            for (int i = 0; i < inputFiles.Count; i++)
            {
                var input = inputFiles[i];

                string file = Path.Combine(baseFolder, input);

                if (File.Exists(file))
                {
                    string content;

                    if (input.EndsWith(".css", StringComparison.OrdinalIgnoreCase) && AdjustRelativePaths(bundle))
                    {
                        content = CssRelativePath.Adjust(file, bundle.GetAbsoluteOutputFile());
                    }
                    else
                    {
                        content = FileHelpers.ReadAllText(file);
                    }
                    var lastWriteFile = System.IO.File.GetLastWriteTimeUtc(file);
                    if (mostRecentWrite < lastWriteFile)
                        mostRecentWrite = lastWriteFile;

                    // adding new line only if there are more than 1 files
                    // otherwise we are preserving file integrity
                    if (sb.Length > 0)
                        sb.AppendLine();

                    sb.Append(content);
                }
            }

            if (sb.Length > 0 && bundle.WrapInIife && IsJavaScript(bundle))
            {
                sb.Insert(0, "(function () {" + Environment.NewLine);

                // the closing statement must start on a line of its own, or a
                // single-line comment at the end of the bundle would swallow it
                if (sb[sb.Length - 1] != '\n')
                    sb.AppendLine();

                sb.Append("})();");
            }

            bundle.MostRecentWrite = mostRecentWrite;
            bundle.Output = sb.ToString();
        }

        private static bool IsJavaScript(Bundle bundle)
        {
            return Path.GetExtension(bundle.OutputFileName).Equals(".js", StringComparison.OrdinalIgnoreCase);
        }

        private static bool AdjustRelativePaths(Bundle bundle)
        {
            if (!bundle.Minify.ContainsKey("adjustRelativePaths"))
                return true;

            return bundle.Minify["adjustRelativePaths"].ToString() == "True";
        }
    }
}
