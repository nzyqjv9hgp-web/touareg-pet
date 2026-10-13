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
    readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 30000 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly ToolStripMenuItem pause = new("Пауза"), updateItem = new("Проверить обновления"), restart = new("Перезапустить для обновления") { Visible = false };
    SettingsWindow? preferences;
    double last, scan;
    bool hidden, disposed;

    internal PetContext(bool welcome)
    {
        renderer = new(sprites);
        var menu = new ContextMenuStrip();
        pause.Click += (_, _) => SetPause(!world.Paused);
        menu.Items.Add(pause);
        menu.Items.Add("Вернуть питомца", null, (_, _) => { Apply(); world.SetSurfaces(Desktop.Scan(world.CarWidth)); world.Reset(); });
        menu.Items.Add("Настройки…", null, (_, _) => Preferences()); menu.Items.Add(new ToolStripSeparator());
        updateItem.Click += async (_, _) => { await updater.Check(); if (!updater.Ready) Balloon(updater.Status); };
        restart.Click += (_, _) => { Process.Start(new ProcessStartInfo(Install.Launcher) { UseShellExecute = true }); ExitThread(); };
        menu.Items.Add(updateItem); menu.Items.Add(restart); menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());
        tray = new() { Icon = Assets.Icon(), Visible = true, Text = "Мини-я на Touareg", ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => Preferences(); pet.ContextMenuStrip = menu;
        pet.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { world.Hold(true); pet.Capture = true; } };
        pet.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) { world.Hold(false); pet.Capture = false; } };
        pet.MouseCaptureChanged += (_, _) => { if (!pet.Capture) world.Hold(false); };
        audio.Error += message => pet.BeginInvoke(() => { Log.Write("audio", new Exception(message)); Balloon("Звук пока недоступен. Питомец продолжает работать."); });
        updater.Changed += () => { restart.Visible = updater.Ready; preferences?.UpdateStatus(updater.Status); };
        Apply(); world.SetSurfaces(Desktop.Scan(world.CarWidth));
        effects.Show(); pet.Show(); renderer.Draw(world, pet, effects);
        timer.Tick += (_, _) => Tick(); timer.Start();
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = 6 * 60 * 60 * 1000;
            if (settings.AutoUpdate) await updater.Check();
        };
        updateTimer.Start();
        if (welcome) { Preferences(); Balloon("Удерживай ЛКМ на машине — бернаут. Отпусти — быстрый старт."); }
        else Balloon("Touareg на рабочем столе. Меню — правой кнопкой по машине или значку рядом с часами.");
    }

    void Apply() { world.CarWidth = settings.Size; world.Speed = settings.Speed / 100.0; }
    void SetPause(bool value)
    {
        world.Hold(false); pet.Capture = false; world.Paused = value; pause.Checked = value;
        pause.Text = value ? "Продолжить" : "Пауза"; audio.Sync(world, settings, hidden);
    }
    void Tick()
    {
        double now = clock.Elapsed.TotalSeconds, dt = Math.Clamp(now - last, 0, .10); last = now; scan += dt;
        if (scan >= .35)
        {
            scan = 0; Apply(); world.SetSurfaces(Desktop.Scan(world.CarWidth));
            bool next = settings.HideFullscreen && Desktop.Fullscreen();
            if (next != hidden)
            {
                hidden = next;
                if (hidden) { world.Hold(false); pet.Capture = false; pet.Hide(); effects.Hide(); }
                else { effects.Show(); pet.Show(); }
            }
            Native.SetWindowPos(pet.Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x13);
        }
        if (world.Held && (Native.GetAsyncKeyState(1) & 0x8000) == 0) { world.Hold(false); pet.Capture = false; }
        if (!hidden) world.Step(dt);
        audio.Sync(world, settings, hidden);
        if (!hidden) renderer.Draw(world, pet, effects);
    }
    void Preferences()
    {
        if (preferences is not null && !preferences.IsDisposed) { preferences.Show(); preferences.Activate(); return; }
        preferences = new(settings, updater.Status);
        preferences.Changed += () => { Apply(); world.SetSurfaces(Desktop.Scan(world.CarWidth)); settings.Save(); };
        preferences.CheckUpdates += async () => await updater.Check();
        preferences.Show();
    }
    void Balloon(string text) { tray.BalloonTipTitle = "Touareg"; tray.BalloonTipText = text; tray.ShowBalloonTip(3500); }
    protected override void ExitThreadCore()
    {
        if (disposed) return; disposed = true;
        timer.Stop(); updateTimer.Stop(); settings.Save(); world.Hold(false);
        tray.Visible = false; tray.Dispose(); audio.Dispose(); renderer.Dispose(); sprites.Dispose();
        preferences?.Dispose(); pet.Dispose(); effects.Dispose(); timer.Dispose(); updateTimer.Dispose();
        base.ExitThreadCore();
    }
}
