using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BundlerMinifier
{
    /// <summary>
    /// A named set of files that are placed around the input files of every bundle referencing it.
    /// </summary>
    public class BundleLayout
    {
        [JsonProperty("layout")]
        public string Layout { get; set; }

        [JsonProperty("preambles")]
        public List<string> Preambles { get; } = new List<string>();

        [JsonProperty("postscripts")]
        public List<string> Postscripts { get; } = new List<string>();

        /// <summary>CSS bundle 用的樣式檔，沿著 layout 鏈由外往內疊加，不分前後包。</summary>
        [JsonProperty("styles")]
        public List<string> Styles { get; } = new List<string>();

        /// <summary>
        /// A layout entry is an object without an outputFileName whose properties are all objects,
        /// each property being the definition of a layout keyed by its name.
        /// </summary>
        internal static bool IsLayoutEntry(JToken entry)
        {
            var obj = entry as JObject;

            return obj != null
                && obj.Count > 0
                && obj["outputFileName"] == null
                && obj.Properties().All(p => p.Value.Type == JTokenType.Object);
        }

        /// <summary>
        /// 沿著 bundle 的 layout 鏈往外走，依輸出檔類型填入 layout 提供的檔案：
        /// JS bundle 由外層 layout 包住內層，最外層的 preambles 最先、postscripts 最後；
        /// CSS bundle 只把 styles 由外往內疊加在 inputFiles 之前；其他類型不套用 layout 檔案。
        /// </summary>
        internal static void Apply(Bundle bundle, IDictionary<string, BundleLayout> layouts)
        {
            bundle.LayoutPreambles.Clear();
            bundle.LayoutPostscripts.Clear();

            string extension = Path.GetExtension(bundle.OutputFileName);
            bool isJavaScript = string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase);
            bool isCss = string.Equals(extension, ".css", StringComparison.OrdinalIgnoreCase);

            var visited = new List<string>();
            string name = bundle.Layout;

            while (!string.IsNullOrEmpty(name))
            {
                if (visited.Contains(name))
                    throw new InvalidOperationException($"Circular layout reference: {string.Join(" -> ", visited)} -> {name}");

                BundleLayout layout;

                if (!layouts.TryGetValue(name, out layout))
                    throw new InvalidOperationException($"The layout \"{name}\" referenced by {bundle.OutputFileName} is not defined");

                visited.Add(name);

                if (isJavaScript)
                {
                    bundle.LayoutPreambles.InsertRange(0, layout.Preambles);
                    bundle.LayoutPostscripts.AddRange(layout.Postscripts);
                }
                else if (isCss)
                {
                    bundle.LayoutPreambles.InsertRange(0, layout.Styles);
                }

                name = layout.Layout;
            }
        }
    }
}
