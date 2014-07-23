﻿// Copyright (c) Aura development team - Licensed under GNU GPL
// For more information, see license file in the main folder

namespace Aura.Shared.Mabi.Const
{
	/// <summary>
	/// Inventory pockets
	/// </summary>
	/// <remarks>
	/// Every item is stored in a specific pocket.
	/// </remarks>
	public enum Pocket : byte
	{
		None = 0,
		Cursor = 1,
		Inventory = 2,
		Face = 3,
		Hair = 4,
		Armor = 5,
		Glove = 6,
		Shoe = 7,
		Head = 8,
		Robe = 9,

		// Actual RIGHT hand (left side in inv).
		RightHand1 = 10,
		RightHand2 = 11,

		// Actual LEFT hand (right side in inv).
		LeftHand1 = 12,
		LeftHand2 = 13,

		// Arrows go here, not in the left hand.
		Magazine1 = 14,
		Magazine2 = 15,

		Accessory1 = 16,
		Accessory2 = 17,

		Trade = 19,
		Temporary = 20,
		Quests = 23,
		Trash = 24,
		BattleReward = 28,
		EnchantReward = 29,
		ManaCrystalReward = 30,
		Falias1 = 32,
		Falias2 = 33,
		Falias3 = 34,
		Falias4 = 35,
		ComboCard = 41,
		ArmorStyle = 43,
		GloveStyle = 44,
		ShoeStyle = 45,
		HeadStyle = 46,
		RobeStyle = 47,
		PersonalInventory = 49,
		VIPInventory = 50,
		FarmStone = 81,
		ItemBags = 100,
		ItemBagsMax = 199,
	}

	public enum BagTags
	{
		Equipment = 0x01,
		RecoveryPotion = 0x02,
		Artifact = 0x04,
		AlchemyCrystal = 0x08,
		Herb = 0x10,
		ThreadBall = 0x20,
		Cloth = 0x40,
		Ore = 0x80,
		Gem = 0x100,
		CullinStone = 0x200,
		Firewood = 0x400,
		Fish = 0x800,
		Food = 0x1000,
		Enchants = 0x2000,
		Pass = 0x4000,
		FomorScroll = 0x8000,
		AncientBook = 0x10000,
	}

	/// <summary>
	/// Extensions for Pocket enum.
	/// </summary>
	public static class PocketExtensions
	{
		/// <summary>
		/// Returns true if pocket is an equipment pocket (incl Face and Hair).
		/// </summary>
		/// <param name="pocket"></param>
		/// <returns></returns>
		public static bool IsEquip(this Pocket pocket)
		{
			if ((pocket >= Pocket.Face && pocket <= Pocket.Accessory2) || (pocket >= Pocket.ArmorStyle && pocket <= Pocket.RobeStyle))
				return true;
			return false;
		}
	}

	/// <summary>
	/// Attack speed of a weapon.
	/// </summary>
	/// <remarks>
	/// Used in ItemOptionInfo.
	/// </remarks>
	public enum AttackSpeed : byte
	{
		VeryFast,
		Fast,
		Normal,
		Slow,
		VerySlow,
	}
}
