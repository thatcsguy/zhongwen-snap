# ZhongWen Snap

A small Windows 11 tray app for reading and translating text on your screen. Press **Ctrl+Alt+T**, drag around a subtitle or other text, and get Traditional Chinese, pinyin, natural English, and a short learner note in a floating popup. Every capture uses a fresh selection.

## Download and use

Download [ZhongWenSnap-Windows.zip](dist/ZhongWenSnap-Windows.zip), extract it, and put `ZhongWenSnap.exe` in a permanent folder. You can also download [the EXE directly](dist/ZhongWenSnap.exe). The app is self-contained apart from the .NET Framework included with Windows 11. It does not need Python, PowerToys, Tesseract, or a separate OCR installation.

1. Open `ZhongWenSnap.exe`. The Settings window appears on first launch. Enter an [OpenAI API key](https://platform.openai.com/api-keys) and save. API billing is separate from ChatGPT subscriptions.
2. Press **Ctrl+Alt+T** and drag around the text. Select text in the popup to copy it with **Ctrl+C**. The popup stays open until you press **Esc** or click **×**.
3. Right-click the green `中` tray icon to capture again, view history, edit Settings, or exit. The tray icon may be under **^** near the clock. Opening the EXE again while it is running shows a reminder instead of starting a second copy.

In Settings you can change the global hotkey, model, and prompt, and choose whether to start when you sign in. The default model is `gpt-6-luna`; the app sends an image and asks the model to read and explain it in one request. The prompt controls the translation style and learner note while the popup keeps the same four fields. Select a tight region to improve reading and keep image usage small.

If an earlier tray version is running, exit it before opening ZhongWen Snap. Existing settings, API key, and history are copied on first launch. If you had Windows startup enabled, the app updates the startup entry to its new name and location.

## Sharing and privacy

The ZIP or EXE can be copied to another Windows 11 computer. Each Windows user enters their own API key. If two people use the same key, both computers draw from the same API account and balance. Do not put an API key in the ZIP, source files, or messages.

Settings and text history are stored separately for each Windows account under `%LOCALAPPDATA%\ZhongWenSnap`. The key is encrypted for the current Windows user with DPAPI. Screenshots are kept in memory and only the selected crop is sent to OpenAI for OCR and translation. The request sets `store` to `false`; the app does not save screenshots. Use **History → Clear history** to delete local translations.

An internet connection, API credit, and an OpenAI API key are required. This EXE is unsigned, so Windows SmartScreen may ask you to review it after download. Some protected videos and exclusive fullscreen apps prevent screenshots or global shortcuts; windowed or borderless playback can help.

## Build from source

Run `build.cmd` on Windows to build `dist\ZhongWenSnap.exe`. Run `test.cmd` for local checks of hotkey registration, API request and response handling, and migration of prior settings and history. The source uses Windows Forms and the .NET Framework compiler; no package restore is required.

The OpenAI request uses the [Responses API with image input](https://developers.openai.com/api/docs/guides/images-vision) and [structured output](https://developers.openai.com/api/docs/guides/structured-outputs). The [GPT-6 Luna model](https://developers.openai.com/api/docs/models/gpt-6-luna) supports both.
