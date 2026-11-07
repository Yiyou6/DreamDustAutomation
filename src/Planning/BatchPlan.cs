using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamDustAutomation.Planning
{
    // Append only: existing saves persist these numeric values.
    internal enum Operation { Blend, Pour, Extract, React, Filter, Dry, Form, Dissolve, Synthesize, StoreMash, FeedMash, BoilDry, ReturnFeed, SwapTanks }

    [Serializable]
    internal sealed class BatchAction
    {
        public Operation operation;
        public int source;
        public int amount;
        public int targetTemperature;
        public string precursorId;
        public int endAmount;
        // Optional coarse filters applied to the same withdrawn portion, before source.
        public int[] preFilters;
        // Preview metadata only. -1 preserves unknown estimates in old saves.
        public int evaporationVolume = -1;
        public BatchAction(Operation operation, int source = -1, int amount = 0, int targetTemperature = 0)
        { this.operation = operation; this.source = source; this.amount = amount; this.targetTemperature = targetTemperature; }
        public BatchAction Copy()
        { var result = (BatchAction)MemberwiseClone(); result.preFilters = preFilters == null ? null : (int[])preFilters.Clone(); return result; }
        internal IEnumerable<int> FilterSources => (preFilters ?? new int[0]).Concat(new[] { source });
    }

    [Serializable]
    internal sealed class BatchPlan
    {
        public int schema = 2;
        public bool directForm;
        public bool separateDirect;
        public DirectDryingMethod directDrying;
        public DirectDryingMethod normalDrying = DirectDryingMethod.Evaporate;
        public string normalHeatReason;
        public int normalHeatBottles, normalHeatMinimumActive, normalHeatSeconds, evaporationBottles, boiledAlcohol;
        public bool directSolvents;
        public string productId;
        public int requestedGrade;
        public int actualGrade;
        public int expectedBottles;
        public int outputCapacity = int.MaxValue;
        public bool outputLimited;
        // Zero is the legacy/default "all" setting. Capacity and requested count
        // are separate: a player limit must not claim the warehouse is full.
        public int requestedBottles;
        public int availableBottles;
        internal int FormingLimit => requestedBottles > 0 ? Math.Min(outputCapacity, requestedBottles) : outputCapacity;
        public int minimumActive;
        public int reactionSeconds;
        public int reagents;
        public int recoveredVolume;
        // Optional preview/candidate costs; old saves default to zero. The
        // actual filter actions and native durability remain authoritative.
        public int fineFilterWear, coarseFilterWear;
        public int alcoholPercent = 100;
        public int extractionAlcohol;
        public int extractionPortions;
        public string concentrationHint;
        public string quantityHint;
        public BatchAction[] actions;
        internal int FormTarget(int step) => separateDirect ?
            actions.Take(step + 1).Where(action => action.operation == Operation.Form).Sum(action => action.amount) : expectedBottles;
        public BatchPlan Copy()
        {
            return new BatchPlan { schema = schema, directForm = directForm, separateDirect = separateDirect, directDrying = directDrying, directSolvents = directSolvents, productId = productId, requestedGrade = requestedGrade, actualGrade = actualGrade,
                expectedBottles = expectedBottles, minimumActive = minimumActive, outputCapacity = outputCapacity, outputLimited = outputLimited,
                requestedBottles = requestedBottles, availableBottles = availableBottles,
                reactionSeconds = reactionSeconds, reagents = reagents, recoveredVolume = recoveredVolume,
                fineFilterWear = fineFilterWear, coarseFilterWear = coarseFilterWear,
                alcoholPercent = alcoholPercent, extractionAlcohol = extractionAlcohol, extractionPortions = extractionPortions,
                concentrationHint = concentrationHint, quantityHint = quantityHint,
                normalDrying = normalDrying, normalHeatReason = normalHeatReason, normalHeatBottles = normalHeatBottles,
                normalHeatMinimumActive = normalHeatMinimumActive, normalHeatSeconds = normalHeatSeconds,
                evaporationBottles = evaporationBottles, boiledAlcohol = boiledAlcohol,
                actions = actions.Select(action => action.Copy()).ToArray() };
        }
    }

    internal sealed class BatchInputs
    {
        public DirectDryingMethod normalDrying = DirectDryingMethod.Evaporate;
        public bool skipDryingComparison;
        public int maxProducts = int.MaxValue;
        // Internal metered-feed planning only; never changes the saved inventory.
        public bool feedPrepared;
        public int reservedBufferVolume;
        public bool directForm;
        public bool separateDirect;
        public DirectDryingMethod directDrying;
        public bool directSolvents;
        public string productId;
        public GameItem[] raw;
        public GameItem[] supplies;
        public int recoveredSource = -1;
        public GameItem chamber;
        public GameItem finisher;
        public bool deferOutput;
        public GameItem mashBuffer;
        // Zero preserves the pre-option planner API; the machine supplies an explicit grade.
        public int alcoholPercent;
        public int blenderLoss;
        public int heaterUpgrades;
        public int finisherLoss;
        public int requestedGrade;
    }
}
