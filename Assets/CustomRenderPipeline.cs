using UnityEngine;
using UnityEngine.Rendering;

public class CustomRenderPipeline : RenderPipeline
{
    private RenderTexture shadowMap;
    private Matrix4x4 lightVP;
    private Cubemap iblCubemap;

    public CustomRenderPipeline(Cubemap cubemap)
    {
        iblCubemap = cubemap;
        GraphicsSettings.useScriptableRenderPipelineBatching = true;
    }

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        foreach (var camera in cameras)
        {
            if (!camera.TryGetCullingParameters(out var cullingParams))
                continue;
            var cull = context.Cull(ref cullingParams);

            Material mainMaterial = null;
            foreach (var r in GameObject.FindObjectsOfType<Renderer>())
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null && m.shader != null && m.shader.name.Contains("SRP_PBR"))
                    {
                        mainMaterial = m;
                        break;
                    }
                }
                if (mainMaterial != null) break;
            }

            float shadowStrength = mainMaterial != null && mainMaterial.HasProperty("_ShadowStrength") ? mainMaterial.GetFloat("_ShadowStrength") : 0.9f;
            float shadowBias = mainMaterial != null && mainMaterial.HasProperty("_ShadowBias") ? mainMaterial.GetFloat("_ShadowBias") : 0.005f;
            int shadowMapSize = mainMaterial != null && mainMaterial.HasProperty("_ShadowMapSize") ? Mathf.RoundToInt(mainMaterial.GetFloat("_ShadowMapSize")) : 1024;
            float iblIntensity = mainMaterial != null && mainMaterial.HasProperty("_IBLIntensity") ? mainMaterial.GetFloat("_IBLIntensity") : 1.0f;
            float aniso = mainMaterial != null && mainMaterial.HasProperty("_Aniso") ? mainMaterial.GetFloat("_Aniso") : 0.0f;

            Shader.SetGlobalFloat("_ShadowStrength", shadowStrength);
            Shader.SetGlobalFloat("_ShadowBias", shadowBias);
            Shader.SetGlobalFloat("_ShadowMapSize", shadowMapSize);
            Shader.SetGlobalFloat("_IBLIntensity", iblIntensity);
            Shader.SetGlobalFloat("_Aniso", aniso);
            Shader.SetGlobalVector("_AmbientColor", (Vector4)RenderSettings.ambientLight);

            if (iblCubemap != null)
                Shader.SetGlobalTexture("_EnvCubemap", iblCubemap);

            // Directional Light
            Vector3 lightDir = Vector3.down;
            Color lightColor = Color.black;
            Light lightRef = null;

            foreach (var l in cull.visibleLights)
            {
                if (l.lightType == LightType.Directional)
                {
                    lightDir = -l.localToWorldMatrix.MultiplyVector(Vector3.forward).normalized;
                    if (l.light != null)
                        lightColor = l.light.color * l.light.intensity;
                    else
                        lightColor = l.finalColor;
                    lightRef = l.light;
                    break;
                }
            }
            Shader.SetGlobalVector("_DirectionalLightColor", (Vector4)lightColor);
            Shader.SetGlobalVector("_DirectionalLightDirection", new Vector4(lightDir.x, lightDir.y, lightDir.z, 0));
            Shader.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);

            // SHADOW PASS 
            if (lightRef != null)
            {
                if (shadowMap == null || shadowMap.width != shadowMapSize)
                {
                    if (shadowMap != null) shadowMap.Release();
                    shadowMap = new RenderTexture(shadowMapSize, shadowMapSize, 16, RenderTextureFormat.Shadowmap);
                    shadowMap.filterMode = FilterMode.Bilinear;
                    shadowMap.wrapMode = TextureWrapMode.Clamp;
                }

                // Bbox всей сцены
                Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
                bool hasBounds = false;
                foreach (var r in GameObject.FindObjectsOfType<Renderer>())
                {
                    if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                    else { bounds.Encapsulate(r.bounds); }
                }
                Vector3 shadowCenter = bounds.center;
                float range = Mathf.Max(bounds.extents.x, bounds.extents.z, 10f) * 1.2f;

                Vector3 lightPos = shadowCenter - lightDir * (range + 10f);
                Quaternion lightRot = Quaternion.LookRotation(lightDir, Vector3.up);
                Matrix4x4 view = Matrix4x4.TRS(lightPos, lightRot, Vector3.one).inverse;
                Matrix4x4 proj = Matrix4x4.Ortho(-range, range, -range, range, 0.1f, 2 * (range + 10f));
                lightVP = proj * view;

                CommandBuffer shadowCmd = new CommandBuffer { name = "ShadowMapPass" };
                shadowCmd.SetRenderTarget(shadowMap);
                shadowCmd.ClearRenderTarget(true, true, Color.clear);
                context.ExecuteCommandBuffer(shadowCmd);
                shadowCmd.Release();

                var shadowDrawingSettings = new DrawingSettings(new ShaderTagId("SRPShadowCaster"), new SortingSettings());
                var shadowFilteringSettings = new FilteringSettings(RenderQueueRange.opaque);
                context.DrawRenderers(cull, ref shadowDrawingSettings, ref shadowFilteringSettings);
            }

            Shader.SetGlobalTexture("_ShadowMap", shadowMap);
            Shader.SetGlobalMatrix("_LightMatrixVP", lightVP);

            // MAIN PASS
            context.SetupCameraProperties(camera);

            var cmd = new CommandBuffer { name = "Clear" };
            cmd.ClearRenderTarget(true, true, Color.black);
            context.ExecuteCommandBuffer(cmd);
            cmd.Release();

            context.DrawSkybox(camera);

            var sortingSettings = new SortingSettings(camera) { criteria = SortingCriteria.CommonOpaque };
            var drawingSettings = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), sortingSettings);
            var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            context.DrawRenderers(cull, ref drawingSettings, ref filteringSettings);

            sortingSettings.criteria = SortingCriteria.CommonTransparent;
            drawingSettings.sortingSettings = sortingSettings;
            filteringSettings = new FilteringSettings(RenderQueueRange.transparent);
            context.DrawRenderers(cull, ref drawingSettings, ref filteringSettings);

            context.Submit();
        }
    }
}
