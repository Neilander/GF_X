using System.Collections.Generic;
using UnityEngine;
using GameFramework;
using UnityGameFramework.Runtime;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌数据提供者工厂
    /// 负责创建和管理卡牌数据提供者
    /// </summary>
    public static class CardDataProviderFactory
    {
        private static bool s_UseDataTable = false;
        private static Dictionary<string, ICardDataProvider> s_ProviderCache = new Dictionary<string, ICardDataProvider>();

        /// <summary>
        /// 设置数据源类型
        /// </summary>
        public static void SetDataSourceType(bool useDataTable)
        {
            if (s_UseDataTable != useDataTable)
            {
                s_UseDataTable = useDataTable;
                ClearCache();
            }
        }

        /// <summary>
        /// 从 ScriptableObject 创建提供者
        /// </summary>
        public static ICardDataProvider CreateFromScriptableObject(CardData cardData)
        {
            if (cardData == null)
            {
                Log.Error("CardData is null.");
                return null;
            }

            string cardId = cardData.index;
            if (!s_ProviderCache.TryGetValue(cardId, out ICardDataProvider provider))
            {
                provider = new CardDataAdapter(cardData);
                s_ProviderCache[cardId] = provider;
            }

            return provider;
        }

        /// <summary>
        /// 从 DataTable 创建提供者（未来实现）
        /// </summary>
        public static ICardDataProvider CreateFromDataTable(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                Log.Error("Card ID is null or empty.");
                return null;
            }

            if (!s_ProviderCache.TryGetValue(cardId, out ICardDataProvider provider))
            {
                // TODO: 从 DataTable 读取
                // var dataTable = GF.DataTable.GetDataTable<DRCard>();
                // var dataRow = dataTable.GetDataRow(int.Parse(cardId));
                // if (dataRow != null)
                // {
                //     provider = new CardDataTableAdapter(dataRow);
                //     s_ProviderCache[cardId] = provider;
                // }
                
                Log.Warning("DataTable implementation not ready yet.");
                return null;
            }

            return provider;
        }

        /// <summary>
        /// 批量创建提供者（从 ScriptableObject 列表）
        /// </summary>
        public static List<ICardDataProvider> CreateFromScriptableObjects(List<CardData> cardDataList)
        {
            List<ICardDataProvider> providers = new List<ICardDataProvider>();
            
            if (cardDataList == null || cardDataList.Count == 0)
            {
                return providers;
            }

            foreach (var cardData in cardDataList)
            {
                var provider = CreateFromScriptableObject(cardData);
                if (provider != null)
                {
                    providers.Add(provider);
                }
            }

            return providers;
        }

        /// <summary>
        /// 批量创建提供者（从 DataTable）
        /// </summary>
        public static List<ICardDataProvider> CreateFromDataTable()
        {
            List<ICardDataProvider> providers = new List<ICardDataProvider>();

            // TODO: 从 DataTable 读取所有卡牌
            // var dataTable = GF.DataTable.GetDataTable<DRCard>();
            // var allRows = dataTable.GetAllDataRows();
            // foreach (var row in allRows)
            // {
            //     var provider = new CardDataTableAdapter(row);
            //     providers.Add(provider);
            //     s_ProviderCache[row.Id.ToString()] = provider;
            // }

            Log.Warning("DataTable implementation not ready yet.");
            return providers;
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public static void ClearCache()
        {
            s_ProviderCache.Clear();
        }

        /// <summary>
        /// 获取缓存的提供者
        /// </summary>
        public static ICardDataProvider GetCachedProvider(string cardId)
        {
            s_ProviderCache.TryGetValue(cardId, out ICardDataProvider provider);
            return provider;
        }
    }
}
