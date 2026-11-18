using System.Collections.Generic;
using Newtonsoft.Json;

namespace BundlerMinifier
{
    /// <summary>
    /// layout 的一組變體設定。有寫的項目整份取代 layout 的預設值，沒寫的（null）沿用預設值；
    /// 寫成空陣列表示這個變體不加任何檔案。
    /// </summary>
    public class BundleLayoutVariant
    {
        /// <summary>這個變體改接的上層 layout，沒寫就沿用 layout 自己的上層。</summary>
        [JsonProperty("layout")]
        public string Layout { get; set; }

        [JsonProperty("preambles")]
        public List<string> Preambles { get; set; }

        [JsonProperty("postscripts")]
        public List<string> Postscripts { get; set; }

        [JsonProperty("styles")]
        public List<string> Styles { get; set; }
    }
}
