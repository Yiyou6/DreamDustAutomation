using System;

namespace DreamDustAutomation.Planning
{
    // A data-only item: no Unity objects, inventories, live callbacks or shared tags.
    // TagSystem.Clone() in this game is SHALLOW. Never use it to clone live inputs.
    internal sealed class SimulationItem : GameItem
    {
        public SimulationItem(GameItem source = null) : base("dda_simulation")
        {
            if (source != null)
            {
                identifier = source.identifier;
                foreach (var pair in source.modifiedState.dict)
                    state.dict.Add(pair.Key, pair.Value.Clone());
            }
            else
            {
                state.GetTag(WaterHelper.LIQUID_CONTAINER_TAG).Enable();
                state.GetTag(ChemicalHelper.CHEM_ITEM_TAG).Enable();
                state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).Enable().SetInt(10000000);
                state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).Enable().SetDouble(26);
                foreach (var liquid in Liquid.Liquids) state.GetTag(liquid.currentPart).Enable().SetInt(0);
            }
            modifiedState = state;
            // Do not attach item features: their native registration allocates a
            // live save ID and their updates can reach localization and rendering.
        }
        public override void Destroy() { }
        public override void Validate() { modifiedState = state; }
    }
}
