Shader "Unlit/Boid"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog
            #pragma instancing_options procedural:setup

            #include "UnityCG.cginc"

            struct BoidData
            {
                half3 velocity;
                half3 position;
            };

            StructuredBuffer<BoidData> _BoidDataBuffer;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint id : SV_InstanceID;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            half3 _BoidScale;

            half4x4 eulerToMatrix(float3 inputAngles)
            {
                // Calculate sine and cosine values for each angle
                half cosYaw = cos(inputAngles.y);
                half sinYaw = sin(inputAngles.y);
                half cosPitch = cos(inputAngles.x);
                half sinPitch = sin(inputAngles.x);
                half cosRoll = cos(inputAngles.z);
                half sinRoll = sin(inputAngles.z);

                // Create a 4x4 rotation matrix to hold the result
                // Fill in the rotation matrix elements using the calculated values
                // Yaw-Pitch-Roll (Ry-Rx-Rz)
                return half4x4(
                     cosYaw * cosRoll + sinYaw * sinPitch * sinRoll, -cosYaw * sinRoll + sinYaw * sinPitch * cosRoll, sinYaw * cosPitch, 0,
                     cosPitch * sinRoll, cosPitch * cosRoll, -sinPitch, 0,
                     -sinYaw * cosRoll + cosYaw * sinPitch * sinRoll, sinYaw * sinRoll + cosYaw * sinPitch * cosRoll, cosYaw * cosPitch, 0,
                     0, 0, 0, 1
                );  // float4(0, 0, 0, 1) for homogeneous coordinates in the last row
            }

            v2f vert (appdata v)
            {
                v2f o;

                BoidData boidData = _BoidDataBuffer[v.id];

                float3 pos = boidData.position.xyz;
                half3 boidScale = _BoidScale;

                float4x4 object2World = 0;

                // scale matrix
                object2World._11_22_33_44 = float4(boidScale.xyz, 1.0);

                // rotation matrix
                half rotY = atan2(boidData.velocity.x, boidData.velocity.z);
                half rotX = -asin(boidData.velocity.y / (length(boidData.velocity.xyz) + 1e-8));

                half4x4 rotMatrix = eulerToMatrix(half3(rotX, rotY, 0));

                // combine matrices and add translation
                object2World = mul(rotMatrix, object2World);
                object2World._14_24_34 += pos.xyz;
                
                v.vertex = mul(object2World, v.vertex);
                
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
