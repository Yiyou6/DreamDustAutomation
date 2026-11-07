using System;

namespace DreamDustAutomation.Planning
{
    internal sealed class EvaporationEstimate
    {
        internal int Volume, DailyVolume;
        // Number of remaining overnight cycles at the current performance,
        // assuming uninterrupted power. Null means there is no positive rate.
        internal int? Days => Volume <= 0 ? 0 : DailyVolume <= 0 ? (int?)null :
            (int)(((long)Volume + DailyVolume - 1) / DailyVolume);

        internal static EvaporationEstimate Read(GameItem tank, GameItem evaporator)
        {
            int daily = 0;
            if (evaporator != null)
            {
                // Match the native evaporator's float arithmetic and rounding.
                float multiplier = Math.Max(0f, 1f + MachineryHelper.GetCurrentPerformanceBonus(evaporator) / 100f);
                daily = Math.Max(0, (int)Math.Round(300000f * multiplier));
            }
            return new EvaporationEstimate
            {
                Volume = tank == null ? 0 : ChemistryOperations.VolatileVolume(tank),
                DailyVolume = daily
            };
        }

        // Each drying stage occupies its own native overnight cycles. Spare
        // capacity from one night cannot be spent on the next heated/mixed lot.
        internal static EvaporationForecast Forecast(BatchPlan plan, int dailyVolume, int step = 0, GameItem currentTank = null)
        {
            var result = new EvaporationForecast { DailyVolume = Math.Max(0, dailyVolume) };
            if (plan?.actions == null) return result;
            long days = 0;
            for (int i = Math.Max(0, step); i < plan.actions.Length; i++)
            {
                var action = plan.actions[i];
                if (action.operation != Operation.Dry) continue;
                int volume = i == step && currentTank != null ? ChemistryOperations.VolatileVolume(currentTank) : action.evaporationVolume;
                if (volume == 0) continue;
                result.Stages++;
                if (volume < 0) { result.Complete = false; continue; }
                result.Volume += volume;
                if (result.DailyVolume > 0) days += ((long)volume + result.DailyVolume - 1) / result.DailyVolume;
            }
            result.Days = !result.Complete || (result.Stages > 0 && result.DailyVolume == 0) ? (int?)null :
                (int)Math.Min(int.MaxValue, days);
            return result;
        }
    }

    internal sealed class EvaporationForecast
    {
        internal long Volume;
        internal int DailyVolume, Stages;
        internal bool Complete = true;
        internal int? Days = 0;
    }
}
