// ============================================================
// MET_FakeSun
// 概要: 見せかけの太陽。Skybox / Directional Light を使わず、発光する球体を
//       「無限遠の太陽」に見せる。ローカルプレイヤーの視点位置に追従させる
//       ことで、どこへ歩いても視差ゼロ・常に同じ方向・同じ大きさに見える。
//       頭の回転には追従しない（顔に貼りつかない）。
//       太陽専用グロー板を持ち、ワールド共通のBloomとは独立してON/OFF・強度調整可能。
// バージョン: 1.0.0
//   a: 大きな変更（破壊的変更・仕様変更）
//   b: 機能追加・変更
//   c: 些細な修正・調整
// ============================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// 同期不要（各プレイヤーのローカル視点で正しく見える見た目のみのギミック）
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MET_FakeSun : UdonSharpBehaviour
{
    // ------------------------------------------------------------
    // 太陽の向き・位置
    // ------------------------------------------------------------
    [Header("太陽の向き・位置")]
    [SerializeField, Range(-10f, 90f), Tooltip("仰角：地平線からの高さ（度）。0=地平線, 90=真上")]
    private float elevationDeg = 35f;

    [SerializeField, Range(0f, 360f), Tooltip("方位角：水平方向の向き（度）。0=+Z方向, 90=+X方向へ時計回り")]
    private float azimuthDeg = 135f;

    [SerializeField, Min(1f), Tooltip("プレイヤー視点から太陽までの距離（m）。追従するので絶対に近づかない")]
    private float distance = 400f;

    // ------------------------------------------------------------
    // 太陽本体の見た目
    // ------------------------------------------------------------
    [Header("太陽本体の見た目")]
    [SerializeField, Tooltip("太陽本体（発光する球体）のRenderer")]
    private Renderer sunRenderer;

    [SerializeField, Tooltip("太陽本体のTransform（大きさ調整に使用）")]
    private Transform sunCore;

    [SerializeField, Tooltip("太陽の色")]
    private Color sunColor = new Color(1f, 0.96f, 0.88f, 1f);

    [SerializeField, Range(0f, 20f), Tooltip("発光強度。値を上げるほど明るく、Bloomでの滲みも強くなる")]
    private float emissionIntensity = 3f;

    [SerializeField, Min(0.01f), Tooltip("太陽本体の大きさ（見かけの角度 ≒ 大きさ ÷ 距離）")]
    private float sunScale = 8f;

    // ------------------------------------------------------------
    // グロー（太陽専用・ワールドBloomとは独立）
    // ------------------------------------------------------------
    [Header("グロー（太陽専用・ワールドBloomとは独立）")]
    [SerializeField, Tooltip("太陽専用グローを表示するか")]
    private bool glowEnabled = true;

    [SerializeField, Tooltip("グロー板（常にカメラを向くビルボード）のRenderer")]
    private Renderer glowRenderer;

    [SerializeField, Tooltip("グロー板のTransform（大きさ・向き制御に使用）")]
    private Transform glowBoard;

    [SerializeField, Tooltip("グロー色を太陽の色と同じにする")]
    private bool useSunColorForGlow = true;

    [SerializeField, Tooltip("グロー色（「太陽の色と同じ」がOFFのとき使用）")]
    private Color glowColor = new Color(1f, 0.85f, 0.6f, 1f);

    [SerializeField, Range(0f, 10f), Tooltip("グローの強度（明るさ）")]
    private float glowIntensity = 2f;

    [SerializeField, Min(0.01f), Tooltip("グローの大きさ（太陽本体に対する倍率）")]
    private float glowSizeMultiplier = 3f;

    [SerializeField, Tooltip("グローが表示されない/裏返る場合にON（表裏を反転）")]
    private bool flipGlow = false;

    // ------------------------------------------------------------
    // 内部
    // ------------------------------------------------------------
    private VRCPlayerApi _localPlayer;
    private MaterialPropertyBlock _sunBlock;
    private MaterialPropertyBlock _glowBlock;

    // よく使われるシェーダープロパティ名（存在しないものへのSetは無視されるので安全に併用可）
    private const string PROP_COLOR = "_Color";
    private const string PROP_EMISSION = "_EmissionColor";
    private const string PROP_TINT = "_TintColor";

    private void Start()
    {
        _localPlayer = Networking.LocalPlayer;
        _sunBlock = new MaterialPropertyBlock();
        _glowBlock = new MaterialPropertyBlock();

        // 起動時に一度、見た目設定を反映
        ApplyVisuals();
    }

    // プレイヤーのトラッキング更新後に位置を確定させることで、追従のカクつきを防ぐ
    public override void PostLateUpdate()
    {
        if (_localPlayer == null)
        {
            _localPlayer = Networking.LocalPlayer;
            if (_localPlayer == null) { return; }
        }

        // 視点（頭）の位置を基準にする
        Vector3 headPos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

        // 角度から方向ベクトルを算出し、視点から一定距離に太陽を配置（＝視差ゼロの無限遠）
        Vector3 dir = DirectionFromAngles(elevationDeg, azimuthDeg);
        transform.position = headPos + dir * distance;

        // グロー板を視点へ向ける（ビルボード）
        if (glowEnabled && glowBoard != null)
        {
            Vector3 toHead = headPos - glowBoard.position;
            if (toHead.sqrMagnitude > 0.0001f)
            {
                Vector3 look = flipGlow ? -toHead : toHead;
                glowBoard.rotation = Quaternion.LookRotation(look, Vector3.up);
            }
        }
    }

    /// <summary>
    /// 色・発光・グローなどの見た目設定をマテリアルへ反映する。
    /// 実行中に色や強度をUI等で変更したら、この関数を呼べば反映される。
    /// （向き・距離は毎フレーム反映されるので呼び出し不要）
    /// </summary>
    public void ApplyVisuals()
    {
        // --- 太陽本体 ---
        if (sunCore != null)
        {
            sunCore.localScale = new Vector3(sunScale, sunScale, sunScale);
        }

        if (sunRenderer != null)
        {
            Color emit = new Color(
                sunColor.r * emissionIntensity,
                sunColor.g * emissionIntensity,
                sunColor.b * emissionIntensity,
                1f);

            sunRenderer.GetPropertyBlock(_sunBlock);
            _sunBlock.SetColor(PROP_COLOR, sunColor);
            _sunBlock.SetColor(PROP_EMISSION, emit);
            _sunBlock.SetColor(PROP_TINT, emit);
            sunRenderer.SetPropertyBlock(_sunBlock);
        }

        // --- グロー ---
        if (glowBoard != null)
        {
            glowBoard.gameObject.SetActive(glowEnabled);
            float glowSize = sunScale * glowSizeMultiplier;
            glowBoard.localScale = new Vector3(glowSize, glowSize, glowSize);
        }

        if (glowRenderer != null)
        {
            Color baseGlow = useSunColorForGlow ? sunColor : glowColor;
            Color glowEmit = new Color(
                baseGlow.r * glowIntensity,
                baseGlow.g * glowIntensity,
                baseGlow.b * glowIntensity,
                baseGlow.a);

            glowRenderer.GetPropertyBlock(_glowBlock);
            _glowBlock.SetColor(PROP_COLOR, glowEmit);
            _glowBlock.SetColor(PROP_EMISSION, glowEmit);
            _glowBlock.SetColor(PROP_TINT, glowEmit);
            glowRenderer.SetPropertyBlock(_glowBlock);
        }
    }

    /// <summary>
    /// 仰角・方位角から単位方向ベクトルを求める。
    /// </summary>
    private Vector3 DirectionFromAngles(float elevation, float azimuth)
    {
        float e = elevation * Mathf.Deg2Rad;
        float a = azimuth * Mathf.Deg2Rad;
        float cosE = Mathf.Cos(e);

        float x = cosE * Mathf.Sin(a);
        float y = Mathf.Sin(e);
        float z = cosE * Mathf.Cos(a);

        return new Vector3(x, y, z);
    }

    // ------------------------------------------------------------
    // 外部（UI等）から実行中に調整するための公開メソッド
    // ------------------------------------------------------------

    /// <summary>グローのON/OFFを切り替える。</summary>
    public void ToggleGlow()
    {
        glowEnabled = !glowEnabled;
        ApplyVisuals();
    }

    /// <summary>グロー強度を設定して反映する。</summary>
    public void SetGlowIntensity(float value)
    {
        glowIntensity = value;
        ApplyVisuals();
    }

    /// <summary>太陽の発光強度を設定して反映する。</summary>
    public void SetEmissionIntensity(float value)
    {
        emissionIntensity = value;
        ApplyVisuals();
    }
}
