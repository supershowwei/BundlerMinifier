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
            entries.Add(newBundle);
            newBundle.FileName = configFile;

            JsonSerializerSettings settings = new JsonSerializerSettings()
            {
                Formatting = Formatting.Indented,
                DefaultValueHandling = DefaultValueHandling.Ignore,
            };

            string content = JsonConvert.SerializeObject(entries, settings);
            File.WriteAllText(configFile, content + Environment.NewLine);
        }

        public static void RemoveBundle(string configFile, Bundle bundleToRemove)
        {
            List<object> entries = ReadConfigEntries(configFile);

            if (entries.RemoveAll(e => bundleToRemove.Equals(e)) > 0)
            {
                string content = JsonConvert.SerializeObject(entries, Formatting.Indented);
                File.WriteAllText(configFile, content);
            }
        }

        /// <summary>
        /// Reads the config as bundles, keeping layout entries as raw JSON so rewriting the file preserves them.
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
            try
            {
                if (string.IsNullOrEmpty(configFile) || !File.Exists(configFile))
                {
                    bundles = Enumerable.Empty<Bundle>();
                    return false;
                }

                configFile = new FileInfo(configFile).FullName;
                string content = File.ReadAllText(configFile);
                var layouts = new Dictionary<string, BundleLayout>(StringComparer.Ordinal);
                var list = new List<Bundle>();

                foreach (JToken entry in JArray.Parse(content))
                {
                    if (BundleLayout.IsLayoutEntry(entry))
                    {
                        // throws on a duplicate layout name
                        foreach (JProperty property in ((JObject)entry).Properties())
                            layouts.Add(property.Name, property.Value.ToObject<BundleLayout>());
                    }
                    else
                    {
                        list.Add(entry.ToObject<Bundle>());
                    }
                }

                foreach (Bundle bundle in list)
                {
                    bundle.FileName = configFile;
                    BundleLayout.Apply(bundle, layouts);
                }

                bundles = list.ToArray();

                return true;
            }
            catch
            {
                bundles = null;
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
