// MET_ShortcutWindow
// v3.8.0
// 実Assetフォルダとは独立した「仮想ショートカットツリー」を、
// Projectウィンドウのアイコングリッド表示に近い形で表示・管理するタブ。
// ・仮想フォルダ / アセットショートカットをドラッグで登録・並び替え・格納
// ・クリックで選択、もう一度クリックで「開く」(フォルダは中に入る)
// ・アセットフォルダのショートカットを開くと、その中の"本物のアセット"を辿れる
//   (中身はショートカットではなく実アセットとしてブラウズする)
// ・色付けはフォルダのみ(フォルダアイコンを着色) / カスタム画像があれば画像を優先
// ・何もない場所の右クリックで背景色を変更(Projectウィンドウ風)
// データは ProjectSettings/MET_ShortcutData.json に保存(Assets非汚染)。

using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Metlabo.EditorTools
{
    [Serializable]
    public class MET_ShortcutNode
    {
        public string id = Guid.NewGuid().ToString();
        public string name;
        public bool isFolder;
        public string assetGuid;       // isFolder = false の場合のみ使用(参照アセット)

        public bool hasColor;
        public float colorR, colorG, colorB;

        public string customIconGuid;  // 設定されていればデフォルトアイコンより優先

        public List<MET_ShortcutNode> children = new List<MET_ShortcutNode>();
    }

    [Serializable]
    public class MET_ShortcutData
    {
        public List<MET_ShortcutNode> roots = new List<MET_ShortcutNode>();

        // ウィンドウ背景色(Projectウィンドウ風)
        public bool hasBgColor;
        public float bgR = 0.22f, bgG = 0.22f, bgB = 0.22f;

        // アイコン表示倍率(下部スライダー)
        public float iconScale = 1f;
    }

    public class MET_ShortcutWindow : EditorWindow
    {
        private const string DataPath = "ProjectSettings/MET_ShortcutData.json";
        private MET_ShortcutData data;
        private Vector2 scroll;

        // ------------------------------------------------------------
        // ナビゲーション(仮想フォルダ or 実アセットフォルダ)
        // ------------------------------------------------------------
        private class NavItem
        {
            public MET_ShortcutNode virtualFolder; // 仮想フォルダ。null の場合は実フォルダ
            public string realFolderPath;          // 実アセットフォルダのパス
            public bool IsReal => virtualFolder == null;
            public string Label => virtualFolder != null
                ? virtualFolder.name
                : Path.GetFileName(realFolderPath);
        }
        private readonly List<NavItem> navStack = new List<NavItem>();

        private bool InRealMode => navStack.Count > 0 && navStack[navStack.Count - 1].IsReal;

        // 現在の仮想リスト(実フォルダ閲覧中は null)
        private List<MET_ShortcutNode> CurrentVirtualList
        {
            get
            {
                if (navStack.Count == 0) return data.roots;
                var top = navStack[navStack.Count - 1];
                return top.IsReal ? null : top.virtualFolder.children;
            }
        }

        // ------------------------------------------------------------
        // 表示用タイル(仮想ノード / 実アセットの両対応)
        // ------------------------------------------------------------
        private class Tile
        {
            public MET_ShortcutNode node;   // 仮想ノード(null = 実アセット)
            public string assetPath;        // アイコン・参照用のアセットパス
            public bool isFolderTile;       // フォルダとして振る舞う
            public bool isRealAsset;        // 実アセット(ツリー操作の対象外)
            public string name;
            public string key;              // 選択キー
            public bool hasColor;
            public Color color;
            public string customIconGuid;
        }
        private List<Tile> tilesCache;
        private readonly HashSet<string> selectedKeys = new HashSet<string>();
        private string selectionAnchor;      // Shift範囲選択の基点

        // ダブルクリック判定(clickCountはFocus等でリセットされ得るため時間ベースで判定)
        private double lastClickTime;
        private string lastClickKey;
        private const double DoubleClickSeconds = 0.3;

        private MET_ShortcutNode renamingNode;
        private string renameBuffer;
        private bool renameFocusPending;      // 名前編集開始直後、テキストフィールドへフォーカスを移すまでの猶予
        private const string RenameControlName = "MET_rename";

        // --- 内部ドラッグ(並び替え)/ クリック判定用 ---
        private string pressedKey;
        private Vector2 mouseDownPos;
        private bool dragInitiated;
        private const float DragThreshold = 6f;
        private const string DragGenericKey = "MET_ShortcutDragNode";

        private const float BaseTileWidth = 72f;
        private const float BaseTileHeight = 76f;
        private const float BaseIconSize = 48f;
        private const float MinScale = 0.5f;
        private const float MaxScale = 2.0f;

        private float Scale => Mathf.Clamp(data != null && data.iconScale > 0f ? data.iconScale : 1f, MinScale, MaxScale);
        private float TileWidth => BaseTileWidth * Scale;
        private float TileHeight => BaseTileHeight * Scale;
        private float IconSize => BaseIconSize * Scale;
        private float LabelHeight => (BaseTileHeight - BaseIconSize) * Scale;

        private bool scaleDirty;

        [MenuItem("Window/MET_ Asset Shortcuts")]
        public static void Open()
        {
            var win = GetWindow<MET_ShortcutWindow>();
            win.titleContent = new GUIContent("MET Shortcuts");
            win.Show();
        }

        private void OnEnable() => Load();

        private void Load()
        {
            data = null;
            if (File.Exists(DataPath))
            {
                try { data = JsonUtility.FromJson<MET_ShortcutData>(File.ReadAllText(DataPath)); }
                catch { data = null; }
            }
            if (data == null) data = new MET_ShortcutData();
        }

        private void Save() => File.WriteAllText(DataPath, JsonUtility.ToJson(data, true));

        // ============================================================
        // OnGUI
        // ============================================================
        private void OnGUI()
        {
            if (Event.current.type == EventType.DragExited)
            {
                pressedKey = null;
                dragInitiated = false;
            }

            // GUILayoutの整合性のため、タイルはLayoutパスで構築して1フレーム内で固定
            if (Event.current.type == EventType.Layout || tilesCache == null)
                tilesCache = BuildCurrentTiles();

            DrawBackground();
            HandleKeyboard();
            DrawTopBar();
            DrawBreadcrumb();

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            DrawGrid();
            EditorGUILayout.EndScrollView();

            HandleRenameCommitOnBlur();

            var gridAreaRect = GUILayoutUtility.GetLastRect();
            HandleEmptyAreaClick(gridAreaRect);
            HandleGridBackgroundDrop(gridAreaRect);
            HandleEmptyAreaContextMenu(gridAreaRect);

            DrawBottomBar();
        }

        private void DrawBackground()
        {
            // 実フォルダ閲覧中はショートカットを作れないため、背景色は適用せずデフォルトに戻す
            if (!data.hasBgColor || InRealMode || Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = new Color(data.bgR, data.bgG, data.bgB);
            GUI.DrawTexture(new Rect(0, 0, position.width, position.height), EditorGUIUtility.whiteTexture);
            GUI.color = prev;
        }

        // Projectウィンドウと体裁を合わせるための空のバー(パンくずの上)
        private void DrawTopBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBreadcrumb()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("ルート", EditorStyles.toolbarButton, GUILayout.Width(50)))
                ClearNav();

            for (int i = 0; i < navStack.Count; i++)
            {
                GUILayout.Label(">", GUILayout.Width(12));
                int captured = i;
                string label = navStack[i].Label ?? "";
                float w = Mathf.Clamp(20 + label.Length * 8, 40, 160);
                if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.Width(w)))
                    TruncateNav(captured + 1);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // 下部のアイコンサイズ調整バー(Projectウィンドウ下部のスライダー風)
        private void DrawBottomBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.FlexibleSpace();

            var iconStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            GUILayout.Label("小", iconStyle, GUILayout.Width(16));

            EditorGUI.BeginChangeCheck();
            float newScale = GUILayout.HorizontalSlider(Scale, MinScale, MaxScale, GUILayout.Width(140));
            if (EditorGUI.EndChangeCheck())
            {
                data.iconScale = Mathf.Clamp(newScale, MinScale, MaxScale);
                scaleDirty = true;
                Repaint();
            }

            GUILayout.Label("大", iconStyle, GUILayout.Width(16));
            GUILayout.Space(6);
            GUILayout.Label($"{Mathf.RoundToInt(Scale * 100f)}%", EditorStyles.miniLabel, GUILayout.Width(38));
            EditorGUILayout.EndHorizontal();

            // ドラッグ終了時にだけ保存(書き込み頻度を抑える)
            if (scaleDirty && Event.current.rawType == EventType.MouseUp)
            {
                Save();
                scaleDirty = false;
            }
        }

        // ============================================================
        // タイル構築
        // ============================================================
        private List<Tile> BuildCurrentTiles()
        {
            if (navStack.Count == 0)
                return BuildVirtualTiles(data.roots);

            var top = navStack[navStack.Count - 1];
            if (top.IsReal)
                return BuildRealTiles(top.realFolderPath);

            return BuildVirtualTiles(top.virtualFolder.children);
        }

        private List<Tile> BuildVirtualTiles(List<MET_ShortcutNode> nodes)
        {
            var list = new List<Tile>(nodes.Count);
            foreach (var n in nodes)
            {
                string path = n.isFolder ? null : AssetDatabase.GUIDToAssetPath(n.assetGuid);
                bool folderTile = n.isFolder || (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path));
                list.Add(new Tile
                {
                    node = n,
                    assetPath = path,
                    isFolderTile = folderTile,
                    isRealAsset = false,
                    name = n.name,
                    key = "v:" + n.id,
                    hasColor = n.hasColor,
                    color = new Color(n.colorR, n.colorG, n.colorB),
                    customIconGuid = n.customIconGuid,
                });
            }
            return list;
        }

        private List<Tile> BuildRealTiles(string folderPath)
        {
            var list = new List<Tile>();
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return list;

            // サブフォルダ(実アセット)
            var subs = AssetDatabase.GetSubFolders(folderPath);
            Array.Sort(subs, (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
            foreach (var sub in subs)
            {
                list.Add(new Tile
                {
                    node = null,
                    assetPath = sub,
                    isFolderTile = true,
                    isRealAsset = true,
                    name = Path.GetFileName(sub),
                    key = "r:" + sub,
                });
            }

            // 直下のファイル(実アセット)
            string[] files;
            try { files = Directory.GetFiles(folderPath); }
            catch { files = Array.Empty<string>(); }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string p = f.Replace('\\', '/');
                string fileName = Path.GetFileName(p);
                if (fileName.StartsWith(".")) continue;
                list.Add(new Tile
                {
                    node = null,
                    assetPath = p,
                    isFolderTile = false,
                    isRealAsset = true,
                    name = Path.GetFileNameWithoutExtension(p),
                    key = "r:" + p,
                });
            }
            return list;
        }

        // ============================================================
        // グリッド描画
        // ============================================================
        private void DrawGrid()
        {
            var items = tilesCache;

            if (items.Count == 0)
            {
                EditorGUILayout.Space(24);
                string msg = InRealMode ? "(空のフォルダ)" : "ここにフォルダ・アセットをドラッグ";
                EditorGUILayout.LabelField(msg, EditorStyles.centeredGreyMiniLabel);
                return;
            }

            float availWidth = Mathf.Max(TileWidth, position.width - 20f);
            int columns = Mathf.Max(1, Mathf.FloorToInt(availWidth / TileWidth));
            int rows = Mathf.CeilToInt(items.Count / (float)columns);

            for (int r = 0; r < rows; r++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int c = 0; c < columns; c++)
                {
                    int idx = r * columns + c;
                    if (idx >= items.Count) { GUILayout.Space(TileWidth); continue; }
                    DrawTile(items[idx]);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawTile(Tile tile)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(TileWidth));

            // アイコン決定(カスタム画像 > フォルダアイコン > アセットアイコン)
            Texture customIcon = null;
            if (!string.IsNullOrEmpty(tile.customIconGuid))
            {
                string iconPath = AssetDatabase.GUIDToAssetPath(tile.customIconGuid);
                if (!string.IsNullOrEmpty(iconPath))
                {
                    // 基底型 Texture で広く対応し、直接読めない場合はサムネイルにフォールバック
                    customIcon = AssetDatabase.LoadAssetAtPath<Texture>(iconPath);
                    if (customIcon == null) customIcon = AssetDatabase.GetCachedIcon(iconPath);
                }
            }

            Texture icon;
            if (customIcon != null) icon = customIcon;
            else if (tile.isFolderTile) icon = EditorGUIUtility.IconContent("Folder Icon").image;
            else icon = string.IsNullOrEmpty(tile.assetPath) ? null : AssetDatabase.GetCachedIcon(tile.assetPath);

            var iconRect = GUILayoutUtility.GetRect(TileWidth, IconSize);
            var fullTileRect = new Rect(iconRect.x, iconRect.y, TileWidth, TileHeight);

            // 選択ハイライト(背景)
            if (selectedKeys.Contains(tile.key) && Event.current.type == EventType.Repaint)
            {
                var prevC = GUI.color;
                GUI.color = new Color(0.24f, 0.48f, 0.90f, 0.35f);
                GUI.DrawTexture(fullTileRect, EditorGUIUtility.whiteTexture);
                GUI.color = prevC;
            }

            var iconDrawRect = new Rect(iconRect.x + (iconRect.width - IconSize) / 2f, iconRect.y, IconSize, IconSize);
            if (icon != null)
            {
                // 着色はフォルダのみ。カスタム画像がある場合は画像優先(着色しない)。
                bool tint = tile.isFolderTile && tile.hasColor && customIcon == null;
                var prevColor = GUI.color;
                if (tint) GUI.color = tile.color;
                GUI.DrawTexture(iconDrawRect, icon, ScaleMode.ScaleToFit);
                GUI.color = prevColor;
            }

            // ショートカット(管理項目)であることを示す隅マーク。実アセットには付けない。
            if (!tile.isRealAsset)
                DrawShortcutBadge(iconDrawRect);

            if (!tile.isRealAsset && renamingNode == tile.node)
            {
                GUI.SetNextControlName(RenameControlName);
                renameBuffer = EditorGUILayout.TextField(renameBuffer, GUILayout.Width(TileWidth));

                // 開始直後のみフォーカスを移す(以後は自然にフォーカスを外せるようにする)
                if (renameFocusPending)
                {
                    EditorGUI.FocusTextInControl(RenameControlName);
                    if (Event.current.type == EventType.Repaint &&
                        GUI.GetNameOfFocusedControl() == RenameControlName)
                        renameFocusPending = false;
                }

                var e = Event.current;
                if (e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { CommitRename(); e.Use(); }
                    else if (e.keyCode == KeyCode.Escape) { renamingNode = null; renameFocusPending = false; e.Use(); Repaint(); }
                }
            }
            else
            {
                var style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter, wordWrap = true };
                EditorGUILayout.LabelField(tile.name, style, GUILayout.Width(TileWidth), GUILayout.Height(LabelHeight));
            }

            EditorGUILayout.EndVertical();

            HandleTileInteraction(tile, fullTileRect);
            HandleTileContextMenu(tile, fullTileRect);
            HandleTileDragTarget(tile, fullTileRect);
        }

        // ============================================================
        // クリック(選択→開く) / 内部ドラッグ開始
        // ============================================================
        private void HandleTileInteraction(Tile tile, Rect rect)
        {
            var evt = Event.current;
            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button != 0 || !rect.Contains(evt.mousePosition)) break;
                    if (tile.node != null && renamingNode == tile.node) break; // 編集中タイルはTextFieldに任せる
                    if (renamingNode != null) CommitRename(); // 別タイルをクリックしたら編集を確定
                    pressedKey = tile.key;
                    mouseDownPos = evt.mousePosition;
                    dragInitiated = false;
                    // キーボードショートカット(F2/Del)を受け取れるよう、
                    // ウィンドウにフォーカスを移し、他コントロールの入力フォーカスを解除する
                    Focus();
                    GUIUtility.keyboardControl = 0;
                    evt.Use();
                    break;

                case EventType.MouseDrag:
                    // 実アセットは内部ドラッグ対象外(仮想ノードのみ並び替え可能)
                    if (evt.button == 0 && pressedKey == tile.key && !dragInitiated && !tile.isRealAsset &&
                        Vector2.Distance(evt.mousePosition, mouseDownPos) > DragThreshold)
                    {
                        dragInitiated = true;

                        // 掴んだ項目が選択に含まれていれば選択中の仮想ノードをまとめて、
                        // そうでなければ掴んだ1件だけを運ぶ
                        List<MET_ShortcutNode> dragNodes;
                        if (selectedKeys.Contains(tile.key))
                        {
                            dragNodes = SelectedVirtualNodes();
                            if (!dragNodes.Contains(tile.node)) dragNodes.Add(tile.node);
                        }
                        else
                        {
                            selectedKeys.Clear();
                            selectedKeys.Add(tile.key);
                            selectionAnchor = tile.key;
                            dragNodes = new List<MET_ShortcutNode> { tile.node };
                        }

                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.SetGenericData(DragGenericKey, dragNodes);
                        DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                        DragAndDrop.StartDrag(dragNodes.Count > 1 ? $"{dragNodes.Count} 項目" :
                            (string.IsNullOrEmpty(tile.name) ? "Shortcut" : tile.name));
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (evt.button == 0 && pressedKey == tile.key)
                    {
                        if (!dragInitiated && rect.Contains(evt.mousePosition))
                            PerformClick(tile);
                        pressedKey = null;
                        dragInitiated = false;
                        evt.Use();
                    }
                    break;
            }
        }

        // 選択中の項目に対する一般的なキーボード操作(F2=リネーム / Del=削除)
        private void HandleKeyboard()
        {
            var evt = Event.current;
            if (evt.type != EventType.KeyDown) return;
            if (renamingNode != null) return;            // 名前編集中は無効
            if (selectedKeys.Count == 0) return;

            if (evt.keyCode == KeyCode.Delete)
            {
                DeleteSelected();
                evt.Use();
            }
            else if (evt.keyCode == KeyCode.F2)
            {
                // 名前変更は単一選択のときのみ
                var nodes = SelectedVirtualNodes();
                if (nodes.Count == 1) { BeginRename(nodes[0]); evt.Use(); }
            }
        }

        // 選択中の仮想ノード(実アセットは除く)
        private List<MET_ShortcutNode> SelectedVirtualNodes()
        {
            var list = new List<MET_ShortcutNode>();
            if (tilesCache == null) return list;
            foreach (var t in tilesCache)
                if (selectedKeys.Contains(t.key) && !t.isRealAsset && t.node != null)
                    list.Add(t.node);
            return list;
        }

        // 名前編集の確定(Enter / フォーカスが外れたとき / 他項目クリック時)
        private void CommitRename()
        {
            if (renamingNode == null) return;
            if (!string.IsNullOrWhiteSpace(renameBuffer))
                renamingNode.name = renameBuffer.Trim();
            renamingNode = null;
            renameFocusPending = false;
            Save();
            Repaint();
        }

        // テキストフィールドからフォーカスが外れたら確定(Projectウィンドウ同様、その辺クリックで確定)
        private void HandleRenameCommitOnBlur()
        {
            if (renamingNode == null || renameFocusPending) return;
            if (Event.current.type != EventType.Repaint) return;
            if (GUI.GetNameOfFocusedControl() != RenameControlName)
                CommitRename();
        }

        private void BeginRename(MET_ShortcutNode node)
        {
            renamingNode = node;
            renameBuffer = node.name;
            renameFocusPending = true;
        }

        // 選択中のショートカットをまとめて削除(中身のあるフォルダ・複数選択時は確認ダイアログ)
        private void DeleteSelected()
        {
            var nodes = SelectedVirtualNodes();
            if (nodes.Count == 0) return;

            int folderWithChildren = nodes.Count(n => n.isFolder && n.children != null && n.children.Count > 0);
            if (nodes.Count > 1 || folderWithChildren > 0)
            {
                string msg = nodes.Count == 1
                    ? $"「{nodes[0].name}」を削除しますか?"
                    : $"{nodes.Count} 個の項目を削除しますか?";
                if (folderWithChildren > 0) msg += $"\n※ 中身のあるフォルダが {folderWithChildren} 個含まれます。";
                msg += "\n(実際のアセットは削除されません)";
                if (!EditorUtility.DisplayDialog("ショートカットの削除", msg, "削除", "キャンセル")) return;
            }

            foreach (var n in nodes) RemoveFromTree(data.roots, n);
            selectedKeys.Clear();
            selectionAnchor = null;
            Save();
            Repaint(); // タイルは次のLayoutパスで再構築
        }

        // クリック: 通常=単一選択 / Ctrl(Cmd)=トグル / Shift=範囲 / 無修飾のダブルクリック=開く
        private void PerformClick(Tile tile)
        {
            var evt = Event.current;
            bool additive = evt.control || evt.command;
            bool range = evt.shift;

            if (additive)
            {
                if (!selectedKeys.Remove(tile.key)) selectedKeys.Add(tile.key);
                selectionAnchor = tile.key;
                lastClickKey = null; // ダブルクリック無効化
                Repaint();
                return;
            }

            if (range && !string.IsNullOrEmpty(selectionAnchor))
            {
                SelectRange(selectionAnchor, tile.key);
                lastClickKey = null;
                Repaint();
                return;
            }

            // 通常クリック(単一選択)
            double now = EditorApplication.timeSinceStartup;
            bool isDouble = tile.key == lastClickKey && (now - lastClickTime) <= DoubleClickSeconds;

            selectedKeys.Clear();
            selectedKeys.Add(tile.key);
            selectionAnchor = tile.key;

            if (isDouble)
            {
                lastClickKey = null; // 連続オープン防止(次は改めて2回押しが必要)
                OpenTile(tile);
            }
            else
            {
                lastClickKey = tile.key;
                lastClickTime = now;
            }
            Repaint();
        }

        // 現在のタイル並び順で anchor～target の範囲を選択
        private void SelectRange(string anchorKey, string targetKey)
        {
            if (tilesCache == null) return;
            int ai = tilesCache.FindIndex(t => t.key == anchorKey);
            int bi = tilesCache.FindIndex(t => t.key == targetKey);
            selectedKeys.Clear();
            if (ai < 0 || bi < 0)
            {
                selectedKeys.Add(targetKey);
                return;
            }
            if (ai > bi) { int tmp = ai; ai = bi; bi = tmp; }
            for (int i = ai; i <= bi; i++) selectedKeys.Add(tilesCache[i].key);
            // anchor は維持(以後のShiftクリックで同じ基点から選べるように)
        }

        // グリッドの空き領域をクリックしたら選択解除 + 名前編集を確定
        private void HandleEmptyAreaClick(Rect areaRect)
        {
            var evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 0) return;
            if (!areaRect.Contains(evt.mousePosition)) return;

            if (renamingNode != null) CommitRename();
            selectedKeys.Clear();
            selectionAnchor = null;
            Focus();
            GUIUtility.keyboardControl = 0;
            evt.Use();
            Repaint();
        }

        private void OpenTile(Tile tile)
        {
            if (tile.isFolderTile)
            {
                if (tile.node != null && tile.node.isFolder)
                {
                    PushNav(new NavItem { virtualFolder = tile.node });
                }
                else if (!string.IsNullOrEmpty(tile.assetPath) && AssetDatabase.IsValidFolder(tile.assetPath))
                {
                    PushNav(new NavItem { realFolderPath = tile.assetPath });
                }
            }
            else if (!string.IsNullOrEmpty(tile.assetPath))
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(tile.assetPath);
                if (obj != null) AssetDatabase.OpenAsset(obj);
            }
        }

        private void PushNav(NavItem item)
        {
            if (renamingNode != null) CommitRename();
            navStack.Add(item);
            ClearSelection();
            scroll = Vector2.zero;
            Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
        }

        private void ClearNav()
        {
            if (renamingNode != null) CommitRename();
            navStack.Clear();
            ClearSelection();
            scroll = Vector2.zero;
            Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
        }

        private void TruncateNav(int keepCount)
        {
            if (renamingNode != null) CommitRename();
            if (keepCount < navStack.Count)
                navStack.RemoveRange(keepCount, navStack.Count - keepCount);
            ClearSelection();
            scroll = Vector2.zero;
            Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
        }

        private void ClearSelection()
        {
            selectedKeys.Clear();
            selectionAnchor = null;
        }

        // ============================================================
        // 右クリックメニュー
        // ============================================================
        private void HandleTileContextMenu(Tile tile, Rect rect)
        {
            if (Event.current.type != EventType.ContextClick || !rect.Contains(Event.current.mousePosition)) return;

            // 選択外の項目を右クリックしたら、その項目だけを選択対象にする
            if (!selectedKeys.Contains(tile.key))
            {
                selectedKeys.Clear();
                selectedKeys.Add(tile.key);
                selectionAnchor = tile.key;
            }

            var menu = new GenericMenu();

            if (tile.isRealAsset)
            {
                // 実アセット: プロジェクトを壊さないよう最小限の操作のみ
                menu.AddItem(new GUIContent(tile.isFolderTile ? "開く" : "アセットを開く"), false, () => OpenTile(tile));
                menu.AddItem(new GUIContent("Projectウィンドウで表示"), false, () => PingAsset(tile.assetPath));
            }
            else
            {
                var node = tile.node;
                menu.AddItem(new GUIContent("名前を変更"), false, () => BeginRename(node));
                if (node.isFolder)
                {
                    menu.AddItem(new GUIContent("開く"), false, () => PushNav(new NavItem { virtualFolder = node }));
                    menu.AddItem(new GUIContent("サブフォルダを追加"), false, () =>
                    {
                        node.children.Add(new MET_ShortcutNode { name = "New Folder", isFolder = true });
                        Save();
                    });
                }
                else
                {
                    menu.AddItem(new GUIContent("Projectウィンドウで表示"), false, () => PingAsset(tile.assetPath));
                }

                menu.AddSeparator("");

                // 色はフォルダのみ
                if (tile.isFolderTile)
                {
                    menu.AddItem(new GUIContent("色を設定..."), false, () =>
                    {
                        var initial = node.hasColor ? new Color(node.colorR, node.colorG, node.colorB) : Color.white;
                        MET_ColorPickerPopup.Show(c =>
                        {
                            node.hasColor = true;
                            node.colorR = c.r; node.colorG = c.g; node.colorB = c.b;
                            Save();
                            Repaint();
                        }, initial);
                    });
                    if (node.hasColor)
                        menu.AddItem(new GUIContent("色を解除"), false, () => { node.hasColor = false; Save(); Repaint(); });
                }

                menu.AddItem(new GUIContent("アイコン画像を設定..."), false, () =>
                {
                    Texture current = null;
                    if (!string.IsNullOrEmpty(node.customIconGuid))
                        current = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(node.customIconGuid));
                    MET_IconPickerPopup.Show(tex =>
                    {
                        string path = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
                        node.customIconGuid = string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
                        Save();
                        Repaint();
                    }, current);
                });
                if (!string.IsNullOrEmpty(node.customIconGuid))
                    menu.AddItem(new GUIContent("アイコン画像を解除"), false, () => { node.customIconGuid = null; Save(); Repaint(); });

                menu.AddSeparator("");
                int selCount = SelectedVirtualNodes().Count;
                string delLabel = selCount > 1 ? $"ショートカットから削除 ({selCount})" : "ショートカットから削除";
                menu.AddItem(new GUIContent(delLabel), false, DeleteSelected);
            }

            menu.ShowAsContext();
            Event.current.Use();
        }

        private static void PingAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj != null) EditorGUIUtility.PingObject(obj);
        }


        // ============================================================
        // ドラッグ&ドロップ(登録 + 内部並び替え。実フォルダ閲覧中は無効)
        // ============================================================
        private void HandleTileDragTarget(Tile tile, Rect rect)
        {
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
            if (!rect.Contains(evt.mousePosition)) return;
            if (InRealMode) { DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; return; }

            var parentList = CurrentVirtualList;
            var draggedNodes = GetDraggedNodes();

            if (draggedNodes != null)
            {
                if (!CanDropOn(draggedNodes, tile.node))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                    return;
                }

                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                DrawDropHighlight(rect);

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    if (tile.node.isFolder)
                    {
                        foreach (var d in draggedNodes) RemoveFromTree(data.roots, d);
                        tile.node.children.AddRange(draggedNodes);
                    }
                    else
                    {
                        bool before = evt.mousePosition.x < rect.x + rect.width / 2f;
                        foreach (var d in draggedNodes) RemoveFromTree(data.roots, d);
                        int insertAt = parentList.IndexOf(tile.node);
                        if (insertAt < 0) insertAt = parentList.Count;
                        if (!before) insertAt += 1;
                        parentList.InsertRange(Mathf.Clamp(insertAt, 0, parentList.Count), draggedNodes);
                    }
                    SelectNodes(draggedNodes);
                    Save();
                    Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
                    evt.Use();
                }
                else evt.Use();
            }
            else if (DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                DrawDropHighlight(rect);

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    var target = (tile.node != null && tile.node.isFolder) ? tile.node.children : parentList;
                    foreach (var obj in DragAndDrop.objectReferences)
                        AddShortcut(target, obj);
                    Save();
                    Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
                    evt.Use();
                }
                else evt.Use();
            }
        }

        // ショートカットである事を示す隅バッジ(OSのショートカット矢印風)
        private static GUIStyle badgeStyle;
        private void DrawShortcutBadge(Rect iconDrawRect)
        {
            if (Event.current.type != EventType.Repaint) return;

            // アイコンの大きさに合わせてバッジも拡縮
            float s = Mathf.Clamp(iconDrawRect.width * 0.34f, 12f, 24f);
            float radius = s * 0.25f;
            var badge = new Rect(iconDrawRect.x - 1f, iconDrawRect.yMax - s + 1f, s, s);

            // 角丸の暗い下地
            GUI.DrawTexture(badge, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(0.10f, 0.10f, 0.10f, 0.92f), 0f, radius);
            // 明るい縁取り
            GUI.DrawTexture(badge, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                new Color(1f, 1f, 1f, 0.85f), 1f, radius);

            if (badgeStyle == null)
                badgeStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            badgeStyle.fontSize = Mathf.RoundToInt(s * 0.7f);

            var prev = badgeStyle.normal.textColor;
            badgeStyle.normal.textColor = Color.white;
            GUI.Label(badge, "↗", badgeStyle);
            badgeStyle.normal.textColor = prev;
        }

        private void DrawDropHighlight(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = new Color(0.2f, 0.6f, 1f, 0.6f);
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(0.2f, 0.6f, 1f, 0.15f), 2, 3);
            GUI.color = prev;
        }

        private void HandleGridBackgroundDrop(Rect areaRect)
        {
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
            if (!areaRect.Contains(evt.mousePosition)) return;
            if (InRealMode) return; // 実フォルダ閲覧中は登録しない

            var draggedNodes = GetDraggedNodes();
            bool external = draggedNodes == null && DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0;
            if (draggedNodes == null && !external) return;

            DragAndDrop.visualMode = draggedNodes != null ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                var list = CurrentVirtualList;
                if (draggedNodes != null)
                {
                    foreach (var d in draggedNodes) RemoveFromTree(data.roots, d);
                    list.AddRange(draggedNodes);
                    SelectNodes(draggedNodes);
                }
                else
                {
                    foreach (var obj in DragAndDrop.objectReferences)
                        AddShortcut(list, obj);
                }
                Save();
                Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
                evt.Use();
            }
        }

        private static List<MET_ShortcutNode> GetDraggedNodes()
        {
            var nodes = DragAndDrop.GetGenericData(DragGenericKey) as List<MET_ShortcutNode>;
            return (nodes != null && nodes.Count > 0) ? nodes : null;
        }

        // ドロップ可否: 対象が仮想ノードで、運ぶノード自身やその子孫でないこと
        private static bool CanDropOn(List<MET_ShortcutNode> dragged, MET_ShortcutNode target)
        {
            if (target == null) return false;
            foreach (var d in dragged)
            {
                if (d == target) return false;                 // 自分自身へは不可
                if (IsDescendant(d, target)) return false;     // 自分の子孫(=掴んだフォルダの中)へは不可
            }
            return true;
        }

        // 指定ノード群を選択状態にする
        private void SelectNodes(List<MET_ShortcutNode> nodes)
        {
            selectedKeys.Clear();
            foreach (var n in nodes) selectedKeys.Add("v:" + n.id);
            selectionAnchor = nodes.Count > 0 ? "v:" + nodes[nodes.Count - 1].id : null;
        }

        private void AddShortcut(List<MET_ShortcutNode> list, UnityEngine.Object obj)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) return;
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (list.Any(n => !n.isFolder && n.assetGuid == guid)) return; // 重複登録防止
            list.Add(new MET_ShortcutNode { name = obj.name, isFolder = false, assetGuid = guid });
        }

        // ============================================================
        // 何もない場所の右クリック
        // ============================================================
        private void HandleEmptyAreaContextMenu(Rect areaRect)
        {
            var evt = Event.current;
            if (evt.type != EventType.ContextClick || !areaRect.Contains(evt.mousePosition)) return;
            // 実フォルダ閲覧中は新規作成も背景色も対象外
            if (InRealMode) { evt.Use(); return; }

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("新規フォルダ"), false, () =>
            {
                CurrentVirtualList?.Add(new MET_ShortcutNode { name = "New Folder", isFolder = true });
                Save();
                Repaint(); // タイルは次のLayoutパスで再構築(フレーム途中でnull化しない)
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("背景色を設定..."), false, () =>
            {
                var initial = data.hasBgColor
                    ? new Color(data.bgR, data.bgG, data.bgB)
                    : new Color(0.22f, 0.22f, 0.22f);
                MET_ColorPickerPopup.Show(c =>
                {
                    data.hasBgColor = true;
                    data.bgR = c.r; data.bgG = c.g; data.bgB = c.b;
                    Save();
                    Repaint();
                }, initial);
            });
            if (data.hasBgColor)
                menu.AddItem(new GUIContent("背景色を解除"), false, () =>
                {
                    data.hasBgColor = false;
                    Save();
                    Repaint();
                });

            menu.ShowAsContext();
            evt.Use();
        }

        // ============================================================
        // ツリー操作ヘルパー
        // ============================================================
        private static bool RemoveFromTree(List<MET_ShortcutNode> list, MET_ShortcutNode target)
        {
            if (list.Remove(target)) return true;
            foreach (var n in list)
            {
                if (n.isFolder && RemoveFromTree(n.children, target)) return true;
            }
            return false;
        }

        private static bool IsDescendant(MET_ShortcutNode possibleAncestor, MET_ShortcutNode node)
        {
            if (!possibleAncestor.isFolder) return false;
            foreach (var c in possibleAncestor.children)
            {
                if (c == node || IsDescendant(c, node)) return true;
            }
            return false;
        }
    }
}
