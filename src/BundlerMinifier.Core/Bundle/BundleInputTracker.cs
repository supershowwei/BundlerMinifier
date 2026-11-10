using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BundlerMinifier
{
    /// <summary>
    /// 在行程存活期間記住每個 bundle 上次處理時的設定檔修改時間與 inputFiles hash，
    /// 用來判斷 inputFiles 清單是否異動。狀態只放記憶體，行程結束就沒有了。
    /// </summary>
    internal static class BundleInputTracker
    {
        private struct Entry
        {
            public long ConfigWriteTicks;
            public ulong Hash;
        }

        // 設定檔完整路徑 -> outputFileName -> 上次的狀態；兩層查詢都不需要另外組字串當 key
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Entry>> _configs =
            new ConcurrentDictionary<string, ConcurrentDictionary<string, Entry>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 設定檔修改時間與上次相同就直接略過，不計算 hash；有異動（或還沒有基準）才計算並比對。
        /// </summary>
        /// <param name="hash">回傳 true 時為目前的 hash，處理完成後交給 <see cref="Commit"/>。</param>
        /// <param name="changed">已有基準且 hash 不同時為 true；沒有基準時無從比對，視為未異動。</param>
        /// <returns>是否需要在處理完成後呼叫 <see cref="Commit"/>。</returns>
        public static bool Check(Bundle bundle, out ulong hash, out bool changed)
        {
            hash = 0;
            changed = false;

            // 不是從設定檔讀出來的 bundle 沒有修改時間可比對
            if (bundle.ConfigWriteTicks == 0 || bundle.FileName == null || bundle.OutputFileName == null)
                return false;

            Entry entry;
            bool known = GetEntries(bundle.FileName).TryGetValue(bundle.OutputFileName, out entry);

            if (known && entry.ConfigWriteTicks == bundle.ConfigWriteTicks)
                return false;

            hash = bundle.ComputeInputFilesHash();
            changed = known && entry.Hash != hash;

            return true;
        }

        /// <summary>
        /// bundle 處理完成後才記錄，處理中途失敗的話下次仍會判定為異動。
        /// </summary>
        public static void Commit(Bundle bundle, ulong hash)
        {
            GetEntries(bundle.FileName)[bundle.OutputFileName] = new Entry { ConfigWriteTicks = bundle.ConfigWriteTicks, Hash = hash };
        }

        /// <summary>
        /// 把還沒有基準的 bundle 以目前的設定記為基準，已有基準的不覆寫。
        /// </summary>
        public static void Prime(IEnumerable<Bundle> bundles)
        {
            foreach (Bundle bundle in bundles)
            {
                if (bundle.ConfigWriteTicks == 0 || bundle.FileName == null || bundle.OutputFileName == null)
                    continue;

                ConcurrentDictionary<string, Entry> entries = GetEntries(bundle.FileName);

                if (!entries.ContainsKey(bundle.OutputFileName))
                    entries.TryAdd(bundle.OutputFileName, new Entry { ConfigWriteTicks = bundle.ConfigWriteTicks, Hash = bundle.ComputeInputFilesHash() });
            }
        }

        private static ConcurrentDictionary<string, Entry> GetEntries(string configFile)
        {
            return _configs.GetOrAdd(configFile, _ => new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase));
        }
    }
}
