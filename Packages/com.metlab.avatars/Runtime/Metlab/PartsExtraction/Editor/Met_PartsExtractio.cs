/*
 * Met_PartsExtraction.cs
 * Unity 2022.3.22f1 対応
 * 配置場所: Assets/Editor/Met_PartsExtraction.cs
 *
 * 使い方:
 *   Unity メニュー → Tools → MetLABO → パーツ抽出するやつ
 */

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MetLABO
{
    public class PartsExtractionWindow : EditorWindow
    {
        // --- フィールド ---
        private GameObject _sourceAsset;       // 服装アセット（指定対象）
        private string _outputName = "";       // 出力名（空なら自動）

        // 子オブジェクト一覧と選択状態
        private List<GameObject> _partsList = new List<GameObject>();
        private List<bool> _partsSelected = new List<bool>();

        // スクロール位置
        private Vector2 _scrollPos;

        // ドラッグ受付エリアの色
        private static readonly Color _dropAreaColor   = new Color(0.25f, 0.35f, 0.45f, 1f);
        private static readonly Color _dropAreaHover   = new Color(0.30f, 0.50f, 0.65f, 1f);

        // ─────────────────────────────────────────
        [MenuItem("Tools/MetLABO/パーツ抽出するやつ")]
        public static void OpenWindow()
        {
            var window = GetWindow<PartsExtractionWindow>("パーツ抽出するやつ");
            window.minSize = new Vector2(380, 480);
            window.Show();
        }

        // ─────────────────────────────────────────
        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            GUILayout.Label("パーツ抽出ツール", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawSourceAssetField();
            EditorGUILayout.Space(8);

            if (_sourceAsset != null)
            {
                DrawPartsList();
                EditorGUILayout.Space(8);
                DrawOutputNameField();
                EditorGUILayout.Space(8);
                DrawExecuteButton();
            }
            else
            {
                EditorGUILayout.HelpBox("服装アセットをドラッグ&ドロップするか、オブジェクトフィールドで指定してください。", MessageType.Info);
            }

            // ウィンドウ全体をドロップ対象にする
            HandleWindowDrop();
        }

        // ─────────────────────────────────────────
        // 服装アセット指定エリア
        // ─────────────────────────────────────────
        private void DrawSourceAssetField()
        {
            GUILayout.Label("▼ 服装アセットを指定", EditorStyles.boldLabel);

            // オブジェクトフィールド
            EditorGUI.BeginChangeCheck();
            var newAsset = (GameObject)EditorGUILayout.ObjectField(
                "服装アセット", _sourceAsset, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                SetSourceAsset(newAsset);
            }

            // ドラッグ&ドロップ用の視覚的エリア
            var dropRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            bool isHovering = dropRect.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(dropRect, isHovering ? _dropAreaHover : _dropAreaColor);

            var labelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                normal = { textColor = Color.white },
                fontSize = 11
            };
            GUI.Label(dropRect, "ここにヒエラルキーからドラッグ&ドロップ", labelStyle);

            HandleDropArea(dropRect);
        }

        // ─────────────────────────────────────────
        // ドロップエリア処理
        // ─────────────────────────────────────────
        private void HandleDropArea(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                e.Use();
            }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (DragAndDrop.objectReferences.Length > 0)
                {
                    var go = DragAndDrop.objectReferences[0] as GameObject;
                    if (go != null) SetSourceAsset(go);
                }
                e.Use();
            }
        }

        // ウィンドウ全体のドロップ対応（フィールド外にドロップされた場合）
        private void HandleWindowDrop()
        {
            var e = Event.current;
            if (e.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (DragAndDrop.objectReferences.Length > 0)
                {
                    var go = DragAndDrop.objectReferences[0] as GameObject;
                    if (go != null) SetSourceAsset(go);
                }
            }
        }

        // ─────────────────────────────────────────
        // ソースアセットをセットし、パーツ一覧を更新
        // ─────────────────────────────────────────
        private void SetSourceAsset(GameObject go)
        {
            _sourceAsset = go;
            _partsList.Clear();
            _partsSelected.Clear();

            if (_sourceAsset == null) return;

            // 1階層の子を列挙。名前が "Armature" のものは除外（常に含める）
            foreach (Transform child in _sourceAsset.transform)
            {
                if (child.name == "Armature") continue;
                _partsList.Add(child.gameObject);
                _partsSelected.Add(true); // デフォルトは全チェック
            }

            Repaint();
        }

        // ─────────────────────────────────────────
        // パーツ選択チェックボックス一覧
        // ─────────────────────────────────────────
        private void DrawPartsList()
        {
            GUILayout.Label("▼ 抽出するパーツを選択", EditorStyles.boldLabel);

            // 全選択・全解除ボタン
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全選択", GUILayout.Width(80)))
                for (int i = 0; i < _partsSelected.Count; i++) _partsSelected[i] = true;
            if (GUILayout.Button("全解除", GUILayout.Width(80)))
                for (int i = 0; i < _partsSelected.Count; i++) _partsSelected[i] = false;
            EditorGUILayout.EndHorizontal();

            // Armature は常に含まれる旨を表示
            bool hasArmature = _sourceAsset.transform
                .Cast<Transform>().Any(t => t.name == "Armature");
            if (hasArmature)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Toggle("Armature（常に含まれます）", true);
                }
            }

            // パーツ一覧スクロール
            float listHeight = Mathf.Min(_partsList.Count * 22f + 8f, 200f);
            _scrollPos = EditorGUILayout.BeginScrollView(
                _scrollPos, GUILayout.Height(listHeight));

            for (int i = 0; i < _partsList.Count; i++)
            {
                _partsSelected[i] = EditorGUILayout.Toggle(
                    _partsList[i].name, _partsSelected[i]);
            }

            EditorGUILayout.EndScrollView();

            if (_partsList.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Armature 以外の子オブジェクトが見つかりませんでした。", MessageType.Warning);
            }
        }

        // ─────────────────────────────────────────
        // 出力名フィールド
        // ─────────────────────────────────────────
        private void DrawOutputNameField()
        {
            GUILayout.Label("▼ 出力オブジェクト名", EditorStyles.boldLabel);
            _outputName = EditorGUILayout.TextField("名前（空欄で自動）", _outputName);

            string preview = string.IsNullOrWhiteSpace(_outputName)
                ? _sourceAsset.name + "_extract"
                : _outputName;
            EditorGUILayout.LabelField("　→ 生成名:", preview, EditorStyles.miniLabel);
        }

        // ─────────────────────────────────────────
        // 実行ボタン
        // ─────────────────────────────────────────
        private void DrawExecuteButton()
        {
            bool anySelected = _partsSelected.Any(s => s);
            using (new EditorGUI.DisabledScope(!anySelected))
            {
                if (GUILayout.Button("パーツを抽出する", GUILayout.Height(36)))
                {
                    ExtractParts();
                }
            }

            if (!anySelected)
            {
                EditorGUILayout.HelpBox(
                    "少なくとも1つのパーツを選択してください。", MessageType.Warning);
            }
        }

        // ─────────────────────────────────────────
        // 抽出処理本体
        // ─────────────────────────────────────────
        private void ExtractParts()
        {
            if (_sourceAsset == null) return;

            // --- 出力名を決定 ---
            string finalName = string.IsNullOrWhiteSpace(_outputName)
                ? _sourceAsset.name + "_extract"
                : _outputName.Trim();

            // --- Undo グループ開始 ---
            Undo.SetCurrentGroupName("パーツ抽出");
            int undoGroup = Undo.GetCurrentGroup();

            // --- 元アセットをコピー ---
            GameObject copy = Instantiate(_sourceAsset);
            copy.name = finalName;

            // 同じ親の下に配置
            copy.transform.SetParent(_sourceAsset.transform.parent, false);
            copy.transform.SetSiblingIndex(_sourceAsset.transform.GetSiblingIndex() + 1);

            // ワールド座標を元アセットと揃える
            copy.transform.position   = _sourceAsset.transform.position;
            copy.transform.rotation   = _sourceAsset.transform.rotation;
            copy.transform.localScale = _sourceAsset.transform.localScale;

            Undo.RegisterCreatedObjectUndo(copy, "パーツ抽出");

            // --- 不要なパーツを削除 ---
            // コピーの1階層子を取得
            var copyChildren = new List<Transform>();
            foreach (Transform t in copy.transform)
                copyChildren.Add(t);

            // チェックされていないパーツ名のセットを作成
            var uncheckNames = new HashSet<string>();
            for (int i = 0; i < _partsList.Count; i++)
            {
                if (!_partsSelected[i])
                    uncheckNames.Add(_partsList[i].name);
            }

            foreach (var child in copyChildren)
            {
                // Armature は絶対に残す
                if (child.name == "Armature") continue;

                // チェックされていないものは削除
                if (uncheckNames.Contains(child.name))
                {
                    DestroyImmediate(child.gameObject);
                }
            }

            // --- Undo グループ終了 ---
            Undo.CollapseUndoOperations(undoGroup);

            // --- 完了メッセージ ---
            int extractedCount = _partsSelected.Count(s => s);
            bool hasArmature = _sourceAsset.transform
                .Cast<Transform>().Any(t => t.name == "Armature");
            string armatureMsg = hasArmature ? " + Armature" : "";

            Debug.Log($"[MetLABO] パーツ抽出完了: 「{finalName}」を生成しました。" +
                      $"（{extractedCount}パーツ{armatureMsg}）");

            EditorUtility.DisplayDialog(
                "パーツ抽出完了",
                $"「{finalName}」を生成しました。\n" +
                $"抽出パーツ数: {extractedCount}{armatureMsg}\n\n" +
                $"※ Ctrl+Z（Cmd+Z）で元に戻せます。",
                "OK");

            // ヒエラルキーで生成オブジェクトを選択状態にする
            Selection.activeGameObject = copy;
        }
    }
}
#endif
