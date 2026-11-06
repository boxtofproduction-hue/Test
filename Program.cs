using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace GestionGym
{
    public static class Program
    {
        [STAThread]
        public static int Main()
        {
            try { AppPaths.Init(); }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible de démarrer l'application :\n\n" + ex.Message, "Gestion GYM", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            Thread.CurrentThread.CurrentCulture = Fmt.Fr;
            Thread.CurrentThread.CurrentUICulture = Fmt.Fr;
            CultureInfo.DefaultThreadCurrentCulture = Fmt.Fr;
            CultureInfo.DefaultThreadCurrentUICulture = Fmt.Fr;

            var app = new Application();
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("fr-FR")));
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log(e.Exception);
                try { if (MainWindow.Current != null) MainWindow.Current.EmergencySave(); } catch { }
                MessageBox.Show("Une erreur inattendue est survenue :\n\n" + e.Exception.Message + "\n\nL'application reste ouverte. Une copie de secours de tes modifications a été faite : enregistre ton travail (Ctrl+S) puis relance l'application si le problème persiste.\nLe détail est dans le fichier erreurs.log.", "Gestion GYM", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log(e.ExceptionObject as Exception);
                try { if (MainWindow.Current != null) MainWindow.Current.EmergencySave(); } catch { }
            };
            return app.Run(new MainWindow());
        }

        static void Log(Exception ex)
        {
            try
            {
                string path = Path.Combine(AppPaths.Root ?? AppDomain.CurrentDomain.BaseDirectory, "erreurs.log");
                File.AppendAllText(path, DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + "\r\n" + (ex != null ? ex.ToString() : "(exception inconnue)") + "\r\n\r\n");
            }
            catch { }
        }
    }
}
