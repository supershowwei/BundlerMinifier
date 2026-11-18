using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BundlerMinifier
{
    /// <summary>
    /// A named set of files that are placed around the input files of every bundle nested in it.
    /// </summary>
    public class BundleLayout
    {
        private const string BundlesProperty = "bundles";
        private const string LayoutProperty = "layout";

        /// <summary>layout 的名稱，也就是定義它的屬性名稱。</summary>
        [JsonIgnore]
        public string Name { get; internal set; }

        /// <summary>包住這個 layout 的上層 layout，最外層的 layout 為 null。</summary>
        [JsonIgnore]
        public BundleLayout Parent { get; internal set; }

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
        /// 讀取設定檔的最上層陣列，依文件順序（深度優先）回傳所有葉節點 bundle。
        /// layout 定義的 bundles 裡可以再放 layout 與 bundle，bundle 套用的 layout 完全由它所在的位置決定；
        /// bundles 可以是空陣列或不寫。layout 名稱不可重複，舊版以 "layout" 指名上層 layout 的寫法不再支援。
        /// </summary>
        internal static List<Bundle> ReadBundles(JArray entries)
        {
            var bundles = new List<Bundle>();
            ReadBundles(entries, null, bundles, new HashSet<string>(StringComparer.Ordinal));

            return bundles;
        }

        private static void ReadBundles(JArray entries, BundleLayout parent, List<Bundle> bundles, HashSet<string> names)
        {
            foreach (JToken entry in entries)
            {
                if (IsLayoutEntry(entry))
                {
                    foreach (JProperty property in ((JObject)entry).Properties())
                    {
                        if (!names.Add(property.Name))
                            throw new InvalidOperationException($"The layout \"{property.Name}\" is defined more than once{AtLine(property)}");

                        var definition = (JObject)property.Value;
                        RejectLayoutProperties(property.Name, definition);

                        BundleLayout layout = definition.ToObject<BundleLayout>();
                        layout.Name = property.Name;
                        layout.Parent = parent;

                        JToken children = definition[BundlesProperty];

                        if (children is JArray array)
                            ReadBundles(array, layout, bundles, names);
                        else if (children != null && children.Type != JTokenType.Null)
                            throw new InvalidOperationException($"The \"{BundlesProperty}\" of the layout \"{property.Name}\" must be an array{AtLine(children)}");
                    }
                }
                else
                {
                    Bundle bundle = entry.ToObject<Bundle>();

                    if (entry is JObject obj && obj[LayoutProperty] != null)
                        throw new InvalidOperationException($"The \"{LayoutProperty}\" property of {bundle.OutputFileName} is no longer supported, place the bundle in the \"{BundlesProperty}\" of the layout instead{AtLine(obj[LayoutProperty])}");

                    bundle.Layout = parent;
                    bundles.Add(bundle);
                }
            }
        }

        /// <summary>舊版用 "layout" 指名上層 layout，忽略它會默默少掉 layout 的檔案，所以直接視為設定檔無效。</summary>
        private static void RejectLayoutProperties(string name, JObject definition)
        {
            if (definition[LayoutProperty] != null)
                throw new InvalidOperationException($"The \"{LayoutProperty}\" property of the layout \"{name}\" is no longer supported, place the layout in the \"{BundlesProperty}\" of its parent layout instead{AtLine(definition[LayoutProperty])}");

            if (definition["variants"] is JObject variants)
            {
                foreach (JProperty variant in variants.Properties())
                {
                    if (variant.Value is JObject obj && obj[LayoutProperty] != null)
                        throw new InvalidOperationException($"The \"{LayoutProperty}\" property of the variant \"{variant.Name}\" of the layout \"{name}\" is no longer supported{AtLine(obj[LayoutProperty])}");
                }
            }
        }

        /// <summary>
        /// 錯誤訊息後面加上設定檔中的行號，Visual Studio 的錯誤清單據此直接跳到該行。
        /// </summary>
        private static string AtLine(JToken token)
        {
            var info = token as IJsonLineInfo;

            return info != null && info.HasLineInfo() ? $" (line {info.LineNumber})" : string.Empty;
        }

        /// <summary>
        /// 依文件順序列出 layout 項目底下（含更深層 layout）所有巢狀的 bundle 物件。
        /// </summary>
        internal static IEnumerable<JObject> GetNestedBundles(JToken layoutEntry)
        {
            if (!IsLayoutEntry(layoutEntry))
                yield break;

            foreach (JProperty property in ((JObject)layoutEntry).Properties())
            {
                if (!(property.Value[BundlesProperty] is JArray children))
                    continue;

                foreach (JToken child in children.ToList())
                {
                    if (IsLayoutEntry(child))
                    {
                        foreach (JObject nested in GetNestedBundles(child))
                            yield return nested;
                    }
                    else if (child is JObject bundle)
                    {
                        yield return bundle;
                    }
                }
            }
        }

        /// <summary>
        /// 沿著 bundle 所在的 layout 往外走，依輸出檔類型填入 layout 提供的檔案：
        /// JS bundle 由外層 layout 包住內層，最外層的 preambles 最先、postscripts 最後；
        /// CSS bundle 只把 styles 由外往內疊加在 inputFiles 之前；其他類型不套用 layout 檔案。
        /// 輸出檔就是自己唯一輸入檔的 bundle 也不套用。
        /// bundle 有指定 variant 時，每一層 layout 若有定義同名變體，就由變體決定這一層的檔案清單；
        /// 指定的變體在整條鏈上都沒有定義時，設定檔無效。
        /// </summary>
        internal static void Apply(Bundle bundle)
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

            for (BundleLayout layout = bundle.Layout; layout != null; layout = layout.Parent)
            {
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
