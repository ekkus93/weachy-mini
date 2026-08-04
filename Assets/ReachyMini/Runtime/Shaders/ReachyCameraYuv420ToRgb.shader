Shader "Hidden/ReachyMini/CameraYuv420ToRgb"
{
    Properties
    {
        _YTexture ("Y", 2D) = "black" {}
        _UTexture ("U", 2D) = "gray" {}
        _VTexture ("V", 2D) = "gray" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            sampler2D _YTexture;
            sampler2D _UTexture;
            sampler2D _VTexture;
            float4 _YTexture_TexelSize;
            float4 _CropScaleOffset;
            float _RotationQuarterTurns;
            float _MirrorX;
            float _ColorStandard;
            float _ColorRange;

            float2 InverseRotate(float2 outputUv, int quarterTurns)
            {
                if (quarterTurns == 1)
                {
                    return float2(outputUv.y, 1.0 - outputUv.x);
                }
                if (quarterTurns == 2)
                {
                    return float2(1.0 - outputUv.x, 1.0 - outputUv.y);
                }
                if (quarterTurns == 3)
                {
                    return float2(1.0 - outputUv.y, outputUv.x);
                }
                return outputUv;
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                // Work in top-left image coordinates. Unity render-target UVs use
                // bottom-left origin, and the packed Android plane rows begin at
                // the top of the image.
                float2 outputTopLeft = float2(input.uv.x, 1.0 - input.uv.y);
                if (_MirrorX > 0.5)
                {
                    outputTopLeft.x = 1.0 - outputTopLeft.x;
                }

                int quarterTurns = ((int)round(_RotationQuarterTurns)) & 3;
                float2 cropUv = InverseRotate(outputTopLeft, quarterTurns);
                float2 sourceTopLeft =
                    _CropScaleOffset.zw + cropUv * _CropScaleOffset.xy;
                float2 sourceUv = float2(sourceTopLeft.x, 1.0 - sourceTopLeft.y);
                float2 halfTexel = 0.5 * _YTexture_TexelSize.xy;
                sourceUv = clamp(sourceUv, halfTexel, 1.0 - halfTexel);

                float ySample = tex2D(_YTexture, sourceUv).r;
                float uSample = tex2D(_UTexture, sourceUv).r;
                float vSample = tex2D(_VTexture, sourceUv).r;

                float y;
                float cb;
                float cr;
                if (_ColorRange > 0.5)
                {
                    y = ySample;
                    cb = uSample - 0.5;
                    cr = vSample - 0.5;
                }
                else
                {
                    y = (ySample * 255.0 - 16.0) / 219.0;
                    cb = (uSample * 255.0 - 128.0) / 224.0;
                    cr = (vSample * 255.0 - 128.0) / 224.0;
                }

                float3 rgb;
                if (_ColorStandard > 0.5)
                {
                    rgb = float3(
                        y + 1.5748 * cr,
                        y - 0.187324 * cb - 0.468124 * cr,
                        y + 1.8556 * cb);
                }
                else
                {
                    rgb = float3(
                        y + 1.402 * cr,
                        y - 0.344136 * cb - 0.714136 * cr,
                        y + 1.772 * cb);
                }

                return fixed4(saturate(rgb), 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
