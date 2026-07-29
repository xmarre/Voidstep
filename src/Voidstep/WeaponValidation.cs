using TaleWorlds.MountAndBlade;

namespace Voidstep
{
    internal static class WeaponValidation
    {
        internal static bool IsUsableMeleeWeapon(MissionWeapon weapon)
        {
            if (weapon.IsEmpty)
                return false;

            try
            {
                var usage = weapon.CurrentUsageItem;
                return usage != null && usage.IsMeleeWeapon;
            }
            catch
            {
                return false;
            }
        }
    }
}
