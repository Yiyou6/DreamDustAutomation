using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace DreamDustAutomation.Planning
{
    [Serializable]
    internal sealed class RunningBatch
    {
        public int schema = 1;
        public BatchPlan plan;
        public int step;
        public int elapsed;
        public int produced;
        public int blenderLoss;
        public int heaterUpgrades;
        public int finisherLoss;
        public bool failed;
        public string failureKey;
        public bool usesRecoveredSolvent;
        public bool deferOutput;
        public bool outputCommitted;
        public int releaseTarget;
        public bool useFinisherTank;
        public int prepared;
        public int originalExpectedBottles;
    }

    internal static class BatchPersistence
    {
        internal const string SaveTag = "DDA_RUNNING_V1";
        internal const string RecoveryTag = "DDA_INCOMPLETE_BATCH_V1";
        internal const string RecoveryReasonTag = "DDA_RECOVERY_REASON_V1";
        private static readonly DefaultContractResolver Contracts = new DefaultContractResolver();

        // Unity's runtime serializer omitted the nested plan from the mod's
        // dynamically loaded types. Use the game's managed JSON library for the
        // entire data graph, without global settings supplied by other mods.
        private static JsonSerializer Serializer() => JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = Contracts,
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MaxDepth = 32,
            Culture = CultureInfo.InvariantCulture
        });

        internal static string Serialize(RunningBatch batch)
        {
            if (batch == null) return "";
            Validate(batch);
            using (var text = new StringWriter(CultureInfo.InvariantCulture))
            using (var writer = new JsonTextWriter(text))
            {
                Serializer().Serialize(writer, batch);
                return text.ToString();
            }
        }

        internal static RunningBatch Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { MaxDepth = 32 })
            {
                var data = JObject.Load(reader);
                if (reader.Read()) throw new InvalidDataException("Trailing batch save data.");
                Require(data, "schema", "plan", "step", "elapsed", "produced", "blenderLoss", "heaterUpgrades", "finisherLoss", "failed");
                if (ReadSchema(data) == 2) Require(data, "useFinisherTank", "prepared", "deferOutput", "outputCommitted", "releaseTarget");
                var plan = data["plan"] as JObject;
                Require(plan, "schema", "requestedGrade", "actualGrade", "expectedBottles", "actions");
                if (ReadSchema(plan) == 3) Require(plan, "directForm");
                var actions = plan["actions"] as JArray;
                if (actions == null) throw Invalid("restore_incomplete", "Missing saved batch actions.");
                foreach (var action in actions) Require(action as JObject, "operation", "source", "amount");
                var batch = data.ToObject<RunningBatch>(Serializer());
                Validate(batch);
                return batch;
            }
        }

        private static int ReadSchema(JObject value)
        {
            // JToken's explicit cast can throw conversion/overflow exceptions
            // outside the recoverable JSON errors. Keep damaged version fields
            // on the same archive-and-unlock path as other invalid batch data.
            var token = value["schema"];
            if (token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int schema))
                throw Invalid("restore_version", "Invalid saved batch version.");
            return schema;
        }

        private static void Require(JObject value, params string[] fields)
        {
            if (value == null || fields.Any(field => value[field] == null || value[field].Type == JTokenType.Null))
                throw Invalid("restore_incomplete", "Incomplete saved batch; its processing plan or progress is missing.");
        }

        private static InvalidDataException Invalid(string key, string message)
        {
            var error = new InvalidDataException(message);
            error.Data[RecoveryReasonTag] = key;
            return error;
        }

        private static void Validate(RunningBatch batch)
        {
            var plan = batch?.plan;
            if (batch != null && ((batch.schema != 1 && batch.schema != 2) || (plan != null && plan.schema != 1 && plan.schema != 2 && plan.schema != 3 && plan.schema != 4)))
                throw Invalid("restore_version", "Unsupported saved batch version.");
            if (plan != null && !ChemicalRecipe.Ids.Contains(plan.productId) && !(plan.schema == 1 && string.IsNullOrEmpty(plan.productId)))
                throw Invalid("restore_product", "Saved product is no longer supported.");
            if (batch == null || (batch.schema != 1 && batch.schema != 2) || plan == null || (plan.schema != 1 && plan.schema != 2 && plan.schema != 3 && plan.schema != 4) ||
                (plan.normalDrying != DirectDryingMethod.Evaporate && plan.normalDrying != DirectDryingMethod.Heat) ||
                (!plan.directForm && plan.normalDrying == DirectDryingMethod.Heat && plan.schema != 4) ||
                (plan.separateDirect && !plan.directForm) || !DirectDryingPolicy.Valid(plan.directDrying) ||
                (plan.directDrying != DirectDryingMethod.LegacyAutomatic && !plan.separateDirect) ||
                plan.actions == null || plan.actions.Length == 0 || batch.step < 0 || batch.step > plan.actions.Length ||
                batch.elapsed < 0 || batch.produced < 0 || batch.produced > plan.expectedBottles || plan.expectedBottles <= 0 || plan.outputCapacity < plan.expectedBottles ||
                plan.requestedBottles < 0 || (plan.requestedBottles > 0 && (plan.requestedBottles != plan.expectedBottles || plan.availableBottles < plan.requestedBottles)) ||
                batch.blenderLoss < 0 || batch.blenderLoss > 100 || batch.finisherLoss < 0 || batch.finisherLoss > 100 || batch.heaterUpgrades < 0 ||
                !AlcoholBudget.Percentages.Contains(plan.alcoholPercent) || plan.extractionAlcohol < 0 || plan.extractionPortions < 0 ||
                plan.dilutionVolume < 0 || plan.dilutionVolume > plan.reagents ||
                (plan.directForm ? plan.schema != 3 || plan.requestedGrade != 0 || plan.actualGrade != 0 :
                    !GradePolicy.Grades.Contains(plan.requestedGrade) || !GradePolicy.Grades.Contains(plan.actualGrade) || plan.actualGrade > plan.requestedGrade) ||
                (!ChemicalRecipe.Ids.Contains(plan.productId) && !(plan.schema == 1 && string.IsNullOrEmpty(plan.productId))))
                throw new InvalidDataException("Unsupported or incomplete saved batch.");
            foreach (var action in plan.actions)
            {
                if (action != null && !DirectDryingPolicy.Allows(plan.directDrying, action.operation))
                    throw Invalid("restore_operation", "Saved drying action conflicts with the selected method.");
                if (plan.directForm && action != null && action.operation != Operation.Blend && action.operation != Operation.Dissolve &&
                    action.operation != Operation.Pour && action.operation != Operation.FeedMash && action.operation != Operation.StoreMash && action.operation != Operation.React && action.operation != Operation.Dry && action.operation != Operation.Form && action.operation != Operation.BoilDry &&
                    !(plan.separateDirect && (action.operation == Operation.ReturnFeed || action.operation == Operation.SwapTanks)))
                    throw Invalid("restore_operation", "Direct forming cannot filter, extract or synthesize.");
                if (action != null && ((!plan.separateDirect && (action.operation == Operation.ReturnFeed || action.operation == Operation.SwapTanks)) ||
                    (plan.separateDirect && (action.operation == Operation.React || action.operation == Operation.FeedMash ||
                        (action.operation == Operation.Form && action.amount == 0)))))
                    throw Invalid("restore_operation", "Invalid independent forming action.");
                if (action != null && action.operation == Operation.BoilDry &&
                    ((plan.directForm ? action.targetTemperature != HeatedDrying.TargetTemperature :
                        plan.schema != 4 || plan.normalDrying != DirectDryingMethod.Heat || !NormalDrying.Temperatures.Contains(action.targetTemperature)) ||
                        action.amount > HeatedDrying.TickLimit))
                    throw Invalid("restore_operation", "Invalid saved boiling action.");
                if (action == null || !Enum.IsDefined(typeof(Operation), action.operation) || action.amount < 0 ||
                    ((action.operation == Operation.Blend || action.operation == Operation.Dissolve || action.operation == Operation.Pour ||
                        action.operation == Operation.Filter || action.operation == Operation.ReturnFeed) && action.source < 0))
                    throw Invalid("restore_operation", "Invalid saved batch action.");
                if (action.preFilters != null && (action.operation != Operation.Filter || action.preFilters.Length > 288 ||
                    action.preFilters.Any(i => i < 0 || i == action.source) || action.preFilters.Distinct().Count() != action.preFilters.Length))
                    throw Invalid("restore_operation", "Invalid saved coarse-filter sources.");
            }
            if ((batch.schema == 2) != batch.useFinisherTank || batch.prepared < 0 || batch.prepared > plan.expectedBottles ||
                (batch.originalExpectedBottles != 0 && batch.originalExpectedBottles != plan.expectedBottles + 1) ||
                (!batch.useFinisherTank && (batch.deferOutput || batch.prepared != 0)) ||
                (batch.useFinisherTank && batch.produced > batch.prepared) ||
                batch.releaseTarget < 0 || batch.releaseTarget > plan.expectedBottles ||
                (!batch.deferOutput && (batch.outputCommitted || batch.releaseTarget != 0)) ||
                (batch.deferOutput && (batch.releaseTarget < batch.produced || batch.releaseTarget > batch.prepared || batch.outputCommitted != (batch.prepared > 0))) ||
                (batch.deferOutput && !batch.outputCommitted && (batch.releaseTarget != 0 || batch.produced != 0)) ||
                (batch.outputCommitted && !plan.actions.Take(Math.Min(plan.actions.Length, batch.step + 1)).Any(a => a.operation == Operation.Form)) ||
                (batch.deferOutput && batch.releaseTarget > plan.FormTarget(Math.Min(batch.step, plan.actions.Length - 1))))
                throw Invalid("restore_progress", "Invalid deferred output permission or progress.");
            if (plan.separateDirect)
            {
                long total = plan.actions.Where(a => a.operation == Operation.Form).Sum(a => (long)a.amount);
                long previous = plan.actions.Take(batch.step).Where(a => a.operation == Operation.Form).Sum(a => (long)a.amount);
                long current = batch.step < plan.actions.Length && plan.actions[batch.step].operation == Operation.Form ? plan.actions[batch.step].amount : 0;
                long progress = batch.useFinisherTank ? batch.prepared : batch.produced;
                if (total != plan.expectedBottles || progress < previous || progress > previous + current ||
                    (!batch.deferOutput && batch.produced < previous))
                    throw Invalid("restore_progress", "Independent lot progress does not match its forming actions.");
            }
            if (batch.step == plan.actions.Length)
            {
                if (batch.elapsed != 0 || (batch.deferOutput ? batch.prepared : batch.produced) != plan.expectedBottles)
                    throw Invalid("restore_progress", "Invalid completed batch progress.");
            }
            else
            {
                var current = plan.actions[batch.step];
                bool timed = ReactionProgress.IsTimed(current.operation);
                if ((timed && batch.elapsed > current.amount) || (!timed && batch.elapsed != 0))
                    throw Invalid("restore_progress", "Invalid saved reaction progress.");
            }
        }

        internal static void Save(GameItem machine, RunningBatch batch)
        {
            string json = Serialize(batch);
            WriteMetadata(machine, SaveTag, json, true);
            if (batch != null)
            {
                WriteMetadata(machine, RecoveryTag, "", false);
                WriteMetadata(machine, RecoveryReasonTag, "", false);
            }
        }

        private static void WriteMetadata(GameItem machine, string key, string value, bool enabled)
        {
            // These private save-only tags have no native shape/state effects.
            // Per-bottle checkpoints must not revalidate every nested inventory.
            Action<TagState> write = tag => { if (enabled) tag.Enable(); else tag.Disable(); tag.SetString(value); };
            machine.ModifyTag(key, write, skipValidate: true);
            if (!ReferenceEquals(machine.state, machine.modifiedState)) write(machine.modifiedState.GetTag(key));
        }

        internal static RunningBatch Load(GameItem machine, out string error)
        {
            error = null;
            string json = machine.GetTagReadonly(SaveTag).GetString();
            try { return Deserialize(json); }
            catch (Exception exception) when (exception is JsonException || exception is InvalidDataException)
            {
                // Old releases already lost the plan before writing the save.
                // Archive that metadata and unlock replanning, without changing
                // any ingredients, tank contents, products or source identities.
                machine.ModifyTag(RecoveryTag, tag => tag.Enable().SetString(json));
                machine.ModifyTag(SaveTag, tag => tag.Enable().SetString(""));
                WriteMetadata(machine, RecoveryReasonTag, (string)exception.Data[RecoveryReasonTag] ?? "restore_invalid", true);
                error = exception.Message;
                return null;
            }
        }
    }
}
