# Metlab

MetraZero制作のVRChatワールド／アバター向けスクリプトと関連アセットを管理するリポジトリです。

## ドキュメント

- [機能一覧・各機能の紹介（GitHub Wiki）](https://github.com/MetraZero/Metlab/wiki)
- [導入方法](https://github.com/MetraZero/Metlab/wiki/Installation)
- [開発段階と注意事項](https://github.com/MetraZero/Metlab/wiki/Package-Status)
- [外部依存・謝辞](https://github.com/MetraZero/Metlab/wiki/External-Dependencies)

## パッケージ

- `com.metlab.worlds`：ワールド向けUdonSharp・Editor拡張・Shader・関連アセット
- `com.metlab.avatars`：アバター向けEditor拡張・Prefab・Animation・関連アセット

## VCCへの登録

VCCのカスタムリポジトリへ次のURLを登録します。

`https://metrazero.github.io/Metlab/index.json`

## 開発

現在使用中の2つのUnityプロジェクトは、`D:\Unity\Metlab`内のローカルパッケージを直接参照します。これにより、Unity上から編集した内容がこのGitリポジトリへそのまま反映されます。

公開するときは、ルートにある`Metlabを公開.cmd`を実行し、バージョン番号と更新内容を入力します。

Wiki原稿は`D:\Unity\Metlab.wiki`で管理します。Markdownを編集した後、ルートにある`Wikiを公開.cmd`を実行するとGitHub Wikiへ反映されます。

## ライセンス

[MIT License](LICENSE)
