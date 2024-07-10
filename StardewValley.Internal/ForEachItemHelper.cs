using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Netcode;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Delegates;
using StardewValley.Inventories;
using StardewValley.Locations;
using StardewValley.SpecialOrders;

namespace StardewValley.Internal
{
	/// <summary>Iterates through every item in the game state and optionally edits, replaces, or removes instances.</summary>
	/// <remarks>This is a low-level class. Most code should use a utility method like <see cref="M:StardewValley.Utility.ForEachItem(System.Func{StardewValley.Item,System.Boolean})" /> or <see cref="M:StardewValley.Utility.ForEachItem(StardewValley.Delegates.ForEachItemDelegate)" /> instead.</remarks>
	public static class ForEachItemHelper
	{
		/// <summary>Perform an action for each item in the game world, including items within items (e.g. in a chest or on a table), hats placed on children, items in player inventories, etc.</summary>
		/// <param name="handler">The action to perform for each item.</param>
		/// <returns>Returns whether to continue iterating if needed (i.e. returns false if the last <paramref name="handler" /> call did).</returns>
		public static bool ForEachItemInWorld(ForEachItemDelegate handler)
		{
			bool canContinue = true;
			Utility.ForEachLocation((GameLocation location) => canContinue = ForEachItemInLocation(location, handler));
			if (!canContinue)
			{
				return false;
			}
			using (IEnumerator<Farmer> enumerator = Game1.getAllFarmers().GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					_003C_003Ec__DisplayClass0_1 _003C_003Ec__DisplayClass0_ = new _003C_003Ec__DisplayClass0_1();
					_003C_003Ec__DisplayClass0_.farmer = enumerator.Current;
					_003C_003Ec__DisplayClass0_2 _003C_003Ec__DisplayClass0_2 = new _003C_003Ec__DisplayClass0_2();
					_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1 = _003C_003Ec__DisplayClass0_;
					_003C_003Ec__DisplayClass0_2.toolIndex = _003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.CurrentToolIndex;
					if (!ApplyToList(_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.Items, handler, true, _003C_003Ec__DisplayClass0_2._003CForEachItemInWorld_003Eg__OnChangedItemSlot_007C2) || !ApplyToField(_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.shirtItem, handler, _003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1._003CForEachItemInWorld_003Eg__OnChangedEquipment_007C1) || !ApplyToField(_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.pantsItem, handler, _003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1._003CForEachItemInWorld_003Eg__OnChangedEquipment_007C1) || !ApplyToField(_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.boots, handler, _003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1._003CForEachItemInWorld_003Eg__OnChangedEquipment_007C1) || !ApplyToField(_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.hat, handler, _003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1._003CForEachItemInWorld_003Eg__OnChangedEquipment_007C1) || !ApplyToField(_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.leftRing, handler, _003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1._003CForEachItemInWorld_003Eg__OnChangedEquipment_007C1) || !ApplyToField(_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.rightRing, handler, _003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1._003CForEachItemInWorld_003Eg__OnChangedEquipment_007C1) || !ApplyToItem(_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.recoveredItem, handler, delegate
					{
						_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.recoveredItem = null;
					}, delegate(Item newItem)
					{
						_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.recoveredItem = PrepareForReplaceWith(_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.recoveredItem, newItem);
					}) || !ApplyToField(_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.toolBeingUpgraded, handler) || !ApplyToList(_003C_003Ec__DisplayClass0_2.CS_0024_003C_003E8__locals1.farmer.itemsLostLastDeath, handler))
					{
						return false;
					}
				}
			}
			if (!ApplyToList(Game1.player.team.returnedDonations, handler))
			{
				return false;
			}
			foreach (Inventory value in Game1.player.team.globalInventories.Values)
			{
				if (!ApplyToList(value, handler))
				{
					return false;
				}
			}
			foreach (SpecialOrder specialOrder in Game1.player.team.specialOrders)
			{
				if (!ApplyToList(specialOrder.donatedItems, handler))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Perform an action for each item within a location, including items within items (e.g. in a chest or on a table), hats placed on children, items in player inventories, etc.</summary>
		/// <param name="location">The location whose items to iterate.</param>
		/// <param name="handler">The action to perform for each item.</param>
		/// <returns>Returns whether to continue iterating if needed (i.e. returns false if the last <paramref name="handler" /> call did).</returns>
		public static bool ForEachItemInLocation(GameLocation location, ForEachItemDelegate handler)
		{
			_003C_003Ec__DisplayClass1_0 _003C_003Ec__DisplayClass1_ = new _003C_003Ec__DisplayClass1_0();
			_003C_003Ec__DisplayClass1_.location = location;
			if (_003C_003Ec__DisplayClass1_.location == null)
			{
				return true;
			}
			if (!ApplyToList(_003C_003Ec__DisplayClass1_.location.furniture, handler))
			{
				return false;
			}
			foreach (NPC character in _003C_003Ec__DisplayClass1_.location.characters)
			{
				Child child = character as Child;
				if (child == null)
				{
					Horse horse = character as Horse;
					if (horse == null)
					{
						Pet pet = character as Pet;
						if (pet != null && !ApplyToField(pet.hat, handler))
						{
							return false;
						}
					}
					else if (!ApplyToField(horse.hat, handler))
					{
						return false;
					}
				}
				else if (!ApplyToField(child.hat, handler))
				{
					return false;
				}
			}
			foreach (Building building in _003C_003Ec__DisplayClass1_.location.buildings)
			{
				if (!building.ForEachItemExcludingInterior(handler))
				{
					return false;
				}
			}
			if ((!(_003C_003Ec__DisplayClass1_.location.GetFridge(false)?.ForEachItem(handler))) ?? false)
			{
				return false;
			}
			if (_003C_003Ec__DisplayClass1_.location.objects.Length > 0)
			{
				using (Dictionary<Vector2, Object>.KeyCollection.Enumerator enumerator3 = _003C_003Ec__DisplayClass1_.location.objects.Keys.GetEnumerator())
				{
					while (enumerator3.MoveNext())
					{
						_003C_003Ec__DisplayClass1_0 _003C_003Ec__DisplayClass1_2 = _003C_003Ec__DisplayClass1_;
						Vector2 tile = enumerator3.Current;
						if (!ApplyToItem(_003C_003Ec__DisplayClass1_2.location.objects[tile], handler, delegate
						{
							_003C_003Ec__DisplayClass1_2.location.objects.Remove(tile);
						}, delegate(Item newItem)
						{
							_003C_003Ec__DisplayClass1_2.location.objects[tile] = PrepareForReplaceWith(_003C_003Ec__DisplayClass1_2.location.objects[tile], (Object)newItem);
						}))
						{
							return false;
						}
					}
				}
			}
			_003C_003Ec__DisplayClass1_2 _003C_003Ec__DisplayClass1_3 = new _003C_003Ec__DisplayClass1_2();
			_003C_003Ec__DisplayClass1_3.CS_0024_003C_003E8__locals2 = _003C_003Ec__DisplayClass1_;
			_003C_003Ec__DisplayClass1_3.i = _003C_003Ec__DisplayClass1_3.CS_0024_003C_003E8__locals2.location.debris.Count - 1;
			while (_003C_003Ec__DisplayClass1_3.i >= 0)
			{
				_003C_003Ec__DisplayClass1_3 _003C_003Ec__DisplayClass1_4 = new _003C_003Ec__DisplayClass1_3();
				_003C_003Ec__DisplayClass1_4.CS_0024_003C_003E8__locals3 = _003C_003Ec__DisplayClass1_3;
				_003C_003Ec__DisplayClass1_4.d = _003C_003Ec__DisplayClass1_4.CS_0024_003C_003E8__locals3.CS_0024_003C_003E8__locals2.location.debris[_003C_003Ec__DisplayClass1_4.CS_0024_003C_003E8__locals3.i];
				if (_003C_003Ec__DisplayClass1_4.d.item != null && !ApplyToItem(_003C_003Ec__DisplayClass1_4.d.item, handler, _003C_003Ec__DisplayClass1_4._003CForEachItemInLocation_003Eg__Remove_007C2, _003C_003Ec__DisplayClass1_4._003CForEachItemInLocation_003Eg__ReplaceWith_007C3))
				{
					return false;
				}
				_003C_003Ec__DisplayClass1_3.i--;
			}
			ShopLocation shopLocation = _003C_003Ec__DisplayClass1_.location as ShopLocation;
			if (shopLocation != null)
			{
				if (!ApplyToList(shopLocation.itemsFromPlayerToSell, handler))
				{
					return false;
				}
				if (!ApplyToList(shopLocation.itemsToStartSellingTomorrow, handler))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Apply a for-each-item callback to an item.</summary>
		/// <typeparam name="TItem">The item type.</typeparam>
		/// <param name="item">The item instance to iterate.</param>
		/// <param name="handler">The action to perform for each item.</param>
		/// <param name="remove">Delete this item instance.</param>
		/// <param name="replaceWith">Replace this item with a new instance.</param>
		/// <returns>Returns whether to continue iterating if needed.</returns>
		public static bool ApplyToItem<TItem>(TItem item, ForEachItemDelegate handler, Action remove, Action<Item> replaceWith) where TItem : Item
		{
			_003C_003Ec__DisplayClass2_0<TItem> _003C_003Ec__DisplayClass2_ = new _003C_003Ec__DisplayClass2_0<TItem>();
			_003C_003Ec__DisplayClass2_.remove = remove;
			_003C_003Ec__DisplayClass2_.item = item;
			_003C_003Ec__DisplayClass2_.replaceWith = replaceWith;
			if (_003C_003Ec__DisplayClass2_.item == null)
			{
				return true;
			}
			if (handler(_003C_003Ec__DisplayClass2_.item, _003C_003Ec__DisplayClass2_._003CApplyToItem_003Eg__Remove_007C0, _003C_003Ec__DisplayClass2_._003CApplyToItem_003Eg__ReplaceWith_007C1))
			{
				return _003C_003Ec__DisplayClass2_.item?.ForEachItem(handler) ?? true;
			}
			return false;
		}

		/// <summary>Apply a for-each-item callback to an item.</summary>
		/// <typeparam name="TItem">The item type.</typeparam>
		/// <param name="field">The field instance to iterate.</param>
		/// <param name="handler">The action to perform for each item.</param>
		/// <param name="onChanged">A callback to invoke when the assigned value changes, which receives the old and new items.</param>
		/// <returns>Returns whether to continue iterating if needed.</returns>
		public static bool ApplyToField<TItem>(NetRef<TItem> field, ForEachItemDelegate handler, Action<Item, Item> onChanged = null) where TItem : Item
		{
			Item oldValue = field.Value;
			return ApplyToItem(field.Value, handler, delegate
			{
				field.Value = null;
				onChanged?.Invoke(oldValue, null);
			}, delegate(Item newItem)
			{
				field.Value = PrepareForReplaceWith(field.Value, (TItem)newItem);
				onChanged?.Invoke(oldValue, newItem);
			});
		}

		/// <summary>Apply a for-each-item callback to an item.</summary>
		/// <typeparam name="TItem">The item type.</typeparam>
		/// <param name="list">The list of items to iterate.</param>
		/// <param name="handler">The action to perform for each item.</param>
		/// <param name="leaveNullSlotsOnRemoval">Whether to leave a null entry in the list when an item is removed. If <c>false</c>, the index is removed from the list instead.</param>
		/// <param name="onChanged">A callback to invoke when the assigned value changes, which receives the old and new items.</param>
		/// <returns>Returns whether to continue iterating if needed.</returns>
		public static bool ApplyToList<TItem>(IList<TItem> list, ForEachItemDelegate handler, bool leaveNullSlotsOnRemoval = false, Action<Item, Item, int> onChanged = null) where TItem : Item
		{
			_003C_003Ec__DisplayClass4_0<TItem> _003C_003Ec__DisplayClass4_ = new _003C_003Ec__DisplayClass4_0<TItem>();
			_003C_003Ec__DisplayClass4_.leaveNullSlotsOnRemoval = leaveNullSlotsOnRemoval;
			_003C_003Ec__DisplayClass4_.list = list;
			_003C_003Ec__DisplayClass4_.onChanged = onChanged;
			_003C_003Ec__DisplayClass4_.i = _003C_003Ec__DisplayClass4_.list.Count - 1;
			while (_003C_003Ec__DisplayClass4_.i >= 0)
			{
				_003C_003Ec__DisplayClass4_1<TItem> _003C_003Ec__DisplayClass4_2 = new _003C_003Ec__DisplayClass4_1<TItem>();
				_003C_003Ec__DisplayClass4_2.CS_0024_003C_003E8__locals1 = _003C_003Ec__DisplayClass4_;
				_003C_003Ec__DisplayClass4_2.oldValue = _003C_003Ec__DisplayClass4_2.CS_0024_003C_003E8__locals1.list[_003C_003Ec__DisplayClass4_2.CS_0024_003C_003E8__locals1.i];
				if (!ApplyToItem(_003C_003Ec__DisplayClass4_2.CS_0024_003C_003E8__locals1.list[_003C_003Ec__DisplayClass4_2.CS_0024_003C_003E8__locals1.i], handler, _003C_003Ec__DisplayClass4_2._003CApplyToList_003Eg__Remove_007C0, _003C_003Ec__DisplayClass4_2._003CApplyToList_003Eg__ReplaceWith_007C1))
				{
					return false;
				}
				_003C_003Ec__DisplayClass4_.i--;
			}
			return true;
		}

		/// <summary>Prepare a new item instance as a replacement for an existing item.</summary>
		/// <param name="previousItem">The existing item that's being replaced.</param>
		/// <param name="newItem">The new item that will replace <paramref name="previousItem" />.</param>
		/// <returns>Returns the <paramref name="newItem" /> for convenience.</returns>
		private static TItem PrepareForReplaceWith<TItem>(TItem previousItem, TItem newItem) where TItem : Item
		{
			Object previousObj = previousItem as Object;
			Object newObj = newItem as Object;
			if (previousObj != null && newObj != null)
			{
				newObj.TileLocation = previousObj.TileLocation;
			}
			return newItem;
		}
	}
}
