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

        private sealed class ConfigState
        {
            // outputFileName -> 上次的狀態
            public readonly ConcurrentDictionary<string, Entry> Entries = new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

            // 整份設定檔的 bundle 都記過基準之後才為 true，此後不在 Entries 裡的 bundle 就是新加入的
            public volatile bool Primed;
        }

        // 設定檔完整路徑 -> 該設定檔的狀態；兩層查詢都不需要另外組字串當 key
        private static readonly ConcurrentDictionary<string, ConfigState> _configs =
            new ConcurrentDictionary<string, ConfigState>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 設定檔修改時間與上次相同就直接略過，不計算 hash；有異動（或還沒有基準）才計算並比對。
        /// </summary>
        /// <param name="hash">回傳 true 時為目前的 hash，處理完成後交給 <see cref="Commit"/>。</param>
        /// <param name="changed">
        /// 已有基準且 hash 不同，或整份設定檔記過基準後才新加入的 bundle 為 true。
        /// 設定檔還沒記過基準時無從比對，視為未異動，否則單次執行每次都會把所有 bundle 當成異動。
        /// </param>
        /// <returns>是否需要在處理完成後呼叫 <see cref="Commit"/>。</returns>
        public static bool Check(Bundle bundle, out ulong hash, out bool changed)
        {
            hash = 0;
            changed = false;

            // 不是從設定檔讀出來的 bundle 沒有修改時間可比對
            if (bundle.ConfigWriteTicks == 0 || bundle.FileName == null || bundle.OutputFileName == null)
                return false;

            ConfigState state = GetState(bundle.FileName);
            Entry entry;
            bool known = state.Entries.TryGetValue(bundle.OutputFileName, out entry);

            if (known && entry.ConfigWriteTicks == bundle.ConfigWriteTicks)
                return false;

            hash = bundle.ComputeInputFilesHash();
            changed = known ? entry.Hash != hash : state.Primed;

            return true;
        }

        /// <summary>
        /// bundle 處理完成後才記錄，處理中途失敗的話下次仍會判定為異動。
        /// </summary>
        public static void Commit(Bundle bundle, ulong hash)
        {
            GetState(bundle.FileName).Entries[bundle.OutputFileName] = new Entry { ConfigWriteTicks = bundle.ConfigWriteTicks, Hash = hash };
        }

        /// <summary>
        /// 把還沒有基準的 bundle 以目前的設定記為基準，已有基準的不覆寫。
        /// 傳入的必須是設定檔裡的全部 bundle，之後才出現的 bundle 會被視為新加入而判定為異動。
        /// </summary>
        public static void Prime(IEnumerable<Bundle> bundles)
        {
            ConfigState state = null;

            foreach (Bundle bundle in bundles)
            {
                if (bundle.ConfigWriteTicks == 0 || bundle.FileName == null || bundle.OutputFileName == null)
                    continue;

                // 同一次讀出來的 bundle 都屬於同一個設定檔
                if (state == null)
                    state = GetState(bundle.FileName);

                if (!state.Entries.ContainsKey(bundle.OutputFileName))
                    state.Entries.TryAdd(bundle.OutputFileName, new Entry { ConfigWriteTicks = bundle.ConfigWriteTicks, Hash = bundle.ComputeInputFilesHash() });
            }

            // 全部記完才標記，避免其他執行緒把還沒記到的 bundle 誤判為新加入
            if (state != null)
                state.Primed = true;
        }

        private static ConfigState GetState(string configFile)
        {
            return _configs.GetOrAdd(configFile, _ => new ConfigState());
        }
    }
}
