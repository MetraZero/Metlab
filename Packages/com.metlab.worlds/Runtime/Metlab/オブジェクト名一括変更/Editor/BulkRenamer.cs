using UnityEngine;
using UnityEditor;

public class BulkRenamer : EditorWindow
{
    [MenuItem("Tools/一括名前変更 (連番)")]
    public static void RenameObjects()
    {
        // ヒエラルキーで選択中のオブジェクトを取得
        GameObject[] selectedObjects = Selection.gameObjects;
        
        if (selectedObjects.Length == 0) return;

        // 基準にする名前（今回は「壁」）
        string baseName = "壁";

        for (int i = 0; i < selectedObjects.Length; i++)
        {
            // 1個目は「壁」、2個目以降は「壁 (1)」「壁 (2)」にする
            if (i == 0) {
                selectedObjects[i].name = baseName;
            } else {
                selectedObjects[i].name = baseName + " (" + i + ")";
            }
        }
    }
}