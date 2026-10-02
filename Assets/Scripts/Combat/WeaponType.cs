// WeaponType.cs
// Purpose: Data-driven weapon categories for player and monster loadouts.
// Used by: PlayerCombat, MonsterController, LevelManager

/// <summary>
/// Assign weapons per level from the Inspector — do not hardcode in battle logic.
/// </summary>
public enum WeaponType
{
    None = 0,   // Bare hands / claws / unarmed
    Hammer = 1,
    Sword = 2,
    Shield = 3,
    // Level 3 selectable pairs (picked up in the arena, see WeaponPickup / PlayerCombat.weaponSets)
    Mace = 4,
    Flail = 5,
    Katana = 6,
    // Level 3 boss: one axe in each hand (monster only)
    DualAxe = 7
}
