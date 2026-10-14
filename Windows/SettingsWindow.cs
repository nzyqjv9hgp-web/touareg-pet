using System.Diagnostics;

namespace Touareg.Windows;

internal sealed class SettingsWindow : Form
{
    readonly Label status;
    internal event Action? Changed;
    internal event Action? CheckUpdates;
    internal SettingsWindow(Settings s, string updateStatus)
    {
        Text = "Touareg — настройки"; Icon = Assets.Icon(); ClientSize = new(540, 581);
        StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; Font = new("Segoe UI", 10); BackColor = Color.FromArgb(245, 247, 251);
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        var title = new Label { Text = "Мини-я на Touareg", Font = new("Segoe UI Semibold", 19), Location = new(26, 22), AutoSize = true };
        Controls.Add(title);
        Controls.Add(new Label { Text = "Зажми ЛКМ на машине — бернаут, отпусти — разгон.\nПотяни мышью — перенесёшь, подержи на месте — взлетит.", Location = new(28, 64), Size = new(480, 44), ForeColor = Color.FromArgb(75, 88, 109) });
        Slider("Размер", 118, 150, 280, s.Size, value => { s.Size = value; Changed?.Invoke(); }, "пикс.");
        Slider("Скорость", 179, 50, 150, s.Speed, value => { s.Speed = value; Changed?.Invoke(); }, "%");
        Slider("Громкость V8", 240, 0, 100, s.Volume, value => { s.Volume = value; Changed?.Invoke(); }, "%");
        Check("Включить звук двигателя", 302, s.Sound, v => { s.Sound = v; Changed?.Invoke(); });
        Check("Скрывать в полноэкранных играх и видео", 333, s.HideFullscreen, v => { s.HideFullscreen = v; Changed?.Invoke(); });
        Check("Запускать вместе с Windows", 364, s.Startup, v =>
        {
            try { s.SetStartup(v); } catch (Exception e) { Log.Write("autostart", e); MessageBox.Show("Не удалось изменить автозапуск.", "Touareg"); }
            Changed?.Invoke();
        });
        Check("Автоматически получать обновления", 395, s.AutoUpdate, v => { s.AutoUpdate = v; Changed?.Invoke(); });
        Check("Ездить по блокам страниц в браузере", 426, s.Pages, v => { s.Pages = v; Changed?.Invoke(); });
        status = new() { Text = updateStatus, Location = new(28, 462), Size = new(355, 43), Font = new("Segoe UI", 9), ForeColor = Color.FromArgb(83, 99, 121) };
        Controls.Add(status);
        var check = new Button { Text = "Проверить", Location = new(397, 462), Size = new(115, 32), FlatStyle = FlatStyle.Flat };
        check.Click += (_, _) => CheckUpdates?.Invoke(); Controls.Add(check);
        var credits = new LinkLabel { Text = "Авторы модели, звука и лицензии", Location = new(28, 529), AutoSize = true };
        credits.LinkClicked += (_, _) => Credits(); Controls.Add(credits);
        Controls.Add(new Label { Text = "v" + Install.Version + " · Windows", Location = new(395, 531), Size = new(125, 22), ForeColor = Color.Gray });
    }
    internal void UpdateStatus(string text) { if (!IsDisposed) status.Text = text; }
    void Check(string text, int y, bool value, Action<bool> change)
    {
        var check = new CheckBox { Text = text, Location = new(28, y), Size = new(475, 26), Checked = value };
        check.CheckedChanged += (_, _) => change(check.Checked); Controls.Add(check);
    }
    void Slider(string text, int y, int min, int max, int value, Action<int> change, string unit)
    {
        Controls.Add(new Label { Text = text, Location = new(28, y), AutoSize = true });
        var label = new Label { Text = $"{value} {unit}", Location = new(424, y), Size = new(90, 22), TextAlign = ContentAlignment.MiddleRight }; Controls.Add(label);
        var track = new TrackBar { Minimum = min, Maximum = max, Value = value, Location = new(22, y + 20), Size = new(493, 32), TickStyle = TickStyle.None, SmallChange = 5, LargeChange = 10 };
        track.ValueChanged += (_, _) => { label.Text = $"{track.Value} {unit}"; change(track.Value); }; Controls.Add(track);
    }
    void Credits()
    {
        var dialog = new Form { Text = "Touareg — авторы и лицензии", ClientSize = new(535, 470), StartPosition = FormStartPosition.CenterParent, Font = Font, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog };
        dialog.Controls.Add(new Label { Text = "3D-модель: 1975 Porsche 911 (930) Turbo\nАвтор: VTX · Sketchfab\nЛицензия: CC BY-NC-SA 4.0. Модель упрощена,\nматериалы запечены; изменённая модель — под той же лицензией.", Location = new(24, 23), Size = new(485, 84) });
        var model = new LinkLabel { Text = "Модель", Location = new(24, 110), AutoSize = true };
        model.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://sketchfab.com/3d-models/1975-porsche-911-930-turbo-86b165439ff4483a981629ffc10d4e10") { UseShellExecute = true }); dialog.Controls.Add(model);
        var modelLicense = new LinkLabel { Text = "Условия лицензии модели", Location = new(310, 110), AutoSize = true };
        modelLicense.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://creativecommons.org/licenses/by-nc-sa/4.0/") { UseShellExecute = true }); dialog.Controls.Add(modelLicense);
        dialog.Controls.Add(new Label { Text = "Настоящая запись Ford V8 5.0\n\nAcceleration sounds of Ford V8 5 Liter engine\nАвтор: noiseloop · Freesound\nЛицензия: Creative Commons Attribution 3.0\n\nИз записи сделаны фрагменты разгона и езды,\nвыровнена громкость, сглажены стыки.\n\nNAudio 2.2.1, Vortice.Windows, Interop.UIAutomationClient — MIT.\nСреда .NET — Microsoft.", Location = new(24, 150), Size = new(485, 248) });
        var source = new LinkLabel { Text = "Оригинальная запись", Location = new(24, 405), AutoSize = true };
        source.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://freesound.org/people/noiseloop/sounds/455926/") { UseShellExecute = true }); dialog.Controls.Add(source);
        var license = new LinkLabel { Text = "Условия лицензии звука", Location = new(310, 405), AutoSize = true };
        license.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://creativecommons.org/licenses/by/3.0/") { UseShellExecute = true }); dialog.Controls.Add(license);
        dialog.ShowDialog(this); dialog.Dispose();
    }
}
