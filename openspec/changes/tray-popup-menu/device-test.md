# Tray test on Ubuntu — steps for the author

Goal: learn which tray clicks really reach the app on your Ubuntu desktop. Nothing is fixed yet;
this only collects facts. It takes about 5 minutes.

## 0. Before you start

1. Pull `develop`.
2. **Quit every running Downloader** (tray menu → Quit). If one is still in the tray, a new start
   only hands over to the old one and you test the old code.
   Check: `ss -ltnp | grep 1515` must print nothing. If it prints a line, it shows `pid=NNNN`;
   end it with `kill NNNN`.
3. Turn logging on once: start the app, Settings → Logging → on, then quit it again.

## 1. Tell me about the desktop (one command)

```bash
echo "$XDG_CURRENT_DESKTOP / $XDG_SESSION_TYPE"; gnome-shell --version; gnome-extensions list --enabled | grep -i -E 'appindicator|tray'
```

Copy the output.

## 2. Run A — with the normal menu

Terminal 1 (watch the tray messages):

```bash
dbus-monitor "interface='org.kde.StatusNotifierItem'" "interface='com.canonical.dbusmenu'" > ~/tray-dbus-A.txt
```

Terminal 2 (start the app in test mode):

```bash
DLDESKTOP_TRAY_DIAG=1 ./scripts/dev-run.sh
```

Then:
1. Wait until the tray icon shows. Close the main window (it goes to the tray).
2. **Left-click** the tray icon once. Wait 3 seconds. Write down what you saw.
3. **Right-click** the tray icon once. Wait 3 seconds. Write down what you saw.
4. If a menu opened, click "Open Downloader".
5. Quit the app. Stop terminal 1 with Ctrl+C.

## 3. Run B — no menu attached

Same as run A, with two changes:

```bash
dbus-monitor "interface='org.kde.StatusNotifierItem'" "interface='com.canonical.dbusmenu'" > ~/tray-dbus-B.txt
```

```bash
DLDESKTOP_TRAY_DIAG=nomenu ./scripts/dev-run.sh
```

Do the same left-click / right-click and write down what you saw. (No menu is expected here; the
question is whether the window comes back.)

## 4. Send me

1. Start the app normally, Settings → Logging → **Export log**, save the zip.
2. Attach to the chat: the zip, `~/tray-dbus-A.txt`, `~/tray-dbus-B.txt`, the output of step 1,
   and your notes (what happened on each click, in each run).

## What I will look for

- `TRAY-DIAG: mode=…` lines: which run the log is from.
- `UI: tray icon clicked (thread N)`: the shell reported a click to the app.
- `TRAY-DIAG: native menu opening`: the shell asked for the menu.
- `Activate` / `SecondaryActivate` / `ContextMenu` / `AboutToShow` calls in the dbus files: what the
  shell sent, even if the app never saw it.
