using System.Collections.Generic;

namespace DreamDustAutomation
{
    internal static class AdjacentCharging
    {
        // Forward native array charging to the actual embedded battery without
        // adding duplicate storage nodes or changing the native charge rules.
        internal static void Apply(GameItem evaporator, IEnumerable<GameItem> adjacent, bool powerOn, bool commercialPower)
        {
            if (!powerOn || evaporator?.identifier != "evaporator" || adjacent == null) return;
            var seen = new HashSet<GameItem>();
            foreach (var item in adjacent)
                if (item != null && item.identifier == "powerblock" && seen.Add(item))
                    MachineHelper.RechargeMachineBattery(evaporator, commercialPower ? 4 : 2);
        }
    }
}
