using StardewValley.Tools;

namespace StardewValley.Enchantments
{
	public class PowerfulEnchantment : BaseEnchantment
	{
		public override string GetName()
		{
			return "Powerful";
		}

		public override bool CanApplyTo(Item item)
		{
			if (item is Tool)
			{
				if (!(item is Pickaxe))
				{
					return item is Axe;
				}
				return true;
			}
			return false;
		}

		protected override void _ApplyTo(Item item)
		{
			base._ApplyTo(item);
			Pickaxe pickaxe = item as Pickaxe;
			if (pickaxe == null)
			{
				Axe axe = item as Axe;
				if (axe != null)
				{
					axe.additionalPower.Value += 2 * GetLevel();
				}
			}
			else
			{
				pickaxe.additionalPower.Value += GetLevel();
			}
		}

		protected override void _UnapplyTo(Item item)
		{
			base._UnapplyTo(item);
			Pickaxe pickaxe = item as Pickaxe;
			if (pickaxe == null)
			{
				Axe axe = item as Axe;
				if (axe != null)
				{
					axe.additionalPower.Value -= 2 * GetLevel();
				}
			}
			else
			{
				pickaxe.additionalPower.Value -= GetLevel();
			}
		}
	}
}
