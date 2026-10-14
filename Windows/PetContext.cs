using System.Diagnostics;
using Touareg.Core;

namespace Touareg.Windows;

internal sealed class PetContext : ApplicationContext
{
    readonly Settings settings = Settings.Load();
    readonly World world = new();
    readonly LayerWindow pet = new(false), effects = new(true);
    readonly Sprites sprites = new();
    readonly EngineAudio audio = new();
    readonly Updater updater = new();
    readonly NotifyIcon tray;
    readonly Renderer renderer;
    readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 30000 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly ToolStripMenuItem pause = new("Пауза"), updateItem = new("Проверить обновления"), restart = new("Перезапустить для обновления") { Visible = false };
    readonly DesktopWatcher watcher;
    readonly FrameClock frames;
    SettingsWindow? preferences;
    double last, topmost;
    bool hidden, fullscreen, disposed;
    Native.Point press;

    internal PetContext(bool welcome)
    {
        renderer = new(sprites, Renderer3D.TryCreate()); watcher = new(settings.Size);
        var menu = new ContextMenuStrip();
        pause.Click += (_, _) => SetPause(!world.Paused);
        menu.Items.Add(pause);
        menu.Items.Add("Вернуть питомца", null, (_, _) => { Apply(); Rescan(); world.Reset(); });
        menu.Items.Add("Настройки…", null, (_, _) => Preferences()); menu.Items.Add(new ToolStripSeparator());
        updateItem.Click += async (_, _) => { await updater.Check(); if (!updater.Ready) Balloon(updater.Status); };
        restart.Click += (_, _) => { Process.Start(new ProcessStartInfo(Install.Launcher) { UseShellExecute = true }); ExitThread(); };
        menu.Items.Add(updateItem); menu.Items.Add(restart); menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());
        tray = new() { Icon = Assets.Icon(), Visible = true, Text = "Мини-я на Touareg", ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Preferences(); pet.ContextMenuStrip = menu; pet.Cursor = Cursors.Hand;
        pet.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            Native.GetCursorPos(out press); world.Grab(press.X, press.Y); world.Hold(true); pet.Capture = true;
        };
        pet.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) { world.Hold(false); pet.Capture = false; } };
        pet.MouseCaptureChanged += (_, _) => { if (!pet.Capture) world.Hold(false); };
        audio.Error += message => pet.BeginInvoke(() => { Log.Write("audio", new Exception(message)); Balloon("Звук пока недоступен. Питомец продолжает работать."); });
        updater.Changed += () => { restart.Visible = updater.Ready; preferences?.UpdateStatus(updater.Status); };
        Apply(); Rescan();
        effects.Show(); pet.Show(); renderer.Draw(world, pet, effects, 0);
        frames = new(pet, Tick);
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = 6 * 60 * 60 * 1000;
            if (settings.AutoUpdate) await updater.Check();
        };
        updateTimer.Start();
        if (welcome) { Preferences(); Balloon("Удерживай ЛКМ на машине — бернаут. Потяни мышью — перенесёшь и бросишь."); }
        else Balloon("Touareg на рабочем столе. Меню — правой кнопкой по машине или значку рядом с часами.");
    }

    void Apply() { world.CarWidth = settings.Size; world.Speed = settings.Speed / 100.0; watcher.CarWidth = settings.Size; }
    void Rescan() => Use(DesktopWatcher.Scan(settings.Size));
    void Use(DesktopWatcher.Snapshot snapshot)
    {
        world.SetScreens(snapshot.Screens); world.SetSurfaces(snapshot.Surfaces); fullscreen = snapshot.Fullscreen;
    }
    void SetPause(bool value)
    {
        world.Hold(false); pet.Capture = false; world.Paused = value; pause.Checked = value;
        pause.Text = value ? "Продолжить" : "Пауза"; audio.Sync(world, settings, hidden);
    }
    void Tick()
    {
        double now = clock.Elapsed.TotalSeconds, dt = Math.Clamp(now - last, 0, .10); last = now; topmost += dt;
        if (watcher.Take() is { } snapshot) Use(snapshot);
        bool next = settings.HideFullscreen && fullscreen;
        if (next != hidden)
        {
            hidden = next;
            if (hidden) { world.Hold(false); pet.Capture = false; pet.Hide(); effects.Hide(); }
            else { effects.Show(); pet.Show(); }
        }
        if (topmost >= .5) { topmost = 0; Native.SetWindowPos(pet.Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x13); }
        // GetAsyncKeyState reads physical buttons: with swapped buttons the primary one is the right button.
        int primary = Native.GetSystemMetrics(23) != 0 ? 2 : 1;
        if (world.Held && (Native.GetAsyncKeyState(primary) & 0x8000) == 0) { world.Hold(false); pet.Capture = false; }
        // Holding still is a burnout; moving the mouse while holding picks the car up.
        if (world.Held && Native.GetCursorPos(out var cursor))
        {
            if (!world.Dragging && Math.Abs(cursor.X - press.X) + Math.Abs(cursor.Y - press.Y) > 6) world.BeginDrag();
            if (world.Dragging) world.DragTo(cursor.X, cursor.Y);
        }
        if (!hidden) world.Step(dt);
        audio.Sync(world, settings, hidden);
        if (!hidden) renderer.Draw(world, pet, effects, dt);
    }
    void Preferences()
    {
        if (preferences is not null && !preferences.IsDisposed) { preferences.Show(); preferences.Activate(); return; }
        preferences = new(settings, updater.Status);
        preferences.Changed += () => { Apply(); Rescan(); settings.Save(); };
        preferences.CheckUpdates += async () => await updater.Check();
        preferences.Show();
    }
    void Balloon(string text) { tray.BalloonTipTitle = "Touareg"; tray.BalloonTipText = text; tray.ShowBalloonTip(3500); }
    protected override void ExitThreadCore()
    {
        if (disposed) return; disposed = true;
        frames.Dispose(); watcher.Dispose(); updateTimer.Stop(); settings.Save(); world.Hold(false);
        tray.Visible = false; tray.Dispose(); audio.Dispose(); renderer.Dispose(); sprites.Dispose();
        preferences?.Dispose(); pet.Dispose(); effects.Dispose(); updateTimer.Dispose();
        base.ExitThreadCore();
    }
}
