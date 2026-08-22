// =============================================================
// MET_GestureLogBoard.cs
// MET_ Gesture System - デバッグ用ログボード（特定プレイヤー限定表示）
// Version: 1.0.0
//
// [概要]
//   指定した表示名のプレイヤー（狭間メトラ）にだけ見えるログ板。
//   ・モードON/OFF状態
//   ・監視中のジェスチャー方向列（↑↓←→ でどんどん伸びる）
//   を表示する。VRCアップロード後の実機確認用。
//   表示名判定はローカル完結。他人には最初から見えない。
//
// [配置]
//   ワールドに固定した Canvas（Render Mode: World Space）配下に
//   Text (TMP) を置き、このスクリプトを付けて Manager の logBoard 欄に刺す。
//
// [Changelog]
//   1.0.1 - テキスト型を TextMeshProUGUI（Canvas配下のText(TMP)）に変更。
//   1.0.0 - 初版。
// =============================================================

using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MET_GestureLogBoard : UdonSharpBehaviour
{
    [Header("■ 表示先")]
    [Tooltip("方向列などを表示する Text (TMP) ＝ TextMeshProUGUI（Canvas配下）")]
    public TextMeshProUGUI text;

    [Tooltip("非表示時に隠す見た目のルート（未指定なら text の GameObject を隠す）")]
    public GameObject visualRoot;

    [Header("■ 表示するプレイヤー")]
    [Tooltip("この表示名のプレイヤーにだけ見える")]
    public string ownerName = "狭間メトラ";

    [Header("■ ログ設定")]
    [Tooltip("履歴として残す過去ジェスチャーの行数")]
    public int maxLines = 12;

    // ---- 内部 ----
    private bool _visible;
    private bool _modeOn;
    private string[] _lines;   // 確定済みジェスチャーの履歴
    private int _lineCount;
    private string _current;   // 現在描いている行

    void Start()
    {
        int cap = (maxLines > 0) ? maxLines : 1;
        _lines = new string[cap];
        _lineCount = 0;
        _current = "";
        _modeOn = false;

        VRCPlayerApi lp = Networking.LocalPlayer;
        _visible = (lp != null) && (lp.displayName == ownerName);

        if (!_visible)
        {
            if (visualRoot != null) visualRoot.SetActive(false);
            else if (text != null) text.gameObject.SetActive(false);
            return;
        }
        _Render();
    }

    // === Manager から呼ばれる ===

    /// <summary>モードON/OFF表示を更新。</summary>
    public void _SetMode(bool on)
    {
        if (!_visible) return;
        _modeOn = on;
        _Render();
    }

    /// <summary>新しいジェスチャーの記録開始（現在行を履歴へ送って新規行に）。</summary>
    public void _BeginGesture()
    {
        if (!_visible) return;
        _PushCurrent();
        _current = "";
        _Render();
    }

    /// <summary>確定した1方向を現在行に追記。dir: 0=上 1=下 2=左 3=右</summary>
    public void _AppendDir(int dir)
    {
        if (!_visible) return;
        if (_current.Length > 0) _current += " ";
        _current += _Arrow(dir);
        _Render();
    }

    // === 内部処理 ===

    private void _PushCurrent()
    {
        if (_current == null || _current.Length == 0) return;

        int cap = _lines.Length;
        if (_lineCount < cap)
        {
            _lines[_lineCount] = _current;
            _lineCount++;
        }
        else
        {
            // 古い行を1つ捨てて詰める（リングバッファ的に）
            for (int i = 1; i < cap; i++) _lines[i - 1] = _lines[i];
            _lines[cap - 1] = _current;
        }
    }

    private void _Render()
    {
        if (!_visible || text == null) return;

        string s = _modeOn ? "モード: ON" : "モード: OFF";
        s += "\n----------------\n";
        for (int i = 0; i < _lineCount; i++)
        {
            s += _lines[i] + "\n";
        }
        if (_current != null && _current.Length > 0) s += _current;

        text.text = s;
    }

    private string _Arrow(int d)
    {
        if (d == 0) return "↑";
        if (d == 1) return "↓";
        if (d == 2) return "←";
        return "→";
    }
}
