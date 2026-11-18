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
        /// 可選的變體，以名稱為鍵。bundle 用 <see cref="Bundle.Variant"/> 指定名稱時，
        /// layout 鏈上每個有定義這個變體的 layout 都改用它的設定，沒定義的照常用自己的預設內容。
        /// </summary>
        [JsonProperty("variants")]
        public Dictionary<string, BundleLayoutVariant> Variants { get; } = new Dictionary<string, BundleLayoutVariant>(StringComparer.Ordinal);

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
        /// 輸出檔就是自己唯一輸入檔的 bundle 也不套用，但 layout 鏈照樣會走過一遍，參照錯誤一樣會被擋下來。
        /// bundle 有指定 variant 時，每一層 layout 若有定義同名變體，就由變體決定這一層的檔案清單與上層 layout；
        /// 指定的變體在實際走過的整條鏈上都沒有定義時，設定檔無效。
        /// </summary>
        internal static void Apply(Bundle bundle, IDictionary<string, BundleLayout> layouts)
        {
            bundle.LayoutPreambles.Clear();
            bundle.LayoutPostscripts.Clear();

            string extension = Path.GetExtension(bundle.OutputFileName);
            bool takesFiles = !IsOwnOutput(bundle);
            bool isJavaScript = takesFiles && string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase);
            bool isCss = takesFiles && string.Equals(extension, ".css", StringComparison.OrdinalIgnoreCase);

            string variantName = bundle.Variant;
            bool hasVariant = !string.IsNullOrEmpty(variantName);
            bool variantFound = false;

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

                // 寫成 "v": null 的變體視為空的變體，全部沿用預設內容
                BundleLayoutVariant variant = null;

                if (hasVariant && layout.Variants.TryGetValue(variantName, out variant))
                    variantFound = true;

                if (isJavaScript)
                {
                    bundle.LayoutPreambles.InsertRange(0, variant?.Preambles ?? layout.Preambles);
                    bundle.LayoutPostscripts.AddRange(variant?.Postscripts ?? layout.Postscripts);
                }
                else if (isCss)
                {
                    bundle.LayoutPreambles.InsertRange(0, variant?.Styles ?? layout.Styles);
                }

                name = variant?.Layout ?? layout.Layout;
            }

            if (hasVariant && !variantFound)
                throw new InvalidOperationException($"The variant \"{variantName}\" set by {bundle.OutputFileName} is not defined by any layout it uses");
        }

        /// <summary>
        /// 唯一的輸入檔就是輸出檔的 bundle 不會被處理，輸出檔（也就是來源檔本身）不會被改寫。
        /// 一旦加上 layout 的檔案，輸入檔就不只一個，它會被當成一般 bundle 寫回來源檔，
        /// 而且每處理一次就再疊上一次 layout 的內容。
        /// </summary>
        private static bool IsOwnOutput(Bundle bundle)
        {
            if (bundle.InputFiles.Count != 1)
                return false;

            string input = bundle.InputFiles[0];
            string output = bundle.OutputFileName;

            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
                return false;

            if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
                return true;

            // 寫法不同但指向同一個檔案，例如 ./site.js 與 site.js
            if (string.IsNullOrEmpty(bundle.FileName) || input.IndexOf('*') > -1 || input[0] == '!')
                return false;

            string folder = Path.GetDirectoryName(bundle.FileName);

            return string.Equals(
                Path.GetFullPath(Path.Combine(folder, input.NormalizePath())),
                Path.GetFullPath(Path.Combine(folder, output.NormalizePath())),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
