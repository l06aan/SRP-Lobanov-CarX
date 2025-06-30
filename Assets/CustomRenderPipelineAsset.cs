using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Custom SRP Asset")]
public class CustomRenderPipelineAsset : RenderPipelineAsset
{
    public Cubemap environmentMap; 

    protected override RenderPipeline CreatePipeline()
    {
        return new CustomRenderPipeline(environmentMap);
    }
}
