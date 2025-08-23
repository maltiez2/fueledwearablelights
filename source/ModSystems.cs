using Vintagestory.API.Common;

namespace FueledWearableLights;

public sealed class FueledWearableLightsSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("FueledWearableLights:WearableFueledLightSource", typeof(WearableFueledLightSource));
        api.RegisterCollectibleBehaviorClass("FueledWearableLights:ShapeTexturesFromAttributes", typeof(ShapeTexturesFromAttributes));
    }
}