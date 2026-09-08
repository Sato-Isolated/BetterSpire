// Source-linked doubles model the inspected clone/event ordering, not the game engine.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BetterSpire2.HandViewer;
using BetterSpire2.Guardian.Core;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

internal static class NativePreviewTests
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
    static int Main()
    {
        try
        {
            var card = new CardModel { Enchantment = new EnchantmentModel(), Affliction = new AfflictionModel() };
            int notifications = 0;
            card.EnchantmentChanged += () => notifications++;
            card.AfflictionChanged += () => notifications++;
            card.ExecutionFinished += () => notifications++;
            card.NativeCalculation = () => 17;
            card.DynamicVars["Damage"].Preview = 123;
            card.Enchantment.DynamicVars["Damage"].Preview = 456;
            for (int i = 0; i < 1000; i++)
            {
                string text = DetachedCardPreview.Description(card);
                Check(text == "Hand:17:17", "original identity with isolated output and original pile");
                Check(card.DynamicVars["Damage"].Preview == 123, "live target preview untouched");
                Check(card.Enchantment.DynamicVars["Damage"].Preview == 456, "live enchantment untouched");
                Check(notifications == 0, "deep clone does not notify original subscribers");
                Check(card.NativeCalculation() == 17, "non-event delegates preserved");
            }
            card.NotifyEnchantment(); card.NotifyAffliction(); card.NotifyExecution();
            Check(notifications == 3, "live subscribers remain attached");
            var alias = new CardModel { AliasVariables = true };
            bool rejected = false;
            try { DetachedCardPreview.Description(alias); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "reject native clone sharing variables");
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ExternalPreviewTest"), AssemblyBuilderAccess.Run);
            var type = assembly.DefineDynamicModule("Mod").DefineType("ExternalCard", TypeAttributes.Public, typeof(CardModel));
            type.DefineDefaultConstructor(MethodAttributes.Public);
            var external = (CardModel)Activator.CreateInstance(type.CreateType()!)!;
            rejected = false;
            try { DetachedCardPreview.Description(external); } catch (NotSupportedException) { rejected = true; }
            Check(rejected, "unreviewed external clone overrides not executed");
            Check(ForecastHookPolicy.IsReaction("BeforeDamageGiven"), "before damage guarded");
            Check(ForecastHookPolicy.IsReaction("AfterDiedToDoom"), "Doom death reaction guarded");
            Check(ForecastHookPolicy.IsReaction("AfterNewFutureEffect"), "new reaction defaults uncertain");
            Check(!ForecastHookPolicy.IsReaction("ModifyDamageAdditive"), "pure modifier remains native");
            Console.WriteLine($"PASS {checks} source-linked preview isolation and forecast-coverage checks (not game rendering).");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL " + ex); return 1; }
    }
}
namespace MegaCrit.Sts2.Core.Entities.Cards
{
    public enum CardPreviewMode { Normal }
    public enum PileType { Hand }
    public sealed class CardPile { public PileType Type => PileType.Hand; }
}
namespace MegaCrit.Sts2.Core.Models
{
    public sealed class Variable { public int Preview = 4; public Variable Clone() => new() { Preview = Preview }; }
    public sealed class Variables : Dictionary<string, Variable>
    {
        public Variables() { this["Damage"] = new(); }
        public Variables Clone() { var result = new Variables(); foreach (var p in this) result[p.Key] = p.Value.Clone(); return result; }
        public void ClearPreview() { foreach (var v in Values) v.Preview = 4; }
    }
    public abstract class AbstractModel
    {
        public event Action? ExecutionFinished;
        public AbstractModel MutableClone()
        {
            var copy = (AbstractModel)MemberwiseClone();
            copy.DeepCloneFields(); copy.AfterCloned(); return copy;
        }
        protected virtual void DeepCloneFields() { }
        protected virtual void AfterCloned() { ExecutionFinished = null; }
        public void NotifyExecution() => ExecutionFinished?.Invoke();
    }
    public class EnchantmentModel : AbstractModel
    {
        public Variables DynamicVars = new();
        protected override void DeepCloneFields() { DynamicVars = DynamicVars.Clone(); }
    }
    public class AfflictionModel : AbstractModel { }
    public class CardModel : AbstractModel
    {
        public event Action? EnchantmentChanged;
        public event Action? AfflictionChanged;
        public EnchantmentModel? Enchantment;
        public AfflictionModel? Affliction;
        public Variables DynamicVars = new();
        public Func<int> NativeCalculation = () => 4;
        public CardPile? Pile = new();
        public bool AliasVariables;
        public void NotifyEnchantment() => EnchantmentChanged?.Invoke();
        public void NotifyAffliction() => AfflictionChanged?.Invoke();
        protected override void DeepCloneFields()
        {
            if (!AliasVariables) DynamicVars = DynamicVars.Clone();
            if (Enchantment != null)
            { Enchantment = (EnchantmentModel)Enchantment.MutableClone(); NotifyEnchantment(); }
            if (Affliction != null)
            { Affliction = (AfflictionModel)Affliction.MutableClone(); NotifyAffliction(); }
        }
        protected override void AfterCloned()
        { base.AfterCloned(); EnchantmentChanged = null; AfflictionChanged = null; Pile = null; }
        public void UpdateDynamicVarPreview(CardPreviewMode mode, object? target, Variables output)
        {
            if (Pile == null) throw new Exception("calculation lost original card identity");
            if (ReferenceEquals(output, DynamicVars)) throw new Exception("writing shared preview variables");
            foreach (var variable in output.Values) variable.Preview = NativeCalculation();
        }
        public string GetDescriptionForPile(PileType pile, object? target) =>
            $"{pile}:{DynamicVars["Damage"].Preview}:{Enchantment?.DynamicVars["Damage"].Preview}";
    }
}
