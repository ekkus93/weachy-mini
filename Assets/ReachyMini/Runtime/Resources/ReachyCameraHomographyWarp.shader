Shader "Hidden/ReachyMini/CameraHomographyWarp"
{
    Properties
    {
        _MainTex ("Normalized phone RGB", 2D) = "black" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4x4 _ReachyToPhonePixels;
    float4 _SourceSize;
    float4 _OutputSize;

    struct WarpSample
    {
        float2 sourceUv;
        float valid;
    };

    WarpSample MapOutputToSource(float2 unityOutputUv)
    {
        WarpSample result;
        float2 outputTopLeft = float2(
            unityOutputUv.x,
            1.0 - unityOutputUv.y);
        float2 outputPixel =
            outputTopLeft * _OutputSize.xy - 0.5;
        float3 sourceHomogeneous = mul(
            _ReachyToPhonePixels,
            float4(outputPixel, 1.0, 1.0)).xyz;

        result.valid = sourceHomogeneous.z > 1.0e-6 ? 1.0 : 0.0;
        float safeDepth = max(sourceHomogeneous.z, 1.0e-6);
        float2 sourcePixel = sourceHomogeneous.xy / safeDepth;
        if (sourcePixel.x < 0.0 ||
            sourcePixel.x > _SourceSize.x - 1.0 ||
            sourcePixel.y < 0.0 ||
            sourcePixel.y > _SourceSize.y - 1.0)
        {
            result.valid = 0.0;
        }

        float2 sourceTopLeftUv =
            (sourcePixel + 0.5) * _SourceSize.zw;
        result.sourceUv = float2(
            sourceTopLeftUv.x,
            1.0 - sourceTopLeftUv.y);
        return result;
    }

    fixed4 FragmentColor(v2f_img input) : SV_Target
    {
        WarpSample mapped = MapOutputToSource(input.uv);
        if (mapped.valid < 0.5)
        {
            return fixed4(0.0, 0.0, 0.0, 1.0);
        }
        return tex2D(_MainTex, mapped.sourceUv);
    }

    fixed4 FragmentValidity(v2f_img input) : SV_Target
    {
        WarpSample mapped = MapOutputToSource(input.uv);
        return fixed4(mapped.valid, 0.0, 0.0, 1.0);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "COLOR"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragmentColor
            #pragma target 3.0
            ENDCG
        }

        Pass
        {
            Name "VALIDITY"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragmentValidity
            #pragma target 3.0
            ENDCG
        }
    }

    Fallback Off
}
