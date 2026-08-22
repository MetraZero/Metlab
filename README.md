# Metlab

MetraZero制作のVRChatワールド／アバター向けスクリプトと関連アセットを管理するリポジトリです。

## パッケージ

- `com.metlab.worlds`：ワールド向けUdonSharp・Editor拡張・Shader・関連アセット
- `com.metlab.avatars`：アバター向けEditor拡張・Prefab・Animation・関連アセット

## VCCへの登録

VCCのカスタムリポジトリへ次のURLを登録します。

`https://metrazero.github.io/Metlab/index.json`

## 開発

現在使用中の2つのUnityプロジェクトは、`D:\Unity\Metlab`内のローカルパッケージを直接参照します。これにより、Unity上から編集した内容がこのGitリポジトリへそのまま反映されます。

公開するときは、ルートにある`Metlabを公開.cmd`を実行し、バージョン番号と更新内容を入力します。

## ライセンス

[MIT License](LICENSE)
