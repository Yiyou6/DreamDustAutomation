using System;
using DreamDustAutomation.Planning;

static class GradePolicyTests
{
    static int checks;
    static void Check(bool value, string label)
    {
        checks++;
        if (!value) throw new Exception(label);
    }
    static ProductionOutcome Outcome(int bottles, int minimumActive, int reagent = 0) =>
        new ProductionOutcome(bottles * minimumActive, bottles * 2000, bottles, minimumActive, reagent);

    public static int Main()
    {
        var high = Outcome(5, 1980);
        var target = Outcome(8, 1800);
        var lower = Outcome(20, 1600);
        var selected = GradePolicy.Select(90, new[] { high, lower, target });
        Check(selected.ActualGrade == 90 && selected.Outcome == target, "Maximize bottles within requested grade.");
        selected = GradePolicy.Select(99, new[] { lower, target });
        Check(selected.ActualGrade == 90 && selected.IsFallback, "Fallback is the highest attainable lower tier.");
        var manyLow = Outcome(100, 600);
        selected = GradePolicy.Select(90, new[] { manyLow, lower });
        Check(selected.ActualGrade == 80 && selected.Outcome == lower, "90% must fall back to feasible 80%, never 30% for more bottles.");
        var attempts = GradePolicy.EvaluateFallback(90, new[] { manyLow, lower });
        Check(attempts.Count == 2 && attempts[0].Grade == 90 && attempts[0].BestOutcome == null &&
            attempts[1].Grade == 80 && attempts[1].BestOutcome == lower, "Displayed attempts must stop at 80% when feasible.");
        int[] fallbackGrades = { 80, 60, 45, 30 };
        foreach (int fallback in fallbackGrades)
        {
            var feasible = Outcome(1, fallback * 20);
            attempts = GradePolicy.EvaluateFallback(90, new[] { manyLow, feasible });
            Check(attempts[attempts.Count - 1].Grade == fallback, "Stop at first feasible tier: " + fallback);
            for (int i = 0; i < attempts.Count; i++)
                Check(attempts[i].Grade == GradePolicy.Grades[i + 1], "Do not skip a tier while falling back to " + fallback);
        }
        attempts = GradePolicy.EvaluateFallback(90, new[] { Outcome(1, 599) });
        Check(attempts.Count == 5 && attempts.TrueForAll(x => x.BestOutcome == null), "Show every failed tier when even 30% is unavailable.");
        selected = GradePolicy.Select(80, new[] { high, lower });
        Check(selected.Outcome == lower && !selected.IsFallback, "Do not insist on a higher tier at the cost of output.");
        Check(GradePolicy.Select(99, new ProductionOutcome[0]) == null, "No ingredients produces no result.");
        Check(GradePolicy.Select(30, new[] { Outcome(10, 599) }) == null, "Reject below minimum grade.");
        Check(!GradePolicy.Meets(Outcome(1, 1799), 90), "89.95% must not qualify as 90% by display rounding.");
        Check(GradePolicy.Meets(Outcome(1, 1800), 90), "Exact threshold qualifies.");
        Check(!GradePolicy.Meets(Outcome(0, 2000), 99), "An empty batch never qualifies.");
        var misleadingAverage = new ProductionOutcome(3600, 4000, 2, 1799);
        Check(!GradePolicy.Meets(misleadingAverage, 90), "Every bottle must meet the target.");
        var cheap = Outcome(8, 1800, 1000);
        var expensive = Outcome(8, 1800, 2000);
        Check(GradePolicy.Select(90, new[] { expensive, cheap }).Outcome == cheap, "Equal output saves reagents.");
        var fineFirst = new ProductionOutcome(16000, 16000, 8, 2000, fineFilterWear: 100);
        var clothFirst = new ProductionOutcome(16000, 16000, 8, 2000, fineFilterWear: 10, coarseFilterWear: 90);
        Check(GradePolicy.Select(99, new[] { fineFirst, clothFirst }).Outcome == clothFirst, "Equal output saves fine cartridge durability before cloth.");
        var fewer = new ProductionOutcome(14000, 14000, 7, 2000, fineFilterWear: 0);
        Check(GradePolicy.Select(99, new[] { fewer, clothFirst }).Outcome == clothFirst, "Filter savings must not reduce quantity.");
        var lessPure = new ProductionOutcome(15920, 16000, 8, 1990, fineFilterWear: 0);
        Check(GradePolicy.Select(99, new[] { lessPure, clothFirst }).Outcome == lessPure, "Save filters once every unit meets the target.");
        var belowGrade = new ProductionOutcome(15832, 16000, 8, 1979, fineFilterWear: 0);
        Check(GradePolicy.Select(99, new[] { belowGrade, clothFirst }).Outcome == clothFirst, "Filter savings must never lower the selected grade.");
        bool rejected = false;
        try { GradePolicy.Select(95, new[] { high }); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Unapproved custom grades are rejected.");
        foreach (int grade in GradePolicy.Grades)
        {
            Check(GradePolicy.Meets(Outcome(1, grade * 20), grade), "Grade boundary " + grade);
            Check(!GradePolicy.Meets(Outcome(1, grade * 20 - 1), grade), "Below grade boundary " + grade);
        }
        Console.WriteLine("PASS: " + checks + " grade-policy checks");
        return 0;
    }
}
