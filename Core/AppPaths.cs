using System;
using System.IO;

namespace GestionGym
{
    /// <summary>Arborescence de travail, créée à côté de l'exécutable (ou dans %LOCALAPPDATA% si le dossier est protégé).</summary>
    public static class AppPaths
    {
        public static string Root { get; private set; }
        public static string Actuel { get; private set; }
        public static string Archive { get; private set; }
        public static string ExportExcel { get; private set; }
        public static string ExportOpenOffice { get; private set; }
        public static string ExportPdf { get; private set; }
        public static string Notice { get; private set; }

        static readonly string[] Names = { "Actuel", "Archive", "Export Excel", "Export OpenOffice", "Export PDF" };

        public static void Init()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string root = exeDir;
            if (!TryPrepare(root))
            {
                string alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestionGym");
                if (!TryPrepare(alt))
                    throw new IOException("Impossible de créer les dossiers de travail, ni dans " + exeDir + " ni dans " + alt + ".\nDéplace l'application dans un dossier où tu as le droit d'écrire (par exemple Documents).");
                Notice = "Le dossier de l'application (" + exeDir + ") n'est pas accessible en écriture.\nLes données sont enregistrées dans : " + alt;
                root = alt;
            }
            Root = root;
            Actuel = Path.Combine(root, "Actuel");
            Archive = Path.Combine(root, "Archive");
            ExportExcel = Path.Combine(root, "Export Excel");
            ExportOpenOffice = Path.Combine(root, "Export OpenOffice");
            ExportPdf = Path.Combine(root, "Export PDF");
        }

        static bool TryPrepare(string root)
        {
            try
            {
                foreach (string n in Names) Directory.CreateDirectory(Path.Combine(root, n));
                string test = Path.Combine(root, "Actuel", ".ecriture.test");
                File.WriteAllText(test, "ok");
                File.Delete(test);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Nom de fichier sûr (sans caractères interdits par Windows).</summary>
        public static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }
    }
}
