# 第三方元件與授權 / Third-Party Notices

本專案自己的程式碼與文件採 MIT 授權（見 [`LICENSE`](LICENSE)）。下列第三方元件**不適用**那份授權，各自依原授權條款。

This project's own code and documentation are MIT-licensed (see [`LICENSE`](LICENSE)).
The third-party components below keep their own licenses.

## 隨 repo 附帶的 / Bundled in this repository

| 元件 | 位置 | 授權 | 來源 |
|---|---|---|---|
| **U²-Net**（輕量版 `u2netp` 模型權重，ONNX 格式） | `Assets/Resources/u2netp.onnx` | Apache License 2.0，全文在 [`Licenses/Apache-2.0.txt`](Licenses/Apache-2.0.txt) | <https://github.com/xuebinqin/U-2-Net> |
| **Material Symbols**（按鈕圖示：復原、清空、提示、旋轉、翻面、回主畫面等） | `Assets/Pics/*.png`（`Tutorial/` 底下的教學截圖除外） | Apache License 2.0，全文在 [`Licenses/Apache-2.0.txt`](Licenses/Apache-2.0.txt) | Google Fonts 的圖示庫 <https://fonts.google.com/icons> |
| **Noto Sans TC**（介面字型） | `Assets/Font/NotoSansTC-VariableFont_wght.ttf` | SIL Open Font License 1.1，全文在 [`Assets/Font/OFL.txt`](Assets/Font/OFL.txt) | <https://fonts.google.com/noto/specimen/Noto+Sans+TC> |
| **TextMesh Pro Essential Resources**（字型材質、著色器、範例 sprite） | `Assets/TextMesh Pro/` | Unity Companion License（隨 Unity 附帶） | Unity Technologies |
| └ EmojiOne 範例 sprite | `Assets/TextMesh Pro/Sprites/EmojiOne.png` | 依 EmojiOne 的條款，見 [`EmojiOne Attribution.txt`](Assets/TextMesh%20Pro/Sprites/EmojiOne%20Attribution.txt) | <https://www.emojione.com/> |

U²-Net 的論文：Xuebin Qin, Zichen Zhang, Chenyang Huang, Masood Dehghan, Osmar R. Zaiane, Martin Jagersand.
*U²-Net: Going Deeper with Nested U-Structure for Salient Object Detection.* Pattern Recognition 106, 2020.
本專案只在裝置上用這份權重執行推論，沒有重新訓練。

## 開專案時由套件管理員下載的 / Fetched by the Unity Package Manager

這些不在 repo 裡，版本釘在 `Packages/manifest.json`，授權以各套件附的檔案為準。

| 套件 | 授權 | 來源 |
|---|---|---|
| **NativeGallery**（`com.yasirkula.nativegallery`，v1.9.5） | MIT | <https://github.com/yasirkula/UnityNativeGallery> |
| **Unity Inference Engine**（`com.unity.ai.inference`）與其他 `com.unity.*` 套件 | Unity Companion License | Unity Technologies |
