namespace Touareg.Windows;

internal static class Program
{
    internal static Mutex? SingleInstance;
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => { Log.Write("UI", e.Exception); };
        try
        {
            if (!args.Contains("--installed")) { Install.Launch(); return; }
            SingleInstance = new Mutex(false, @"Local\TouaregPet-" + Environment.UserName);
            bool acquired;
            try { acquired = SingleInstance.WaitOne(TimeSpan.FromSeconds(2)); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { MessageBox.Show("Touareg уже запущен. Его меню — в значке рядом с часами.", "Touareg"); return; }
            try { Application.Run(new PetContext(args.Contains("--welcome"))); }
            finally { SingleInstance.ReleaseMutex(); SingleInstance.Dispose(); SingleInstance = null; }
        }
        catch (Exception e)
        {
            Log.Write("startup", e);
            MessageBox.Show("Не удалось запустить питомца. Подробности сохранены в папке TouaregPet рядом с настройками.\n\n" + e.Message, "Touareg", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
