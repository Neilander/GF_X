using System.Collections.Generic;
using UnityGameFramework.Runtime;

namespace AAAGame.Card
{
    /// <summary>
    /// 卡牌数据提供者工厂。
    /// </summary>
    public static class CardDataProviderFactory
    {
        private static readonly Dictionary<string, ICardDataProvider> s_ProviderCache = new Dictionary<string, ICardDataProvider>();

        public static ICardDataProvider CreateFromScriptableObject(CardData cardData)
        {
            if (cardData == null)
            {
                Log.Error("CardData is null.");
                return null;
            }

            string cardId = cardData.CardId;
            if (!s_ProviderCache.TryGetValue(cardId, out ICardDataProvider provider))
            {
                provider = new CardDataAdapter(cardData);
                s_ProviderCache[cardId] = provider;
            }

            return provider;
        }

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

        public static void ClearCache()
        {
            s_ProviderCache.Clear();
        }

        public static ICardDataProvider GetCachedProvider(string cardId)
        {
            s_ProviderCache.TryGetValue(cardId, out ICardDataProvider provider);
            return provider;
        }
    }
}
