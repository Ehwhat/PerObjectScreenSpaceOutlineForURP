Shader "CustomEffects/Blur"
{
    HLSLINCLUDE
    
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // The Blit.hlsl file provides the vertex shader (Vert),
        // the input structure (Attributes), and the output structure (Varyings)
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        // urp vert shader
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float _VerticalBlur;
        float _HorizontalBlur;
        float _Alpha = 1;
        
        float4 BlurVertical (Varyings input) : SV_Target
        {
            const float BLUR_SAMPLES = 64;
            const float BLUR_SAMPLES_RANGE = BLUR_SAMPLES / 2;
            
            float3 color = 0;
            float maxAlpha = 0;
            float blurPixels = _VerticalBlur * _ScreenParams.y;
            
            for(float i = -BLUR_SAMPLES_RANGE; i <= BLUR_SAMPLES_RANGE; i++)
            {
                float2 sampleOffset = float2 (0, (blurPixels / _BlitTexture_TexelSize.w) * (i / BLUR_SAMPLES_RANGE));
                float4 sample = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord + sampleOffset);
                color += sample.rgb;
                maxAlpha = max(maxAlpha, sample.a);
            }
            
            return float4(color / (BLUR_SAMPLES + 1), maxAlpha);
        }

        float4 BlurHorizontal (Varyings input) : SV_Target
        {
            const float BLUR_SAMPLES = 64;
            const float BLUR_SAMPLES_RANGE = BLUR_SAMPLES / 2;
            
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float3 color = 0;
            float maxAlpha = 0;
            float blurPixels = _HorizontalBlur * _ScreenParams.x;
            for(float i = -BLUR_SAMPLES_RANGE; i <= BLUR_SAMPLES_RANGE; i++)
            {
                float2 sampleOffset =
                    float2 ((blurPixels / _BlitTexture_TexelSize.z) * (i / BLUR_SAMPLES_RANGE), 0);
                float4 sample = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord + sampleOffset);
                color += sample.rgb;
                maxAlpha = max(maxAlpha, sample.a);
            }
            return float4(color / (BLUR_SAMPLES + 1), maxAlpha);
        }

        float4 Step (Varyings input) : SV_Target
        {
            float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord);
            return step( 0.1,color.r);
        }


        float4 Additive (Varyings input) : SV_Target
        {
            float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord);
            return color;
        }

          float4 Additive2 (Varyings input) : SV_Target
        {
            float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord);
            return color;
        }
        
        float4 Draw (Varyings input) : SV_Target
        {
            return float4(1, 1, 1, _Alpha);
        }
    
    ENDHLSL
    
    SubShader
    {

        Pass
        {
            Name "BlurPassVertical"
            Blend One Zero
            
            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment BlurVertical
            
            ENDHLSL
        }
        
        Pass
        {
            Name "BlurPassHorizontal"
            Blend One Zero
            
            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment BlurHorizontal
            
            ENDHLSL
        }

        Pass
        {
            Name "OutlineCompositePass"
            Blend SrcAlpha One
            ZTest Always
            ZWrite Off
            
            Stencil
            {
                Ref 6
                Comp NotEqual
                Pass Keep
            }
            
            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment FragBilinear
            
            ENDHLSL
        }

        Pass
        {
            Name "InfillCompositePass"
            Blend SrcAlpha One
            ZTest Always
            ZWrite Off
            
            Stencil
            {
                Ref 6
                Comp Equal
                Pass Keep
            }
            
            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment FragBilinear
            
            ENDHLSL
        }
        
        Pass
        {
            Name "DrawPass"
            ZWrite Off
            ZTest LEqual
            Stencil
            {
                Ref 6
                Comp always
                Pass replace
            }
            
            HLSLPROGRAM
            
            #pragma vertex Vert3D
            #pragma fragment Draw

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes3D
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings3D
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings3D Vert3D(Attributes3D IN)
            {
                Varyings3D OUT;

                // ✅ Use URP's built-in function to transform object-space -> clip-space
                VertexPositionInputs vertexInput = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = vertexInput.positionCS;
                OUT.uv = IN.uv;

                return OUT;
            }
            
            ENDHLSL
        }
    }
}
