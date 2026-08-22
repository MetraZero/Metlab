/*
 * METDropdownSwitch.cs
 * Version: 1.2.0
 * 
 * 概要:
 * TMP_Dropdownで選択された項目に応じて、対応するGameObjectの
 * アクティブ/非アクティブを切り替えるスクリプト。
 * 
 * 【重要な動作】
 * - 選択された項目：アクティブターゲット→ON、ディアクティブターゲット→OFF
 * - 選択されていない項目：アクティブターゲット→OFF、ディアクティブターゲット→ON（反転）
 * 
 * 例：項目0（りんごON）、項目1（バナナON、みかんOFF）がある場合
 * - 項目0選択時：りんごON、バナナOFF、みかんON
 * - 項目1選択時：りんごOFF、バナナON、みかんOFF
 * 
 * 最大10項目まで対応。VRChatのネットワーク機能でグローバル同期されます。
 * 
 * 使用方法:
 * 1. UdonBehaviourコンポーネントにこのスクリプトをアタッチ
 * 2. Dropdownフィールドに TMP_Dropdown コンポーネントを設定
 * 3. Dropdown Item Count で項目数を設定（1〜10）
 * 4. 各項目ごとに：
 *    - Item N Objects To Activate: 項目Nが選択されたときにアクティブにするオブジェクト
 *    - Item N Objects To Deactivate: 項目Nが選択されたときに非アクティブにするオブジェクト
 * 5. Initial Active Index で初期アクティブ項目を設定（デフォルト: 0）
 */

using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class METDropdownSwitch : UdonSharpBehaviour
{
    [Header("ドロップダウン設定")]
    [Tooltip("切り替えを制御するTMP_Dropdownコンポーネント")]
    public TMP_Dropdown dropdown;
    
    [Tooltip("ドロップダウンの項目数（1〜10）")]
    [Range(1, 10)]
    public int dropdownItemCount = 3;

    [Header("項目0の設定")]
    [Tooltip("項目0が選択されたときにアクティブにするオブジェクト")]
    public GameObject[] item0ObjectsToActivate;
    [Tooltip("項目0が選択されたときに非アクティブにするオブジェクト")]
    public GameObject[] item0ObjectsToDeactivate;

    [Header("項目1の設定")]
    [Tooltip("項目1が選択されたときにアクティブにするオブジェクト")]
    public GameObject[] item1ObjectsToActivate;
    [Tooltip("項目1が選択されたときに非アクティブにするオブジェクト")]
    public GameObject[] item1ObjectsToDeactivate;

    [Header("項目2の設定")]
    [Tooltip("項目2が選択されたときにアクティブにするオブジェクト")]
    public GameObject[] item2ObjectsToActivate;
    [Tooltip("項目2が選択されたときに非アクティブにするオブジェクト")]
    public GameObject[] item2ObjectsToDeactivate;

    [Header("項目3の設定")]
    [Tooltip("項目3が選択されたときにアクティブにするオブジェクト")]
    public GameObject[] item3ObjectsToActivate;
    [Tooltip("項目3が選択されたときに非アクティブにするオブジェクト")]
    public GameObject[] item3ObjectsToDeactivate;

    [Header("項目4の設定")]
    [Tooltip("項目4が選択されたときにアクティブにするオブジェクト")]
    public GameObject[] item4ObjectsToActivate;
    [Tooltip("項目4が選択されたときに非アクティブにするオブジェクト")]
    public GameObject[] item4ObjectsToDeactivate;

    [Header("項目5の設定")]
    [Tooltip("項目5が選択されたときにアクティブにするオブジェクト")]
    public GameObject[] item5ObjectsToActivate;
    [Tooltip("項目5が選択されたときに非アクティブにするオブジェクト")]
    public GameObject[] item5ObjectsToDeactivate;

    [Header("項目6の設定")]
    [Tooltip("項目6が選択されたときにアクティブにするオブジェクト")]
    public GameObject[] item6ObjectsToActivate;
    [Tooltip("項目6が選択されたときに非アクティブにするオブジェクト")]
    public GameObject[] item6ObjectsToDeactivate;

    [Header("項目7の設定")]
    [Tooltip("項目7が選択されたときにアクティブにするオブジェクト")]
    public GameObject[] item7ObjectsToActivate;
    [Tooltip("項目7が選択されたときに非アクティブにするオブジェクト")]
    public GameObject[] item7ObjectsToDeactivate;

    [Header("項目8の設定")]
    [Tooltip("項目8が選択されたときにアクティブにするオブジェクト")]
    public GameObject[] item8ObjectsToActivate;
    [Tooltip("項目8が選択されたときに非アクティブにするオブジェクト")]
    public GameObject[] item8ObjectsToDeactivate;

    [Header("項目9の設定")]
    [Tooltip("項目9が選択されたときにアクティブにするオブジェクト")]
    public GameObject[] item9ObjectsToActivate;
    [Tooltip("項目9が選択されたときに非アクティブにするオブジェクト")]
    public GameObject[] item9ObjectsToDeactivate;

    [Header("初期設定")]
    [Tooltip("初期状態でアクティブにする項目のインデックス")]
    public int initialActiveIndex = 0;

    [Header("デバッグ")]
    [Tooltip("デバッグログを表示するか")]
    public bool showDebugLog = false;

    // ネットワーク同期される現在のアクティブインデックス
    [UdonSynced]
    private int currentActiveIndex = 0;

    // 前回のドロップダウンの値（変更検知用）
    private int previousDropdownValue = -1;

    private bool isInitialized = false;

    void Start()
    {
        Initialize();
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    private void Initialize()
    {
        if (isInitialized) return;

        // 必須コンポーネントのチェック
        if (dropdown == null)
        {
            Debug.LogError("[METDropdownSwitch] TMP_Dropdownが設定されていません！");
            return;
        }

        // 項目数の範囲チェック
        if (dropdownItemCount < 1 || dropdownItemCount > 10)
        {
            Debug.LogWarning($"[METDropdownSwitch] Dropdown Item Count({dropdownItemCount})が範囲外です。3に設定します。");
            dropdownItemCount = 3;
        }

        // 初期インデックスの範囲チェック
        if (initialActiveIndex < 0 || initialActiveIndex >= dropdownItemCount)
        {
            Debug.LogWarning($"[METDropdownSwitch] Initial Active Index({initialActiveIndex})が範囲外です。0に設定します。");
            initialActiveIndex = 0;
        }

        // 初期値を設定
        currentActiveIndex = initialActiveIndex;
        dropdown.value = initialActiveIndex;
        previousDropdownValue = initialActiveIndex;

        // 初期状態を適用
        ApplySwitchState(currentActiveIndex);

        isInitialized = true;

        if (showDebugLog)
        {
            Debug.Log($"[METDropdownSwitch] 初期化完了。初期アクティブインデックス: {currentActiveIndex}");
        }
    }

    /// <summary>
    /// ドロップダウンの値変更を監視
    /// </summary>
    void Update()
    {
        if (!isInitialized || dropdown == null) return;

        // ドロップダウンの値が変更されたかチェック
        int currentDropdownValue = dropdown.value;
        if (currentDropdownValue != previousDropdownValue)
        {
            previousDropdownValue = currentDropdownValue;
            OnDropdownValueChanged(currentDropdownValue);
        }
    }

    /// <summary>
    /// ドロップダウンの値が変更された時の処理
    /// </summary>
    private void OnDropdownValueChanged(int newIndex)
    {
        // 範囲チェック
        if (newIndex < 0 || newIndex >= dropdownItemCount)
        {
            Debug.LogWarning($"[METDropdownSwitch] 範囲外のインデックス: {newIndex}");
            return;
        }

        // オーナーシップを取得
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        // 新しいインデックスを設定
        currentActiveIndex = newIndex;

        // ネットワーク同期
        RequestSerialization();

        // 切り替えを適用
        ApplySwitchState(currentActiveIndex);

        if (showDebugLog)
        {
            Debug.Log($"[METDropdownSwitch] ドロップダウン変更: インデックス {newIndex}");
        }
    }

    /// <summary>
    /// オブジェクトの切り替え状態を適用
    /// </summary>
    private void ApplySwitchState(int activeIndex)
    {
        // インデックスの範囲チェック
        if (activeIndex < 0 || activeIndex >= dropdownItemCount)
        {
            Debug.LogError($"[METDropdownSwitch] 無効なインデックス: {activeIndex}");
            return;
        }

        // 全ての項目をループ処理
        for (int itemIndex = 0; itemIndex < dropdownItemCount; itemIndex++)
        {
            GameObject[] activateObjects = null;
            GameObject[] deactivateObjects = null;

            // 項目インデックスに応じて配列を取得
            switch (itemIndex)
            {
                case 0:
                    activateObjects = item0ObjectsToActivate;
                    deactivateObjects = item0ObjectsToDeactivate;
                    break;
                case 1:
                    activateObjects = item1ObjectsToActivate;
                    deactivateObjects = item1ObjectsToDeactivate;
                    break;
                case 2:
                    activateObjects = item2ObjectsToActivate;
                    deactivateObjects = item2ObjectsToDeactivate;
                    break;
                case 3:
                    activateObjects = item3ObjectsToActivate;
                    deactivateObjects = item3ObjectsToDeactivate;
                    break;
                case 4:
                    activateObjects = item4ObjectsToActivate;
                    deactivateObjects = item4ObjectsToDeactivate;
                    break;
                case 5:
                    activateObjects = item5ObjectsToActivate;
                    deactivateObjects = item5ObjectsToDeactivate;
                    break;
                case 6:
                    activateObjects = item6ObjectsToActivate;
                    deactivateObjects = item6ObjectsToDeactivate;
                    break;
                case 7:
                    activateObjects = item7ObjectsToActivate;
                    deactivateObjects = item7ObjectsToDeactivate;
                    break;
                case 8:
                    activateObjects = item8ObjectsToActivate;
                    deactivateObjects = item8ObjectsToDeactivate;
                    break;
                case 9:
                    activateObjects = item9ObjectsToActivate;
                    deactivateObjects = item9ObjectsToDeactivate;
                    break;
            }

            // この項目が選択されているか確認
            bool isSelected = (itemIndex == activeIndex);

            // アクティブターゲットの処理
            if (activateObjects != null)
            {
                for (int i = 0; i < activateObjects.Length; i++)
                {
                    if (activateObjects[i] != null)
                    {
                        // 選択されている項目：アクティブ / 選択されていない項目：非アクティブ（反転）
                        activateObjects[i].SetActive(isSelected);

                        if (showDebugLog)
                        {
                            Debug.Log($"[METDropdownSwitch] 項目{itemIndex} アクティブターゲット {activateObjects[i].name}: {(isSelected ? "ON" : "OFF")}");
                        }
                    }
                }
            }

            // ディアクティブターゲットの処理
            if (deactivateObjects != null)
            {
                for (int i = 0; i < deactivateObjects.Length; i++)
                {
                    if (deactivateObjects[i] != null)
                    {
                        // 選択されている項目：非アクティブ / 選択されていない項目：アクティブ（反転）
                        deactivateObjects[i].SetActive(!isSelected);

                        if (showDebugLog)
                        {
                            Debug.Log($"[METDropdownSwitch] 項目{itemIndex} ディアクティブターゲット {deactivateObjects[i].name}: {(!isSelected ? "ON" : "OFF")}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// ネットワーク同期後の処理（他のプレイヤーからの同期を受信）
    /// </summary>
    public override void OnDeserialization()
    {
        // ドロップダウンのUIを更新
        if (dropdown != null)
        {
            dropdown.value = currentActiveIndex;
            previousDropdownValue = currentActiveIndex;
        }

        // 切り替え状態を適用
        ApplySwitchState(currentActiveIndex);

        if (showDebugLog)
        {
            Debug.Log($"[METDropdownSwitch] ネットワーク同期受信: インデックス {currentActiveIndex}");
        }
    }

    /// <summary>
    /// プログラムから直接インデックスを設定する（外部スクリプト用）
    /// </summary>
    public void SetActiveIndex(int newIndex)
    {
        if (newIndex < 0 || newIndex >= dropdownItemCount)
        {
            Debug.LogError($"[METDropdownSwitch] SetActiveIndex: 無効なインデックス {newIndex}");
            return;
        }

        // オーナーシップを取得
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        // インデックスを設定
        currentActiveIndex = newIndex;

        // UIを更新
        if (dropdown != null)
        {
            dropdown.value = newIndex;
            previousDropdownValue = newIndex;
        }

        // ネットワーク同期
        RequestSerialization();

        // 切り替えを適用
        ApplySwitchState(currentActiveIndex);

        if (showDebugLog)
        {
            Debug.Log($"[METDropdownSwitch] SetActiveIndex呼び出し: インデックス {newIndex}");
        }
    }

    /// <summary>
    /// 現在のアクティブインデックスを取得
    /// </summary>
    public int GetActiveIndex()
    {
        return currentActiveIndex;
    }
}