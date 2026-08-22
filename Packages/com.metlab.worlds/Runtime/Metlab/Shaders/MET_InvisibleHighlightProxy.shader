// ============================================================
// MET_InvisibleHighlightProxy
// 概要: VRChatのPickUp/インタラクトのハイライト形状取得専用の不可視シェーダ。
//       色も深度も書き込まないため画面には一切描画されないが、
//       MeshRendererとしては有効なのでハイライトの「形」として使われる。
//       SkinnedMeshRenderer（揺れ物付き）本体はそのまま残し、
//       このシェーダを貼った静的メッシュのプロキシでハイライトだけ差し替える用途。
// バージョン: 1.0.0
// ============================================================
Shader "MET/InvisibleHighlightProxy"
{
    SubShader
    {
        // 不透明扱い。実際には何も描画しない。
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            ColorMask 0   // カラーバッファに書き込まない（不可視）
            ZWrite Off    // 深度バッファに書き込まない（他の描画に影響しない）
        }
    }
}
