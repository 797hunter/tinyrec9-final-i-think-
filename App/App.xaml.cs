// App.xaml.cs - start-up and crash handling. Any unexpected error is written to data\error.log and shown in a box.
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TinyRec;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUiError;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log(a.ExceptionObject?.ToString());
        TaskScheduler.UnobservedTaskException += (_, a) => { Log(a.Exception?.ToString()); a.SetObserved(); };
        try
        {
            var w = new MainWindow();
            w.Start();
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            MessageBox.Show($"TinyRec couldn't start:\n\n{ex.Message}\n\nDetails were saved to data\\error.log", "TinyRec 9",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    void OnUiError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception.ToString());
        e.Handled = true;
        if (MainWindow is TinyRec.MainWindow mw && mw.IsLoaded) mw.Toast($"Something went wrong: {e.Exception.Message}  (details in data\\error.log)", "error", 7000);
        else MessageBox.Show(e.Exception.Message, "TinyRec 9", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(TinyRec.MainWindow.Data);
            File.AppendAllText(TinyRec.MainWindow.ErrorLog, $"\n--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---\n{text}\n");
        }
        catch { }
    }
}
