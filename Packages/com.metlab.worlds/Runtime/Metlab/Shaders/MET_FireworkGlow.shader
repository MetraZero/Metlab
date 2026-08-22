// ============================================================
// MET_FireworkGlow
// 概要: 花火用の疑似発光パーティクルシェーダ。ポストプロセスのブルームに
//       頼らず、パーティクル自身が「白く光る芯＋外へ色付きでにじむグロー」を
//       描画するため、ブルーム弱め／無しの環境でも光って見える。
//
//       仕組み:
//         ・パーティクルのUVから放射状(丸)または横断方向(トレイル)の
//           フォールオフを計算し、中心ほど明るいグローを procedural に生成。
//         ・中心の最も明るい部分を白〜白黄へ寄せて「熱い芯(コア)」を疑似再生成。
//           → ブルームが無くても芯が白飛びしたキラキラ感になる。
//         ・パーティクルの頂点カラー(Start Color / Color over Lifetime /
//           Color over Trail)で色とフェードを制御。加算合成。
//         ・_MainTex を割り当てれば従来のスプライトも掛け合わせ可（既定は白＝不要）。
//
//       使い方:
//         ・炸裂の丸い玉 … _Shape = Radial
//         ・しだれ等のトレイル … _Shape = Trail（Trailsモジュールのマテリアルに使用）
//         ・色は Start Color / Color over Lifetime 側で付け、
//           芯の白飛びは _CoreWhite で調整する。
//
// バージョン: 1.0.0
//   a: 大きな変更（破壊的変更・仕様変更）
//   b: 機能追加・変更
//   c: 些細な修正・調整
// ============================================================
Shader "MET/FireworkGlow"
{
    Properties
    {
        [Header(Texture)]
        _MainTex ("スプライト（任意・既定は白＝不要）", 2D) = "white" {}

        [Header(Glow)]
        [KeywordEnum(Radial, Trail)] _Shape ("形状（Radial=丸玉 / Trail=トレイル）", Float) = 0
        _Intensity ("発光の強さ", Range(0, 8)) = 1.8
        _GlowFalloff ("にじみの締まり（大きいほど鋭い）", Range(0.5, 8)) = 2.0

        [Header(Hot Core)]
        _CoreWhite ("芯の白飛び量", Range(0, 3)) = 0.7
        _CorePower ("芯の集中度（大きいほど中心だけ）", Range(1, 16)) = 5.0
        [HDR] _CoreTint ("芯の色（白黄が花火らしい）", Color) = (1, 0.95, 0.8, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Blend One One          // 加算合成（光が重なるほど明るく）
        ZWrite Off             // 半透明なので深度は書かない
        Cull Off               // 両面（ビルボード・トレイルの裏表対策）
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _SHAPE_RADIAL _SHAPE_TRAIL
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;      // パーティクル頂点カラー
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float  _Intensity;
            float  _GlowFalloff;
            float  _CoreWhite;
            float  _CorePower;
            float4 _CoreTint;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // --- 形状に応じたフォールオフ（0..1、中心=1）---
                float g;
                #if defined(_SHAPE_TRAIL)
                    // トレイル：幅方向(V)の中央ほど明るい帯。長さ方向は頂点カラーで制御。
                    float dv = abs(i.uv.y - 0.5) * 2.0;
                    g = saturate(1.0 - dv);
                #else
                    // 放射状：UV中心からの距離で丸いグロー。
                    float2 d = (i.uv - 0.5) * 2.0;
                    g = saturate(1.0 - length(d));
                #endif
                g = pow(g, _GlowFalloff);

                // --- 任意スプライトを掛け合わせ（既定は白なので無影響）---
                fixed4 tex = tex2D(_MainTex, i.uv);
                g *= tex.a;

                // --- 色：パーティクルカラー × スプライト色 ---
                float3 tint = i.color.rgb * tex.rgb;
                float3 col  = tint * g;

                // --- 熱い芯：最も明るい中心を白〜白黄へ寄せて疑似ブルーム ---
                float core = pow(g, _CorePower) * _CoreWhite;
                col += core * _CoreTint.rgb;

                col *= _Intensity;

                // --- フェード（頂点カラーのアルファ）を加算量に反映 ---
                float a = g * i.color.a;
                return fixed4(col * a, a);
            }
            ENDCG
        }
    }
}
