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
    Shield = 3
}
