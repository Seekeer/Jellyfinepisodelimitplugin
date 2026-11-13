# 🌙 Episode Limit Plugin for Jellyfin

Stop playback after X episodes — perfect for falling asleep!

Adds a 🌙 moon button to the Jellyfin web player. Choose how many items play before playback stops automatically. Four more episodes, two more songs, whatever you like. It counts completed files of any length.

## Why v2? A short, honest history

Version 1 was built in 2026 with AI assistance and worked, but it had counting bugs: the official Jellyfin Android app reports every playback stop twice (so limits counted double), log names went stale, and limits were keyed on client-supplied device IDs that could disagree with the session IDs and silently break counting.

I retired v1 in favour of Jellysleep. Then real-world testing showed both plugins hit exactly the same wall: **the official Jellyfin Android app ignores server-issued stop commands** (see [Jellysleep issue #43](https://github.com/jon4hz/jellyfin-plugin-jellysleep/issues/43)). The timer fires, the server sends the stop, and the app just keeps playing. That is a client-side limitation no server-side plugin can fix. Web clients honour the stop command perfectly.

Meanwhile, nothing available did the one thing I actually wanted at 1am: *stop after N episodes*. Jellysleep offers time-based timers and end-of-current-episode. Episode counting did not exist anywhere. So this plugin came out of retirement with every bug fixed and the log evidence designed into the code.

**To the Jellysleep team: if you want to add this UI or episode counting to Jellysleep, I am 100% on board. Go for it.** This is MIT-licensed. Take the code, take the ideas, ship it in yours. One great bedtime plugin beats two half-maintained ones, and I say that as the author of one of them.

## Installation (v2)

v2 needs the JavaScript Injector plugin. There is no install.sh and no index.html patching any more, and the button survives Jellyfin updates.

1. Go to **Dashboard → Plugins → Repositories** (in newer versions: Plugins, Catalog, ⚙️) and add this fork's catalog. It lists both plugins, built by this fork's GitHub Actions for Jellyfin 12.2+:
   - `https://raw.githubusercontent.com/Seekeer/Jellyfinepisodelimitplugin/main/manifest.json`
2. In **Plugins → Catalog**, install **JavaScript Injector**.
3. Install **Episode Limit**.
4. Restart Jellyfin. The Episode Limit plugin registers its button script with the injector automatically.
5. Hard-refresh your browser (Ctrl/Cmd + Shift + R) and play something. The 🌙 button appears in the player controls.

Upgrading from v1: remove any old index.html patch (the sed command from the v1 install), install the JavaScript Injector, and update Episode Limit. The old patch will not break anything, but running both it and the injector loads the button twice.

## Client support

- ✅ Jellyfin Web in any browser (Firefox, Chrome, Safari): the moon works and the stop command is honoured.
- ✅ Any client that honours server-issued playstate commands.
- ✅ The official Jellyfin Android app (since 2.0.1.0, fork fix). With its default native player (ExoPlayer) the app used to ignore server-issued stop commands ([Jellysleep issue #43](https://github.com/jon4hz/jellyfin-plugin-jellysleep/issues/43)): jellyfin-web never registers ExoPlayer as the current player, so `Stop` reached `playbackManager.stop()` with no player and was dropped. The injected script now forwards `Stop` to the app's `NativePlayer` bridge. The moon button only shows in the web player; with the native player, set the limit from a browser or the web player (limits are per user). Not yet verified on a device.
- ❌ Android TV and other native clients: no web UI, no button.


## Usage

While playing, tap the 🌙 button:

- Quick buttons: Off, 1, 2, 3, 4 or 5 items.
- Custom: any number from 1 to 100.
- The status line shows the limit and progress. The button turns green while a limit is active.
- When the limit is reached, the next item is stopped a moment after it starts, and the limit resets. Set it again each night.

Limits are per-user: set it on your phone's browser, it applies to every client you are signed into.

## Credits

- Built with Mistral's Vibe. v2 was diagnosed from real server logs, line by line, until the culprit confessed.
- The test data was supplied by a very tired man at 1am who pressed the moon three times and was ignored three times. He got better evidence than any test plan could.

## License

MIT, same as v1.
