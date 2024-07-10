using System;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using StardewValley.Inventories;
using StardewValley.Objects;

namespace StardewValley.Buildings
{
	[Obsolete("The Mill class is only used to preserve data from old save files. All mills were converted into plain Building instances based on the rules in Data/Buildings. The input and output items are now stored in Building.buildingChests with the 'Input' and 'Output' keys respectively.")]
	public class Mill : Building
	{
		/// <summary>Obsolete. The <c>Mill</c> class is only used to preserve data from old save files. All mills were converted into plain <see cref="T:StardewValley.Buildings.Building" /> instances, with the input items in <see cref="F:StardewValley.Buildings.Building.buildingChests" /> with the <c>Input</c> key.</summary>
		[XmlElement("input")]
		public Chest obsolete_input;

		/// <summary>Obsolete. The <c>Mill</c> class is only used to preserve data from old save files. All mills were converted into plain <see cref="T:StardewValley.Buildings.Building" /> instances, with the output items in <see cref="F:StardewValley.Buildings.Building.buildingChests" /> with the <c>Output</c> key.</summary>
		[XmlElement("output")]
		public Chest obsolete_output;

		public Mill(Vector2 tileLocation)
			: base("Mill", tileLocation)
		{
		}

		public Mill()
			: this(Vector2.Zero)
		{
		}

		/// <summary>Copy the data from this mill to a new data-driven building instance.</summary>
		/// <param name="targetBuilding">The new building that will replace this instance.</param>
		public void TransferValuesToNewBuilding(Building targetBuilding)
		{
			Chest chest = obsolete_input;
			if (chest != null && chest.Items?.Count > 0)
			{
				IInventory source2 = obsolete_input.Items;
				Chest target2 = targetBuilding.GetBuildingChest("Input");
				for (int j = 0; j < source2.Count; j++)
				{
					Item item2 = source2[j];
					if (item2 != null)
					{
						source2[j] = null;
						target2.addItem(item2);
					}
				}
				obsolete_input = null;
			}
			Chest chest2 = obsolete_output;
			if (chest2 == null || !(chest2.Items?.Count > 0))
			{
				return;
			}
			IInventory source = obsolete_output.Items;
			Chest target = targetBuilding.GetBuildingChest("Output");
			for (int i = 0; i < source.Count; i++)
			{
				Item item = source[i];
				if (item != null)
				{
					source[i] = null;
					target.addItem(item);
				}
			}
			obsolete_output = null;
		}
	}
}
