using System;
using System.Collections.Generic;

namespace DreamDustAutomation.Planning
{
    public sealed class ProductionOutcome
    {
        public int ActiveVolume { get; }
        public int TotalVolume { get; }
        public int Bottles { get; }
        public int MinimumBottleActiveVolume { get; }
        public int ReagentVolume { get; }
        public int ProcessingSeconds { get; }
        public int UnitVolume { get; }
        public int FineFilterWear { get; }
        public int CoarseFilterWear { get; }
        public object Plan { get; }

        public ProductionOutcome(int activeVolume, int totalVolume, int bottles,
            int minimumBottleActiveVolume, int reagentVolume = 0, int processingSeconds = 0, object plan = null,
            int unitVolume = GradePolicy.BottleVolume, int fineFilterWear = 0, int coarseFilterWear = 0)
        {
            if (activeVolume < 0 || totalVolume < activeVolume || bottles < 0 ||
                unitVolume <= 0 || minimumBottleActiveVolume < 0 || minimumBottleActiveVolume > unitVolume ||
                reagentVolume < 0 || processingSeconds < 0 || fineFilterWear < 0 || coarseFilterWear < 0)
                throw new ArgumentOutOfRangeException(nameof(activeVolume));
            ActiveVolume = activeVolume;
            TotalVolume = totalVolume;
            Bottles = bottles;
            MinimumBottleActiveVolume = minimumBottleActiveVolume;
            ReagentVolume = reagentVolume;
            ProcessingSeconds = processingSeconds;
            UnitVolume = unitVolume;
            FineFilterWear = fineFilterWear;
            CoarseFilterWear = coarseFilterWear;
            Plan = plan;
        }
    }

    public sealed class GradeSelection
    {
        public int RequestedGrade { get; }
        public int ActualGrade { get; }
        public ProductionOutcome Outcome { get; }
        public bool IsFallback => ActualGrade < RequestedGrade;
        public GradeSelection(int requested, int actual, ProductionOutcome outcome)
        { RequestedGrade = requested; ActualGrade = actual; Outcome = outcome; }
    }

    public sealed class GradeAttempt
    {
        public int Grade { get; }
        public ProductionOutcome BestOutcome { get; }
        public GradeAttempt(int grade, ProductionOutcome bestOutcome)
        { Grade = grade; BestOutcome = bestOutcome; }
    }

    public static class GradePolicy
    {
        public const int BottleVolume = 2000;
        public static readonly int[] Grades = { 99, 90, 80, 60, 45, 30 };

        // Compare integer amounts, not the rounded percentage displayed in the UI.
        // Every bottle must qualify; a good batch average cannot hide a weak bottle.
        public static bool Meets(ProductionOutcome outcome, int grade) =>
            outcome != null && outcome.Bottles > 0 &&
            (long)outcome.MinimumBottleActiveVolume * 100 >= (long)outcome.UnitVolume * grade;

        public static GradeSelection Select(int requestedGrade, IEnumerable<ProductionOutcome> outcomes)
        {
            foreach (var attempt in EvaluateFallback(requestedGrade, outcomes))
                if (attempt.BestOutcome != null)
                    return new GradeSelection(requestedGrade, attempt.Grade, attempt.BestOutcome);
            return null;
        }

        // This same ordered list drives both selection and the player's per-grade
        // results. Stop at the first feasible grade, even if lower grades yield more.
        public static List<GradeAttempt> EvaluateFallback(int requestedGrade, IEnumerable<ProductionOutcome> outcomes)
        {
            if (Array.IndexOf(Grades, requestedGrade) < 0)
                throw new ArgumentOutOfRangeException(nameof(requestedGrade));
            if (outcomes == null) throw new ArgumentNullException(nameof(outcomes));
            var candidates = new List<ProductionOutcome>(outcomes);
            var attempts = new List<GradeAttempt>();
            foreach (int grade in Grades)
            {
                if (grade > requestedGrade) continue;
                ProductionOutcome best = null;
                foreach (var candidate in candidates)
                    if (Meets(candidate, grade) && Better(candidate, best)) best = candidate;
                attempts.Add(new GradeAttempt(grade, best));
                if (best != null) break;
            }
            return attempts; // Below 30% is not silently manufactured as a valid grade.
        }

        internal static bool Better(ProductionOutcome candidate, ProductionOutcome current)
        {
            if (current == null) return true;
            if (candidate.Bottles != current.Bottles) return candidate.Bottles > current.Bottles;
            // Both candidates already meet the selected per-unit grade. At equal
            // yield, conserve fine filters before chasing surplus purity.
            if (candidate.FineFilterWear != current.FineFilterWear) return candidate.FineFilterWear < current.FineFilterWear;
            if (candidate.MinimumBottleActiveVolume != current.MinimumBottleActiveVolume)
                return candidate.MinimumBottleActiveVolume > current.MinimumBottleActiveVolume;
            if (candidate.ActiveVolume != current.ActiveVolume) return candidate.ActiveVolume > current.ActiveVolume;
            if (candidate.CoarseFilterWear != current.CoarseFilterWear) return candidate.CoarseFilterWear < current.CoarseFilterWear;
            if (candidate.ReagentVolume != current.ReagentVolume) return candidate.ReagentVolume < current.ReagentVolume;
            return candidate.ProcessingSeconds < current.ProcessingSeconds;
        }
    }
}
