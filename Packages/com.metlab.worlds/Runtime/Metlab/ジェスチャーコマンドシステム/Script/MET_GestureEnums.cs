// =============================================================
// MET_GestureEnums.cs
// MET_ Gesture System - 共通enum定義
// Version: 1.5.0
//
// [Changelog]
// 1.5.0 - 花火アクション(MET_FireworkAction)を追加。Start/Stop/Toggle。
// 1.4.0 - 音アクション(MET_SoundAction)を追加。Play/Stop。
// 1.3.0 - スカイボックス動作(MET_SkyboxMode)を追加。Set/Flip(A↔B往復)。
// 1.2.0 - ワープ基準(MET_WarpAnchor)を追加。視点前/手元を選択可能に。
// 1.1.0 - トラッキング方式(MET_TrackMode)を追加。手ワールド/手頭相対/視点の3方式。
// 1.0.0 - 初版。方向(4方向) / ジェスチャーモード / 描画手 / トグル動作。
// =============================================================

/// <summary>ジェスチャーの方向。斜めなしの4方向のみ。</summary>
public enum MET_GestureDir
{
    Up,     // 上
    Down,   // 下
    Left,   // 左
    Right,  // 右
}

/// <summary>ジェスチャー記録の開始/終了トリガー。</summary>
public enum MET_GestureMode
{
    BothTriggers,     // 両手トリガー（両手のUse同時押し中）誤爆しにくい・推奨
    OneTrigger,       // 片手トリガー（描く手のUse）手軽だがピックアップと競合しやすい
    OneGrip,          // 片手グリップ（描く手のGrab）
    GripThenTrigger,  // 構え＋描画（反対の手のGripで構え、描く手のUseで描画）誤爆ほぼゼロ
    Always,           // 常時監視（トリガー不要・実験的）手/視点が止まると確定
}

/// <summary>ジェスチャーの軌跡を何で取るか。</summary>
public enum MET_TrackMode
{
    HandWorld,  // 手・ワールド固定（起き上がり想定）上下=世界Y / 左右=YawのみのプレイヤーX
    HandHead,   // 手・頭相対（寝ても使えるが酔いやすい）頭のright/upを基準に判定
    Gaze,       // 視点操作（体勢無関係）頭の向きの変化を4方向に量子化。手はフリー
}

/// <summary>ジェスチャーを描く手（手モード時）。</summary>
public enum MET_DrawHand
{
    Right,  // 右手
    Left,   // 左手
}

/// <summary>トグルアクションの動作種別。</summary>
public enum MET_ToggleType
{
    Flip,   // 反転（現在の状態を切り替え）
    On,     // 強制ON
    Off,    // 強制OFF
}

/// <summary>ワープ先の基準位置。</summary>
public enum MET_WarpAnchor
{
    ViewFront,  // 視点(頭)の前 … 目の前に呼び寄せる。おすすめ
    Hand,       // 描画手の位置 … 手元に呼び寄せる
}

/// <summary>スカイボックス切り替えの動作。</summary>
public enum MET_SkyboxMode
{
    Set,    // 指定Materialに切り替える（一方通行）
    Flip,   // A↔B を交互に切り替える（往復）
}

/// <summary>音アクションの動作。</summary>
public enum MET_SoundAction
{
    Play,   // 鳴らす（soundLoopでループ指定可）
    Stop,   // 指定AudioSourceの再生を止める
}

/// <summary>花火（打ち上げ）アクションの動作。</summary>
public enum MET_FireworkAction
{
    Start,   // 自動連続発射を開始する
    Stop,    // 自動連続発射を停止する
    Toggle,  // 開始/停止を交互に切り替える
}