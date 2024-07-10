using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Netcode;
using StardewValley.GameData.Shops;
using StardewValley.Network;

namespace StardewValley.Util
{
	public class SynchronizedShopStock : INetObject<NetFields>
	{
		private readonly NetStringDictionary<int, NetInt> stockDictionary = new NetStringDictionary<int, NetInt>();

		protected static HashSet<string> _usedKeys = new HashSet<string>();

		protected static List<ISalable> _stockSalables = new List<ISalable>();

		public NetFields NetFields { get; } = new NetFields("SynchronizedShopStock");


		public SynchronizedShopStock()
		{
			initNetFields();
		}

		private void initNetFields()
		{
			NetFields.SetOwner(this).AddField(stockDictionary, "stockDictionary");
		}

		public virtual void Clear()
		{
			stockDictionary.Clear();
		}

		public void OnItemPurchased(string shop_id, ISalable item, Dictionary<ISalable, ItemStockInformation> stock, int amount)
		{
			NetStringDictionary<int, NetInt> sharedStock = stockDictionary;
			ItemStockInformation stockData;
			if (stock.TryGetValue(item, out stockData) && stockData.Stock != int.MaxValue)
			{
				string key = GetQualifiedSyncedKey(shop_id, stockData);
				stockData.Stock -= amount;
				stock[item] = stockData;
				sharedStock[key] = stockData.Stock;
			}
		}

		public string GetQualifiedSyncedKey(string shop_id, ItemStockInformation item)
		{
			if (item.LimitedStockMode == LimitedStockMode.Global)
			{
				return shop_id + "/Global/" + item.SyncedKey;
			}
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(2, 3);
			defaultInterpolatedStringHandler.AppendFormatted(shop_id);
			defaultInterpolatedStringHandler.AppendLiteral("/");
			defaultInterpolatedStringHandler.AppendFormatted(Game1.player.UniqueMultiplayerID);
			defaultInterpolatedStringHandler.AppendLiteral("/");
			defaultInterpolatedStringHandler.AppendFormatted(item.SyncedKey);
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}

		public void UpdateLocalStockWithSyncedQuanitities(string shop_id, Dictionary<ISalable, ItemStockInformation> local_stock)
		{
			_usedKeys.Clear();
			_stockSalables.Clear();
			List<ISalable> items_to_remove = new List<ISalable>();
			_stockSalables.AddRange(local_stock.Keys);
			foreach (ISalable salable in _stockSalables)
			{
				ItemStockInformation stock_data = local_stock[salable];
				if (stock_data.Stock == int.MaxValue || stock_data.LimitedStockMode == LimitedStockMode.None)
				{
					continue;
				}
				if (stock_data.SyncedKey == null)
				{
					string base_key = salable.Name;
					string key = base_key;
					int collision_count = 1;
					while (_usedKeys.Contains(key))
					{
						key = base_key + collision_count;
						collision_count++;
					}
					_usedKeys.Add(key);
					stock_data.SyncedKey = key;
					local_stock[salable] = stock_data;
				}
				string qualified_key = GetQualifiedSyncedKey(shop_id, stock_data);
				int stock;
				if (stockDictionary.TryGetValue(qualified_key, out stock))
				{
					stock_data.Stock = stock;
					local_stock[salable] = stock_data;
					if (stock <= 0)
					{
						items_to_remove.Add(salable);
					}
				}
			}
			_usedKeys.Clear();
			_stockSalables.Clear();
			foreach (Item item in items_to_remove)
			{
				local_stock.Remove(item);
			}
		}
	}
}
