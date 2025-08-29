using AttributeRenderingLibrary;
using CombatOverhaul;
using CombatOverhaul.Utils;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace FueledWearableLights;

public class WearableFueledLightSourceExntendedStats : WearableFueledLightSourceStats
{
    public Dictionary<string, byte[]> LightHsvByVariant { get; set; } = [];
    public string LightHsvVariantCode { get; set; } = "lining";
}

public class WearableFueledLightSource : Item, IWearableLightSource, IFueledItem, ITogglableItem
{
    public virtual string HotKeyCode => "toggleWearableLight";
    public WearableFueledLightSourceExntendedStats Stats { get; set; } = new();
    public Dictionary<string, float> FuelItems { get; protected set; } = [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        Stats = Attributes.AsObject<WearableFueledLightSourceExntendedStats>();

        if (MaxStackSize > 1)
        {
            LoggerUtil.Error(api, this, $"Item '{Code}' has max stack size > 1 while WearableFueledLightSource supposed to have it set to 1");
        }

        _clickToToggle = new()
        {
            MouseButton = EnumMouseButton.Right,
            ActionLangCode = "combatoverhaul:interaction-toggle-light-source"
        };

        _hotkeyToToggle = new()
        {
            ActionLangCode = Lang.Get("combatoverhaul:interaction-toggle-light-source-hotkey"),
            HotKeyCodes = [HotKeyCode],
            MouseButton = EnumMouseButton.None
        };

        foreach ((string wildcard, float fuel) in Stats.FuelItems)
        {
            foreach (Item item in api.World.Items.Where(item => WildcardUtil.Match(wildcard, item.Code?.ToString() ?? "")))
            {
                FuelItems.Add(GetHeldItemName(item), fuel);
            }
        }
    }
    public override int GetMergableQuantity(ItemStack sinkStack, ItemStack sourceStack, EnumMergePriority priority)
    {
        if (priority == EnumMergePriority.DirectMerge)
        {
            if (GetStackFuel(sourceStack) == 0f)
            {
                return base.GetMergableQuantity(sinkStack, sourceStack, priority);
            }

            return 1;
        }

        return base.GetMergableQuantity(sinkStack, sourceStack, priority);
    }
    public override void TryMergeStacks(ItemStackMergeOperation op)
    {
        if (op.CurrentPriority == EnumMergePriority.DirectMerge)
        {
            float stackFuel = GetStackFuel(op.SourceSlot.Itemstack);
            double fuelHours = GetFuelHours(op.ActingPlayer, op.SinkSlot);
            if (stackFuel > 0f && fuelHours + (double)(stackFuel * Stats.MaxFuelWasteFraction) < (double)Stats.FuelCapacityHours)
            {
                SetFuelHours(op.ActingPlayer, op.SinkSlot, (double)stackFuel + fuelHours);
                op.MovedQuantity = 1;
                op.SourceSlot.TakeOut(1);
                op.SinkSlot.MarkDirty();
            }
            else if (api.Side == EnumAppSide.Client)
            {
                (api as ICoreClientAPI)?.TriggerIngameError(this, "maskfull", Lang.Get("ingameerror-mask-full")); // @TODO change error message
            }
        }
    }
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        if (Stats.NeedsFuel)
        {
            double fuelHours = GetFuelHours((world as IClientWorldAccessor)?.Player, inSlot);
            dsc.AppendLine($"Has fuel for {fuelHours:0.#}/{Stats.FuelCapacityHours:0.#} hours");
            dsc.AppendLine($"Light output: {GetMaxLightHsv(inSlot)[2]}");
            dsc.AppendLine($"Fuel items:");
            foreach ((string name, float fuel) in FuelItems)
            {
                dsc.AppendLine($" - {name} \t: {fuel:0.#} hours");
            }
            dsc.AppendLine();
        }

        dsc.AppendLine();

        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        
    }
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
    {
        if ((byEntity as EntityPlayer)?.Player is IPlayer player)
        {
            Toggle(player, slot);
            handHandling = EnumHandHandling.PreventDefault;
        }

        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
    }
    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        WorldInteraction[] interactions = base.GetHeldInteractionHelp(inSlot);

        return interactions.Append(_clickToToggle).Append(_hotkeyToToggle).ToArray();
    }

    public virtual void AddFuelHours(IPlayer player, ItemSlot slot, double hours)
    {
        if (slot?.Itemstack?.Attributes == null) return;

        if (hours < 0 && !TurnedOn(player, slot)) return;

        slot.Itemstack.Attributes.SetDouble("fuelHours", Math.Max(0.0, hours + GetFuelHours(player, slot)));
        slot.OnItemSlotModified(sinkStack: null);
    }
    public virtual double GetFuelHours(IPlayer player, ItemSlot slot)
    {
        if (slot?.Itemstack?.Attributes == null) return 0;

        return Math.Max(0.0, slot.Itemstack.Attributes.GetDecimal("fuelHours"));
    }
    public virtual bool ConsumeFuelWhenSleeping(IPlayer player, ItemSlot slot) => Stats.ConsumeFuelWhileSleeping;
    public virtual byte[] GetLightHsv(EntityPlayer player, ItemSlot slot)
    {
        byte[] result = Stats.LightHsv;

        Variants variants = Variants.FromStack(slot.Itemstack);
        string? variant = variants.Get(Stats.LightHsvVariantCode);
        if (variant != null && Stats.LightHsvByVariant.ContainsKey(variant))
        {
            result = Stats.LightHsvByVariant[variant];
        }

        if (slot.Itemstack?.Attributes?.HasAttribute("lighthsv") == true)
        {
            result = slot.Itemstack.Attributes
                .GetString("lighthsv")
                .Split('-')
                .Select(int.Parse)
                .Select(i => (byte)i)
                .ToArray();
        }

        return TurnedOn(player.Player, slot) && Stats.NeedsFuel ? result : Stats.TurnedOffLightHsv;
    }
    public virtual byte[] GetMaxLightHsv(ItemSlot slot)
    {
        byte[] result = Stats.LightHsv;

        Variants variants = Variants.FromStack(slot.Itemstack);
        string? variant = variants.Get(Stats.LightHsvVariantCode);
        if (variant != null && Stats.LightHsvByVariant.ContainsKey(variant))
        {
            result = Stats.LightHsvByVariant[variant];
        }

        if (slot.Itemstack?.Attributes?.HasAttribute("lighthsv") == true)
        {
            result = slot.Itemstack.Attributes
                .GetString("lighthsv")
                .Split('-')
                .Select(int.Parse)
                .Select(i => (byte)i)
                .ToArray();
        }

        return result;
    }
    public virtual float GetStackFuel(ItemStack stack)
    {
        foreach ((string itemWildcard, float fuelAmount) in Stats.FuelItems)
        {
            if (WildcardUtil.Match(itemWildcard, stack.Item?.Code.ToString() ?? ""))
            {
                return fuelAmount * Stats.FuelEfficiency;
            }
            else if (WildcardUtil.Match(itemWildcard, stack.Block?.Code.ToString() ?? ""))
            {
                return fuelAmount * Stats.FuelEfficiency;
            }
        }

        return (stack.ItemAttributes?[Stats.FuelAttribute].AsFloat() ?? 0f) * Stats.FuelEfficiency;
    }
    public virtual void SetFuelHours(IPlayer player, ItemSlot slot, double fuelHours)
    {
        if (slot?.Itemstack?.Attributes == null) return;

        fuelHours = GameMath.Clamp(fuelHours, 0, Stats.FuelCapacityHours);
        slot.Itemstack.Attributes.SetDouble("fuelHours", fuelHours);
        slot.MarkDirty();
    }
    public virtual bool TurnedOn(IPlayer player, ItemSlot slot) => slot?.Itemstack?.Attributes?.GetBool(Stats.ToggleAttribute) ?? false;
    public virtual void TurnOn(IPlayer player, ItemSlot slot)
    {
        if (GetFuelHours(player, slot) <= 0)
        {
            TurnOff(player, slot);
            return;
        }

        slot?.Itemstack?.Attributes?.SetBool(Stats.ToggleAttribute, true);

        if (slot?.Itemstack != null)
        {
            Variants variants = Variants.FromStack(slot.Itemstack);
            variants.Set(Stats.ToggleAttribute, "on");
            variants.ToStack(slot.Itemstack);
        }

        slot?.MarkDirty();
    }
    public virtual void TurnOff(IPlayer player, ItemSlot slot)
    {
        slot?.Itemstack?.Attributes?.SetBool(Stats.ToggleAttribute, false);

        if (slot?.Itemstack != null)
        {
            Variants variants = Variants.FromStack(slot.Itemstack);
            variants.Set(Stats.ToggleAttribute, "off");
            variants.ToStack(slot.Itemstack);
        }

        slot?.MarkDirty();
    }
    public virtual void Toggle(IPlayer player, ItemSlot slot)
    {
        if (TurnedOn(player, slot))
        {
            TurnOff(player, slot);
        }
        else
        {
            TurnOn(player, slot);
        }
    }

    private WorldInteraction? _clickToToggle;
    private WorldInteraction? _hotkeyToToggle;

    private string GetHeldItemName(Item item)
    {
        string text = item.ItemClass.Name();
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.Append(Lang.GetMatching(item.Code?.Domain + ":" + text + "-" + item.Code?.Path));
        return stringBuilder.ToString();
    }
}