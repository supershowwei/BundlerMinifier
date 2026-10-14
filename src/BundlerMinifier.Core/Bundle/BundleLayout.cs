using System;
using System.Collections.Generic;
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
        /// Fills the layout preambles and postscripts of the bundle by walking up its layout chain.
        /// The outermost layout wraps everything, so its preambles come first and its postscripts last.
        /// </summary>
        internal static void Apply(Bundle bundle, IDictionary<string, BundleLayout> layouts)
        {
            bundle.LayoutPreambles.Clear();
            bundle.LayoutPostscripts.Clear();

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
                bundle.LayoutPreambles.InsertRange(0, layout.Preambles);
                bundle.LayoutPostscripts.AddRange(layout.Postscripts);
                name = layout.Layout;
            }
        }
    }
}
