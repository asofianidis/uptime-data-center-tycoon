Shader "DC/Lit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Emission ("Emission", Color) = (0,0,0,0)
        _Glossiness ("Smoothness", Range(0,1)) = 0.25
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        struct Input { float4 color : COLOR; };
        fixed4 _Color;
        fixed4 _Emission;
        half _Glossiness;
        half _Metallic;
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed3 c = _Color.rgb * IN.color.rgb;
            o.Albedo = c;
            // Vertex alpha < 1 marks emissive "LED" geometry baked into meshes.
            o.Emission = _Emission.rgb + c * (1 - IN.color.a) * 2.5;
            o.Smoothness = _Glossiness;
            o.Metallic = _Metallic;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
